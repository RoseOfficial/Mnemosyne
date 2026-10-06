using System.Text;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>A deterministic text form of the Totals page, used by its golden test and by /mnemo dump, as BoardText is
/// for All tasks. Each line reads "name · total", then each holder indented, largest first, "label · amount", with " · as of last visit" or
/// " · last seen …" when the holder is stale. With nothing held anywhere, one line says so. Always "\n" line ends.</summary>
public static class TotalsText
{
    public static string Render(IReadOnlyList<TotalLine> lines, DateTimeOffset now)
    {
        if (lines.Count == 0) return "(nothing held)\n";
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            text.Append(line.Name).Append(" · ").Append(Compact.Format(line.Total)).Append('\n');
            foreach (var holder in line.Holders)
            {
                text.Append("  ").Append(holder.Record.Label).Append(" · ").Append(Compact.Format(holder.Amount));
                text.Append(holder.Stale switch
                {
                    StaleReason.Visit => " · as of last visit",
                    StaleReason.Threshold => " · last seen " + Relative.Since(holder.Record.LastSeen, now),
                    _ => "",
                });
                text.Append('\n');
            }
        }
        return text.ToString();
    }
}
