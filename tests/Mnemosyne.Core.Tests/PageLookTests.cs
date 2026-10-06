using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The rules the look of All tasks, All characters, Totals and the Gil over time graph draws. Every board, row and line here
/// is built as the window builds it, so a rule is read from the very objects the page draws.</summary>
public class PageLookTests
{
    private static (CharacterRecord Record, Mnemosyne.Core.Checklist.Checklist Checklist) Owes(CharacterRecord record, Catalog? catalog = null) =>
        (Record: record, Checklist: Assembler.Assemble(catalog ?? Items.Catalog(), record, new Settings(), Items.Now));

    private static TaskLine LineOf(TaskBoard board, string id) => board.Groups.SelectMany(g => g.Lines).Single(l => l.Id == id);

    private static MatrixRow RowOf(CharacterRecord record) => Summariser.Summarise(TotalsTests.Built, record, new Settings(), Items.Now);

    [Fact]
    public void A_lines_chips_are_its_first_three_characters_in_its_own_order_then_how_many_more()
    {
        // Five characters owe the stickers, given in the sidebar's order, which the line keeps: the chips are the first three, then
        // "+2". A short line asks for fewer; a line of two has no "+n".
        string[] names = ["Momo Lulu", "Aria Whitewind", "Zed Zero", "Kestrel Vane", "Old Alt"];
        var owing = names.Select((name, i) =>
        {
            var record = Items.Record((ulong)(i + 1), name);
            record.Values[Items.Stickers.Id] = new CounterValue(3, 9, Items.Now);
            return Owes(record);
        }).ToArray();
        var line = LineOf(Board.Build(owing, new TaskPreferences(), Items.Now), Items.Stickers.Id);

        var (shown, more) = PageLook.Chips(line);
        Assert.Equal(["Momo Lulu", "Aria Whitewind", "Zed Zero"], shown);
        Assert.Equal(2, more);
        Assert.Equal(3, PageLook.ChipsShown);
        (shown, more) = PageLook.Chips(line, 1);
        Assert.Equal(["Momo Lulu"], shown);
        Assert.Equal(4, more);
        (shown, more) = PageLook.Chips(line, 0);
        Assert.Empty(shown);
        Assert.Equal(5, more);

        var two = LineOf(Board.Build(owing[..2], new TaskPreferences(), Items.Now), Items.Stickers.Id);
        (shown, more) = PageLook.Chips(two);
        Assert.Equal(["Momo Lulu", "Aria Whitewind"], shown);
        Assert.Equal(0, more);
    }

    [Fact]
    public void A_lines_time_is_rose_in_ending_soon_or_overdue_quiet_for_a_reset_or_a_later_end_and_green_when_ready()
    {
        // A line's time as a pill: quiet "resets in 6 h", rose "ends in 1 d", green "ready". Rose has one meaning everywhere: in Ending
        // soon, or overdue. On the board BoardTests reads its words from, each word the board can give a line and the tone the look
        // draws it in: the venture and the Moonfire Faire, in Ending soon, are rose; the overdue timer, in Today, is rose; the long
        // event, running with 20 days left, is information and quiet. A line with no time has no pill.
        var longEvent = new ItemDefinition("event:long", Category.Events, "Long", "A long event", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
            new Availability(Items.Now - TimeSpan.FromDays(10), Items.Now + TimeSpan.FromDays(20)));
        var readyMap = new ItemDefinition("reset:treasure-map", Category.Resets, "Allowances", "Treasure map", ItemKind.Timer, ResetRule.None, 1, Freshness.AtLogin,
            ReadyWhenPassed: true);
        var catalog = new Catalog([.. Items.Catalog().Items, longEvent, readyMap]);
        var aria = SummariserTests.Aria();
        aria.Values[longEvent.Id] = new DoneValue(false);
        aria.Values[readyMap.Id] = new TimerValue(Items.Now - TimeSpan.FromHours(3));
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(5));
        kestrel.Values[Items.Map.Id] = new TimerValue(Items.Now - TimeSpan.FromDays(1) - TimeSpan.FromHours(2));
        var board = Board.Build([Owes(aria, catalog), Owes(kestrel, catalog)], new TaskPreferences(), Items.Now);

