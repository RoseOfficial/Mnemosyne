using System.Globalization;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>A pure function from catalog, record, settings and the clock to the checklist.
/// Nothing here reads a clock or a file; the caller passes now.
/// Today holds not-done Daily items, Accrual Counters at cap, and Accrual Timers whose deadline passed.
/// This week holds not-done Weekly items.
/// Ending soon holds Timers of any reset kind due within the window (overdue included, or within a Timer's own warning window,
/// when it carries one) and open Window items closing within it, the Fashion Report's recurring window among them.
/// Running events holds the open windows that are not closing within it, in
/// end order, each reading "ends in N d".
/// A row flagged HiddenWhenBlank is left out of its section while blank. An item off for the character's role is
/// listed, carrying the role's name, and placed in no top group. A My task is left out
/// for a character it is not for, reads not done with nothing stored, and, as a one-off not done or an "every" task that is ready, is in Today.</summary>
public static class Assembler
{
    public static Checklist Assemble(Catalog catalog, CharacterRecord record, Settings settings, DateTimeOffset now)
    {
        var today = new List<Entry>();
        var week = new List<Entry>();
        var soon = new List<Entry>();
        var running = new List<Entry>();
        var sections = new List<Section>();
        var window = TimeSpan.FromDays(settings.EndingSoonDays);
        // The character's role, resolved here from the settings by the id its record stores. No
        // role, or an id this PC does not have, is null, and everything is tracked as before.
        var role = settings.RoleOf(record);

        foreach (var category in Enum.GetValues<Category>())
        {
            if (!settings.IsCategoryOn(category)) continue;
            var groups = new List<EntryGroup>();
            foreach (var group in catalog.InCategory(category).GroupBy(i => i.Group))
            {
                var entries = new List<Entry>();
                foreach (var item in group)
                {
                    // A character a task is not for does not have the item at all. Left out before
                    // evaluation, as an ignored item is, so it is in no section, no top group and no count.
                    if (IsIgnored(item, record, settings) || !IsFor(item, record, settings)) continue;
                    var entry = Evaluate(item, record, now);
                    // A row listed only while it says something is left out while blank. A blank row is never
                    // attention either, so skipping Place changes no top group (Place already returns for a lock and for an AtLogin row
                    // with nothing stored).
                    if (item.HiddenWhenBlank && IsBlank(entry, record)) continue;
                    // An item off for the character's role is evaluated and listed like any other, carrying the
                    // role's name, and never placed. So it is in no top group and no badge, and so on no All tasks line, in no matrix count
                    // built from the checklist, in no login line and in no reminder. The ignore and the blank row above win over it, and a
                    // category switched off for everyone never reaches here: a role never lists what another rule left out.
                    if (role != null && role.IsOff(item))
                    {
                        entries.Add(entry with { OffFor = role.Name });
                        continue;
                    }
                    entries.Add(entry);
                    Place(entry, record, now, window, today, week, soon, running);
                }
                if (entries.Count > 0) groups.Add(new EntryGroup(group.Key, entries));
            }
            // A My tasks section with no row is left out, so with no task, or none this character
            // has, it is neither drawn nor printed.
            if (category == Category.MyTasks && groups.Count == 0) continue;
            sections.Add(new Section(category, groups));
        }

        // Running is deliberately not in this sum: a running event with weeks left is information, not attention,
        // and counting it would keep the badge lit for a fortnight at a time.
        var badge = today.Count + (settings.WeekliesCount ? week.Count : 0) + soon.Count;
        var stale = IsStale(record, settings, now);
        // Running events read as countdowns, so they are listed in end order, soonest first. OrderBy is stable,
        // so two events ending at the same instant keep their catalog order. The window is resolved per record, because
        // the Fashion Report's recurs and has no fixed dates.
        var inEndOrder = running.OrderBy(e => WindowOf(e.Item, record, now)?.End ?? DateTimeOffset.MaxValue).ToList();
        return new Checklist(today, week, soon, inEndOrder, sections, badge, stale);
    }

