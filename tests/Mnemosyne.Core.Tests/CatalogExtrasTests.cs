using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Tests;

public class CatalogExtrasTests
{
    [Fact]
    public void The_embedded_hand_file_loads_and_carries_the_dated_occurrences()
    {
        var extras = CatalogExtras.Load();
        Assert.Equal(30, extras.EventOccurrences.Count);
        var moonfire2026 = extras.EventOccurrences.Single(o => o.Festival == 174);
        Assert.Equal(2026, moonfire2026.Year);
        Assert.Equal(new DateTimeOffset(2026, 8, 12, 8, 0, 0, TimeSpan.Zero), moonfire2026.Start);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 14, 59, 0, TimeSpan.Zero), moonfire2026.End);
        Assert.Equal(100, extras.LevelCap);
        Assert.Equal(80, extras.CapFor(36));
        Assert.Contains(extras.FeatureUnlocks, f => f.QuestId == 66957 && f.Feature == "Glamour");
        Assert.Equal("AllianceRaid", extras.ChroniclesKinds.Single(k => k.CategoryId == 18).Kind);
        Assert.Equal(16, extras.ChroniclesKinds.Count);
    }

    [Fact]
    public void Every_occurrence_has_a_window_that_ends_after_it_starts_and_a_unique_genre_and_festival()
    {
        var extras = CatalogExtras.Load();
        Assert.All(extras.EventOccurrences, o => Assert.True(o.End > o.Start, o.Festival.ToString()));
        Assert.Equal(extras.EventOccurrences.Count, extras.EventOccurrences.Select(o => (o.Genre, o.Festival)).Distinct().Count());
    }

    [Fact]
    public void A_malformed_hand_file_is_an_error_not_a_default()
    {
        Assert.ThrowsAny<Exception>(() => CatalogExtras.Parse("{ nope"));
        Assert.ThrowsAny<Exception>(() => CatalogExtras.Parse("{}"));
    }

    private const string MinimalHandFileTemplate = """
        {
          "eventOccurrences": [ EVENT1, EVENT2 ],
          "featureUnlocks": [],
          "chroniclesKinds": [],
          "levelCap": 100,
          "limitedJobCaps": [ { "jobId": 36, "cap": 80 } ],
          "resetCaps": { "levequestAllowances": 100, "alliedSocietyAllowances": 12, "wondrousTailsStickers": 9, "wondrousTailsSecondChance": 9, "miniCactpotTickets": 3, "jumboCactpotTickets": 3 },
          "rouletteGates": [],
          "levequestUnlockQuests": [ 65756 ],
          "treasureMapGatherLevel": 40,
          "unlockQuests": { "challengeLog": 66967, "fashionReport": 68617, "domanEnclave": 68677, "maskedCarnivale": 68734, "miniCactpot": 66024, "jumboCactpot": 66025 },
          "fashionReport": { "attempts": 4, "rewardScore": 80 },
          "troves": [ { "itemId": 51750, "start": "2026-09-09T08:00:00Z", "end": "2026-10-19T14:59:00Z" } ],
          "contentUnlockQuests": { "goldSaucer": 65970, "pvp": [ 66640, 66641, 66642 ] },
          "variantSeries": [ { "seriesId": 1, "contentId": 36001 }, { "seriesId": 2, "contentId": 36002 } ],
          "placeUnlockQuests": { "eureka": 68614, "bozja": 69477, "occultCrescent": 70847, "cosmicExploration": 70789, "islandSanctuary": 70179, "deepDungeons": [ { "dungeonId": 1, "questId": 67092 } ] },
          "contentMaxima": { "elementalLevel": 60, "resistanceRank": 25, "knowledgeLevel": 40, "aetherpool": 99, "racingRank": 50 },
          "houseVisit": { "demolitionDays": 45, "warningDays": 30 },
          "collectionUnlockQuests": { "bardings": [ 66236, 66237, 66238 ], "tripleTriad": 65973, "sightseeingHeavensward": 67643 },
          "storage": { "retainerInventory": 175, "saddlebag": 70, "premiumSaddlebag": 70, "glamourDresser": 800, "saddlebagQuest": 66698, "castGlamourAction": 22 }
        }
        """;

    /// <summary>The template with both occurrences filled and, when given, the roulette gate lines spliced in.</summary>
    private static string MinimalHandFile(string gates = "") =>
        MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", MoonfireOccurrence(247))
            .Replace("\"rouletteGates\": []", $"\"rouletteGates\": [ {gates} ]");

    private static string MoonfireOccurrence(int genre) =>
        $$"""{ "genre": {{genre}}, "festival": 174, "event": "Moonfire Faire", "year": 2026, "start": "2026-08-12T08:00:00Z", "end": "2026-08-26T14:59:00Z" }""";

    [Fact]
    public void A_duplicated_occurrence_is_rejected()
    {
        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", MoonfireOccurrence(246));
        var ex = Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json));
        Assert.Contains("Moonfire Faire", ex.Message);
    }

    [Fact]
    public void A_duplicated_feature_unlock_or_chronicles_category_is_rejected()
    {
        var oneOccurrence = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace(", EVENT2", "");

        var unlockedTwice = oneOccurrence.Replace("\"featureUnlocks\": []",
            "\"featureUnlocks\": [ { \"questId\": 66957, \"feature\": \"Glamour\" }, { \"questId\": 66957, \"feature\": \"Glamour plates\" } ]");
        var unlock = Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(unlockedTwice));
        Assert.Contains("66957", unlock.Message);

        var categorisedTwice = oneOccurrence.Replace("\"chroniclesKinds\": []",
            "\"chroniclesKinds\": [ { \"categoryId\": 18, \"kind\": \"AllianceRaid\" }, { \"categoryId\": 18, \"kind\": \"Trial\" } ]");
        var kind = Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(categorisedTwice));
        Assert.Contains("18", kind.Message);

        CatalogExtras.Parse(oneOccurrence);   // the same file without either duplicate still loads
    }

    [Fact]
    public void An_occurrence_with_no_genre_is_rejected()
    {
        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(0)).Replace(", EVENT2", "");
        Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json));
    }

    [Fact]
    public void The_hand_file_marks_exactly_the_reused_quest_returns()
    {
        var extras = CatalogExtras.Load();
        var reused = extras.EventOccurrences.Where(o => o.Reused).Select(o => (o.Genre, o.Festival)).OrderBy(p => p.Festival).ToList();
        // Genre 252 is the collaboration genre: Dragon Quest X (7), Final Fantasy XI (30), Yo-kai Watch (39),
        // Final Fantasy XV (84) and Blunderville (148) each reuse one set of quest rows across their returns.
        Assert.Equal([(252u, 7u), (252u, 30u), (252u, 39u), (252u, 84u), (252u, 148u)], reused);
        // Final Fantasy XVI has run once, on its own quests.
        Assert.False(extras.EventOccurrences.Single(o => o.Genre == 252 && o.Festival == 147).Reused);
    }

    [Fact]
    public void An_occurrence_without_the_reused_key_parses_as_not_reused()
    {
        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace(", EVENT2", "");
        Assert.False(CatalogExtras.Parse(json).EventOccurrences.Single().Reused);

        var flagged = MinimalHandFileTemplate
            .Replace("EVENT1", MoonfireOccurrence(246).Replace("\"year\": 2026", "\"year\": 2026, \"reused\": true"))
            .Replace(", EVENT2", "");
        Assert.True(CatalogExtras.Parse(flagged).EventOccurrences.Single().Reused);
    }

    private static string RewardOccurrence(string rewards) =>
        $$"""{ "genre": 252, "festival": 84, "event": "Final Fantasy XV collaboration", "year": 2026, "start": "2026-09-13T08:00:00Z", "end": "2026-10-18T14:59:00Z", "reused": true, "rewards": [ {{rewards}} ] }""";

    [Fact]
    public void Rewards_parse_by_kind_and_id_and_default_to_empty()
    {
        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", RewardOccurrence("""{ "kind": "mount", "id": 133 }, { "kind": "item", "id": 24234 }, { "kind": "Achievement", "id": 2241 }"""));
        var extras = CatalogExtras.Parse(json);
        var moonfire = extras.EventOccurrences.Single(o => o.Festival == 174);
        Assert.Empty(moonfire.RewardList);
        Assert.False(moonfire.HasAchievement);
        var ffxv = extras.EventOccurrences.Single(o => o.Festival == 84);
        Assert.Equal([new RewardSignal(RewardKind.Mount, 133), new RewardSignal(RewardKind.Item, 24234), new RewardSignal(RewardKind.Achievement, 2241)], ffxv.RewardList);
        Assert.True(ffxv.HasAchievement);
    }

    [Fact]
    public void A_zero_or_duplicated_reward_or_an_unknown_kind_is_rejected()
    {
        var zero = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", RewardOccurrence("""{ "kind": "mount", "id": 0 }"""));
        Assert.Contains("Final Fantasy XV collaboration 2026", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(zero)).Message);
        var twice = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", RewardOccurrence("""{ "kind": "mount", "id": 133 }, { "kind": "mount", "id": 133 }"""));
        Assert.Contains("twice", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(twice)).Message);
        var unknown = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", RewardOccurrence("""{ "kind": "sticker", "id": 1 }"""));
        Assert.ThrowsAny<Exception>(() => CatalogExtras.Parse(unknown));
    }

    [Fact]
    public void A_festival_of_zero_is_rejected()
    {
        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246).Replace("\"festival\": 174", "\"festival\": 0")).Replace(", EVENT2", "");
        var ex = Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json));
        Assert.Contains("festival 0", ex.Message);
    }

    [Fact]
    public void The_embedded_hand_file_lists_the_reward_signals()
    {
        var extras = CatalogExtras.Load();
        int Count(uint festival) => extras.EventOccurrences.Single(o => o.Genre == 252 && o.Festival == festival).RewardList.Count;
        bool Gated(uint festival) => extras.EventOccurrences.Single(o => o.Genre == 252 && o.Festival == festival).HasAchievement;
        Assert.Equal(38, Count(39));   // Yo-kai Watch: 17 minions, 3 mounts, 1 framer's kit, 17 weapon achievements
        Assert.True(Gated(39));
        Assert.Equal(9, Count(84));    // Final Fantasy XV: mount, card, 6 orchestrion rolls, hairstyle item
        Assert.False(Gated(84));
        Assert.Equal(0, Count(30));    // Final Fantasy XI: attire only, no registry
        Assert.Equal(2, Count(7));     // Dragon Quest X: minion, framer's kit
        Assert.Equal(8, Count(148));   // Blunderville: 2 minions, mount, emote, orchestrion roll, 3 framer's kits
        Assert.Equal(2, Count(147));   // Final Fantasy XVI: mount, minion
        Assert.All(extras.EventOccurrences.Where(o => o.Genre != 252), o => Assert.Empty(o.RewardList));
        Assert.Equal("A Nocturne for Heroes (Final Fantasy XV)", extras.EventOccurrences.Single(o => o.Genre == 252 && o.Festival == 84).Event);
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_reset_caps()
    {
        var caps = CatalogExtras.Load().ResetCaps;
        Assert.Equal(100, caps.LevequestAllowances);
        Assert.Equal(12, caps.AlliedSocietyAllowances);
        Assert.Equal(9, caps.WondrousTailsStickers);
        Assert.Equal(9, caps.WondrousTailsSecondChance);
    }

    [Fact]
    public void A_missing_or_impossible_reset_cap_is_rejected()
    {
        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", MoonfireOccurrence(247));
        // A renamed section lands in the loader's extension data, so the cap object is simply absent.
        var missing = json.Replace("\"resetCaps\"", "\"resetCapsOff\"");
        Assert.Contains("resetCaps", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(missing)).Message);
        var zero = json.Replace("\"levequestAllowances\": 100", "\"levequestAllowances\": 0");
        Assert.Contains("levequestAllowances", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(zero)).Message);
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_unlock_gates()
    {
        var extras = CatalogExtras.Load();
        Assert.Equal(11, extras.RouletteGates.Count);
        RouletteGate Gate(uint roulette) => extras.RouletteGates.Single(g => g.RouletteId == roulette);
        Assert.Equal(RouletteRule.Completed, Gate(1).Rule);
        Assert.Equal([4u, 2u], Gate(1).ContentIds);                       // Leveling: Sastasha and the Tam-Tara Deepcroft completed
        Assert.Equal((RouletteRule.UnlockedAtLeast, 2), (Gate(2).Rule, Gate(2).Count));
        Assert.Equal(RouletteRule.UnlockedAll, Gate(3).Rule);
        Assert.Equal((RouletteRule.UnlockedAtLeast, 2), (Gate(4).Rule, Gate(4).Count));
        Assert.Equal(RouletteRule.CompletedAll, Gate(5).Rule);
        Assert.Equal((RouletteRule.UnlockedAtLeast, 2), (Gate(6).Rule, Gate(6).Count));
        Assert.Equal((RouletteRule.UnlockedAtLeast, 1), (Gate(7).Rule, Gate(7).Count));      // taken to be UnlockedAtLeast 1; not yet confirmed in game
        Assert.Equal(RouletteRule.UnlockedAll, Gate(8).Rule);
        Assert.Equal(RouletteRule.BattleMentor, Gate(9).Rule);
        Assert.Equal((RouletteRule.UnlockedAtLeast, 1), (Gate(15).Rule, Gate(15).Count));    // taken to be UnlockedAtLeast 1; not yet confirmed in game
        Assert.Equal((RouletteRule.UnlockedAtLeast, 1), (Gate(17).Rule, Gate(17).Count));    // taken to be UnlockedAtLeast 1; not yet confirmed in game

        Assert.Equal([65756u, 66223u, 66229u], extras.LevequestUnlockQuests);
        Assert.Equal(40, extras.TreasureMapGatherLevel);
    }

    [Fact]
    public void A_malformed_roulette_gate_is_rejected_naming_the_roulette()
    {
        string Parse(string gates) => Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(MinimalHandFile(gates))).Message;
        Assert.Contains("roulette 2", Parse("""{ "rouletteId": 2, "rule": "unlockedSome", "count": 2 }"""));
        Assert.Contains("roulette 4", Parse("""{ "rouletteId": 4, "rule": "unlockedAtLeast", "count": 0 }"""));
        Assert.Contains("roulette 4", Parse("""{ "rouletteId": 4, "rule": "unlockedAtLeast" }"""));
        Assert.Contains("roulette 1", Parse("""{ "rouletteId": 1, "rule": "completed" }"""));
        Assert.Contains("roulette 1", Parse("""{ "rouletteId": 1, "rule": "completed", "contentIds": [] }"""));
        Assert.Contains("roulette 3", Parse("""{ "rouletteId": 3, "rule": "unlockedAll" }, { "rouletteId": 3, "rule": "unlockedAtLeast", "count": 1 }"""));
        Assert.Contains("rouletteGates", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(MinimalHandFile().Replace("\"rouletteGates\"", "\"rouletteGatesOff\""))).Message);

        // The same lines, each well formed, load; an empty list is allowed and leaves every roulette ungated (the builder says so).
        var loaded = CatalogExtras.Parse(MinimalHandFile("""{ "rouletteId": 1, "rule": "completed", "contentIds": [ 4, 2 ] }, { "rouletteId": 9, "rule": "battleMentor" }"""));
        Assert.Equal(2, loaded.RouletteGates.Count);
        Assert.Empty(CatalogExtras.Parse(MinimalHandFile()).RouletteGates);
    }

    [Fact]
    public void An_empty_levequest_unlock_list_or_an_impossible_gather_level_is_rejected()
    {
        var json = MinimalHandFile();
        string Parse(string variant) => Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(variant)).Message;
        Assert.Contains("levequestUnlockQuests", Parse(json.Replace("\"levequestUnlockQuests\": [ 65756 ]", "\"levequestUnlockQuests\": []")));
        Assert.Contains("levequestUnlockQuests", Parse(json.Replace("\"levequestUnlockQuests\"", "\"levequestUnlockQuestsOff\"")));
        Assert.Contains("treasureMapGatherLevel", Parse(json.Replace("\"treasureMapGatherLevel\": 40", "\"treasureMapGatherLevel\": 0")));
        Assert.Contains("treasureMapGatherLevel", Parse(json.Replace("\"treasureMapGatherLevel\": 40", "\"treasureMapGatherLevel\": 101")));
        Assert.Contains("treasureMapGatherLevel", Parse(json.Replace("\"treasureMapGatherLevel\"", "\"treasureMapGatherLevelOff\"")));
        Assert.Equal(100, CatalogExtras.Parse(json.Replace("\"treasureMapGatherLevel\": 40", "\"treasureMapGatherLevel\": 100")).TreasureMapGatherLevel);
    }

    [Fact]
    public void Every_limited_job_takes_its_own_cap_and_the_rest_take_the_level_cap()
    {
        var extras = CatalogExtras.Load();
        Assert.Equal(100, extras.LevelCap);
        Assert.Equal(80, extras.CapFor(36));    // Blue Mage
        Assert.Equal(80, extras.CapFor(43));    // Beastmaster
        Assert.Equal(100, extras.CapFor(19));   // Paladin, and every job the list does not name
        Assert.Equal(2, extras.LimitedJobCaps.Count);

        var json = MinimalHandFileTemplate.Replace("EVENT1", MoonfireOccurrence(246)).Replace("EVENT2", MoonfireOccurrence(247));
        Assert.Contains("36", Assert.Throws<InvalidOperationException>(
            () => CatalogExtras.Parse(json.Replace("{ \"jobId\": 36, \"cap\": 80 }", "{ \"jobId\": 36, \"cap\": 80 }, { \"jobId\": 36, \"cap\": 70 }"))).Message);
        Assert.Contains("101", Assert.Throws<InvalidOperationException>(
            () => CatalogExtras.Parse(json.Replace("\"cap\": 80", "\"cap\": 101"))).Message);
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_window_cactpot_fashion_and_trove_sections()
    {
        var extras = CatalogExtras.Load();
        Assert.Equal(66967u, extras.UnlockQuests.ChallengeLog);
        Assert.Equal(68617u, extras.UnlockQuests.FashionReport);
        Assert.Equal(68677u, extras.UnlockQuests.DomanEnclave);
        Assert.Equal(68734u, extras.UnlockQuests.MaskedCarnivale);
        Assert.Equal(66024u, extras.UnlockQuests.MiniCactpot);
        Assert.Equal(66025u, extras.UnlockQuests.JumboCactpot);
        Assert.Equal(3, extras.ResetCaps.MiniCactpotTickets);
        Assert.Equal(3, extras.ResetCaps.JumboCactpotTickets);
        Assert.Equal((4, 80), (extras.FashionReport.Attempts, extras.FashionReport.RewardScore));
        var trove = Assert.Single(extras.Troves);
        Assert.Equal(51750u, trove.ItemId);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.Zero), trove.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 14, 59, 0, TimeSpan.Zero), trove.End);
    }

    [Fact]
    public void A_bad_unlock_quest_trove_or_fashion_rule_is_rejected()
    {
        var json = MinimalHandFile();
        Assert.Contains("unlockQuests", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"unlockQuests\"", "\"unlockQuestsOff\""))).Message);
        Assert.Contains("miniCactpot", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"miniCactpot\": 66024", "\"miniCactpot\": 24"))).Message);   // below 65536 is no quest row
        Assert.Contains("troves", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"troves\"", "\"trovesOff\""))).Message);
        Assert.Contains("51750", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"end\": \"2026-10-19T14:59:00Z\"", "\"end\": \"2026-09-01T14:59:00Z\""))).Message);
        Assert.Contains("rewardScore", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"rewardScore\": 80", "\"rewardScore\": 0"))).Message);
        Assert.Contains("miniCactpotTickets", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"miniCactpotTickets\": 3", "\"miniCactpotTickets\": 0"))).Message);
    }

    [Fact]
    public void A_trove_with_item_zero_or_listed_twice_is_rejected()
    {
        var json = MinimalHandFile();
        Assert.Contains("item 0", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(json.Replace("\"itemId\": 51750", "\"itemId\": 0"))).Message);
        var twice = json.Replace(
            "\"troves\": [ { \"itemId\": 51750, \"start\": \"2026-09-09T08:00:00Z\", \"end\": \"2026-10-19T14:59:00Z\" } ]",
            "\"troves\": [ { \"itemId\": 51750, \"start\": \"2026-09-09T08:00:00Z\", \"end\": \"2026-10-19T14:59:00Z\" }, { \"itemId\": 51750, \"start\": \"2026-11-01T08:00:00Z\", \"end\": \"2026-12-01T14:59:00Z\" } ]");
        Assert.Contains("twice", Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(twice)).Message);
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_content_unlock_and_variant_sections()
    {
        var extras = CatalogExtras.Load();
        Assert.Equal(65970u, extras.ContentUnlockQuests.GoldSaucer);
        Assert.Equal([66640u, 66641u, 66642u], extras.ContentUnlockQuests.Pvp);
        Assert.Equal([(1u, 36001u), (2u, 36002u), (3u, 36003u), (4u, 36004u)], extras.VariantSeries.Select(v => (v.SeriesId, v.ContentId)));
    }

    [Fact]
    public void A_bad_content_unlock_or_variant_series_line_is_rejected()
    {
        var json = MinimalHandFile();
        string Parse(string variant) => Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(variant)).Message;
        Assert.Contains("contentUnlockQuests", Parse(json.Replace("\"contentUnlockQuests\"", "\"contentUnlockQuestsOff\"")));
        Assert.Contains("goldSaucer", Parse(json.Replace("\"goldSaucer\": 65970", "\"goldSaucer\": 970")));          // below 65536 is no quest row
        Assert.Contains("pvp", Parse(json.Replace("\"pvp\": [ 66640, 66641, 66642 ]", "\"pvp\": []")));
        Assert.Contains("pvp", Parse(json.Replace("\"pvp\": [ 66640, 66641, 66642 ]", "\"pvpOff\": []")));   // a missing key names the key, not a null reference
        Assert.Contains("pvp", Parse(json.Replace("66641", "641")));
        Assert.Contains("variantSeries", Parse(json.Replace("\"variantSeries\"", "\"variantSeriesOff\"")));
        Assert.Contains("series 2", Parse(json.Replace("{ \"seriesId\": 2, \"contentId\": 36002 }", "{ \"seriesId\": 2, \"contentId\": 0 }")));
        Assert.Contains("series 1", Parse(json.Replace("{ \"seriesId\": 2, \"contentId\": 36002 }", "{ \"seriesId\": 1, \"contentId\": 36002 }")));
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_place_unlock_and_maxima_sections()
    {
        var extras = CatalogExtras.Load();
        var quests = extras.PlaceUnlockQuests;
        Assert.Equal((68614u, 69477u, 70847u, 70789u, 70179u), (quests.Eureka, quests.Bozja, quests.OccultCrescent, quests.CosmicExploration, quests.IslandSanctuary));
        Assert.Equal([(1u, 67092u), (2u, 68667u), (3u, 70199u), (4u, 70944u)], quests.DeepDungeons.Select(d => (d.DungeonId, d.QuestId)));
        Assert.Equal(new ContentMaxima(60, 25, 40, 99, 50), extras.ContentMaxima);
        Assert.Equal(new HouseVisitRules(45, 30), extras.HouseVisit);
        Assert.Equal(TimeSpan.FromDays(15), extras.HouseVisit.RemindWithin);   // the game's own 30-day warning, 15 days before demolition
    }

    [Fact]
    public void A_bad_place_quest_maximum_or_house_visit_line_is_rejected()
    {
        var json = MinimalHandFile();
        string Parse(string variant) => Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(variant)).Message;
        Assert.Contains("placeUnlockQuests", Parse(json.Replace("\"placeUnlockQuests\"", "\"placeUnlockQuestsOff\"")));
        Assert.Contains("eureka", Parse(json.Replace("\"eureka\": 68614", "\"eureka\": 614")));                     // below 65536 is no quest row
        Assert.Contains("deepDungeons", Parse(json.Replace("\"deepDungeons\": [ { \"dungeonId\": 1, \"questId\": 67092 } ]", "\"deepDungeons\": []")));
        Assert.Contains("dungeon 1", Parse(json.Replace("{ \"dungeonId\": 1, \"questId\": 67092 }", "{ \"dungeonId\": 1, \"questId\": 92 }")));
        Assert.Contains("twice", Parse(json.Replace("{ \"dungeonId\": 1, \"questId\": 67092 }", "{ \"dungeonId\": 1, \"questId\": 67092 }, { \"dungeonId\": 1, \"questId\": 68667 }")));
        Assert.Contains("contentMaxima", Parse(json.Replace("\"contentMaxima\"", "\"contentMaximaOff\"")));
        Assert.Contains("aetherpool", Parse(json.Replace("\"aetherpool\": 99", "\"aetherpool\": 0")));
        Assert.Contains("houseVisit", Parse(json.Replace("\"houseVisit\"", "\"houseVisitOff\"")));
        Assert.Contains("warningDays", Parse(json.Replace("\"warningDays\": 30", "\"warningDays\": 45")));        // a warning must come before demolition
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_collection_unlock_section()
    {
        var quests = CatalogExtras.Load().CollectionUnlockQuests;
        Assert.Equal([66236u, 66237u, 66238u], quests.Bardings);
        Assert.Equal((65973u, 67643u), (quests.TripleTriad, quests.SightseeingHeavensward));
    }

    [Fact]
    public void A_bad_collection_unlock_line_is_rejected()
    {
        var json = MinimalHandFile();
        string Parse(string variant) => Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(variant)).Message;
        Assert.Contains("collectionUnlockQuests", Parse(json.Replace("\"collectionUnlockQuests\"", "\"collectionUnlockQuestsOff\"")));
        Assert.Contains("bardings", Parse(json.Replace("\"bardings\": [ 66236, 66237, 66238 ]", "\"bardings\": []")));
        Assert.Contains("bardings", Parse(json.Replace("\"bardings\": [ 66236, 66237, 66238 ]", "\"bardingsOff\": []")));   // a missing key names the key
        Assert.Contains("bardings", Parse(json.Replace("66237", "237")));                                                 // below 65536 is no quest row
        Assert.Contains("tripleTriad", Parse(json.Replace("\"tripleTriad\": 65973", "\"tripleTriad\": 973")));
        Assert.Contains("sightseeingHeavensward", Parse(json.Replace("\"sightseeingHeavensward\": 67643", "\"sightseeingHeavensward\": 0")));
    }

    [Fact]
    public void The_embedded_hand_file_carries_the_storage_section()
    {
        var storage = CatalogExtras.Load().Storage;
        Assert.Equal((175, 70, 70, 800), (storage.RetainerInventory, storage.Saddlebag, storage.PremiumSaddlebag, storage.GlamourDresser));
        Assert.Equal((66698u, 22u), (storage.SaddlebagQuest, storage.CastGlamourAction));
    }

    [Fact]
    public void A_bad_storage_line_is_rejected()
    {
        var json = MinimalHandFile();
        string Parse(string variant) => Assert.Throws<InvalidOperationException>(() => CatalogExtras.Parse(variant)).Message;
        Assert.Contains("storage", Parse(json.Replace("\"storage\"", "\"storageOff\"")));
        Assert.Contains("glamourDresser", Parse(json.Replace("\"glamourDresser\": 800", "\"glamourDresser\": 0")));
        Assert.Contains("retainerInventory", Parse(json.Replace("\"retainerInventory\": 175", "\"retainerInventoryOff\": 175")));   // a missing key reads 0 and is named
        Assert.Contains("saddlebagQuest", Parse(json.Replace("\"saddlebagQuest\": 66698", "\"saddlebagQuest\": 698")));            // below 65536 is no quest row
        Assert.Contains("castGlamourAction", Parse(json.Replace("\"castGlamourAction\": 22", "\"castGlamourAction\": 0")));
    }
}
