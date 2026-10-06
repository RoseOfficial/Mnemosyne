using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The Totals page's lines, summed from stored values alone, and the one compact number form. The golden of a small page
/// is GoldenTests'; the four All characters columns are SummariserTests'.</summary>
public class TotalsTests
{
    /// <summary>The checked-in rows, and the real catalog built once from them, so the lines follow the Money category's own order and names.</summary>
    internal static readonly CatalogRows Rows = new FixtureCatalogSource().Load();

    internal static readonly Catalog Built = CatalogBuilder.Build(Rows, CatalogExtras.Load());

    internal static CounterValue Held(int current, int cap) => new(current, cap, Items.Now);

    /// <summary>A character's own gil and, slot by slot from 0, its retainers' gil, over gil's 999,999,999 cap.</summary>
    internal static void Gil(CharacterRecord record, int own, params int[] retainers)
    {
        record.Values[MoneyIds.CurrencyId(MoneyIds.Gil)] = Held(own, 999_999_999);
        for (var slot = 0; slot < retainers.Length; slot++) record.Values[MoneyIds.RetainerId(slot, MoneyIds.RetainerGil)] = Held(retainers[slot], 999_999_999);
    }

    private static TotalLine LineOf(IReadOnlyList<TotalLine> lines, string id) => lines.Single(l => l.Id == id);

    private static IEnumerable<(string Label, long Amount)> Holders(TotalLine line) => line.Holders.Select(h => (h.Record.Label, h.Amount));

    [Fact]
    public void A_number_reads_plain_under_10000_then_with_k_M_or_B_and_one_decimal_cut_never_rounded_up()
    {
        // Tested at its edges 9,999, 10,000, 999,999 and 1,000,000. Grouped by thousands under 10,000; then one decimal, cut and never
        // rounded up, so 999,999 is never "1000k" or "1M"; a trailing ".0" dropped; "B" from a billion.
        (long Value, string Text)[] cases =
        [
            (0, "0"), (900, "900"), (8_500, "8,500"), (9_999, "9,999"), (10_000, "10k"), (12_345, "12.3k"), (26_104, "26.1k"), (50_000, "50k"),
            (160_000, "160k"), (999_999, "999.9k"), (1_000_000, "1M"), (1_234_567, "1.2M"), (999_999_999, "999.9M"), (1_000_000_000, "1B"),
            (2_999_999_997, "2.9B"),
        ];
        foreach (var (value, text) in cases) Assert.Equal(text, Compact.Format(value));
    }

    [Fact]
    public void The_gil_lines_sum_every_characters_own_gil_their_retainers_gil_from_several_retainers_and_both()
    {
        // Gil on characters sums currency:1, gil on retainers every retainer:<slot>:gil row, and All gil the two. A character's
        // retainers are one holder, their sum. A row stored at zero (a retainer with no gil) holds nothing. A record with no Money
        // read yet adds nothing and has no breakdown row. The three lines are listed whatever they hold, as gil itself always is.
        var aria = Items.Record(1);
        Gil(aria, 1_234_567, 100_000, 50_000, 10_000);                                        // three retainers
        var kestrel = Items.Record(2, "Kestrel Vane");
        Gil(kestrel, 8_500, 0);                                                                // one retainer, with no gil
        var momo = Items.Record(3, "Momo Lulu");                                               // nothing read yet

        var lines = Totals.Build([aria, kestrel, momo], Built, new Settings(), Items.Now);

        Assert.Equal([Totals.CharacterGilId, Totals.RetainerGilId, Totals.AllGilId], lines.Take(3).Select(l => l.Id));
        Assert.Equal(["Gil on characters", "Gil on retainers", "All gil"], lines.Take(3).Select(l => l.Name));
        Assert.Equal(1_243_067L, LineOf(lines, Totals.CharacterGilId).Total);
        Assert.Equal([("Aria Whitewind", 1_234_567L), ("Kestrel Vane", 8_500L)], Holders(LineOf(lines, Totals.CharacterGilId)));
        Assert.Equal(160_000L, LineOf(lines, Totals.RetainerGilId).Total);
        Assert.Equal([("Aria Whitewind", 160_000L)], Holders(LineOf(lines, Totals.RetainerGilId)));
        Assert.Equal(1_403_067L, LineOf(lines, Totals.AllGilId).Total);
        Assert.Equal([("Aria Whitewind", 1_394_567L), ("Kestrel Vane", 8_500L)], Holders(LineOf(lines, Totals.AllGilId)));
        Assert.Equal(3, lines.Count);                                                          // nobody holds anything else

        var none = Totals.Build([momo], Built, new Settings(), Items.Now);
        Assert.Equal([0L, 0L, 0L], none.Select(l => l.Total));
        Assert.All(none, l => Assert.Empty(l.Holders));
    }

