namespace Mnemosyne.Core.Definitions;

/// <summary>The sheets reduced to plain rows. Interop fills these from Lumina once at start; the dump tool
/// writes the same records to JSON for the test fixtures. No Lumina type ever crosses into Core. The rows cover the
/// challenge-log entries, the week-restricted duties and the trove items; the duties, the Variant record
/// series, the hunting logs, the PvP series levels and the currencies; the content types, the phantom jobs,
/// the Cosmic research tools, the Island Sanctuary ranks and the deep dungeons; the MainCommand names, the
/// collection kinds, the logs, the sightseeing log, the achievement categories, the PvP ranks and the Crystalline
/// Conflict tiers; the tomestone items, the Addon names and the armoire's storable rows, and each allied
/// society's currency.</summary>
public sealed record CatalogRows(
    string GameVersion,
    string DumpedUtc,
    IReadOnlyList<QuestRow> Quests,
    IReadOnlyList<ClassJobRow> ClassJobs,
    IReadOnlyList<AetherCurrentZoneRow> AetherCurrentZones,
    IReadOnlyList<BeastTribeRow> BeastTribes,
    IReadOnlyList<GrandCompanyRow> GrandCompanies,
    int AozActionCount,
    IReadOnlyList<RouletteRow> Roulettes,
    IReadOnlyList<HuntOrderTypeRow> HuntOrderTypes,
    IReadOnlyList<SatisfactionNpcRow> SatisfactionNpcs,
    IReadOnlyList<TomestoneRow> Tomestones,
    IReadOnlyList<ContentsNoteRow> ChallengeEntries,
    IReadOnlyList<LockoutRow> Lockouts,
    IReadOnlyList<TroveItemRow> TroveItems,
    IReadOnlyList<DutyRow> Duties,
    IReadOnlyList<VariantSeriesRow> VariantSeries,
    IReadOnlyList<HuntingLogRow> HuntingLogs,
    IReadOnlyList<PvPSeriesLevelRow> PvPSeriesLevels,
    IReadOnlyList<CurrencyRow> Currencies,
    IReadOnlyList<ContentTypeRow> ContentTypes,
    IReadOnlyList<PhantomJobRow> PhantomJobs,
    IReadOnlyList<CosmicClassRow> CosmicClasses,
    IReadOnlyList<IslandRankRow> IslandRanks,
    IReadOnlyList<DeepDungeonRow> DeepDungeons,
    IReadOnlyList<MainCommandRow> MainCommands,
    IReadOnlyList<CollectionKindRow> Collections,
    IReadOnlyList<LogRow> Logs,
    IReadOnlyList<SightseeingRow> SightseeingLogs,
    IReadOnlyList<AchievementCategoryRow> AchievementCategories,
    IReadOnlyList<PvPRankRow> PvPRanks,
    IReadOnlyList<ColosseumRankRow> ColosseumRanks,
    IReadOnlyList<TomestoneItemRow> TomestoneItems,
    IReadOnlyList<AddonRow> Addons,
    IReadOnlyList<uint> ArmoireRows);

/// <summary>One quest in a journal genre that is tracked. Section is the journal section (0 and 1 are MSQ).</summary>
public sealed record QuestRow(uint RowId, string Name, uint GenreId, string GenreName, uint CategoryId, string CategoryName, int Section, int SortKey, uint FestivalId, uint Expansion);

/// <summary>ExpArrayIndex is -1 for rows that are not levelled; Parent equals RowId for a class.</summary>
public sealed record ClassJobRow(uint RowId, string Abbreviation, string Name, int ExpArrayIndex, int Role, uint Category, uint Parent, int DohDolIndex, bool IsLimitedJob, int UiPriority);

/// <summary>One flag set per zone with the currents it holds.</summary>
public sealed record AetherCurrentZoneRow(uint CompFlgSetId, uint TerritoryId, string PlaceName, IReadOnlyList<uint> AetherCurrentIds);

