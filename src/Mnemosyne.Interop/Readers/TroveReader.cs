using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Readers;

/// <summary>The Moogle Treasure Trove's tomestones held, one Counter per trove in the Events category. An irregular
/// tomestone is a plain item, never a TomestonesItem row, so the read is the inventory manager's item count over the hand
/// file's item id. Gate: a character holding none reads nothing and the row clears; a zero is trusted only once the
/// Currency container reports loaded, which is taken to be where the item lives; not yet confirmed, so a zero from the
/// count is checked against that container's own slots first. Third reader of the Events category; a throw disables this
/// one alone. Nothing here writes: SetInventoryType, Clear and SendTradeRequest are never called.
/// The read is HeldItems.Held, shared with the MGP row. HeldItems.HeldAll reads every trove in one pass.</summary>
public sealed class TroveReader(Catalog catalog, CatalogRows rows, IPlayerState player, IClock clock) : IProgressReader
{
    /// <summary>Each trove row with its item id and holding cap, found once: the catalog and the rows do not change after load, so
    /// the Events category is not walked and no id is split on every tick.</summary>
    private readonly IReadOnlyList<(string Id, uint ItemId, int Cap)> _troves = Troves(catalog, rows);

    // The item ids read in one pass.
    private readonly IReadOnlyList<uint> _itemIds = Troves(catalog, rows).Select(t => t.ItemId).ToList();

    public string Name => "TroveReader";
    public Category Category => Category.Events;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Interval;

    public IReadOnlyList<Reading>? Read()
    {
        // Nothing is read while the player state is not loaded, and the category's stamp does not move.
        if (!player.IsLoaded) return null;
        var now = clock.UtcNow;
        var readings = new List<Reading>(_troves.Count);
        var holdings = HeldItems.HeldAll(_itemIds);
        foreach (var (id, itemId, cap) in _troves)
        {
            if (holdings == null || !holdings.TryGetValue(itemId, out var held)) continue;   // nothing the read can speak for: the row keeps
            if (held.Count > 0) readings.Add(new Reading(id, new CounterValue(held.Count, cap, now)));
            else if (held.Loaded) readings.Add(new Reading(id, null));                       // none held, and the container says so: the row clears
        }
        return readings;
    }

    private static List<(string Id, uint ItemId, int Cap)> Troves(Catalog catalog, CatalogRows rows)
    {
        var caps = rows.TroveItems.ToDictionary(t => t.ItemId, t => t.StackSize);
        var troves = new List<(string Id, uint ItemId, int Cap)>();
        foreach (var item in catalog.InCategory(Category.Events))
        {
            var parts = item.Id.Split(':');
            if (parts.Length == 3 && parts[1] == "trove" && uint.TryParse(parts[2], out var itemId) && caps.TryGetValue(itemId, out var cap))
                troves.Add((item.Id, itemId, cap));
        }
        return troves;
    }
}
