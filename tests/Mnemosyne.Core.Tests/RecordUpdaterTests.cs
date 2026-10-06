using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

public class RecordUpdaterTests
{
    [Fact]
    public void A_reading_with_an_instant_stamps_the_item_and_one_without_leaves_it()
    {
        var r = Items.Record();
        var t1 = Items.Now;
        var t2 = Items.Now + TimeSpan.FromMinutes(5);
        RecordUpdater.Merge(r, Category.Collections, [new Reading("achievement:1", new DoneValue(true), t1)], t1);
        Assert.Equal(t1, r.ItemReadAt["achievement:1"]);
        RecordUpdater.Merge(r, Category.Collections, [new Reading("achievement:1", new DoneValue(true))], t2);
        Assert.Equal(t1, r.ItemReadAt["achievement:1"]);
        RecordUpdater.Merge(r, Category.Collections, [new Reading("achievement:1", new DoneValue(true), t2)], t2);
        Assert.Equal(t2, r.ItemReadAt["achievement:1"]);
        Assert.Equal(t2, r.CategoryReadAt["Collections"]);
    }

    [Fact]
    public void Item_read_instants_round_trip_through_storage()
    {
        var fs = new InMemoryFileSystem();
        var store = new Core.Storage.RecordStore(fs, @"C:\cfg\Mnemosyne");
        var r = Items.Record();
        r.ItemReadAt["achievement:1"] = Items.Now;
        store.Save(r);
        Assert.Equal(Items.Now, store.Load(1)!.ItemReadAt["achievement:1"]);
    }

    [Fact]
    public void A_done_reading_with_no_instant_is_stamped_only_where_the_flip_was_observed()
    {
        var earlier = Items.Now - TimeSpan.FromDays(400);
        var now = Items.Now;

        // No history at all: the quest was already complete the first time the plugin looked, so the instant is unknown.
        var fresh = Items.Record();
        Assert.True(RecordUpdater.Merge(fresh, Category.Events, [new Reading("event:252:39", new DoneValue(true))], now));
        Assert.Equal(new DoneValue(true), fresh.Values["event:252:39"]);

        // Known not done, now done: this is the flip, and it happened now.
        var flipping = Items.Record();
        flipping.Values["event:252:39"] = new DoneValue(false);
        Assert.True(RecordUpdater.Merge(flipping, Category.Events, [new Reading("event:252:39", new DoneValue(true))], now));
        Assert.Equal(new DoneValue(true, null, now), flipping.Values["event:252:39"]);

        // Already done and stamped: the stamp is the flip's, and a later read must not move it to today.
        var stamped = Items.Record();
        stamped.Values["event:252:39"] = new DoneValue(true, null, earlier);
        Assert.False(RecordUpdater.Merge(stamped, Category.Events, [new Reading("event:252:39", new DoneValue(true))], now));
        Assert.Equal(new DoneValue(true, null, earlier), stamped.Values["event:252:39"]);
    }

    [Fact]
    public void A_reading_that_carries_its_own_instant_is_stored_unchanged()
    {
        var reader = Items.Now - TimeSpan.FromHours(2);
        var r = Items.Record();
        r.Values["event:252:39"] = new DoneValue(true, null, Items.Now - TimeSpan.FromDays(400));
        Assert.True(RecordUpdater.Merge(r, Category.Events, [new Reading("event:252:39", new DoneValue(true, null, reader))], Items.Now));
        Assert.Equal(new DoneValue(true, null, reader), r.Values["event:252:39"]);
    }

    [Fact]
    public void Two_done_values_differing_only_in_their_stamp_earn_a_save()
    {
        var r = Items.Record();
        r.Values["event:252:39"] = new DoneValue(true, null, Items.Now - TimeSpan.FromDays(400));
        Assert.True(RecordUpdater.Merge(r, Category.Events, [new Reading("event:252:39", new DoneValue(true, null, Items.Now))], Items.Now));
        Assert.False(RecordUpdater.Merge(r, Category.Events, [new Reading("event:252:39", new DoneValue(true, null, Items.Now))], Items.Now));
    }

