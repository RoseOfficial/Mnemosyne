using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Readers;

/// <summary>What was seen happen for an ObservedOnly item: OneMore is one event, a fresh Mini Cactpot ticket; AtLeast
/// is a floor the event proves, a Jumbo Cactpot purchase whose price names the ticket.</summary>
public enum ObservationKind { OneMore, AtLeast }

/// <summary>One observation for one item. Cap and Schedule come from the catalog item; the schedule is resolved against the
/// record before it is applied, because the Jumbo Cactpot's is a per-record sentinel (ResetSchedules.Jumbo).</summary>
public sealed record Observation(ObservationKind Kind, int Count, int Cap, string Schedule);

/// <summary>The rules an observation applies to a stored value, pure so they are tested without a client. An observed count
/// is a floor: tickets bought while the plugin was not running are not seen, and the row says so with its marker.</summary>
public static class Observations
{
    /// <summary>Whether a stored count is still this period's at the given instant: no schedule is one endless period, and
    /// otherwise no boundary lies between the count's read instant and now. Apply and Tracker.Observe both ask this, so the
    /// two can never disagree about which period a count belongs to.</summary>
    public static bool Continues(CounterValue stored, string? schedule, DateTimeOffset now) =>
        schedule == null || !ResetSchedules.BoundaryBetween(schedule, stored.ReadAt, now);

    /// <summary>The stored value after an observation. A count from an earlier period is over, so the observation starts the
    /// new period's count; within a period OneMore adds one and AtLeast raises the count to its floor. Neither passes the cap
    /// and neither lowers a count, so a repeated event changes nothing. A stored lock is contradicted by the observation and
    /// gives way to it. A null schedule is a period that cannot be keyed for this record, so every count is one period.</summary>
    public static CounterValue Apply(ItemValue? stored, Observation observation, string? schedule, DateTimeOffset now)
    {
        var current = stored is CounterValue c && Continues(c, schedule, now) ? c.Current : 0;
        var next = observation.Kind switch
        {
            ObservationKind.OneMore => current + 1,
            ObservationKind.AtLeast => Math.Max(current, observation.Count),
            _ => throw new ArgumentOutOfRangeException(nameof(observation), observation.Kind, "unknown observation kind"),
        };
        return new CounterValue(Math.Clamp(next, 0, observation.Cap), observation.Cap, now);
    }

    /// <summary>Which Jumbo Cactpot ticket a purchase price names: the first costs 100 MGP, the second 150, the third 200
    /// (the Lodestone's Cactpot guide). Null for a price outside the ladder, which the observer reads as one more.</summary>
    public static int? JumboTicketOrdinal(int mgp) => mgp switch { 100 => 1, 150 => 2, 200 => 3, _ => null };
}
