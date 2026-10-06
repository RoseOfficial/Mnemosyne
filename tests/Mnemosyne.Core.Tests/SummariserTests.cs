using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

public class SummariserTests
{
    internal static CharacterRecord Aria()
    {
        var r = Items.Record();
        r.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-12");
        r.Values[Items.Allowances.Id] = new CounterValue(4, 12, Items.Now);
        r.Values[Items.Stickers.Id] = new CounterValue(7, 9, Items.Now);
        r.Values[Items.Leves.Id] = new CounterValue(40, 100, Items.Now);
        r.Values[Items.Map.Id] = new TimerValue(Items.Now + TimeSpan.FromDays(5));
        r.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(18));
        r.Values[Items.Paladin.Id] = new LevelValue(72, 100);
        r.Values[Items.Msq1.Id] = new DoneValue(true);
        r.Values[Items.Msq2.Id] = new DoneValue(false);
        r.Values[Items.Moonfire.Id] = new DoneValue(false);
        r.Values[Items.Rising.Id] = new DoneValue(true);
        r.Values[Items.Starlight.Id] = new DoneValue(false);
        return r;
    }

    [Fact]
    public void A_row_reduces_the_checklist_to_the_matrix_columns()
    {
        var row = Summariser.Summarise(Items.Catalog(), Aria(), new Settings(), Items.Now);
        Assert.Equal("Aria Whitewind", row.Label);
        Assert.Equal("Balmung", row.World);
        Assert.Equal("MSQ: Dawntrail: The First Step", row.MsqPosition);
        Assert.Equal(0, row.JobsAtCap);
        Assert.Equal(1, row.JobsUnlocked);
        Assert.Equal(1, row.EventsDone);
        Assert.Equal(3, row.EventsAvailable);
        Assert.Equal(2, row.DailiesLeft);
        Assert.Equal(1, row.WeekliesLeft);
        Assert.Equal(2, row.EndingSoon);
        Assert.Equal(5, row.Badge);
        Assert.False(row.Stale);
    }

    [Fact]
    public void Msq_position_is_the_furthest_done_block_not_the_alphabetically_last()
    {
        var stormblood = new ItemDefinition("q:sb", Category.Story, "Stormblood", "The Stormblood Finale", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, IsMsq: true, GroupOrder: 2);
        var shadowbringers = new ItemDefinition("q:shb", Category.Story, "Shadowbringers", "The First Step", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, IsMsq: true, GroupOrder: 3);
        var record = Items.Record();
        record.Values["q:sb"] = new DoneValue(true);
        record.Values["q:shb"] = new DoneValue(true);
        var row = Summariser.Summarise(new Catalog([stormblood, shadowbringers]), record, new Settings(), Items.Now);
        Assert.Equal("Shadowbringers: The First Step", row.MsqPosition);
    }

    [Fact]
    public void An_empty_catalog_gives_a_row_with_no_position()
    {
        var row = Summariser.Summarise(Catalog.Empty, Items.Record(), new Settings(), Items.Now);
        Assert.Equal("", row.MsqPosition);
        Assert.Equal(0, row.Badge);
    }

    /// <summary>A record whose readers have run: the four values that put an item in each top group. A thing to do has to have
    /// been read, so a busy character is one whose readers reported something, not one whose record is empty.</summary>
    private static CharacterRecord Busy(ulong id, string name)
    {
        var r = Items.Record(id, name);
        r.Values[Items.Roulette.Id] = new DoneValue(false);                    // Today
        r.Values[Items.Allowances.Id] = new CounterValue(0, 12, Items.Now);    // Today
        r.Values[Items.Stickers.Id] = new CounterValue(0, 9, Items.Now);       // This week
        r.Values[Items.Moonfire.Id] = new DoneValue(false);                    // Ending soon
        return r;
    }

    [Fact]
    public void Sort_orders_by_any_column_in_either_direction()
    {
        var a = Summariser.Summarise(Items.Catalog(), Aria(), new Settings(), Items.Now);
        var kestrel = Busy(2, "Kestrel Vane");
        kestrel.Values[Items.Leves.Id] = new CounterValue(100, 100, Items.Now);   // at cap: accrual wasted, one more thing to do
        var b = Summariser.Summarise(Items.Catalog(), kestrel, new Settings(), Items.Now);
        Assert.Equal(5, a.Badge);
        Assert.Equal(6, b.Badge);
        Assert.Equal(["Kestrel Vane", "Aria Whitewind"], Summariser.Sort([a, b], MatrixColumn.Badge, descending: true).Select(r => r.Label));
        Assert.Equal(["Aria Whitewind", "Kestrel Vane"], Summariser.Sort([b, a], MatrixColumn.Name, descending: false).Select(r => r.Label));
        Assert.Equal(["Kestrel Vane", "Aria Whitewind"], Summariser.Sort([a, b], MatrixColumn.Msq, descending: false).Select(r => r.Label));
    }

    [Fact]
    public void Summarising_a_supplied_checklist_matches_assembling_it()
    {
        var record = Aria();
        var checklist = Assembler.Assemble(Items.Catalog(), record, new Settings(), Items.Now);
        Assert.Equal(Summariser.Summarise(Items.Catalog(), record, new Settings(), Items.Now), Summariser.Summarise(Items.Catalog(), record, new Settings(), Items.Now, checklist));
    }

    [Fact]
    public void Events_this_year_are_the_windows_that_overlap_it_not_the_ones_that_start_in_it()
    {
        // Heavensturn's window opens on 31 December of the year before the run it is named for, so a run named 2026 starts
        // in 2025 and must be counted in both years it is open in, and in neither of them only once.
        var heavensturn = new ItemDefinition("event:238:157", Category.Events, "Heavensturn", "Heavensturn 2026", ItemKind.Done, ResetRule.Window, 157, Freshness.AtLogin,
            new Availability(new DateTimeOffset(2025, 12, 31, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 15, 15, 0, 0, TimeSpan.Zero)));
        var undatedPast = new ItemDefinition("event:246:11", Category.Events, "Moonfire Faire", "Moonfire Faire (run 1)", ItemKind.Done, ResetRule.Window, 11, Freshness.AtLogin, CatalogBuilder.PastWindow);
        var undatedUpcoming = new ItemDefinition("event:248:176", Category.Events, "All Saints' Wake", "All Saints' Wake (run 13)", ItemKind.Done, ResetRule.Window, 176, Freshness.AtLogin, CatalogBuilder.UpcomingWindow);
        var catalog = new Catalog([heavensturn, undatedPast, undatedUpcoming]);
        var record = Items.Record();
        foreach (var item in catalog.Items) record.Values[item.Id] = new DoneValue(true);

        var thisYear = Summariser.Summarise(catalog, record, new Settings(), Items.Now);                                      // 2026-09-13
        Assert.Equal(1, thisYear.EventsAvailable);
        Assert.Equal(1, thisYear.EventsDone);

        // The same occurrence is available in the year its window opens in, and the two undated sentinels never are.
        var yearItOpens = Summariser.Summarise(catalog, record, new Settings(), new DateTimeOffset(2025, 12, 31, 16, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, yearItOpens.EventsAvailable);
        Assert.Equal(0, Summariser.Summarise(new Catalog([undatedPast, undatedUpcoming]), record, new Settings(), Items.Now).EventsAvailable);
    }

    [Fact]
    public void The_events_column_counts_a_reward_counter_like_a_done_occurrence()
    {
        var catalog = new Catalog([Items.Moonfire, Items.Yokai]);
        var record = Items.Record();
        record.Values[Items.Yokai.Id] = new CounterValue(39, 39, Items.Now);
        var row = Summariser.Summarise(catalog, record, new Settings(), Items.Now, Assembler.Assemble(catalog, record, new Settings(), Items.Now));
        Assert.Equal(2, row.EventsAvailable);
        Assert.Equal(1, row.EventsDone);
    }

    [Fact]
    public void Ties_break_by_label_ascending_in_both_directions()
    {
        var aria = Summariser.Summarise(Items.Catalog(), Aria(), new Settings(), Items.Now);                       // badge 5
        var momo = Summariser.Summarise(Items.Catalog(), Busy(3, "Momo Lulu"), new Settings(), Items.Now);        // badge 5: roulette, allowances, stickers, Moonfire
        var kestrel = Busy(2, "Kestrel Vane");
        kestrel.Values[Items.Leves.Id] = new CounterValue(100, 100, Items.Now);
        var k = Summariser.Summarise(Items.Catalog(), kestrel, new Settings(), Items.Now);                        // badge 6
        Assert.Equal(5, momo.Badge);
        Assert.Equal(["Kestrel Vane", "Aria Whitewind", "Momo Lulu"], Summariser.Sort([momo, k, aria], MatrixColumn.Badge, descending: true).Select(r => r.Label));
        Assert.Equal(["Aria Whitewind", "Momo Lulu", "Kestrel Vane"], Summariser.Sort([momo, k, aria], MatrixColumn.Badge, descending: false).Select(r => r.Label));
    }

    private static MatrixRow Row(string label, int jobsAtCap = 0, int eventsDone = 0, int dailies = 0, int weeklies = 0, int soon = 0, int lastSeenDays = 0) =>
        new(1, label, "Balmung", "", jobsAtCap, 10, eventsDone, 10, dailies, weeklies, soon, Items.Now - TimeSpan.FromDays(lastSeenDays), false, 0);

    [Fact]
    public void Sort_orders_by_each_numeric_column()
    {
        void Both(MatrixColumn column, MatrixRow low, MatrixRow high)
        {
            Assert.Equal(["high", "low"], Summariser.Sort([low, high], column, descending: true).Select(r => r.Label));
            Assert.Equal(["low", "high"], Summariser.Sort([high, low], column, descending: false).Select(r => r.Label));
        }
        Both(MatrixColumn.Jobs, Row("low", jobsAtCap: 1), Row("high", jobsAtCap: 9));
        Both(MatrixColumn.Events, Row("low", eventsDone: 1), Row("high", eventsDone: 9));
        Both(MatrixColumn.Dailies, Row("low", dailies: 1), Row("high", dailies: 9));
        Both(MatrixColumn.Weeklies, Row("low", weeklies: 1), Row("high", weeklies: 9));
        Both(MatrixColumn.EndingSoon, Row("low", soon: 1), Row("high", soon: 9));
    }

    [Fact]
    public void Sort_orders_by_last_seen()
    {
        var recent = Row("high", lastSeenDays: 0);
        var old = Row("low", lastSeenDays: 30);
        Assert.Equal(["high", "low"], Summariser.Sort([old, recent], MatrixColumn.LastSeen, descending: true).Select(r => r.Label));
        Assert.Equal(["low", "high"], Summariser.Sort([recent, old], MatrixColumn.LastSeen, descending: false).Select(r => r.Label));
    }

    [Fact]
    public void The_role_column_reads_the_roles_name_unknown_role_or_nothing_and_sorts_by_that_text_then_by_label()
    {
        // The name, "unknown role" for an id this PC does not have, or nothing. The column sorts like the others, by what it shows,
        // ignoring case, and ties break by label ascending in both directions. One name is lower case, "alt", so a case-sensitive
        // sort, which puts "Mule" before "alt", fails here.
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        var alt = settings.Roles.Create("alt")!;
        MatrixRow RowOf(ulong id, string name, string? role)
        {
            var record = Items.Record(id, name);
            record.Role = role;
            return Summariser.Summarise(Items.Catalog(), record, settings, Items.Now);
        }
        var none = RowOf(1, "Aria Whitewind", null);
        var unknown = RowOf(2, "Kestrel Vane", "gone");
        var momo = RowOf(3, "Momo Lulu", mule.Id);
        var old = RowOf(4, "Old Alt", mule.Id);
        var second = RowOf(5, "Second Alt", alt.Id);
        Assert.Equal(["", Summariser.UnknownRole, "Mule", "Mule", "alt"], new[] { none, unknown, momo, old, second }.Select(r => r.Role));
        Assert.Equal("unknown role", Summariser.UnknownRole);
        Assert.Equal(RoleList.UnknownName, Summariser.UnknownRole);                   // the word no role may take (RoleTests)

        MatrixRow[] rows = [momo, second, unknown, none, old];
        Assert.Equal(["Aria Whitewind", "Second Alt", "Momo Lulu", "Old Alt", "Kestrel Vane"],
            Summariser.Sort(rows, MatrixColumn.Role, descending: false).Select(r => r.Label));
        Assert.Equal(["Kestrel Vane", "Momo Lulu", "Old Alt", "Second Alt", "Aria Whitewind"],
            Summariser.Sort(rows, MatrixColumn.Role, descending: true).Select(r => r.Label));
    }

    /// <summary>Stores gc:current as the Jobs reader does at login, from the checked-in GrandCompany rows; row 0 is no company.</summary>
    private static void Company(CharacterRecord record, uint row) =>
        RecordUpdater.Merge(record, Category.Jobs,
            [Readings.CurrentCompany(TotalsTests.Built.TryGet(CatalogBuilder.CurrentCompanyId)!, TotalsTests.Rows.GrandCompanies.FirstOrDefault(c => c.RowId == row))], Items.Now);

    [Theory]
    [InlineData(1u, "Maelstrom", 20u, "Storm Seal")]
    [InlineData(2u, "Order of the Twin Adder", 21u, "Serpent Seal")]
    [InlineData(3u, "Immortal Flames", 22u, "Flame Seal")]
    public void Each_grand_company_reads_its_own_seal_item(uint company, string companyName, uint seal, string sealName)
    {
        // One mapping from a GrandCompany row to its seal item (MoneyIds), checked for each company against the checked-in rows'
        // names, and the inverse of SealCompany, which the Money reader uses. Row 0, no company, and any other row have none.
        Assert.Equal(seal, MoneyIds.SealItem(company));
        Assert.Equal((byte)company, MoneyIds.SealCompany(seal));
        Assert.Equal(companyName, TotalsTests.Rows.GrandCompanies.Single(c => c.RowId == company).Name);
        Assert.Equal(sealName, TotalsTests.Rows.Currencies.Single(c => c.ItemId == seal).Name);
        Assert.Null(MoneyIds.SealItem(0));
        Assert.Null(MoneyIds.SealItem(4));
    }

    [Fact]
    public void The_current_grand_company_is_a_named_row_in_jobs_that_is_never_attention_and_no_company_clears_it()
    {
        // The Jobs reader stores the current Grand Company at login as gc:current, first in Jobs' Grand Company group. The value
        // is a Done value that is never done, whose period key is the company's GrandCompany row. The character's own name for the
        // row is "Grand Company: <company>", as a retainer row is named. It reads as that name alone, with no value text, in no top
        // group. No company clears it, name and all, and the row is then blank and not listed. A stored row outside 1 to 3, which
        // no build writes, names no company.
        var item = TotalsTests.Built.TryGet(CatalogBuilder.CurrentCompanyId)!;
        Assert.Equal((Category.Jobs, "Grand Company", "Grand Company", ItemKind.Done, ResetKind.None, 0), (item.Category, item.Group, item.Name, item.Kind, item.Reset.Kind, item.Order));
        Assert.Equal((FreshnessKind.AtLogin, true, 8), (item.Freshness.Kind, item.HiddenWhenBlank, item.GroupOrder));
        var record = Items.Record();

        Company(record, 3);

        Assert.Equal(new DoneValue(false, "3"), record.Values[CatalogBuilder.CurrentCompanyId]);
        Assert.Equal("Grand Company: Immortal Flames", record.ItemNames[CatalogBuilder.CurrentCompanyId]);
        Assert.Equal(3u, Holdings.CurrentCompany(record));
        var checklist = Assembler.Assemble(TotalsTests.Built, record, new Settings(), Items.Now);
        var row = checklist.Sections.Single(s => s.Category == Category.Jobs).Groups.Single(g => g.Name == "Grand Company").Entries[0];
        Assert.Equal(("Grand Company: Immortal Flames", EntryState.NotDone, ""), (row.Name, row.State, row.Display));
        Assert.DoesNotContain(checklist.Today.Concat(checklist.ThisWeek).Concat(checklist.EndingSoon).Concat(checklist.Running), e => e.Item.Id == item.Id);

        Company(record, 0);

        Assert.False(record.Values.ContainsKey(item.Id));
        Assert.False(record.ItemNames.ContainsKey(item.Id));
        Assert.Null(Holdings.CurrentCompany(record));
        Assert.DoesNotContain(Assembler.Assemble(TotalsTests.Built, record, new Settings(), Items.Now).Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries),
            e => e.Item.Id == item.Id);
        record.Values[item.Id] = new DoneValue(false, "4");
        Assert.Null(Holdings.CurrentCompany(record));
    }

    [Fact]
    public void The_gil_column_reads_own_gil_with_the_retainers_in_brackets_and_no_role_ignore_or_switch_changes_a_holding_column()
    {
        // Gil is the character's own, currency:1, with its retainers' sum in brackets, the bracket left out while they hold none.
        // It is blank when no gil of its own is stored, whatever its retainers hold. Holdings are information. None of Gil, Bag
        // space and Company seals changes under any of these:
        // - a role switching Money and Jobs off;
        // - an ignore of the gil, bag, seal or Grand Company row;
        // - the Money category switched off.
        MatrixRow RowOf(CharacterRecord record, Settings? settings = null) => Summariser.Summarise(TotalsTests.Built, record, settings ?? new Settings(), Items.Now);
        var aria = Items.Record(1);
        TotalsTests.Gil(aria, 1_234_567, 100_000, 50_000, 10_000);
        aria.Values[MoneyIds.Bags] = TotalsTests.Held(120, 140);
        Company(aria, 1);
        aria.Values[MoneyIds.CurrencyId(20)] = TotalsTests.Held(26_104, 50_000);
        var kestrel = Items.Record(2, "Kestrel Vane");
        TotalsTests.Gil(kestrel, 8_500, 0);
        var momo = Items.Record(3, "Momo Lulu");
        momo.Values[MoneyIds.RetainerId(0, MoneyIds.RetainerGil)] = TotalsTests.Held(40_000, 999_999_999);   // no gil of its own stored

        var a = RowOf(aria);
        Assert.Equal(1_234_567L, a.Gil);
        Assert.Equal(160_000L, a.RetainerGil);
        Assert.Equal(("1.2M (+160k)", "20 free", "26.1k / 50k"), (a.GilText, a.BagsText, a.SealsText));
        Assert.Equal("8,500", RowOf(kestrel).GilText);
        Assert.Null(RowOf(momo).Gil);
        Assert.Equal("", RowOf(momo).GilText);

        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.SetCategory(Category.Money, false, TotalsTests.Built);
        mule.SetCategory(Category.Jobs, false, TotalsTests.Built);
        aria.Role = mule.Id;
        aria.Ignored.Add(MoneyIds.CurrencyId(MoneyIds.Gil));
        settings.IgnoredEverywhere.Add(MoneyIds.Bags);
        settings.IgnoredEverywhere.Add(MoneyIds.CurrencyId(20));
        settings.IgnoredEverywhere.Add(CatalogBuilder.CurrentCompanyId);
        settings.CategoriesOff.Add(nameof(Category.Money));
        var switched = RowOf(aria, settings);
        Assert.Equal(("1.2M (+160k)", "20 free", "26.1k / 50k"), (switched.GilText, switched.BagsText, switched.SealsText));
    }

    [Fact]
    public void Ventures_ready_counts_the_retainers_whose_venture_has_ended_over_those_with_one_and_leaves_out_an_ignored_or_off_slot()
    {
        // "n of m", m the retainers with a venture timer stored and n those whose deadline has passed, a deadline of now included.
        // Blank with no timer stored. A slot ignored on the character or everywhere, or off for its role, is in neither count, as
        // in every matrix count. With Resets switched off in the settings it is blank, as Dailies left is. The ids are the
        // builder's, reset:venture:<slot>, now named once in MoneyIds.
        Assert.Equal("reset:venture:3", MoneyIds.VentureId(3));
        Assert.Equal("Venture: retainer 4", TotalsTests.Built.TryGet(MoneyIds.VentureId(3))!.Name);
        var aria = Items.Record(1);
        aria.Values[MoneyIds.VentureId(0)] = new TimerValue(Items.Now - TimeSpan.FromHours(2));      // ended
        aria.Values[MoneyIds.VentureId(1)] = new TimerValue(Items.Now);                              // ends now: ready
        aria.Values[MoneyIds.VentureId(2)] = new TimerValue(Items.Now + TimeSpan.FromHours(5));      // running
        aria.Values[MoneyIds.VentureId(3)] = new TimerValue(Items.Now - TimeSpan.FromHours(1));      // ended
        var settings = new Settings();
        MatrixRow Read() => Summariser.Summarise(TotalsTests.Built, aria, settings, Items.Now);

        Assert.Equal((3, 4, "3 of 4"), (Read().VenturesReady, Read().Ventures, Read().VenturesText));
        aria.Ignored.Add(MoneyIds.VentureId(3));
        Assert.Equal("2 of 3", Read().VenturesText);
        settings.IgnoredEverywhere.Add(MoneyIds.VentureId(2));
        Assert.Equal("2 of 2", Read().VenturesText);
        settings.CategoriesOff.Add(nameof(Category.Resets));                                         // Resets switched off in the settings
        Assert.Equal((0, ""), (Read().Ventures, Read().VenturesText));
        settings.CategoriesOff.Clear();
        Assert.Equal("2 of 2", Read().VenturesText);
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(TotalsTests.Built.TryGet(MoneyIds.VentureId(0))!.GroupKey);                     // the Timers group of Resets
        aria.Role = mule.Id;
        Assert.Equal((0, ""), (Read().Ventures, Read().VenturesText));
        Assert.Equal("", Summariser.Summarise(TotalsTests.Built, Items.Record(2, "Kestrel Vane"), new Settings(), Items.Now).VenturesText);
    }

    [Fact]
    public void Bag_space_reads_the_free_slots_in_the_four_bags_and_is_low_at_five_or_fewer()
    {
        // "n free" from storage:bags, stored as used over the bags' size; drawn red at 5 free or fewer; blank with no bag value stored.
        MatrixRow RowOf(int? used)
        {
            var record = Items.Record();
            if (used is { } u) record.Values[MoneyIds.Bags] = TotalsTests.Held(u, 140);
            return Summariser.Summarise(TotalsTests.Built, record, new Settings(), Items.Now);
        }

        Assert.Equal(5, RowOf(135).BagsFree);
        Assert.Equal("5 free", RowOf(135).BagsText);
        Assert.True(RowOf(135).BagsLow);
        Assert.Equal("6 free", RowOf(134).BagsText);
        Assert.False(RowOf(134).BagsLow);
        Assert.Equal("0 free", RowOf(140).BagsText);
        Assert.True(RowOf(140).BagsLow);
        Assert.Null(RowOf(null).BagsFree);
        Assert.Equal("", RowOf(null).BagsText);
        Assert.False(RowOf(null).BagsLow);
        Assert.Equal(5, MatrixRow.LowBagSpace);
    }

    [Fact]
    public void Company_seals_read_the_current_companys_seal_row_and_are_blank_until_the_company_is_known()
    {
        // The column reads gc:current, the current Grand Company read at login, and shows that company's seal row over its stored
        // cap. A former company keeps its rank, so its row is stored over a real cap too. However many seals it holds, it is not
        // the column, and it is still its own Totals line. Holding none of the current company's seals reads "0": a read zero
        // clears the row, its cap with it. The column is blank in three cases:
        // - no gc:current stored yet (a record until its first login on this build);
        // - no company;
        // - a company known but Money not read yet.
        MatrixRow RowOf(CharacterRecord record) => Summariser.Summarise(TotalsTests.Built, record, new Settings(), Items.Now);
        var aria = Items.Record(1);
        Company(aria, 1);                                                                          // the Maelstrom
        aria.Values[MoneyIds.CurrencyId(20)] = TotalsTests.Held(26_104, 50_000);                   // Storm Seals, this character's
        aria.Values[MoneyIds.CurrencyId(22)] = TotalsTests.Held(80_000, 90_000);                   // Flame Seals left from a former company, over its rank's cap

        Assert.Equal(26_104, RowOf(aria).Seals);
        Assert.Equal(50_000, RowOf(aria).SealCap);
        Assert.Equal("26.1k / 50k", RowOf(aria).SealsText);
        Assert.Contains(Totals.Build([aria], TotalsTests.Built, new Settings(), Items.Now), l => l.Id == MoneyIds.CurrencyId(22) && l.Total == 80_000);

        Company(aria, 3);                                                                          // moved to the Immortal Flames
        Assert.Equal("80k / 90k", RowOf(aria).SealsText);
        aria.Values.Remove(MoneyIds.CurrencyId(22));                                               // spent them all: the read zero cleared the row
        Assert.Equal(0, RowOf(aria).Seals);
        Assert.Equal("0", RowOf(aria).SealsText);

        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[MoneyIds.CurrencyId(20)] = TotalsTests.Held(3_000, 10_000);                 // seals, but no gc:current stored yet
        Assert.Null(RowOf(kestrel).Seals);
        Assert.Equal("", RowOf(kestrel).SealsText);
        Company(kestrel, 0);                                                                       // in no company
        Assert.Null(RowOf(kestrel).Seals);
        Assert.Equal("", RowOf(kestrel).SealsText);

        var momo = new CharacterRecord { ContentId = 3, Name = "Momo Lulu", LastSeen = Items.Now };  // nothing read but Jobs
        Company(momo, 2);
        Assert.Null(RowOf(momo).Seals);
        Assert.Equal("", RowOf(momo).SealsText);
    }

    [Fact]
    public void Each_new_column_sorts_by_its_value_with_a_blank_last_in_both_directions()
    {
        // Gil sorts by own plus retainers' (past int.MaxValue here), ventures ready by the ready count, company seals by the count.
        // In every column a blank sorts last, both ways, even one whose other fields would sort first; a zero that is stored is
        // not a blank. Each "high" row is lower by the key a wrong sort would use: own gil alone, or the ventures stored rather
        // than those ready. Bag space sorts by how full the bags are, fullest first descending (the table's default), so "high"
        // here is the fuller bag (fewer free slots).
        void Check(MatrixColumn column, MatrixRow low, MatrixRow high, MatrixRow blank)
        {
            Assert.Equal(["high", "low", "blank"], Summariser.Sort([blank, low, high], column, descending: true).Select(r => r.Label));
            Assert.Equal(["low", "high", "blank"], Summariser.Sort([high, blank, low], column, descending: false).Select(r => r.Label));
        }
        Check(MatrixColumn.Gil,
            Row("low") with { Gil = 999_999_999 },
            Row("high") with { Gil = 1_000, RetainerGil = 9_999_999_990 },                          // 10,000,000,990: own gil alone would sort it low
            Row("blank") with { RetainerGil = 50_000_000_000 });
        Check(MatrixColumn.Ventures, Row("low") with { VenturesReady = 0, Ventures = 5 }, Row("high") with { VenturesReady = 3, Ventures = 3 }, Row("blank"));
        Check(MatrixColumn.Bags, Row("low") with { BagsFree = 60 }, Row("high") with { BagsFree = 0 }, Row("blank"));
        Check(MatrixColumn.Seals, Row("low") with { Seals = 0 }, Row("high") with { Seals = 89_000, SealCap = 90_000 }, Row("blank"));
    }

    [Fact]
    public void The_four_columns_are_blank_where_nothing_is_stored_and_come_after_the_existing_ten()
    {
        // With the new columns blank, every existing column reads as before (A_row_reduces_the_checklist_to_the_matrix_columns pins
        // those). The four come after To do, so no existing member moves. No golden prints the matrix, so none moves.
        var row = Summariser.Summarise(Items.Catalog(), Aria(), new Settings(), Items.Now);
        Assert.Null(row.Gil);
        Assert.Equal(0L, row.RetainerGil);
        Assert.Equal((0, 0), (row.VenturesReady, row.Ventures));
        Assert.Null(row.BagsFree);
        Assert.Null(row.Seals);
        Assert.Equal(["", "", "", ""], new[] { row.GilText, row.VenturesText, row.BagsText, row.SealsText });
        Assert.False(row.BagsLow);
        Assert.Equal(
            [MatrixColumn.Name, MatrixColumn.Role, MatrixColumn.Msq, MatrixColumn.Jobs, MatrixColumn.Events, MatrixColumn.Dailies, MatrixColumn.Weeklies,
             MatrixColumn.EndingSoon, MatrixColumn.LastSeen, MatrixColumn.Badge, MatrixColumn.Gil, MatrixColumn.Ventures, MatrixColumn.Bags, MatrixColumn.Seals],
            Enum.GetValues<MatrixColumn>());
    }
}