    public static bool IsIgnored(ItemDefinition item, CharacterRecord record, Settings settings) =>
        record.Ignored.Contains(item.Id) || record.Ignored.Contains(item.GroupKey)
        || settings.IgnoredEverywhere.Contains(item.Id) || settings.IgnoredEverywhere.Contains(item.GroupKey);

    /// <summary>Every item is for every character but a My task, which is for everyone, one role or picked
    /// characters. A Manual item whose task the settings no longer hold is for nobody.</summary>
    public static bool IsFor(ItemDefinition item, CharacterRecord record, Settings settings) =>
        item.Freshness.Kind != FreshnessKind.Manual || settings.MyTasks.Find(item.Id)?.IsFor(record, settings) == true;

    /// <summary>A record not seen for longer than the staleness threshold, which the sidebar and the table dim. The checklist carries
    /// it, and the Totals page reads the same rule for each holder.</summary>
    public static bool IsStale(CharacterRecord record, Settings settings, DateTimeOffset now) =>
        now - record.LastSeen > TimeSpan.FromDays(settings.StalenessDays);

    /// <summary>Blank: a stored lock, or nothing stored on an AtLogin row whose category has been read. An unread category ("not read yet")
    /// and a gated row with nothing stored ("needs: &lt;window&gt;") say something and are not blank.</summary>
    private static bool IsBlank(Entry entry, CharacterRecord record) =>
        entry.Value is GateValue { Locked: true }
        || (entry.Value == null && entry.Item.Freshness.Kind == FreshnessKind.AtLogin && record.CategoryReadAt.ContainsKey(entry.Item.Category.ToString()));

    /// <summary>One item's state for one character. The per-character name, when the record has one, is applied here so
    /// every verdict below can be written without carrying it.</summary>
    public static Entry Evaluate(ItemDefinition item, CharacterRecord record, DateTimeOffset now)
    {
        var entry = Verdict(item, record, now);
        return record.ItemNames.TryGetValue(item.Id, out var label) && label.Length > 0 ? entry with { Label = label } : entry;
    }

