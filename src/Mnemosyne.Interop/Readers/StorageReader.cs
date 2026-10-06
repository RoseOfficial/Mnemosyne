using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;
using ClientCabinet = FFXIVClientStructs.FFXIV.Client.Game.UI.Cabinet;
using PlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using UIState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState;

namespace Mnemosyne.Interop.Readers;

/// <summary>The three window-gated storage families of the Money rows. One reader instance per family, so a
/// moved member behind one of them disables that family alone.</summary>
public enum StorageFamily { Saddlebag, Dresser, Armoire }

/// <summary>The saddlebag and its premium tab, the glamour dresser and the armoire, each NeedsWindow on its own window.
///
/// Each row's count comes first from the client's item-search cache, the per-character copy of these containers that the game writes on its own
/// visits and reads back from the character's own file: taken to be so; not yet confirmed. So the rows fill at login with no visit. A
/// cache naming another content id never belongs to this character. The live read is the fallback and the cross-check: the saddlebag's containers while its
/// window is up, the dresser's own data once trusted for this character, and the armoire's state once trusted for this character. Once it answers on a character it
/// wins for the rest of the session, so a cache that lags the window never puts an older count back; a switch to another character lets the
/// cache fill again only once this row's family reads live for the new character, which is only while its gate is open (StorageSource):
/// a switch made while the gate is closed keeps the previous character's live-seen memory until a later, gate-open switch clears it.
///
/// Gates: the saddlebag on "My Feisty Little Chocobo" (66698), its right tab also on the premium flag, which can close again,
/// so a closed flag clears and then locks on a second closed read; the dresser on the Cast Glamour action (GeneralAction 22); the
/// armoire on nothing. A locked gate stores the lock, an unreadable one says nothing, and an open gate with no count keeps the last visit's.
///
/// The saddlebag and the dresser read on the tick and on their windows' PostShow. The armoire's live read, one native call per storable row,
/// runs only at login, on a territory change, and on any tick or window fire while the armoire's own window is up, since its data arrives
/// after that window's PostShow; any other window's fire, which runs every Window reader, and any tick with the armoire closed read only
/// the cache. Each family logs its state whenever it changes for this character, showing
/// the cache's owner, flags and counts beside the windows' own. Every pointer read sits in a private static
/// unsafe helper behind the player-state guard. Nothing here writes, requests, opens or stores anything: ItemFinderModule's
/// SearchForItem and file functions, MirageManager's restore and store functions, Cabinet.WithdrawCabinetItem and StoreCabinetItem,
/// AgentMiragePrismPrismBox's update functions and AddonInventoryBuddy.SetTab are never called.</summary>
public sealed class StorageReader(StorageFamily family, Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    private readonly HashSet<string> _ids = catalog.InCategory(Category.Money).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
    // Each row picks between the cache and its live window, per character.
    private readonly StorageSource _saddlebag = new(), _premium = new(), _dresser = new(), _armoire = new();
    // The premium tab's flag can close again when the storage subscription lapses: a closed flag is believed only on a second closed read.
    private readonly RevocableGateState _premiumGate = new();
    // The armoire's own state may be the previous character's, or one loaded before the plugin reloaded: trusted only once seen
    // not loaded for this character, as the achievement list is.
    private readonly ListLoadGuard _cabinetGuard = new();
    // The dresser's PrismBoxLoaded flag has the same residual risk as the cabinet's state: if it survived a
    // switch made straight from an inn room, it could read loaded for the next character before their own visit. Trusted only once seen
    // not loaded for this character.
    private readonly ListLoadGuard _dresserGuard = new();
    // The character the state line was last written for, so a switch writes the new character's evidence again.
    private ulong _reportedFor;
    private string? _reported;
    // The armoire's last live count and whose it was, so the state line names it and changes only when a new live read differs.
    private (ulong Owner, int Count)? _armoireLive;

    public string Name => $"StorageReader({family})";
    public Category Category => Category.Money;
    public ReaderTrigger Triggers => family switch
    {
        StorageFamily.Saddlebag or StorageFamily.Dresser => ReaderTrigger.Login | ReaderTrigger.Interval | ReaderTrigger.Window,
        StorageFamily.Armoire => ReaderTrigger.Login | ReaderTrigger.Interval | ReaderTrigger.Territory | ReaderTrigger.Window,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown storage family"),
    };

    /// <summary>A read that says nothing of its trigger is a full one.</summary>
    public IReadOnlyList<Reading>? Read() => Read(ReaderTrigger.Login);

    public IReadOnlyList<Reading>? Read(ReaderTrigger fired)
    {
        // Nothing is read while the player state is not loaded. Every gate below answers "locked" before it.
        if (!player.IsLoaded) return null;
        var now = clock.UtcNow;
        return family switch
        {
            StorageFamily.Saddlebag => Saddlebags(now),
            StorageFamily.Dresser => Dresser(now),
            StorageFamily.Armoire => Armoire(fired, now),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown storage family"),
        };
    }

    /// <summary>Both tabs of the chocobo saddlebag, used over 70 each.</summary>
    private IReadOnlyList<Reading> Saddlebags(DateTimeOffset now)
    {
        var owner = player.ContentId;
        bool? open = data.GetExcelSheet<Quest>().GetRowOrDefault(extras.Storage.SaddlebagQuest) is { } quest ? unlocks.IsQuestCompleted(quest) : null;
        var flag = PremiumFlag();
        bool? premium = open == true ? _premiumGate.Evaluate(owner, flag, now) : open;
        var cache = CachedSaddlebags();
        var live = LiveSaddlebags();
        var left = open == true ? _saddlebag.Pick(owner, live?.Left, cache?.Left) : null;
        var right = premium == true ? _premium.Pick(owner, live?.Right, cache?.Right) : null;
        Report(owner, $"saddlebag: quest {Say(open)}, premium flag {Say(flag)} (gate {Say(premium)}); window {(live is { } w ? $"up, {Say(w.Left)} / {Say(w.Right)}" : "not up")}; " +
            $"cache {Cache(owner, cache?.Left)} / {Cache(owner, cache?.Right)}; read {Say(left)} / {Say(right)}");
        var readings = new List<Reading>(3);
        if (_ids.Contains(MoneyIds.Saddlebag))
            readings.AddRange(Readings.Gated(MoneyIds.Saddlebag, open, left is { } l ? new CounterValue(l, extras.Storage.Saddlebag, now) : null));
        if (_ids.Contains(MoneyIds.PremiumSaddlebag))
            readings.AddRange(Readings.Gated(MoneyIds.PremiumSaddlebag, premium, right is { } r ? new CounterValue(r, extras.Storage.PremiumSaddlebag, now) : null, revocable: true));
        return readings;
    }

    /// <summary>The glamour dresser, used over 800. Never the dresser agent's UsedSlots, which waits on item names rather than data.
    /// PrismBoxLoaded is trusted only once seen not loaded for this character (_dresserGuard), the armoire's own
    /// lesson: a switch made straight from an inn room could otherwise leave the flag reading loaded from the previous character.</summary>
    private IReadOnlyList<Reading> Dresser(DateTimeOffset now)
    {
        var owner = player.ContentId;
        bool? open = data.GetExcelSheet<GeneralAction>().GetRowOrDefault(extras.Storage.CastGlamourAction) is { } action ? unlocks.IsGeneralActionUnlocked(action) : null;
        var cache = CachedDresser();
        var raw = LiveDresser();
        var trusted = _dresserGuard.Trust(owner, raw != null);
        var live = trusted ? raw : null;
        var count = open == true ? _dresser.Pick(owner, live, cache) : null;
        Report(owner, $"dresser: gate {Say(open)}; loaded {Say(raw)} (trusted {trusted}); cache {Cache(owner, cache)}; read {Say(count)}");
        if (!_ids.Contains(MoneyIds.Dresser)) return [];
        return Readings.Gated(MoneyIds.Dresser, open, count is { } c ? new CounterValue(c, extras.Storage.GlamourDresser, now) : null);
    }

    /// <summary>The armoire: items stored over the storable Cabinet rows, since it has no capacity; no gate. The live read, one native
    /// call per storable row, runs at login, on a territory change, and on any tick or window fire while the armoire's own window is up.
    /// Opening the armoire is what requests its data, so its window's PostShow finds it Requested, and the tick reads it once it
    /// arrives. Every window fire runs every Window reader, whatever window fired, so without the agent test each Character window or Duty
    /// Finder fire would pay for it, and so would every tick. Its count is logged through the state line, so it is written only when it
    /// changes.</summary>
    private IReadOnlyList<Reading> Armoire(ReaderTrigger fired, DateTimeOffset now)
    {
        var owner = player.ContentId;
        var state = ArmoireState();
        var trusted = state is { } s && _cabinetGuard.Trust(owner, s == ClientCabinet.CabinetState.Loaded);
        var cache = CachedArmoire(rows.ArmoireRows);
        var liveNow = (fired & (ReaderTrigger.Login | ReaderTrigger.Territory)) != 0 || ArmoireWindowUp();
        int? live = trusted && liveNow ? LiveArmoire(rows.ArmoireRows) : null;
        if (live is { } seen) _armoireLive = (owner, seen);
        var count = _armoire.Pick(owner, live, cache);
        var lastLive = _armoireLive is { } last && last.Owner == owner ? $"{last.Count} of {rows.ArmoireRows.Count}" : "none";
        Report(owner, $"armoire: state {state?.ToString() ?? "absent"} (trusted {trusted}); last live read {lastLive}; cache {Cache(owner, cache)}");
        if (!_ids.Contains(MoneyIds.Armoire)) return [];
        return Readings.Gated(MoneyIds.Armoire, true, count is { } c ? new CounterValue(c, rows.ArmoireRows.Count, now) : null);
    }

    /// <summary>Logs a family's state whenever it changes for this character (a switch logs the new character's evidence again).</summary>
    private void Report(ulong owner, string said)
    {
        if (owner != _reportedFor)
        {
            _reportedFor = owner;
            _reported = null;
        }
        if (said == _reported) return;
        _reported = said;
        log($"storage: {said}");
    }

    private static string Say(bool? value) => value?.ToString() ?? "unread";

    private static string Say(int? value) => value?.ToString() ?? "unread";

    private static string Cache(ulong owner, CacheCount? cache) =>
        cache is { } c ? $"{c.Count}{(c.Owner == owner ? "" : " for another character")}" : "none";

    private readonly record struct Tabs(int? Left, int? Right);

    private readonly record struct CachedTabs(CacheCount? Left, CacheCount? Right);

    /// <summary>The four saddlebag containers, read only while the saddlebag window's agent (InventoryBuddy, 300) is active,
    /// so they can only belong to this character; each tab is its two containers, and a tab whose containers are not loaded is unread. Null
    /// while the window is not up. FFXIVClientStructs' AgentModule: static AgentModule* Instance(),
    /// AgentInterface* GetAgentByInternalId(AgentId); AgentInterface: bool IsAgentActive(); AgentId.InventoryBuddy = 300.</summary>
    private static unsafe Tabs? LiveSaddlebags()
    {
        var agents = AgentModule.Instance();
        if (agents == null) return null;
        var agent = agents->GetAgentByInternalId(AgentId.InventoryBuddy);
        if (agent == null || !agent->IsAgentActive()) return null;
        var inventory = InventoryManager.Instance();
        if (inventory == null) return null;
        return new Tabs(Used(inventory, InventoryType.SaddleBag1, InventoryType.SaddleBag2), Used(inventory, InventoryType.PremiumSaddleBag1, InventoryType.PremiumSaddleBag2));
    }

    /// <summary>The used slots of two containers, or null when either is absent or not loaded. FFXIVClientStructs' InventoryContainer:
    /// int Size, bool IsLoaded, InventoryItem* GetInventorySlot(int); InventoryItem: uint GetItemId().</summary>
    private static unsafe int? Used(InventoryManager* inventory, InventoryType first, InventoryType second)
    {
        var used = 0;
        foreach (var type in new[] { first, second })
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) return null;
            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item != null && item->GetItemId() != 0) used++;
            }
        }
        return used;
    }

    /// <summary>The cache's saddlebag: IsSaddleBagCached, and each 70-slot tab's item ids, each non-zero id one used slot,
    /// tagged with the module's CharacterContentId. FFXIVClientStructs' ItemFinderModule:
    /// static ItemFinderModule* Instance(), ulong CharacterContentId, bool IsSaddleBagCached, Span&lt;uint&gt; SaddleBagItemIds,
    /// PremiumSaddleBagItemIds.</summary>
    private static unsafe CachedTabs? CachedSaddlebags()
    {
        var module = ItemFinderModule.Instance();
        if (module == null || !module->IsSaddleBagCached) return null;
        return new CachedTabs(new CacheCount(module->CharacterContentId, NonZero(module->SaddleBagItemIds)), new CacheCount(module->CharacterContentId, NonZero(module->PremiumSaddleBagItemIds)));
    }

    /// <summary>MirageManager is a pointer slot; once PrismBoxLoaded, the non-zero PrismBoxItemIds of its 800. The client
    /// clears this data on a zone change, so a loaded dresser is taken to always be this character's; not yet confirmed. FFXIVClientStructs'
    /// MirageManager: static MirageManager* Instance(), bool PrismBoxLoaded, Span&lt;uint&gt;
    /// PrismBoxItemIds.</summary>
    private static unsafe int? LiveDresser()
    {
        var manager = MirageManager.Instance();
        return manager == null || !manager->PrismBoxLoaded ? null : NonZero(manager->PrismBoxItemIds);
    }

    /// <summary>The cache's dresser: IsGlamourDresserCached, then the non-zero GlamourDresserItemIds of its 800.
    /// FFXIVClientStructs' ItemFinderModule: bool IsGlamourDresserCached, Span&lt;uint&gt; GlamourDresserItemIds.</summary>
    private static unsafe CacheCount? CachedDresser()
    {
        var module = ItemFinderModule.Instance();
        return module == null || !module->IsGlamourDresserCached ? null : new CacheCount(module->CharacterContentId, NonZero(module->GlamourDresserItemIds));
    }

    /// <summary>The armoire's own state, Invalid, Requested or Loaded. FFXIVClientStructs'
    /// UIState: Cabinet Cabinet; Cabinet: CabinetState State.</summary>
    private static unsafe ClientCabinet.CabinetState? ArmoireState()
    {
        var ui = UIState.Instance();
        return ui == null ? null : ui->Cabinet.State;
    }

    /// <summary>Whether the armoire's own window is up: its store or its withdraw window's agent (92, 93) active, read the way
    /// LiveSaddlebags reads the saddlebag's. FFXIVClientStructs' AgentId: Cabinet = 92,
    /// CabinetWithdraw = 93; AgentModule: AgentInterface* GetAgentByInternalId(AgentId); AgentInterface: bool IsAgentActive().</summary>
    private static unsafe bool ArmoireWindowUp()
    {
        var agents = AgentModule.Instance();
        if (agents == null) return false;
        foreach (var id in new[] { AgentId.Cabinet, AgentId.CabinetWithdraw })
        {
            var agent = agents->GetAgentByInternalId(id);
            if (agent != null && agent->IsAgentActive()) return true;
        }
        return false;
    }

    /// <summary>IsItemInCabinet for each storable row, once the armoire reports loaded. FFXIVClientStructs' Cabinet:
    /// bool IsItemInCabinet(uint cabinetId), bool IsCabinetLoaded().</summary>
    private static unsafe int? LiveArmoire(IReadOnlyList<uint> storable)
    {
        var ui = UIState.Instance();
        if (ui == null || !ui->Cabinet.IsCabinetLoaded()) return null;
        var stored = 0;
        foreach (var row in storable)
        {
            if (ui->Cabinet.IsItemInCabinet(row)) stored++;
        }
        return stored;
    }

    /// <summary>The cache's armoire: its CabinetState ("See Cabinet.CabinetState"), Loaded being 2, and
    /// CabinetItemUnlockBits, 125 words over the Cabinet row ids, counted at each storable row by MoneyRules.Stored; the bit layout is taken
    /// to be so; not yet confirmed. FFXIVClientStructs' ItemFinderModule: byte CabinetState, Span&lt;uint&gt; CabinetItemUnlockBits; Cabinet.CabinetState
    /// { Invalid, Requested, Loaded }.</summary>
    private static unsafe CacheCount? CachedArmoire(IReadOnlyList<uint> storable)
    {
        var module = ItemFinderModule.Instance();
        if (module == null || module->CabinetState != (byte)ClientCabinet.CabinetState.Loaded) return null;
        return new CacheCount(module->CharacterContentId, MoneyRules.Stored(storable, module->CabinetItemUnlockBits));
    }

    /// <summary>PlayerState.HasPremiumSaddlebag, with no upstream comment, taken as a flag that can turn off. FFXIVClientStructs'
    /// PlayerState: bool HasPremiumSaddlebag.</summary>
    private static unsafe bool? PremiumFlag()
    {
        var state = PlayerState.Instance();
        return state == null ? null : state->HasPremiumSaddlebag;
    }

    private static int NonZero(ReadOnlySpan<uint> ids)
    {
        var count = 0;
        foreach (var id in ids)
        {
            if (id != 0) count++;
        }
        return count;
    }
}
