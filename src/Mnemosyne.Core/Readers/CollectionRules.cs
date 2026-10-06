namespace Mnemosyne.Core.Readers;

/// <summary>The Collections readers' pure rules, tested without a client, as ContentRules is for the Content category:
/// when a polled read is worth doing, and how a log's rows are counted over a client bit array that may be shorter than the
/// sheet.</summary>
public static class CollectionRules
{
    /// <summary>Whether a reader whose full read is costly reads in full now. It always does when a trigger that is not a
    /// polled one fired (a login, a level or class change); on a poll it reads only when its fingerprint moved since its last full
    /// read, or when it has none. The fingerprint carries the character's content id, so a switch always reads.</summary>
    public static bool ReadNow<T>(ReaderTrigger fired, ReaderTrigger polled, T? last, T now) where T : struct, IEquatable<T> =>
        (fired & ~polled) != 0 || last is not { } seen || !seen.Equals(now);

    /// <summary>The rows owned through a client bit array: a row's bit is its id less the offset, 0 for a fish and 20000
    /// for a spearfish. A bit the array cannot answer for belongs to a row a patch added before the client structs caught up. That
    /// row is not owned, and it is returned apart so the reader can say so once; BitArray.Get would throw there and disable the
    /// reader for the session.</summary>
    public static (int Owned, IReadOnlyList<uint> Beyond) Owned(IReadOnlyList<uint> ids, int offset, Func<int, bool?> bit)
    {
        var owned = 0;
        var beyond = new List<uint>();
        foreach (var id in ids)
        {
            switch (bit((int)id - offset))
            {
                case true: owned++; break;
                case null: beyond.Add(id); break;
            }
        }
        return (owned, beyond);
    }

    /// <summary>A client array's bits over its own bytes, bounded by their length: bit i is byte i / 8's bit i % 8, BitArray's own
    /// layout, and an index outside the bytes answers null. The bound is the bytes, never the declared BitCount. For the fish array
    /// that count is the last log fish's id (1,522 over 191 bytes), so BitArray.TryGet and PopCount, which stop at it, refuse that
    /// fish, and these bytes hold it. (FFXIVClientStructs' BitArray: TryGet refuses index &gt;=
    /// bitCount; PlayerState's CaughtFishBitArray has bitCount 1522 over 191 bytes.)</summary>
    public static Func<int, bool?> Bits(byte[] bytes) => i => i >= 0 && i < bytes.Length * 8 ? (bytes[i >> 3] & (1 << (i & 7))) != 0 : null;
}

/// <summary>Whether a list the client loads only on request (the achievements, the titles) is this character's to count.
/// A list already loaded at the first read for a content id may be the previous character's, carried across a switch, or one
/// loaded before the plugin reloaded, so a loaded list is trusted only after the guard has seen it not loaded under the same
/// content id. The same lesson as RevocableGateState: state kept per character, reset by a switch. The cost is that
/// after a plugin reload that follows a visit, the rows keep what the last visit stored until the list is next seen not loaded,
/// and nothing wrong is ever written.</summary>
public sealed class ListLoadGuard
{
    private ulong _owner;
    private bool _seenUnloaded;

    public bool Trust(ulong owner, bool loaded)
    {
        if (owner != _owner)
        {
            _owner = owner;
            _seenUnloaded = false;
        }
        if (!loaded)
        {
            _seenUnloaded = true;
            return false;
        }
        return _seenUnloaded;
    }
}