    private static Entry Verdict(ItemDefinition item, CharacterRecord record, DateTimeOffset now)
    {
        record.Values.TryGetValue(item.Id, out var value);
        // The gate's answer for a row whose empty state is not blank, an observed or a window row: a
        // stored lock reads blank, sits in no top group and is still listed, exactly as a cleared AtLogin row does; an open
        // gate is never stored, and one that is read anyway means nothing is stored.
        if (value is GateValue gate)
        {
            if (gate.Locked) return new Entry(item, value, EntryState.NotDone, "", false, "");
            value = null;
        }
        // A record written under an earlier catalog can hold a value of the wrong shape for an item whose kind has
        // changed since (an Events occurrence stored as a Done flag, now a reward Counter). Such a value says nothing
        // about this item: it is dropped here and the item is treated as never read, so a gated one still reads
        // "needs: <gate>" and a windowed one still runs its Upcoming and Past tests below.
        if (value != null && !Matches(item.Kind, value)) value = null;
        // A change of kind starts the task fresh. A daily or weekly tick carries its period key,
        // which a one-off never stores, so under a one-off it reads as nothing stored; the test above already drops a Done value under an
        // "every" task and a Timer value under the rest.
        if (item.Freshness.Kind == FreshnessKind.Manual && item.Reset.Kind == ResetKind.None && value is DoneValue { PeriodKey: not null }) value = null;
        var marker = item.Freshness.Kind switch
        {
            FreshnessKind.ObservedOnly => "as seen",
            FreshnessKind.NeedsWindow => "as of last visit to " + item.Freshness.Gate,
            FreshnessKind.NeedsPlace => "as of last visit to " + item.Freshness.Gate,
            _ => "",
        };
        // A gated item with nothing stored needs its visit first; the window says nothing until then. Only
        // the two gated kinds return here, because only their Missing verdict is Unknown and that must win over a closed
        // window. An ObservedOnly item keeps the ordinary path, where the window's Upcoming and Past tests come first.
        if (value == null && item.Freshness.Kind is FreshnessKind.NeedsWindow or FreshnessKind.NeedsPlace) return Missing(item, record);

        // The reset rule is applied once, here, and the window block and the value arms below all read the same answer,
        // so an item that is both windowed and periodic cannot read done in this window on last period's value. Until the Fashion Report every
        // Resets item had no window and every Events counter had no period, which is why the old raw read never showed.
        var schedule = ScheduleFor(item, record);
        var corrected = Correct(item, value, schedule, now);
        var run = WindowOf(item, record, now);
        if (run is { } window)
        {
            // A run the sheets carry before it is announced has no date yet, so it can be completed while its window still
            // reads as future. Done wins over both ends of the window, exactly as it does for a window that has closed.
            // A reward counter at its cap is done for the window's purposes too: it is never upcoming or missed.
            var doneInWindow = corrected.Done;
            // A return that reuses its earlier run's quest rows reads done only when the plugin saw that quest flip to
            // complete inside this return's own window. A completion stamped earlier, or carrying no stamp at all
            // because it was already complete when the plugin first read it, says only "has ever done this": the item runs
            // with a marker while the window is open and is done with that marker afterwards, but is never missed.
            // The marker names what is actually known. An unstamped completion may well be this return's own, done before
            // Mnemosyne was installed, so it must not claim the quests were done in an earlier return.
            if (item.ReusedQuest && value is DoneValue { Done: true } reused
                && !(reused.At is { } at && at >= window.Start && at < window.End))
            {
                if (now >= window.End) return new Entry(item, value, EntryState.Done, "done", false, "quests done");
                if (now >= window.Start) return new Entry(item, value, EntryState.NotDone, "ends " + Relative.Until(window.End, now), false, "quests done; which return is unknown");
                doneInWindow = false;   // still upcoming, exactly as a not-done occurrence is
            }
            if (now < window.Start && !doneInWindow) return new Entry(item, value, EntryState.Upcoming, "upcoming", false, marker);
            if (now >= window.End && !doneInWindow)
            {
                // A partial count is the one thing a reward counter exists to show, and most reward events return, so the
                // count stays on the row after the run closes. A held counter was never something to finish, and what it
                // counts may still be spent (the trove's tomestones until the next patch), so it reads "ended".
                var missed = value is CounterValue partial
                    ? $"{partial.Current} of {partial.Cap}, {(item.DoneAtCap ? "missed" : "ended")}"
                    : "missed";
                return new Entry(item, value, EntryState.Past, missed, false, marker, Count: value is CounterValue counted ? new Meter(counted.Current, counted.Cap) : null);
            }
        }
        if (value == null) return Missing(item, record);

        switch (value)
        {
            case DoneValue:
            {
                var display = corrected.Done ? "done" : run is { } open && now >= open.Start ? "ends " + Relative.Until(open.End, now) : "";
                return new Entry(item, value, corrected.Done ? EntryState.Done : EntryState.NotDone, display, false, marker);
            }
            case CounterValue c:
            {
                // A counted My task reads against the item's number, not the cap stored with its last click.
                var cap = item.Target ?? c.Cap;
                var atCap = corrected.Current >= cap;
                var mark = corrected.Assumed ? (marker.Length == 0 ? "assumed" : marker + ", assumed") : marker;
                var display = $"{corrected.Current} of {cap}";
                if (!corrected.Done && run is { } open && now >= open.Start) display += ", ends " + Relative.Until(open.End, now);
                return new Entry(item, value, corrected.Done ? EntryState.Done : EntryState.NotDone, display, corrected.Assumed, mark, atCap, Count: new Meter(corrected.Current, cap));
            }
            case LevelValue l:
            {
                // The step in progress, when the reader has it. At the cap there is no next step, so no second line.
                var detail = l is { Progress: { } progress, ProgressMax: { } needed } && needed > 0 && l.Current < l.Max
                    ? $"{progress.ToString("N0", CultureInfo.InvariantCulture)} / {needed.ToString("N0", CultureInfo.InvariantCulture)} to the next"
                    : "";
                return new Entry(item, value, l.Current >= l.Max ? EntryState.Done : EntryState.NotDone, $"{l.Current} / {l.Max}", false, marker, Detail: detail);
            }
            case TimerValue t when item.Freshness.Kind == FreshnessKind.Manual:
                // An "every" task's cooldown. Until its deadline the task is done, reading "ready in 14 h"; from it
                // on it reads "ready" and is not done, which Place puts in Today.
                return t.Deadline > now
                    ? new Entry(item, value, EntryState.Done, "ready " + Relative.Until(t.Deadline, now), false, marker)
                    : new Entry(item, value, EntryState.NotDone, "ready", false, marker);
            case TimerValue t:
                // A passed deadline is "ready" for an item that says so, the treasure map: the client's next-map instant on
                // a character with no cooldown is the login instant, and "overdue by 3 h" would count the time since login
                // as lateness. Every other timer keeps "overdue by", which is what a journal
                // expiry or a demolition timer means.
                return new Entry(item, value, EntryState.NotDone, item.ReadyWhenPassed && t.Deadline <= now ? "ready" : Relative.Until(t.Deadline, now), false, marker);
            default:
                throw new InvalidOperationException($"unknown value type {value.GetType().Name} for '{item.Id}'");
        }
    }

