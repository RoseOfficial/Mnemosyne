using FFXIVClientStructs.FFXIV.Client.Game;

namespace Mnemosyne.Interop.Readers;

/// <summary>How many of one item the character holds, and whether a zero can be trusted. Count is the inventory manager's own
/// item count; Loaded is whether the Currency container reports loaded, which is what separates "none held" from "not read
/// yet". A count above zero is trusted whatever the flag says, and the flag is then reported as it is rather than
/// claimed true, since it can be false while the container pointer is null.</summary>
public readonly record struct Holding(int Count, bool Loaded);

/// <summary>The trove reader and the Content reader's MGP row read an item the same way, so the read lives
/// here once. Nothing here writes: SetInventoryType, Clear and SendTradeRequest are never called. HeldAll reads
/// many items in one pass, which the trove reader and the Money currency family share.</summary>
public static class HeldItems
{
    /// <summary>GetInventoryItemCount(itemId, isHq, checkEquipped, checkArmory, minCollectability);
    /// InventoryContainer.IsLoaded separates unread from zero. The Currency container is taken to hold irregular tomestones
    /// and MGP, read through the same count; not yet confirmed independently. Its slots are read directly when the count says
    /// zero, so the answer does not depend on which containers the count scans. Null when the inventory manager is absent, or
    /// when the container is absent and the count is zero. FFXIVClientStructs' InventoryManager, InventoryContainer,
    /// InventoryItem, InventoryType.</summary>
    public static Holding? Held(uint itemId) => HeldAll([itemId]) is { } held && held.TryGetValue(itemId, out var holding) ? holding : null;

    /// <summary>Held for several items at once. The Currency container is resolved once and its slots are walked at most once,
    /// however many counts read zero: the Money currency family reads 52 currencies a tick this way. Each item answers exactly
    /// as Held does alone. An item the read cannot speak for is left out: the inventory manager absent is
    /// null for all, and with the container absent an item whose count is zero is left out. FFXIVClientStructs' InventoryManager:
    /// InventoryContainer* GetInventoryContainer(InventoryType), int GetInventoryItemCount(uint, bool, bool, bool, short); InventoryContainer:
    /// int Size, bool IsLoaded, InventoryItem* GetInventorySlot(int); InventoryItem: uint GetItemId(), uint GetQuantity().</summary>
    public static unsafe IReadOnlyDictionary<uint, Holding>? HeldAll(IReadOnlyCollection<uint> itemIds)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return null;
        var currency = inventory->GetInventoryContainer(InventoryType.Currency);
        Dictionary<uint, int>? slots = null;
        var held = new Dictionary<uint, Holding>(itemIds.Count);
        foreach (var itemId in itemIds)
        {
            var count = inventory->GetInventoryItemCount(itemId, false, false, false, 0);
            if (currency == null)
            {
                if (count > 0) held[itemId] = new Holding(count, false);
                continue;
            }
            if (count == 0 && currency->IsLoaded)
            {
                slots ??= Slots(currency);
                count = slots.GetValueOrDefault(itemId);
            }
            held[itemId] = new Holding(count, currency->IsLoaded);
        }
        return held;
    }

    /// <summary>The Currency container's items and quantities, walked once; the first slot holding an item is its count, as Held read it.</summary>
    private static unsafe Dictionary<uint, int> Slots(InventoryContainer* container)
    {
        var slots = new Dictionary<uint, int>();
        for (var slot = 0; slot < container->Size; slot++)
        {
            var entry = container->GetInventorySlot(slot);
            if (entry == null) continue;
            var id = entry->GetItemId();
            if (id != 0 && !slots.ContainsKey(id)) slots[id] = (int)entry->GetQuantity();
        }
        return slots;
    }
}
