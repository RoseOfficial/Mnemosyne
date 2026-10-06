namespace Mnemosyne.Core.Definitions;

/// <summary>The ids the Collections rows are built under and read by, the names the reducer groups its rows
/// by, and the MainCommand rows that name them, in one place, so the reducer, the builder and the readers cannot drift apart, as
/// JournalIds serves the quest rows.</summary>
public static class CollectionIds
{
    /// <summary>The eleven collection kinds, in the Collection group's order; each is the tail of its row's id.</summary>
    public const string Mount = "mount", Minion = "minion", Emote = "emote", Barding = "barding", Hairstyle = "hairstyle", Facewear = "facewear",
        Ornament = "ornament", Orchestrion = "orchestrion", Card = "card", FramerKit = "framerkit", Title = "title";

    public static readonly IReadOnlyList<string> Kinds = [Mount, Minion, Emote, Barding, Hairstyle, Facewear, Ornament, Orchestrion, Card, FramerKit, Title];

    /// <summary>The four logs the client keeps its own completion bits for: crafting and gathering per class,
    /// fishing and spearfishing for Fisher.</summary>
    public const string Craft = "craft", Gather = "gather", Fish = "fish", Spearfish = "spearfish";

    /// <summary>The classes the logs belong to and are gated on: Carpenter to Culinarian at 8 to 15 (8 +
    /// Recipe.CraftType), Miner 16, Botanist 17, Fisher 18.</summary>
    public const uint FirstCrafter = 8, LastCrafter = 15, Miner = 16, Botanist = 17, Fisher = 18;

    /// <summary>The client's completion array covers the recipe rows below 30000 only.</summary>
    public const uint RecipeLogEnd = 30000;

    /// <summary>The MainCommand rows that name the Collections groups and six of the kinds in the client's
    /// language: Collection 87, Crafting Log 9, Gathering Log 7, Fishing Log 29, Sightseeing Log 64; Mount Guide 61, Minion Guide
    /// 62, Emotes 17, Facewear 95, Fashion Accessories 89 (the ornaments) and Orchestrion List 69.</summary>
    public const uint CollectionCommand = 87, CraftingLogCommand = 9, GatheringLogCommand = 7, FishingLogCommand = 29, SightseeingLogCommand = 64,
        MountCommand = 61, MinionCommand = 62, EmoteCommand = 17, FacewearCommand = 95, OrnamentCommand = 89, OrchestrionCommand = 69;

    public static readonly IReadOnlyList<uint> Commands =
        [CollectionCommand, CraftingLogCommand, GatheringLogCommand, FishingLogCommand, SightseeingLogCommand,
         MountCommand, MinionCommand, EmoteCommand, FacewearCommand, OrnamentCommand, OrchestrionCommand];

    /// <summary>The id families: collection:&lt;kind&gt;, log:craft:&lt;class&gt;, log:gather:&lt;class&gt;, log:fish,
    /// log:spearfish, log:sightseeing:&lt;ExVersion row&gt; and achievement:category:&lt;AchievementCategory row&gt;.</summary>
    public const string KindPrefix = "collection:", LogPrefix = "log:", SightseeingPrefix = "log:sightseeing:", AchievementPrefix = "achievement:category:";

    public static string KindId(string kind) => KindPrefix + kind;

    public static string LogId(string log, uint classJobId) => log is Craft or Gather ? $"{LogPrefix}{log}:{classJobId}" : LogPrefix + log;

    public static string SightseeingId(uint exVersion) => SightseeingPrefix + exVersion;

    public static string AchievementId(uint category) => AchievementPrefix + category;
}
