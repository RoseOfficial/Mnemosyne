using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mnemosyne.Core.Definitions;

/// <summary>What the sheets cannot say, from the embedded hand file. Loading is strict: a malformed file is an error
/// at plugin start, never a silent default, because every value here is a game fact with a cited source.</summary>
public sealed record CatalogExtras(
    IReadOnlyList<EventOccurrence> EventOccurrences,
    IReadOnlyList<FeatureUnlock> FeatureUnlocks,
    IReadOnlyList<ChroniclesKind> ChroniclesKinds,
    int LevelCap,
    IReadOnlyList<LimitedJobCap> LimitedJobCaps,
    ResetCaps ResetCaps,
    IReadOnlyList<RouletteGate> RouletteGates,
    IReadOnlyList<uint> LevequestUnlockQuests,
    int TreasureMapGatherLevel,
    UnlockQuests UnlockQuests,
    FashionReportRules FashionReport,
    IReadOnlyList<Trove> Troves,
    ContentUnlockQuests ContentUnlockQuests,
    IReadOnlyList<VariantSeriesGate> VariantSeries,
    PlaceUnlockQuests PlaceUnlockQuests,
    ContentMaxima ContentMaxima,
    HouseVisitRules HouseVisit,
    CollectionUnlockQuests CollectionUnlockQuests,
    StorageRules Storage)
{
    /// <summary>The cap for one class or job: its own when the hand file names it, else the global level cap. Every limited
    /// job needs one because no sheet, no Dalamud service and no client field states a per-job maximum.</summary>
    public int CapFor(uint classJobId) => LimitedJobCaps.FirstOrDefault(c => c.JobId == classJobId)?.Cap ?? LevelCap;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static CatalogExtras Load()
    {
        using var stream = typeof(CatalogExtras).Assembly.GetManifestResourceStream("Mnemosyne.Core.Data.catalog-extras.json")
            ?? throw new InvalidOperationException("the hand file catalog-extras.json is not embedded");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static CatalogExtras Parse(string json)
    {
        var file = JsonSerializer.Deserialize<HandFile>(json, Options) ?? throw new InvalidOperationException("the hand file is empty");
        if (file.EventOccurrences == null) throw new InvalidOperationException("the hand file is missing its eventOccurrences section");
        if (file.FeatureUnlocks == null) throw new InvalidOperationException("the hand file is missing its featureUnlocks section");
        if (file.ChroniclesKinds == null) throw new InvalidOperationException("the hand file is missing its chroniclesKinds section");
        if (file.LevelCap == null || file.LimitedJobCaps == null) throw new InvalidOperationException("the hand file is missing a level cap");
        if (file.ResetCaps == null) throw new InvalidOperationException("the hand file is missing its resetCaps section");
        if (file.RouletteGates == null) throw new InvalidOperationException("the hand file is missing its rouletteGates section");
        if (file.LevequestUnlockQuests == null) throw new InvalidOperationException("the hand file is missing its levequestUnlockQuests section");
        if (file.TreasureMapGatherLevel == null) throw new InvalidOperationException("the hand file is missing its treasureMapGatherLevel");
        if (file.UnlockQuests == null) throw new InvalidOperationException("the hand file is missing its unlockQuests section");
        if (file.FashionReport == null) throw new InvalidOperationException("the hand file is missing its fashionReport section");
        if (file.Troves == null) throw new InvalidOperationException("the hand file is missing its troves section");
        if (file.ContentUnlockQuests == null) throw new InvalidOperationException("the hand file is missing its contentUnlockQuests section");
        if (file.VariantSeries == null) throw new InvalidOperationException("the hand file is missing its variantSeries section");
        if (file.PlaceUnlockQuests == null) throw new InvalidOperationException("the hand file is missing its placeUnlockQuests section");
        if (file.ContentMaxima == null) throw new InvalidOperationException("the hand file is missing its contentMaxima section");
        if (file.HouseVisit == null) throw new InvalidOperationException("the hand file is missing its houseVisit section");
        if (file.CollectionUnlockQuests == null) throw new InvalidOperationException("the hand file is missing its collectionUnlockQuests section");
        if (file.Storage == null) throw new InvalidOperationException("the hand file is missing its storage section");
        ValidateOccurrences(file.EventOccurrences);
        ValidateUnlocksAndKinds(file.FeatureUnlocks, file.ChroniclesKinds);
        ValidateCaps(file.ResetCaps);
        ValidateJobCaps(file.LevelCap.Value, file.LimitedJobCaps);
        var gates = ParseRouletteGates(file.RouletteGates);
        ValidateUnlockGates(file.LevequestUnlockQuests, file.TreasureMapGatherLevel.Value, file.LevelCap.Value);
        ValidateUnlockQuests(file.UnlockQuests);
        ValidateFashionReport(file.FashionReport);
        ValidateTroves(file.Troves);
        ValidateContentUnlockQuests(file.ContentUnlockQuests);
        ValidateVariantSeries(file.VariantSeries);
        ValidatePlaceUnlockQuests(file.PlaceUnlockQuests);
        ValidateContentMaxima(file.ContentMaxima);
        ValidateHouseVisit(file.HouseVisit);
        ValidateCollectionUnlockQuests(file.CollectionUnlockQuests);
        ValidateStorage(file.Storage);
        return new CatalogExtras(file.EventOccurrences, file.FeatureUnlocks, file.ChroniclesKinds, file.LevelCap.Value, file.LimitedJobCaps, file.ResetCaps,
            gates, file.LevequestUnlockQuests, file.TreasureMapGatherLevel.Value, file.UnlockQuests, file.FashionReport, file.Troves,
            file.ContentUnlockQuests, file.VariantSeries, file.PlaceUnlockQuests, file.ContentMaxima, file.HouseVisit, file.CollectionUnlockQuests, file.Storage);
    }

    /// <summary>Every occurrence must name its genre, and no (genre, festival) pair may repeat: either would let
    /// <see cref="CatalogBuilder"/> attach a dated window to the wrong event, since two events can share a festival id.</summary>
    private static void ValidateOccurrences(IReadOnlyList<EventOccurrence> occurrences)
    {
        var seen = new HashSet<(uint Genre, uint Festival)>();
        foreach (var occ in occurrences)
        {
            if (occ.Genre == 0)
                throw new InvalidOperationException($"the hand file's {occ.Event} {occ.Year} occurrence has no genre (festival {occ.Festival})");
            if (occ.Festival == 0)
                throw new InvalidOperationException($"the hand file's {occ.Event} {occ.Year} occurrence has festival 0; a permanent occurrence cannot be dated or rewarded");
            if (!seen.Add((occ.Genre, occ.Festival)))
                throw new InvalidOperationException($"the hand file lists {occ.Event} {occ.Year} twice (genre {occ.Genre}, festival {occ.Festival})");
            var rewards = new HashSet<RewardSignal>();
            foreach (var reward in occ.RewardList)
            {
                if (reward.Id == 0)
                    throw new InvalidOperationException($"the hand file's {occ.Event} {occ.Year} occurrence lists a {reward.Kind} with id 0");
                if (!rewards.Add(reward))
                    throw new InvalidOperationException($"the hand file's {occ.Event} {occ.Year} occurrence lists {reward.Kind.ToString().ToLowerInvariant()} {reward.Id} twice");
            }
        }
    }

    /// <summary>A quest id listed twice would build two catalog items under one id, and a category id listed twice would
    /// throw from the builder's own lookup. Both cost the whole catalog, so name the offending line here: this file is
    /// hand-edited after every patch.</summary>
    private static void ValidateUnlocksAndKinds(IReadOnlyList<FeatureUnlock> unlocks, IReadOnlyList<ChroniclesKind> kinds)
    {
        var quests = new HashSet<uint>();
        foreach (var unlock in unlocks)
        {
            if (!quests.Add(unlock.QuestId))
                throw new InvalidOperationException($"the hand file lists feature unlock quest {unlock.QuestId} ({unlock.Feature}) twice");
        }
        var categories = new HashSet<uint>();
        foreach (var kind in kinds)
        {
            if (!categories.Add(kind.CategoryId))
                throw new InvalidOperationException($"the hand file lists Chronicles category {kind.CategoryId} ({kind.Kind}) twice");
        }
    }

    /// <summary>A cap of zero or less would make its counter read "0 of 0", which the assembler treats as done at cap, and
    /// a daily that is permanently done is a daily the player never sees again. This file is hand-edited after every patch, so
    /// name the offending key.</summary>
    private static void ValidateCaps(ResetCaps caps)
    {
        foreach (var (name, value) in new[]
                 {
                     ("levequestAllowances", caps.LevequestAllowances),
                     ("alliedSocietyAllowances", caps.AlliedSocietyAllowances),
                     ("wondrousTailsStickers", caps.WondrousTailsStickers),
                     ("wondrousTailsSecondChance", caps.WondrousTailsSecondChance),
                     ("miniCactpotTickets", caps.MiniCactpotTickets), ("jumboCactpotTickets", caps.JumboCactpotTickets),
                 })
        {
            if (value <= 0) throw new InvalidOperationException($"the hand file's resetCaps.{name} is {value}; every cap must be above zero");
        }
    }

    /// <summary>A cap above the global one is a typo, a cap at or below zero hides the job's bar, and the same job twice
    /// means two answers to one question. Name the job id: this file is hand-edited after every patch.</summary>
    private static void ValidateJobCaps(int levelCap, IReadOnlyList<LimitedJobCap> caps)
    {
        var seen = new HashSet<uint>();
        foreach (var cap in caps)
        {
            if (!seen.Add(cap.JobId)) throw new InvalidOperationException($"the hand file lists a cap for class job {cap.JobId} twice");
            if (cap.Cap <= 0 || cap.Cap > levelCap)
                throw new InvalidOperationException($"the hand file's cap for class job {cap.JobId} is {cap.Cap}; it must be between 1 and the level cap of {levelCap}");
        }
    }

    /// <summary>Each roulette's unlock rule, read line by line so every message names the roulette: the rule must be one of
    /// the five kinds, a roulette may appear once, a member count must be at least one, and a completed rule must list the
    /// duties it means. A roulette with no line here is read ungated and the builder says so.</summary>
    private static List<RouletteGate> ParseRouletteGates(IReadOnlyList<RouletteGateEntry> entries)
    {
        var seen = new HashSet<uint>();
        var gates = new List<RouletteGate>(entries.Count);
        foreach (var entry in entries)
        {
            var rule = entry.Rule switch
            {
                "unlockedAtLeast" => RouletteRule.UnlockedAtLeast,
                "unlockedAll" => RouletteRule.UnlockedAll,
                "completedAll" => RouletteRule.CompletedAll,
                "completed" => RouletteRule.Completed,
                "battleMentor" => RouletteRule.BattleMentor,
                _ => throw new InvalidOperationException($"the hand file's gate for roulette {entry.RouletteId} has an unknown rule '{entry.Rule}'"),
            };
            if (!seen.Add(entry.RouletteId))
                throw new InvalidOperationException($"the hand file lists a gate for roulette {entry.RouletteId} twice");
            if (rule == RouletteRule.UnlockedAtLeast && entry.Count is not >= 1)
                throw new InvalidOperationException($"the hand file's gate for roulette {entry.RouletteId} needs a count of at least 1: how many members must be unlocked");
            if (rule == RouletteRule.Completed && entry.ContentIds is not { Count: > 0 })
                throw new InvalidOperationException($"the hand file's gate for roulette {entry.RouletteId} says completed but lists no contentIds");
            gates.Add(new RouletteGate(entry.RouletteId, rule, entry.Count ?? 0, entry.ContentIds ?? []));
        }
        return gates;
    }

    /// <summary>An empty levequest list would leave the allowance row locked for every character, and a gather level outside
    /// 1 to the cap is a typo. Name the key: this file is hand-edited after every patch.</summary>
    private static void ValidateUnlockGates(IReadOnlyList<uint> levequestUnlockQuests, int treasureMapGatherLevel, int levelCap)
    {
        if (levequestUnlockQuests.Count == 0)
            throw new InvalidOperationException("the hand file's levequestUnlockQuests list is empty; the levequest allowance would never read");
        if (treasureMapGatherLevel < 1 || treasureMapGatherLevel > levelCap)
            throw new InvalidOperationException($"the hand file's treasureMapGatherLevel is {treasureMapGatherLevel}; it must be between 1 and the level cap of {levelCap}");
    }

    /// <summary>Every gate quest is a Quest row, and no Quest row has an id below 65536, so a smaller number is a typo
    /// or an unlock link written where a quest belongs. Name the key: this file is hand-edited after every patch.</summary>
    private static void ValidateUnlockQuests(UnlockQuests quests)
    {
        foreach (var (name, id) in new[]
                 {
                     ("challengeLog", quests.ChallengeLog), ("fashionReport", quests.FashionReport), ("domanEnclave", quests.DomanEnclave),
                     ("maskedCarnivale", quests.MaskedCarnivale), ("miniCactpot", quests.MiniCactpot), ("jumboCactpot", quests.JumboCactpot),
                 })
        {
            if (id < 65536) throw new InvalidOperationException($"the hand file's unlockQuests.{name} is {id}; a quest row id is 65536 or above");
        }
    }

    /// <summary>Zero attempts would make every rule read done at once and a zero reward score would make the default rule
    /// always true.</summary>
    private static void ValidateFashionReport(FashionReportRules rules)
    {
        if (rules.Attempts <= 0) throw new InvalidOperationException($"the hand file's fashionReport.attempts is {rules.Attempts}; it must be above zero");
        if (rules.RewardScore <= 0) throw new InvalidOperationException($"the hand file's fashionReport.rewardScore is {rules.RewardScore}; it must be above zero");
    }

    /// <summary>A trove is keyed by its tomestone, so an item of zero or a repeated item would build no row or two rows under
    /// one id, and a window that ends before it starts is a typo in a date. Name the item: this section is edited every trove.</summary>
    private static void ValidateTroves(IReadOnlyList<Trove> troves)
    {
        var seen = new HashSet<uint>();
        foreach (var trove in troves)
        {
            if (trove.ItemId == 0) throw new InvalidOperationException("the hand file lists a trove with item 0");
            if (!seen.Add(trove.ItemId)) throw new InvalidOperationException($"the hand file lists trove item {trove.ItemId} twice");
            if (trove.End <= trove.Start) throw new InvalidOperationException($"the hand file's trove {trove.ItemId} ends at {trove.End:u}, before it starts at {trove.Start:u}");
        }
    }

    /// <summary>The Gold Saucer's quest and the PvP quests are Quest rows, so an id below 65536 is a typo, and an empty
    /// PvP list would lock the PvP rows on every character. Name the key: this file is hand-edited after every patch.</summary>
    private static void ValidateContentUnlockQuests(ContentUnlockQuests quests)
    {
        if (quests.GoldSaucer < 65536) throw new InvalidOperationException($"the hand file's contentUnlockQuests.goldSaucer is {quests.GoldSaucer}; a quest row id is 65536 or above");
        // The list is null when the key is absent, since the record's parameter is filled by the deserializer: name the key
        // either way rather than let a NullReferenceException name nothing.
        if (quests.Pvp is not { Count: > 0 }) throw new InvalidOperationException("the hand file's contentUnlockQuests.pvp list is missing or empty; the PvP rows would never read");
        foreach (var id in quests.Pvp)
        {
            if (id < 65536) throw new InvalidOperationException($"the hand file's contentUnlockQuests.pvp lists {id}; a quest row id is 65536 or above");
        }
    }

    /// <summary>Each Variant series names the InstanceContent row of its own dungeon, the gate the reader asks; a series
    /// or content of zero and a series listed twice would gate nothing or gate it twice. Name the series: the fourth pair is taken from the
    /// order of the series, not yet confirmed in game.</summary>
    private static void ValidateVariantSeries(IReadOnlyList<VariantSeriesGate> series)
    {
        var seen = new HashSet<uint>();
        foreach (var gate in series)
        {
            if (gate.SeriesId == 0) throw new InvalidOperationException("the hand file's variantSeries lists series 0");
            if (gate.ContentId == 0) throw new InvalidOperationException($"the hand file's variantSeries entry for series {gate.SeriesId} names content 0");
            if (!seen.Add(gate.SeriesId)) throw new InvalidOperationException($"the hand file lists variantSeries series {gate.SeriesId} twice");
        }
    }

    /// <summary>Every place quest is a Quest row, so an id below 65536 is a typo; an empty deep-dungeon list would read every
    /// dungeon ungated, and a dungeon listed twice would gate it twice. Name the key: this file is hand-edited after every patch.</summary>
    private static void ValidatePlaceUnlockQuests(PlaceUnlockQuests quests)
    {
        foreach (var (name, id) in new[]
                 {
                     ("eureka", quests.Eureka), ("bozja", quests.Bozja), ("occultCrescent", quests.OccultCrescent),
                     ("cosmicExploration", quests.CosmicExploration), ("islandSanctuary", quests.IslandSanctuary),
                 })
        {
            if (id < 65536) throw new InvalidOperationException($"the hand file's placeUnlockQuests.{name} is {id}; a quest row id is 65536 or above");
        }
        // The list is null when the key is absent, since the record's parameter is filled by the deserializer: name the key.
        if (quests.DeepDungeons is not { Count: > 0 }) throw new InvalidOperationException("the hand file's placeUnlockQuests.deepDungeons list is missing or empty; every deep dungeon would read ungated");
        var seen = new HashSet<uint>();
        foreach (var gate in quests.DeepDungeons)
        {
            if (gate.DungeonId == 0) throw new InvalidOperationException("the hand file's placeUnlockQuests.deepDungeons lists dungeon 0");
            if (gate.QuestId < 65536) throw new InvalidOperationException($"the hand file's placeUnlockQuests.deepDungeons entry for dungeon {gate.DungeonId} names quest {gate.QuestId}; a quest row id is 65536 or above");
            if (!seen.Add(gate.DungeonId)) throw new InvalidOperationException($"the hand file lists placeUnlockQuests.deepDungeons dungeon {gate.DungeonId} twice");
        }
    }

    /// <summary>A maximum of zero or less would make a Level read done at any value. Name the key.</summary>
    private static void ValidateContentMaxima(ContentMaxima maxima)
    {
        foreach (var (name, value) in new[]
                 {
                     ("elementalLevel", maxima.ElementalLevel), ("resistanceRank", maxima.ResistanceRank), ("knowledgeLevel", maxima.KnowledgeLevel),
                     ("aetherpool", maxima.Aetherpool), ("racingRank", maxima.RacingRank),
                 })
        {
            if (value <= 0) throw new InvalidOperationException($"the hand file's contentMaxima.{name} is {value}; every maximum must be above zero");
        }
    }

    /// <summary>The warning must fall inside the demolition period, or the reminder would fire at the stamp or never.</summary>
    private static void ValidateHouseVisit(HouseVisitRules rules)
    {
        if (rules.DemolitionDays <= 0) throw new InvalidOperationException($"the hand file's houseVisit.demolitionDays is {rules.DemolitionDays}; it must be above zero");
        if (rules.WarningDays <= 0 || rules.WarningDays >= rules.DemolitionDays)
            throw new InvalidOperationException($"the hand file's houseVisit.warningDays is {rules.WarningDays}; it must be above zero and below demolitionDays ({rules.DemolitionDays})");
    }

    /// <summary>Every collection gate is a Quest row, so an id below 65536 is a typo, and an empty barding list would leave
    /// the barding row never read. Name the key: this file is hand-edited after every patch.</summary>
    private static void ValidateCollectionUnlockQuests(CollectionUnlockQuests quests)
    {
        // The list is null when the key is absent, since the record's parameter is filled by the deserializer: name the key.
        if (quests.Bardings is not { Count: > 0 }) throw new InvalidOperationException("the hand file's collectionUnlockQuests.bardings list is missing or empty; the barding row would never read");
        foreach (var id in quests.Bardings)
        {
            if (id < 65536) throw new InvalidOperationException($"the hand file's collectionUnlockQuests.bardings lists {id}; a quest row id is 65536 or above");
        }
        foreach (var (name, id) in new[] { ("tripleTriad", quests.TripleTriad), ("sightseeingHeavensward", quests.SightseeingHeavensward) })
        {
            if (id < 65536) throw new InvalidOperationException($"the hand file's collectionUnlockQuests.{name} is {id}; a quest row id is 65536 or above");
        }
    }

    /// <summary>A capacity of zero or less would put every storage row over nothing, the saddlebag's gate is a Quest row (65536 or above),
    /// and the dresser's gate must name a GeneralAction row. Name the key: this file is hand-edited after every patch.</summary>
    private static void ValidateStorage(StorageRules rules)
    {
        foreach (var (name, value) in new[]
                 {
                     ("retainerInventory", rules.RetainerInventory), ("saddlebag", rules.Saddlebag), ("premiumSaddlebag", rules.PremiumSaddlebag),
                     ("glamourDresser", rules.GlamourDresser),
                 })
        {
            if (value <= 0) throw new InvalidOperationException($"the hand file's storage.{name} is {value}; every capacity must be above zero");
        }
        if (rules.SaddlebagQuest < 65536) throw new InvalidOperationException($"the hand file's storage.saddlebagQuest is {rules.SaddlebagQuest}; a quest row id is 65536 or above");
        if (rules.CastGlamourAction == 0) throw new InvalidOperationException("the hand file's storage.castGlamourAction is 0; it must name the GeneralAction row of Cast Glamour");
    }

    private sealed class HandFile
    {
        public List<EventOccurrence>? EventOccurrences { get; set; }
        public List<FeatureUnlock>? FeatureUnlocks { get; set; }
        public List<ChroniclesKind>? ChroniclesKinds { get; set; }
        public int? LevelCap { get; set; }
        public List<LimitedJobCap>? LimitedJobCaps { get; set; }
        public ResetCaps? ResetCaps { get; set; }
        public List<RouletteGateEntry>? RouletteGates { get; set; }
        public List<uint>? LevequestUnlockQuests { get; set; }
        public int? TreasureMapGatherLevel { get; set; }
        public UnlockQuests? UnlockQuests { get; set; }
        public FashionReportRules? FashionReport { get; set; }
        public List<Trove>? Troves { get; set; }
        public ContentUnlockQuests? ContentUnlockQuests { get; set; }
        public List<VariantSeriesGate>? VariantSeries { get; set; }
        public PlaceUnlockQuests? PlaceUnlockQuests { get; set; }
        public ContentMaxima? ContentMaxima { get; set; }
        public HouseVisitRules? HouseVisit { get; set; }
        public CollectionUnlockQuests? CollectionUnlockQuests { get; set; }
        public StorageRules? Storage { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    /// <summary>A roulette gate as written, with the rule still a string so an unknown one can be reported by roulette
    /// rather than by the enum converter's JSON path.</summary>
    private sealed class RouletteGateEntry
    {
        public uint RouletteId { get; set; }
        public string? Rule { get; set; }
        public int? Count { get; set; }
        public List<uint>? ContentIds { get; set; }
    }
}

/// <summary>How a roulette's own unlock is judged, because the client offers no per-roulette read that is not a
/// load. The member rules count over the roulette's RouletteRow.MemberContentIds through the client's per-duty unlock and
/// completion bits; Completed names its own duties instead; BattleMentor asks the player state.</summary>
public enum RouletteRule { UnlockedAtLeast, UnlockedAll, CompletedAll, Completed, BattleMentor }

/// <summary>One roulette's unlock rule from the hand file. Count is read only by UnlockedAtLeast; ContentIds only by
/// Completed, as InstanceContent row ids. Rows 7, 15 and 17 are taken to open at one member unlocked; not yet confirmed in game.</summary>
public sealed record RouletteGate(uint RouletteId, RouletteRule Rule, int Count = 0, IReadOnlyList<uint>? ContentIds = null);

/// <summary>One dated run of a seasonal or collaboration event: the journal genre and festival row its quests carry, its
/// year, its window in UTC, whether its returns reuse one set of quest rows, and for a reward event the signals that
/// define its reward set.</summary>
public sealed record EventOccurrence(uint Genre, uint Festival, string Event, int Year, DateTimeOffset Start, DateTimeOffset End, bool Reused = false, IReadOnlyList<RewardSignal>? Rewards = null)
{
    public IReadOnlyList<RewardSignal> RewardList => Rewards ?? [];
    /// <summary>An achievement is the one signal the client loads only after a visit to the Achievements window.</summary>
    public bool HasAchievement => RewardList.Any(r => r.Kind == RewardKind.Achievement);
}

/// <summary>What the plugin can read a reward as, each through one IUnlockState member: IsAchievementComplete,
/// IsMountUnlocked, IsCompanionUnlocked, IsEmoteUnlocked, IsOrchestrionUnlocked, IsTripleTriadCardUnlocked, IsItemUnlocked
/// (unlockable items only). A title would need the Titles list loaded like an achievement needs the Achievements window,
/// and no reward event grants one, so it is not a kind.</summary>
public enum RewardKind { Achievement, Mount, Minion, Emote, Orchestrion, Card, Item }

/// <summary>One reward of a reward event: its kind and the row id in that kind's sheet.</summary>
public sealed record RewardSignal(RewardKind Kind, uint Id);

/// <summary>A quest that unlocks a feature; the sheets carry no flag for these.</summary>
public sealed record FeatureUnlock(uint QuestId, string Feature);

/// <summary>Which kind of content a Chronicles of a New Era category leads to.</summary>
public sealed record ChroniclesKind(uint CategoryId, string Kind);

/// <summary>The caps that neither a sheet nor the client states, so they are hand-file facts with a source.
/// The custom-delivery caps are deliberately absent: the client's own GetRemainingAllowances
/// is literally "12 minus used" and SatisfactionNpc.DeliveriesPerWeek gives the per-client six, so both come from the game.
/// The cactpot caps are the observed rows' ceilings: three tickets a day and three a week.</summary>
public sealed record ResetCaps(int LevequestAllowances, int AlliedSocietyAllowances, int WondrousTailsStickers, int WondrousTailsSecondChance, int MiniCactpotTickets, int JumboCactpotTickets);

/// <summary>The quest that unlocks each row, read through IUnlockState.IsQuestCompleted at read time. None sits in a
/// journal genre the reducer keeps, so each is a hand-file id resolved through the Quest sheet, the way the levequest unlock
/// quests are.</summary>
public sealed record UnlockQuests(uint ChallengeLog, uint FashionReport, uint DomanEnclave, uint MaskedCarnivale, uint MiniCactpot, uint JumboCactpot);

/// <summary>The Fashion Report's numbers: up to four attempts a week, and a rating of 80 or more earns the second reward.
/// Which of them makes the row done is FashionReport.Rule in Core: the reward score.</summary>
public sealed record FashionReportRules(int Attempts, int RewardScore);

/// <summary>One Moogle Treasure Trove: keyed by its irregular tomestone's item id, with the window in which the tomestones can
/// be earned, in UTC. Spending stays open until the next patch, which has no date, so no exchange end is carried and
/// the row reads "ended", never "missed", after the window.</summary>
public sealed record Trove(uint ItemId, DateTimeOffset Start, DateTimeOffset End);

/// <summary>The quests that unlock the Content rows on a quest the reducer's kept genres do not carry: the Gold
/// Saucer's "It Could Happen to You" (65970) for MGP, and the three "A Pup No Longer" variants (66640 to 66642), any one of
/// which opens PvP. Read through IUnlockState.IsQuestCompleted at read time.</summary>
public sealed record ContentUnlockQuests(uint GoldSaucer, IReadOnlyList<uint> Pvp);

/// <summary>One Variant record series to the InstanceContent row of its dungeon, the gate its Counter is read behind: no sheet
/// reachable through Lumina links the two (InstanceContent.ContentRoute holds 1, 4, 7 and 10, not the series ids). The first
/// three pairs match by name; Corvosi to The Merchant's Tale is taken from the order of the series, not yet confirmed in game.</summary>
public sealed record VariantSeriesGate(uint SeriesId, uint ContentId);

/// <summary>The quests that open the place rows: Eureka's "And We Shall Call It Eureka" (68614), Bozja's
/// "Where Eagles Nest" (69477), the Occult Crescent's "Unfamiliar Territory" (70847, the end of 70845 → 70846 → 70847), Cosmic
/// Exploration's "A Cosmic Homecoming" (70789), Island Sanctuary's "Seeking Sanctuary" (70179), and one per deep dungeon. Read
/// through IUnlockState.IsQuestCompleted at read time.</summary>
public sealed record PlaceUnlockQuests(uint Eureka, uint Bozja, uint OccultCrescent, uint CosmicExploration, uint IslandSanctuary, IReadOnlyList<DeepDungeonGate> DeepDungeons);

/// <summary>One deep dungeon (a DeepDungeon row) to the quest that unlocks it.</summary>
public sealed record DeepDungeonGate(uint DungeonId, uint QuestId);

/// <summary>The maxima no sheet states: elemental level 60, resistance rank 25, knowledge level 40 since 7.55,
/// aetherpool +99 and racing rank 50.</summary>
public sealed record ContentMaxima(int ElementalLevel, int ResistanceRank, int KnowledgeLevel, int Aetherpool, int RacingRank);

/// <summary>The personal estate's demolition rule: demolished after DemolitionDays without a visit from its owner, with the
/// game's own warning at WarningDays. RemindWithin is how long before the deadline the warning falls, the house-visit reminder's
/// window.</summary>
public sealed record HouseVisitRules(int DemolitionDays, int WarningDays)
{
    public TimeSpan RemindWithin => TimeSpan.FromDays(DemolitionDays - WarningDays);
}

/// <summary>The quests that open the collection rows the sheets do not gate: "My Little Chocobo" for the Twin Adder
/// (66236), the Maelstrom (66237) and the Immortal Flames (66238), any one of which opens the barding row; "Triple Triad Trial"
/// (65973) the card row; "Sights of the North" (67643) Heavensward's vistas. A Realm Reborn's vistas are gated on the player
/// state's own sightseeing byte and the later expansions' on AdventureExPhase's quests, so neither is here. Read through
/// IUnlockState.IsQuestCompleted at read time.</summary>
public sealed record CollectionUnlockQuests(IReadOnlyList<uint> Bardings, uint TripleTriad, uint SightseeingHeavensward);

/// <summary>The storage facts no sheet states: retainer inventory 175 slots, each saddlebag tab 70, the glamour
/// dresser 800; the saddlebag's unlock quest, "My Feisty Little Chocobo" (66698); and the Cast Glamour action, GeneralAction row 22, the
/// dresser's gate, read through IUnlockState.IsGeneralActionUnlocked at read time.</summary>
public sealed record StorageRules(int RetainerInventory, int Saddlebag, int PremiumSaddlebag, int GlamourDresser, uint SaddlebagQuest, uint CastGlamourAction);

/// <summary>One class or job whose maximum level is not the global one. No sheet column and no client member states a
/// per-job maximum, so this is a hand-file fact with a source, for Blue Mage;
/// Beastmaster is taken to be the same 80, not yet confirmed in game.</summary>
public sealed record LimitedJobCap(uint JobId, int Cap);
