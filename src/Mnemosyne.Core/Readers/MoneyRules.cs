using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Readers;

/// <summary>What one currency's two sources said: whether the currency manager holds it, with its count, maximum and limit
/// flag, and the inventory's count with whether the Currency container reports loaded.</summary>
public readonly record struct CurrencyProbe(bool InManager, long ManagerCount, long ManagerMax, bool ManagerLimited, long Held, bool ContainerLoaded);

/// <summary>A count read from the client's item-search cache, with the content id the cache names as its owner.</summary>
public readonly record struct CacheCount(ulong Owner, int Count);

/// <summary>The Money readers' pure rules, tested without a client, as ContentRules is for the Content category: when a zero is a
/// value, where a currency's count and cap come from, what a retainer's level is over, and how the armoire cache's bits are counted.</summary>
public static class MoneyRules
{
    /// <summary>A held count as a reading. A count above zero is always data. A zero is data only when its source has shown it
    /// can mean it (zeroRead): then it clears the row, unless showZero says the row reads 0 (gil, a retainer's gil and inventory). An unread
    /// zero says nothing, so the stored value survives, as every unreadable value does.</summary>
    public static Reading? Held(string itemId, long count, bool zeroRead, int cap, bool showZero, DateTimeOffset now)
    {
        if (count > 0) return new Reading(itemId, new CounterValue((int)Math.Min(count, int.MaxValue), cap, now));
        if (!zeroRead) return null;
        return showZero ? new Reading(itemId, new CounterValue(0, cap, now)) : new Reading(itemId, null);
    }

    /// <summary>One currency other than gil and the seals. An item the currency manager holds reads its count there, over its
    /// maximum when the manager limits it and over the stack size otherwise, and its zero is read. Every other item reads the inventory's
    /// count over the stack size. That zero is read only with the Currency container loaded and the manager filled, so the inventory's zero
    /// never clears a currency the manager has simply not filled yet (the manager is taken to be filled at login). A tomestone is never the manager's, and
    /// the reader passes managerFilled true for it.</summary>
    public static Reading? Currency(string itemId, CurrencyProbe probe, int stackSize, bool managerFilled, DateTimeOffset now)
    {
        if (probe.InManager)
        {
            var cap = probe.ManagerLimited && probe.ManagerMax > 0 ? (int)Math.Min(probe.ManagerMax, int.MaxValue) : stackSize;
            return Held(itemId, probe.ManagerCount, true, cap, false, now);
        }
        return Held(itemId, probe.Held, probe.ContainerLoaded && managerFilled, stackSize, false, now);
    }

    /// <summary>A company's seals are over its rank's cap, GetMaxCompanySeals, and over the stack size only when that reads 0.</summary>
    public static int SealCap(long rankMaximum, int stackSize) => rankMaximum > 0 ? (int)Math.Min(rankMaximum, int.MaxValue) : stackSize;

    /// <summary>A retainer's level: over the character's own level in the retainer's class, which caps the retainer, so a retainer at it reads
    /// done; over the hand file's level cap when the character's level reads nothing; never below the retainer's own. The row is labelled with the
    /// retainer and the class's name, as the Jobs section writes it. A retainer with no class (ClassJob 0, taken to be right) clears the row and
    /// keeps its name; so does a level of 0. Either zero is believed only once the roster has shown its levels (RosterFill). A class row
    /// that does not resolve says nothing, as every unreadable value does. A retainer whose name has not arrived says nothing on either clearing arm: a clearing
    /// reading with an empty label would clear its stored name too (RecordUpdater.Name), so the row waits for the name.</summary>
    public static Reading? RetainerLevel(string itemId, string retainer, int classJob, ClassJobRow? job, int level, int? classLevel, int levelCap, bool filled)
    {
        if (classJob == 0) return filled && retainer.Length > 0 ? new Reading(itemId, null, Label: retainer) : null;
        if (job == null) return null;
        var label = RetainerLabel(retainer, CatalogBuilder.Capitalise(job.Name));
        if (level <= 0) return filled && label.Length > 0 ? new Reading(itemId, null, Label: label) : null;
        var cap = Math.Max(level, classLevel is > 0 ? classLevel.Value : levelCap);
        return new Reading(itemId, new LevelValue(level, cap), Label: label);
    }

    /// <summary>A retainer row's per-character name: the retainer, then the field. If the roster ever names a retainer with an
    /// empty NameString, this returns an empty label rather than a composed ": Gil" or ": Retainer Inventory": an empty Reading.Label
    /// leaves the row's stored name alone, the same rule the venture rows already follow, so the row falls back to its catalog name
    /// until a name arrives.</summary>
    public static string RetainerLabel(string retainer, string field) => retainer.Length > 0 ? $"{retainer}: {field}" : "";

    /// <summary>The rows whose bit is set in a client bit array of 32-bit words: bit i is word i / 32's bit i % 32 (the layout of the
    /// client's other bit arrays; not yet confirmed in game for this one). A row past
    /// the words is not counted and never read: the rows all sit below 3,850, inside the cache's 4,000 bits, and a patch that adds one
    /// past them counts it as not stored rather than throwing. The words are read in place, so the cache's span is passed as it is and a read
    /// allocates nothing (a 125-word array per read).</summary>
    public static int Stored(IReadOnlyList<uint> rows, ReadOnlySpan<uint> words)
    {
        var stored = 0;
        foreach (var row in rows)
        {
            var word = (int)(row >> 5);
            if (word < words.Length && (words[word] & (1u << (int)(row & 31))) != 0) stored++;
        }
        return stored;
    }
}

/// <summary>Where a window-gated storage row's count comes from. The client's item-search cache is a per-character copy the game writes on its
/// own visits, so it fills the row with no visit; a count it names for another content id belongs to another character. A live read of the
/// window's own data, once it answers, wins for the rest of the session on that character: the cache may lag the window, and a lagging copy
/// must never put an older count back. Pick resets that memory whenever the character it is called for changes, so after a switch the cache
/// can fill the row again only once this row's family calls Pick for the new character, which happens only while its gate is open: a switch
/// made while the gate is closed keeps the previous character's live-seen memory until a later, gate-open switch clears it. The effect is
/// benign: the row then keeps the last live count rather than reading the cache. A relog onto the same character keeps the live count,
/// which is safe: these containers change only through their windows, which the live read sees. Kept per character, per row.</summary>
public sealed class StorageSource
{
    private ulong _owner;
    private bool _liveSeen;

    public int? Pick(ulong owner, int? live, CacheCount? cached)
    {
        if (owner != _owner)
        {
            _owner = owner;
            _liveSeen = false;
        }
        if (live is { } count)
        {
            _liveSeen = true;
            return count;
        }
        if (_liveSeen || cached is not { } cache || cache.Owner != owner) return null;
        return cache.Count;
    }
}
