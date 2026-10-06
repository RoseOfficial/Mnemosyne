using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

public class ReadingsTests
{
    [Fact]
    public void A_done_reading_carries_its_period_only_when_done_and_periodic()
    {
        // Sunday 2026-09-13 12:00 UTC lies in the daily period that began Saturday 15:00, so a done daily keys to the 12th; a
        // not-done reading has no period it was done in, and an item with no rule carries none either.
        var now = Items.Now;
        Assert.Equal(new DoneValue(true, "daily:2026-09-12"), Readings.Done(Items.Roulette, true, now).Value);
        Assert.Equal(new DoneValue(false), Readings.Done(Items.Roulette, false, now).Value);
        Assert.Equal(new DoneValue(true), Readings.Done(Items.Msq1, true, now).Value);
        Assert.Equal(Items.Roulette.Id, Readings.Done(Items.Roulette, true, now).ItemId);
    }

    [Fact]
    public void A_gated_reading_says_nothing_locks_opens_or_carries_the_value()
    {
        // An unreadable gate says nothing, a locked one stores the lock, an open one with nothing to read now sends the open
        // gate, and an open one with a value sends it, with the per-character name when there is one.
        Assert.Empty(Readings.Gated("field:eureka", null, new LevelValue(3, 60)));
        Assert.Equal([new Reading("field:eureka", GateValue.Shut)], Readings.Gated("field:eureka", false, new LevelValue(3, 60)));
        Assert.Equal([new Reading("field:eureka", GateValue.Open)], Readings.Gated("field:eureka", true, null));
        Assert.Equal([new Reading("field:eureka", new LevelValue(34, 60), Label: "Elemental level (last read in Eureka Pyros)")],
            Readings.Gated("field:eureka", true, new LevelValue(34, 60), label: "Elemental level (last read in Eureka Pyros)"));
        // A gate that can close again clears first, because a lock is never stored over a value.
        Assert.Equal([new Reading("timer:housing", null), new Reading("timer:housing", GateValue.Shut)], Readings.Gated("timer:housing", false, null, revocable: true));

        // Leaving the place keeps the value: the open gate a family sends outside its zone changes nothing stored.
        var r = Items.Record();
        RecordUpdater.Merge(r, Category.Content, Readings.Gated("field:eureka", true, new LevelValue(34, 60)), Items.Now);
        Assert.False(RecordUpdater.Merge(r, Category.Content, Readings.Gated("field:eureka", true, null), Items.Now));
        Assert.Equal(new LevelValue(34, 60), r.Values["field:eureka"]);
    }
}
