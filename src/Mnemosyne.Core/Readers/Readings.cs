using System.Globalization;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Readers;

/// <summary>The readings every reader builds the same way, written once, so no reader duplicates another's.</summary>
public static class Readings
{
    /// <summary>A Done item with a reset rule stores the period it was done in, so an offline reset is right without a read.
    /// A not-done reading carries no period, because there is no period in which it was done, and an item with no
    /// rule carries none either.</summary>
    public static Reading Done(ItemDefinition item, bool done, DateTimeOffset now) =>
        new(item.Id, new DoneValue(done, done && item.Reset.Schedule.Length > 0 ? ResetSchedules.PeriodKey(item.Reset.Schedule, now) : null));

    /// <summary>The current Grand Company as gc:current. A
    /// company stores a Done value that is never done, whose period key is the company's GrandCompany row, and the per-character name
    /// "Grand Company: &lt;company&gt;". So the row reads as that name alone and is never attention, and Holdings.CurrentCompany reads the
    /// row back. No company, or a row with no seal item, clears the row and its name. The Jobs reader
    /// passes null only for row 0, a real "no company"; a non-zero row the sheets do not carry is unreadable, so the reader sends no reading at
    /// all rather than calling this, as the codebase's rule elsewhere is that unreadable keeps the stored value.</summary>
    public static Reading CurrentCompany(ItemDefinition item, GrandCompanyRow? company) =>
        company is { } joined && MoneyIds.SealItem(joined.RowId) != null
            ? new Reading(item.Id, new DoneValue(false, joined.RowId.ToString(CultureInfo.InvariantCulture)), Label: $"{item.Name}: {joined.Name}")
            : new Reading(item.Id, null);

    /// <summary>A gated row's reading, written once for every place family and the two revocable
    /// families (the personal estate, the racing chocobo); WindowReader and the PvP family keep their own Lock() and Open()
    /// instead. An unreadable gate says nothing. A locked one stores the lock, which reads blank in no
    /// top group; a revocable gate, one that can close again over a value (the personal estate, the racing chocobo), sends a
    /// clearing reading first, because a lock is never stored over a value. An open gate with nothing to read now sends the
    /// open gate, which lifts a stale lock and keeps the last visit's value; an open gate with a value sends it, with the
    /// per-character name when there is one.</summary>
    public static IReadOnlyList<Reading> Gated(string itemId, bool? open, ItemValue? value, bool revocable = false, string? label = null)
    {
        if (open == null) return [];
        if (open == false)
        {
            if (!revocable) return [new Reading(itemId, GateValue.Shut)];
            return [new Reading(itemId, null), new Reading(itemId, GateValue.Shut)];
        }
        if (value == null) return [new Reading(itemId, GateValue.Open)];
        return [new Reading(itemId, value, Label: label)];
    }
}
