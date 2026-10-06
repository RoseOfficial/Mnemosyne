using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The Content category's pure rules, each tried at and around its edge. The reader's own "locked clears,
/// unreadable keeps" step reads the client and is checked in game; where its readings land in the checklist is proven on the
/// real catalog by Content_rows_are_information_never_attention in CatalogBuilderTests.</summary>
public class ContentRulesTests
{
    [Fact]
    public void A_duty_is_done_on_its_completion_bit_whatever_its_unlock_bit_says()
    {
        Assert.Equal(DutyState.Locked, ContentRules.Duty(unlocked: false, completed: false));
        Assert.Equal(DutyState.NotDone, ContentRules.Duty(true, false));
        Assert.Equal(DutyState.Done, ContentRules.Duty(true, true));
        Assert.Equal(DutyState.Done, ContentRules.Duty(false, true));   // an Unreal trial that rotated out after the clear
    }

    [Fact]
    public void Variant_records_count_the_bit_below_each_row_id()
    {
        var series = new uint[] { 1, 2, 3, 12 };
        Assert.Equal(2, ContentRules.VariantRecords(series, bit => bit is 0 or 11));   // rows 1 and 12
        Assert.Equal(0, ContentRules.VariantRecords(series, bit => bit == 12));         // bit 12 is row 13, not this series
        Assert.Equal(0, ContentRules.VariantRecords(series, _ => null));                // an array that cannot answer counts nothing
        Assert.Equal(1, ContentRules.VariantRecords([0, 49], bit => bit == 48));        // a zero link is skipped; row 49 is bit 48
    }

    private static IReadOnlyList<HuntingNoteRow> Notes(int ranks) =>
        Enumerable.Range(0, ranks).SelectMany(rank => Enumerable.Range(0, 10).Select(note => new HuntingNoteRow(rank, note, [3, note == 9 ? 2 : 0, 0, 0]))).ToList();

    [Fact]
    public void A_hunting_log_counts_ten_per_rank_passed_plus_the_finished_notes_of_the_rank_in_progress()
    {
        var notes = Notes(5);
        // Rank 1 in progress: notes 0 to 3 have their three kills; note 9 has its first target but not its second.
        int Kills(int note, int target) => target == 0 ? (note <= 3 || note == 9 ? 3 : 1) : 0;
        Assert.Equal(14, ContentRules.HuntingLogDone(1, notes, 5, Kills));
        Assert.Equal(0, ContentRules.HuntingLogDone(0, notes, 5, (_, _) => 0));
        Assert.Equal(10, ContentRules.HuntingLogDone(0, notes, 5, (_, _) => 99));      // every note of rank 0 met, rank not yet advanced
        Assert.Equal(50, ContentRules.HuntingLogDone(5, notes, 5, (_, _) => 0));       // a finished log reads its cap
        Assert.Equal(50, ContentRules.HuntingLogDone(7, notes, 5, (_, _) => 0));       // and never above it
        Assert.Equal(30, ContentRules.HuntingLogDone(3, Notes(3), 3, (_, _) => 0));    // a company log has three ranks
        Assert.Equal(0, ContentRules.HuntingLogDone(-1, notes, 5, (_, _) => 99));      // an unread rank counts nothing
        // A note whose sheet row is missing is reduced with no needed kills and is never counted done, however many kills stand.
        var missing = notes.Select(n => n.Rank == 0 && n.Note == 5 ? n with { NeededKills = [] } : n).ToList();
        Assert.Equal(9, ContentRules.HuntingLogDone(0, missing, 5, (_, _) => 99));
    }

    [Fact]
    public void Series_rewards_claimed_are_judged_against_the_levels_earned_up_to_the_maximum()
    {
        Assert.Equal((0, 0), ContentRules.PvpClaimed(0, 0, 30));
        Assert.Equal((3, 7), ContentRules.PvpClaimed(7, 3, 30));
        Assert.Equal((7, 7), ContentRules.PvpClaimed(7, 7, 30));
        Assert.Equal((30, 30), ContentRules.PvpClaimed(35, 30, 30));   // extra levels have nothing to claim
        Assert.Equal((7, 7), ContentRules.PvpClaimed(7, 9, 30));       // a claim above the earned level is clamped, never a negative gap

        var levels = Enumerable.Range(0, 31).Select(i => new PvPSeriesLevelRow((uint)i, i == 0 ? 0 : i == 30 ? 20000 : 1000 + i)).ToList();
        Assert.Equal(0, ContentRules.PvpExpToNext(0, levels));
        Assert.Equal(1004, ContentRules.PvpExpToNext(4, levels));     // the current level's own row, not level + 1's (1005)
        Assert.Equal(1029, ContentRules.PvpExpToNext(29, levels));    // level 29's own row, not row 30's 20,000
        Assert.Equal(20000, ContentRules.PvpExpToNext(30, levels));
        Assert.Equal(20000, ContentRules.PvpExpToNext(35, levels));   // extra levels cost what level 30 does
        Assert.Null(ContentRules.PvpExpToNext(3, []));
    }

