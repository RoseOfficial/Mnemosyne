using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.MJI;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Interop.Readers;

/// <summary>The six families of place rows this reader covers. One reader instance per family, so a moved
/// member behind one director disables that family alone.</summary>
public enum PlaceFamily { Eureka, Bozja, OccultCrescent, CosmicExploration, IslandSanctuary, DeepDungeons }

/// <summary>The Content rows the client holds only while the character stands in their content (NeedsPlace). Every
/// family runs at login, on a territory change and on the tick: the tick keeps a value current while the character stays inside and
/// lets a plugin loaded inside the zone read at all. Each first asks its unlock quest for a gated row: locked stores a gate lock,
/// unreadable says nothing. Then, inside its territory (the zone's TerritoryIntendedUse, from
/// Dalamud's current territory through the sheet), it reads the value; outside it sends the open gate, which lifts a stale lock and
/// leaves the last visit's value alone, so the row keeps it with "as of last visit to <place>". The Island Sanctuary rank is read
/// wherever the client holds the character's own island; whether it is AtLogin is not yet settled. Each family logs what it
/// read once per zone, showing the halves that are taken to be true but not yet confirmed. Every pointer read sits in a
/// private static unsafe helper that copies plain values out, behind the player-state guard. Nothing here writes, requests,
/// loads or moves anything: WKSManager.Load, MJIManager.RequestDemandFull, RequestFavorData and ScheduleCraft,
/// PublicContentBozja.UseFromHolster, PublicContentOccultCrescent.ChangeSupportJob, InstanceContentDeepDungeon.UsePomander and
/// UseStone, and every director virtual are never called.</summary>
public sealed class PlaceReader(PlaceFamily family, Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IClientState client, IDataManager data, Action<string> log) : IProgressReader
{
    private readonly IReadOnlyList<ItemDefinition> _items = catalog.InCategory(Category.Content).Where(i => Prefixes(family).Any(p => i.Id.StartsWith(p, StringComparison.Ordinal))).ToList();
    private readonly Dictionary<uint, PhantomJobRow> _phantoms = family == PlaceFamily.OccultCrescent ? rows.PhantomJobs.ToDictionary(j => j.RowId) : new Dictionary<uint, PhantomJobRow>();
    private readonly Dictionary<uint, CosmicClassRow> _tools = family == PlaceFamily.CosmicExploration ? rows.CosmicClasses.ToDictionary(c => c.RowId) : new Dictionary<uint, CosmicClassRow>();
    private readonly Dictionary<uint, uint> _dungeonQuests = family == PlaceFamily.DeepDungeons ? extras.PlaceUnlockQuests.DeepDungeons.ToDictionary(g => g.DungeonId, g => g.QuestId) : new Dictionary<uint, uint>();
    // Each dungeon item with its dungeon id and whether it is the arm (not the armor) row, found once: the catalog does not
    // change after construction, so no id needs splitting on every tick, the same fold TroveReader._troves does.
    private readonly IReadOnlyList<(ItemDefinition Item, uint Dungeon, bool Weapon)> _dungeonItems = family == PlaceFamily.DeepDungeons ? DeepDungeonItems(catalog) : [];
    private readonly HashSet<string> _reported = [];
    // The character _reported was last logged for: a switch clears it, so the new character's first read
    // in a zone still writes an evidence line, rather than being silently suppressed by a match with the previous character.
    private ulong _reportedOwner;

    /// <summary>Each family's TerritoryType.TerritoryIntendedUse.</summary>
    private const uint DeepDungeonUse = 31, EurekaUse = 41, BozjaUse = 48, IslandUse = 49, CosmicUse = 60, OccultUse = 61;

    /// <summary>PlayerState.GetContentValue's keys for the current elemental level, resistance rank and knowledge
    /// level, "Only loaded inside the relevant content".</summary>
    private const uint ElementalLevelKey = 4, ResistanceRankKey = 5, KnowledgeLevelKey = 7;