/// <summary>The society's currency item (BeastTribe.CurrencyItem) and its display order within its expansion,
/// which name and place its Money row.</summary>
public sealed record BeastTribeRow(uint RowId, string Name, int MaxRank, uint Expansion, uint CurrencyItem, int DisplayOrder);

/// <summary>The three companies and how many ranks the GrandCompanyRank sheet defines; the count is read from the sheet at
/// reduction time, never hard-coded.</summary>
public sealed record GrandCompanyRow(uint RowId, string Name, int RankCount);

/// <summary>One duty roulette that grants a once-a-day completion bonus: the eleven rows the sheet marks with a
/// CompletionArrayIndex of 0 or more and IsInDutyFinder set. The seven blank rows carry index 0 rather than -1, so the
/// duty-finder flag is what separates them from the real Leveling roulette. SortKey is the game's own duty-finder display
/// order and is neither the row id nor the completion index. MemberContentIds are the InstanceContent row
/// ids of every named ContentFinderCondition row flagged for this roulette, in row order: the duties the roulette draws
/// from, and so the duties whose unlock and completion bits decide whether the roulette itself is open.</summary>
public sealed record RouletteRow(uint RowId, string Name, int SortKey, IReadOnlyList<uint> MemberContentIds);

/// <summary>One hunt board's bill. The MobHuntOrderType row id is the client's own mark index, one for one; Type 1 is a
/// daily bill and Type 2 a weekly elite bill, which is a pure sheet rule with no exceptions; Name is the bill's event item,
/// which is what the board calls it. OrderStart and OrderAmount give the contiguous MobHuntOrder row range this mark's
/// available and obtained row ids fall in.</summary>
public sealed record HuntOrderTypeRow(uint RowId, int Type, string Name, uint OrderStart, int OrderAmount);

/// <summary>One custom-delivery client. The row id indexes the client's twelve-entry allowance arrays at row id minus one,
/// and the name is read from the ENpcResident row the sheet points at, so it arrives in the client's language.
/// QuestRequired is the quest that unlocks the client (row 1 is 67087 "Arms Wide Open"), which is the
/// gate on the client's row; zero would mean the sheet names none and the row reads ungated.</summary>
public sealed record SatisfactionNpcRow(uint RowId, string Name, int DeliveriesPerWeek, uint QuestRequired);

/// <summary>A tomestone with a weekly earning limit: a TomestonesItem row with a live currency slot whose Tomestones row
/// carries a non-zero WeeklyLimit. Exactly one qualifies today, Mnemonics at 900, and the rule needs no code change at the
/// next tier. The limit here is the fallback; the client's own limit is the authority.</summary>
public sealed record TomestoneRow(uint ItemId, string Name, int WeeklyLimit);

/// <summary>One challenge-log entry: 104 rows, 90 named, in fourteen categories whose SortKey is the display order and whose
/// MenuOrder orders entries within one; every entry resets weekly. ReqUnlock is the raw gate value: a quest row
/// above 65535 or an unlock link below (95 is Desynthesis's, 372 is taken to be one; not yet confirmed in game), and LevelUnlock gates five entries on a
/// level; both are 0 when absent. The category name arrives in the client's language.</summary>
public sealed record ContentsNoteRow(uint RowId, string Name, uint CategoryId, string CategoryName, int CategorySortKey, int MenuOrder, uint ReqUnlock, int LevelUnlock);

/// <summary>One duty whose instance row carries a weekly loot lockout: exactly the InstanceContent rows with WeekRestriction 1,
/// two on this game version, fully sheet-derived. ConditionId is the ContentFinderCondition row the Duty Finder
/// selects by and the row's id; ContentId is the InstanceContent row the unlock read takes; SortKey is the Duty
/// Finder's own order.</summary>
public sealed record LockoutRow(uint ConditionId, uint ContentId, string Name, int SortKey);

