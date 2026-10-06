using System.Text;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>A deterministic text form of the board, used by its golden test and by /mnemo dump, as ChecklistText is
/// for a checklist. Every group is listed in order, "(nothing)" when empty, then the Hidden (n) lines. A line reads as the window's tree node
/// does (Heading), and each character under it as the window's row does (Character). Always "\n" line ends.</summary>
public static class BoardText
{
    public static string Render(TaskBoard board, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        foreach (var section in board.Groups)
        {
            sb.Append(Board.Title(section.Group).ToUpperInvariant()).Append('\n');
            if (section.Lines.Count == 0) { sb.Append("  (nothing)\n"); continue; }
            foreach (var line in section.Lines) Line(sb, line, now);
        }
        sb.Append("HIDDEN (").Append(board.Hidden.Count).Append(")\n");
        foreach (var line in board.Hidden) Line(sb, line, now);
        return sb.ToString();
    }

    /// <summary>A line's own words: the catalog's name, how many characters owe it, its time, and its adjustments.</summary>
    public static string Heading(TaskLine line, DateTimeOffset now)
    {
        var text = $"{line.Name} — {line.Count} character{(line.Count == 1 ? "" : "s")}";
        if (line.When.Length > 0) text += " · " + line.When;
        var tags = Tags(line.Preference, now);
        return tags.Count > 0 ? text + " (" + string.Join(", ", tags) + ")" : text;
    }

    /// <summary>One character under a line: its name, then its own label for the task, its value and its marker, and "stale" when it is.</summary>
    public static string Character(OwingCharacter character)
    {
        var text = $"{character.Record.Label}: {character.Entry.Name}";
        if (character.Entry.Display.Length > 0) text += " — " + character.Entry.Display;
        if (character.Entry.Marker.Length > 0) text += " (" + character.Entry.Marker + ")";
        return character.Stale ? text + " · stale" : text;
    }

    /// <summary>The adjustments a line carries, in the menu's order; a live snooze says when the task is back.</summary>
    private static List<string> Tags(TaskPreference preference, DateTimeOffset now)
    {
        var tags = new List<string>();
        if (preference.Pinned) tags.Add("pinned");
        if (preference.Priority == TaskPriority.High) tags.Add("high");
        else if (preference.Priority == TaskPriority.Low) tags.Add("low");
        if (preference.SnoozedUntil is { } until && preference.SnoozedAt(now)) tags.Add("snoozed, back " + Relative.Until(until, now));
        if (preference.Hidden) tags.Add("hidden");
        return tags;
    }

    private static void Line(StringBuilder sb, TaskLine line, DateTimeOffset now)
    {
        sb.Append("  ").Append(Heading(line, now)).Append('\n');
        foreach (var character in line.Characters) sb.Append("    ").Append(Character(character)).Append('\n');
    }
}
