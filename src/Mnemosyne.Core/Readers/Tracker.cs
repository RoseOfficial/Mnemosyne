using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Readers;

/// <summary>One reader the Tracker has switched off for the session, and why. Keyed by reader rather than by category so a
/// throw in one of a category's readers cannot silence the others.</summary>
public sealed record DisabledReader(string Name, Category Category, string Reason);

/// <summary>Owns the character records, drives the readers, and is the only thing that changes a record. Login loads or creates the record
/// and runs every Login reader; Tick runs the Interval readers and any trigger marked dirty, and saves only when a reader changed a value;
/// LastSeen is refreshed in memory each tick and saved with the next save, the logout or the unload. A reader that throws is switched off for
/// the session. Version moves on every change, so a window cache assembles a checklist again only when something moved. MarkDirty may be
/// called from any thread; every other member is called from the framework thread. Each reader is told which of its own triggers fired.
/// Besides the readers, a record changes only through Observe (an observed-only item seen to happen), Import (a file the player picked), the
/// window's edits, and TickTask, UntickTask and CountTask (the player's own ticks and counts of a My task, on any record). A read that
/// changes the logged-in record also sets today's gil history point, and every record gets a first point at load and at import. Routine
/// events go to <paramref name="log"/>; faults (a reader switched off, a record not saved) go to <paramref name="fault"/> when one is given,
/// else to the log as well.</summary>
public sealed class Tracker(IClock clock, RecordStore store, IReadOnlyList<IProgressReader> readers, string gameVersion, Action<string> log,
    Action<string>? fault = null)
{
    private readonly List<CharacterRecord> _all = store.LoadAll().Select(Seeded).OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
    private readonly Dictionary<IProgressReader, DisabledReader> _disabled = new();
    private readonly Dictionary<ulong, int> _versions = new();
    private readonly HashSet<ulong> _unsaved = [];
    private readonly HashSet<ulong> _reported = [];
    // Held as an int so the mark is atomic: the level and job events reach MarkDirty from a packet handler whose thread is
    // not verified to be the framework thread, and a lost mark would hide a level-up until the next tick.
    private int _dirty;

    public CharacterRecord? Current { get; private set; }
    public IReadOnlyList<CharacterRecord> All => _all;
    public IReadOnlyCollection<DisabledReader> Disabled => _disabled.Values;

    /// <summary>Why a category is shown as unavailable, naming every one of its readers that threw, or null while they all work.</summary>
    public string? DisabledReason(Category category)
    {
        var hits = _disabled.Values.Where(d => d.Category == category).ToList();
        return hits.Count == 0 ? null : string.Join("; ", hits.Select(d => $"{d.Name}: {d.Reason}"));
    }

    /// <summary>The version of one record alone, so a checklist cache re-assembles only the character that moved.</summary>
    public int VersionOf(ulong contentId) => _versions.GetValueOrDefault(contentId);
    public RecordStore Store { get; } = store;
    public int Version { get; private set; }

    /// <summary>The login capture. A character seen for the first time gets <paramref name="roleForNew"/> as its role (the default role);
    /// a known character keeps its own.</summary>
    public void Login(ICharacterIdentity identity, string? roleForNew = null)
    {
        if (!identity.IsLoaded) { log("login seen but the player state is not loaded yet"); return; }
        if (Store.IsReadOnly(identity.ContentId))
        {
            log($"character {identity.ContentId}: its record is from a newer build, could not be read, or the folder could not be listed; not tracked this session");
            return;
        }
        var now = clock.UtcNow;
        var record = _all.FirstOrDefault(r => r.ContentId == identity.ContentId);
        if (record == null)
        {
            record = new CharacterRecord { ContentId = identity.ContentId, FirstSeen = now, Role = roleForNew };
            _all.Add(record);
            log($"new character record {identity.ContentId}");
        }
        record.Name = identity.Name;
        record.HomeWorld = identity.HomeWorld;
        record.DataCenter = identity.DataCenter;
        record.LastSeen = now;
        SortAll();
        Current = record;
        Run(ReaderTrigger.Login, now);
        Bump(record);
        SavePending(record);
    }

    /// <summary>Marks every reader with this trigger to run on the next Tick. Safe to call from any thread.</summary>
    public void MarkDirty(ReaderTrigger trigger) => Interlocked.Or(ref _dirty, (int)trigger);

    /// <summary>Runs every reader that names this trigger now, on the caller's frame, and leaves the save to the next Tick as
    /// every merge does. For a window's Post event: an edge-gated row is readable only while its window is open, and
    /// MarkDirty would read it up to one interval later, after the window may have closed. The per-reader disable
    /// applies exactly as on a Tick, because this is the same Run.</summary>
    public void Fire(ReaderTrigger trigger)
    {
        if (Current is not { } current) return;
        if (Run(trigger, clock.UtcNow)) Bump(current);
    }

    /// <summary>One live interval elapsed. Runs Interval readers plus any dirty trigger, then writes every record that is
    /// waiting to be saved — including one edited while another character was logged in, whose write failed.</summary>
    public void Tick()
    {
        if (Current is { } current)
        {
            var now = clock.UtcNow;
            current.LastSeen = now;
            var triggers = ReaderTrigger.Interval | (ReaderTrigger)Interlocked.Exchange(ref _dirty, 0);
            if (Run(triggers, now)) Bump(current);
        }
        else Interlocked.Exchange(ref _dirty, 0);
        SavePending();
    }

    public void Logout()
    {
        if (Current is not { } current) return;
        SavePending(current);
        Current = null;
        Interlocked.Exchange(ref _dirty, 0);
        Bump(current);
    }

    public void Forget(ulong contentId)
    {
        _all.RemoveAll(r => r.ContentId == contentId);
        if (Current?.ContentId == contentId) Current = null;
        _unsaved.Remove(contentId);
        _reported.Remove(contentId);
        // Kept and bumped, not reset: a forgotten id that is logged into again must not reuse a version a cache could
        // already have seen for the record's earlier state.
        _versions[contentId] = _versions.GetValueOrDefault(contentId) + 1;
        Store.Delete(contentId);
        Version++;
    }

    /// <summary>Applies an import's adds and replaces. The Tracker owns the list, the per-record versions the checklist cache keys on and
    /// the pending saves, so an import comes through here rather than writing files behind its back, which the next save would overwrite.
    /// The logged-in character and a record whose file could not be read or is from a newer build are refused here too, whatever was
    /// asked: a character switch can fall between the request and the click. Each record is saved through RecordStore.Save. Returns how
    /// many records were added or replaced. A record replaced keeps the file's gil history whole; one with none is seeded before its
    /// save, as a record is at load.</summary>
    public int Import(IEnumerable<CharacterRecord> records)
    {
        var applied = 0;
        foreach (var record in records)
        {
            if (record.ContentId == Current?.ContentId || Store.IsReadOnly(record.ContentId)) continue;
            GilPoints.Seed(record);
            _all.RemoveAll(r => r.ContentId == record.ContentId);
            _all.Add(record);
            _unsaved.Add(record.ContentId);
            Bump(record);
            applied++;
        }
        SortAll();
        SavePending();
        return applied;
    }

    public void Rename(ulong contentId, string? displayName) => Edit(contentId, r => r.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim());

    public void Hide(ulong contentId, bool hidden) => Edit(contentId, r => r.Hidden = hidden);

    public void Ignore(ulong contentId, string key, bool on) => Edit(contentId, r => { if (on) r.Ignored.Add(key); else r.Ignored.Remove(key); });

    /// <summary>The character's role, a role id or null for none. Moves that record's version alone, so only its
    /// checklist is assembled again, and saves it, as every window edit does.</summary>
    public void SetRole(ulong contentId, string? roleId) => Edit(contentId, r => r.Role = roleId);

    /// <summary>The same role for each named character, as SetRole does one by one, so each moves only its own version
    /// and is saved. A content id the Tracker does not have is skipped.</summary>
    public void SetRoles(IEnumerable<ulong> contentIds, string? roleId)
    {
        foreach (var contentId in contentIds) SetRole(contentId, roleId);
    }

    /// <summary>A tick by hand, for any character, logged in or not. Stores what the task's kind
    /// stores at the Tracker's own now (MyTask.TickValue), moves that record's version alone and saves it, as every window edit does. A task that
    /// is not valid stores nothing.</summary>
    public void TickTask(ulong contentId, MyTask task)
    {
        if (task.TickValue(clock.UtcNow) is not { } value) return;
        Edit(contentId, r => r.Values[task.Id] = value);
    }

    /// <summary>The click on a ticked box. The value goes, so the task reads not done again, or "ready" for an "every" task.
    /// Moves that record's version alone and saves it.</summary>
    public void UntickTask(ulong contentId, string taskId) => Edit(contentId, r => r.Values.Remove(taskId));

    /// <summary>A click on a counted task's box, for any character, logged in or not: one more (step 1) or one fewer (step -1) than the
    /// count the character holds at the Tracker's own now (MyTask.CountValue). Moves that record's version alone and saves it, as a tick does.
    /// UntickTask is its "Reset to 0". A task that is not counted, and a record the Tracker does not have, change nothing.</summary>
    public void CountTask(ulong contentId, MyTask task, int step)
    {
        if (_all.FirstOrDefault(r => r.ContentId == contentId) is not { } record) return;
        record.Values.TryGetValue(task.Id, out var stored);
        if (task.CountValue(stored, step, clock.UtcNow) is not { } value) return;
        Edit(contentId, r => r.Values[task.Id] = value);
    }

    /// <summary>Records that a reminder was said for this occasion, so the next tick, the next relog and the next settings
    /// edit all stay quiet about it.</summary>
    public void Remind(string key, string occasion)
    {
        if (Current is not { } current) return;
        if (current.Reminded.TryGetValue(key, out var said) && said == occasion) return;
        current.Reminded[key] = occasion;
        _unsaved.Add(current.ContentId);
        Bump(current);
    }

    /// <summary>An ObservedOnly item was seen to happen for the current character: the one path from a lifecycle
    /// handler into a record. The observation is applied to the stored count, the schedule resolved against the record's own
    /// data centre first, and the record is bumped and saved on the next tick like any merge. Nothing happens with no
    /// character, and nothing is written when the count did not move, so a repeated event is free.</summary>
    public void Observe(string itemId, Observation observation)
    {
        if (Current is not { } current) return;
        current.Values.TryGetValue(itemId, out var stored);
        var schedule = ResetSchedules.Resolve(observation.Schedule, current.DataCenter);
        var value = Observations.Apply(stored, observation, schedule, clock.UtcNow);
        // No change is no save: the same count in the same period. A repeated count across a boundary is a new period's first
        // observation and must be stored with its own instant, or the assembler would zero it against the old one.
        if (stored is CounterValue before && before.Current == value.Current && before.Cap == value.Cap && Observations.Continues(before, schedule, value.ReadAt)) return;
        current.Values[itemId] = value;
        _unsaved.Add(current.ContentId);
        Bump(current);
    }

    private void Edit(ulong contentId, Action<CharacterRecord> change)
    {
        var record = _all.FirstOrDefault(r => r.ContentId == contentId);
        if (record == null) return;
        change(record);
        SortAll();
        SavePending(record);
        Bump(record);
    }

    private bool Run(ReaderTrigger triggers, DateTimeOffset now)
    {
        if (Current is not { } current) return false;
        var changed = false;
        foreach (var reader in readers)
        {
            if ((reader.Triggers & triggers) == 0 || _disabled.ContainsKey(reader)) continue;
            try
            {
                // A null return is "I did not read": the player state was not loaded, so the category's stamp stays where
                // it was and the window keeps saying how old the last real read is.
                if (reader.Read(reader.Triggers & triggers) is not { } readings) continue;
                if (RecordUpdater.Merge(current, reader.Category, readings, now)) { changed = true; _unsaved.Add(current.ContentId); }
            }
            catch (Exception ex)
            {
                var reason = $"unavailable since game version {gameVersion}: {ex.GetType().Name}: {ex.Message}";
                _disabled[reader] = new DisabledReader(reader.Name, reader.Category, reason);
                Fault($"{reader.Name} ({reader.Category}) disabled for this session on game version {gameVersion}: {ex}");
            }
        }
        // A read that changed the record sets today's point in the same change, so the point is
        // saved with it. Every reader's merge passes here, at login, on the tick and on a window's Post event; a read that changed nothing writes
        // no point, and that day carries the last one.
        if (changed) GilPoints.Note(current, now);
        return changed;
    }

    /// <summary>A record loaded with own gil and no history gets its first point in
    /// memory. Nothing is written at load: the seed is a pure function of the stored gil and last-seen instant, so a load before the record's
    /// next save derives the same point again, and the record's next save writes it.</summary>
    private static CharacterRecord Seeded(CharacterRecord record)
    {
        GilPoints.Seed(record);
        return record;
    }

    /// <summary>Writes every record waiting to be saved. A write that fails keeps its record on the list, so the change is
    /// retried on the next tick instead of living only in memory until the session ends. A failure is
    /// reported and logged once per id; a permanent failure then retries silently on every later tick instead of flooding
    /// the problems list and the log once per live interval, and a subsequent successful save clears the mark so a later
    /// failure on the same id is reported again.</summary>
    private void SavePending(CharacterRecord? first = null)
    {
        if (first != null) _unsaved.Add(first.ContentId);
        if (_unsaved.Count == 0) return;
        foreach (var contentId in _unsaved.ToList())
        {
            var record = _all.FirstOrDefault(r => r.ContentId == contentId);
            if (record == null) { _unsaved.Remove(contentId); _reported.Remove(contentId); continue; }
            try
            {
                Store.Save(record);
                _unsaved.Remove(contentId);
                _reported.Remove(contentId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (!_reported.Add(contentId)) continue;
                Fault(Store.Report($"{Store.PathFor(contentId)}: not saved ({ex.Message}); the change is kept in memory and retried"));
            }
        }
    }

    /// <summary>A fault goes to its own line when the caller gave one. A method rather than a field, since a field initialiser that read
    /// <c>log</c> would raise CS9124, which TreatWarningsAsErrors makes an error.</summary>
    private void Fault(string message) => (fault ?? log)(message);

    private void Bump(CharacterRecord record)
    {
        _versions[record.ContentId] = _versions.GetValueOrDefault(record.ContentId) + 1;
        Version++;
    }

    private void SortAll() => _all.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
}
