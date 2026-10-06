using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>The status tones, one meaning everywhere. Done: done, ready, all clear. Today, EndingSoon,
/// ThisWeek and Running: in that top group. Quiet: unknown, locked, upcoming, past, or off for the role. Neutral: listed, but in no top
/// group. The plugin gives each its colour (Look/Palette); every rule that picks one is in Tones, where it is tested.</summary>
public enum Tone { Neutral, Done, Today, EndingSoon, ThisWeek, Running, Quiet }

/// <summary>A category's or a group's done/total, over the rows
/// that can be finished (Tones.CanBeDone) and are known and available now (Tones.IsAvailable), a row off for the role in neither. A Total of 0
/// means nothing there can be finished right now, and the look draws no bar.</summary>
public readonly record struct Tally(int Done, int Total)
{
    /// <summary>How full its bar is, from 0 to 1; 0 when nothing can be finished right now, where no bar is drawn.</summary>
    public float Fraction => Total == 0 ? 0f : (float)Done / Total;
}

/// <summary>What a row's thin bar shows, Current of Max.</summary>
public readonly record struct Meter(long Current, long Max)
{
    /// <summary>How full the bar is, from 0 to 1 whatever the numbers; 0 with no Max.</summary>
    public float Fraction => Max <= 0 ? 0f : (float)Math.Clamp((double)Current / Max, 0d, 1d);
}

/// <summary>The rules that decide every colour the look draws, the bar a row carries, and the one
/// thing on a page that moves. Pure: every instant is passed in, and nothing here reads a clock or a file. The window reads them through
/// ChecklistLook, from the checklists the cache already holds.</summary>
public static class Tones
{
    /// <summary>How close to its end a row in Ending soon starts to pulse.</summary>
    public static readonly TimeSpan PulseWithin = TimeSpan.FromHours(1);

    private const string VisitPrefix = "visit: ", NeedsPrefix = "needs: ";

    /// <summary>Owning an estate is information, as the current Grand Company is, so the four ownership
    /// rows CatalogBuilder builds under Content's Housing group are never finishable.</summary>
    private static readonly HashSet<string> Ownership = new(StringComparer.Ordinal) { "housing:personal", "housing:fc", "housing:apartment", "housing:chambers" };

    /// <summary>A row's tone, with the row's top group looked up in the checklist it was drawn from.</summary>
    public static Tone Of(Entry entry, Checklist checklist) => ChecklistLook.For(checklist).ToneOf(entry);

    /// <summary>A row's tone, given the top group the checklist placed the row in, or Neutral for none. A row off for the role is
    /// Quiet first: that dims it whatever its state, so it reads neither as work nor as done. Then Done when done;
    /// then Quiet when unknown, locked, upcoming or past, which wins over a top group; then the group's own tone.</summary>
    public static Tone OfRow(Entry entry, Tone placed)
    {
        if (entry.OffFor != null) return Tone.Quiet;
        if (entry.IsDone) return Tone.Done;
        if (entry.Locked || entry.State is EntryState.Unknown or EntryState.Upcoming or EntryState.Past) return Tone.Quiet;
        return placed;
    }

    /// <summary>A character's urgency, the tone of its badge pill: Ending soon if its Ending soon holds anything; otherwise Today;
    /// otherwise This week, when weeklies count toward the badge; otherwise Done. It is Done exactly when the badge is 0.</summary>
    public static Tone Urgency(Checklist checklist, bool weekliesCount) =>
        checklist.EndingSoon.Count > 0 ? Tone.EndingSoon
        : checklist.Today.Count > 0 ? Tone.Today
        : weekliesCount && checklist.ThisWeek.Count > 0 ? Tone.ThisWeek
        : Tone.Done;

    /// <summary>A category's and a group's done/total: the rows that can
    /// be finished (CanBeDone) and are known and available right now (IsAvailable), done over listed; a row off for the role counts in
    /// neither. Nothing that can be finished right now reads 0 / 0, and the look draws no bar.</summary>
    public static Tally TallyOf(IEnumerable<Entry> entries)
    {
        int done = 0, total = 0;
        foreach (var entry in entries)
        {
            if (entry.OffFor != null || !CanBeDone(entry) || !IsAvailable(entry)) continue;
            total++;
            if (entry.IsDone) done++;
        }
        return new Tally(done, total);
    }

