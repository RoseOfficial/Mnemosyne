using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>The rules the look of All tasks, All characters, Totals and the Gil over time graph draws, beside
/// the tone rules (Tones). Pure: every instant is passed in, and nothing here reads a clock or a file. Each reads what its page already
/// holds (a board line, a matrix row, a totals line, a graph line), so no page assembles anything again to draw it, and
/// every word it returns is a word the page or its text form already says.</summary>
public static class PageLook
{
    /// <summary>Character chips (the first three names, then "+n").</summary>
    public const int ChipsShown = 3;

    /// <summary>Board.Line's words: a timer that says it is ready; a deadline passed less than a minute ago (Relative.Until's "now"); the
    /// start of a reset's words and of a passed deadline's.</summary>
    private const string ReadyWord = "ready", NowWord = "now", ResetsPrefix = "resets ", OverduePrefix = "overdue by ";

    /// <summary>The labels of the first <paramref name="shown"/> characters under a line, in the order the line lists them (the
    /// sidebar's), and how many more there are, for the "+n" chip. A short line shows fewer.</summary>
    public static (IReadOnlyList<string> Names, int More) Chips(TaskLine line, int shown = ChipsShown)
    {
        var names = new List<string>(Math.Clamp(shown, 0, line.Count));
        foreach (var character in line.Characters)
        {
            if (names.Count >= shown) break;
            names.Add(character.Record.Label);
        }
        return (names, line.Count - names.Count);
    }

    /// <summary>A line's time as a pill: quiet "resets in 6 h", rose "ends in 1 d", green "ready". Rose has one meaning everywhere: in Ending
    /// soon, or overdue. A timer that says it is ready reads green. A reset is the end of a period, not a deadline, and reads quiet. An end
    /// reads rose while the line's own group is Ending soon (a pinned line keeps its own group), and so does a time already passed ("overdue by
    /// 2 h", or "now" within the minute); any other end is information, such as a running event with weeks left, and reads quiet. A line with no
    /// time has no pill (Neutral).</summary>
    public static Tone OfTime(TaskLine line) =>
        line.When.Length == 0 ? Tone.Neutral
        : line.When == ReadyWord ? Tone.Done
        : line.When.StartsWith(ResetsPrefix, StringComparison.Ordinal) ? Tone.Quiet
        : line.Group == BoardGroup.EndingSoon || line.When == NowWord || line.When.StartsWith(OverduePrefix, StringComparison.Ordinal) ? Tone.EndingSoon
        : Tone.Quiet;

    /// <summary>While a line is snoozed, the words its heading carries for it (BoardText.Heading: "snoozed, back in 3 h"),
    /// which the look draws as a quiet pill; null while it is not snoozed.</summary>
    public static string? Snoozed(TaskPreference preference, DateTimeOffset now) =>
        preference.SnoozedUntil is { } until && preference.SnoozedAt(now) ? "snoozed, back " + Relative.Until(until, now) : null;

    /// <summary>The badge is the sidebar's pill: a character's urgency read from its matrix row, as Tones.Urgency reads it from the
    /// checklist the row was summarised from: the row's Ending soon, Dailies left and Weeklies left are that checklist's three top groups'
    /// counts (Summariser.Summarise), and its badge their sum as the settings count it.</summary>
    public static Tone Urgency(MatrixRow row, bool weekliesCount) =>
        row.EndingSoon > 0 ? Tone.EndingSoon
        : row.DailiesLeft > 0 ? Tone.Today
        : weekliesCount && row.WeekliesLeft > 0 ? Tone.ThisWeek
        : Tone.Done;

    /// <summary>Dailies and weeklies left are pills in their tones: a count left reads its top group's tone, and none left reads
    /// quiet, since only a count above zero is highlighted. The
    /// Ending soon column reads the same way.</summary>
    public static Tone OfLeft(int left, Tone tone) => left > 0 ? tone : Tone.Quiet;

    /// <summary>Jobs at cap is a small bar: the jobs at their cap over the jobs unlocked, the numbers the column's words read; null
    /// while no job is read (none unlocked), whose "0 / 0" reads as its words alone, as a seal count with no cap does.</summary>
    public static Meter? Jobs(MatrixRow row) => row.JobsUnlocked > 0 ? new Meter(row.JobsAtCap, row.JobsUnlocked) : null;

    /// <summary>Bag space is a bar, rose at 5 free or fewer: how full the four bags are, used over their size, so the bar fills as
    /// the free slots run out; null for a blank cell, nothing stored.</summary>
    public static Meter? Bags(MatrixRow row) =>
        row.BagsFree is { } free && row.BagSlots > 0 ? new Meter(Math.Max(0, row.BagSlots - free), row.BagSlots) : null;

    /// <summary>The bag bar is rose at MatrixRow.LowBagSpace free or fewer, and Neutral, the accent, otherwise.</summary>
    public static Tone OfBags(MatrixRow row) => row.BagsLow ? Tone.EndingSoon : Tone.Neutral;

    /// <summary>Company seals is a bar toward the cap: the current company's seals over its cap; null for a blank, and for a count
    /// with no cap ("0" once a read zero cleared the row), which reads as its words alone.</summary>
    public static Meter? Seals(MatrixRow row) => row.Seals is { } seals && row.SealCap > 0 ? new Meter(seals, row.SealCap) : null;

    /// <summary>Ventures ready reads "2 of 5" with a dot, green when some are ready: the dot's tone, quiet while none is.</summary>
    public static Tone OfVentures(MatrixRow row) => row.VenturesReady > 0 ? Tone.Done : Tone.Quiet;

    /// <summary>Gil is compact, with the retainers' share in secondary text: MatrixRow.GilText in its two parts, the character's own
    /// gil and "(+160k)", the second empty while the retainers hold none; both empty for a blank.</summary>
    public static (string Own, string Retainers) Gil(MatrixRow row) =>
        row.Gil is { } gil ? (Compact.Format(gil), row.RetainerGil > 0 ? $"(+{Compact.Format(row.RetainerGil)})" : "") : ("", "");

    /// <summary>Each with a bar sized against the largest holder: a holder's amount over the line's largest, its first holder, since
    /// Totals.Build lists them largest first.</summary>
    public static Meter Share(TotalHolder holder, TotalLine line) => new(holder.Amount, line.Holders.Count > 0 ? line.Holders[0].Amount : 0);

    /// <summary>A "held by n" chip: how many characters hold more than zero, every holder the line opens on. A line no one holds (a gil
    /// line, which Totals.Build always lists) says so instead, "nothing held yet", and the look draws it quiet.</summary>
    public static string HeldBy(TotalLine line) => line.Holders.Count > 0 ? $"held by {line.Holders.Count}" : "nothing held yet";

    /// <summary>The currency's game icon: a currency line's own item's icon, MGP's included; the three gil lines take gil's.
    /// Null when the catalog carries none, as an older dump can.</summary>
    public static uint? IconOf(TotalLine line, Catalog catalog) =>
        catalog.TryGet(line.Id is Totals.CharacterGilId or Totals.RetainerGilId or Totals.AllGilId ? MoneyIds.CurrencyId(MoneyIds.Gil) : line.Id)?.IconId;

    /// <summary>Each change line is a pill, green for up and rose for down, quiet for no change: the line's amount on its last day
    /// against its first, as GilGraph.ChangeLine words it.</summary>
    public static Tone OfChange(GilSeries series) =>
        series.Amounts[^1] > series.Amounts[0] ? Tone.Done
        : series.Amounts[^1] < series.Amounts[0] ? Tone.EndingSoon
        : Tone.Quiet;
}
