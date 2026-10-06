using System.Diagnostics;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Readers;

/// <summary>The three AtLogin families of Money rows. One reader instance per family, so a moved member behind one of them
/// disables that family alone.</summary>
public enum MoneyFamily { Currencies, Retainers, Bags }

/// <summary>The Money rows in memory once the player state is loaded. Every family runs at login and on the
/// tick, because nothing raises an event when a currency, a retainer's gil or a bag slot changes.
///
/// Currencies reads gil, the company seals and every other currency the character can hold. Each currency is read from the currency
/// manager where it holds the item and from the inventory otherwise, and each is over its own cap (MoneyRules). A zero is a value only
/// once its source has shown it can mean it, so a manager not yet filled at login never clears a stored count.
///
/// Retainers reads three rows per roster slot from Roster.Read, the same read the venture rows make. The roster is trusted only once it is
/// this character's (RosterGuard), an empty one only once this login has shown the roster non-empty, and a field's zero only once the
/// roster has shown that field since this login (RosterFill).
///
/// Bags reads the four bags and the armoury chest as two totals, only while every counted container reports loaded.
///
/// Each family logs its first read per login and per character with useful context beside it: both currency sources, every
/// container's size, the roster whenever it changes, and the cost. Every pointer read sits in a private static unsafe helper, or in the shared
/// Roster.Read and HeldItems.HeldAll, behind the player-state guard. Nothing here writes, requests or opens anything:
/// CurrencyManager.SetItemData, RetainerManager.RequestVenturesTimers and every InventoryManager and container writer are never called.</summary>
public sealed class MoneyReader(MoneyFamily family, Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    // Each currency row with its sheet row, paired once (the catalog and the rows do not change after load), and the items the currency
    // manager or the inventory is asked for: every currency but gil and the seals, which have reads of their own.
    private readonly IReadOnlyList<(ItemDefinition Item, CurrencyRow Currency)> _currencies = family == MoneyFamily.Currencies ? CurrencyItems(catalog, rows) : [];
    private readonly IReadOnlyList<uint> _probed = family == MoneyFamily.Currencies
        ? CurrencyItems(catalog, rows).Select(c => c.Currency.ItemId).Where(id => id != MoneyIds.Gil && id is < MoneyIds.FirstSeal or > MoneyIds.LastSeal).ToList()
        : [];
    // A tomestone is never the currency manager's (outside every bucket), so its zero needs only the Currency container loaded.
    private readonly HashSet<uint> _tomestones = rows.TomestoneItems.Select(t => t.ItemId).ToHashSet();
    private readonly HashSet<string> _ids = catalog.InCategory(Category.Money).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
    private readonly Dictionary<uint, ClassJobRow> _jobs = family == MoneyFamily.Retainers ? rows.ClassJobs.ToDictionary(j => j.RowId) : new Dictionary<uint, ClassJobRow>();
    // The field words of the retainer labels, Addon 830 "Gil" and 3731 "Retainer Inventory", in the client's language.
    private readonly string _gilWord = rows.Addons.FirstOrDefault(a => a.RowId == MoneyIds.GilAddon)?.Text ?? "";
    private readonly string _inventoryWord = rows.Addons.FirstOrDefault(a => a.RowId == MoneyIds.RetainerInventoryAddon)?.Text ?? "";
    // A retainer's gil cap is the player's, the gil item's stack size.
    private readonly int _gilCap = rows.Currencies.FirstOrDefault(c => c.ItemId == MoneyIds.Gil)?.StackSize ?? 0;
    // The roster is this character's only once RosterGuard says so, and a field's zero only once RosterFill has seen it filled.
    private readonly RosterGuard _guard = new();
    private readonly RosterFill _fill = new();
    // The characters the log lines were last written for, so a switch writes the new character's evidence again. A login
    // clears them too, so a relog onto the same character writes its own first-read lines rather than leaning on an earlier login's.
    private ulong _currenciesReportedFor, _bagsReportedFor, _rosterReportedFor;
    private string? _rosterReported;

    /// <summary>The four bags, and the thirteen armoury containers. The waist container is expected to
    /// report size 0; an absent waist container is treated the same way, taken to be so rather than confirmed.</summary>
    private static readonly InventoryType[] BagTypes = [InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4];
    private static readonly InventoryType[] ArmouryTypes =
    [
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody, InventoryType.ArmoryHands,
        InventoryType.ArmoryWaist, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar, InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist, InventoryType.ArmoryRings, InventoryType.ArmorySoulCrystal,
    ];

    public string Name => $"MoneyReader({family})";
    public Category Category => Category.Money;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Interval;

    /// <summary>A read that says nothing of its trigger is a login's.</summary>
    public IReadOnlyList<Reading>? Read() => Read(ReaderTrigger.Login);

    public IReadOnlyList<Reading>? Read(ReaderTrigger fired)
    {
        // Nothing is read while the player state is not loaded. Every IPlayerState member answers 0 before it.
        if (!player.IsLoaded) return null;
        var login = (fired & ReaderTrigger.Login) != 0;
        if (login)
        {
            // A login writes its own first-read evidence, even on the same character as the login before it (a relog).
            _currenciesReportedFor = _bagsReportedFor = 0;
            _rosterReported = null;
        }
        var now = clock.UtcNow;
        return family switch
        {
            MoneyFamily.Currencies => Currencies(now),
            MoneyFamily.Retainers => Retainers(login, now),
            MoneyFamily.Bags => Bags(now),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown money family"),
        };
    }

    private static List<(ItemDefinition Item, CurrencyRow Currency)> CurrencyItems(Catalog catalog, CatalogRows rows)
    {
        var byId = rows.Currencies.ToDictionary(c => MoneyIds.CurrencyId(c.ItemId), StringComparer.Ordinal);
        return catalog.InCategory(Category.Money).Where(i => byId.ContainsKey(i.Id)).Select(i => (i, byId[i.Id])).ToList();
    }

    /// <summary>Every currency the character can hold, each through its own read: gil through GetGil and each company's seals through
    /// GetCompanySeals, their zero read once the Currency container reports loaded; the rest through MoneyRules.Currency, the manager where it
    /// holds the item and the inventory otherwise.</summary>
    private IReadOnlyList<Reading>? Currencies(DateTimeOffset now)
    {
        var watch = Stopwatch.StartNew();
        if (CurrencySnapshot(_probed) is not { } state) return null;
        var readings = new List<Reading>(_currencies.Count);
        foreach (var (item, currency) in _currencies)
        {
            Reading? reading;
            if (currency.ItemId == MoneyIds.Gil)
                reading = MoneyRules.Held(item.Id, state.Gil, state.Loaded, currency.StackSize, showZero: true, now);
            else if (currency.ItemId is >= MoneyIds.FirstSeal and <= MoneyIds.LastSeal)
            {
                var company = MoneyIds.SealCompany(currency.ItemId) - 1;
                reading = MoneyRules.Held(item.Id, state.Seals[company], state.Loaded, MoneyRules.SealCap(state.SealCaps[company], currency.StackSize), showZero: false, now);
            }
            else if (state.Probes.TryGetValue(currency.ItemId, out var probe))
                reading = MoneyRules.Currency(item.Id, probe, currency.StackSize, state.ManagerFilled || _tomestones.Contains(currency.ItemId), now);
            else reading = null;
            if (reading != null) readings.Add(reading);
        }
        ReportCurrencies(state, watch.Elapsed);
        return readings;
    }

    /// <summary>Three rows per roster slot. Nothing while the roster is not ready, while it is the previous character's, or while it is
    /// ready with no retainers before this login has shown it non-empty, so every stored row survives. A slot beyond the
    /// roster clears its rows, name and all. A slot the client gave no retainer for says nothing. Every other slot reads its level, gil and
    /// used slots, each zero believed only once the roster has shown that field since this login.</summary>
    private IReadOnlyList<Reading>? Retainers(bool login, DateTimeOffset now)
    {
        // The retainer manager is a direct static address, so a login's read always reaches the guard and the fill with its login flag.
        if (Roster.Read() is not { } roster) return null;
        var owner = player.ContentId;
        var trusted = _guard.Trust(owner, login, roster.Ready, roster.Ids);
        // Observed on every read, with no retainers while the roster is not trusted, so a login read that finds the roster not ready or not
        // this character's still starts the proof over.
        var fields = _fill.Observe(owner, login, trusted ? roster.Retainers.Where(r => r != null).Select(r => r!.Value) : Array.Empty<RetainerFacts>());
        ReportRoster(owner, roster, trusted, fields);
        if (!trusted) return [];
        var jobs = data.GetExcelSheet<ClassJob>();
        var readings = new List<Reading>(MoneyIds.RetainerSlots * 3);
        void Add(Reading? reading)
        {
            if (reading != null && _ids.Contains(reading.ItemId)) readings.Add(reading);
        }
        for (var slot = 0; slot < MoneyIds.RetainerSlots; slot++)
        {
            var level = MoneyIds.RetainerId(slot, MoneyIds.Level);
            var gil = MoneyIds.RetainerId(slot, MoneyIds.RetainerGil);
            var slots = MoneyIds.RetainerId(slot, MoneyIds.Slots);
            if (slot >= roster.Count)
            {
                Add(new Reading(level, null));
                Add(new Reading(gil, null));
                Add(new Reading(slots, null));
                continue;
            }
            if (roster.Retainers[slot] is not { } retainer) continue;
            _jobs.TryGetValue((uint)retainer.ClassJob, out var job);
            int? classLevel = retainer.ClassJob != 0 && jobs.GetRowOrDefault((uint)retainer.ClassJob) is { } row ? player.GetClassJobLevel(row) : (int?)null;
            Add(MoneyRules.RetainerLevel(level, retainer.Name, retainer.ClassJob, job, retainer.Level, classLevel, extras.LevelCap, fields.Levels));
            Add(MoneyRules.Held(gil, retainer.Gil, fields.Gil, _gilCap, showZero: true, now) is { } held
                ? held with { Label = MoneyRules.RetainerLabel(retainer.Name, _gilWord) }
                : null);
            Add(MoneyRules.Held(slots, retainer.Items, fields.Items, extras.Storage.RetainerInventory, showZero: true, now) is { } used
                ? used with { Label = MoneyRules.RetainerLabel(retainer.Name, _inventoryWord) }
                : null);
        }
        return readings;
    }

    /// <summary>The bags and the armoury chest, used slots over the containers' own sizes. A total is read only while every
    /// container it counts reports loaded; otherwise the row keeps what it stored.</summary>
    private IReadOnlyList<Reading>? Bags(DateTimeOffset now)
    {
        var watch = Stopwatch.StartNew();
        if (Containers() is not { } state) return null;
        var readings = new List<Reading>(2);
        if (state.Bags is { } bags && _ids.Contains(MoneyIds.Bags)) readings.Add(new Reading(MoneyIds.Bags, new CounterValue(bags.Used, bags.Size, now)));
        if (state.Armoury is { } armoury && _ids.Contains(MoneyIds.Armoury)) readings.Add(new Reading(MoneyIds.Armoury, new CounterValue(armoury.Used, armoury.Size, now)));
        ReportBags(state, watch.Elapsed);
        return readings;
    }

    /// <summary>Once per login and per character, at the first read with the Currency container loaded: both sources side by side for every currency held,
    /// the seals with their caps, and the cost, showing whether the currency manager is filled at login, whether the
    /// inventory's count covers its items, and whether the caps match the game.</summary>
    private void ReportCurrencies(CurrencyState state, TimeSpan elapsed)
    {
        if (!state.Loaded || player.ContentId == _currenciesReportedFor) return;
        _currenciesReportedFor = player.ContentId;
        var held = state.Probes.Where(p => p.Value.ManagerCount > 0 || p.Value.Held > 0).Select(p => p.Value.InManager
            ? $"{p.Key} manager {p.Value.ManagerCount} of {p.Value.ManagerMax}{(p.Value.ManagerLimited ? " limited" : "")}, inventory {InventoryCount(p.Key)}"
            : $"{p.Key} inventory {p.Value.Held}");
        log($"currencies: first read, Currency container loaded {state.Loaded}, manager filled {state.ManagerFilled}; gil {state.Gil}; " +
            $"seals {state.Seals[0]} of {state.SealCaps[0]}, {state.Seals[1]} of {state.SealCaps[1]}, {state.Seals[2]} of {state.SealCaps[2]}; held: {string.Join("; ", held)}; {elapsed.TotalMicroseconds:0} us");
    }

    /// <summary>The roster as read, whenever it changes for this character: ready, the guard's answer, which fields have shown data, and each
    /// slot's class, level, gil, items and venture, showing whether the roster is filled before the first bell and whose
    /// roster a switch left in the manager. The log is dalamud.log, on this PC only.</summary>
    private void ReportRoster(ulong owner, RosterState roster, bool trusted, RosterFields fields)
    {
        if (owner != _rosterReportedFor)
        {
            _rosterReportedFor = owner;
            _rosterReported = null;
        }
        var slots = string.Join("; ", roster.Retainers.Select((r, i) => r is { } x
            ? $"{i} {x.Name} class {x.ClassJob} level {x.Level} gil {x.Gil} items {x.Items} venture {x.VentureComplete}"
            : $"{i} unreadable"));
        var said = $"ready {roster.Ready}, count {roster.Count}, trusted {trusted}; shown gil {fields.Gil}, items {fields.Items}, levels {fields.Levels}; {slots}";
        if (said == _rosterReported) return;
        _rosterReported = said;
        log($"retainers: {said}");
    }

    /// <summary>Once per login and per character, at the first read where either total reads: both totals, "unread" for one that did not, every
    /// container's size and load state and the client's own count of empty bag slots, and the cost. Written whichever total
    /// reads, so an armoury that never reads still leaves the line that says why.</summary>
    private void ReportBags(ContainerState state, TimeSpan elapsed)
    {
        if ((state.Bags == null && state.Armoury == null) || player.ContentId == _bagsReportedFor) return;
        _bagsReportedFor = player.ContentId;
        log($"bags: first read, bags {Said(state.Bags)}, armoury {Said(state.Armoury)}; {Describe()}; {elapsed.TotalMicroseconds:0} us");
    }

    private static string Said(Fill? fill) => fill is { } f ? $"{f.Used} of {f.Size}" : "unread";

    private readonly record struct CurrencyState(bool Loaded, bool ManagerFilled, long Gil, long[] Seals, long[] SealCaps, Dictionary<uint, CurrencyProbe> Probes);

    /// <summary>Every currency's two sources in one read. The currency manager answers for the 39 items in its
    /// buckets and is taken to be filled by the server at login; not yet confirmed. The inventory answers for the rest through HeldItems.HeldAll; gil and the
    /// seals have reads of their own. ManagerFilled is whether the manager answered for any item this read. FFXIVClientStructs'
    /// InventoryManager: static InventoryManager* Instance(), uint GetGil(), uint GetCompanySeals(byte),
    /// uint GetMaxCompanySeals(byte), InventoryContainer* GetInventoryContainer(InventoryType); CurrencyManager: static CurrencyManager*
    /// Instance(), bool HasItem(uint), uint GetItemCount(uint), uint GetItemMaxCount(uint), bool IsItemLimited(uint).</summary>
    private static unsafe CurrencyState? CurrencySnapshot(IReadOnlyList<uint> probed)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return null;
        var container = inventory->GetInventoryContainer(InventoryType.Currency);
        var loaded = container != null && container->IsLoaded;
        var manager = CurrencyManager.Instance();
        var probes = new Dictionary<uint, CurrencyProbe>(probed.Count);
        var fromInventory = new List<uint>();
        var filled = false;
        foreach (var id in probed)
        {
            if (manager != null && manager->HasItem(id))
            {
                filled = true;
                probes[id] = new CurrencyProbe(true, manager->GetItemCount(id), manager->GetItemMaxCount(id), manager->IsItemLimited(id), 0, loaded);
            }
            else fromInventory.Add(id);
        }
        var held = HeldItems.HeldAll(fromInventory);
        foreach (var id in fromInventory)
        {
            probes[id] = held != null && held.TryGetValue(id, out var holding)
                ? new CurrencyProbe(false, 0, 0, false, holding.Count, holding.Loaded)
                : new CurrencyProbe(false, 0, 0, false, 0, false);
        }
        // One entry per company, the Maelstrom, the Twin Adder and the Immortal Flames, as the seal items 20 to 22 are (MoneyIds).
        var seals = new long[MoneyIds.LastSeal - MoneyIds.FirstSeal + 1];
        var caps = new long[seals.Length];
        for (var company = 0; company < seals.Length; company++)
        {
            seals[company] = inventory->GetCompanySeals((byte)(company + 1));
            caps[company] = inventory->GetMaxCompanySeals((byte)(company + 1));
        }
        return new CurrencyState(loaded, filled, inventory->GetGil(), seals, caps, probes);
    }

    /// <summary>The inventory's own count, for the log only, beside the manager's. FFXIVClientStructs'
    /// InventoryManager: int GetInventoryItemCount(uint itemId, bool isHq, bool checkEquipped, bool checkArmory, short minCollectability).</summary>
    private static unsafe int InventoryCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? -1 : inventory->GetInventoryItemCount(itemId, false, false, false, 0);
    }

    private readonly record struct Fill(int Used, int Size);

    private readonly record struct ContainerState(Fill? Bags, Fill? Armoury);

    private static unsafe ContainerState? Containers()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return null;
        return new ContainerState(Filled(inventory, BagTypes), Filled(inventory, ArmouryTypes));
    }

    /// <summary>Used slots over the sizes of a set of containers. A container of size 0 is left out. The waist container is
    /// expected to report size 0, and an absent waist container is skipped the same way: taken to be so, not itself confirmed.
    /// Any other container absent or not loaded makes the set unread, which keeps the row. FFXIVClientStructs'
    /// InventoryContainer: int Size, bool IsLoaded, InventoryItem* GetInventorySlot(int); InventoryItem: uint GetItemId().</summary>
    private static unsafe Fill? Filled(InventoryManager* inventory, InventoryType[] types)
    {
        int used = 0, size = 0;
        foreach (var type in types)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null)
            {
                if (type == InventoryType.ArmoryWaist) continue;
                return null;
            }
            if (container->Size == 0) continue;
            if (!container->IsLoaded) return null;
            size += container->Size;
            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item != null && item->GetItemId() != 0) used++;
            }
        }
        return size == 0 ? null : new Fill(used, size);
    }

    /// <summary>Every bag and armoury container's size and load state, and the client's own count of empty bag slots, for the log only.
    /// FFXIVClientStructs' InventoryManager: uint GetEmptySlotsInBag().</summary>
    private static unsafe string Describe()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return "inventory absent";
        var said = new StringBuilder();
        foreach (var type in BagTypes.Concat(ArmouryTypes))
        {
            var container = inventory->GetInventoryContainer(type);
            if (said.Length > 0) said.Append(", ");
            said.Append(type).Append(container == null ? " absent" : $" size {container->Size} loaded {container->IsLoaded}");
        }
        return said.Append("; the client's empty bag slots ").Append(inventory->GetEmptySlotsInBag()).ToString();
    }
}
