using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Definitions;

/// <summary>Rows plus the hand file become the catalog. Group orders are the sheets' own orders (journal category ids
/// for MSQ, genre ids for the rest), offset per block so the Story section reads MSQ, job quests, role quests, Hildibrand,
/// Chronicles, storylines, unlocks, aether currents, societies. A dated event occurrence with reward signals is built as a
/// Counter of its rewards. The Resets section reads daily groups first, then weekly, then allowances and timers. Every
/// row id scheme here is parsed by the readers. The Moogle Treasure Trove is a held
/// Counter in Events, one per trove. The Content section reads housing, PvP, the Gold Saucer and the hunting log
/// first, then every duty in the Duty Finder's own headings and orders. It also adds the place rows (the field levels, the
/// phantom jobs, the Cosmic tools, the Island Sanctuary rank, the aetherpool), the racing rank and the house-visit timer.
/// The Collections section holds one Counter per collection kind, log class, sightseeing expansion and achievement
/// category, and the Crystalline Conflict tier and the Grand Company PvP ranks sit under Content. The Money
/// section holds 92 rows: the currencies the player can hold, three rows per retainer slot and the storage rows. The company
/// seals sit there rather than under Jobs. Each job row carries its job's icon and each currency row its item's icon
/// (IconId).</summary>
public static class CatalogBuilder
{
    /// <summary>An event occurrence the hand file does not date and whose festival id is not newer than its event's last dated
    /// occurrence: rendered as past by the assembler, never counted.</summary>
    public static readonly Availability PastWindow = new(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2000, 1, 2, 0, 0, 0, TimeSpan.Zero));

    /// <summary>An event occurrence the hand file does not date but whose festival id is newer than its event's last dated
    /// occurrence: the sheets can carry a run before it is announced, so it is rendered as upcoming.</summary>
    public static readonly Availability UpcomingWindow = new(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2100, 1, 2, 0, 0, 0, TimeSpan.Zero));

    private const string ChroniclesPrefix = "Chronicles of a New Era - ";

    /// <summary>Named "Rising Events" in the sheets; the event itself is "The Rising".</summary>
    private const uint RisingGenre = 247;

    /// <summary>MobHuntOrderType.Type: 1 is a daily bill, 2 a weekly elite bill, with no exceptions in the sheet.</summary>
    private const int DailyHuntBill = 1, WeeklyHuntBill = 2;

    /// <summary>Three levequest allowances arrive at each 00:00 and 12:00 UTC boundary, to a cap the hand file carries.</summary>
    private const int LevequestGrant = 3;

    /// <summary>The retainer roster is ten slots. The catalog is global while the roster is per character, so the catalog
    /// carries ten venture items and a character with two retainers simply has no value in the other eight. The id scheme,
    /// a sorted roster index, is the client's own; the ten catalog slots are a deliberate departure from that: a record,
    /// not a catalog row count.</summary>
    private const int VentureSlots = 10;

    /// <summary>The Wondrous Tails journal's expiry timer. Named here because the reminder looks for exactly
    /// this item and nothing else should have to restate the id.</summary>
    public const string WondrousTailsJournalId = "reset:bingo:expiry";

    /// <summary>The character's current Grand Company, which the Jobs reader
    /// stores through Readings.CurrentCompany and the Company seals column reads through Holdings.CurrentCompany.</summary>
    public const string CurrentCompanyId = "gc:current";

    /// <summary>The trove sits after the twelve event genres in the Events section. Group orders
    /// there are the genres' 1-based ranks, so 100 leaves room for every genre a patch could add.</summary>
    private const int TroveGroupOrder = 100;

    /// <summary>The window names the freshness markers show ("needs: …", "as of last visit to …"), one per gated window.
    /// The window readers key on the same strings' items by id prefix, never on these names.</summary>
    public const string GrandCompanyWindow = "Grand Company supply", ChallengeLogWindow = "Challenge Log", FashionReportWindow = "Fashion Report",
        CarnivaleWindow = "Masked Carnivale briefing", EnclaveWindow = "Doman Enclave", DutyFinderWindow = "Duty Finder";

    /// <summary>Judging opens 72 hours after the Tuesday 08:00 UTC weekly reset, Friday 08:00, and closes at the next reset,
    /// 168 hours after. Both Pacific variants of the Lodestone's times convert to the same
    /// UTC instants, so daylight saving moves nothing.</summary>
    public static readonly Recurrence FashionReportRecurrence = new(TimeSpan.FromDays(3), TimeSpan.FromDays(7));

    /// <summary>The Masked Carnivale's three weekly targets by AozWeeklyChallenge value, never by flag bit. Hand
    /// names: no sheet holds the three.</summary>
    private static readonly (int Challenge, string Name)[] CarnivaleTargets = [(0, "Novice target"), (1, "Moderate target"), (2, "Advanced target")];

    /// <summary>The Content category's hand-named groups and their orders, ahead of the sheet-named field and duty groups:
    /// Housing 100, PvP 200, Gold Saucer 300, Hunting log 400. The field-zone duty groups take 500 to 520 from their ContentType
    /// and the Duty Finder groups 1000 and up (DutyBands).</summary>
    public const string HousingGroup = "Housing", PvpGroup = "PvP", GoldSaucerGroup = "Gold Saucer", HuntingLogGroup = "Hunting log";
    private const int HousingGroupOrder = 100, PvpGroupOrder = 200, GoldSaucerGroupOrder = 300, HuntingLogGroupOrder = 400;

    /// <summary>MGP is item 29, one of the 77 currencies Money owns, so its id is currency:29 and the Money section skips
    /// it rather than building it twice.</summary>
    public const uint MgpItemId = 29;

    /// <summary>The ClassJob sheet has no icon column (Lumina's ClassJob), so a job's icon is the game's own framed job icon,
    /// numbered 62100 plus the ClassJob row: ui/icon/062000/0621NN_hr1.tex exists for
    /// every ClassJob row 1 to 43 in the game files, 64 by 64, the job's glyph on a frame in its role's colour; 62000 plus the row is the same glyph
    /// unframed.</summary>
    public const uint JobIconBase = 62100;

    /// <summary>The ids of the place rows, the racing rank and the house-visit timer, named here because their
    /// readers and the reminder key on exactly these; a reader keys on the id, never on a display name.</summary>
    public const string EurekaLevelId = "field:eureka", BozjaRankId = "field:bozja", OccultKnowledgeId = "field:occult", IslandRankId = "field:island",
        PhantomJobPrefix = "field:phantom:", CosmicToolPrefix = "field:cosmic:", DeepDungeonPrefix = "deepdungeon:",
        RacingId = "goldsaucer:racing", HouseVisitId = "timer:housing";

    /// <summary>The place and the window the markers name for the house-visit timer and the racing rank: "visit: your house",
    /// "needs: Gold Saucer window".</summary>
    public const string YourHouse = "your house", GoldSaucerWindow = "Gold Saucer window";

    /// <summary>Cosmic Exploration has no ContentType row, so its group is a hand name; it, Island Sanctuary and
    /// Deep Dungeons follow the three field groups.</summary>
    public const string CosmicGroup = "Cosmic Exploration";
    private const int CosmicGroupOrder = 530, IslandGroupOrder = 540, DeepDungeonGroupOrder = 550;

    /// <summary>The windows the Collections markers name ("needs: Achievements", "needs: Titles"). Achievements is the gate string the
    /// reward events have always used; the titles' window is "Titles".</summary>
    public const string AchievementsWindow = "Achievements", TitlesWindow = "Titles";

    /// <summary>The two PvP rows added to the Content PvP group, the Crystalline Conflict tier at order 3
    /// and one Grand Company PvP rank per company at 10 plus its row; a reader keys on the id, never on a display name.</summary>
    public const string CrystallineConflictId = "pvp:cc", PvpRankPrefix = "pvp:rank:";
    private const int CrystallineConflictOrder = 3, PvpRankOrderBase = 10;

    /// <summary>The Collections section's group orders: the Collection group, then the four logs, then one group
    /// per achievement kind at 300 plus ten times the kind's own order (Battle 310 to Grand Company 380).</summary>
    private const int CollectionGroupOrder = 100, CraftingLogGroupOrder = 200, GatheringLogGroupOrder = 210, FishingLogGroupOrder = 220,
        SightseeingLogGroupOrder = 230, AchievementGroupOrderBase = 300;

    /// <summary>The six kinds a MainCommand row names in the client's language.</summary>
    private static readonly Dictionary<string, uint> KindCommands = new()
    {
        [CollectionIds.Mount] = CollectionIds.MountCommand, [CollectionIds.Minion] = CollectionIds.MinionCommand, [CollectionIds.Emote] = CollectionIds.EmoteCommand,
        [CollectionIds.Facewear] = CollectionIds.FacewearCommand, [CollectionIds.Ornament] = CollectionIds.OrnamentCommand, [CollectionIds.Orchestrion] = CollectionIds.OrchestrionCommand,
    };

    /// <summary>No MainCommand row names bardings, hairstyles, cards, framer's kits or titles, so these five are hand
    /// names, as the PvP rows' "Series level" is.</summary>
    private static readonly Dictionary<string, string> KindNames = new()
    {
        [CollectionIds.Barding] = "Bardings", [CollectionIds.Hairstyle] = "Hairstyles", [CollectionIds.Card] = "Triple Triad cards",
        [CollectionIds.FramerKit] = "Framer's kits", [CollectionIds.Title] = "Titles",
    };

    /// <summary>The fishing log's two halves: no sheet names them apart, so they are hand names, in the log's own order.</summary>
    private static readonly Dictionary<string, (string Name, int Order)> FisherLogs = new()
    {
        [CollectionIds.Fish] = ("Fishing", 0), [CollectionIds.Spearfish] = ("Spearfishing", 1),
    };

    /// <summary>The Money section's group orders: the currencies, the tomestones, the society currencies, the retainers, then
    /// storage.</summary>
    private const int CurrencyGroupOrder = 100, TomestoneGroupOrder = 110, SocietalGroupOrder = 120, RetainerGroupOrder = 200, StorageGroupOrder = 300;

    /// <summary>The ContentType rows that name the place groups: Eureka 26, Save the Queen 29 and Occult Crescent
    /// 38, whose field duties already sit in them, and Deep Dungeons 21. Island Sanctuary is row 36 (the game's
    /// ContentType sheet), which carries a hand name: the sheet has one, so the client's language is respected.</summary>
    private const uint EurekaType = 26, SaveTheQueenType = 29, OccultType = 38, DeepDungeonType = 21, IslandType = 36;

    /// <summary>A phantom job's order is 10 plus its JobIndex, after the knowledge level at 0 and before the field
    /// duties, whose row ids are in the thousands.</summary>
    private const int PhantomOrderBase = 10;

    /// <summary>The three character-wide field levels, named by hand because no sheet names them, each in the group
    /// of its ContentType.</summary>
    private static readonly (string Id, uint Type, string Name)[] FieldLevels =
        [(EurekaLevelId, EurekaType, "Elemental level"), (BozjaRankId, SaveTheQueenType, "Resistance rank"), (OccultKnowledgeId, OccultType, "Knowledge level")];

    /// <summary>The Content category's duty groups follow the Duty Finder. Band by ContentType, then ten
    /// per expansion, then the rank of the heading's row id within its type and expansion, which puts Normal before Savage before
    /// Alliance for every raid expansion and Trials before High-end Trials for every trial expansion. Ordering by the lowest
    /// SortKey would not: Dawntrail's high-end trials sort ahead of its normal ones (187 against 220). The 500 to 520 field-group
    /// bands themselves are their own ids and orders, not the Duty Finder ordering rule.</summary>
    private static readonly Dictionary<uint, int> DutyBands = new()
    {
        [26] = 500, [29] = 510, [38] = 520,                  // Eureka, Save the Queen, Occult Crescent: the field groups, shared with the Levels
        [2] = 1000, [3] = 1100, [4] = 1200, [5] = 1300,      // Dungeons, Guildhests, Trials, Raids
        [28] = 1400, [37] = 1410, [30] = 1420, [27] = 1430,  // Ultimate Raids, Chaotic Alliance Raid, V&C Dungeon Finder, The Masked Carnivale
    };

    /// <summary>The ContentType rows whose groups split by expansion and Duty Finder heading.</summary>
    private static readonly HashSet<uint> HeadedDutyTypes = [2, 4, 5];

    /// <summary>The ContentType row the Variant record series join, the V&C Dungeon Finder, and the order that puts the
    /// four series after its duties, whose SortKeys are in the hundreds.</summary>
    private const uint VariantDutyType = 30;
    private const int VariantSeriesOrderBase = 900;

    /// <summary><paramref name="warn"/>, if given, is called once for every hand-file feature unlock whose quest id is not in
    /// the rows (a game update removed or renumbered it), instead of the item being silently dropped, and once for every
    /// roulette row the hand file gives no unlock gate, which is then read ungated rather than silently.</summary>
    public static Catalog Build(CatalogRows rows, CatalogExtras extras, Action<string>? warn = null)
    {
        var items = new List<ItemDefinition>();
        items.AddRange(StoryQuests(rows, extras));
        items.AddRange(FeatureUnlocks(rows, extras, warn));
        items.AddRange(AetherCurrents(rows));
        items.AddRange(Tribes(rows));
        items.AddRange(Events(rows, extras));
        items.AddRange(Jobs(rows));
        items.AddRange(Resets(rows, extras, warn));
        items.AddRange(Troves(rows, extras, warn));
        items.AddRange(Content(rows, extras, warn));
        items.AddRange(Places(rows, extras, warn));
        items.AddRange(Collections(rows, warn));
        items.AddRange(Money(rows, warn));
        Validate(items);
        return new Catalog(items);
    }

    /// <summary>One group name carries one group order within a category.</summary>
    public static void Validate(IReadOnlyList<ItemDefinition> items)
    {
        foreach (var category in items.GroupBy(i => i.Category))
        {
            foreach (var group in category.GroupBy(i => i.Group, StringComparer.Ordinal))
            {
                var orders = group.Select(i => i.GroupOrder).Distinct().ToList();
                if (orders.Count > 1) throw new InvalidOperationException($"group '{group.Key}' in {category.Key} has {orders.Count} different group orders: {string.Join(", ", orders)}");
            }
        }
    }

    private static IEnumerable<ItemDefinition> StoryQuests(CatalogRows rows, CatalogExtras extras)
    {
        var kinds = extras.ChroniclesKinds.ToDictionary(k => k.CategoryId, k => k.Kind);
        var hildibrandRank = Rank(rows.Quests.Where(q => q.CategoryId == JournalIds.Hildibrand).Select(q => q.GenreId));
        var storylineRank = Rank(rows.Quests.Where(q => JournalIds.IsStorylineCategory(q.CategoryId)).Select(q => q.GenreId));
        foreach (var q in rows.Quests)
        {
            if (q.Section is >= 0 and <= JournalIds.MsqSectionMax)
                yield return Quest(q, q.GenreName, (int)q.CategoryId, isMsq: true);
            else if (q.CategoryId is >= JournalIds.ClassQuestsFirst and <= JournalIds.ClassQuestsLast)
                yield return Quest(q, q.GenreName, 100 + (int)q.GenreId - (int)JournalIds.ClassQuestGenreFirst);
            else if (q.CategoryId == JournalIds.RoleQuests)
                yield return Quest(q, q.GenreName, 200 + (int)q.GenreId - (int)JournalIds.RoleQuestGenreFirst);
            else if (q.CategoryId == JournalIds.Hildibrand)
                yield return Quest(q, q.GenreName, 300 + hildibrandRank[q.GenreId]);
            else if (q.CategoryId is >= JournalIds.ChroniclesFirst and <= JournalIds.ChroniclesLast)
            {
                // Most Chronicles category names repeat "Chronicles of a New Era - "; strip it so the group reads the arc's
                // own name, with the kind as a trailing qualifier ("<arc> (alliance raid)") rather than a repeated prefix.
                var arc = q.CategoryName.StartsWith(ChroniclesPrefix, StringComparison.Ordinal) ? q.CategoryName[ChroniclesPrefix.Length..] : q.CategoryName;
                yield return Quest(q, arc + KindSuffix(kinds.GetValueOrDefault(q.CategoryId, "Story")), 400 + (int)q.CategoryId);
            }
            else if (JournalIds.IsStorylineCategory(q.CategoryId))
                yield return Quest(q, q.GenreName, 500 + storylineRank[q.GenreId]);
        }
    }

    private static ItemDefinition Quest(QuestRow q, string group, int groupOrder, bool isMsq = false) =>
        new("quest:" + q.RowId, Category.Story, group, QuestName(q.Name), ItemKind.Done, ResetRule.None, q.SortKey, Freshness.AtLogin, IsMsq: isMsq, GroupOrder: groupOrder);

    /// <summary>A quest name as the journal reads it. Forty-two rows begin with a private-use glyph and a space: that glyph
    /// is the journal's own quest-type icon, drawn beside the name rather than part of it, so strip it and the space.</summary>
    private static string QuestName(string name)
    {
        var start = 0;
        while (start < name.Length && name[start] is >= '\uE000' and <= '\uF8FF') start++;
        if (start == 0) return name;
        while (start < name.Length && name[start] == ' ') start++;
        return name[start..];
    }

    private static string KindSuffix(string kind) => kind switch
    {
        "AllianceRaid" => " (alliance raid)",
        "NormalRaid" => " (raid)",
        "Trial" => " (trials)",
        _ => "",
    };

    private static IEnumerable<ItemDefinition> FeatureUnlocks(CatalogRows rows, CatalogExtras extras, Action<string>? warn)
    {
        var names = rows.Quests.ToDictionary(q => q.RowId, q => q.Name);
        var order = 0;
        foreach (var unlock in extras.FeatureUnlocks)
        {
            order++;
            if (!names.TryGetValue(unlock.QuestId, out var name))
            {
                warn?.Invoke($"feature unlock quest {unlock.QuestId} ({unlock.Feature}) is not in the rows; skipped");
                continue;
            }
            yield return new ItemDefinition("unlock:" + unlock.QuestId, Category.Story, "Feature unlocks", $"{QuestName(name)} ({unlock.Feature})", ItemKind.Done, ResetRule.None, order, Freshness.AtLogin, GroupOrder: 600);
        }
    }

    private static IEnumerable<ItemDefinition> AetherCurrents(CatalogRows rows) =>
        rows.AetherCurrentZones.Select(z => new ItemDefinition("aether:" + z.CompFlgSetId, Category.Story, "Aether currents", z.PlaceName, ItemKind.Counter, ResetRule.None, (int)z.CompFlgSetId, Freshness.AtLogin, GroupOrder: 700));

    private static IEnumerable<ItemDefinition> Tribes(CatalogRows rows) =>
        rows.BeastTribes.Select(t => new ItemDefinition("tribe:" + t.RowId, Category.Story, "Allied societies", t.Name, ItemKind.Level, ResetRule.None, (int)t.RowId, Freshness.AtLogin, GroupOrder: 800));

    /// <summary>The one place that defines which quests form an event occurrence — every quest in the seasonal
    /// events category or the collaboration genre, grouped by (genre, festival), quest ids in row-id order, ordered by
    /// genre then festival. The quest reader consumes this directly for "event:" items instead of restating the grouping.</summary>
    public static IReadOnlyList<(uint Genre, uint Festival, IReadOnlyList<uint> QuestIds)> EventOccurrences(CatalogRows rows) =>
        rows.Quests
            .Where(q => q.CategoryId == JournalIds.SeasonalEvents || q.GenreId == JournalIds.CollaborationGenre)
            .GroupBy(q => (q.GenreId, q.FestivalId))
            .OrderBy(g => g.Key.GenreId).ThenBy(g => g.Key.FestivalId)
            .Select(g => (g.Key.GenreId, g.Key.FestivalId, (IReadOnlyList<uint>)g.Select(q => q.RowId).OrderBy(id => id).ToList()))
            .ToList();

    private static IEnumerable<ItemDefinition> Events(CatalogRows rows, CatalogExtras extras)
    {
        // Two events can share a festival id, so a dated window is keyed on (genre, festival), never festival alone.
        var dated = extras.EventOccurrences.ToDictionary(o => (o.Genre, o.Festival));
        var maxDatedFestivalByGenre = extras.EventOccurrences.GroupBy(o => o.Genre).ToDictionary(g => g.Key, g => g.Max(o => o.Festival));
        var occurrences = EventOccurrences(rows);
        var genreNames = rows.Quests.Where(q => q.CategoryId == JournalIds.SeasonalEvents || q.GenreId == JournalIds.CollaborationGenre)
            .GroupBy(q => q.GenreId).ToDictionary(g => g.Key, g => g.First().GenreName);
        var genreRank = Rank(occurrences.Select(o => o.Genre));
        // Festival ids order occurrences within one event; an occurrence the hand file does not date is named by
        // its rank among that genre's own non-zero festival ids, ascending, since the hand file only dates 2024 onward.
        var festivalRankByGenre = occurrences.GroupBy(o => o.Genre).ToDictionary(g => g.Key, g => Rank(g.Select(o => o.Festival).Where(f => f != 0)));
        foreach (var occurrence in occurrences)
        {
            var genreName = genreNames[occurrence.Genre];
            var stripped = genreName.EndsWith(" Events", StringComparison.Ordinal) ? genreName[..^" Events".Length] : genreName;
            var group = occurrence.Genre == RisingGenre ? "The Rising" : stripped;
            var id = $"event:{occurrence.Genre}:{occurrence.Festival}";
            var groupOrder = genreRank[occurrence.Genre] + 1;   // group orders are 1-based: ItemDefinition documents 0 as "no group order"
            if (occurrence.Festival == 0)
            {
                yield return new ItemDefinition(id, Category.Events, group, group + " (permanent)", ItemKind.Done, ResetRule.None, 0, Freshness.AtLogin, GroupOrder: groupOrder);
                continue;
            }
            var window = dated.GetValueOrDefault((occurrence.Genre, occurrence.Festival));
            string name;
            Availability availability;
            if (window != null)
            {
                name = $"{window.Event} {window.Year}";
                availability = new Availability(window.Start, window.End);
            }
            else
            {
                var rank = festivalRankByGenre[occurrence.Genre][occurrence.Festival] + 1;
                name = $"{group} (run {rank})";
                // The sheets can carry a run before it is announced: an undated occurrence newer than the event's last
                // dated one is upcoming, not past; a genre with no dated occurrence at all is past.
                availability = maxDatedFestivalByGenre.TryGetValue(occurrence.Genre, out var maxDated) && occurrence.Festival > maxDated
                    ? UpcomingWindow
                    : PastWindow;
            }
            // A reward event counts its reward signals; it is gated on the Achievements window only
            // when an achievement is among them, because every other kind is in memory at login.
            var kind = window is { RewardList.Count: > 0 } ? ItemKind.Counter : ItemKind.Done;
            var freshness = window is { HasAchievement: true } ? Freshness.Window(AchievementsWindow) : Freshness.AtLogin;
            // Only a dated occurrence can be a reused return or a reward event; an undated one carries no hand-file row to say so.
            yield return new ItemDefinition(id, Category.Events, group, name, kind, ResetRule.Window, (int)occurrence.Festival, freshness, availability,
                GroupOrder: groupOrder, ReusedQuest: window?.Reused ?? false);
        }
    }

    /// <summary>One held Counter per Moogle Treasure Trove, keyed by the irregular tomestone's item id and named by
    /// the sheet, dated by the hand file's earning window. A held counter is never done and, inside
    /// its window, is information rather than attention; spending stays open until the next patch, so after the
    /// window the row reads "ended", never "missed". A trove whose item row the reducer did not find is reported and skipped.</summary>
    private static IEnumerable<ItemDefinition> Troves(CatalogRows rows, CatalogExtras extras, Action<string>? warn)
    {
        var names = rows.TroveItems.ToDictionary(t => t.ItemId, t => t.Name);
        foreach (var trove in extras.Troves)
        {
            if (!names.TryGetValue(trove.ItemId, out var name))
            {
                warn?.Invoke($"trove item {trove.ItemId} is not in the rows; skipped");
                continue;
            }
            yield return new ItemDefinition($"event:trove:{trove.ItemId}", Category.Events, "Moogle Treasure Trove", name,
                ItemKind.Counter, ResetRule.Window, (int)trove.ItemId, Freshness.AtLogin, new Availability(trove.Start, trove.End),
                GroupOrder: TroveGroupOrder, DoneAtCap: false);
        }
    }

    /// <summary>The Content rows that read at login: the owned estates, the PvP series, MGP, the
    /// hunting logs, then every duty in the Duty Finder's own groups with the Variant record series beside the V&C duties.
    /// Every row has ResetRule.None, so no Content row ever reaches a top group or the badge: a duty not yet cleared is
    /// information in its section, not attention. The place-gated Levels sit in the field groups.</summary>
    private static IEnumerable<ItemDefinition> Content(CatalogRows rows, CatalogExtras extras, Action<string>? warn)
    {
        // One Done row per estate type the static ownership call answers for; the reader labels an owned one.
        yield return new ItemDefinition("housing:personal", Category.Content, HousingGroup, "Personal estate", ItemKind.Done, ResetRule.None, 0, Freshness.AtLogin, GroupOrder: HousingGroupOrder);
        yield return new ItemDefinition("housing:fc", Category.Content, HousingGroup, "Free company estate", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: HousingGroupOrder);
        yield return new ItemDefinition("housing:apartment", Category.Content, HousingGroup, "Apartment", ItemKind.Done, ResetRule.None, 2, Freshness.AtLogin, GroupOrder: HousingGroupOrder);
        yield return new ItemDefinition("housing:chambers", Category.Content, HousingGroup, "Private chambers", ItemKind.Done, ResetRule.None, 3, Freshness.AtLogin, GroupOrder: HousingGroupOrder);

        // The house-visit timer, stamped only inside the personal estate; the reader supplies the deadline, and its
        // own warning window is the game's 30-day warning, 15 days before the 45-day demolition.
        yield return new ItemDefinition(HouseVisitId, Category.Content, HousingGroup, "House visit due", ItemKind.Timer, ResetRule.None, 10, Freshness.Place(YourHouse),
            GroupOrder: HousingGroupOrder, RemindWithin: extras.HouseVisit.RemindWithin);

        // The series level over the sheet's maximum, the rewards claimed over the levels earned, and the previous
        // series' rewards, claimable until this series ends. The profile is loaded at login, so
        // these read AtLogin; the gate is any of the three "A Pup No Longer" quests.
        yield return new ItemDefinition("pvp:series", Category.Content, PvpGroup, "Series level", ItemKind.Level, ResetRule.None, 0, Freshness.AtLogin, GroupOrder: PvpGroupOrder);
        yield return new ItemDefinition("pvp:claimed", Category.Content, PvpGroup, "Series rewards claimed", ItemKind.Counter, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: PvpGroupOrder);
        yield return new ItemDefinition("pvp:claimed-previous", Category.Content, PvpGroup, "Previous series rewards claimed", ItemKind.Counter, ResetRule.None, 2, Freshness.AtLogin, GroupOrder: PvpGroupOrder);

        // The Crystalline Conflict tier, a Level over the highest ColosseumMatchRank row that the reader labels
        // with its riser or credit and its season; a hand name, as "Series level" is.
        yield return new ItemDefinition(CrystallineConflictId, Category.Content, PvpGroup, "Crystalline Conflict tier", ItemKind.Level, ResetRule.None, CrystallineConflictOrder, Freshness.AtLogin, GroupOrder: PvpGroupOrder);
        // One PvP rank per Grand Company, over 50 from PvPRank; the reader clears a company never ranked in.
        foreach (var company in rows.GrandCompanies.OrderBy(c => c.RowId))
            yield return new ItemDefinition(PvpRankPrefix + company.RowId, Category.Content, PvpGroup, $"{company.Name} PvP rank", ItemKind.Level, ResetRule.None,
                PvpRankOrderBase + (int)company.RowId, Freshness.AtLogin, GroupOrder: PvpGroupOrder);

        // MGP under its currency id, a held counter with the sheet's stack size as its cap; a game version
        // whose currency list lacks item 29 builds no row and says so.
        if (rows.Currencies.FirstOrDefault(c => c.ItemId == MgpItemId) is { } mgp)
            yield return new ItemDefinition($"currency:{mgp.ItemId}", Category.Content, GoldSaucerGroup, mgp.Name, ItemKind.Counter, ResetRule.None, 0, Freshness.AtLogin, GroupOrder: GoldSaucerGroupOrder, DoneAtCap: false,
                IconId: IconOf(mgp));
        else warn?.Invoke($"currency item {MgpItemId} (MGP) is not in the rows; the Gold Saucer row is not built");

        // The racing chocobo's rank, behind the Gold Saucer window's Chocobo tab.
        yield return new ItemDefinition(RacingId, Category.Content, GoldSaucerGroup, "Racing chocobo rank", ItemKind.Level, ResetRule.None, 1, Freshness.Window(GoldSaucerWindow), GroupOrder: GoldSaucerGroupOrder);

        // One Counter per log, named by its class or company and ordered by the manager's index; done at 50 or 30.
        foreach (var log in rows.HuntingLogs)
        {
            var id = log.ClassJobId != 0 ? $"hunt:class:{log.ClassJobId}" : $"hunt:gc:{log.GrandCompanyId}";
            yield return new ItemDefinition(id, Category.Content, HuntingLogGroup, FirstUpper(log.Name), ItemKind.Counter, ResetRule.None, log.Index, Freshness.AtLogin, GroupOrder: HuntingLogGroupOrder);
        }

        // Every duty in the Duty Finder's own heading, or its tab's name where the Duty Finder has no heading,
        // ordered by the Duty Finder's SortKey; a family the Duty Finder does not list (the Carnivale, the field zones) carries
        // SortKey 0 throughout and is ordered by row id, the stage or zone order. The sheet writes a leading article in lower
        // case ("the Tam-Tara Deepcroft"); the Duty Finder shows it capitalised, so the first letter is raised.
        var headingRank = rows.Duties.Where(d => HeadedDutyTypes.Contains(d.ContentTypeId))
            .GroupBy(d => (d.ContentTypeId, d.Expansion))
            .ToDictionary(g => g.Key, g => Rank(g.Select(d => d.UiCategoryId)));
        foreach (var duty in rows.Duties)
        {
            if (!DutyBands.TryGetValue(duty.ContentTypeId, out var band))
            {
                warn?.Invoke($"duty {duty.RowId} ({duty.Name}) has content type {duty.ContentTypeId}, which has no group band; skipped");
                continue;
            }
            var headed = HeadedDutyTypes.Contains(duty.ContentTypeId);
            var group = headed ? duty.UiCategoryName : duty.ContentTypeName;
            var groupOrder = headed ? band + 10 * (int)duty.Expansion + headingRank[(duty.ContentTypeId, duty.Expansion)][duty.UiCategoryId] : band;
            yield return new ItemDefinition($"duty:{duty.RowId}", Category.Content, group, FirstUpper(duty.Name), ItemKind.Done, ResetRule.None,
                duty.SortKey != 0 ? duty.SortKey : (int)duty.RowId, Freshness.AtLogin, GroupOrder: groupOrder);
        }

        // One Counter per Variant series, records found over the series size, in the V&C group after its duties;
        // gated by the reader on the series' own dungeon through the hand file's map. A series the map does not name is built
        // ungated and said once, as an ungated roulette is.
        var variantGroup = rows.Duties.FirstOrDefault(d => d.ContentTypeId == VariantDutyType);
        var gated = extras.VariantSeries.Select(v => v.SeriesId).ToHashSet();
        foreach (var series in rows.VariantSeries)
        {
            if (variantGroup == null)
            {
                warn?.Invoke($"variant series {series.RowId} ({series.Name}) has no V&C duty to sit beside; skipped");
                continue;
            }
            if (!gated.Contains(series.RowId)) warn?.Invoke($"variant series {series.RowId} ({series.Name}) has no dungeon in the hand file's variantSeries; it is read ungated");
            yield return new ItemDefinition($"vvd:{series.RowId}", Category.Content, variantGroup.ContentTypeName, series.Name, ItemKind.Counter, ResetRule.None,
                VariantSeriesOrderBase + (int)series.RowId, Freshness.AtLogin, GroupOrder: DutyBands[VariantDutyType]);
        }
    }

    /// <summary>The place-gated Content rows: the three character-wide field levels in the
    /// field groups their duties already sit in, the 24 phantom jobs after the knowledge level, the eleven Cosmic research tools, the
    /// Island Sanctuary rank and each deep dungeon's aetherpool arm and armour. Every one is a NeedsPlace Level with ResetRule.None,
    /// so none reaches a top group or the badge. The place a row's marker names is its group's own name, or its dungeon's.
    /// A group whose ContentType row has no name on this game version is not built and is said once; a deep dungeon the hand file
    /// does not gate is built and read ungated, and said once, as an unmapped Variant series is.</summary>
    private static IEnumerable<ItemDefinition> Places(CatalogRows rows, CatalogExtras extras, Action<string>? warn)
    {
        var types = rows.ContentTypes.ToDictionary(t => t.RowId, t => t.Name);
        string? Named(uint type)
        {
            if (types.TryGetValue(type, out var name) && name.Length > 0) return name;
            warn?.Invoke($"content type {type} has no name in the rows; its place rows are not built");
            return null;
        }

        // One Level each for the whole character, beside the field duties of the same ContentType.
        foreach (var (id, type, name) in FieldLevels)
        {
            if (Named(type) is not { } group) continue;
            yield return new ItemDefinition(id, Category.Content, group, name, ItemKind.Level, ResetRule.None, 0, Freshness.Place(group), GroupOrder: DutyBands[type]);
            if (type != OccultType) continue;
            // The 24 phantom jobs, ordered 10 + JobIndex, each over its own LevelMax.
            foreach (var job in rows.PhantomJobs)
                yield return new ItemDefinition(PhantomJobPrefix + job.RowId, Category.Content, group, job.Name, ItemKind.Level, ResetRule.None,
                    PhantomOrderBase + job.JobIndex, Freshness.Place(group), GroupOrder: DutyBands[type]);
        }

        // One Level per research tool, named by the sheet with its first letter raised, over its own stages.
        foreach (var tool in rows.CosmicClasses)
            yield return new ItemDefinition(CosmicToolPrefix + tool.RowId, Category.Content, CosmicGroup, FirstUpper(tool.Name), ItemKind.Level, ResetRule.None,
                (int)tool.RowId, Freshness.Place(CosmicGroup), GroupOrder: CosmicGroupOrder);

        // The Sanctuary rank, over the highest MJIRank row.
        if (Named(IslandType) is { } island)
            yield return new ItemDefinition(IslandRankId, Category.Content, island, "Sanctuary rank", ItemKind.Level, ResetRule.None, 0, Freshness.Place(island), GroupOrder: IslandGroupOrder);

        // Each deep dungeon's aetherpool arm and armour, named by the dungeon and its equipment.
        if (Named(DeepDungeonType) is { } deep)
        {
            var gated = extras.PlaceUnlockQuests.DeepDungeons.Select(g => g.DungeonId).ToHashSet();
            foreach (var dungeon in rows.DeepDungeons)
            {
                if (!gated.Contains(dungeon.RowId)) warn?.Invoke($"deep dungeon {dungeon.RowId} ({dungeon.Name}) has no unlock quest in the hand file's placeUnlockQuests; it is read ungated");
                var place = FirstUpper(dungeon.Name);
                yield return new ItemDefinition($"{DeepDungeonPrefix}{dungeon.RowId}:arm", Category.Content, deep, $"{place}: {dungeon.ArmName}", ItemKind.Level, ResetRule.None,
                    10 * (int)dungeon.RowId, Freshness.Place(place), GroupOrder: DeepDungeonGroupOrder);
                yield return new ItemDefinition($"{DeepDungeonPrefix}{dungeon.RowId}:armor", Category.Content, deep, $"{place}: {dungeon.ArmorName}", ItemKind.Level, ResetRule.None,
                    10 * (int)dungeon.RowId + 1, Freshness.Place(place), GroupOrder: DeepDungeonGroupOrder);
            }
        }
    }

    /// <summary>The Collections category: one Counter per collection kind, log class, sightseeing
    /// expansion and achievement category, 102 rows, rather than a row per mount or achievement. Each is owned (or done) over the
    /// rows the reducer counted, done at cap, with ResetRule.None, so none reaches a top group or the badge. The achievement
    /// categories and the titles wait on their lists; everything else is AtLogin. A group or kind whose MainCommand
    /// row has no name on this game version is not built, and the builder says so.</summary>
    private static IEnumerable<ItemDefinition> Collections(CatalogRows rows, Action<string>? warn)
    {
        var commands = rows.MainCommands.ToDictionary(c => c.RowId, c => c.Name);
        string? Named(uint command)
        {
            if (commands.TryGetValue(command, out var name) && name.Length > 0) return name;
            warn?.Invoke($"main command {command} has no name in the rows; the collections rows it names are not built");
            return null;
        }

        // The eleven kinds, in CollectionIds.Kinds' order; only the titles wait on their list.
        var kinds = rows.Collections.Select(k => k.Kind).ToHashSet(StringComparer.Ordinal);
        if (Named(CollectionIds.CollectionCommand) is { } collection)
        {
            for (var order = 0; order < CollectionIds.Kinds.Count; order++)
            {
                var kind = CollectionIds.Kinds[order];
                if (!kinds.Contains(kind))
                {
                    warn?.Invoke($"collection kind {kind} is not in the rows; skipped");
                    continue;
                }
                var name = KindCommands.TryGetValue(kind, out var command) ? Named(command) : KindNames[kind];
                if (name == null) continue;
                var freshness = kind == CollectionIds.Title ? Freshness.Window(TitlesWindow) : Freshness.AtLogin;
                yield return new ItemDefinition(CollectionIds.KindId(kind), Category.Collections, collection, name, ItemKind.Counter, ResetRule.None, order, freshness, GroupOrder: CollectionGroupOrder);
            }
        }

        // One row per crafting and gathering class, named by the class and ordered by its ClassJob row, and the
        // fishing log's two halves.
        var jobs = rows.ClassJobs.ToDictionary(j => j.RowId);
        foreach (var log in rows.Logs)
        {
            var (command, groupOrder) = log.Log switch
            {
                CollectionIds.Craft => (CollectionIds.CraftingLogCommand, CraftingLogGroupOrder),
                CollectionIds.Gather => (CollectionIds.GatheringLogCommand, GatheringLogGroupOrder),
                _ => (CollectionIds.FishingLogCommand, FishingLogGroupOrder),
            };
            if (Named(command) is not { } group) continue;
            string name;
            int order;
            if (FisherLogs.TryGetValue(log.Log, out var fisher)) (name, order) = fisher;
            else if (jobs.TryGetValue(log.ClassJobId, out var job)) (name, order) = (FirstUpper(job.Name), (int)job.RowId);
            else
            {
                warn?.Invoke($"log {log.Log} names class {log.ClassJobId}, which is not in the rows; skipped");
                continue;
            }
            yield return new ItemDefinition(CollectionIds.LogId(log.Log, log.ClassJobId), Category.Collections, group, name, ItemKind.Counter, ResetRule.None, order, Freshness.AtLogin, GroupOrder: groupOrder);
        }

        // One row per expansion, named by the ExVersion sheet and ordered by its row.
        if (Named(CollectionIds.SightseeingLogCommand) is { } sightseeing)
        {
            foreach (var vistas in rows.SightseeingLogs)
                yield return new ItemDefinition(CollectionIds.SightseeingId(vistas.ExVersionId), Category.Collections, sightseeing, vistas.Name, ItemKind.Counter, ResetRule.None,
                    (int)vistas.ExVersionId, Freshness.AtLogin, GroupOrder: SightseeingLogGroupOrder);
        }

        // One row per achievement category, in its kind's group, behind the Achievements window.
        foreach (var category in rows.AchievementCategories)
            yield return new ItemDefinition(CollectionIds.AchievementId(category.RowId), Category.Collections, category.KindName, category.Name, ItemKind.Counter, ResetRule.None,
                category.Order, Freshness.Window(AchievementsWindow), GroupOrder: AchievementGroupOrderBase + 10 * category.KindOrder);
    }

    /// <summary>The Money category: 92 rows, every one with ResetRule.None, so none reaches a top group or the
    /// badge. The 56 currencies the player can hold, in three groups; three rows per retainer slot, which the reader labels with the
    /// retainer; and the six storage rows, the window-gated four named after their windows. Every Counter is held, never done at its cap,
    /// and every row but gil is listed only while it says something (HiddenWhenBlank). MGP is Content's and is not built again; the
    /// 20 tomestones with no Tomestones row cannot be held and are not built. A row whose MainCommand or Addon name is missing on this game
    /// version is not built, and the builder says so.</summary>
    private static IEnumerable<ItemDefinition> Money(CatalogRows rows, Action<string>? warn)
    {
        var commands = rows.MainCommands.ToDictionary(c => c.RowId, c => c.Name);
        var addons = rows.Addons.ToDictionary(a => a.RowId, a => a.Text);
        string? Named(Dictionary<uint, string> names, string sheet, uint row)
        {
            if (names.TryGetValue(row, out var name) && name.Length > 0) return name;
            warn?.Invoke($"{sheet} {row} has no name in the rows; the money rows it names are not built");
            return null;
        }

        // Of the 24 tomestone items only the four with a Tomestones row can be held; the society currencies are the
        // BeastTribe rows' CurrencyItem; everything else but MGP is a general currency.
        var tomestones = rows.TomestoneItems.ToDictionary(t => t.ItemId, t => t.TomestonesId);
        var societies = rows.BeastTribes.Where(t => t.CurrencyItem != 0).GroupBy(t => t.CurrencyItem).ToDictionary(g => g.Key, g => g.First());
        var currencies = rows.Currencies.Where(c => c.ItemId != MgpItemId).ToList();

        // Gil first, then the other general currencies in item order; gil is always listed, 0 included.
        if (Named(commands, "main command", MoneyIds.CurrencyCommand) is { } currencyGroup)
        {
            var order = 0;
            foreach (var currency in currencies.Where(c => !tomestones.ContainsKey(c.ItemId) && !societies.ContainsKey(c.ItemId)).OrderBy(c => c.ItemId))
                yield return HeldRow(MoneyIds.CurrencyId(currency.ItemId), currencyGroup, currency.Name, order++, Freshness.AtLogin, CurrencyGroupOrder, hidden: currency.ItemId != MoneyIds.Gil,
                    icon: IconOf(currency));
        }

        // The four tomestones that can be held, in their Tomestones row's order.
        if (Named(addons, "addon", MoneyIds.TomestonesAddon) is { } tomestoneGroup)
        {
            foreach (var currency in currencies.Where(c => tomestones.GetValueOrDefault(c.ItemId) != 0))
                yield return HeldRow(MoneyIds.CurrencyId(currency.ItemId), tomestoneGroup, currency.Name, (int)tomestones[currency.ItemId], Freshness.AtLogin, TomestoneGroupOrder,
                    icon: IconOf(currency));
        }

        // The society currencies, ten orders per expansion, in the society's own display order.
        if (Named(addons, "addon", MoneyIds.SocietalCurrencyAddon) is { } societalGroup)
        {
            foreach (var currency in currencies.Where(c => societies.ContainsKey(c.ItemId)))
            {
                var tribe = societies[currency.ItemId];
                yield return HeldRow(MoneyIds.CurrencyId(currency.ItemId), societalGroup, currency.Name, 10 * (int)tribe.Expansion + tribe.DisplayOrder, Freshness.AtLogin, SocietalGroupOrder,
                    icon: IconOf(currency));
            }
        }

        // Three rows per roster slot, the venture rows' ten slots, which the reader labels with the retainer.
        if (Named(addons, "addon", MoneyIds.RetainerAddon) is { } retainerGroup
            && Named(addons, "addon", MoneyIds.GilAddon) is { } gilWord
            && Named(addons, "addon", MoneyIds.RetainerInventoryAddon) is { } inventoryWord)
        {
            for (var slot = 0; slot < MoneyIds.RetainerSlots; slot++)
            {
                var name = $"{retainerGroup} {slot + 1}";
                yield return new ItemDefinition(MoneyIds.RetainerId(slot, MoneyIds.Level), Category.Money, retainerGroup, name, ItemKind.Level, ResetRule.None, 10 * slot,
                    Freshness.AtLogin, GroupOrder: RetainerGroupOrder, HiddenWhenBlank: true);
                yield return HeldRow(MoneyIds.RetainerId(slot, MoneyIds.RetainerGil), retainerGroup, $"{name}: {gilWord}", 10 * slot + 1, Freshness.AtLogin, RetainerGroupOrder);
                yield return HeldRow(MoneyIds.RetainerId(slot, MoneyIds.Slots), retainerGroup, $"{name}: {inventoryWord}", 10 * slot + 2, Freshness.AtLogin, RetainerGroupOrder);
            }
        }

        // The storage rows; the saddlebag, its right tab, the dresser and the armoire wait on their windows, each named
        // after its window.
        if (Named(addons, "addon", MoneyIds.StorageAddon) is { } storageGroup)
        {
            if (Named(commands, "main command", MoneyIds.InventoryCommand) is { } bags)
                yield return HeldRow(MoneyIds.Bags, storageGroup, bags, 0, Freshness.AtLogin, StorageGroupOrder);
            if (Named(commands, "main command", MoneyIds.ArmouryChestCommand) is { } armoury)
                yield return HeldRow(MoneyIds.Armoury, storageGroup, armoury, 1, Freshness.AtLogin, StorageGroupOrder);
            if (Named(commands, "main command", MoneyIds.SaddlebagCommand) is { } saddlebag)
            {
                yield return HeldRow(MoneyIds.Saddlebag, storageGroup, saddlebag, 2, Freshness.Window(saddlebag), StorageGroupOrder);
                if (Named(addons, "addon", MoneyIds.RightSaddlebagAddon) is { } premium)
                    yield return HeldRow(MoneyIds.PremiumSaddlebag, storageGroup, premium, 3, Freshness.Window(saddlebag), StorageGroupOrder);
            }
            if (Named(addons, "addon", MoneyIds.GlamourDresserAddon) is { } dresser)
                yield return HeldRow(MoneyIds.Dresser, storageGroup, dresser, 4, Freshness.Window(dresser), StorageGroupOrder);
            if (Named(addons, "addon", MoneyIds.ArmoireAddon) is { } armoire)
                yield return HeldRow(MoneyIds.Armoire, storageGroup, armoire, 5, Freshness.Window(armoire), StorageGroupOrder);
        }
    }

    /// <summary>A held Money Counter: never done at its cap, and listed only while it says something unless told otherwise.</summary>
    private static ItemDefinition HeldRow(string id, string group, string name, int order, Freshness freshness, int groupOrder, bool hidden = true, uint? icon = null) =>
        new(id, Category.Money, group, name, ItemKind.Counter, ResetRule.None, order, freshness, GroupOrder: groupOrder, DoneAtCap: false, HiddenWhenBlank: hidden, IconId: icon);

    /// <summary>A currency row's icon, its item's own, or none when the rows carry none.</summary>
    private static uint? IconOf(CurrencyRow currency) => currency.Icon == 0 ? null : currency.Icon;

    /// <summary>The first letter raised and nothing else touched, so "the Tam-Tara Deepcroft" reads as the Duty Finder shows it
    /// and "Order of the Twin Adder" keeps its own casing; Capitalise, which raises every word, would break the latter.</summary>
    private static string FirstUpper(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    private static IEnumerable<ItemDefinition> Jobs(CatalogRows rows)
    {
        foreach (var index in rows.ClassJobs.Where(j => j.ExpArrayIndex >= 0).GroupBy(j => j.ExpArrayIndex).OrderBy(g => g.Key))
        {
            // An experience index is usually one class/job pair; a few (Arcanist's) are shared by two jobs that both leave
            // the base class behind (Summoner, Scholar). Name the item after every such job, in row-id order, and take its
            // group and order from the first of them.
            var jobs = index.Where(j => j.Parent != j.RowId).OrderBy(j => j.RowId).ToList();
            if (jobs.Count == 0) jobs = index.OrderBy(j => j.RowId).Take(1).ToList();
            var job = jobs[0];
            var (group, groupOrder) = job.Category switch
            {
                33 => ("Crafters", 5),
                32 => ("Gatherers", 6),
                _ when job.IsLimitedJob => ("Limited jobs", 7),
                _ => job.Role switch { 1 => ("Tanks", 1), 4 => ("Healers", 2), 2 => ("Melee DPS", 3), _ => ("Ranged DPS", 4) },
            };
            var name = string.Join(" / ", jobs.Select(j => Capitalise(j.Name)));
            // The level cap (100, or 80 for Blue Mage) is not part of the definition; the reader supplies the value's Max from the same extras.
            // The icon of the row the item is named after, as its group and order are.
            yield return new ItemDefinition("job:" + index.Key, Category.Jobs, group, name, ItemKind.Level, ResetRule.None, job.UiPriority, Freshness.AtLogin, GroupOrder: groupOrder,
                IconId: JobIconBase + job.RowId);
        }
        yield return new ItemDefinition("blu:spells", Category.Jobs, "Limited jobs", "Blue Mage spells", ItemKind.Counter, ResetRule.None, 999, Freshness.AtLogin, GroupOrder: 7);
        foreach (var company in rows.GrandCompanies.OrderBy(c => c.RowId))
            yield return new ItemDefinition("gc:rank:" + company.RowId, Category.Jobs, "Grand Company", company.Name + " rank", ItemKind.Level, ResetRule.None, (int)company.RowId, Freshness.AtLogin, GroupOrder: 8);
        // The current Grand Company, first in the group. An information row the
        // reader names "Grand Company: <company>"; a Done value never done, so it is never attention, and in no top group, badge or matrix count
        // — though the group header counts it, as not done. Blank, and so not listed, in no company.
        yield return new ItemDefinition(CurrentCompanyId, Category.Jobs, "Grand Company", "Grand Company", ItemKind.Done, ResetRule.None, 0, Freshness.AtLogin,
            GroupOrder: 8, HiddenWhenBlank: true);
    }

    /// <summary>The Resets category. Daily groups first, then weekly, then the accrual and timer block, numbered
    /// in tens; the window and observed groups sit between the others at their own orders.
    /// Every group name is a sheet name or a hand name for a family with no sheet.</summary>
    private static IEnumerable<ItemDefinition> Resets(CatalogRows rows, CatalogExtras extras, Action<string>? warn)
    {
        var gated = extras.RouletteGates.Select(g => g.RouletteId).ToHashSet();
        foreach (var roulette in rows.Roulettes)
        {
            // A roulette the hand file does not gate is read as it was before the gates: visible on every character, never
            // silent. Saying so here is what turns a patch's new roulette into a hand-file line instead of a low alt's
            // badge.
            if (!gated.Contains(roulette.RowId))
                warn?.Invoke($"roulette {roulette.RowId} ({roulette.Name}) has no unlock gate in the hand file; it is read ungated");
            yield return new ItemDefinition($"reset:roulette:{roulette.RowId}", Category.Resets, "Duty roulettes", roulette.Name,
                ItemKind.Done, ResetRule.Daily, roulette.SortKey, Freshness.AtLogin, GroupOrder: 110);
        }

        foreach (var bill in rows.HuntOrderTypes.Where(t => t.Type == DailyHuntBill))
            yield return new ItemDefinition($"reset:hunt:{bill.RowId}", Category.Resets, "Hunt bills, daily", bill.Name,
                ItemKind.Done, ResetRule.Daily, (int)bill.RowId, Freshness.AtLogin, GroupOrder: 120);

        // One allowance for the whole character, not one per society: the client's counter is whole-character and the
        // per-questgiver route needs a raw iterator and an output buffer.
        // The name carries the direction: this row counts allowances used, so 12 of 12 is the day finished, while the
        // levequest row below counts allowances remaining, where 100 of 100 is the waste the top groups want to show.
        yield return new ItemDefinition("reset:tribe-allowance", Category.Resets, "Allied societies", "Allied society allowances used",
            ItemKind.Counter, ResetRule.Daily, 0, Freshness.AtLogin, GroupOrder: 130);

        // Two Done rows on one agent, split by position: the player does the halves separately, and one flag could
        // not say which is outstanding. Both share the Grand Company's own 20:00 UTC hour and the supply
        // window's edge gate.
        yield return new ItemDefinition("reset:gc:supply", Category.Resets, "Grand Company", "Supply missions",
            ItemKind.Done, ResetRule.DailyOn(ResetSchedules.GrandCompany), 0, Freshness.Window(GrandCompanyWindow), GroupOrder: 140);
        yield return new ItemDefinition("reset:gc:provision", Category.Resets, "Grand Company", "Provisioning missions",
            ItemKind.Done, ResetRule.DailyOn(ResetSchedules.GrandCompany), 1, Freshness.Window(GrandCompanyWindow), GroupOrder: 140);

        // Observed only: no memory holds a ticket count, so the plugin counts the tickets it sees bought, and the gate
        // reader stores a lock for a character without the broker's quest. Three a day at 15:00 UTC;
        // done at three.
        yield return new ItemDefinition("reset:cactpot:mini", Category.Resets, "Mini Cactpot", "Mini Cactpot tickets",
            ItemKind.Counter, ResetRule.Daily, 0, Freshness.Observed, GroupOrder: 150);

        // A new journal is offered every Tuesday 08:00, but a journal is valid for two weeks, so a partly filled one held
        // across that boundary still holds its stickers. The Weekly rule is what puts the row in This week; CarriesOver is
        // what stops the boundary zeroing the count of every character who was not logged in for it.
        yield return new ItemDefinition("reset:bingo:stickers", Category.Resets, "Wondrous Tails", "Wondrous Tails stickers",
            ItemKind.Counter, ResetRule.Weekly, 0, Freshness.AtLogin, GroupOrder: 210, CarriesOver: true);
        // Second chance points carry across journals, so they reset on nothing, and holding nine of them is
        // not a thing to do, so they are never done at cap.
        yield return new ItemDefinition("reset:bingo:second-chance", Category.Resets, "Wondrous Tails", "Second chance points",
            ItemKind.Counter, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: 210, DoneAtCap: false);

        yield return new ItemDefinition("reset:hollows", Category.Resets, "Faux Hollows", "Faux Hollows",
            ItemKind.Done, ResetRule.Weekly, 0, Freshness.AtLogin, GroupOrder: 215);

        // Fourteen categories in the sheet's own SortKey order, entries in MenuOrder; every entry resets
        // weekly and reads only once the client reports the log loaded, a state that survives the window closing.
        foreach (var entry in rows.ChallengeEntries)
            yield return new ItemDefinition($"reset:challenge:{entry.RowId}", Category.Resets, "Challenge log: " + entry.CategoryName, entry.Name,
                ItemKind.Done, ResetRule.Weekly, entry.MenuOrder, Freshness.Window(ChallengeLogWindow), GroupOrder: 220 + entry.CategorySortKey);

        yield return new ItemDefinition("reset:delivery:all", Category.Resets, "Custom deliveries", "Custom deliveries",
            ItemKind.Counter, ResetRule.Weekly, 0, Freshness.AtLogin, GroupOrder: 240);
        foreach (var npc in rows.SatisfactionNpcs)
            yield return new ItemDefinition($"reset:delivery:{npc.RowId}", Category.Resets, "Custom deliveries", npc.Name,
                ItemKind.Counter, ResetRule.Weekly, (int)npc.RowId, Freshness.AtLogin, GroupOrder: 240);

        foreach (var bill in rows.HuntOrderTypes.Where(t => t.Type == WeeklyHuntBill))
            yield return new ItemDefinition($"reset:hunt:{bill.RowId}", Category.Resets, "Hunt bills, weekly", bill.Name,
                ItemKind.Done, ResetRule.Weekly, (int)bill.RowId, Freshness.AtLogin, GroupOrder: 250);

        // One stable id across tiers: the row means "tomestones earned this week", which survives a tier change, while the
        // tomestone's own item id does not. Exactly one row qualifies today; if a patch ever made two, the
        // lowest item id wins and the other is not built, rather than colliding on the id.
        if (rows.Tomestones.OrderBy(t => t.ItemId).FirstOrDefault() is { } weekly)
            yield return new ItemDefinition("reset:tomestone:weekly", Category.Resets, "Tomestones", weekly.Name,
                ItemKind.Counter, ResetRule.Weekly, 0, Freshness.AtLogin, GroupOrder: 255);

        // Three a week, refilled at the draw, which falls at a different instant per data centre: the schedule is the
        // per-record sentinel the assembler resolves against the record's own data centre.
        yield return new ItemDefinition("reset:cactpot:jumbo", Category.Resets, "Jumbo Cactpot", "Jumbo Cactpot tickets",
            ItemKind.Counter, ResetRule.WeeklyOn(ResetSchedules.Jumbo), 0, Freshness.Observed, GroupOrder: 260);

        // Weekly, and doable only from Friday 08:00 UTC to the Tuesday 08:00 UTC reset.
        // The window recurs with the schedule, so no dates are carried and the row is never missed.
        yield return new ItemDefinition("reset:fashion-report", Category.Resets, "Fashion Report", "Fashion Report",
            ItemKind.Done, ResetRule.Weekly, 0, Freshness.Window(FashionReportWindow), GroupOrder: 265, Recurrence: FashionReportRecurrence);

        foreach (var (challenge, name) in CarnivaleTargets)
            yield return new ItemDefinition($"reset:carnivale:{challenge}", Category.Resets, "Masked Carnivale", name,
                ItemKind.Done, ResetRule.Weekly, challenge, Freshness.Window(CarnivaleWindow), GroupOrder: 270);

        // Donated over the weekly allowance, both read from the client; done when the allowance is spent.
        yield return new ItemDefinition("reset:enclave", Category.Resets, "Doman Enclave", "Doman Enclave donations",
            ItemKind.Counter, ResetRule.Weekly, 0, Freshness.Window(EnclaveWindow), GroupOrder: 275);

        // Exactly the duties whose instance row carries a week restriction, two today, fully sheet-derived;
        // the count is readable only for the duty selected in the Duty Finder.
        foreach (var lockout in rows.Lockouts)
            yield return new ItemDefinition($"reset:lockout:{lockout.ConditionId}", Category.Resets, "Raid lockouts", lockout.Name,
                ItemKind.Done, ResetRule.Weekly, lockout.SortKey, Freshness.Window(DutyFinderWindow), GroupOrder: 280);

        yield return new ItemDefinition("reset:leve-allowance", Category.Resets, "Allowances", "Levequest allowances remaining",
            ItemKind.Counter, ResetRule.Accrual(ResetSchedules.Leve, LevequestGrant), 0, Freshness.AtLogin, GroupOrder: 310);
        // The map cooldown is a rolling eighteen hours from the last map, not a clock boundary, so it is a plain deadline
        // with no schedule. A passed deadline is a map that may be gathered, not a lateness: on a
        // character with no cooldown the client's instant is the login instant, so the row reads "ready".
        yield return new ItemDefinition("reset:treasure-map", Category.Resets, "Allowances", "Treasure map",
            ItemKind.Timer, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: 310, ReadyWhenPassed: true);

        yield return new ItemDefinition(WondrousTailsJournalId, Category.Resets, "Timers", "Wondrous Tails journal",
            ItemKind.Timer, ResetRule.None, 0, Freshness.AtLogin, GroupOrder: 320);
        yield return new ItemDefinition("reset:squadron:mission", Category.Resets, "Timers", "Squadron mission",
            ItemKind.Timer, ResetRule.None, 1, Freshness.AtLogin, GroupOrder: 320);
        // Training shares the Grand Company's own 20:00 UTC hour. The deadline comes from the
        // client, and it is that timestamp falling into the past that puts a finished course back on the list: PlaceTimer
        // and the TimerValue verdict both branch on the deadline alone. The rule is carried for the day a Timer gains
        // period semantics.
        yield return new ItemDefinition("reset:squadron:training", Category.Resets, "Timers", "Squadron training",
            ItemKind.Timer, ResetRule.DailyOn(ResetSchedules.GrandCompany), 2, Freshness.AtLogin, GroupOrder: 320);
        for (var slot = 0; slot < VentureSlots; slot++)
            yield return new ItemDefinition(MoneyIds.VentureId(slot), Category.Resets, "Timers", $"Venture: retainer {slot + 1}",
                ItemKind.Timer, ResetRule.None, 10 + slot, Freshness.AtLogin, GroupOrder: 320);
    }

    private static Dictionary<uint, int> Rank(IEnumerable<uint> ids) =>
        ids.Distinct().OrderBy(id => id).Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);

    /// <summary>Every word's first letter raised, as the Jobs section names a job; MoneyRules names a retainer's class the same way.</summary>
    internal static string Capitalise(string name) =>
        string.Join(' ', name.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
}
