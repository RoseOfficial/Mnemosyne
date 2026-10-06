using System.Numerics;
using Lumina.Excel;
using Mnemosyne.Core.Readers;

namespace Mnemosyne.Interop.Readers;

/// <summary>The counts the two Collections readers share: the rows one IUnlockState member answers owned, and a client
/// array's bits, read from its own bytes. A row that does not resolve on this game version is not owned; the rows were reduced
/// from the same sheets at start, so that cannot happen short of a data change mid-session.</summary>
internal static class CollectionCounts
{
    /// <summary>The rows of one sheet that an IUnlockState member answers owned. Lumina's ExcelSheet&lt;T&gt; (where T : struct,
    /// IExcelRow&lt;T&gt;) exposes T? GetRowOrDefault(uint rowId).</summary>
    public static int Owned<T>(ExcelSheet<T> sheet, IReadOnlyList<uint> ids, Func<T, bool> owned) where T : struct, IExcelRow<T>
    {
        var count = 0;
        foreach (var id in ids)
        {
            if (sheet.GetRowOrDefault(id) is { } row && owned(row)) count++;
        }
        return count;
    }

    /// <summary>The set bits of a client array's own bytes, the fingerprint a tick compares. Every byte is counted, so the last
    /// fish, whose bit sits at the array's declared count, where BitArray.PopCount stops, still moves it. FFXIVClientStructs'
    /// BitArray.PopCount counts bitCount bits; PlayerState's CaughtFishBitArray has a bitCount of 1522 over 191
    /// bytes.</summary>
    public static int Population(ReadOnlySpan<byte> bytes)
    {
        var count = 0;
        foreach (var b in bytes) count += BitOperations.PopCount(b);
        return count;
    }

    /// <summary>A client array's bits copied into managed memory, so a Core rule can ask for them without a pointer, as ContentReader's
    /// Variant record bits are. The reading itself is CollectionRules.Bits, tested in Core: bounded by the bytes, an index outside
    /// them answering null rather than throwing.</summary>
    public static Func<int, bool?> Bits(ReadOnlySpan<byte> bytes) => CollectionRules.Bits(bytes.ToArray());
}
