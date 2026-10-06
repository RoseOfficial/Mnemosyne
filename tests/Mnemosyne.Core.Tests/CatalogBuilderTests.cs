using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class CatalogBuilderTests
{
    private static readonly CatalogRows Rows = new FixtureCatalogSource().Load();
    private static readonly CatalogExtras Extras = CatalogExtras.Load();
    private static readonly Catalog Built = CatalogBuilder.Build(Rows, Extras);

    [Fact]
    public void The_fixture_is_the_current_game_and_holds_the_expected_rows()
    {
        Assert.Equal("2026.09.15.0000.0000", Rows.GameVersion);
        Assert.Equal(1050, Rows.Quests.Count(q => q.Section is 0 or 1));
        Assert.Equal(43, Rows.ClassJobs.Count);
        Assert.Equal(31, Rows.AetherCurrentZones.Count);
        Assert.Equal(20, Rows.BeastTribes.Count);
        Assert.Equal(3, Rows.GrandCompanies.Count);
        Assert.True(Rows.AozActionCount > 100);
    }

    [Fact]
    public void Msq_is_fifteen_blocks_in_story_order_with_the_journal_names()
    {
        var msq = Built.InCategory(Category.Story).Where(i => i.IsMsq).ToList();
        Assert.Equal(1050, msq.Count);
        var groups = msq.Select(i => (i.GroupOrder, i.Group)).Distinct().OrderBy(g => g.GroupOrder).ToList();
        Assert.Equal(15, groups.Count);
        Assert.Equal((1, "Seventh Umbral Era"), groups[0]);
        Assert.Equal((13, "Dawntrail"), groups[12]);
        Assert.Equal((15, "Winter's Prelude"), groups[14]);
        Assert.Equal("Close to Home", msq.First().Name);
        Assert.Equal("Windborne", msq.Last().Name);
        Assert.All(msq, i => Assert.StartsWith("quest:", i.Id));
    }

    [Fact]
    public void Story_groups_read_in_the_planned_order()
    {
        var orders = Built.InCategory(Category.Story).Select(i => (i.GroupOrder, i.Group)).Distinct().OrderBy(g => g.GroupOrder).ToList();
        Assert.Contains((100, "Gladiator Quests"), orders);
        Assert.Contains((200, "Tank Role Quests (Shadowbringers)"), orders);
        Assert.Contains(orders, g => g.GroupOrder is >= 300 and < 400 && g.Group == "Hildibrand Adventures");
        Assert.Contains((418, "The Crystal Tower (alliance raid)"), orders);
        Assert.Contains((424, "The Four Lords"), orders);
        // Category 26 carries no "Chronicles of a New Era - " prefix to strip; the kind suffix still applies.
        Assert.Contains((426, "YoRHa: Dark Apocalypse (alliance raid)"), orders);
        Assert.Contains(orders, g => g.GroupOrder is >= 500 and < 600 && g.Group == "Zodiac Weapons");
        Assert.Contains((600, "Feature unlocks"), orders);
        Assert.Contains((700, "Aether currents"), orders);
        Assert.Contains((800, "Allied societies"), orders);
        Assert.All(orders, g => Assert.True(g.GroupOrder is (>= 1 and <= 15) or (>= 100 and <= 199) or (>= 200 and <= 299) or (>= 300 and <= 399) or (>= 400 and <= 499) or (>= 500 and <= 599) or 600 or 700 or 800, $"{g.Group} at {g.GroupOrder}"));
    }

    [Fact]
    public void Feature_unlocks_aether_currents_and_tribes_are_shaped_as_planned()
    {
        var unlocks = Built.InCategory(Category.Story).Where(i => i.Group == "Feature unlocks").ToList();
        Assert.Equal(17, unlocks.Count);
        Assert.Contains(unlocks, i => i.Id == "unlock:66957" && i.Name == "A Self-improving Man (Glamour)");
        Assert.NotNull(Built.TryGet("quest:66045"));   // the retainer unlock is also an MSQ quest; both items exist under different ids

        var zones = Built.InCategory(Category.Story).Where(i => i.Group == "Aether currents").ToList();
        Assert.Equal(31, zones.Count);
        Assert.All(zones, z => { Assert.Equal(ItemKind.Counter, z.Kind); Assert.True(z.DoneAtCap); Assert.StartsWith("aether:", z.Id); });

        var tribes = Built.InCategory(Category.Story).Where(i => i.Group == "Allied societies").ToList();
        Assert.Equal(20, tribes.Count);
        Assert.All(tribes, t => Assert.Equal(ItemKind.Level, t.Kind));
    }

    [Fact]
    public void Events_are_one_item_per_occurrence_dated_from_the_hand_file_or_shown_as_past()
    {
        var events = Built.InCategory(Category.Events).ToList();
        var moonfire2026 = events.Single(i => i.Group == "Moonfire Faire" && i.Order == 174);
        Assert.Equal("Moonfire Faire", moonfire2026.Group);
        Assert.Equal("Moonfire Faire 2026", moonfire2026.Name);
        Assert.Equal(ResetKind.Window, moonfire2026.Reset.Kind);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero), moonfire2026.Availability!.Start);
        Assert.Equal(174, moonfire2026.Order);

        var undated = events.Single(i => i.Group == "Moonfire Faire" && i.Order == 11);
        // Festival 11 is Moonfire Faire's lowest festival id, so it is the genre's first (undated) run, and it is older
        // than the genre's newest dated run (174), so it reads as past.
        Assert.Equal("Moonfire Faire (run 1)", undated.Name);
        Assert.Equal(CatalogBuilder.PastWindow, undated.Availability);

        // Genre 247 ("Rising Events" in the sheets) is the event "The Rising".
        Assert.Contains(events, i => i.Group == "The Rising" && i.Name == "The Rising 2026");

        Assert.All(events.Where(i => !i.Id.EndsWith(":0")), i => Assert.NotNull(i.Availability));
        Assert.All(events.Where(i => i.Id.EndsWith(":0")), i => { Assert.Null(i.Availability); Assert.Equal(ResetKind.None, i.Reset.Kind); });
        Assert.Equal(events.Select(i => i.Group).Distinct().Count(), events.Select(i => (i.Group, i.GroupOrder)).Distinct().Count());
    }

    [Fact]
    public void A_dated_window_attaches_only_to_its_own_event()
    {
        // Genres 246 (Moonfire Faire) and 247 (The Rising) both carry raw festival id 11 in the sheets; a hand file that
        // dates only the Moonfire Faire occurrence must not also attach that window to The Rising's.
        var moonfireOnly = new CatalogExtras(
            [new EventOccurrence(246, 11, "Moonfire Faire", 2013, new DateTimeOffset(2013, 8, 1, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2013, 8, 15, 15, 0, 0, TimeSpan.Zero))],
            [], [], 100, [new LimitedJobCap(36, 80)], new ResetCaps(100, 12, 9, 9, 3, 3),
            Extras.RouletteGates, [65756], 40, Extras.UnlockQuests, Extras.FashionReport, Extras.Troves, Extras.ContentUnlockQuests, Extras.VariantSeries, Extras.PlaceUnlockQuests, Extras.ContentMaxima, Extras.HouseVisit, Extras.CollectionUnlockQuests, Extras.Storage);
        var events = CatalogBuilder.Build(Rows, moonfireOnly).InCategory(Category.Events).ToList();

        var moonfire = events.Single(i => i.Id == "event:246:11");
        Assert.Equal("Moonfire Faire 2013", moonfire.Name);
        Assert.Equal(new DateTimeOffset(2013, 8, 1, 8, 0, 0, TimeSpan.Zero), moonfire.Availability!.Start);

        // The Rising has no dated occurrence at all in this hand file, so its festival 11 reads as past, not upcoming.
        var rising = events.Single(i => i.Id == "event:247:11");
        Assert.Equal(CatalogBuilder.PastWindow, rising.Availability);
    }

    [Fact]
    public void An_undated_run_newer_than_the_last_dated_one_is_upcoming()
    {
        // All Saints' Wake (genre 248) is dated through 2025 (festival 164); the sheets already carry an undated 2026
        // occurrence (festival 176), which is newer than the last dated one and so is upcoming, not past.
        var newest = Built.InCategory(Category.Events).Where(i => i.Group == "All Saints' Wake").OrderByDescending(i => i.Order).First();
        Assert.Equal(CatalogBuilder.UpcomingWindow, newest.Availability);
        Assert.StartsWith("All Saints' Wake (run ", newest.Name);

        var oldestUndated = Built.InCategory(Category.Events).Single(i => i.Group == "Moonfire Faire" && i.Order == 11);
        Assert.Equal(CatalogBuilder.PastWindow, oldestUndated.Availability);
    }

    [Fact]
    public void Event_items_are_exactly_the_occurrences_the_builder_exposes()
    {
        var occurrences = CatalogBuilder.EventOccurrences(Rows);
        Assert.All(occurrences, o => Assert.NotEmpty(o.QuestIds));
        var occurrenceIds = occurrences.Select(o => $"event:{o.Genre}:{o.Festival}").ToHashSet();
        var itemIds = Built.InCategory(Category.Events).Select(i => i.Id).Where(id => !id.StartsWith("event:trove:", StringComparison.Ordinal)).ToHashSet();
        Assert.Equal(occurrenceIds, itemIds);
    }

    [Fact]
    public void A_hand_file_unlock_missing_from_the_rows_is_reported()
    {
        var rowsWithoutGlamourQuest = Rows with { Quests = Rows.Quests.Where(q => q.RowId != 66957).ToList() };
        var warnings = new List<string>();
        var built = CatalogBuilder.Build(rowsWithoutGlamourQuest, Extras, warnings.Add);
        var warning = Assert.Single(warnings);
        Assert.Contains("66957", warning);
        Assert.Equal(16, built.InCategory(Category.Story).Count(i => i.Group == "Feature unlocks"));
    }

    [Fact]
    public void Jobs_are_one_item_per_experience_index_named_after_the_job()
    {
        var jobs = Built.InCategory(Category.Jobs).Where(i => i.Id.StartsWith("job:")).ToList();
        var indices = Rows.ClassJobs.Where(j => j.ExpArrayIndex >= 0).Select(j => j.ExpArrayIndex).Distinct().Count();
        Assert.Equal(indices, jobs.Count);
        Assert.Contains(jobs, j => j.Name == "Paladin" && j.Group == "Tanks");
        Assert.DoesNotContain(jobs, j => j.Name == "Gladiator");
        Assert.Contains(jobs, j => j.Name == "Carpenter" && j.Group == "Crafters");
        Assert.Contains(jobs, j => j.Name == "Blue Mage" && j.Group == "Limited jobs");
        // Experience index 18 is shared by Arcanist, Summoner and Scholar; name it after the two jobs, not the base class.
        Assert.Contains(jobs, j => j.Name == "Summoner / Scholar" && j.Group == "Ranged DPS");
        Assert.All(jobs, j => Assert.Equal(ItemKind.Level, j.Kind));

        var spells = Built.TryGet("blu:spells")!;
        Assert.Equal(ItemKind.Counter, spells.Kind);
        Assert.True(spells.DoneAtCap);
        Assert.Equal(3, Built.InCategory(Category.Jobs).Count(i => i.Id.StartsWith("gc:rank:")));
        Assert.Null(Built.TryGet("gc:seals"));                             // the seals moved to Money
        Assert.False(Built.TryGet("currency:20")!.DoneAtCap);             // where each is a held counter, never done at its cap
    }

    [Fact]
    public void A_quest_name_drops_the_journal_icon_glyph_in_front_of_it()
    {
        // Row 69429's name in the sheets is "\uE0BF Ever Skyward": the leading private-use glyph is the journal's own
        // quest-type icon, drawn beside the name rather than part of it. Forty-two rows carry one.
        Assert.Equal("Ever Skyward", Built.TryGet("quest:69429")!.Name);
        Assert.All(Built.InCategory(Category.Story), i => Assert.True(i.Name.Length > 0 && i.Name[0] < '\uE000', i.Name));
    }

    [Fact]
    public void Two_groups_with_one_name_and_two_orders_are_rejected()
    {
        var a = new ItemDefinition("x:1", Category.Story, "Same", "A", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: 1);
        var b = new ItemDefinition("x:2", Category.Story, "Same", "B", ItemKind.Done, ResetRule.None, 2, Freshness.AtLogin, GroupOrder: 2);
        var ex = Assert.Throws<InvalidOperationException>(() => CatalogBuilder.Validate([a, b]));
        Assert.Contains("Same", ex.Message);
        CatalogBuilder.Validate([a, b with { GroupOrder = 1 }]);
    }

    [Fact]
    public void Every_window_item_has_an_availability()
    {
        Assert.All(Built.Items.Where(i => i.Reset.Kind == ResetKind.Window), i => Assert.NotNull(i.Availability));
    }

    [Fact]
    public void A_reused_quest_return_is_marked_and_nothing_else_is()
    {
        var yokai = Built.InCategory(Category.Events).Single(i => i.Id == "event:252:39");
        Assert.True(yokai.ReusedQuest);
        Assert.Equal("Gather One, Gather All! (Yo-kai Watch) 2026", yokai.Name);

        // Final Fantasy XVI has run once, on its own quests, so a completion is that run's own.
        Assert.False(Built.InCategory(Category.Events).Single(i => i.Id == "event:252:147").ReusedQuest);

        var marked = Built.InCategory(Category.Events).Where(i => i.ReusedQuest).Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(["event:252:148", "event:252:30", "event:252:39", "event:252:7", "event:252:84"], marked);
        Assert.All(Built.InCategory(Category.Story).Concat(Built.InCategory(Category.Jobs)), i => Assert.False(i.ReusedQuest));
    }

    [Fact]
    public void A_reward_occurrence_is_a_counter_gated_on_the_achievements_window_only_when_it_lists_one()
    {
        var yokai = Built.TryGet("event:252:39")!;
        Assert.Equal(ItemKind.Counter, yokai.Kind);
        Assert.Equal(Freshness.Window("Achievements"), yokai.Freshness);
        Assert.True(yokai.DoneAtCap);
        Assert.Equal(ResetKind.Window, yokai.Reset.Kind);
        Assert.True(yokai.ReusedQuest);

        var ffxv = Built.TryGet("event:252:84")!;
        Assert.Equal(ItemKind.Counter, ffxv.Kind);
        Assert.Equal(Freshness.AtLogin, ffxv.Freshness);

        var moonfire = Built.TryGet("event:246:174")!;
        Assert.Equal(ItemKind.Done, moonfire.Kind);
        Assert.Equal(Freshness.AtLogin, moonfire.Freshness);
        Assert.All(Built.InCategory(Category.Events).Where(i => i.Kind == ItemKind.Counter), i => Assert.NotNull(i.Availability));
    }

    [Fact]
    public void A_constructed_reward_list_turns_only_its_own_occurrence_into_a_counter()
    {
        var extras = new CatalogExtras(
            [new EventOccurrence(252, 84, "Final Fantasy XV collaboration", 2026, new(2026, 9, 13, 8, 0, 0, TimeSpan.Zero), new(2026, 10, 18, 14, 59, 0, TimeSpan.Zero), true, [new RewardSignal(RewardKind.Mount, 133)]),
             new EventOccurrence(252, 39, "Yo-kai Watch collaboration", 2026, new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero), new(2026, 10, 5, 14, 59, 0, TimeSpan.Zero), true, [new RewardSignal(RewardKind.Minion, 1), new RewardSignal(RewardKind.Achievement, 2)])],
            [], [], 100, [new LimitedJobCap(36, 80)], new ResetCaps(100, 12, 9, 9, 3, 3),
            Extras.RouletteGates, [65756], 40, Extras.UnlockQuests, Extras.FashionReport, Extras.Troves, Extras.ContentUnlockQuests, Extras.VariantSeries, Extras.PlaceUnlockQuests, Extras.ContentMaxima, Extras.HouseVisit, Extras.CollectionUnlockQuests, Extras.Storage);
        var built = CatalogBuilder.Build(Rows, extras);
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin), (built.TryGet("event:252:84")!.Kind, built.TryGet("event:252:84")!.Freshness));
        Assert.Equal((ItemKind.Counter, Freshness.Window("Achievements")), (built.TryGet("event:252:39")!.Kind, built.TryGet("event:252:39")!.Freshness));
        Assert.Equal(ItemKind.Done, built.TryGet("event:252:147")!.Kind);
        Assert.Equal("Final Fantasy XV collaboration 2026", built.TryGet("event:252:84")!.Name);
    }

    [Fact]
    public void Storyline_categories_are_the_four_journal_section_three_ones()
    {
        Assert.True(JournalIds.IsStorylineCategory(54));
        Assert.True(JournalIds.IsStorylineCategory(56));
        Assert.True(JournalIds.IsStorylineCategory(57));
        Assert.True(JournalIds.IsStorylineCategory(58));
        Assert.False(JournalIds.IsStorylineCategory(55));   // Hildibrand is its own block
        Assert.False(JournalIds.IsStorylineCategory(97));
        Assert.Equal(156u, JournalIds.ClassQuestGenreFirst);
        Assert.Equal(217u, JournalIds.RoleQuestGenreFirst);
    }

    [Fact]
    public void The_fixture_holds_the_resets_rows()
    {
        Assert.Equal(11, Rows.Roulettes.Count);
        var levelling = Rows.Roulettes.Single(r => r.RowId == 1);
        Assert.Equal("Duty Roulette: Leveling", levelling.Name);
        Assert.Equal(4, levelling.SortKey);
        Assert.Equal("Duty Roulette: Expert", Rows.Roulettes.OrderBy(r => r.SortKey).First().Name);
        Assert.Equal("Daily Challenge: Frontline", Rows.Roulettes.OrderBy(r => r.SortKey).Last().Name);

        Assert.Equal(22, Rows.HuntOrderTypes.Count);
        Assert.Equal(16, Rows.HuntOrderTypes.Count(t => t.Type == 1));
        Assert.Equal(6, Rows.HuntOrderTypes.Count(t => t.Type == 2));
        var grandCompanyDaily = Rows.HuntOrderTypes.Single(t => t.RowId == 0);
        Assert.Equal("Mark Bills", grandCompanyDaily.Name);
        Assert.Equal(1u, grandCompanyDaily.OrderStart);
        Assert.Equal(50, grandCompanyDaily.OrderAmount);
        Assert.Equal("Elite Dawn Hunt Bills", Rows.HuntOrderTypes.Single(t => t.RowId == 21).Name);
        Assert.All(Rows.HuntOrderTypes, t => Assert.True(t.OrderAmount > 0 && t.Name.Length > 0, t.RowId.ToString()));

        Assert.Equal(12, Rows.SatisfactionNpcs.Count);
        Assert.Equal("Zhloe Aliapoh", Rows.SatisfactionNpcs.Single(n => n.RowId == 1).Name);
        Assert.Equal("Tiisol Ja", Rows.SatisfactionNpcs.Single(n => n.RowId == 12).Name);
        Assert.All(Rows.SatisfactionNpcs, n => Assert.Equal(6, n.DeliveriesPerWeek));

        var tomestone = Assert.Single(Rows.Tomestones);
        Assert.Equal(49u, tomestone.ItemId);
        Assert.Equal("Allagan Tomestone of Mnemonics", tomestone.Name);
        Assert.Equal(900, tomestone.WeeklyLimit);
    }

    [Fact]
    public void The_fixture_roulettes_carry_note_15s_members_and_the_clients_their_unlock_quests()
    {
        // The named ContentFinderCondition rows flagged for each roulette, reduced to their InstanceContent ids.
        IReadOnlyList<uint> Members(uint roulette) => Rows.Roulettes.Single(r => r.RowId == roulette).MemberContentIds;
        Assert.Equal(
            [(1u, 40), (2u, 56), (3u, 3), (4u, 14), (5u, 2), (6u, 52), (7u, 5), (8u, 6), (9u, 290), (15u, 18), (17u, 60)],
            Rows.Roulettes.OrderBy(r => r.RowId).Select(r => (r.RowId, r.MemberContentIds.Count)));
        Assert.Equal([15u, 86u, 20082u], Members(3).OrderBy(id => id));       // Main Scenario: the three story duties
        Assert.Equal([103u, 104u], Members(5).OrderBy(id => id));             // Expert: the two current dungeons
        Assert.Contains(4u, Members(1));                                       // Leveling holds Sastasha
        Assert.Contains(2u, Members(1));                                       // and the Tam-Tara Deepcroft
        Assert.All(Rows.Roulettes, r => Assert.Equal(r.MemberContentIds.Count, r.MemberContentIds.Distinct().Count()));
        Assert.All(Rows.Roulettes.SelectMany(r => r.MemberContentIds), id => Assert.NotEqual(0u, id));

        // SatisfactionNpc.QuestRequired is each client's unlock quest.
        Assert.Equal(67087u, Rows.SatisfactionNpcs.Single(n => n.RowId == 1).QuestRequired);    // Arms Wide Open
        Assert.Equal(70996u, Rows.SatisfactionNpcs.Single(n => n.RowId == 12).QuestRequired);   // Taco Time
        Assert.All(Rows.SatisfactionNpcs, n => Assert.NotEqual(0u, n.QuestRequired));
    }

    [Fact]
    public void Every_fixture_roulette_has_a_gate_in_the_hand_file_and_one_without_is_reported()
    {
        // A patch that adds a roulette fails here and asks for a hand-file line; a gate for a roulette the sheet no
        // longer carries fails here too, so the two lists cannot drift apart unnoticed.
        Assert.Equal(Rows.Roulettes.Select(r => r.RowId).OrderBy(id => id), Extras.RouletteGates.Select(g => g.RouletteId).OrderBy(id => id));
        var warnings = new List<string>();
        CatalogBuilder.Build(Rows, Extras, warnings.Add);
        Assert.DoesNotContain(warnings, w => w.Contains("roulette"));

        // Without Expert's line the roulette is still built, read ungated, and said once by number and name. Its own
        // list, so the single warning asserted is this build's and not whatever the full build may one day say.
        var withoutExpert = Extras with { RouletteGates = Extras.RouletteGates.Where(g => g.RouletteId != 5).ToList() };
        var ungated = new List<string>();
        var built = CatalogBuilder.Build(Rows, withoutExpert, ungated.Add);
        var warning = Assert.Single(ungated);
        Assert.Contains("roulette 5", warning);
        Assert.Contains("Duty Roulette: Expert", warning);
        Assert.NotNull(built.TryGet("reset:roulette:5"));
        Assert.Equal(11, built.InCategory(Category.Resets).Count(i => i.Group == "Duty roulettes"));
    }

    [Fact]
    public void Resets_groups_hold_their_counts()
    {
        var groups = Built.InCategory(Category.Resets).GroupBy(i => (i.GroupOrder, i.Group)).ToList();
        Assert.Equal(
        [
            (110, "Duty roulettes", 11),
            (120, "Hunt bills, daily", 16),
            (130, "Allied societies", 1),
            (140, "Grand Company", 2),
            (150, "Mini Cactpot", 1),
            (210, "Wondrous Tails", 2),
            (215, "Faux Hollows", 1),
            (221, "Challenge log: Battles", 8),
            (222, "Challenge log: PvP", 6),
            (223, "Challenge log: FATE", 2),
            (224, "Challenge log: Levequests", 2),
            (225, "Challenge log: Crafting & Gathering", 10),
            (226, "Challenge log: Treasure Hunt", 3),
            (227, "Challenge log: Allied Society Quests", 2),
            (228, "Challenge log: Grand Company", 3),
            (229, "Challenge log: Retainers", 2),
            (230, "Challenge log: Gold Saucer", 17),
            (231, "Challenge log: Field Operations", 18),
            (232, "Challenge log: Deep Dungeons", 5),
            (233, "Challenge log: Other", 7),
            (234, "Challenge log: Complete", 5),
            (240, "Custom deliveries", 13),
            (250, "Hunt bills, weekly", 6),
            (255, "Tomestones", 1),
            (260, "Jumbo Cactpot", 1),
            (265, "Fashion Report", 1),
            (270, "Masked Carnivale", 3),
            (275, "Doman Enclave", 1),
            (280, "Raid lockouts", 2),
            (310, "Allowances", 2),
            (320, "Timers", 13),
        ], groups.Select(g => (g.Key.GroupOrder, g.Key.Group, g.Count())));
        Assert.Equal(167, Built.InCategory(Category.Resets).Count);
    }

    [Fact]
    public void Resets_ids_follow_note_13s_schemes()
    {
        var resets = Built.InCategory(Category.Resets).ToList();
        Assert.All(resets, i => Assert.StartsWith("reset:", i.Id));
        Assert.Equal("Duty Roulette: Expert", Built.TryGet("reset:roulette:5")!.Name);
        Assert.Equal("Duty Roulette: Leveling", Built.TryGet("reset:roulette:1")!.Name);
        Assert.Equal("Mark Bills", Built.TryGet("reset:hunt:0")!.Name);
        Assert.Equal("Elite Dawn Hunt Bills", Built.TryGet("reset:hunt:21")!.Name);
        Assert.Equal("Zhloe Aliapoh", Built.TryGet("reset:delivery:1")!.Name);
        Assert.Equal("Custom deliveries", Built.TryGet("reset:delivery:all")!.Name);
        Assert.Equal("Allagan Tomestone of Mnemonics", Built.TryGet("reset:tomestone:weekly")!.Name);
        Assert.Equal("Venture: retainer 1", Built.TryGet("reset:venture:0")!.Name);
        Assert.Equal("Venture: retainer 10", Built.TryGet("reset:venture:9")!.Name);
        Assert.Null(Built.TryGet("reset:venture:10"));
        foreach (var id in new[] { "reset:tribe-allowance", "reset:leve-allowance", "reset:treasure-map", "reset:hollows",
                                   "reset:bingo:stickers", "reset:bingo:second-chance", "reset:bingo:expiry",
                                   "reset:squadron:mission", "reset:squadron:training" })
            Assert.NotNull(Built.TryGet(id));
        // These ids, present now, follow the same schemes as the rest.
        foreach (var id in new[] { "reset:gc:supply", "reset:gc:provision", "reset:cactpot:mini", "reset:cactpot:jumbo", "reset:fashion-report",
                                   "reset:enclave", "reset:challenge:6", "reset:challenge:72", "reset:carnivale:0", "reset:carnivale:2", "reset:lockout:1074", "reset:lockout:1117" })
            Assert.NotNull(Built.TryGet(id));
        Assert.Null(Built.TryGet("reset:carnivale:3"));
        Assert.Equal(90, resets.Count(i => i.Id.StartsWith("reset:challenge:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Resets_items_carry_the_kind_reset_and_their_freshness()
    {
        var resets = Built.InCategory(Category.Resets).ToList();
        // 66 rows are AtLogin; 99 are gated on a window and 2 are observed. No Resets item carries fixed dates.
        Assert.Equal(66, resets.Count(i => i.Freshness == Freshness.AtLogin));
        Assert.Equal(99, resets.Count(i => i.Freshness.Kind == FreshnessKind.NeedsWindow));
        Assert.Equal(2, resets.Count(i => i.Freshness.Kind == FreshnessKind.ObservedOnly));
        Assert.All(resets, i => Assert.Null(i.Availability));

        (ItemKind Kind, ResetKind Reset) Shape(string id) => (Built.TryGet(id)!.Kind, Built.TryGet(id)!.Reset.Kind);
        Assert.Equal((ItemKind.Done, ResetKind.Daily), Shape("reset:roulette:1"));
        Assert.Equal((ItemKind.Done, ResetKind.Daily), Shape("reset:hunt:0"));
        Assert.Equal((ItemKind.Done, ResetKind.Weekly), Shape("reset:hunt:4"));
        Assert.Equal((ItemKind.Counter, ResetKind.Daily), Shape("reset:tribe-allowance"));
        Assert.Equal((ItemKind.Counter, ResetKind.Weekly), Shape("reset:delivery:all"));
        Assert.Equal((ItemKind.Counter, ResetKind.Weekly), Shape("reset:bingo:stickers"));
        Assert.Equal((ItemKind.Counter, ResetKind.None), Shape("reset:bingo:second-chance"));
        Assert.False(Built.TryGet("reset:bingo:second-chance")!.DoneAtCap);
        // A journal is valid for two weeks, so the stickers row is the one Resets item whose count survives its own
        // boundary. The second line is what stops the flag spreading to the tomestone, delivery and allowance rows,
        // which really do zero at theirs.
        Assert.True(Built.TryGet("reset:bingo:stickers")!.CarriesOver);
        Assert.All(Built.InCategory(Category.Resets).Where(i => i.Id != "reset:bingo:stickers"), i => Assert.False(i.CarriesOver));
        Assert.Equal((ItemKind.Done, ResetKind.Weekly), Shape("reset:hollows"));
        Assert.Equal((ItemKind.Counter, ResetKind.Weekly), Shape("reset:tomestone:weekly"));
        Assert.Equal((ItemKind.Timer, ResetKind.None), Shape("reset:treasure-map"));
        // Only the map reads "ready" once its deadline passes; every other timer means "overdue".
        Assert.True(Built.TryGet("reset:treasure-map")!.ReadyWhenPassed);
        Assert.Equal(1, resets.Count(i => i.ReadyWhenPassed));
        Assert.Equal((ItemKind.Timer, ResetKind.None), Shape("reset:venture:3"));

        var leves = Built.TryGet("reset:leve-allowance")!;
        Assert.Equal((ItemKind.Counter, ResetKind.Accrual), (leves.Kind, leves.Reset.Kind));
        Assert.Equal(ResetSchedules.Leve, leves.Reset.Schedule);
        Assert.Equal(3, leves.Reset.AccrualAmount);

        var training = Built.TryGet("reset:squadron:training")!;
        Assert.Equal((ItemKind.Timer, ResetKind.Daily), (training.Kind, training.Reset.Kind));
        Assert.Equal(ResetSchedules.GrandCompany, training.Reset.Schedule);

        // Every daily and weekly hunt bill, and nothing else, sits in its own group.
        Assert.Equal(16, resets.Count(i => i.Group == "Hunt bills, daily" && i.Reset.Kind == ResetKind.Daily));
        Assert.Equal(6, resets.Count(i => i.Group == "Hunt bills, weekly" && i.Reset.Kind == ResetKind.Weekly));
    }

    [Fact]
    public void Window_and_observed_items_carry_their_gates_recurrence_and_kinds()
    {
        (ItemKind Kind, ResetKind Reset, string Gate) Shape(string id) { var i = Built.TryGet(id)!; return (i.Kind, i.Reset.Kind, i.Freshness.Gate); }
        Assert.Equal((ItemKind.Done, ResetKind.Daily, "Grand Company supply"), Shape("reset:gc:supply"));
        Assert.Equal((ItemKind.Done, ResetKind.Daily, "Grand Company supply"), Shape("reset:gc:provision"));
        Assert.Equal(ResetSchedules.GrandCompany, Built.TryGet("reset:gc:provision")!.Reset.Schedule);
        Assert.Equal(["Supply missions", "Provisioning missions"], Built.InCategory(Category.Resets).Where(i => i.Group == "Grand Company").Select(i => i.Name));

        var mini = Built.TryGet("reset:cactpot:mini")!;
        Assert.Equal((ItemKind.Counter, ResetKind.Daily, Freshness.Observed, true), (mini.Kind, mini.Reset.Kind, mini.Freshness, mini.DoneAtCap));
        var jumbo = Built.TryGet("reset:cactpot:jumbo")!;
        Assert.Equal((ItemKind.Counter, ResetKind.Weekly, ResetSchedules.Jumbo, Freshness.Observed), (jumbo.Kind, jumbo.Reset.Kind, jumbo.Reset.Schedule, jumbo.Freshness));

        Assert.Equal((ItemKind.Done, ResetKind.Weekly, "Challenge Log"), Shape("reset:challenge:6"));
        Assert.All(Built.InCategory(Category.Resets).Where(i => i.Id.StartsWith("reset:challenge:", StringComparison.Ordinal)),
            i => Assert.True(i.Group.StartsWith("Challenge log: ", StringComparison.Ordinal) && i.GroupOrder is >= 221 and <= 234, i.Id));

        var report = Built.TryGet("reset:fashion-report")!;
        Assert.Equal((ItemKind.Done, ResetKind.Weekly, "Fashion Report"), Shape(report.Id));
        Assert.Equal(new Recurrence(TimeSpan.FromDays(3), TimeSpan.FromDays(7)), report.Recurrence);
        Assert.Equal(ResetSchedules.Weekly, report.Reset.Schedule);

        Assert.Equal((ItemKind.Done, ResetKind.Weekly, "Masked Carnivale briefing"), Shape("reset:carnivale:2"));
        Assert.Equal(["Novice target", "Moderate target", "Advanced target"], Built.InCategory(Category.Resets).Where(i => i.Group == "Masked Carnivale").Select(i => i.Name));
        Assert.Equal((ItemKind.Counter, ResetKind.Weekly, "Doman Enclave"), Shape("reset:enclave"));
        Assert.Equal((ItemKind.Done, ResetKind.Weekly, "Duty Finder"), Shape("reset:lockout:1074"));
        Assert.Equal("AAC Heavyweight M4", Built.TryGet("reset:lockout:1074")!.Name);
        Assert.Equal("Windurst: The Third Walk", Built.TryGet("reset:lockout:1117")!.Name);
        Assert.Single(Built.InCategory(Category.Resets), i => i.Recurrence != null);
    }

    [Fact]
    public void The_fixture_holds_the_window_and_trove_rows()
    {
        Assert.Equal(90, Rows.ChallengeEntries.Count);
        Assert.Equal(14, Rows.ChallengeEntries.Select(e => e.CategoryId).Distinct().Count());
        Assert.Equal(24, Rows.ChallengeEntries.Count(e => e.ReqUnlock != 0));
        Assert.Equal(2, Rows.ChallengeEntries.Count(e => e.ReqUnlock is not 0 and < 65536));   // 95 and 372 are unlock links, not quests
        Assert.Equal(95u, Rows.ChallengeEntries.Single(e => e.RowId == 72).ReqUnlock);
        Assert.Equal(5, Rows.ChallengeEntries.Count(e => e.LevelUnlock != 0));
        Assert.Equal(60, Rows.ChallengeEntries.Single(e => e.RowId == 6).LevelUnlock);         // Wondrous Tails' entry
        Assert.Equal(["Battles", "PvP", "FATE", "Levequests", "Crafting & Gathering", "Treasure Hunt", "Allied Society Quests", "Grand Company",
                      "Retainers", "Gold Saucer", "Field Operations", "Deep Dungeons", "Other", "Complete"],
            Rows.ChallengeEntries.OrderBy(e => e.CategorySortKey).Select(e => e.CategoryName).Distinct());
        Assert.All(Rows.ChallengeEntries, e => Assert.True(e.Name.Length > 0 && e.CategoryId != 0, e.RowId.ToString()));

        var lockouts = Rows.Lockouts.OrderBy(l => l.ConditionId).ToList();
        Assert.Equal(2, lockouts.Count);
        Assert.Equal((1074u, 30160u, "AAC Heavyweight M4"), (lockouts[0].ConditionId, lockouts[0].ContentId, lockouts[0].Name));
        Assert.Equal((1117u, 30163u, "Windurst: The Third Walk"), (lockouts[1].ConditionId, lockouts[1].ContentId, lockouts[1].Name));

        var trove = Assert.Single(Rows.TroveItems);
        Assert.Equal(51750u, trove.ItemId);
        Assert.Equal("Irregular Tomestone of Astronomy I", trove.Name);
        Assert.Equal(999, trove.StackSize);
    }

    [Fact]
    public void The_trove_is_a_held_counter_in_events_named_by_its_tomestone()
    {
        var trove = Built.TryGet("event:trove:51750")!;
        Assert.Equal(Category.Events, trove.Category);
        Assert.Equal(("Moogle Treasure Trove", 100), (trove.Group, trove.GroupOrder));
        Assert.Equal("Irregular Tomestone of Astronomy I", trove.Name);
        Assert.Equal((ItemKind.Counter, ResetKind.Window, Freshness.AtLogin, false), (trove.Kind, trove.Reset.Kind, trove.Freshness, trove.DoneAtCap));
        Assert.Equal(new Availability(new(2026, 9, 9, 8, 0, 0, TimeSpan.Zero), new(2026, 10, 19, 14, 59, 0, TimeSpan.Zero)), trove.Availability);
        Assert.Single(Built.InCategory(Category.Events), i => i.Group == "Moogle Treasure Trove");
    }

    [Fact]
    public void The_fixture_holds_the_content_rows()
    {
        Assert.Equal(440, Rows.Duties.Count);
        Assert.Equal(429, Rows.Duties.Count(d => d.LinkType == 1));
        Assert.Equal(11, Rows.Duties.Count(d => d.LinkType == 3));
        Assert.Equal(
            [(2u, 103), (3u, 14), (4u, 105), (5u, 155), (26u, 4), (27u, 32), (28u, 7), (29u, 4), (30u, 12), (37u, 1), (38u, 3)],
            Rows.Duties.GroupBy(d => d.ContentTypeId).OrderBy(g => g.Key).Select(g => (g.Key, g.Count())));
        Assert.Equal("Sastasha", Rows.Duties.Single(d => d.RowId == 4).Name);
        Assert.Equal("the Tam-Tara Deepcroft", Rows.Duties.Single(d => d.RowId == 2).Name);   // the sheet's own casing; the builder raises the first letter
        var m4 = Rows.Duties.Single(d => d.RowId == 1074);
        Assert.Equal((1, 30160u, 5u, "Raids", 48u, "Normal Raids (Dawntrail)", 5u, 181), (m4.LinkType, m4.ContentId, m4.ContentTypeId, m4.ContentTypeName, m4.UiCategoryId, m4.UiCategoryName, m4.Expansion, m4.SortKey));
        var anemos = Rows.Duties.Single(d => d.RowId == 283);
        Assert.Equal((3, 3u, 26u, "Eureka", 0u, 2u, 0), (anemos.LinkType, anemos.ContentId, anemos.ContentTypeId, anemos.ContentTypeName, anemos.UiCategoryId, anemos.Expansion, anemos.SortKey));
        Assert.DoesNotContain(Rows.Duties, d => d.RowId is 70 or 71);   // the two obsolete special events have no heading
        Assert.All(Rows.Duties, d => Assert.True(d.Name.Length > 0 && d.ContentId != 0, d.RowId.ToString()));
        Assert.All(Rows.Duties.Where(d => d.ContentTypeId is 2 or 4 or 5), d => Assert.NotEqual(0u, d.UiCategoryId));
        Assert.All(Rows.Duties.Where(d => d.ContentTypeId is 26 or 27 or 29 or 38), d => Assert.Equal(0, d.SortKey));   // families the Duty Finder does not list carry no order

        Assert.Equal([(1u, "Sil'dih Survey Record", 12), (2u, "Mount Rokkon Exorcism Record", 12), (3u, "Aloalo Conservation Record", 12), (4u, "Corvosi Folklore Record", 13)],
            Rows.VariantSeries.Select(s => (s.RowId, s.Name, s.ContentsIds.Count)));
        Assert.Equal(Enumerable.Range(37, 13).Select(i => (uint)i), Rows.VariantSeries.Single(s => s.RowId == 4).ContentsIds);

        Assert.Equal(12, Rows.HuntingLogs.Count);
        Assert.Equal([(0, 1u, 0u), (1, 2u, 0u), (2, 3u, 0u), (3, 4u, 0u), (4, 5u, 0u), (5, 6u, 0u), (6, 7u, 0u), (7, 26u, 0u), (8, 0u, 1u), (9, 0u, 2u), (10, 0u, 3u), (11, 29u, 0u)],
            Rows.HuntingLogs.Select(h => (h.Index, h.ClassJobId, h.GrandCompanyId)));
        Assert.Equal("gladiator", Rows.HuntingLogs[0].Name);
        Assert.Equal("Order of the Twin Adder", Rows.HuntingLogs[9].Name);
        Assert.All(Rows.HuntingLogs.Where(h => h.ClassJobId != 0), h => Assert.Equal(50, h.Notes.Count));
        Assert.All(Rows.HuntingLogs.Where(h => h.GrandCompanyId != 0), h => Assert.Equal(30, h.Notes.Count));
        Assert.All(Rows.HuntingLogs.SelectMany(h => h.Notes), n => Assert.True(n.NeededKills.Count == 4 && n.NeededKills.Any(k => k > 0), $"{n.Rank}/{n.Note}"));
        Assert.Equal([3, 0, 0, 0], Rows.HuntingLogs[0].Notes[0].NeededKills);    // Gladiator 01: three of one target
        Assert.Equal([5, 5, 0, 0], Rows.HuntingLogs[0].Notes[49].NeededKills);   // Gladiator 50: two targets

        Assert.Equal(31, Rows.PvPSeriesLevels.Count);
        Assert.Equal((30u, 20000), (Rows.PvPSeriesLevels.Max(l => l.RowId), Rows.PvPSeriesLevels.Single(l => l.RowId == 30).ExpToNext));
        Assert.Equal(2000, Rows.PvPSeriesLevels.Single(l => l.RowId == 1).ExpToNext);

        Assert.Equal(77, Rows.Currencies.Count);
        var mgp = Rows.Currencies.Single(c => c.ItemId == 29);
        Assert.Equal(("MGP", 9999999), (mgp.Name, mgp.StackSize));
        Assert.Contains(Rows.Currencies, c => c.ItemId == 1 && c.Name == "Gil");
    }

    [Fact]
    public void Content_groups_hold_their_counts()
    {
        var groups = Built.InCategory(Category.Content).GroupBy(i => (i.GroupOrder, i.Group)).Select(g => (g.Key.GroupOrder, g.Key.Group, g.Count())).ToList();
        Assert.Equal(51, groups.Count);
        Assert.Equal(517, Built.InCategory(Category.Content).Count);
        Assert.Equal([(100, "Housing", 5), (200, "PvP", 7), (300, "Gold Saucer", 2), (400, "Hunting log", 12), (500, "Eureka", 5), (510, "Save the Queen", 5), (520, "Occult Crescent", 28),
                      (530, "Cosmic Exploration", 11), (540, "Island Sanctuary", 1), (550, "Deep Dungeons", 8)], groups.Take(10));
        Assert.Equal([(1000, "Dungeons (A Realm Reborn)", 31), (1010, "Dungeons (Heavensward)", 18), (1020, "Dungeons (Stormblood)", 15), (1030, "Dungeons (Shadowbringers)", 13), (1040, "Dungeons (Endwalker)", 13), (1050, "Dungeons (Dawntrail)", 13), (1100, "Guildhests", 14)],
            groups.Skip(10).Take(7));
        // Trials before High-end Trials in every expansion, by the heading's row rank and not by SortKey.
        Assert.Equal([(1200, "Trials (A Realm Reborn)", 18), (1201, "High-end Trials (A Realm Reborn)", 8), (1250, "Trials (Dawntrail)", 8), (1251, "High-end Trials (Dawntrail)", 9)],
            groups.Where(g => g.Item1 is 1200 or 1201 or 1250 or 1251));
        Assert.Equal(12, groups.Count(g => g.Item1 is >= 1200 and < 1300));
        // Normal, Savage, Alliance for every raid expansion; A Realm Reborn's normal heading is plain "Raids".
        Assert.Equal([(1300, "Raids (A Realm Reborn)", 13), (1301, "Savage Raids (A Realm Reborn)", 4), (1302, "Alliance Raids (A Realm Reborn)", 3), (1350, "Normal Raids (Dawntrail)", 12), (1351, "Savage Raids (Dawntrail)", 12), (1352, "Alliance Raids (Dawntrail)", 3)],
            groups.Where(g => g.Item1 is 1300 or 1301 or 1302 or 1350 or 1351 or 1352));
        Assert.Equal(18, groups.Count(g => g.Item1 is >= 1300 and < 1400));
        Assert.Equal([(1400, "Ultimate Raids", 7), (1410, "Chaotic Alliance Raid", 1), (1420, "V&C Dungeon Finder", 16), (1430, "The Masked Carnivale", 32)], groups.Skip(47));
        Assert.Equal(groups.Count, groups.Select(g => g.Item2).Distinct().Count());   // no group name repeats within the category
    }

    [Fact]
    public void Content_items_carry_note_17s_ids_kinds_and_orders()
    {
        var content = Built.InCategory(Category.Content).ToList();
        (ItemKind Kind, ResetKind Reset, Freshness Freshness, bool DoneAtCap) Shape(string id) { var i = Built.TryGet(id)!; return (i.Kind, i.Reset.Kind, i.Freshness, i.DoneAtCap); }
        Assert.All(content, i => Assert.Equal(ResetKind.None, i.Reset.Kind));
        Assert.Equal(440, content.Count(i => i.Id.StartsWith("duty:", StringComparison.Ordinal)));
        Assert.All(content.Where(i => i.Id.StartsWith("duty:", StringComparison.Ordinal)), i => Assert.Equal((ItemKind.Done, Freshness.AtLogin), (i.Kind, i.Freshness)));

        Assert.Equal((ItemKind.Done, ResetKind.None, Freshness.AtLogin, true), Shape("housing:personal"));
        Assert.Equal(["Personal estate", "Free company estate", "Apartment", "Private chambers", "House visit due"], content.Where(i => i.Group == "Housing").Select(i => i.Name));

        Assert.Equal((ItemKind.Level, ResetKind.None, Freshness.AtLogin, true), Shape("pvp:series"));
        Assert.Equal((ItemKind.Counter, ResetKind.None, Freshness.AtLogin, true), Shape("pvp:claimed"));
        Assert.Equal((ItemKind.Counter, ResetKind.None, Freshness.AtLogin, true), Shape("pvp:claimed-previous"));
        Assert.Equal((ItemKind.Level, ResetKind.None, Freshness.AtLogin, true), Shape(CatalogBuilder.CrystallineConflictId));
        Assert.Equal((ItemKind.Level, ResetKind.None, Freshness.AtLogin, true), Shape(CatalogBuilder.PvpRankPrefix + "2"));
        Assert.Equal([("Series level", 0), ("Series rewards claimed", 1), ("Previous series rewards claimed", 2), ("Crystalline Conflict tier", 3),
                      ("Maelstrom PvP rank", 11), ("Order of the Twin Adder PvP rank", 12), ("Immortal Flames PvP rank", 13)],
            content.Where(i => i.Group == "PvP").Select(i => (i.Name, i.Order)));
        Assert.Equal(1, content.Count(i => i.Freshness.Kind == FreshnessKind.NeedsWindow));   // the racing rank
        Assert.Equal(468, content.Count(i => i.Freshness == Freshness.AtLogin));

        Assert.Equal((ItemKind.Counter, ResetKind.None, Freshness.AtLogin, false), Shape("currency:29"));
        Assert.Equal("MGP", Built.TryGet("currency:29")!.Name);

        Assert.Equal(["Gladiator", "Pugilist", "Marauder", "Lancer", "Archer", "Conjurer", "Thaumaturge", "Arcanist", "Maelstrom", "Order of the Twin Adder", "Immortal Flames", "Rogue"],
            content.Where(i => i.Group == "Hunting log").Select(i => i.Name));
        Assert.Equal((ItemKind.Counter, ResetKind.None, Freshness.AtLogin, true), Shape("hunt:class:29"));
        Assert.Equal(11, Built.TryGet("hunt:class:29")!.Order);
        Assert.Equal((ItemKind.Counter, ResetKind.None, Freshness.AtLogin, true), Shape("hunt:gc:2"));

        // Duty names read as the Duty Finder shows them; the order is the Duty Finder's, and the Carnivale's and the field
        // zones', which it does not list, is the row order.
        Assert.Equal("The Tam-Tara Deepcroft", Built.TryGet("duty:2")!.Name);
        Assert.Equal("Sastasha", Built.TryGet("duty:4")!.Name);
        Assert.Equal(1, Built.TryGet("duty:4")!.Order);
        Assert.Equal(["Sastasha", "The Tam-Tara Deepcroft"], content.Where(i => i.Group == "Dungeons (A Realm Reborn)").Take(2).Select(i => i.Name));
        Assert.Equal("All's Well That Starts Well", content.First(i => i.Group == "The Masked Carnivale").Name);
        Assert.Equal(["The Forbidden Land, Eureka Anemos", "The Forbidden Land, Eureka Pagos", "The Forbidden Land, Eureka Pyros", "The Forbidden Land, Eureka Hydatos"],
            content.Where(i => i.Group == "Eureka" && i.Id.StartsWith("duty:", StringComparison.Ordinal)).Select(i => i.Name));
        Assert.Equal(("AAC Heavyweight M4", "Normal Raids (Dawntrail)"), (Built.TryGet("duty:1074")!.Name, Built.TryGet("duty:1074")!.Group));
        Assert.Equal("High-end Trials (Dawntrail)", Built.TryGet("duty:1118")!.Group);   // Shinryu's Domain (Unreal), the rotating one

        // The four Variant series sit after the twelve V&C duties.
        var vc = content.Where(i => i.Group == "V&C Dungeon Finder").ToList();
        Assert.Equal(16, vc.Count);
        Assert.Equal(["Sil'dih Survey Record", "Mount Rokkon Exorcism Record", "Aloalo Conservation Record", "Corvosi Folklore Record"], vc.Skip(12).Select(i => i.Name));
        Assert.Equal((ItemKind.Counter, ResetKind.None, Freshness.AtLogin, true), Shape("vvd:4"));
        Assert.Equal(("The Sil'dihn Subterrane", "V&C Dungeon Finder"), (Built.TryGet("duty:868")!.Name, Built.TryGet("duty:868")!.Group));
    }

    [Fact]
    public void Content_rows_are_information_never_attention()
    {
        // A Done row with no reset is never a to-do, so 517 Content values, every place Level among them, leave the badge and
        // every top group exactly as they were; a PvP row with nothing stored is now AtLogin and sits in no top group either,
        // the same exemption a cleared AtLogin row with nothing stored gets. The comparison is against the same record before
        // any Content value, not against zero: the two observed cactpot rows sit in Today and This week on every character
        // with nothing stored.
        var record = Items.Record();
        foreach (var item in Built.InCategory(Category.Content))
        {
            record.Values[item.Id] = item.Kind switch
            {
                ItemKind.Done => new DoneValue(false),
                ItemKind.Counter => new CounterValue(0, 50, Items.Now),
                ItemKind.Level => new LevelValue(0, 30),
                _ => new TimerValue(Items.Now),
            };
        }
        foreach (var pvp in Built.InCategory(Category.Content).Where(i => i.Group == "PvP")) record.Values.Remove(pvp.Id);
        // The house timer is the one Content row that becomes attention, from its own 15-day window, pinned on its own by
        // The_house_visit_timer_is_the_one_content_row_that_becomes_attention; unstamped here, it reads unknown.
        record.Values.Remove(CatalogBuilder.HouseVisitId);
        var baseline = Assembler.Assemble(Built, Items.Record(), new Settings(), Items.Now);
        var checklist = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Equal(baseline.Badge, checklist.Badge);
        Assert.Equal((baseline.Today.Count, baseline.ThisWeek.Count, baseline.EndingSoon.Count, baseline.Running.Count),
            (checklist.Today.Count, checklist.ThisWeek.Count, checklist.EndingSoon.Count, checklist.Running.Count));
        Assert.DoesNotContain(checklist.Today.Concat(checklist.ThisWeek).Concat(checklist.EndingSoon).Concat(checklist.Running), e => e.Item.Category == Category.Content);
        var entries = checklist.Sections.Single(s => s.Category == Category.Content).Groups.SelectMany(g => g.Entries).ToList();
        Assert.Equal(517, entries.Count);
        Assert.Equal(0, entries.Count(e => e.Marker == "needs: PvP Profile"));
        Assert.Equal(516, entries.Count(e => e.State == EntryState.NotDone));
    }

    [Fact]
    public void The_fixture_holds_the_place_rows()
    {
        Assert.Equal(36, Rows.ContentTypes.Count);
        Assert.Equal([(21u, "Deep Dungeons"), (26u, "Eureka"), (29u, "Save the Queen"), (36u, "Island Sanctuary"), (38u, "Occult Crescent")],
            Rows.ContentTypes.Where(t => t.RowId is 21 or 26 or 29 or 36 or 38).Select(t => (t.RowId, t.Name)));

        Assert.Equal(24, Rows.PhantomJobs.Count);
        Assert.Equal([(0u, "Phantom Freelancer", 24, 24), (1u, "Phantom Knight", 6, 1), (23u, "Phantom Necromancer", 5, 20)],
            Rows.PhantomJobs.Where(j => j.RowId is 0 or 1 or 23).Select(j => (j.RowId, j.Name, j.LevelMax, j.JobIndex)));
        Assert.Equal(Enumerable.Range(1, 24), Rows.PhantomJobs.Select(j => j.JobIndex).Order());   // the Phantom Jobs list's own order, each once

        Assert.Equal(11, Rows.CosmicClasses.Count);
        Assert.Equal([(1u, "carpenter's tool", 20), (11u, "fisher's tool", 20)], Rows.CosmicClasses.Where(c => c.RowId is 1 or 11).Select(c => (c.RowId, c.Name, c.Stages)));
        Assert.All(Rows.CosmicClasses, c => Assert.Equal(20, c.Stages));

        Assert.Equal(21, Rows.IslandRanks.Count);
        Assert.Equal([(1u, 3000), (19u, 100000), (20u, 0)], Rows.IslandRanks.Where(r => r.RowId is 1 or 19 or 20).Select(r => (r.RowId, r.ExpToNext)));

        Assert.Equal([(1u, "the Palace of the Dead", "Aetherpool Arm", "Aetherpool Armor"), (2u, "Heaven-on-High", "Empyrean Aetherpool Arm", "Empyrean Aetherpool Armor"),
                      (3u, "Eureka Orthos", "Orthos Aetherpool Arm", "Orthos Aetherpool Armor"), (4u, "Pilgrim's Traverse", "Illumed Aetherpool Arm", "Illumed Aetherpool Armor")],
            Rows.DeepDungeons.Select(d => (d.RowId, d.Name, d.ArmName, d.ArmorName)));
    }

    [Fact]
    public void Place_items_carry_note_17s_ids_kinds_orders_and_gates()
    {
        var content = Built.InCategory(Category.Content).ToList();
        (ItemKind Kind, Freshness Freshness, string Group, int Order, string Name) Shape(string id) { var i = Built.TryGet(id)!; return (i.Kind, i.Freshness, i.Group, i.Order, i.Name); }
        Assert.Equal((ItemKind.Level, Freshness.Place("Eureka"), "Eureka", 0, "Elemental level"), Shape(CatalogBuilder.EurekaLevelId));
        Assert.Equal((ItemKind.Level, Freshness.Place("Save the Queen"), "Save the Queen", 0, "Resistance rank"), Shape(CatalogBuilder.BozjaRankId));
        Assert.Equal((ItemKind.Level, Freshness.Place("Occult Crescent"), "Occult Crescent", 0, "Knowledge level"), Shape(CatalogBuilder.OccultKnowledgeId));
        Assert.Equal((ItemKind.Level, Freshness.Place("Island Sanctuary"), "Island Sanctuary", 0, "Sanctuary rank"), Shape(CatalogBuilder.IslandRankId));
        Assert.Equal((ItemKind.Level, Freshness.Window("Gold Saucer window"), "Gold Saucer", 1, "Racing chocobo rank"), Shape(CatalogBuilder.RacingId));
        Assert.Equal((ItemKind.Timer, Freshness.Place("your house"), "Housing", 10, "House visit due"), Shape(CatalogBuilder.HouseVisitId));

        // The knowledge level, then the 24 phantom jobs in JobIndex order, Freelancer last, then the zones.
        var occult = content.Where(i => i.Group == "Occult Crescent").ToList();
        Assert.Equal(28, occult.Count);
        Assert.Equal(["Knowledge level", "Phantom Knight", "Phantom Monk", "Phantom Thief"], occult.Take(4).Select(i => i.Name));
        Assert.Equal(("Phantom Freelancer", 34), (occult[24].Name, occult[24].Order));
        Assert.Equal("The Occult Crescent: South Horn", occult[25].Name);
        Assert.Equal(["Elemental level", "The Forbidden Land, Eureka Anemos"], content.Where(i => i.Group == "Eureka").Take(2).Select(i => i.Name));

        var cosmic = content.Where(i => i.Group == "Cosmic Exploration").ToList();
        Assert.Equal(11, cosmic.Count);
        Assert.Equal((ItemKind.Level, Freshness.Place("Cosmic Exploration"), "Cosmic Exploration", 1, "Carpenter's tool"), Shape(CatalogBuilder.CosmicToolPrefix + "1"));
        Assert.Equal("Fisher's tool", cosmic[^1].Name);

        Assert.Equal(["The Palace of the Dead: Aetherpool Arm", "The Palace of the Dead: Aetherpool Armor", "Heaven-on-High: Empyrean Aetherpool Arm", "Heaven-on-High: Empyrean Aetherpool Armor",
                      "Eureka Orthos: Orthos Aetherpool Arm", "Eureka Orthos: Orthos Aetherpool Armor", "Pilgrim's Traverse: Illumed Aetherpool Arm", "Pilgrim's Traverse: Illumed Aetherpool Armor"],
            content.Where(i => i.Group == "Deep Dungeons").Select(i => i.Name));
        Assert.Equal((ItemKind.Level, Freshness.Place("Heaven-on-High"), "Deep Dungeons", 21, "Heaven-on-High: Empyrean Aetherpool Armor"), Shape(CatalogBuilder.DeepDungeonPrefix + "2:armor"));

        Assert.Equal(48, content.Count(i => i.Freshness.Kind == FreshnessKind.NeedsPlace));   // 47 place Levels and the house timer
        Assert.All(content.Where(i => i.Freshness.Kind == FreshnessKind.NeedsPlace), i => Assert.Equal(ResetKind.None, i.Reset.Kind));

        // The house timer's own warning window: 45 days less the game's warning at 30; no other item carries one.
        Assert.Equal(TimeSpan.FromDays(15), Built.TryGet(CatalogBuilder.HouseVisitId)!.RemindWithin);
        Assert.Equal([CatalogBuilder.HouseVisitId], Built.Items.Where(i => i.RemindWithin != null).Select(i => i.Id));
    }

    [Fact]
    public void The_house_visit_timer_is_the_one_content_row_that_becomes_attention()
    {
        var record = Items.Record();
        // Checklist is qualified: the Checklist namespace of the same name shadows it in an unqualified local-function signature.
        Entry Timer(Mnemosyne.Core.Checklist.Checklist c) => c.Sections.Single(s => s.Category == Category.Content).Groups.Single(g => g.Name == "Housing").Entries.Single(e => e.Item.Id == CatalogBuilder.HouseVisitId);

        // Never stamped: "unknown", never a guess, and attention nowhere.
        var unstamped = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Equal((EntryState.Unknown, "unknown", "visit: your house"), (Timer(unstamped).State, Timer(unstamped).Display, Timer(unstamped).Marker));
        Assert.DoesNotContain(unstamped.EndingSoon, e => e.Item.Id == CatalogBuilder.HouseVisitId);

        // Last seen inside 29 days ago: sixteen days left, before the game's own 30-day warning; a countdown in its section and
        // nowhere else.
        record.Values[CatalogBuilder.HouseVisitId] = new TimerValue(Items.Now + TimeSpan.FromDays(16));
        var counting = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Equal(("in 16 d", "as of last visit to your house"), (Timer(counting).Display, Timer(counting).Marker));
        Assert.DoesNotContain(counting.EndingSoon, e => e.Item.Id == CatalogBuilder.HouseVisitId);

        // Thirty-one days: fourteen left. Its own fifteen-day warning window places it in Ending soon, though the global
        // ending-soon window is three days, and it is on the badge.
        record.Values[CatalogBuilder.HouseVisitId] = new TimerValue(Items.Now + TimeSpan.FromDays(14));
        var due = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Contains(due.EndingSoon, e => e.Item.Id == CatalogBuilder.HouseVisitId);
        Assert.Equal(counting.Badge + 1, due.Badge);
        // A wider global window does not stretch it either: two days earlier, sixteen days left, a thirty-day setting still leaves
        // it out, because the item's own window governs.
        Assert.DoesNotContain(Assembler.Assemble(Built, record, new Settings { EndingSoonDays = 30 }, Items.Now - TimeSpan.FromDays(2)).EndingSoon,
            e => e.Item.Id == CatalogBuilder.HouseVisitId);

        // No personal estate: the lock reads blank and sits nowhere.
        record.Values[CatalogBuilder.HouseVisitId] = GateValue.Shut;
        var locked = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Equal((EntryState.NotDone, "", ""), (Timer(locked).State, Timer(locked).Display, Timer(locked).Marker));
        Assert.DoesNotContain(locked.EndingSoon, e => e.Item.Id == CatalogBuilder.HouseVisitId);
    }

    [Fact]
    public void The_fixture_holds_the_collection_rows()
    {
        // The MainCommand rows that name the groups and six kinds, in the client's language.
        Assert.Equal([(7u, "Gathering Log"), (9u, "Crafting Log"), (17u, "Emotes"), (29u, "Fishing Log"), (61u, "Mount Guide"), (62u, "Minion Guide"), (64u, "Sightseeing Log"),
                      (69u, "Orchestrion List"), (87u, "Collection"), (89u, "Fashion Accessories"), (95u, "Facewear")],
            Rows.MainCommands.Where(c => CollectionIds.Commands.Contains(c.RowId)).Select(c => (c.RowId, c.Name)));

        // Every kind's total, in the Collection group's order.
        Assert.Equal([("mount", 356), ("minion", 590), ("emote", 205), ("barding", 107), ("hairstyle", 52), ("facewear", 61), ("ornament", 56), ("orchestrion", 886), ("card", 475), ("framerkit", 348), ("title", 834)],
            Rows.Collections.Select(k => (k.Kind, k.Ids.Count)));
        IReadOnlyList<uint> Kind(string kind) => Rows.Collections.Single(k => k.Kind == kind).Ids;
        Assert.Contains(1u, Kind("mount"));             // the company chocobo, Order 0, a real entry
        Assert.DoesNotContain(103u, Kind("mount"));     // Red Baron, Order -1, a placeholder
        Assert.DoesNotContain(0u, Kind("barding"));     // the Leather Saddle, the default
        Assert.Contains(228u, Kind("hairstyle"));       // the one hairstyle link with no item, a unit all the same
        Assert.All(Kind("hairstyle"), link => Assert.InRange(link, 66u, 638u));
        Assert.Contains(0u, Kind("facewear"));          // Oval Spectacles, Order 0
        Assert.DoesNotContain(34u, Kind("facewear"));   // the development placeholder no item reaches
        Assert.Contains(1u, Kind("ornament"));          // the Parasol, Order 0
        Assert.DoesNotContain(0u, Kind("card"));        // row 0 is named "0" and is no card
        Assert.Contains(39576u, Kind("framerkit"));     // the Loporrit Framingway Kit, kit 76, the one not named "… Framer's Kit"
        Assert.Contains(37247u, Kind("framerkit"));     // the Paladin Framer's Kit, kit 0

        // The logs, per class for crafting and gathering; only recipes below 30000, the client's array.
        Assert.Equal([("craft", 8u, 716), ("craft", 9u, 896), ("craft", 10u, 578), ("craft", 11u, 1180), ("craft", 12u, 811), ("craft", 13u, 887), ("craft", 14u, 699), ("craft", 15u, 388),
                      ("gather", 16u, 551), ("gather", 17u, 692), ("fish", 18u, 1517), ("spearfish", 18u, 289)],
            Rows.Logs.Select(l => (l.Log, l.ClassJobId, l.Ids.Count)));
        Assert.All(Rows.Logs.Where(l => l.Log == "craft").SelectMany(l => l.Ids), id => Assert.True(id < 30000, id.ToString()));
        Assert.Equal(90, Rows.Logs.Single(l => l.ClassJobId == 16).Ids.Intersect(Rows.Logs.Single(l => l.ClassJobId == 17).Ids).Count());   // items in both
        Assert.DoesNotContain(0u, Rows.Logs.Single(l => l.Log == "fish").Ids);                          // row 0 has neither item nor spot
        Assert.Equal(1522u, Rows.Logs.Single(l => l.Log == "fish").Ids.Max());                          // the last log fish: index 1,522, at the declared count, inside the 191 bytes
        Assert.All(Rows.Logs.Single(l => l.Log == "spearfish").Ids, id => Assert.True(id < 30000, id.ToString()));   // 30000 and up are no fish
        Assert.Equal(20288u, Rows.Logs.Single(l => l.Log == "spearfish").Ids.Max());                   // the last log spearfish, index 288, inside the client's 38 bytes; 20,304 is neither visible nor an item

        // The sightseeing log by expansion, with the quests AdventureExPhase names from Stormblood on.
        Assert.Equal([(0u, "A Realm Reborn", 0u, 80), (1u, "Heavensward", 0u, 62), (2u, "Stormblood", 68456u, 62), (3u, "Shadowbringers", 69140u, 45), (4u, "Endwalker", 69710u, 46), (5u, "Dawntrail", 70542u, 45)],
            Rows.SightseeingLogs.Select(s => (s.ExVersionId, s.Name, s.QuestId, s.AdventureIds.Count)));
        Assert.Equal((2162688u, 2163027u), (Rows.SightseeingLogs[0].AdventureIds[0], Rows.SightseeingLogs[5].AdventureIds[^1]));

        // 73 categories in eight kinds, 3,652 achievements; the Legacy kind and the Ranking category are gone.
        Assert.Equal(73, Rows.AchievementCategories.Count);
        Assert.Equal([(1u, "Battle", 1, 7, 753), (2u, "PvP", 2, 4, 193), (3u, "Character", 3, 7, 558), (4u, "Items", 4, 16, 675), (5u, "Crafting & Gathering", 5, 12, 530),
                      (8u, "Quests", 6, 6, 623), (11u, "Exploration", 7, 17, 248), (12u, "Grand Company", 8, 4, 72)],
            Rows.AchievementCategories.GroupBy(c => (c.KindId, c.KindName, c.KindOrder)).Select(g => (g.Key.KindId, g.Key.KindName, g.Key.KindOrder, g.Count(), g.Sum(c => c.AchievementIds.Count))));
        Assert.DoesNotContain(Rows.AchievementCategories, c => c.RowId == 8 || c.KindId == 13);
        var general = Rows.AchievementCategories.Single(c => c.RowId == 1);
        Assert.Equal(("General", 1, 216), (general.Name, general.Order, general.AchievementIds.Count));
        var seasonal = Rows.AchievementCategories.Single(c => c.RowId == 38);
        Assert.Equal(("Seasonal Events", 99, 157), (seasonal.Name, seasonal.Order, seasonal.AchievementIds.Count));   // kept

        // The PvP rank table and its band titles in company order, and the Crystalline Conflict tiers.
        Assert.Equal(101, Rows.PvPRanks.Count);
        Assert.Equal([(0u, 0), (1u, 10), (50u, 226210), (51u, 0)], Rows.PvPRanks.Where(r => r.RowId is 0 or 1 or 50 or 51).Select(r => (r.RowId, r.ExpRequired)));
        Assert.Equal(["Storm Howler", "Serpent Howler", "Flame Howler"], Rows.PvPRanks.Single(r => r.RowId == 10).Titles);
        Assert.Equal(["-", "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Crystal", "Omega", "Ultima"], Rows.ColosseumRanks.Select(r => r.Name));
    }

    [Fact]
    public void Collections_groups_hold_their_counts()
    {
        var collections = Built.InCategory(Category.Collections).ToList();
        var groups = collections.GroupBy(i => (i.GroupOrder, i.Group)).Select(g => (g.Key.GroupOrder, g.Key.Group, g.Count())).ToList();
        Assert.Equal([(100, "Collection", 11), (200, "Crafting Log", 8), (210, "Gathering Log", 2), (220, "Fishing Log", 2), (230, "Sightseeing Log", 6),
                      (310, "Battle", 7), (320, "PvP", 4), (330, "Character", 7), (340, "Items", 16), (350, "Crafting & Gathering", 12), (360, "Quests", 6),
                      (370, "Exploration", 17), (380, "Grand Company", 4)], groups);
        Assert.Equal(102, collections.Count);
        Assert.Equal(groups.Count, groups.Select(g => g.Item2).Distinct().Count());   // no group name repeats within the category
        IEnumerable<string> Names(string group) => collections.Where(i => i.Group == group).Select(i => i.Name);
        Assert.Equal(["Mount Guide", "Minion Guide", "Emotes", "Bardings", "Hairstyles", "Facewear", "Fashion Accessories", "Orchestrion List", "Triple Triad cards", "Framer's kits", "Titles"],
            Names("Collection"));
        Assert.Equal(["Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian"], Names("Crafting Log"));
        Assert.Equal(["Miner", "Botanist"], Names("Gathering Log"));
        Assert.Equal(["Fishing", "Spearfishing"], Names("Fishing Log"));
        Assert.Equal(["A Realm Reborn", "Heavensward", "Stormblood", "Shadowbringers", "Endwalker", "Dawntrail"], Names("Sightseeing Log"));
        Assert.Equal(["General", "The Wolves' Den", "Frontline", "Rival Wings"], Names("PvP"));                                               // Ranking leaves
        Assert.Equal(["General", "Main Scenario", "Job/Role Quests", "Levequests", "Allied Society Quests", "Seasonal Events"], Names("Quests"));   // Seasonal Events stays, last by its order
    }

    [Fact]
    public void Collections_items_carry_note_18s_ids_kinds_orders_and_gates()
    {
        var collections = Built.InCategory(Category.Collections).ToList();
        (string Group, string Name, int Order, Freshness Freshness) Shape(string id) { var i = Built.TryGet(id)!; return (i.Group, i.Name, i.Order, i.Freshness); }
        Assert.All(collections, i => Assert.Equal((ItemKind.Counter, ResetKind.None, true), (i.Kind, i.Reset.Kind, i.DoneAtCap)));
        Assert.Equal(("Collection", "Mount Guide", 0, Freshness.AtLogin), Shape("collection:mount"));
        Assert.Equal(("Collection", "Framer's kits", 9, Freshness.AtLogin), Shape("collection:framerkit"));
        Assert.Equal(("Collection", "Titles", 10, Freshness.Window(CatalogBuilder.TitlesWindow)), Shape("collection:title"));
        Assert.Equal(("Crafting Log", "Carpenter", 8, Freshness.AtLogin), Shape("log:craft:8"));
        Assert.Equal(("Gathering Log", "Botanist", 17, Freshness.AtLogin), Shape("log:gather:17"));
        Assert.Equal(("Fishing Log", "Spearfishing", 1, Freshness.AtLogin), Shape("log:spearfish"));
        Assert.Equal(("Sightseeing Log", "Dawntrail", 5, Freshness.AtLogin), Shape("log:sightseeing:5"));
        Assert.Equal(("Battle", "General", 1, Freshness.Window(CatalogBuilder.AchievementsWindow)), Shape("achievement:category:1"));
        Assert.Equal(("Quests", "Seasonal Events", 99, Freshness.Window("Achievements")), Shape("achievement:category:38"));
        Assert.Null(Built.TryGet("achievement:category:8"));    // Ranking
        Assert.Null(Built.TryGet("achievement:category:54"));   // the Legacy kind's first category
        Assert.Equal(74, collections.Count(i => i.Freshness.Kind == FreshnessKind.NeedsWindow));   // the 73 categories and the titles
        Assert.Equal(28, collections.Count(i => i.Freshness == Freshness.AtLogin));
        Assert.All(collections, i => Assert.True(i.Id.StartsWith(CollectionIds.KindPrefix, StringComparison.Ordinal) || i.Id.StartsWith(CollectionIds.LogPrefix, StringComparison.Ordinal)
            || i.Id.StartsWith(CollectionIds.AchievementPrefix, StringComparison.Ordinal), i.Id));
    }

    [Fact]
    public void Collections_rows_are_information_never_attention()
    {
        // A Counter with no reset is in no rule, so the 102 rows, part-way, at the cap or with nothing stored, leave the
        // badge and every top group exactly as they were; a gated row with nothing stored reads "needs:" and sits nowhere either.
        var baseline = Assembler.Assemble(Built, Items.Record(), new Settings(), Items.Now);
        var record = Items.Record();
        foreach (var item in Built.InCategory(Category.Collections).Where(i => i.Freshness == Freshness.AtLogin))
            record.Values[item.Id] = new CounterValue(3, 10, Items.Now);
        record.Values["achievement:category:1"] = new CounterValue(216, 216, Items.Now);   // one category finished
        var checklist = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Equal(baseline.Badge, checklist.Badge);
        Assert.Equal((baseline.Today.Count, baseline.ThisWeek.Count, baseline.EndingSoon.Count, baseline.Running.Count),
            (checklist.Today.Count, checklist.ThisWeek.Count, checklist.EndingSoon.Count, checklist.Running.Count));
        Assert.DoesNotContain(checklist.Today.Concat(checklist.ThisWeek).Concat(checklist.EndingSoon).Concat(checklist.Running), e => e.Item.Category == Category.Collections);
        var entries = checklist.Sections.Single(s => s.Category == Category.Collections).Groups.SelectMany(g => g.Entries).ToDictionary(e => e.Item.Id);
        Assert.Equal(102, entries.Count);
        (EntryState State, string Display, string Marker) Read(string id) => (entries[id].State, entries[id].Display, entries[id].Marker);
        Assert.Equal((EntryState.NotDone, "3 of 10", ""), Read("collection:mount"));
        Assert.Equal((EntryState.Done, "216 of 216", "as of last visit to Achievements"), Read("achievement:category:1"));
        Assert.Equal((EntryState.Unknown, "unknown", "needs: Achievements"), Read("achievement:category:2"));
        Assert.Equal((EntryState.Unknown, "unknown", "needs: Titles"), Read("collection:title"));
        Assert.Equal(73, entries.Values.Count(e => e.State == EntryState.Unknown));   // 72 categories and the titles with nothing stored
    }

    [Fact]
    public void The_fixture_holds_the_money_rows()
    {
        // The MainCommand and Addon rows that name the Money groups, storage rows and retainer field words, in the client's language.
        Assert.Equal([(10u, "Inventory"), (25u, "Armoury Chest"), (66u, "Currency"), (77u, "Chocobo Saddlebag")],
            Rows.MainCommands.Where(c => MoneyIds.Commands.Contains(c.RowId)).Select(c => (c.RowId, c.Name)));
        Assert.Equal([(532u, "Retainer"), (830u, "Gil"), (885u, "Right Saddlebag"), (3500u, "Allagan Tomestones"), (3731u, "Retainer Inventory"),
                      (3734u, "Armoire"), (3735u, "Glamour Dresser"), (5751u, "Societal Currency"), (17580u, "Storage")],
            Rows.Addons.Select(a => (a.RowId, a.Text)));

        // 77 currencies; 24 tomestone items, and only the four with a Tomestones row can be held.
        Assert.Equal(77, Rows.Currencies.Count);
        Assert.Equal(24, Rows.TomestoneItems.Count);
        Assert.All(Rows.TomestoneItems, t => Assert.InRange(t.ItemId, 23u, 49u));
        Assert.Equal([(28u, 1u), (48u, 2u), (49u, 3u), (47u, 4u)],
            Rows.TomestoneItems.Where(t => t.TomestonesId != 0).OrderBy(t => t.TomestonesId).Select(t => (t.ItemId, t.TomestonesId)));

        // The stack sizes that are the holding caps: gil, a seal, a tomestone, Venture, Bicolor Gemstones, a white scrip.
        int Stack(uint item) => Rows.Currencies.Single(c => c.ItemId == item).StackSize;
        Assert.Equal((999_999_999, 200_000, 2_000, 65_000, 1_500, 4_000), (Stack(1), Stack(20), Stack(49), Stack(21072), Stack(26807), Stack(25199)));

        // Each allied society's currency, its expansion and its display order within it.
        Assert.Equal([(1u, 21076u, 0u, 1), (2u, 21075u, 0u, 2), (3u, 21078u, 0u, 3), (4u, 21077u, 0u, 4), (5u, 21073u, 0u, 5),
                      (6u, 21074u, 1u, 1), (7u, 21079u, 1u, 2), (8u, 21080u, 1u, 3), (9u, 21081u, 2u, 1), (10u, 21935u, 2u, 2),
                      (11u, 22525u, 2u, 3), (12u, 28186u, 3u, 1), (13u, 28187u, 3u, 2), (14u, 28188u, 3u, 3), (15u, 36657u, 4u, 1),
                      (16u, 37854u, 4u, 2), (17u, 38952u, 4u, 3), (18u, 44472u, 5u, 3), (19u, 48084u, 5u, 2), (20u, 46178u, 5u, 1)],
            Rows.BeastTribes.Select(t => (t.RowId, t.CurrencyItem, t.Expansion, t.DisplayOrder)));
        Assert.All(Rows.BeastTribes, t => Assert.Contains(Rows.Currencies, c => c.ItemId == t.CurrencyItem));

        // The armoire's storable rows, every one inside the item-search cache's 4,000 bits.
        Assert.Equal(3844, Rows.ArmoireRows.Count);
        Assert.Equal(3844, Rows.ArmoireRows.Distinct().Count());
        Assert.True(Rows.ArmoireRows.Max() < 3850);
    }

    [Fact]
    public void Money_groups_hold_their_rows()
    {
        var money = Built.InCategory(Category.Money).ToList();
        var groups = money.GroupBy(i => (i.GroupOrder, i.Group)).Select(g => (g.Key.GroupOrder, g.Key.Group, g.Count())).ToList();
        Assert.Equal([(100, "Currency", 32), (110, "Allagan Tomestones", 4), (120, "Societal Currency", 20), (200, "Retainer", 30), (300, "Storage", 6)], groups);
        Assert.Equal(92, money.Count);
        Assert.Null(Built.TryGet("gc:seals"));                                    // the seals moved to Money
        Assert.Null(Built.TryGet("currency:23"));                                 // Philosophy, a tomestone that cannot be held
        Assert.Equal(Category.Content, Built.TryGet("currency:29")!.Category);    // MGP stays Content's, built once
        IEnumerable<string> Names(string group) => money.Where(i => i.Group == group).Select(i => i.Name);
        Assert.Equal(["Gil", "Storm Seal", "Serpent Seal", "Flame Seal", "Wolf Mark", "Allied Seal"], Names("Currency").Take(6));
        Assert.Equal("MGC", Names("Currency").Last());
        Assert.Equal(["Allagan Tomestone of Poetics", "Allagan Tomestone of Mathematics", "Allagan Tomestone of Mnemonics", "Allagan Tomestone of Heliometry"], Names("Allagan Tomestones"));
        Assert.Equal(["Steel Amalj'ok", "Sylphic Goldleaf", "Titan Cobaltpiece", "Rainbowtide Psashp", "Ixali Oaknot"], Names("Societal Currency").Take(5));
        Assert.Equal(["Yok Huy Ward", "Mamool Ja Nanook", "Pelu Pelplume"], Names("Societal Currency").TakeLast(3));   // Dawntrail's, in their display order
        Assert.Equal(["Retainer 1", "Retainer 1: Gil", "Retainer 1: Retainer Inventory", "Retainer 2"], Names("Retainer").Take(4));
        Assert.Equal(["Retainer 10", "Retainer 10: Gil", "Retainer 10: Retainer Inventory"], Names("Retainer").TakeLast(3));
        Assert.Equal(["Inventory", "Armoury Chest", "Chocobo Saddlebag", "Right Saddlebag", "Glamour Dresser", "Armoire"], Names("Storage"));
        Assert.Equal(groups.Count, groups.Select(g => g.Item2).Distinct().Count());   // no group name repeats within the category
    }

    [Fact]
    public void Money_items_carry_note_19s_ids_kinds_orders_and_gates()
    {
        var money = Built.InCategory(Category.Money).ToList();
        (ItemKind Kind, Freshness Freshness, int Order, bool DoneAtCap, bool Hidden) Shape(string id) { var i = Built.TryGet(id)!; return (i.Kind, i.Freshness, i.Order, i.DoneAtCap, i.HiddenWhenBlank); }
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 0, false, false), Shape("currency:1"));                            // gil: always listed
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 1, false, true), Shape("currency:20"));                            // the Storm Seal
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 3, false, true), Shape("currency:49"));                            // Mnemonics, Tomestones row 3
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 51, false, true), Shape("currency:46178"));                        // Yok Huy Ward, Dawntrail's first
        Assert.Equal((ItemKind.Level, Freshness.AtLogin, 20, true, true), Shape(MoneyIds.RetainerId(2, MoneyIds.Level)));
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 21, false, true), Shape(MoneyIds.RetainerId(2, MoneyIds.RetainerGil)));
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 22, false, true), Shape(MoneyIds.RetainerId(2, MoneyIds.Slots)));
        Assert.Equal((ItemKind.Counter, Freshness.AtLogin, 1, false, true), Shape(MoneyIds.Armoury));
        Assert.Equal((ItemKind.Counter, Freshness.Window("Chocobo Saddlebag"), 3, false, true), Shape(MoneyIds.PremiumSaddlebag));
        Assert.Equal((ItemKind.Counter, Freshness.Window("Glamour Dresser"), 4, false, true), Shape(MoneyIds.Dresser));
        Assert.Equal((ItemKind.Counter, Freshness.Window("Armoire"), 5, false, true), Shape(MoneyIds.Armoire));
        Assert.All(money, i => Assert.Equal(ResetKind.None, i.Reset.Kind));
        Assert.Equal(88, money.Count(i => i.Freshness == Freshness.AtLogin));
        Assert.Equal(4, money.Count(i => i.Freshness.Kind == FreshnessKind.NeedsWindow));
        Assert.Equal(56, money.Count(i => i.Id.StartsWith(MoneyIds.CurrencyPrefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void Money_rows_are_information_never_attention_and_a_blank_one_is_not_listed()
    {
        // Every Money row has Reset None, so none is attention, full or empty, and a row with nothing to say is not listed, so
        // the section holds gil and what the character holds rather than 92 rows of which most read nothing.
        var baseline = Assembler.Assemble(Built, Items.Record(), new Settings(), Items.Now);
        var record = Items.Record();
        var level = MoneyIds.RetainerId(0, MoneyIds.Level);
        record.Values["currency:1"] = new CounterValue(0, 999_999_999, Items.Now);                         // gil at zero: still listed
        record.Values["currency:28"] = new CounterValue(1_200, 2_000, Items.Now);                          // Poetics held
        record.Values[level] = new LevelValue(90, 90);                                                     // at the character's own class level: done
        record.ItemNames[level] = "Mira: Botanist";
        record.Values[MoneyIds.RetainerId(0, MoneyIds.RetainerGil)] = new CounterValue(0, 999_999_999, Items.Now);
        record.Values[MoneyIds.RetainerId(0, MoneyIds.Slots)] = new CounterValue(175, 175, Items.Now);    // full: held, never done
        record.Values[MoneyIds.Bags] = new CounterValue(140, 140, Items.Now);
        record.Values[MoneyIds.Saddlebag] = GateValue.Shut;                                                // no saddlebag: a lock, not listed
        record.Values[MoneyIds.Dresser] = new CounterValue(800, 800, Items.Now);
        var checklist = Assembler.Assemble(Built, record, new Settings(), Items.Now);
        Assert.Equal(baseline.Badge, checklist.Badge);
        Assert.Equal((baseline.Today.Count, baseline.ThisWeek.Count, baseline.EndingSoon.Count, baseline.Running.Count),
            (checklist.Today.Count, checklist.ThisWeek.Count, checklist.EndingSoon.Count, checklist.Running.Count));
        Assert.DoesNotContain(checklist.Today.Concat(checklist.ThisWeek).Concat(checklist.EndingSoon).Concat(checklist.Running), e => e.Item.Category == Category.Money);
        var entries = checklist.Sections.Single(s => s.Category == Category.Money).Groups.SelectMany(g => g.Entries).ToList();
        Assert.Equal(["currency:1", "currency:28", level, MoneyIds.RetainerId(0, MoneyIds.RetainerGil), MoneyIds.RetainerId(0, MoneyIds.Slots),
                      MoneyIds.Bags, MoneyIds.PremiumSaddlebag, MoneyIds.Dresser, MoneyIds.Armoire], entries.Select(e => e.Item.Id));
        (EntryState State, string Name, string Display, string Marker) Read(string id) { var e = entries.Single(x => x.Item.Id == id); return (e.State, e.Name, e.Display, e.Marker); }
        Assert.Equal((EntryState.NotDone, "Gil", "0 of 999999999", ""), Read("currency:1"));
        Assert.Equal((EntryState.Done, "Mira: Botanist", "90 / 90", ""), Read(level));
        Assert.Equal((EntryState.NotDone, "Retainer 1: Retainer Inventory", "175 of 175", ""), Read(MoneyIds.RetainerId(0, MoneyIds.Slots)));
        Assert.Equal((EntryState.NotDone, "Glamour Dresser", "800 of 800", "as of last visit to Glamour Dresser"), Read(MoneyIds.Dresser));
        Assert.Equal((EntryState.Unknown, "Right Saddlebag", "unknown", "needs: Chocobo Saddlebag"), Read(MoneyIds.PremiumSaddlebag));
        Assert.Equal((EntryState.Unknown, "Armoire", "unknown", "needs: Armoire"), Read(MoneyIds.Armoire));

        // A category never read says "not read yet" on every AtLogin row, which is something: all 92 are listed but the lock.
        record.CategoryReadAt.Remove(nameof(Category.Money));
        var unread = Assembler.Assemble(Built, record, new Settings(), Items.Now).Sections.Single(s => s.Category == Category.Money).Groups.SelectMany(g => g.Entries).ToList();
        Assert.Equal(91, unread.Count);
        Assert.Contains(unread, e => e.Item.Id == "currency:25" && e.Marker == "not read yet");
    }
}