    /// <summary>A stored value's verdict once its reset rule has been applied: a periodic Done is done only in the current
    /// period; a periodic Counter zeroes across its boundary unless it carries over, and an Accrual counter counts
    /// its grants forward from the client's anchor or the schedule. One computation for the window block and the
    /// value arms, so no item can read done in a window on a count the arm below would correct.
    /// A null schedule is a period that cannot be keyed for this record: nothing is corrected and nothing is done.</summary>
    private readonly record struct Corrected(bool Done, int Current, bool Assumed);

    private static Corrected Correct(ItemDefinition item, ItemValue? value, string? schedule, DateTimeOffset now)
    {
        switch (value)
        {
            case DoneValue d:
            {
                var periodic = item.Reset.Kind is ResetKind.Daily or ResetKind.Weekly;
                var done = periodic ? d.Done && schedule != null && d.PeriodKey == ResetSchedules.PeriodKey(schedule, now) : d.Done;
                return new Corrected(done, 0, false);
            }
            case CounterValue c:
            {
                var current = c.Current;
                var assumed = false;
                if (item.Reset.Kind is ResetKind.Daily or ResetKind.Weekly)
                {
                    // A carry-over counter keeps the count it was last read with: a Wondrous Tails journal is valid for two
                    // weeks, so the Tuesday 08:00 that offers a new one takes nothing off the one already held.
                    // The rule still runs for placement, so the row is still a thing to do this week.
                    if (!item.CarriesOver && schedule != null && ResetSchedules.BoundaryBetween(schedule, c.ReadAt, now))
                    {
                        // An observed count that zeroes at its boundary is a fact, not a guess: nothing has been seen this
                        // period. So is a count by hand: the task starts over at its own reset. Every other counter's
                        // refill is assumed until it is read again.
                        current = 0;
                        assumed = item.Freshness.Kind is not (FreshnessKind.ObservedOnly or FreshnessKind.Manual);
                    }
                }
                else if (item.Reset.Kind == ResetKind.Accrual && item.Reset.AccrualAmount > 0 && schedule != null)
                {
                    // The client's own next-grant instant is the authority when it gave one; the schedule is the fallback.
                    // After that first boundary the schedule takes over, because the client only ever
                    // names the next one.
                    var grants = c.NextGrant is { } anchor
                        ? now < anchor ? 0 : 1 + ResetSchedules.GrantsBetween(schedule, anchor, now)
                        : ResetSchedules.GrantsBetween(schedule, c.ReadAt, now);
                    if (grants > 0) { current = Math.Min(c.Cap, current + grants * item.Reset.AccrualAmount); assumed = true; }
                }
                // A counted My task's number is the item's, so an edit reaches every character at once; the count never reads above it,
                // so a number lowered below the clicks reads done at the number.
                if (item.Target is { } target) current = Math.Clamp(current, 0, target);
                var done = item.DoneAtCap && item.Reset.Kind != ResetKind.Accrual && current >= (item.Target ?? c.Cap);
                return new Corrected(done, current, assumed);
            }
            default:
                return new Corrected(false, 0, false);
        }
    }

