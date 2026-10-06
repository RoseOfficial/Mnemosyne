using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class ObservationTests
{
    private static readonly DateTimeOffset Now = Items.Now;   // Sunday 2026-09-13 12:00 UTC
    private static Observation Mini(ObservationKind kind = ObservationKind.OneMore, int count = 1) => new(kind, count, 3, ResetSchedules.Daily);

    [Fact]
    public void One_more_and_at_least_count_within_a_period_and_start_over_after_its_boundary()
    {
        var first = Observations.Apply(null, Mini(), ResetSchedules.Daily, Now);
        Assert.Equal(new CounterValue(1, 3, Now), first);
        Assert.Equal(2, Observations.Apply(first, Mini(), ResetSchedules.Daily, Now.AddMinutes(5)).Current);
        Assert.Equal(3, Observations.Apply(new CounterValue(3, 3, Now), Mini(), ResetSchedules.Daily, Now.AddMinutes(10)).Current);   // clamped at the cap

        // Yesterday's two tickets are over at 15:00 UTC: the next ticket seen is the first of the new day.
        var yesterday = new CounterValue(2, 3, new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero));
        Assert.Equal(1, Observations.Apply(yesterday, Mini(), ResetSchedules.Daily, Now).Current);

        // A floor: the second ticket's price says at least two, whatever was stored, and never fewer than what was.
        Assert.Equal(2, Observations.Apply(null, Mini(ObservationKind.AtLeast, 2), ResetSchedules.Daily, Now).Current);
        Assert.Equal(3, Observations.Apply(new CounterValue(3, 3, Now), Mini(ObservationKind.AtLeast, 2), ResetSchedules.Daily, Now).Current);
        Assert.Equal(2, Observations.Apply(yesterday, Mini(ObservationKind.AtLeast, 2), ResetSchedules.Daily, Now).Current);   // last period's count does not carry

        // A stored lock is contradicted by the observation itself and gives way to it.
        Assert.Equal(1, Observations.Apply(GateValue.Shut, Mini(), ResetSchedules.Daily, Now).Current);
        // No known schedule (a data centre with no listed draw): every count is one period, so nothing ever starts over.
        Assert.Equal(3, Observations.Apply(yesterday, Mini(), null, Now).Current);
    }

    [Fact]
    public void A_repeated_observation_changes_nothing_and_a_price_names_the_jumbo_ticket()
    {
        var two = new CounterValue(2, 3, Now);
        Assert.Equal(2, Observations.Apply(two, Mini(ObservationKind.AtLeast, 2), ResetSchedules.Daily, Now.AddSeconds(1)).Current);

        // The first ticket costs 100 MGP, the second 150, the third 200; anything else is not a ticket the ladder knows.
        Assert.Equal(1, Observations.JumboTicketOrdinal(100));
        Assert.Equal(2, Observations.JumboTicketOrdinal(150));
        Assert.Equal(3, Observations.JumboTicketOrdinal(200));
        Assert.Null(Observations.JumboTicketOrdinal(250));
        Assert.Null(Observations.JumboTicketOrdinal(0));
    }
}
