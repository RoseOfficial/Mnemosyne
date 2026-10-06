using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>The board's groups, top to bottom. A task's own group is never Pinned: it is the most urgent group any
/// of its characters puts it in, in the order EndingSoon, Today, ThisWeek, Running. A pin moves the line to Pinned.</summary>
public enum BoardGroup { Pinned, EndingSoon, Today, ThisWeek, Running }

/// <summary>One character under a task line: its record; its own entry for the task, whose Name is its own label,
/// Display its value and Marker its marker; and whether it is stale, which the sidebar dims.</summary>
public sealed record OwingCharacter(CharacterRecord Record, Entry Entry, bool Stale);

/// <summary>One task: the item; its own group; the most urgent time, and its words; the next reset of its own schedule,
/// the earliest among its characters, or null when it has none (a snooze then takes the next daily reset); every character that owes it, in the
/// order the board was given; and its saved adjustments. Name is the catalog's, so a slot numbered per character reads as the catalog numbers
/// it, and each character's own name for it is under the line.</summary>
public sealed record TaskLine(ItemDefinition Item, BoardGroup Group, DateTimeOffset Urgent, string When, DateTimeOffset? NextReset, IReadOnlyList<OwingCharacter> Characters, TaskPreference Preference)
{
    public string Id => Item.Id;
    public string Name => Item.Name;
    public int Count => Characters.Count;
}

public sealed record BoardSection(BoardGroup Group, IReadOnlyList<TaskLine> Lines);

/// <summary>What Board.Build returns: the five groups in order, empty ones included; the hidden and the snoozed lines, which the Hidden (n)
/// toggle shows; and the ids whose saved snooze has passed, which the caller clears and saves.</summary>
public sealed record TaskBoard(IReadOnlyList<BoardSection> Groups, IReadOnlyList<TaskLine> Hidden, IReadOnlyList<string> Expired)
{
    public IReadOnlyList<TaskLine> In(BoardGroup group) => Groups.Single(section => section.Group == group).Lines;
}

/// <summary>The key a caller keeps its last board on, so All tasks does not rebuild on every
/// frame it is open. Equal keys mean nothing that could change the board has moved: each character's own Checklist reference, in order (the
/// cache hands back the same object until that record, the settings version or the minute moves); the tasks version, which moves only on an
/// adjustment (Plugin.UpdateTasks); and the UTC minute, which also covers a snooze passing, since Board.Build resolves every expired one within
/// the minute it ends. A pure key: it reads nothing but what it is given, so the comparison is a plain equality check, not a rebuild.</summary>
public sealed record BoardCacheKey(IReadOnlyList<Checklist> Checklists, int TasksVersion, long Minute)
{
    public static BoardCacheKey For(IReadOnlyList<(CharacterRecord Record, Checklist Checklist)> characters, int tasksVersion, DateTimeOffset now) =>
        new(characters.Select(c => c.Checklist).ToList(), tasksVersion, now.ToUnixTimeSeconds() / 60);

    public bool Equals(BoardCacheKey? other) =>
        other != null && TasksVersion == other.TasksVersion && Minute == other.Minute && Checklists.SequenceEqual(other.Checklists);

    public override int GetHashCode() => HashCode.Combine(TasksVersion, Minute, Checklists.Count);
}