    /// <summary>The schedule an item's periods are keyed on, for this record. The Jumbo Cactpot's draw falls at a different
    /// instant per data centre, so its item names ResetSchedules.Jumbo and the record's data centre picks the draw; a data
    /// centre the table does not know has no schedule at all.
    /// Internal: the All tasks board resolves a line's reset and deadline with it.</summary>
    internal static string? ScheduleFor(ItemDefinition item, CharacterRecord record) =>
        item.Reset.Schedule.Length == 0 ? null : ResetSchedules.Resolve(item.Reset.Schedule, record.DataCenter);

    /// <summary>The window an item is doable in right now: its fixed dates, or the current occurrence of its recurring window
    /// on this record's schedule. Null for an item with neither, or a recurrence whose schedule cannot be keyed.
    /// Internal: the All tasks board resolves a line's reset and deadline with it.</summary>
    internal static Availability? WindowOf(ItemDefinition item, CharacterRecord record, DateTimeOffset now) =>
        item.Availability ?? (item.Recurrence is { } recurs && ScheduleFor(item, record) is { } schedule ? recurs.WindowFor(schedule, now) : null);

    /// <summary>Whether a stored value has the shape the item's kind calls for.</summary>
    private static bool Matches(ItemKind kind, ItemValue value) => kind switch
    {
        ItemKind.Done => value is DoneValue,
        ItemKind.Counter => value is CounterValue,
        ItemKind.Level => value is LevelValue,
        ItemKind.Timer => value is TimerValue,
        _ => false,
    };

    private static Entry Missing(ItemDefinition item, CharacterRecord record)
    {
        switch (item.Freshness.Kind)
        {
            case FreshnessKind.ObservedOnly: return new Entry(item, null, EntryState.NotDone, "not seen", false, "as seen");
            case FreshnessKind.NeedsWindow: return new Entry(item, null, EntryState.Unknown, "unknown", false, "needs: " + item.Freshness.Gate);
            case FreshnessKind.NeedsPlace: return new Entry(item, null, EntryState.Unknown, "unknown", false, "visit: " + item.Freshness.Gate);
            // No reader will ever fill a Manual item, so nothing stored is never "not read yet" or
            // "unknown". It is not done, or "ready" for an "every" task, or "0 of 5" for a counted one.
            case FreshnessKind.Manual:
                return new Entry(item, null, EntryState.NotDone, item.Kind == ItemKind.Timer ? "ready" : item.Target is { } target ? $"0 of {target}" : "", false, "",
                    Count: item.Target is { } counted ? new Meter(0, counted) : null);
            default:
                return record.CategoryReadAt.ContainsKey(item.Category.ToString())
                    ? new Entry(item, null, EntryState.NotDone, "", false, "")
                    : new Entry(item, null, EntryState.Unknown, "unknown", false, "not read yet");
        }
    }