    [Fact]
    public void A_reading_with_no_value_forgets_the_item_and_can_still_name_it()
    {
        var r = Items.Record();
        RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:venture:0", new TimerValue(Items.Now.AddHours(18)), Items.Now, "Kupo")], Items.Now);
        Assert.Equal(new TimerValue(Items.Now.AddHours(18)), r.Values["reset:venture:0"]);
        Assert.Equal("Kupo", r.ItemNames["reset:venture:0"]);
        Assert.Equal(Items.Now, r.ItemReadAt["reset:venture:0"]);

        // The venture finished and nothing replaced it: the retainer is still there, the deadline is not.
        Assert.True(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:venture:0", null, Label: "Kupo")], Items.Now));
        Assert.False(r.Values.ContainsKey("reset:venture:0"));
        Assert.False(r.ItemReadAt.ContainsKey("reset:venture:0"));
        Assert.Equal("Kupo", r.ItemNames["reset:venture:0"]);

        // The retainer was dismissed: the slot is empty and says nothing at all.
        Assert.True(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:venture:0", null)], Items.Now));
        Assert.False(r.ItemNames.ContainsKey("reset:venture:0"));
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:venture:0", null)], Items.Now));   // nothing left to forget
    }

    [Fact]
    public void An_open_gate_lifts_a_stored_lock_and_leaves_a_count_alone()
    {
        var r = Items.Record();
        Assert.True(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:cactpot:mini", GateValue.Shut)], Items.Now));
        Assert.Equal(GateValue.Shut, r.Values["reset:cactpot:mini"]);
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:cactpot:mini", GateValue.Shut)], Items.Now));   // a repeated lock earns no save

        Assert.True(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:cactpot:mini", GateValue.Open)], Items.Now));
        Assert.False(r.Values.ContainsKey("reset:cactpot:mini"));
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:cactpot:mini", GateValue.Open)], Items.Now));   // nothing to lift

        // The gate reader runs every tick; its "open" must never touch a count the observer stored, and a "locked" arriving
        // over a value is a misread frame (every gate is monotonic) that must not cost the count either.
        r.Values["reset:cactpot:mini"] = new CounterValue(2, 3, Items.Now);
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:cactpot:mini", GateValue.Open)], Items.Now));
        Assert.Equal(new CounterValue(2, 3, Items.Now), r.Values["reset:cactpot:mini"]);
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:cactpot:mini", GateValue.Shut)], Items.Now));
        Assert.Equal(new CounterValue(2, 3, Items.Now), r.Values["reset:cactpot:mini"]);
    }

    [Fact]
    public void A_gate_never_replaces_a_done_value_and_a_value_replaces_a_lock()
    {
        // The gate rules apply over a counter too; the window rows store Done values beside their locks, at the same precedence.
        // A lock is stored only over nothing or a lock, so a done row a later frame misreads as locked keeps its value, and an
        // open gate over it changes nothing; a real value arriving over a lock replaces the lock.
        var r = Items.Record();
        r.Values["reset:fashion-report"] = new DoneValue(true, "weekly:2026-09-08");
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:fashion-report", GateValue.Shut)], Items.Now));
        Assert.Equal(new DoneValue(true, "weekly:2026-09-08"), r.Values["reset:fashion-report"]);
        Assert.False(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:fashion-report", GateValue.Open)], Items.Now));
        Assert.Equal(new DoneValue(true, "weekly:2026-09-08"), r.Values["reset:fashion-report"]);

        r.Values["reset:carnivale:0"] = GateValue.Shut;
        Assert.True(RecordUpdater.Merge(r, Category.Resets, [new Reading("reset:carnivale:0", new DoneValue(false))], Items.Now));
        Assert.Equal(new DoneValue(false), r.Values["reset:carnivale:0"]);
    }

    [Fact]
    public void A_revoked_gate_clears_the_value_then_locks_and_repeats_without_a_save()
    {
        // A sold estate and a retired racing chocobo close their gate for real, but a lock arriving over a value is ignored as a
        // misread frame, so such a reader sends a clearing reading and then the lock, in that order. It cannot see what is
        // stored, so it sends the pair on every tick; over its own lock the pair must change nothing and earn no save.
        var r = Items.Record();
        r.Values["timer:housing"] = new TimerValue(Items.Now.AddDays(30));
        IReadOnlyList<Reading> revoked = [new Reading("timer:housing", null), new Reading("timer:housing", GateValue.Shut)];
        Assert.True(RecordUpdater.Merge(r, Category.Content, revoked, Items.Now));
        Assert.Equal(GateValue.Shut, r.Values["timer:housing"]);
        Assert.False(RecordUpdater.Merge(r, Category.Content, revoked, Items.Now));   // the next tick: nothing moved
        Assert.Equal(GateValue.Shut, r.Values["timer:housing"]);

        // A clearing reading alone never lifts a lock: "no such thing" is what the lock already says, and both read blank.
        Assert.False(RecordUpdater.Merge(r, Category.Content, [new Reading("timer:housing", null)], Items.Now));
        Assert.Equal(GateValue.Shut, r.Values["timer:housing"]);
        // Only the open gate, or a value, lifts it.
        Assert.True(RecordUpdater.Merge(r, Category.Content, [new Reading("timer:housing", GateValue.Open)], Items.Now));
        Assert.False(r.Values.ContainsKey("timer:housing"));
    }
}
