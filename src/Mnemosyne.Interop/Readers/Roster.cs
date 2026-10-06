using FFXIVClientStructs.FFXIV.Client.Game;
using Mnemosyne.Core.Readers;

namespace Mnemosyne.Interop.Readers;

/// <summary>The retainer roster read once. Ready is RetainerManager.IsReady and Count is the client's own count. Retainers holds each
/// slot in the client's sorted order up to Count, null where the client gave no retainer. A roster that is not ready carries no retainers,
/// because not ready is every login before the list arrives and is not "no retainers".</summary>
public sealed record RosterState(bool Ready, int Count, IReadOnlyList<RetainerFacts?> Retainers)
{
    /// <summary>The retainer ids RosterGuard compares, in memory only.</summary>
    public IReadOnlyList<ulong> Ids => Retainers.Where(r => r != null).Select(r => r!.Value.Id).ToList();
}

/// <summary>The venture rows and the Money retainer rows read the roster the same way, so the read lives here once, as
/// HeldItems does for the held items. Nothing here writes or requests: RetainerManager.RequestVenturesTimers is never called.</summary>
public static class Roster
{
    /// <summary>RetainerManager is a direct static address; IsReady, GetRetainerCount and GetRetainerBySortedIndex, and
    /// the Retainer fields RetainerId, NameString, ClassJob, Level, ItemCount, Gil and VentureComplete. Null while the manager is absent.
    /// FFXIVClientStructs' RetainerManager: static RetainerManager* Instance(), bool IsReady,
    /// byte GetRetainerCount(), Retainer* GetRetainerBySortedIndex(uint sortedIndex); Retainer: ulong RetainerId, string NameString, byte
    /// ClassJob, byte Level, byte ItemCount, uint Gil, uint VentureComplete.</summary>
    public static unsafe RosterState? Read()
    {
        var manager = RetainerManager.Instance();
        if (manager == null) return null;
        if (!manager->IsReady) return new RosterState(false, 0, []);
        int count = manager->GetRetainerCount();
        var retainers = new RetainerFacts?[count];
        for (var index = 0; index < count; index++)
        {
            var retainer = manager->GetRetainerBySortedIndex((uint)index);
            retainers[index] = retainer == null
                ? null
                : new RetainerFacts(retainer->RetainerId, retainer->NameString, retainer->ClassJob, retainer->Level, retainer->Gil, retainer->ItemCount, retainer->VentureComplete);
        }
        return new RosterState(true, count, retainers);
    }
}
