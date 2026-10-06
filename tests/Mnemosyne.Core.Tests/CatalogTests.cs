using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class CatalogTests
{
    private static ItemDefinition Def(string id, Category c = Category.Story, string group = "G", int order = 0) =>
        new(id, c, group, "Name " + id, ItemKind.Done, ResetRule.None, order, Freshness.AtLogin);

    [Fact]
    public void A_catalog_finds_items_by_id_and_lists_a_category_in_order()
    {
        var catalog = new Catalog([Def("b", order: 2), Def("a", order: 1), Def("m", Category.Money)]);
        Assert.Equal("Name a", catalog.TryGet("a")!.Name);
        Assert.Null(catalog.TryGet("zzz"));
        Assert.Equal(["a", "b"], catalog.InCategory(Category.Story).Select(i => i.Id));
        Assert.Empty(Catalog.Empty.Items);
    }

    [Fact]
    public void Groups_order_by_group_order_before_their_name()
    {
        var late = new ItemDefinition("q:late", Category.Story, "Shadowbringers", "Late", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: 3);
        var early = new ItemDefinition("q:early", Category.Story, "Stormblood", "Early", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: 2);
        var catalog = new Catalog([late, early]);
        Assert.Equal(["q:early", "q:late"], catalog.InCategory(Category.Story).Select(i => i.Id));
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Catalog([Def("a"), Def("a")]));
    }

    [Fact]
    public void Record_label_prefers_the_display_name_and_name_at_world_is_derived()
    {
        var r = new CharacterRecord { ContentId = 7, Name = "Aria Whitewind", HomeWorld = "Balmung" };
        Assert.Equal("Aria Whitewind", r.Label);
        Assert.Equal("Aria Whitewind@Balmung", r.NameAtWorld);
        r.DisplayName = "Aria";
        Assert.Equal("Aria", r.Label);
    }

    [Fact]
    public void Settings_start_at_their_safe_defaults()
    {
        var s = new Settings();
        Assert.Equal(7, s.StalenessDays);
        Assert.Equal(3, s.EndingSoonDays);
        Assert.True(s.WeekliesCount);
        Assert.True(s.LoginLine);
        Assert.True(s.InfoBar);
        Assert.False(s.RemindDailyReset);
        Assert.False(s.RemindEventEnding);
        Assert.False(s.RemindWondrousTails);
        Assert.False(s.RemindHouseVisit);
        Assert.Equal(5, s.LiveReadSeconds);
        Assert.True(s.IsCategoryOn(Category.Money));
        s.CategoriesOff.Add(nameof(Category.Money));
        Assert.False(s.IsCategoryOn(Category.Money));
    }

    [Fact]
    public void A_window_item_without_an_availability_is_rejected()
    {
        var bad = new ItemDefinition("event:1:1", Category.Events, "G", "No dates", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin);
        Assert.Contains("event:1:1", Assert.Throws<ArgumentException>(() => new Catalog([bad])).Message);
        _ = new Catalog([bad with { Availability = CatalogBuilder.PastWindow }]);
    }

    [Fact]
    public void A_carry_over_flag_is_rejected_on_anything_but_a_periodic_counter()
    {
        var counter = new ItemDefinition("reset:c", Category.Resets, "G", "Counter", ItemKind.Counter, ResetRule.Weekly, 1, Freshness.AtLogin, CarriesOver: true);
        _ = new Catalog([counter]);
        _ = new Catalog([counter with { Id = "reset:d", Reset = ResetRule.Daily }]);
        Assert.Contains("reset:c", Assert.Throws<ArgumentException>(() => new Catalog([counter with { Kind = ItemKind.Done }])).Message);
        Assert.Contains("reset:c", Assert.Throws<ArgumentException>(() => new Catalog([counter with { Reset = ResetRule.None }])).Message);
        Assert.Throws<ArgumentException>(() => new Catalog([counter with { Reset = ResetRule.Accrual(ResetSchedules.Leve, 3) }]));
    }

    [Fact]
    public void A_recurrence_or_a_ready_flag_is_rejected_where_it_means_nothing()
    {
        var recurs = new Recurrence(TimeSpan.FromDays(3), TimeSpan.FromDays(7));
        var weekly = new ItemDefinition("reset:fashion-report", Category.Resets, "G", "Fashion Report", ItemKind.Done, ResetRule.Weekly, 0, Freshness.AtLogin, Recurrence: recurs);
        _ = new Catalog([weekly]);
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([weekly with { Reset = ResetRule.None }])).Message);          // no period to recur with
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([weekly with { Availability = CatalogBuilder.PastWindow }])).Message);   // fixed dates or recurring, not both
        Assert.Throws<ArgumentException>(() => new Catalog([weekly with { Recurrence = new Recurrence(TimeSpan.FromDays(7), TimeSpan.FromDays(3)) }]));            // closes before it opens
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([weekly with { Recurrence = null, ReadyWhenPassed = true }])).Message);   // only a Timer is ever ready

        // A warning window belongs to a Timer with no reset rule, the one kind Place reads it for (the house visit).
        var timer = weekly with { Kind = ItemKind.Timer, Reset = ResetRule.None, Recurrence = null, RemindWithin = TimeSpan.FromDays(15) };
        _ = new Catalog([timer]);
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([weekly with { Recurrence = null, RemindWithin = TimeSpan.FromDays(15) }])).Message);   // not a Timer
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([timer with { Reset = ResetRule.Weekly }])).Message);                               // a Timer with a reset rule would ignore it
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([timer with { RemindWithin = TimeSpan.Zero }])).Message);                          // a window of zero warns at the deadline
        Assert.Contains("reset:fashion-report", Assert.Throws<ArgumentException>(() => new Catalog([timer with { RemindWithin = TimeSpan.FromDays(-1) }])).Message);                 // nor does a negative one
    }

    [Fact]
    public void A_target_is_rejected_where_it_is_not_a_manual_counter_at_least_1()
    {
        var counter = new ItemDefinition("mine:a", Category.MyTasks, "G", "Deliver", ItemKind.Counter, ResetRule.Daily, 1, Freshness.Manual, Target: 5);
        _ = new Catalog([counter]);
        Assert.Contains("mine:a", Assert.Throws<ArgumentException>(() => new Catalog([counter with { Kind = ItemKind.Done }])).Message);          // a Done item has no number
        Assert.Contains("mine:a", Assert.Throws<ArgumentException>(() => new Catalog([counter with { Freshness = Freshness.AtLogin }])).Message);  // only a Manual counter reads it
        Assert.Contains("mine:a", Assert.Throws<ArgumentException>(() => new Catalog([counter with { Target = 0 }])).Message);                     // below 1 is not a number
    }

    [Fact]
    public void A_recurrence_window_is_computed_from_the_periods_start()
    {
        var recurs = new Recurrence(TimeSpan.FromDays(3), TimeSpan.FromDays(7));
        // Sunday 2026-09-13 12:00 UTC lies in the weekly period that began Tuesday the 8th at 08:00.
        var window = recurs.WindowFor(ResetSchedules.Weekly, Items.Now);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 8, 0, 0, TimeSpan.Zero), window.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero), window.End);
        // At the reset instant the period has rolled: the window is the coming Friday's, and the old one has just closed.
        var atReset = recurs.WindowFor(ResetSchedules.Weekly, new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero), atReset.Start);
    }
}