    public string Name => $"PlaceReader({family})";
    public Category Category => Category.Content;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Territory | ReaderTrigger.Interval;

    public IReadOnlyList<Reading>? Read()
    {
        // Nothing is read while the player state is not loaded. Every gate below answers "locked" before it.
        if (!player.IsLoaded) return null;
        if (_items.Count == 0) return [];
        var quests = data.GetExcelSheet<Quest>();
        var place = Place();
        return family switch
        {
            PlaceFamily.Eureka => SharedLevel(quests, place, extras.PlaceUnlockQuests.Eureka, EurekaUse, ElementalLevelKey, extras.ContentMaxima.ElementalLevel, EurekaProgress),
            PlaceFamily.Bozja => SharedLevel(quests, place, extras.PlaceUnlockQuests.Bozja, BozjaUse, ResistanceRankKey, extras.ContentMaxima.ResistanceRank, BozjaProgress),
            PlaceFamily.OccultCrescent => Occult(quests, place),
            PlaceFamily.CosmicExploration => Cosmic(quests, place),
            PlaceFamily.IslandSanctuary => Island(quests, place),
            PlaceFamily.DeepDungeons => DeepDungeons(quests, place),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown place family"),
        };
    }

    /// <summary>The id families each instance fills.</summary>
    private static string[] Prefixes(PlaceFamily family) => family switch
    {
        PlaceFamily.Eureka => [CatalogBuilder.EurekaLevelId],
        PlaceFamily.Bozja => [CatalogBuilder.BozjaRankId],
        PlaceFamily.OccultCrescent => [CatalogBuilder.OccultKnowledgeId, CatalogBuilder.PhantomJobPrefix],
        PlaceFamily.CosmicExploration => [CatalogBuilder.CosmicToolPrefix],
        PlaceFamily.IslandSanctuary => [CatalogBuilder.IslandRankId],
        PlaceFamily.DeepDungeons => [CatalogBuilder.DeepDungeonPrefix],
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown place family"),
    };

    /// <summary>The current territory's intended use and place name, through Dalamud's current territory and the sheet; null when the
    /// row does not resolve, which reads as "not in any place".</summary>
    private (uint Use, string Name)? Place() =>
        data.GetExcelSheet<TerritoryType>().GetRowOrDefault(client.TerritoryType) is { } territory
            ? (territory.TerritoryIntendedUse.RowId, territory.PlaceName.ValueNullable?.Name.ExtractText() ?? "")
            : null;

    /// <summary>One character-wide level held only inside its content: the elemental level in any Eureka zone, the resistance
    /// rank in the Southern Front or Zadnor. Labelled with the zone it was last read in; the director's experience is the
    /// second line when it answers.</summary>
    private IReadOnlyList<Reading> SharedLevel(ExcelSheet<Quest> quests, (uint Use, string Name)? place, uint questId, uint use, uint key, int max, Func<(int Current, int Needed)?> progress)
    {
        var item = _items[0];
        var open = QuestOpen(questId, quests);
        if (open != true || place is not { } here || here.Use != use) return Readings.Gated(item.Id, open, null);
        var level = (int)ContentValue(key);
        var step = progress();
        Report($"{family}:{here.Name}:{level > 0}", $"{family}: level {level} of {max}, progress {(step is { } s ? $"{s.Current}/{s.Needed}" : "none")}, in {here.Name}");
        var value = ContentRules.Level(level, max, step?.Current, step?.Needed);
        return Readings.Gated(item.Id, open, value, label: value is null ? null : $"{item.Name} (last read in {here.Name})");
    }