    [Fact]
    public void An_estate_is_labelled_by_district_ward_and_plot_or_room()
    {
        // The label replaces the row's name when drawn (Entry.Name), so it carries the estate's name too, or the free company
        // estate and the private chambers in it would read as one address with nothing telling them apart.
        Assert.Equal("Personal estate: Mist, ward 3, plot 12", ContentRules.HouseLabel("Personal estate", "Mist", 2, 11, false, 0));
        Assert.Equal("Apartment: Shirogane, ward 1, room 42", ContentRules.HouseLabel("Apartment", "Shirogane", 0, 0, true, 42));
        Assert.Equal("Private chambers: Mist, ward 3, plot 12, room 7", ContentRules.HouseLabel("Private chambers", "Mist", 2, 11, false, 7));
    }

    [Fact]
    public void A_place_level_is_nothing_until_the_content_holds_it()
    {
        // GetContentValue answers 0 for a key it does not hold; an island with no data and a racing manager not loaded read rank 0.
        // A zero is never a level, so it can never overwrite the last visit's value.
        Assert.Null(ContentRules.Level(0, 60));
        Assert.Null(ContentRules.Level(-1, 60));
        Assert.Null(ContentRules.Level(12, 0));                                         // no cap to judge against
        Assert.Equal(new LevelValue(34, 60, 1200, 5000), ContentRules.Level(34, 60, 1200, 5000));
        Assert.Equal(new LevelValue(34, 60), ContentRules.Level(34, 60));               // no director state: the level alone
        Assert.Equal(new LevelValue(34, 60), ContentRules.Level(34, 60, 1200, 0));      // nothing needed is no step to show
        Assert.Equal(new LevelValue(60, 60), ContentRules.Level(60, 60, 0, 0));         // at the cap there is no next step
    }

    [Fact]
    public void The_island_rank_is_read_only_for_the_characters_own_island()
    {
        var ranks = Enumerable.Range(0, 21).Select(i => new IslandRankRow((uint)i, i == 20 ? 0 : 1000 * i)).ToList();
        Assert.Equal(new LevelValue(5, 20, 1200, 5000), ContentRules.IslandRank(true, 5, 1200, ranks));   // the rank's own row is the step to the next
        Assert.Null(ContentRules.IslandRank(false, 5, 1200, ranks));                                        // someone else's island
        Assert.Null(ContentRules.IslandRank(true, 0, 0, ranks));                                            // no island data held
        Assert.Equal(new LevelValue(20, 20), ContentRules.IslandRank(true, 20, 0, ranks));                  // the cap is the highest row
        Assert.Null(ContentRules.IslandRank(true, 5, 1200, []));                                            // no rank table, no cap
    }

