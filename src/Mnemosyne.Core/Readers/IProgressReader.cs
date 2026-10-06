using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Readers;

/// <summary>When a reader runs. Interval is the live read every few seconds; the others are game events the plugin maps.</summary>
[Flags]
public enum ReaderTrigger
{
    None = 0,
    Login = 1,
    Interval = 2,
    Unlock = 4,
    Territory = 8,
    LevelOrJob = 16,
    DutyCompleted = 32,

    /// <summary>A game window's Post event. The plugin fires it through Tracker.Fire on the frame it happens,
    /// because an edge-gated row is readable only while its agent is active and the next interval tick may be too late.</summary>
    Window = 64,
}

/// <summary>One item's value as a reader saw it. ReadAt is the reader's own instant for items whose freshness is per item;
/// a reader that reads a whole category at once leaves it null and the category stamp applies. A null
/// Value is not "no reading" but "this character has no such thing" — an empty retainer slot, a venture nobody is running —
/// and the merge forgets what was stored. Label is a name only this character can supply, such as a retainer's, for an item
/// the global catalog can only number.</summary>
public sealed record Reading(string ItemId, ItemValue? Value, DateTimeOffset? ReadAt = null, string? Label = null);

/// <summary>One reader fills one category; a category may have several. Name identifies the reader in the
/// disabled list and in the dump, because two readers of one category (Events has the quest reader and the reward reader)
/// are told apart by nothing else. Read returns what it could read now, an empty list for "there was nothing to read", or
/// null for "I did not read at all" — the player state was not loaded, so the category's read stamp must not move.
/// It may throw, and the Tracker then disables that one reader for the session. It never writes.
/// The Tracker calls Read(ReaderTrigger) with the triggers that fired.</summary>
public interface IProgressReader
{
    string Name { get; }
    Category Category { get; }
    ReaderTrigger Triggers { get; }
    IReadOnlyList<Reading>? Read();

    /// <summary>The read the Tracker calls, told which of this reader's own triggers fired (Triggers masked by what fired). A reader
    /// whose full read is costly reads in full only on a trigger that can change it, and on a poll only when a cheap fingerprint
    /// moved; every other reader reads the same whatever fired, which is this default.</summary>
    IReadOnlyList<Reading>? Read(ReaderTrigger fired) => Read();
}

/// <summary>Who the logged-in character is. IsLoaded false means nothing else is valid yet.</summary>
public interface ICharacterIdentity
{
    bool IsLoaded { get; }
    ulong ContentId { get; }
    string Name { get; }
    string HomeWorld { get; }
    string DataCenter { get; }
}