    /// <summary>The knowledge level and the 24 phantom jobs, in either Horn. The knowledge level is labelled with the Horn, so a
    /// level that differs between them shows; a phantom job is read from the director's state once it reports loaded, at the
    /// MKDSupportJob row's index, taken to be so; not yet confirmed, a 0 stored as "not acquired". The whole array is logged once
    /// per Horn.</summary>
    private IReadOnlyList<Reading> Occult(ExcelSheet<Quest> quests, (uint Use, string Name)? place)
    {
        var open = QuestOpen(extras.PlaceUnlockQuests.OccultCrescent, quests);
        var zone = open == true && place is { } p && p.Use == OccultUse ? p.Name : null;
        var knowledge = zone != null ? (int)ContentValue(KnowledgeLevelKey) : 0;
        var state = zone != null ? OccultSnapshot() : null;
        if (zone != null)
            Report($"occult:{zone}:{state != null}", $"occult crescent: knowledge {knowledge}; state {(state is { } s ? $"knowledge {s.Knowledge}/{s.Needed}, phantom levels [{string.Join(",", s.JobLevels)}]" : "not loaded")}; in {zone}");
        var readings = new List<Reading>(_items.Count);
        foreach (var item in _items)
        {
            LevelValue? value = null;
            string? label = null;
            if (item.Id == CatalogBuilder.OccultKnowledgeId)
            {
                value = ContentRules.Level(knowledge, extras.ContentMaxima.KnowledgeLevel, state?.Knowledge, state?.Needed);
                if (value != null) label = $"{item.Name} (last read in {zone})";
            }
            else if (state is { } loaded && Number(item.Id, 2) is { } row && _phantoms.TryGetValue(row, out var job) && row < loaded.JobLevels.Length)
                value = new LevelValue(loaded.JobLevels[row], job.LevelMax);
            readings.AddRange(Readings.Gated(item.Id, open, value, label: label));
        }
        return readings;
    }

    /// <summary>Eleven research tools on any planet: CurrentStages[row − 1] over the tool's own stage count, once the research
    /// module reports loaded. That this is the research reached, per class and not per planet, is taken to be so; not yet
    /// confirmed, so both stage arrays are logged once per planet.</summary>
    private IReadOnlyList<Reading> Cosmic(ExcelSheet<Quest> quests, (uint Use, string Name)? place)
    {
        var open = QuestOpen(extras.PlaceUnlockQuests.CosmicExploration, quests);
        var planet = open == true && place is { } p && p.Use == CosmicUse ? p.Name : null;
        var research = planet != null ? CosmicSnapshot() : null;
        if (planet != null)
            Report($"cosmic:{planet}:{research != null}", $"cosmic exploration: {(research is { } r ? $"current stages [{string.Join(",", r.Current)}], unlocked stages [{string.Join(",", r.Unlocked)}]" : "research not loaded")} on {planet}");
        var readings = new List<Reading>(_items.Count);
        foreach (var item in _items)
        {
            LevelValue? value = null;
            if (research is { } stages && Number(item.Id, 2) is { } row && _tools.TryGetValue(row, out var tool) && row >= 1 && row <= stages.Current.Length)
                value = new LevelValue(stages.Current[row - 1], tool.Stages);
            readings.AddRange(Readings.Gated(item.Id, open, value));
        }
        return readings;
    }

    /// <summary>The Island Sanctuary rank, read wherever the manager holds the character's own island. The first read of each kind is
    /// logged, on or off the island and with or without a rank produced, showing that an off-island read
    /// at login that finds the island not yet loaded cannot hide a later off-island read that does produce the rank.</summary>
    private IReadOnlyList<Reading> Island(ExcelSheet<Quest> quests, (uint Use, string Name)? place)
    {
        var item = _items[0];
        var open = QuestOpen(extras.PlaceUnlockQuests.IslandSanctuary, quests);
        if (open != true) return Readings.Gated(item.Id, open, null);
        var island = IslandSnapshot();
        var onIsland = island?.InSanctuary == true || place?.Use == IslandUse;
        var value = island is { } held ? ContentRules.IslandRank(held.Own, held.Rank, held.Experience, rows.IslandRanks) : null;
        Report($"island:{onIsland}:{island?.Own}:{value != null}", island is { } seen
            ? $"island sanctuary: own {seen.Own}, rank {seen.Rank}, experience {seen.Experience}, {(value != null ? "a rank produced" : "no rank produced")}; read {(onIsland ? "on" : "off")} the island (territory {client.TerritoryType})"
            : $"island sanctuary: the manager is absent {(onIsland ? "on" : "off")} the island (territory {client.TerritoryType})");
        return Readings.Gated(item.Id, open, value);
    }

