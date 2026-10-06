using System.Globalization;
using System.Text;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>The dump's gil history section, as TotalsText is its totals section. One line for All gil over the
/// visible characters, then one per visible character, indented, in the order given, the sidebar's: each amount today, 7 days ago, 30 days ago
/// and a year ago, carried forward from the latest point on or before that day, or "no history yet" before the first. Amounts are
/// exact and grouped by thousands, so a vendor purchase shows. A hidden character is left out. Always "\n" line
/// ends.</summary>
public static class GilHistoryText
{
    public const string NoHistory = "no history yet";

    private static readonly string[] Words = ["today", "7 days ago", "30 days ago", "a year ago"];

    public static string Render(IReadOnlyList<CharacterRecord> records, DateTimeOffset now)
    {
        var visible = records.Where(r => !r.Hidden).ToList();
        var today = GilPoints.DayOf(now);
        DateOnly[] days = [today, today.AddDays(-7), today.AddDays(-30), today.AddYears(-1)];
        var text = new StringBuilder();
        Line(text, "", Totals.AllGilName, days.Select(day => GilPoints.TotalOn(visible, day)));
        foreach (var record in visible) Line(text, "  ", record.Label, days.Select(day => GilPoints.On(record, day)));
        return text.ToString();
    }

    private static void Line(StringBuilder text, string indent, string name, IEnumerable<long?> amounts)
    {
        text.Append(indent).Append(name);
        foreach (var (word, amount) in Words.Zip(amounts))
            text.Append(" · ").Append(word).Append(' ').Append(amount is { } held ? held.ToString("N0", CultureInfo.InvariantCulture) : NoHistory);
        text.Append('\n');
    }
}
