using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Readers;

/// <summary>Merges one reader's readings into a record and stamps the category. Values the reader did not mention are kept.
/// A gate value is a rule, not a value: open lifts a stored lock, locked is stored only where nothing or a lock is stored, and
/// neither ever replaces a value. A clearing reading never removes a stored lock.</summary>
public static class RecordUpdater
{
    public static bool Merge(CharacterRecord record, Category category, IReadOnlyList<Reading> readings, DateTimeOffset now)
    {
        var changed = false;
        foreach (var reading in readings)
        {
            if (reading.Value is GateValue gate)
            {
                // A gate answer is a rule, not a value. Open says only "nothing is locked here": it lifts a stored
                // lock and leaves anything else alone, because a gate reader says it on every tick. Shut is stored only
                // over nothing or another lock: every gate is monotonic (15), so a lock arriving over a value is a misread
                // frame, and it must not cost an observed count or a visit's value.
                record.Values.TryGetValue(reading.ItemId, out var held);
                if (!gate.Locked && held is GateValue) { record.Values.Remove(reading.ItemId); changed = true; }
                else if (gate.Locked && held is null) { record.Values[reading.ItemId] = gate; changed = true; }
                continue;
            }
            if (reading.Value is null)
            {
                // The reader looked and there is nothing there. Forgetting is the point: a venture slot that keeps its last
                // deadline reads "overdue by 200 d" for ever, and no later reading would ever correct it.
                // A stored lock stays: "no such thing here" is what the lock already says, and both read blank. A gate that can
                // close again sends this clearing reading and then the lock on every tick; without this rule the pair
                // would remove and re-store the lock each time and earn a save every live interval.
                if (record.Values.TryGetValue(reading.ItemId, out var locked) && locked is GateValue { Locked: true }) continue;
                if (record.Values.Remove(reading.ItemId)) changed = true;
                record.ItemReadAt.Remove(reading.ItemId);
                if (Name(record, reading)) changed = true;
                continue;
            }
            record.Values.TryGetValue(reading.ItemId, out var existing);
            var value = Stamped(existing, reading.Value, now);
            if (!SameForSaving(existing, value)) changed = true;
            record.Values[reading.ItemId] = value;
            if (reading.ReadAt is { } readAt) record.ItemReadAt[reading.ItemId] = readAt;
            if (Name(record, reading)) changed = true;
        }
        record.CategoryReadAt[category.ToString()] = now;
        return changed;
    }

    /// <summary>Stores, replaces or forgets the per-character name of an item, and says whether anything moved. A reading
    /// with no label at all clears the name, because a slot nobody is in has no name to show.</summary>
    private static bool Name(CharacterRecord record, Reading reading)
    {
        if (reading.Label is { Length: > 0 } label)
        {
            if (record.ItemNames.TryGetValue(reading.ItemId, out var existing) && existing == label) return false;
            record.ItemNames[reading.ItemId] = label;
            return true;
        }
        return reading.Value is null && record.ItemNames.Remove(reading.ItemId);
    }

    /// <summary>When a reader reports "done" without saying when, the record keeps the instant the flip was first seen, because
    /// an event whose return reuses one quest is only that return's if the plugin watched it happen inside the window.
    /// A done value that was already done keeps its earlier stamp; one that has just turned done is stamped now; one with no
    /// history at all stays unstamped, which reads as "done at an unknown time" and is exactly what it is.</summary>
    private static ItemValue Stamped(ItemValue? existing, ItemValue incoming, DateTimeOffset now) =>
        incoming is DoneValue { Done: true, At: null } done
            ? existing switch
            {
                DoneValue { Done: true, At: { } at } => done with { At = at },
                DoneValue { Done: false } => done with { At = now },
                _ => done,
            }
            : incoming;

    /// <summary>A Counter whose only difference is its read instant is stored but does not by itself earn a save; a
    /// crash then leaves the previous (Current, ReadAt) pair on disk, which is self-consistent for the accrual math.
    /// A Done value is different, so two that differ only in their stamp are different for saving: a newly observed flip
    /// must reach the disk or the return it belongs to is lost.</summary>
    private static bool SameForSaving(ItemValue? existing, ItemValue incoming) =>
        (existing, incoming) switch
        {
            (CounterValue a, CounterValue b) => a.Current == b.Current && a.Cap == b.Cap && a.NextGrant == b.NextGrant,
            _ => existing == incoming,
        };
}
