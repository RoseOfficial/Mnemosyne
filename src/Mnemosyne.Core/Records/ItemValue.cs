using System.Text.Json.Serialization;

namespace Mnemosyne.Core.Records;

/// <summary>One stored value. Polymorphic in JSON through the "kind" discriminator.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DoneValue), "done")]
[JsonDerivedType(typeof(CounterValue), "counter")]
[JsonDerivedType(typeof(LevelValue), "level")]
[JsonDerivedType(typeof(TimerValue), "timer")]
[JsonDerivedType(typeof(GateValue), "gate")]
public abstract record ItemValue;

/// <summary>PeriodKey is the reset period the item was done in; null for items with no reset rule, but for gc:current, which is never
/// done and keeps the current Grand Company's row there (Readings.CurrentCompany).</summary>
public sealed record DoneValue(bool Done, string? PeriodKey = null, DateTimeOffset? At = null) : ItemValue;

/// <summary>Current over Cap, with the instant it was read so refill and accrual can be computed forward. NextGrant
/// is the client's own instant for the next accrual, when it has one: the levequest allowance function gives it directly,
/// and it is preferred over the schedule because it survives a logout and is the game's own answer. Null falls back
/// to the item's reset schedule.</summary>
public sealed record CounterValue(int Current, int Cap, DateTimeOffset ReadAt, DateTimeOffset? NextGrant = null) : ItemValue;

/// <summary>Current over Max, and how far through the current step: experience earned over experience needed for a job,
/// reputation earned over reputation needed for an allied society rank. Both null when the reader
/// has no figure and both meaningless at the cap, where nothing comes next.</summary>
public sealed record LevelValue(int Current, int Max, int? Progress = null, int? ProgressMax = null) : ItemValue;

/// <summary>Progress, when present, is 0 to 1 toward the deadline.</summary>
public sealed record TimerValue(DateTimeOffset Deadline, double? Progress = null) : ItemValue;

/// <summary>The unlock gate's answer for a row whose empty state is not blank: an ObservedOnly row reads "not seen" and sits in
/// Today, a NeedsWindow row reads "needs: &lt;window&gt;" and waits in Running events while its window is open, and both are
/// right for a character who could do the thing and wrong for one who cannot, so a locked gate has to be stored where the
/// assembler reads. Shut is the stored form and reads as a blank row in no top group; it is stored only
/// over nothing or another lock, because every gate is monotonic and a lock arriving over a value is a misread frame that
/// must not cost an observed count or a visit's value. Open is never stored: merged over a stored lock it lifts it, merged over
/// anything else it changes nothing, so a gate reader can say "open" on every tick without touching what is stored.</summary>
public sealed record GateValue(bool Locked) : ItemValue
{
    public static readonly GateValue Shut = new(true);
    public static readonly GateValue Open = new(false);
}

/// <summary>What the plugin saw for an ObservedOnly item, and in which period.</summary>
public sealed record ObservedValue(int Count, DateTimeOffset SeenAt, string PeriodKey);