    [Fact]
    public void A_currency_is_summed_over_the_characters_that_hold_it_and_a_currency_nobody_holds_is_left_out()
    {
        // One line per currency some visible character holds, with its sum and each holder's amount. A cap is per character, so the
        // line sums the counts alone. A currency nobody holds has no line: Wolf Marks here, never stored, and the Serpent Seal, stored
        // at zero only by a hand edit, since a read zero clears the row.
        var aria = Items.Record(1);
        aria.Values[MoneyIds.CurrencyId(28)] = Held(1_200, 2_000);                             // Poetics
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[MoneyIds.CurrencyId(28)] = Held(900, 2_000);
        kestrel.Values[MoneyIds.CurrencyId(21)] = Held(0, 200_000);                            // the Serpent Seal at zero

        var lines = Totals.Build([aria, kestrel], Built, new Settings(), Items.Now);

        var poetics = LineOf(lines, MoneyIds.CurrencyId(28));
        Assert.Equal("Allagan Tomestone of Poetics", poetics.Name);
        Assert.Equal(2_100L, poetics.Total);
        Assert.Equal([("Aria Whitewind", 1_200L), ("Kestrel Vane", 900L)], Holders(poetics));
        Assert.DoesNotContain(lines, l => l.Id == MoneyIds.CurrencyId(25) || l.Id == MoneyIds.CurrencyId(21));
    }

    [Fact]
    public void The_lines_follow_the_money_categorys_order_with_mgp_after_it_and_list_no_storage_or_retainer_inventory()
    {
        // The Money category's own order: the Currency group in item order, the tomestones in their Tomestones row's order (Poetics 1,
        // Mnemonics 3), the society currencies. Then MGP, Content's currency:29, which is included too. Storage, the retainers' levels
        // and their inventories are not currencies and are never a line, though stored.
        var aria = Items.Record(1);
        Gil(aria, 5_000);
        foreach (var (item, count) in new (uint Item, int Count)[] { (29, 250_000), (21073, 40), (28, 1_200), (21072, 12), (20, 50_000), (49, 300) })
            aria.Values[MoneyIds.CurrencyId(item)] = Held(count, 999_999);
        aria.Values[MoneyIds.Bags] = Held(135, 140);
        aria.Values[MoneyIds.RetainerId(0, MoneyIds.Slots)] = Held(80, 175);
        aria.Values[MoneyIds.RetainerId(0, MoneyIds.Level)] = new LevelValue(90, 100);

        var lines = Totals.Build([aria], Built, new Settings(), Items.Now);

        Assert.Equal(
            [Totals.CharacterGilId, Totals.RetainerGilId, Totals.AllGilId, "currency:20", "currency:21072", "currency:28", "currency:49", "currency:21073", "currency:29"],
            lines.Select(l => l.Id));
        Assert.Equal(["Storm Seal", "Venture", "Allagan Tomestone of Poetics", "Allagan Tomestone of Mnemonics", "Ixali Oaknot", "MGP"], lines.Skip(3).Select(l => l.Name));
    }

    [Fact]
    public void A_hidden_character_is_left_out_of_every_line_and_a_currency_only_it_holds_has_none()
    {
        // The totals leave out a hidden character, as the table does: it is in no sum and is no holder, and a currency only it holds
        // is not listed.
        var aria = Items.Record(1);
        Gil(aria, 1_000, 500);
        aria.Values[MoneyIds.CurrencyId(28)] = Held(1_200, 2_000);
        var momo = Items.Record(3, "Momo Lulu");
        momo.Hidden = true;
        Gil(momo, 5_000_000, 700_000);
        momo.Values[MoneyIds.CurrencyId(28)] = Held(2_000, 2_000);
        momo.Values[MoneyIds.CurrencyId(25)] = Held(20_000, 20_000);                           // Wolf Marks: Momo's alone

        var lines = Totals.Build([aria, momo], Built, new Settings(), Items.Now);

        Assert.Equal([1_000L, 500L, 1_500L, 1_200L], lines.Select(l => l.Total));
        Assert.Equal([MoneyIds.CurrencyId(28)], lines.Skip(3).Select(l => l.Id));
        Assert.All(lines, l => Assert.DoesNotContain(l.Holders, h => h.Record.ContentId == 3));
    }

