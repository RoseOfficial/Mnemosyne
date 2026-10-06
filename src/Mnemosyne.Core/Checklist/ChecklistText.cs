using System.Text;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>A deterministic text form of a checklist, used by the golden tests and by /mnemo dump. Always "\n" line ends.</summary>
public static class ChecklistText
{
    public static string Render(Checklist checklist, CharacterRecord record, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.Append(record.Label).Append(" · ").Append(record.HomeWorld)
          .Append(" · last seen ").Append(Relative.Since(record.LastSeen, now))
          .Append(" · ").Append(checklist.Badge).Append(" to do");
        if (checklist.Stale) sb.Append(" · stale");
        sb.Append('\n');

        Top(sb, "TODAY", checklist.Today);
        Top(sb, "THIS WEEK", checklist.ThisWeek);
        Top(sb, "ENDING SOON", checklist.EndingSoon);
        Top(sb, "RUNNING EVENTS", checklist.Running);

        foreach (var section in checklist.Sections)
        {
            // My tasks prints as "MY TASKS"; the seven others as before.
            sb.Append(section.Category.Label().ToUpperInvariant()).Append('\n');
            if (section.Groups.Count == 0) { sb.Append("  (nothing)\n"); continue; }
            foreach (var group in section.Groups)
            {
                sb.Append("  ").Append(group.Name).Append('\n');
                foreach (var entry in group.Entries) sb.Append("    ").Append(Line(entry)).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static void Top(StringBuilder sb, string title, IReadOnlyList<Entry> entries)
    {
        sb.Append(title).Append('\n');
        if (entries.Count == 0) { sb.Append("  (nothing)\n"); return; }
        foreach (var entry in entries) sb.Append("  ").Append(Line(entry)).Append('\n');
    }

    public static string Line(Entry entry)
    {
        var glyph = entry.State switch
        {
            EntryState.Done => "[x]",
            EntryState.NotDone => "[ ]",
            EntryState.Upcoming => "[~]",
            EntryState.Past => "[-]",
            _ => "[?]",
        };
        var text = glyph + " " + entry.Name;
        if (entry.Display.Length > 0) text += " — " + entry.Display;
        if (entry.Detail.Length > 0) text += " · " + entry.Detail;
        if (entry.Marker.Length > 0) text += " (" + entry.Marker + ")";
        // The words the window draws beside a row off for the character's role.
        if (entry.OffFor is { } role) text += " · off for " + role;
        return text;
    }
}