/// <summary>One list across every character, built from each character's own checklist, so it can never
/// disagree with one about what is due. A pure function: it reads the checklists it is given and the adjustments, and changes
/// neither. A task is in the list while it sits in some character's Ending soon, Today, This week or Running events; a hidden character counts
/// for nothing, and an ignored item never reaches a top group. It is listed once, in the most urgent of its characters' groups, with
/// every one of them under it in the order given, which the plugin makes the sidebar's. Within a group, lines run by priority, the most
/// urgent time, the most characters, then the name. A pin moves a line to Pinned, where its own group ranks first; a hide or a live snooze
/// takes it out of the list whatever its pin, into the Hidden (n) lines. A snooze ends exactly at its instant, and every passed one is
/// named in Expired for the caller to clear. An adjustment for a task no character owes, or for an id the catalog no longer has, stays saved and
/// does nothing.</summary>
public static class Board
{
    public static TaskBoard Build(IReadOnlyList<(CharacterRecord Record, Checklist Checklist)> characters, TaskPreferences preferences, DateTimeOffset now)
    {
        var owed = new Dictionary<string, List<(OwingCharacter Character, BoardGroup Group)>>(StringComparer.Ordinal);
        foreach (var (record, checklist) in characters)
        {
            if (record.Hidden) continue;
            Collect(owed, record, checklist, checklist.EndingSoon, BoardGroup.EndingSoon);
            Collect(owed, record, checklist, checklist.Today, BoardGroup.Today);
            Collect(owed, record, checklist, checklist.ThisWeek, BoardGroup.ThisWeek);
            Collect(owed, record, checklist, checklist.Running, BoardGroup.Running);
        }

        var groups = Enum.GetValues<BoardGroup>().ToDictionary(group => group, _ => new List<TaskLine>());
        var hidden = new List<TaskLine>();
        // Resolved once per distinct schedule for the whole build, not once per owing character. The same daily or weekly
        // schedule is asked for by every line that uses it, and each ask allocates an OrderBy (ResetSchedules.NextReset); this cache is the
        // cheap fix, ahead of the frame-level one in Plugin.BoardFor. ResetSchedules itself is unchanged: other callers rely on its OrderBy.
        var resets = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var owing in owed.Values)
        {
            var line = Line(owing, preferences, now, resets);
            if (line.Preference.Hidden || line.Preference.SnoozedAt(now)) hidden.Add(line);
            else groups[line.Preference.Pinned ? BoardGroup.Pinned : line.Group].Add(line);
        }
        var sections = Enum.GetValues<BoardGroup>()
            .Select(group => new BoardSection(group, InOrder(groups[group], ownGroupFirst: group == BoardGroup.Pinned)))
            .ToList();
        var expired = preferences.Keys.Where(id => preferences.Get(id).SnoozedUntil <= now).Order(StringComparer.Ordinal).ToList();
        return new TaskBoard(sections, InOrder(hidden, ownGroupFirst: true), expired);
    }

    /// <summary>Each group's name, which the window and the text form both use.</summary>
    public static string Title(BoardGroup group) => group switch
    {
        BoardGroup.Pinned => "Pinned",
        BoardGroup.EndingSoon => "Ending soon",
        BoardGroup.Today => "Today",
        BoardGroup.ThisWeek => "This week",
        _ => "Running events",
    };

    /// <summary>A checklist puts an entry in at most one top group (Assembler.Place), so each character is added to a task's list once, in the
    /// order the characters are given.</summary>
    private static void Collect(Dictionary<string, List<(OwingCharacter Character, BoardGroup Group)>> owed, CharacterRecord record, Checklist checklist,
        IReadOnlyList<Entry> entries, BoardGroup group)
    {
        foreach (var entry in entries)
        {
            if (!owed.TryGetValue(entry.Item.Id, out var list)) owed[entry.Item.Id] = list = [];
            list.Add((new OwingCharacter(record, entry, checklist.Stale), group));
        }
    }

    /// <summary>One line. Its group is the most urgent any of its characters gives it. Its time in Today and This week is the task's own
    /// next reset, the earliest among its characters' schedules ("resets in 6 h"). In Ending soon and Running events, and for a Timer anywhere,
    /// it is the earliest deadline among its characters ("ends in 1 d 4 h", "overdue by 2 h", or "ready" for a timer that says so), since a
    /// Timer is a deadline, not a period. A task with neither has no time and sorts after those that have one.</summary>
    private static TaskLine Line(List<(OwingCharacter Character, BoardGroup Group)> owing, TaskPreferences preferences, DateTimeOffset now,
        Dictionary<string, DateTimeOffset> resets)
    {
        var item = owing[0].Character.Entry.Item;
        var group = owing.Min(o => o.Group);
        DateTimeOffset? reset = null;
        DateTimeOffset? deadline = null;
        foreach (var (character, _) in owing)
        {
            if (Assembler.ScheduleFor(item, character.Record) is { } schedule) reset = Earliest(reset, NextReset(schedule, now, resets));
            if (Deadline(character.Entry, character.Record, now) is { } due) deadline = Earliest(deadline, due);
        }
        var (urgent, when) = group is BoardGroup.Today or BoardGroup.ThisWeek && item.Kind != ItemKind.Timer && reset is { } next
            ? (next, "resets " + Relative.Until(next, now))
            : deadline is { } end ? (end, Ends(item, end, now))
            : reset is { } fallback ? (fallback, "resets " + Relative.Until(fallback, now))
            : (DateTimeOffset.MaxValue, "");
        return new TaskLine(item, group, urgent, when, reset, owing.Select(o => o.Character).ToList(), preferences.Get(item.Id));
    }

    /// <summary>A character's deadline for the task: its timer's; now for a Manual Timer with nothing stored, an "every" task never ticked or
    /// whose stored value a change of kind dropped, so it sorts with one whose deadline has just passed instead of after every daily at
    /// DateTimeOffset.MaxValue, since both read "ready" and are due now; or the end of the window the task is doable in.
    /// None for a periodic task without a window.</summary>
    private static DateTimeOffset? Deadline(Entry entry, CharacterRecord record, DateTimeOffset now) =>
        entry.Value is TimerValue timer ? timer.Deadline
        : entry.Item.Kind == ItemKind.Timer && entry.Item.Freshness.Kind == FreshnessKind.Manual ? now
        : Assembler.WindowOf(entry.Item, record, now)?.End;

    private static DateTimeOffset Earliest(DateTimeOffset? soFar, DateTimeOffset candidate) => soFar is { } known && known <= candidate ? known : candidate;

    /// <summary>ResetSchedules.NextReset for one schedule, resolved once per Build and reused by every line and every character that asks for it.</summary>
    private static DateTimeOffset NextReset(string schedule, DateTimeOffset now, Dictionary<string, DateTimeOffset> resets)
    {
        if (!resets.TryGetValue(schedule, out var next)) resets[schedule] = next = ResetSchedules.NextReset(schedule, now);
        return next;
    }

    private static string Ends(ItemDefinition item, DateTimeOffset deadline, DateTimeOffset now) =>
        item.ReadyWhenPassed && deadline <= now ? "ready"
        : deadline >= now ? "ends " + Relative.Until(deadline, now)
        : Relative.Until(deadline, now);

    /// <summary>The board's keys in turn, after each line's own group where asked (Pinned, and the Hidden (n) lines). The id comes last, so two
    /// tasks that share a name still keep one order.</summary>
    private static IReadOnlyList<TaskLine> InOrder(IEnumerable<TaskLine> lines, bool ownGroupFirst) =>
        lines.OrderBy(line => ownGroupFirst ? line.Group : BoardGroup.Pinned)
            .ThenBy(line => Rank(line.Preference.Priority))
            .ThenBy(line => line.Urgent)
            .ThenByDescending(line => line.Count)
            .ThenBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>High, then Normal, then Low. A value no build names, such as a number in a hand-edited file, ranks as Normal.</summary>
    private static int Rank(TaskPriority priority) => priority switch
    {
        TaskPriority.High => 0,
        TaskPriority.Low => 2,
        _ => 1,
    };
}