    [Fact]
    public void Holders_are_listed_largest_first_and_a_stale_holder_is_counted_and_marked()
    {
        // A holder whose value may be out of date still counts in the sum and is marked, so the page dims it with its last seen. It
        // is stale when not seen for longer than the settings' threshold (seven days by default; exactly seven is not stale), or its
        // row is read at a window or place visit, "as of last visit", however recently it was seen. Holders run largest first, ties by
        // label: the three tied here run the other way by content id. The holder carries which of the two caused it, so the page's
        // words match the cause.
        var aria = Items.Record(5);
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.LastSeen = Items.Now - TimeSpan.FromDays(8);
        var momo = Items.Record(3, "Momo Lulu");
        momo.LastSeen = Items.Now - TimeSpan.FromDays(7);
        var alt = Items.Record(1, "Old Alt");
        Gil(aria, 5_000);
        Gil(kestrel, 9_000);
        Gil(momo, 5_000);
        Gil(alt, 5_000);

        var gil = LineOf(Totals.Build([alt, momo, kestrel, aria], Built, new Settings(), Items.Now), Totals.CharacterGilId);

        Assert.Equal(24_000L, gil.Total);
        Assert.Equal(
            [("Kestrel Vane", 9_000L, StaleReason.Threshold), ("Aria Whitewind", 5_000L, null), ("Momo Lulu", 5_000L, null), ("Old Alt", 5_000L, null)],
            gil.Holders.Select(h => (h.Record.Label, h.Amount, h.Stale)));
        var lenient = new Settings { StalenessDays = 10 };
        Assert.Null(LineOf(Totals.Build([kestrel], Built, lenient, Items.Now), Totals.CharacterGilId).Holders.Single().Stale);

        // A currency row read at a window visit: a test item, since every currency the game has is read at login.
        var visited = new ItemDefinition("currency:90001", Category.Money, "Currency", "Visited coin", ItemKind.Counter, ResetRule.None, 0,
            Freshness.Window("Visited window"), DoneAtCap: false);
        aria.Values[visited.Id] = Held(3, 10);
        var lines = Totals.Build([aria], new Catalog([visited]), new Settings(), Items.Now);
        Assert.Equal(StaleReason.Visit, LineOf(lines, visited.Id).Holders.Single().Stale);
        Assert.Null(LineOf(lines, Totals.CharacterGilId).Holders.Single().Stale);

        // A place-visited row is marked "as of last visit" the same way a window-visited one is.
        var placed = new ItemDefinition("currency:90002", Category.Money, "Currency", "Placed coin", ItemKind.Counter, ResetRule.None, 0,
            Freshness.Place("Some territory"), DoneAtCap: false);
        aria.Values[placed.Id] = Held(4, 10);
        var placedLines = Totals.Build([aria], new Catalog([placed]), new Settings(), Items.Now);
        Assert.Equal(StaleReason.Visit, LineOf(placedLines, placed.Id).Holders.Single().Stale);
    }

    [Fact]
    public void Sums_past_int_max_are_held_as_64_bit_numbers()
    {
        // Each stored value fits an int, gil's cap being 999,999,999 on a character and on each retainer, but three characters with
        // ten full retainers each hold 32,999,999,967, past int.MaxValue on every line.
        CharacterRecord[] records = [Items.Record(1), Items.Record(2, "Kestrel Vane"), Items.Record(3, "Momo Lulu")];
        foreach (var record in records) Gil(record, 999_999_999, Enumerable.Repeat(999_999_999, MoneyIds.RetainerSlots).ToArray());

        var lines = Totals.Build(records, Built, new Settings(), Items.Now);

        Assert.Equal([2_999_999_997L, 29_999_999_970L, 32_999_999_967L], lines.Select(l => l.Total));
        Assert.All(LineOf(lines, Totals.AllGilId).Holders, h => Assert.Equal(10_999_999_989L, h.Amount));
        Assert.Equal("32.9B", Compact.Format(LineOf(lines, Totals.AllGilId).Total));
    }

    [Fact]
    public void Roles_ignores_and_the_category_switches_do_not_change_the_totals()
    {
        // The totals report holdings, which are information, not tasks. A role switching Money off, an ignore of the gil row on the
        // character, an ignore of a currency everywhere and the Money category switched off change no line.
        var aria = Items.Record(1);
        Gil(aria, 5_000, 1_000);
        aria.Values[MoneyIds.CurrencyId(28)] = Held(1_200, 2_000);
        var before = Totals.Build([aria], Built, new Settings(), Items.Now);

        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.SetCategory(Category.Money, false, Built);
        aria.Role = mule.Id;
        aria.Ignored.Add(MoneyIds.CurrencyId(MoneyIds.Gil));
        settings.IgnoredEverywhere.Add(MoneyIds.CurrencyId(28));
        settings.CategoriesOff.Add(nameof(Category.Money));
        var after = Totals.Build([aria], Built, settings, Items.Now);

        Assert.Equal(4, after.Count);
        Assert.Equal(before.Select(l => (l.Id, l.Total)), after.Select(l => (l.Id, l.Total)));
    }

    /// <summary>The dump's totals section: with nothing held on any visible character, the text says so rather than printing nothing
    /// under its header.</summary>
    [Fact]
    public void Totals_with_nothing_held_render_as_one_line_saying_so()
    {
        Assert.Equal("(nothing held)\n", TotalsText.Render([], Items.Now));
    }
}