    /// <summary>Each deep dungeon's aetherpool arm and armour, read in a run from the run's own director when its DeepDungeonId
    /// is the row: taken to be so; not yet confirmed. Logged once per dungeon and per whether a strength was set yet, with the
    /// floor and the synced level beside them, so a first read before the director's fields are filled cannot hide the one that
    /// is. A dungeon the hand file does not gate is read ungated, as the builder said.</summary>
    private IReadOnlyList<Reading> DeepDungeons(ExcelSheet<Quest> quests, (uint Use, string Name)? place)
    {
        var run = place?.Use == DeepDungeonUse ? DeepDungeonSnapshot() : null;
        if (run is { } seen)
            Report($"deep:{seen.Dungeon}:{seen.Weapon > 0 || seen.Armor > 0}", $"deep dungeon: id {seen.Dungeon}, floor {seen.Floor}, weapon {seen.Weapon}, armor {seen.Armor}, synced {seen.Synced}; in {place?.Name}");
        var readings = new List<Reading>(_dungeonItems.Count);
        foreach (var (item, dungeon, weapon) in _dungeonItems)
        {
            var open = _dungeonQuests.TryGetValue(dungeon, out var quest) ? QuestOpen(quest, quests) : true;
            LevelValue? value = null;
            if (open == true && run is { } inRun && inRun.Dungeon == dungeon)
                value = new LevelValue(weapon ? inRun.Weapon : inRun.Armor, extras.ContentMaxima.Aetherpool);
            readings.AddRange(Readings.Gated(item.Id, open, value));
        }
        return readings;
    }

    /// <summary>Each deep-dungeon item's dungeon id and whether it is the arm row, parsed from its id once.</summary>
    private static List<(ItemDefinition Item, uint Dungeon, bool Weapon)> DeepDungeonItems(Catalog catalog)
    {
        var list = new List<(ItemDefinition, uint, bool)>();
        foreach (var item in catalog.InCategory(Category.Content))
        {
            if (!item.Id.StartsWith(CatalogBuilder.DeepDungeonPrefix, StringComparison.Ordinal)) continue;
            var parts = item.Id.Split(':');
            if (parts.Length == 3 && uint.TryParse(parts[1], out var dungeon)) list.Add((item, dungeon, parts[2] == "arm"));
        }
        return list;
    }

    /// <summary>A field of an id, parsed; null when it is absent or not a number.</summary>
    private static uint? Number(string id, int index)
    {
        var parts = id.Split(':');
        return index < parts.Length && uint.TryParse(parts[index], out var value) ? value : null;
    }

    private void Report(string key, string message)
    {
        if (player.ContentId != _reportedOwner) { _reportedOwner = player.ContentId; _reported.Clear(); }   // a switch logs the new character's evidence again
        if (_reported.Add(key)) log(message);
    }

    /// <summary>A quest gate: true or false once the quest row resolves, null when it does not (never judged from an
    /// unresolved row).</summary>
    private bool? QuestOpen(uint questId, ExcelSheet<Quest> quests) =>
        questId != 0 && quests.GetRowOrDefault(questId) is { } quest ? unlocks.IsQuestCompleted(quest) : null;

    /// <summary>The content value for a key, 0 when the player state is absent or does not hold the key.
    /// FFXIVClientStructs' PlayerState: uint GetContentValue(uint key), a lookup
    /// over three pairs that returns 0 when none matches.</summary>
    private static unsafe uint ContentValue(uint key)
    {
        var state = PlayerState.Instance();
        return state == null ? 0 : state->GetContentValue(key);
    }

