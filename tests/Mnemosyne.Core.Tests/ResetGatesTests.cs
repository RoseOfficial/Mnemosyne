using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The unlock gates as pure rules over reads the reader supplies. Each member rule is tried below, at and above its
/// threshold; the reader's own "locked clears, unreadable keeps" step is exercised end to end by the level-two test at the end of
/// this file.</summary>
public class ResetGatesTests
{
    private static readonly IReadOnlyList<uint> Members = [10, 11, 12];
    private static readonly Func<uint, bool> None = _ => false;
    private static Func<uint, bool> Only(params uint[] ids) => id => ids.Contains(id);

    [Fact]
    public void Unlocked_at_least_counts_unlocked_members_against_the_threshold()
    {
        var gate = new RouletteGate(2, RouletteRule.UnlockedAtLeast, 2);
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10), None, false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11), None, false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12), None, false));
        // Completion is not unlocking: a member the client reports completed but not unlocked does not count.
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10), Only(10, 11), false));
        // A duty outside the roulette does not count either, however many are unlocked.
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10, 20, 21), None, false));
    }

    [Fact]
    public void Unlocked_all_needs_every_member_and_nothing_more()
    {
        var gate = new RouletteGate(3, RouletteRule.UnlockedAll);
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10, 11), None, false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12), None, false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12, 13), None, false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12), None, true));   // mentor status is not asked
    }

    [Fact]
    public void Completed_all_needs_every_member_both_unlocked_and_completed()
    {
        var gate = new RouletteGate(5, RouletteRule.CompletedAll);
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12), Only(10, 11), false));
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10, 11), Only(10, 11, 12), false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12), Only(10, 11, 12), false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12, 13), Only(10, 11, 12, 13), false));
    }

    [Fact]
    public void Completed_reads_its_own_listed_duties_not_the_roulettes_members()
    {
        var gate = new RouletteGate(1, RouletteRule.Completed, ContentIds: [4, 2]);
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(4, 2), Only(4), false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, None, Only(4, 2), false));
        Assert.True(ResetGates.RouletteOpen(gate, Members, None, Only(4, 2, 10), false));
        Assert.True(ResetGates.RouletteOpen(gate, [], None, Only(4, 2), false));   // the member list is not consulted
    }

    [Fact]
    public void Battle_mentor_asks_the_player_state_alone()
    {
        var gate = new RouletteGate(9, RouletteRule.BattleMentor);
        Assert.False(ResetGates.RouletteOpen(gate, Members, Only(10, 11, 12), Only(10, 11, 12), false));
        Assert.True(ResetGates.RouletteOpen(gate, [], None, None, true));
    }

    [Fact]
    public void An_ungated_roulette_is_open_and_a_member_rule_with_no_members_cannot_be_judged()
    {
        // No hand-file line: the roulette reads as it did before the gates, and the builder has said so.
        Assert.True(ResetGates.RouletteOpen(null, [], None, None, false));
        // The sheet gave the roulette no members: a reduction that went wrong, not a locked character, so the reader
        // leaves the row alone rather than clearing it.
        Assert.Null(ResetGates.RouletteOpen(new RouletteGate(2, RouletteRule.UnlockedAtLeast, 1), [], Only(10), None, false));
        Assert.Null(ResetGates.RouletteOpen(new RouletteGate(3, RouletteRule.UnlockedAll), [], Only(10), None, false));
        Assert.Null(ResetGates.RouletteOpen(new RouletteGate(5, RouletteRule.CompletedAll), [], Only(10), Only(10), false));
        Assert.Null(ResetGates.RouletteOpen(new RouletteGate(1, RouletteRule.Completed), Members, None, Only(4, 2), false));
    }

    [Fact]
    public void The_delivery_total_cap_is_the_smaller_of_the_clients_read_and_the_unlocked_clients_sum()
    {
        Assert.Null(ResetGates.DeliveryTotalCap(12, []));                // no client unlocked: the total row clears
        Assert.Equal(6, ResetGates.DeliveryTotalCap(12, [6]));           // one client could never reach 12 of 12
        Assert.Equal(12, ResetGates.DeliveryTotalCap(12, [6, 6, 6]));    // three clients: the client's own twelve wins
        Assert.Equal(10, ResetGates.DeliveryTotalCap(10, [6, 6, 6]));    // and a lower read is never raised
    }

    private static ClassJobRow Job(uint id, int dohDol = -1, bool limited = false) => new(id, "JOB", "job", (int)id, 0, 0, id, dohDol, limited, 0);

    [Fact]
    public void The_tomestone_opens_on_a_non_limited_combat_job_at_the_cap()
    {
        var jobs = new[] { Job(36, limited: true), Job(8, dohDol: 0), Job(19) };   // Blue Mage, Carpenter, Paladin
        Assert.False(ResetGates.TomestoneOpen(jobs, j => j.RowId == 36 ? 100 : 50, 100));   // a limited job at the cap does not count
        Assert.False(ResetGates.TomestoneOpen(jobs, j => j.RowId == 8 ? 100 : 50, 100));    // a crafter at the cap does not count
        Assert.False(ResetGates.TomestoneOpen(jobs, j => j.RowId == 19 ? 99 : 100, 100));   // the combat job is one short
        Assert.True(ResetGates.TomestoneOpen(jobs, j => j.RowId == 19 ? 100 : 1, 100));
        Assert.False(ResetGates.TomestoneOpen([], _ => 100, 100));
    }

    [Fact]
    public void The_map_opens_when_the_highest_gatherer_reaches_the_hand_files_level()
    {
        Assert.False(ResetGates.MapOpen([39, 1, 0], 40));
        Assert.True(ResetGates.MapOpen([40, 1, 0], 40));
        Assert.True(ResetGates.MapOpen([0, 0, 41], 40));
        Assert.False(ResetGates.MapOpen([], 40));
        Assert.Equal([16u, 17u, 18u], ResetGates.GathererClassJobs);   // Miner, Botanist, Fisher
    }

    [Fact]
    public void A_challenge_entry_opens_on_its_unlock_and_its_level()
    {
        // The whole log on one quest, then each entry on its own unlock link or quest and its level requirement.
        Assert.True(ResetGates.ChallengeEntryOpen(unlocked: true, levelUnlock: 0, highestLevel: 1));
        Assert.False(ResetGates.ChallengeEntryOpen(false, 0, 100));
        Assert.False(ResetGates.ChallengeEntryOpen(true, 60, 59));
        Assert.True(ResetGates.ChallengeEntryOpen(true, 60, 60));
        Assert.Null(ResetGates.ChallengeEntryOpen(true, 60, null));      // no job level could be read: not judged
        Assert.Equal([8u, 9u, 10u, 11u, 12u, 13u, 14u, 15u], ResetGates.CrafterClassJobs);
    }

    /// <summary>A brand-new level-2 character: 28 stored Resets values, 27 of them in the top groups, every one a thing it cannot
    /// do. The reader now answers each with a clearing reading; this is the whole effect of those readings through the merge and
    /// the assembler, on the real catalog.</summary>
    [Fact]
    public void Clearing_every_gated_row_of_the_level_two_character_empties_the_top_groups()
    {
        var catalog = CatalogBuilder.Build(new FixtureCatalogSource().Load(), CatalogExtras.Load());
        var now = Items.Now;
        var record = Items.Record();
        var resets = catalog.InCategory(Category.Resets);
        foreach (var roulette in resets.Where(i => i.Id.StartsWith("reset:roulette:", StringComparison.Ordinal)))
            record.Values[roulette.Id] = new DoneValue(false);
        foreach (var client in resets.Where(i => i.Id.StartsWith("reset:delivery:", StringComparison.Ordinal)))
            record.Values[client.Id] = new CounterValue(0, client.Id == "reset:delivery:all" ? 12 : 6, now);
        record.Values["reset:tomestone:weekly"] = new CounterValue(0, 900, now);
        record.Values["reset:tribe-allowance"] = new CounterValue(0, 12, now);
        record.Values["reset:leve-allowance"] = new CounterValue(0, 100, now);
        record.Values["reset:treasure-map"] = new TimerValue(now + TimeSpan.FromSeconds(2));
        Assert.Equal(28, record.Values.Count);

        // 27 items: 11 roulettes, the allowance and the map today or ending soon; 13 delivery rows and the tomestone this week.
        // The levequest counter sits below its cap and so in no group. The other two are the cactpot rows, which read "not
        // seen" with nothing stored and sit in Today and This week — the defect the locks below fix.
        var before = Assembler.Assemble(catalog, record, new Settings(), now);
        Assert.Equal(29, before.Badge);

        var clearing = record.Values.Keys.Select(id => new Reading(id, null)).ToList();
        Assert.True(RecordUpdater.Merge(record, Category.Resets, clearing, now));
        Assert.Empty(record.Values);

        // The level-2 character has none of the unlock quests, so the window readers and the cactpot gate store a lock on
        // every NeedsWindow and ObservedOnly row; without it "not seen" would put the cactpots in Today and "needs:" would
        // put the Fashion Report in Running events.
        var locks = resets.Where(i => i.Freshness.Kind is FreshnessKind.NeedsWindow or FreshnessKind.ObservedOnly).Select(i => new Reading(i.Id, GateValue.Shut)).ToList();
        Assert.Equal(101, locks.Count);
        Assert.True(RecordUpdater.Merge(record, Category.Resets, locks, now));

        var after = Assembler.Assemble(catalog, record, new Settings(), now);
        Assert.Equal(0, after.Badge);
        Assert.Empty(after.Today);
        Assert.Empty(after.ThisWeek);
        Assert.Empty(after.EndingSoon);
        // The fixture's open Yo-kai Watch run (Events, gated on Achievements, nothing stored) waits in Running events; no
        // Resets row does.
        Assert.DoesNotContain(after.Running, e => e.Item.Category == Category.Resets);
        var entries = after.Sections.Single(s => s.Category == Category.Resets).Groups.SelectMany(g => g.Entries).ToList();
        Assert.Equal(167, entries.Count);   // every row is still listed
        Assert.All(entries, e =>
        {
            Assert.Equal("", e.Display);
            Assert.Equal("", e.Marker);
            Assert.Equal(EntryState.NotDone, e.State);
        });
        Assert.All(entries.Where(e => e.Item.Freshness.Kind == FreshnessKind.AtLogin), e => Assert.Null(e.Value));
        Assert.All(entries.Where(e => e.Item.Freshness.Kind != FreshnessKind.AtLogin), e => Assert.IsType<GateValue>(e.Value));
    }
}