    [Fact]
    public void A_house_visit_deadline_is_forty_five_days_after_the_minute_the_character_was_last_seen_inside()
    {
        var seen = new DateTimeOffset(2026, 9, 13, 12, 34, 56, 789, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 12, 34, 0, TimeSpan.Zero), ContentRules.HouseVisitDeadline(seen, 45));
        // Every tick inside the same minute gives the same deadline, so an hour inside rewrites the record once a minute.
        Assert.Equal(ContentRules.HouseVisitDeadline(seen, 45), ContentRules.HouseVisitDeadline(seen.AddSeconds(3), 45));
        Assert.NotEqual(ContentRules.HouseVisitDeadline(seen, 45), ContentRules.HouseVisitDeadline(seen.AddSeconds(4), 45));   // the next minute
        // The instant is UTC whatever offset it arrives with.
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 12, 34, 0, TimeSpan.Zero), ContentRules.HouseVisitDeadline(new DateTimeOffset(2026, 9, 13, 21, 34, 56, TimeSpan.FromHours(9)), 45));
    }

    [Fact]
    public void A_house_matches_on_its_whole_id_or_on_world_ward_and_plot()
    {
        // Fictional packed ids; only equality matters to the rule. An interior's territory may differ from its ward's, so the
        // same world, ward and plot is taken to be a match too; not yet confirmed in game. Anything else is someone else's house.
        var own = new HouseKey(0x0049_0153_000B_0203, 73, 2, 11, false);
        Assert.Equal(HouseMatch.WholeId, ContentRules.SameHouse(own, own));
        Assert.Equal(HouseMatch.WorldWardPlot, ContentRules.SameHouse(own, own with { Id = 0x0049_0122_000B_0203 }));
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own, new HouseKey(0x0049_0153_000C_0203, 73, 2, 12, false)));   // the free company house next door
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own, new HouseKey(0x004A_0153_000B_0203, 74, 2, 11, false)));   // the same plot on another world
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own, new HouseKey(0x0049_0153_000D_0203, 73, 3, 11, false)));   // the same plot in another ward
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own, own with { Id = 0x0049_0153_000B_0280, IsApartment = true }));   // an apartment is exempt
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own with { IsApartment = true }, own));   // and so is an owned one: an owned apartment never stamps, whatever house the character stands in
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own, own with { Id = 0 }));                                           // no house
        Assert.Equal(HouseMatch.None, ContentRules.SameHouse(own with { Id = 0 }, own with { Id = 0 }));
    }

    [Fact]
    public void A_gate_that_can_close_again_is_believed_closed_on_the_second_read()
    {
        // "No personal estate" and "no racing chocobo" clear a stored value, and the house's stamp comes back only with the next
        // visit inside. One frame that reads the sentinel (a login frame before the ownership table fills) must not erase it, so
        // a closed answer is believed on a second closed read, and only one at least a second after the last counted one: two
        // reads of one frame, or of the two frames a loading screen collapses into, count once. Any other answer resets the count.
        var closedReads = 0;
        var lastClosedAt = default(DateTimeOffset);
        var t = Items.Now;
        Assert.True(ContentRules.RevocableGate(true, ref closedReads, ref lastClosedAt, t));
        Assert.Null(ContentRules.RevocableGate(null, ref closedReads, ref lastClosedAt, t));                                    // unreadable: not judged
        Assert.Null(ContentRules.RevocableGate(false, ref closedReads, ref lastClosedAt, t));                                   // the first "none": not believed
        Assert.Null(ContentRules.RevocableGate(false, ref closedReads, ref lastClosedAt, t + TimeSpan.FromMilliseconds(16)));   // one frame on: still not
        Assert.False(ContentRules.RevocableGate(false, ref closedReads, ref lastClosedAt, t + TimeSpan.FromSeconds(5)));        // the next live tick: closed
        Assert.False(ContentRules.RevocableGate(false, ref closedReads, ref lastClosedAt, t + TimeSpan.FromSeconds(10)));       // and it stays closed
        Assert.True(ContentRules.RevocableGate(true, ref closedReads, ref lastClosedAt, t + TimeSpan.FromSeconds(15)));         // an open read resets the count
        Assert.Null(ContentRules.RevocableGate(false, ref closedReads, ref lastClosedAt, t + TimeSpan.FromSeconds(20)));        // so a new close starts over

        // Two closed reads of one frame keep the stored value: neither reading they produce touches the record.
        var record = Items.Record();
        record.Values["timer:housing"] = new TimerValue(Items.Now.AddDays(30));
        closedReads = 0;
        foreach (var instant in new[] { Items.Now, Items.Now + TimeSpan.FromMilliseconds(3) })
            RecordUpdater.Merge(record, Category.Content, Readings.Gated("timer:housing", ContentRules.RevocableGate(false, ref closedReads, ref lastClosedAt, instant), null, revocable: true), instant);
        Assert.Equal(new TimerValue(Items.Now.AddDays(30)), record.Values["timer:housing"]);
    }

    [Fact]
    public void A_revocable_gates_count_does_not_cross_a_character_switch()
    {
        // The count is kept per character: RevocableGateState keys it on the content id, so a switch starts it over, and a
        // fresh character's login-frame sentinel never inherits a count the previous character had already run up to two
        // believed closed reads.
        var state = new RevocableGateState();
        var t = Items.Now;
        const ulong characterA = 1, characterB = 2;

        // Character A: a first closed read is not believed, a second a second later is.
        Assert.Null(state.Evaluate(characterA, false, t));
        Assert.False(state.Evaluate(characterA, false, t + TimeSpan.FromSeconds(1)));

        // Switch to character B. The new character's first closed read does not inherit A's count of two: it is not believed
        // either, and a stored timer survives the merge it produces, exactly as a fresh character's first closed read always does.
        var record = Items.Record(characterB);
        record.Values["timer:housing"] = new TimerValue(Items.Now.AddDays(30));
        var firstB = state.Evaluate(characterB, false, t + TimeSpan.FromSeconds(2));
        Assert.Null(firstB);
        RecordUpdater.Merge(record, Category.Content, Readings.Gated("timer:housing", firstB, null, revocable: true), t + TimeSpan.FromSeconds(2));
        Assert.Equal(new TimerValue(Items.Now.AddDays(30)), record.Values["timer:housing"]);

        // B's second closed read, a second after its first, is believed, exactly as A's was.
        Assert.False(state.Evaluate(characterB, false, t + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void A_grand_company_pvp_rank_reads_over_the_sheets_cap_with_its_band_and_clears_when_never_ranked()
    {
        // PvPRank's ExpRequired at the rows the sheet holds, taken to be read as cumulative totals; not yet confirmed in game.
        // The cap is the highest row with experience required, 50; the band title is the company's own column.
        static string[] Band(string band) => [$"Storm {band}", $"Serpent {band}", $"Flame {band}"];
        var ranks = new List<PvPRankRow>
        {
            new(0, 0, Band("Puppy")), new(1, 10, Band("Puppy")), new(2, 40, Band("Puppy")), new(10, 1810, Band("Howler")), new(11, 2350, Band("Howler")),
            new(49, 212590, Band("Stalker")), new(50, 226210, Band("Chaser")), new(51, 0, Band("Chaser")),
        };
        Assert.Null(ContentRules.PvpRank(0, 0, 1, ranks));                                                     // never ranked with the Maelstrom: the row clears
        Assert.Equal((new LevelValue(10, 50, 200, 540), "Serpent Howler"), ContentRules.PvpRank(10, 2010, 2, ranks));  // 2,010 − 1,810 of 2,350 − 1,810
        Assert.Equal((new LevelValue(50, 50), "Flame Chaser"), ContentRules.PvpRank(50, 230000, 3, ranks));      // the cap: no step to show
        Assert.Equal((new LevelValue(50, 50), ""), ContentRules.PvpRank(60, 230000, 3, ranks));                 // a rank byte past the cap reads the cap; this table has no row 60 to name it
        Assert.Equal((new LevelValue(1, 50, 0, 30), "Storm Puppy"), ContentRules.PvpRank(1, 5, 1, ranks));        // below the rank's own total: clamped, never negative
        Assert.Null(ContentRules.PvpRank(3, 100, 1, []));                                                      // no table, no cap
    }

    [Fact]
    public void The_crystalline_conflict_tier_names_its_tier_and_season_and_clears_with_none()
    {
        // A Level over the highest ColosseumMatchRank row, the byte taken to be read as the row; not yet confirmed in game.
        // The season names a stale value, the riser below Crystal and the Crystal Credit from it up.
        IReadOnlyList<ColosseumRankRow> tiers = new[] { "-", "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Crystal", "Omega", "Ultima" }
            .Select((name, row) => new ColosseumRankRow((uint)row, name)).ToList();
        const string Name = "Crystalline Conflict tier";
        Assert.Equal((new LevelValue(3, 8), "Crystalline Conflict tier: Gold 2, season 14"), ContentRules.CrystallineTier(Name, 14, 3, 2, 0, tiers));
        Assert.Equal((new LevelValue(6, 8), "Crystalline Conflict tier: Crystal 1,250 credit, season 14"), ContentRules.CrystallineTier(Name, 14, 6, 0, 1250, tiers));
        Assert.Equal((new LevelValue(8, 8), "Crystalline Conflict tier: Ultima 1,900 credit, season 14"), ContentRules.CrystallineTier(Name, 14, 8, 0, 1900, tiers));   // the top tier reads done
        Assert.Null(ContentRules.CrystallineTier(Name, 0, 3, 2, 0, tiers));   // no season
        Assert.Null(ContentRules.CrystallineTier(Name, 14, 0, 0, 0, tiers));  // unranked this season
        Assert.Null(ContentRules.CrystallineTier(Name, 14, 3, 2, 0, []));     // no tier table
    }
}