    /// <summary>An entry lands in at most one top group.</summary>
    private static void Place(Entry e, CharacterRecord record, DateTimeOffset now, TimeSpan window, List<Entry> today, List<Entry> week, List<Entry> soon, List<Entry> running)
    {
        // A locked gate on an observed row is blank and is attention nowhere. An AtLogin item no reader has
        // reported has nothing stored, so it is not a thing to do — it is a thing not known. The Resets reader
        // stays silent about anything this character cannot do, and this is what keeps a hunt board the character has never
        // unlocked out of Today for ever. ObservedOnly with nothing stored is exempt: no
        // reader can ever report it, so its absence is the reading and the row belongs in Today with "as seen".
        if (e.Value is GateValue) return;
        if (e.Value == null && e.Item.Freshness.Kind == FreshnessKind.AtLogin) return;
        // A period that cannot be keyed for this record — the Jumbo Cactpot on a data centre whose draw is not listed — is
        // information in its section and never attention.
        if (e.Item.Reset.Kind is ResetKind.Daily or ResetKind.Weekly && e.Item.Kind != ItemKind.Timer && ScheduleFor(e.Item, record) == null) return;
        // A running window that still needs its visit shows at the top with its marker, so the visit gets made: a seasonal
        // reward set and the Fashion Report alike.
        if (e.State == EntryState.Unknown && WindowOf(e.Item, record, now) is { } gatedWindow && now >= gatedWindow.Start && now < gatedWindow.End)
        {
            running.Add(e);
            return;
        }
        if (e.State is EntryState.Upcoming or EntryState.Past or EntryState.Unknown) return;
        var item = e.Item;
        switch (item.Reset.Kind)
        {
            case ResetKind.Daily:
            case ResetKind.Weekly:
                if (item.Kind == ItemKind.Timer) PlaceTimer(e, now, window, today, soon);
                else if (e.IsDone) break;
                // A periodic item with a window is a running event while the window is open: information, then attention
                // as it closes, never a plain to-do for the week ("and so does Fashion Report, open Friday to Tuesday").
                else if (WindowOf(item, record, now) is { } run) PlaceWindow(e, run, now, window, soon, running);
                else if (item.Reset.Kind == ResetKind.Daily) today.Add(e);
                else week.Add(e);
                break;
            case ResetKind.Accrual:
                if (item.Kind == ItemKind.Counter && e.AtCap) today.Add(e);
                else if (item.Kind == ItemKind.Timer) PlaceTimer(e, now, window, today, soon);
                break;
            case ResetKind.Window:
                if (e.IsDone || item.Availability is not { } a) break;
                PlaceWindow(e, a, now, window, soon, running);
                break;
            case ResetKind.None:
                // A Manual one-off that is not done, and an "every"
                // task that is ready, are attention until ticked. Every other item with no reset stays information however long it is not done,
                // and a cooling-down "every" task, which is done, is never Ending soon.
                if (item.Freshness.Kind == FreshnessKind.Manual)
                {
                    if (!e.IsDone) today.Add(e);
                    break;
                }
                // A Timer's own warning window, when it has one, replaces the ending-soon setting: the house visit enters Ending soon
                // at the game's own 30-day warning, 15 days before demolition.
                if (item.Kind == ItemKind.Timer && e.Value is TimerValue tv && tv.Deadline - now <= (item.RemindWithin ?? window)) soon.Add(e);
                break;
        }
    }

    /// <summary>An open window is information while it has time left (Running events, which no badge counts) and attention as
    /// it closes (Ending soon), for a seasonal event and for the Fashion Report alike. A held counter never leaves
    /// Running events.</summary>
    private static void PlaceWindow(Entry e, Availability run, DateTimeOffset now, TimeSpan window, List<Entry> soon, List<Entry> running)
    {
        // A held counter has nothing to finish, so inside its window it is information for the whole run and never
        // attention: the trove's tomestones on fourteen characters must not light fourteen badges for a month.
        if (e.Item.Kind == ItemKind.Counter && !e.Item.DoneAtCap) { running.Add(e); return; }
        if (run.End - now <= window) soon.Add(e);
        else running.Add(e);
    }

    /// <summary>A Timer is a deadline, not a period. Whatever reset rule its item carries, it is attention only once it is
    /// due (Today) or close (Ending soon); before that it is just a countdown in its section. Without this a Daily or Weekly
    /// Timer sat in Today for its whole run, because a Timer is never "done".</summary>
    private static void PlaceTimer(Entry e, DateTimeOffset now, TimeSpan window, List<Entry> today, List<Entry> soon)
    {
        if (e.Value is not TimerValue t) return;
        if (t.Deadline <= now) today.Add(e);
        else if (t.Deadline - now <= window) soon.Add(e);
    }
}
