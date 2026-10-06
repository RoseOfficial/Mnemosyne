using System.Diagnostics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Readers;

/// <summary>The two families of Collections rows in memory at login: what IUnlockState answers, which Dalamud's
/// own unlock event raises, and the four log arrays, which nothing raises. One reader instance per family, so a moved member behind
/// one of them disables that family alone.</summary>
public enum CollectionFamily { Unlocks, Logs }

/// <summary>The AtLogin Collections rows.
///
/// The Unlocks family counts the ten collection kinds and the six sightseeing blocks through IUnlockState, at login, after an
/// unlock and on a territory change. Dalamud's own sweep raises the unlock event for a new mount, card or vista, so the
/// family is never on the tick.
///
/// The Logs family counts the crafting, gathering, fishing and spearfishing logs from the client's own completion arrays, which no
/// event raises. At login and on a level or class change it reads in full, since a class can open its log. On the tick it reads
/// only when the four arrays' set bits moved (CollectionRules.ReadNow).
///
/// Every gate is monotonic and every row is AtLogin, so a locked gate clears and an unreadable one keeps. A fish or
/// spearfish bit is read from the array's own bytes, bounded by their length (CollectionRules.Bits). FFXIVClientStructs'
/// BitArray.Get throws past the declared count, and TryGet refuses the last fish, whose index is the count itself (TryGet
/// refuses index &gt;= bitCount; PlayerState's CaughtFishBitArray has a bitCount of 1522 over 191 bytes); a row past the bytes
/// is not caught and is said once. The bardings are read through IUnlockState.IsBuddyEquipUnlocked, whose native function
/// knows every row, never through the managed bitmask, which is five rows short.
///
/// Each family logs its first full read per character beside the client's own counts, and once more when they first change,
/// with its cost. The Logs family also logs once per character what the client answers for the recipes at 30000
/// and up, timed apart from the read itself, so the first read's cost is not the one-off probe's.
/// Every pointer read sits in a private static unsafe helper behind the player-state guard. Nothing here writes,
/// requests or loads anything: PlayerState.SetOrnamentUnlocked and SetGlassesStyleUnlocked,
/// RecipeNote.InitializeRecipeList, BitArray.Set, TrySet and Get, and PlayerState.IsFishCaught and IsSpearfishCaught are never
/// called.</summary>
public sealed class CollectionsReader(CollectionFamily family, Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    // Each item with the row it counts over, paired once: the catalog and the rows do not change after load.
    private readonly IReadOnlyList<(ItemDefinition Item, CollectionKindRow Kind)> _kinds = family == CollectionFamily.Unlocks ? KindItems(catalog, rows) : [];
    private readonly IReadOnlyList<(ItemDefinition Item, SightseeingRow Vistas)> _vistas = family == CollectionFamily.Unlocks ? VistaItems(catalog, rows) : [];
    private readonly IReadOnlyList<(ItemDefinition Item, LogRow Log)> _logs = family == CollectionFamily.Logs ? LogItems(catalog, rows) : [];
    private readonly HashSet<string> _said = [];
    private LogPrint? _lastPrint;
    // The character the log lines were last written for: a switch writes the new character's first read again.
    private ulong _reportedOwner, _masterAskedFor;
    private string? _firstReport;
    private bool _changeReported;

    /// <summary>A Realm Reborn's vistas open with the sightseeing log itself and Heavensward's with the hand file's
    /// quest; every later block's quest is AdventureExPhase's, which the reducer carried.</summary>
    private const uint ArrExVersion = 0, HeavenswardExVersion = 1;

    /// <summary>A spearfishing row's bit is its row id minus 20000.</summary>
    private const int SpearfishOffset = 20000;

    public string Name => $"CollectionsReader({family})";
    public Category Category => Category.Collections;
    public ReaderTrigger Triggers => family switch
    {
        CollectionFamily.Unlocks => ReaderTrigger.Login | ReaderTrigger.Unlock | ReaderTrigger.Territory,
        CollectionFamily.Logs => ReaderTrigger.Login | ReaderTrigger.LevelOrJob | ReaderTrigger.Interval,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown collection family"),
    };

    /// <summary>A read that says nothing of its trigger is a full one.</summary>
    public IReadOnlyList<Reading>? Read() => Read(ReaderTrigger.Login);

    public IReadOnlyList<Reading>? Read(ReaderTrigger fired)
    {
        // Nothing is read while the player state is not loaded. Every IUnlockState member answers false before it,
        // which would store zeros over real counts.
        if (!player.IsLoaded) return null;
        return family switch
        {
            CollectionFamily.Unlocks => Unlocks(clock.UtcNow),
            CollectionFamily.Logs => Logs(fired, clock.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown collection family"),
        };
    }

    private static List<(ItemDefinition, CollectionKindRow)> KindItems(Catalog catalog, CatalogRows rows)
    {
        // The titles are a kind too, but they wait on their list, which AchievementReader reads.
        var byId = rows.Collections.Where(k => k.Kind != CollectionIds.Title).ToDictionary(k => CollectionIds.KindId(k.Kind), StringComparer.Ordinal);
        return catalog.InCategory(Category.Collections).Where(i => byId.ContainsKey(i.Id)).Select(i => (i, byId[i.Id])).ToList();
    }

    private static List<(ItemDefinition, SightseeingRow)> VistaItems(Catalog catalog, CatalogRows rows)
    {
        var byId = rows.SightseeingLogs.ToDictionary(s => CollectionIds.SightseeingId(s.ExVersionId), StringComparer.Ordinal);
        return catalog.InCategory(Category.Collections).Where(i => byId.ContainsKey(i.Id)).Select(i => (i, byId[i.Id])).ToList();
    }

    private static List<(ItemDefinition, LogRow)> LogItems(Catalog catalog, CatalogRows rows)
    {
        var byId = rows.Logs.ToDictionary(l => CollectionIds.LogId(l.Log, l.ClassJobId), StringComparer.Ordinal);
        return catalog.InCategory(Category.Collections).Where(i => byId.ContainsKey(i.Id)).Select(i => (i, byId[i.Id])).ToList();
    }

    /// <summary>The ten kinds and the six sightseeing blocks, each owned over its total. Only the bardings and the cards are gated,
    /// because without their quest the count means nothing; the sightseeing blocks each open with their own quest or byte.</summary>
    private IReadOnlyList<Reading> Unlocks(DateTimeOffset now)
    {
        var watch = Stopwatch.StartNew();
        var quests = data.GetExcelSheet<Quest>();
        var readings = new List<Reading>(_kinds.Count + _vistas.Count);
        var counts = new List<string>(_kinds.Count + _vistas.Count);
        foreach (var (item, kind) in _kinds)
        {
            var open = kind.Kind switch
            {
                CollectionIds.Barding => AnyQuestOpen(extras.CollectionUnlockQuests.Bardings, quests),
                CollectionIds.Card => QuestOpen(extras.CollectionUnlockQuests.TripleTriad, quests),
                _ => true,
            };
            if (open == null) continue;                                                     // unreadable: keeps what is stored
            if (open == false) { readings.Add(new Reading(item.Id, null)); continue; }       // locked, and AtLogin: the row clears
            if (Owned(kind) is not { } owned) continue;
            readings.Add(new Reading(item.Id, new CounterValue(owned, kind.Ids.Count, now)));
            counts.Add($"{kind.Kind} {owned}");
        }
        var adventures = data.GetExcelSheet<Adventure>();
        var sightseeing = SightseeingUnlockState();
        foreach (var (item, vistas) in _vistas)
        {
            var open = vistas.ExVersionId switch
            {
                ArrExVersion => sightseeing is { } state ? state >= 1 : (bool?)null,
                HeavenswardExVersion => QuestOpen(extras.CollectionUnlockQuests.SightseeingHeavensward, quests),
                _ => QuestOpen(vistas.QuestId, quests),
            };
            if (open == null) continue;
            if (open == false) { readings.Add(new Reading(item.Id, null)); continue; }
            var done = CollectionCounts.Owned(adventures, vistas.AdventureIds, unlocks.IsAdventureComplete);
            readings.Add(new Reading(item.Id, new CounterValue(done, vistas.AdventureIds.Count, now)));
            counts.Add($"vistas {vistas.ExVersionId} {done}");
        }
        Report("collection kinds and vistas", $"{string.Join(", ", counts)}; the client's own: {ClientKindCounts()}", "", watch.Elapsed);
        return readings;
    }

    /// <summary>Each kind through its own IUnlockState member; a hairstyle is an unlock link, asked the way
    /// IsCharaMakeCustomizeUnlocked asks it for a purchasable row.</summary>
    private int? Owned(CollectionKindRow kind) => kind.Kind switch
    {
        CollectionIds.Mount => CollectionCounts.Owned(data.GetExcelSheet<Mount>(), kind.Ids, unlocks.IsMountUnlocked),
        CollectionIds.Minion => CollectionCounts.Owned(data.GetExcelSheet<Companion>(), kind.Ids, unlocks.IsCompanionUnlocked),
        CollectionIds.Emote => CollectionCounts.Owned(data.GetExcelSheet<Emote>(), kind.Ids, unlocks.IsEmoteUnlocked),
        CollectionIds.Barding => CollectionCounts.Owned(data.GetExcelSheet<BuddyEquip>(), kind.Ids, unlocks.IsBuddyEquipUnlocked),
        CollectionIds.Hairstyle => kind.Ids.Count(link => unlocks.IsUnlockLinkUnlocked((ushort)link)),
        CollectionIds.Facewear => CollectionCounts.Owned(data.GetExcelSheet<GlassesStyle>(), kind.Ids, unlocks.IsGlassesStyleUnlocked),
        CollectionIds.Ornament => CollectionCounts.Owned(data.GetExcelSheet<Ornament>(), kind.Ids, unlocks.IsOrnamentUnlocked),
        CollectionIds.Orchestrion => CollectionCounts.Owned(data.GetExcelSheet<Orchestrion>(), kind.Ids, unlocks.IsOrchestrionUnlocked),
        CollectionIds.Card => CollectionCounts.Owned(data.GetExcelSheet<TripleTriadCard>(), kind.Ids, unlocks.IsTripleTriadCardUnlocked),
        CollectionIds.FramerKit => CollectionCounts.Owned(data.GetExcelSheet<Item>(), kind.Ids, unlocks.IsItemUnlocked),
        _ => Missing(kind.Kind),
    };

    /// <summary>The four logs. On a tick whose population counts did not move since the last full read nothing is read: no event says a
    /// fish was caught or a recipe crafted. A login and a level or class change always read, and the count names the
    /// character, so a switch reads too.</summary>
    private IReadOnlyList<Reading>? Logs(ReaderTrigger fired, DateTimeOffset now)
    {
        if (ArrayCounts() is not { } counts) return null;
        var print = new LogPrint(player.ContentId, counts.Fish, counts.Spearfish, counts.Gathered, counts.Recipes);
        if (!CollectionRules.ReadNow(fired, ReaderTrigger.Interval, _lastPrint, print)) return [];
        _lastPrint = print;
        var watch = Stopwatch.StartNew();
        var jobs = data.GetExcelSheet<ClassJob>();
        var caught = CaughtBits();
        var readings = new List<Reading>(_logs.Count);
        var said = new List<string>(_logs.Count);
        foreach (var (item, row) in _logs)
        {
            // Each log is gated on its class at level 1; locked clears (AtLogin), a class row that does not resolve says nothing.
            var open = jobs.GetRowOrDefault(row.ClassJobId) is { } job ? player.GetClassJobLevel(job) >= 1 : (bool?)null;
            if (open == null) continue;
            if (open == false) { readings.Add(new Reading(item.Id, null)); continue; }
            int? owned = row.Log switch
            {
                CollectionIds.Craft => RecipesCrafted(row.Ids),
                CollectionIds.Gather => ItemsGathered(row.Ids),
                CollectionIds.Fish => caught is { } bits ? Caught(row, bits.Fish, 0) : null,
                CollectionIds.Spearfish => caught is { } bits ? Caught(row, bits.Spearfish, SpearfishOffset) : null,
                _ => Missing(row.Log),
            };
            if (owned is not { } count) continue;
            readings.Add(new Reading(item.Id, new CounterValue(count, row.Ids.Count, now)));
            said.Add($"{row.Log} {row.ClassJobId} {count}");
        }
        var elapsed = watch.Elapsed;
        var master = "";
        if (_masterAskedFor != player.ContentId)
        {
            _masterAskedFor = player.ContentId;
            var probe = Stopwatch.StartNew();
            var crafted = MasterRecipesCrafted(data.GetExcelSheet<Recipe>());
            master = $"; recipes at 30000 and up the client answers crafted: {crafted} (never counted); probe {probe.Elapsed.TotalMicroseconds:0} us, not part of the read above";
        }
        Report("logs", $"{string.Join(", ", said)}; the client's own: {ClientLogCounts()}", master, elapsed);
        return readings;
    }

    /// <summary>A fish or spearfish log over its copied bits (CollectionRules.Owned): a row past the array's bytes is not caught, and
    /// is said once per row for the session.</summary>
    private int Caught(LogRow row, Func<int, bool?> bits, int offset)
    {
        var (owned, beyond) = CollectionRules.Owned(row.Ids, offset, bits);
        foreach (var id in beyond)
        {
            if (_said.Add($"{row.Log}:{id}")) log($"logs: {row.Log} row {id} is past the client's array; counted as not caught");
        }
        return owned;
    }

    /// <summary>A kind or log with no arm above is a defect in this reader, not a sheet change: say so once, count nothing.</summary>
    private int? Missing(string kind)
    {
        if (_said.Add(kind)) log($"collections: {kind} has no reader; not counted");
        return null;
    }

    /// <summary>Logs the first full read per character, and once more the first later read whose counts differ from it, showing a
    /// login frame read before the arrays filled, compared with the client's own counts, and the cost; extra is written with the
    /// first read only and never compared.</summary>
    private void Report(string what, string counts, string extra, TimeSpan elapsed)
    {
        if (player.ContentId != _reportedOwner)
        {
            _reportedOwner = player.ContentId;
            _firstReport = null;
            _changeReported = false;
        }
        if (_firstReport == null)
        {
            _firstReport = counts;
            log($"{what}: first read {counts}{extra}; {elapsed.TotalMicroseconds:0} us");
            return;
        }
        if (_changeReported || counts == _firstReport) return;
        _changeReported = true;
        log($"{what}: changed after the first read: {counts}; {elapsed.TotalMicroseconds:0} us");
    }

    /// <summary>A quest gate: true or false once the quest row resolves, null when it does not (never judged from an
    /// unresolved row).</summary>
    private bool? QuestOpen(uint questId, ExcelSheet<Quest> quests) =>
        questId != 0 && quests.GetRowOrDefault(questId) is { } quest ? unlocks.IsQuestCompleted(quest) : null;

    /// <summary>Open once any listed quest is complete, as the PvP gate is (the three "My Little Chocobo" quests); null when
    /// none of them resolves.</summary>
    private bool? AnyQuestOpen(IReadOnlyList<uint> questIds, ExcelSheet<Quest> quests)
    {
        var resolved = false;
        foreach (var id in questIds)
        {
            if (quests.GetRowOrDefault(id) is not { } quest) continue;
            resolved = true;
            if (unlocks.IsQuestCompleted(quest)) return true;
        }
        return resolved ? false : null;
    }

    private readonly record struct LogPrint(ulong Owner, int Fish, int Spearfish, int Gathered, int Recipes);

    /// <summary>The set bits of the four log arrays, the fingerprint a tick compares. FFXIVClientStructs' PlayerState
    /// (Span&lt;byte&gt; CaughtFish, 191 bytes; CaughtSpearfish, 38) and QuestManager (static QuestManager* Instance();
    /// Span&lt;byte&gt; GatheredGatheringItems, 102; CompletedRecipes, 801).</summary>
    private static unsafe (int Fish, int Spearfish, int Gathered, int Recipes)? ArrayCounts()
    {
        var state = PlayerState.Instance();
        var quests = QuestManager.Instance();
        if (state == null || quests == null) return null;
        return (CollectionCounts.Population(state->CaughtFish), CollectionCounts.Population(state->CaughtSpearfish),
            CollectionCounts.Population(quests->GatheredGatheringItems), CollectionCounts.Population(quests->CompletedRecipes));
    }

    /// <summary>The caught-fish and caught-spearfish bits, copied from the arrays' own bytes and bounded by their
    /// length, never by the declared BitCount, which for the fish array is the last log fish's id and so refuses that fish.
    /// FFXIVClientStructs' PlayerState (Span&lt;byte&gt; CaughtFish, 191 bytes; CaughtSpearfish, 38; CaughtFishBitArray bitCount
    /// 1522, CaughtSpearfishBitArray 304) and BitArray (TryGet refuses index &gt;= bitCount).
    /// IsFishCaught and IsSpearfishCaught, which call BitArray.Get and throw past the count, are never called.</summary>
    private static unsafe (Func<int, bool?> Fish, Func<int, bool?> Spearfish)? CaughtBits()
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        return (CollectionCounts.Bits(state->CaughtFish), CollectionCounts.Bits(state->CaughtSpearfish));
    }

    /// <summary>The native static over the completed-recipe array: FFXIVClientStructs' QuestManager, public static bool
    /// IsRecipeComplete(uint recipeId).</summary>
    private static int RecipesCrafted(IReadOnlyList<uint> ids)
    {
        var count = 0;
        foreach (var id in ids)
        {
            if (QuestManager.IsRecipeComplete(id)) count++;
        }
        return count;
    }

    /// <summary>The native static over the gathered-item array; its item-to-bit rule is unpublished, and that it answers for every
    /// notebook item is taken to be so; not yet confirmed. FFXIVClientStructs' QuestManager, public static bool
    /// IsGatheringItemGathered(ushort gatheringItemId).</summary>
    private static int ItemsGathered(IReadOnlyList<uint> ids)
    {
        var count = 0;
        foreach (var id in ids)
        {
            if (QuestManager.IsGatheringItemGathered((ushort)id)) count++;
        }
        return count;
    }

    /// <summary>What the client answers for the recipes at 30000 and up with a result, which no row counts. The
    /// reader asks once per character, for whether the totals should widen to all 13,892.</summary>
    private static int MasterRecipesCrafted(ExcelSheet<Recipe> recipes)
    {
        var crafted = 0;
        foreach (var recipe in recipes)
        {
            if (recipe.RowId >= CollectionIds.RecipeLogEnd && recipe.ItemResult.RowId != 0 && QuestManager.IsRecipeComplete(recipe.RowId)) crafted++;
        }
        return crafted;
    }

    /// <summary>PlayerState.SightseeingLogUnlockState, "0 = Not Unlocked, 1 = ARR Part 1, 2 = ARR Part 2", the
    /// gate on A Realm Reborn's vistas. FFXIVClientStructs' PlayerState: byte SightseeingLogUnlockState.</summary>
    private static unsafe byte? SightseeingUnlockState()
    {
        var state = PlayerState.Instance();
        return state == null ? null : state->SightseeingLogUnlockState;
    }

    /// <summary>The client's own counts, for the log only. FFXIVClientStructs' PlayerState (ushort NumOwnedMounts) and UIState
    /// (static UIState* Instance(); uint UnlockedCompanionsCount; ulong UnlockedTripleTriadCardsCount).</summary>
    private static unsafe string ClientKindCounts()
    {
        var state = PlayerState.Instance();
        var ui = UIState.Instance();
        if (state == null || ui == null) return "absent";
        return $"mounts {state->NumOwnedMounts}, minions {ui->UnlockedCompanionsCount}, cards {ui->UnlockedTripleTriadCardsCount}";
    }

    /// <summary>The client's own fish counts, for the log only. FFXIVClientStructs' PlayerState: uint NumFishCaught,
    /// NumSpearfishCaught.</summary>
    private static unsafe string ClientLogCounts()
    {
        var state = PlayerState.Instance();
        return state == null ? "absent" : $"fish {state->NumFishCaught}, spearfish {state->NumSpearfishCaught}";
    }
}