    /// <summary>A bar counts only rows you can finish: whether a row can be finished, and so
    /// counts in a category's or a group's bar. A My task can, of any kind, an "every" task included. A Money row never can: holdings are
    /// information, as on the Totals page, so Money has no bar, though a retainer's level is a Level row. Otherwise a Done row can, but the
    /// current Grand Company, which is never done, and an estate owned, which is information; a Level row can; a counter can when it is done at its cap and does not accrue (stickers,
    /// allowances used, reward counters); a held or accruing counter and a timer (a venture, the treasure map, a journal, the house visit)
    /// cannot.</summary>
    public static bool CanBeDone(Entry entry)
    {
        var item = entry.Item;
        if (item.Freshness.Kind == FreshnessKind.Manual) return true;
        if (item.Category == Category.Money) return false;
        return item.Kind switch
        {
            ItemKind.Done => item.Id != CatalogBuilder.CurrentCompanyId && !Ownership.Contains(item.Id),
            ItemKind.Level => true,
            ItemKind.Counter => item.DoneAtCap && item.Reset.Kind != ResetKind.Accrual,
            _ => false,
        };
    }

    /// <summary>A bar counts only what it can do now: whether a row is known and available
    /// right now, so it can count in a bar at all, whether it can be finished (CanBeDone) or not. False for a stored lock (Entry.Locked, "not
    /// unlocked yet"), for a blank row (nothing stored on an AtLogin row, State NotDone: exactly the second half of Assembler.IsBlank — an
    /// unread category reads Unknown, not NotDone, through Assembler.Missing's default case, so no separate "read" check is needed here), and
    /// for a missed, upcoming or not-read-yet row (State Past, Upcoming or Unknown). An ObservedOnly row with nothing stored ("not seen") and a
    /// My task with nothing stored ("0 of 5", "ready") are State NotDone with a Value that is not null-and-AtLogin, so they stay available.</summary>
    public static bool IsAvailable(Entry entry) =>
        entry.State is EntryState.Done or EntryState.NotDone
        && !entry.Locked
        && !(entry.Value is null && entry.Item.Freshness.Kind == FreshnessKind.AtLogin);

    /// <summary>An All tasks line's tone: its group's. Pinned is a place, not a status, and reads Neutral; the look draws its label
    /// gold.</summary>
    public static Tone OfGroup(BoardGroup group) => group switch
    {
        BoardGroup.EndingSoon => Tone.EndingSoon,
        BoardGroup.Today => Tone.Today,
        BoardGroup.ThisWeek => Tone.ThisWeek,
        BoardGroup.Running => Tone.Running,
        _ => Tone.Neutral,
    };

    /// <summary>A line reads its own group's tone, which is never Pinned, so a pinned line keeps it.</summary>
    public static Tone OfLine(TaskLine line) => OfGroup(line.Group);

    /// <summary>A marker that asks for a visit ("visit: ...", "needs: ...") is an amber pill, the call to action;
    /// one that only qualifies the value ("as seen", "assumed", "as of last visit to ...", "not read yet", "quests done") is a quiet pill; so is
    /// any marker on a row off for the role. Neutral when there is no marker, and so no pill.</summary>
    public static Tone OfMarker(Entry entry) =>
        entry.Marker.Length == 0 ? Tone.Neutral
        : entry.OffFor != null ? Tone.Quiet
        : entry.Marker.StartsWith(VisitPrefix, StringComparison.Ordinal) || entry.Marker.StartsWith(NeedsPrefix, StringComparison.Ordinal) ? Tone.Today
        : Tone.Quiet;

    /// <summary>What a "needs: &lt;window&gt;" marker asks of the player, shown as the marker's tooltip: the window is the row's own gate, the name
    /// the marker shows. Null for every other marker, whose words already say what they mean.</summary>
    public static string? HintOf(Entry entry) =>
        entry.Marker.StartsWith(NeedsPrefix, StringComparison.Ordinal) ? $"Open {entry.Item.Freshness.Gate} once on this character to read this row." : null;

    /// <summary>A job's experience, a counted My task, and any count with a cap (stickers, allowances, reward counters) are shown as
    /// a thin bar. A counter reads the count its words read (Entry.Count), unless it is held, never done at its cap (a
    /// currency, a bag, a trove): those are information, and have none. A level reads the experience toward the next level when the reader has
    /// it, else the level of its maximum, and is full at the maximum. Every other row has none.</summary>
    public static Meter? MeterOf(Entry entry)
    {
        if (entry.Count is { } count) return entry.Item.DoneAtCap ? count : null;
        if (entry.Value is not LevelValue level) return null;
        if (level.Current >= level.Max) return new Meter(level.Max, level.Max);
        return level is { Progress: { } progress, ProgressMax: { } needed } && needed > 0 ? new Meter(progress, needed) : new Meter(level.Current, level.Max);
    }

    /// <summary>A row pulses while it is in Ending soon (its tone) and its end is less than PulseWithin away: a timer's
    /// deadline, or its window's end.</summary>
    public static bool Pulses(Entry entry, Tone tone, CharacterRecord record, DateTimeOffset now)
    {
        if (tone != Tone.EndingSoon) return false;
        var end = entry.Value is TimerValue timer ? timer.Deadline : Assembler.WindowOf(entry.Item, record, now)?.End;
        return end is { } at && at > now && at - now <= PulseWithin;
    }
}