/// <summary>The item behind one Moogle Treasure Trove: an irregular tomestone, a plain Item row and never a TomestonesItem
/// row, named by the hand file's troves section and read from the sheet so the name is in the client's language; StackSize
/// is the holding cap the counter shows.</summary>
public sealed record TroveItemRow(uint ItemId, string Name, int StackSize);

/// <summary>One duty of the Content category: a named ContentFinderCondition row of an in-scope ContentType, with the link its
/// completion is read through: LinkType 1 is an InstanceContent row and 3 a PublicContent row, and ContentId is that
/// row's id, never the condition's. ContentTypeName is the Duty Finder's tab ("Dungeons", "Ultimate Raids") and UiCategoryName
/// its heading ("Dungeons (Dawntrail)"), both in the client's language; UiCategoryId is 0 where the Duty Finder has no heading
/// (guildhests, ultimates, the Carnivale, the field zones). Expansion is RequiredExVersion; SortKey is the Duty Finder's order,
/// 0 throughout a family it does not list.</summary>
public sealed record DutyRow(uint RowId, string Name, int LinkType, uint ContentId, uint ContentTypeId, string ContentTypeName, uint UiCategoryId, string UiCategoryName, uint Expansion, int SortKey);

/// <summary>One Variant dungeon's record series: the four named VVDNotebookSeries rows and the VVDNotebookContents rows each
/// lists, 12, 12, 12 and 13; the bit for a record is its row id minus one.</summary>
public sealed record VariantSeriesRow(uint RowId, string Name, IReadOnlyList<uint> ContentsIds);

/// <summary>One hunting log: the manager index (0 to 11) it is read at, the class or Grand Company row it belongs to (the other
/// is 0), its name in the client's language, and the kills each of its notes needs. The index is sheet data, ClassJob.MonsterNote
/// and GrandCompany.MonsterNote: nine classes at 0 to 7 and 11, the three companies at 8 to 10. Notes run rank by rank,
/// 50 for a class and 30 for a company.</summary>
public sealed record HuntingLogRow(int Index, uint ClassJobId, uint GrandCompanyId, string Name, IReadOnlyList<HuntingNoteRow> Notes);

/// <summary>One hunting log note: its zero-based rank and position, and the kills each of its four target slots needs, 0 for a
/// slot the note does not use.</summary>
public sealed record HuntingNoteRow(int Rank, int Note, IReadOnlyList<int> NeededKills);

/// <summary>One PvP series level: the row id is the level and ExpToNext the experience that leaves it, 0 at row 0 and 20,000 at
/// row 30, which is also every extra level's price. The highest row is the series' maximum.</summary>
public sealed record PvPSeriesLevelRow(uint RowId, int ExpToNext);

/// <summary>One currency: every item whose sort category is 3, exactly 77 on this game version, with its holding cap. One of them,
/// MGP (item 29), is built under Content; 56 more are built under Money from the same list, gil
/// among them: every one but MGP and the tomestones that cannot be held. Icon is the item's own icon
/// number, the Item sheet's Icon, which the Starlight look draws beside the row; 0 when the rows carry none, as an
/// older dump does.</summary>
public sealed record CurrencyRow(uint ItemId, string Name, int StackSize, uint Icon = 0);

/// <summary>One named ContentType row, the Duty Finder's tab name in the client's language. The place groups take their names
/// from it: Eureka 26, Save the Queen 29 and Occult Crescent 38, whose field duties sit in them, Deep Dungeons 21 and Island
/// Sanctuary 36.</summary>
public sealed record ContentTypeRow(uint RowId, string Name);

/// <summary>One Occult Crescent phantom job: the MKDSupportJob row, which the phantom level array is taken to be indexed by
/// (not yet confirmed in game), its name, its own maximum level, and its JobIndex, which orders the rows; JobIndex runs 1 to 24, each once
/// (the game's MKDSupportJob sheet, rows 0 to 23).</summary>
public sealed record PhantomJobRow(uint RowId, string Name, int LevelMax, int JobIndex);