        (string Id, string When, Tone Tone)[] expected =
        [
            (Items.Allowances.Id, "resets in 3 h", Tone.Quiet),
            (Items.Stickers.Id, "resets in 1 d 20 h", Tone.Quiet),
            (Items.Venture.Id, "ends in 5 h", Tone.EndingSoon),
            (Items.Moonfire.Id, "ends in 1 d 20 h", Tone.EndingSoon),
            (longEvent.Id, "ends in 20 d", Tone.Quiet),
            (Items.Map.Id, "overdue by 1 d 2 h", Tone.EndingSoon),
            (readyMap.Id, "ready", Tone.Done),
        ];
        foreach (var (id, when, tone) in expected)
        {
            var line = LineOf(board, id);
            Assert.Equal(when, line.When);
            Assert.Equal(tone, PageLook.OfTime(line));
        }
        Assert.Equal(BoardGroup.EndingSoon, LineOf(board, Items.Moonfire.Id).Group);
        Assert.Equal(BoardGroup.Running, LineOf(board, longEvent.Id).Group);
        var venture = LineOf(board, Items.Venture.Id);
        Assert.Equal(Tone.Quiet, PageLook.OfTime(venture with { Group = BoardGroup.Running }));          // an end off Ending soon
        Assert.Equal(Tone.EndingSoon, PageLook.OfTime(venture with { Group = BoardGroup.Today, When = "now" }));   // passed within the minute
        Assert.Equal(Tone.Neutral, PageLook.OfTime(venture with { When = "" }));
    }

    [Fact]
    public void A_snoozed_line_carries_the_words_its_heading_says_and_a_line_awake_carries_none()
    {
        // The look draws a snooze as a quiet pill with the heading's own words (BoardText.Heading), so the page says what the text
        // form says. A snooze that has passed is no snooze.
        var preferences = new TaskPreferences();
        preferences.Snooze(Items.Stickers.Id, Items.Now + TimeSpan.FromHours(3), Items.Now);
        var line = Assert.Single(Board.Build([Owes(SummariserTests.Aria())], preferences, Items.Now).Hidden);

        var words = PageLook.Snoozed(line.Preference, Items.Now);
        Assert.Equal("snoozed, back in 3 h", words);
        Assert.Contains(words!, BoardText.Heading(line, Items.Now), StringComparison.Ordinal);
        Assert.Null(PageLook.Snoozed(line.Preference, Items.Now + TimeSpan.FromHours(3)));
        Assert.Null(PageLook.Snoozed(TaskPreference.Default, Items.Now));
    }

    [Fact]
    public void A_rows_badge_reads_the_urgency_its_checklist_reads_and_a_count_left_reads_its_tone_or_quiet_at_zero()
    {
        // The badge is the sidebar's pill; dailies and weeklies left are pills in their tones. The row's urgency is the one the
        // sidebar's pill takes from the same character's checklist (Tones.Urgency), weeklies counted or not. A count of none is quiet:
        // only a count above zero is highlighted.
        var busy = SummariserTests.Aria();                                         // Ending soon: Moonfire, the venture
        var today = Items.Record(2, "Kestrel Vane");
        today.Values[Items.Roulette.Id] = new DoneValue(false);
        var week = Items.Record(3, "Momo Lulu");
        week.Values[Items.Stickers.Id] = new CounterValue(3, 9, Items.Now);
        var clear = Items.Record(4, "Old Alt");
        clear.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-12");
        clear.Values[Items.Stickers.Id] = new CounterValue(9, 9, Items.Now);
        var small = new Catalog([Items.Roulette, Items.Stickers]);
        var urgencies = new List<Tone>();
        foreach (var (record, catalog) in new[] { (busy, Items.Catalog()), (today, small), (week, new Catalog([Items.Stickers])), (clear, small) })
        {
            var checklist = Assembler.Assemble(catalog, record, new Settings(), Items.Now);
            var row = Summariser.Summarise(catalog, record, new Settings(), Items.Now, checklist);
            foreach (var weekliesCount in new[] { true, false })
                Assert.Equal(Tones.Urgency(checklist, weekliesCount), PageLook.Urgency(row, weekliesCount));
            urgencies.Add(PageLook.Urgency(row, weekliesCount: true));
        }
        Assert.Equal([Tone.EndingSoon, Tone.Today, Tone.ThisWeek, Tone.Done], urgencies);

        Assert.Equal(Tone.Today, PageLook.OfLeft(3, Tone.Today));
        Assert.Equal(Tone.ThisWeek, PageLook.OfLeft(1, Tone.ThisWeek));
        Assert.Equal(Tone.Quiet, PageLook.OfLeft(0, Tone.EndingSoon));
    }

    [Fact]
    public void The_holdings_cells_read_their_bars_and_tones_from_the_row_and_a_blank_cell_has_none()
    {
        // Jobs at cap and company seals are bars toward their cap, bag space a bar of how full the bags are, rose at 5 free or fewer,
        // ventures ready a dot, green when some are ready, and gil in two parts, the retainers' share apart. Each reads the numbers the
        // column's words read; a blank reads none, and so does "0 / 0" jobs, with no job read.
        var aria = Items.Record(1);
        TotalsTests.Gil(aria, 1_234_567, 100_000, 50_000, 10_000);
        aria.Values[MoneyIds.Bags] = TotalsTests.Held(120, 140);
        aria.Values[CatalogBuilder.CurrentCompanyId] = new DoneValue(false, "1");               // the Maelstrom: Storm Seals
        aria.Values[MoneyIds.CurrencyId(20)] = TotalsTests.Held(26_104, 50_000);
        aria.Values[MoneyIds.VentureId(0)] = new TimerValue(Items.Now - TimeSpan.FromHours(1));
        aria.Values[MoneyIds.VentureId(1)] = new TimerValue(Items.Now + TimeSpan.FromHours(1));
        var a = RowOf(aria);

        Assert.Equal(new Meter(120, 140), PageLook.Bags(a));
        Assert.Equal(("20 free", 140), (a.BagsText, a.BagSlots));
        Assert.Equal(Tone.Neutral, PageLook.OfBags(a));
        Assert.Equal(new Meter(26_104, 50_000), PageLook.Seals(a));
        Assert.Equal(("1 of 2", Tone.Done), (a.VenturesText, PageLook.OfVentures(a)));
        Assert.Equal(("1.2M", "(+160k)"), PageLook.Gil(a));
        Assert.Equal(a.GilText, PageLook.Gil(a).Own + " " + PageLook.Gil(a).Retainers);

        aria.Values[MoneyIds.Bags] = TotalsTests.Held(136, 140);                                 // 4 free: low
        aria.Values.Remove(MoneyIds.CurrencyId(20));                                             // a read zero cleared the seals, their cap with it
        aria.Values[MoneyIds.VentureId(0)] = new TimerValue(Items.Now + TimeSpan.FromHours(2));
        var low = RowOf(aria);
        Assert.Equal(new Meter(136, 140), PageLook.Bags(low));
        Assert.Equal(Tone.EndingSoon, PageLook.OfBags(low));
        Assert.Equal("0", low.SealsText);
        Assert.Null(PageLook.Seals(low));
        Assert.Equal(("0 of 2", Tone.Quiet), (low.VenturesText, PageLook.OfVentures(low)));

        var kestrel = Items.Record(2, "Kestrel Vane");
        TotalsTests.Gil(kestrel, 8_500);
        var k = RowOf(kestrel);
        Assert.Equal(("8,500", ""), PageLook.Gil(k));
        Assert.Equal(k.GilText, PageLook.Gil(k).Own);
        var blank = RowOf(Items.Record(3, "Momo Lulu"));
        Assert.Null(PageLook.Bags(blank));
        Assert.Null(PageLook.Seals(blank));
        Assert.Equal(("", ""), PageLook.Gil(blank));
        Assert.Equal(0, blank.BagSlots);

        var jobs = PageLook.Jobs(a with { JobsAtCap = 3, JobsUnlocked = 12 });
        Assert.Equal(new Meter(3, 12), jobs);
        Assert.Equal(0.25f, jobs!.Value.Fraction);
        Assert.Equal(0, blank.JobsUnlocked);
        Assert.Null(PageLook.Jobs(blank));
    }

    [Fact]
    public void A_holders_bar_is_sized_against_the_largest_holder_and_each_line_names_its_icon_and_holders()
    {
        // An open line lists its holders, each with a bar sized against the largest holder; the currency's game icon, gil's for the
        // three gil lines and MGP's own under Content; a "held by n" chip, or "nothing held yet" on a gil line no one holds, which
        // Totals.Build lists all the same.
        var aria = Items.Record(1);
        TotalsTests.Gil(aria, 1_200_000, 300_000);
        aria.Values[MoneyIds.CurrencyId(28)] = TotalsTests.Held(1_200, 2_000);                   // Poetics
        aria.Values[MoneyIds.CurrencyId(CatalogBuilder.MgpItemId)] = TotalsTests.Held(50_000, 9_999_999);
        var kestrel = Items.Record(2, "Kestrel Vane");
        TotalsTests.Gil(kestrel, 300_000);
        var lines = Totals.Build([kestrel, aria], TotalsTests.Built, new Settings(), Items.Now);
        TotalLine Line(string id) => lines.Single(l => l.Id == id);

        var own = Line(Totals.CharacterGilId);
        Assert.Equal(["Aria Whitewind", "Kestrel Vane"], own.Holders.Select(h => h.Record.Label));
        Assert.Equal(1f, PageLook.Share(own.Holders[0], own).Fraction);
        Assert.Equal(new Meter(300_000, 1_200_000), PageLook.Share(own.Holders[1], own));
        Assert.Equal(0.25f, PageLook.Share(own.Holders[1], own).Fraction);
        Assert.Equal("held by 2", PageLook.HeldBy(own));
        Assert.Equal("held by 1", PageLook.HeldBy(Line(Totals.RetainerGilId)));

        Assert.Equal(65002u, PageLook.IconOf(own, TotalsTests.Built));
        Assert.Equal(65002u, PageLook.IconOf(Line(Totals.RetainerGilId), TotalsTests.Built));
        Assert.Equal(65002u, PageLook.IconOf(Line(Totals.AllGilId), TotalsTests.Built));
        Assert.Equal(65023u, PageLook.IconOf(Line(MoneyIds.CurrencyId(28)), TotalsTests.Built));
        Assert.Equal(65025u, PageLook.IconOf(Line(MoneyIds.CurrencyId(CatalogBuilder.MgpItemId)), TotalsTests.Built));
        Assert.Null(PageLook.IconOf(own, Catalog.Empty));
        var nobody = Totals.Build([Items.Record(3, "Momo Lulu")], TotalsTests.Built, new Settings(), Items.Now);
        Assert.Empty(nobody.Single(l => l.Id == Totals.AllGilId).Holders);
        Assert.Equal("nothing held yet", PageLook.HeldBy(nobody.Single(l => l.Id == Totals.AllGilId)));
    }

    [Fact]
    public void A_change_line_is_green_up_rose_down_and_quiet_with_no_change_as_its_words_say()
    {
        // Each change line is a pill, green for up and rose for down, quiet for no change, read from the line's own first and last day,
        // the two amounts GilGraph.ChangeLine words; a dip or a peak between them changes nothing.
        var from = new DateOnly(2026, 8, 13);
        var graph = new GilGraph(GilRange.OneMonth, from, from.AddDays(2), from, null, new Dictionary<ulong, GilSeries>());
        (GilSeries Series, string Word, Tone Tone)[] cases =
        [
            (new GilSeries(from, [1_000_000, 900_000, 1_250_000]), "up 250k", Tone.Done),
            (new GilSeries(from, [1_000_000, 1_400_000, 700_000]), "down 300k", Tone.EndingSoon),
            (new GilSeries(from, [1_000_000, 2_000_000, 1_000_000]), "no change", Tone.Quiet),
        ];
        foreach (var (series, word, tone) in cases)
        {
            Assert.Contains(word, graph.ChangeLine("Aria Whitewind", series), StringComparison.Ordinal);
            Assert.Equal(tone, PageLook.OfChange(series));
        }
    }
}