    /// <summary>The elemental experience from the Eureka director's state. FFXIVClientStructs' PublicContentEureka: static
    /// EurekaState* GetState(); EurekaState.CurrentExperience, NeededExperience (uint).</summary>
    private static unsafe (int Current, int Needed)? EurekaProgress()
    {
        var state = PublicContentEureka.GetState();
        if (state == null) return null;
        return ((int)state->CurrentExperience, (int)state->NeededExperience);
    }

    /// <summary>Mettle from the Bozja director's state, null unless the director is active and initialised. FFXIVClientStructs'
    /// PublicContentBozja: static BozjaState* GetState(); BozjaState.CurrentExperience, NeededExperience.</summary>
    private static unsafe (int Current, int Needed)? BozjaProgress()
    {
        var state = PublicContentBozja.GetState();
        if (state == null) return null;
        return ((int)state->CurrentExperience, (int)state->NeededExperience);
    }

    private readonly record struct OccultState(int Knowledge, int Needed, byte[] JobLevels);

    /// <summary>The Occult Crescent director's state, guarded by GetInstance() != null && StateLoaded; the phantom
    /// levels copied out. FFXIVClientStructs' PublicContentOccultCrescent: static GetInstance(); bool StateLoaded;
    /// OccultCrescentState State (CurrentKnowledge, NeededKnowledge uint; SupportJobLevels Span&lt;byte&gt; of 24).</summary>
    private static unsafe OccultState? OccultSnapshot()
    {
        var director = PublicContentOccultCrescent.GetInstance();
        if (director == null || !director->StateLoaded) return null;
        return new OccultState((int)director->State.CurrentKnowledge, (int)director->State.NeededKnowledge, director->State.SupportJobLevels.ToArray());
    }

    /// <summary>WKSManager.Instance() (may be null) → IsLoaded, "Set to 1 when WKS territory loads" → ResearchModule →
    /// IsLoaded → CurrentStages, with UnlockedStages copied for the log. FFXIVClientStructs' WKSManager, WKSResearchModule.
    /// WKSManager.Load is never called.</summary>
    private static unsafe (byte[] Current, byte[] Unlocked)? CosmicSnapshot()
    {
        var manager = WKSManager.Instance();
        if (manager == null || !manager->IsLoaded) return null;
        var research = manager->ResearchModule;
        if (research == null || !research->IsLoaded) return null;
        return (research->CurrentStages.ToArray(), research->UnlockedStages.ToArray());
    }

    /// <summary>MJIManager.Instance() (may be null), IsPlayerInSanctuary, and the island state's CanEditIsland,
    /// CurrentRank and CurrentXP. FFXIVClientStructs' MJIManager, IslandState. RequestDemandFull, RequestFavorData and
    /// ScheduleCraft are never called.</summary>
    private static unsafe (bool Own, int Rank, int Experience, bool InSanctuary)? IslandSnapshot()
    {
        var manager = MJIManager.Instance();
        if (manager == null) return null;
        return (manager->IslandState.CanEditIsland, manager->IslandState.CurrentRank, (int)manager->IslandState.CurrentXP, manager->IsPlayerInSanctuary);
    }

    /// <summary>The run's own director through EventFramework, null outside a deep dungeon. FFXIVClientStructs' EventFramework:
    /// static Instance(), InstanceContentDeepDungeon* GetInstanceContentDeepDungeon(); InstanceContentDeepDungeon's byte
    /// fields. UsePomander and UseStone are never called.</summary>
    private static unsafe (uint Dungeon, int Weapon, int Armor, int Floor, int Synced)? DeepDungeonSnapshot()
    {
        var framework = EventFramework.Instance();
        if (framework == null) return null;
        var run = framework->GetInstanceContentDeepDungeon();
        if (run == null) return null;
        return (run->DeepDungeonId, run->WeaponLevel, run->ArmorLevel, run->Floor, run->SyncedGearLevel);
    }
}