/// <summary>One Cosmic Exploration research tool: the WKSCosmoToolClass row (1 Carpenter to 11 Fisher) and how many research stages
/// it lists, 20 on this game version, with the tool's name ("carpenter's tool") (the game's WKSCosmoToolName sheet,
/// rows 3001 to 3011).</summary>
public sealed record CosmicClassRow(uint RowId, string Name, int Stages);

/// <summary>One Island Sanctuary rank: the MJIRank row is the rank and ExpToNext the experience from it to the next, 0 at the cap,
/// row 20, which makes the cap sheet-derived.</summary>
public sealed record IslandRankRow(uint RowId, int ExpToNext);

/// <summary>One deep dungeon: the DeepDungeon row (1 to 4), its name, and the names of its aetherpool arm and armour from
/// DeepDungeonEquipment.</summary>
public sealed record DeepDungeonRow(uint RowId, string Name, string ArmName, string ArmorName);

/// <summary>One MainCommand row the Collections section takes a name from, in the client's language: its groups and six of its
/// kinds. Only the rows CollectionIds.Commands names are reduced.</summary>
public sealed record MainCommandRow(uint RowId, string Name);

/// <summary>One collection kind (CollectionIds' names) and the rows its total counts: the kind's own sheet rows for
/// mounts, minions, emotes, bardings, facewear styles, ornaments, orchestrion rolls and cards; unlock links for hairstyles; Item
/// rows for framer's kits; Title rows for titles.</summary>
public sealed record CollectionKindRow(string Kind, IReadOnlyList<uint> Ids);

/// <summary>One log the client keeps its own completion bits for: crafting per class (Recipe rows below 30000 with a
/// result), gathering per class (the notebook's GatheringItem rows), fishing (FishParameter rows) and spearfishing
/// (SpearfishingItem rows), keyed by the class that opens it, Fisher's for both of the last two.</summary>
public sealed record LogRow(string Log, uint ClassJobId, IReadOnlyList<uint> Ids);

/// <summary>One expansion's block of the sightseeing log: the ExVersion row and its name, the AdventureExPhase quest
/// that opens the block (0 for A Realm Reborn and Heavensward, which the reader gates otherwise), and its Adventure rows.</summary>
public sealed record SightseeingRow(uint ExVersionId, string Name, uint QuestId, IReadOnlyList<uint> AdventureIds);

/// <summary>One achievement category: its name and order, its kind's row, name and order, which place its group, and its
/// named achievements. The Legacy kind and the Ranking category are not reduced.</summary>
public sealed record AchievementCategoryRow(uint RowId, string Name, int Order, uint KindId, string KindName, int KindOrder, IReadOnlyList<uint> AchievementIds);

/// <summary>One PvP rank: ExpRequired as the sheet holds it, 0 above row 50, and the rank's band title in the Maelstrom's,
/// the Twin Adder's and the Immortal Flames' columns, GrandCompany rows 1 to 3 in order.</summary>
public sealed record PvPRankRow(uint RowId, int ExpRequired, IReadOnlyList<string> Titles);

/// <summary>One Crystalline Conflict tier: the ColosseumMatchRank row and its name, "-" at row 0, Bronze to Ultima at 1
/// to 8.</summary>
public sealed record ColosseumRankRow(uint RowId, string Name);

/// <summary>One tomestone item: every TomestonesItem row with an item, 24, and its Tomestones row. Only the four whose Tomestones row
/// is not 0 can be held, since tomestones from older expansions are converted to Poetics; the other 20 are not built.</summary>
public sealed record TomestoneItemRow(uint ItemId, uint TomestonesId);

/// <summary>One Addon row the Money section takes a name from, in the client's language: a group, a storage row or a retainer field word. Only
/// the rows MoneyIds.Addons names are reduced.</summary>
public sealed record AddonRow(uint RowId, string Text);
