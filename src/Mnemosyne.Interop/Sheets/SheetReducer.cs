using Lumina.Excel;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Interop.Sheets;

/// <summary>The sheets reduced to plain rows, once. Only Lumina types appear here so the dump tool can call it
/// outside the game. Which quests are kept follows the journal rules; every journal id it turns on is <see cref="JournalIds"/>,
/// which the catalog builder reads from too, so the two cannot drift apart. It reduces: the four Resets collections — the
/// daily-bonus roulettes, the hunt mark bills, the custom-delivery clients, and the tomestone with a weekly limit; the
/// challenge-log entries, the week-restricted duties and the trove items; the duties, the Variant
/// record series, the hunting logs, the PvP series levels and the currencies; the content types, the phantom
/// jobs, the Cosmic research tools, the Island Sanctuary ranks and the deep dungeons; the MainCommand names,
/// the collection kinds, the logs, the sightseeing log, the achievement categories, the PvP ranks and the Crystalline
/// Conflict tiers; and each allied society's currency, the tomestone items, the Addon names and the armoire's
/// storable rows, keeping Money's four MainCommand rows.</summary>
public static class SheetReducer
{
    public static CatalogRows Reduce(ExcelModule excel, CatalogExtras extras, string gameVersion, DateTimeOffset now)
    {
        var unlocks = extras.FeatureUnlocks.Select(f => f.QuestId).ToHashSet();
        var quests = new List<QuestRow>();
        foreach (var quest in excel.GetSheet<Quest>())
        {
            var name = quest.Name.ExtractText();
            if (name.Length == 0) continue;
            var genre = quest.JournalGenre.RowId == 0 ? null : quest.JournalGenre.ValueNullable;           // row 0 is a real row; it means "no genre"
            var category = genre is { } g && g.JournalCategory.RowId != 0 ? g.JournalCategory.ValueNullable : null;
            var section = category is { } c ? (int)c.JournalSection.RowId : -1;                             // -1 only when there is no category; 0 and 1 are MSQ
            var categoryId = category?.RowId ?? 0;
            var genreId = genre?.RowId ?? 0;
            var keep = section is >= 0 and <= JournalIds.MsqSectionMax || JournalIds.IsKeptCategory(categoryId) || genreId == JournalIds.CollaborationGenre || unlocks.Contains(quest.RowId);
            if (!keep) continue;
            quests.Add(new QuestRow(quest.RowId, name, genreId, genre?.Name.ExtractText() ?? "", categoryId, category?.Name.ExtractText() ?? "", section, quest.SortKey, quest.Festival.RowId, quest.Expansion.RowId));
        }

        var jobs = excel.GetSheet<ClassJob>()
            .Where(j => j.RowId != 0 && j.Abbreviation.ExtractText().Length > 0)
            .Select(j => new ClassJobRow(j.RowId, j.Abbreviation.ExtractText(), j.Name.ExtractText(), j.ExpArrayIndex, j.Role, j.ClassJobCategory.RowId, j.ClassJobParent.RowId, j.DohDolJobIndex, j.IsLimitedJob, j.UIPriority))
            .ToList();

        var zones = excel.GetSheet<AetherCurrentCompFlgSet>()
            .Where(z => z.Territory.RowId != 0)
            .Select(z => new AetherCurrentZoneRow(z.RowId, z.Territory.RowId,
                z.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
                z.AetherCurrents.Where(c => c.RowId != 0).Select(c => c.RowId).ToList()))
            .ToList();

        // Each society's currency item and its display order within its expansion place its Money row.
        var tribes = excel.GetSheet<BeastTribe>()
            .Where(t => t.RowId != 0 && t.Name.ExtractText().Length > 0)
            .Select(t => new BeastTribeRow(t.RowId, t.Name.ExtractText(), t.MaxRank, t.Expansion.RowId, t.CurrencyItem.RowId, t.DisplayOrder))
            .ToList();

        var rankCount = excel.GetSheet<GrandCompanyRank>().Count(r => r.RowId != 0);
        var companies = excel.GetSheet<GrandCompany>()
            .Where(g => g.RowId is >= 1 and <= 3)
            .Select(g => new GrandCompanyRow(g.RowId, g.Name.ExtractText(), rankCount))
            .ToList();

        var aozActions = excel.GetSheet<AozAction>().Count(a => a.RowId != 0);

        // A roulette's members are the named ContentFinderCondition rows flagged for it, and every flagged
        // row links an InstanceContent row (ContentLinkType 1), whose id is what the client's unlock and completion reads
        // take. One pass over the sheet, kept as a list so the eleven flag filters below do not re-read it.
        var conditions = excel.GetSheet<ContentFinderCondition>()
            .Where(c => c.ContentLinkType == 1 && c.Name.ExtractText().Length > 0)
            .ToList();

        // The daily-bonus roulettes are exactly IsInDutyFinder with a CompletionArrayIndex of 0 or more.
        var roulettes = excel.GetSheet<ContentRoulette>()
            .Where(r => r.IsInDutyFinder && r.CompletionArrayIndex >= 0)
            .Select(r => new RouletteRow(r.RowId, r.Name.ExtractText(), r.SortKey, RouletteMembers(r.RowId, conditions)))
            .OrderBy(r => r.SortKey)
            .ToList();

        // Type 1 is daily, Type 2 weekly, and the row id is the client's own mark index.
        var huntOrderTypes = excel.GetSheet<MobHuntOrderType>()
            .Where(t => t.Type is 1 or 2)
            .Select(t => new HuntOrderTypeRow(t.RowId, t.Type,
                t.EventItem.RowId == 0 ? "" : t.EventItem.ValueNullable?.Name.ExtractText() ?? "",
                t.OrderStart.RowId, t.OrderAmount))
            .OrderBy(t => t.RowId)
            .ToList();

        // Twelve clients, each six deliveries a week; the name comes from the ENpcResident row.
        // QuestRequired is the client's unlock quest.
        var satisfactionNpcs = excel.GetSheet<SatisfactionNpc>()
            .Where(n => n.RowId != 0 && n.DeliveriesPerWeek > 0)
            .Select(n => new SatisfactionNpcRow(n.RowId,
                n.Npc.RowId == 0 ? "" : n.Npc.ValueNullable?.Singular.ExtractText() ?? "",
                n.DeliveriesPerWeek, n.QuestRequired.RowId))
            .OrderBy(n => n.RowId)
            .ToList();

        // A live currency slot plus a non-zero weekly limit on the Tomestones row.
        var tomestones = excel.GetSheet<TomestonesItem>()
            .Where(t => t.CurrencyInventorySlot >= 0 && t.Item.RowId != 0 && t.Tomestones.RowId != 0)
            .Select(t => (t.Item, Limit: (int)(t.Tomestones.ValueNullable?.WeeklyLimit ?? 0)))
            .Where(t => t.Limit > 0)
            .Select(t => new TomestoneRow(t.Item.RowId, t.Item.ValueNullable?.Name.ExtractText() ?? "", t.Limit))
            .ToList();

        // 104 rows, 90 named, across 14 categories; the category's SortKey is the display order and
        // MenuOrder orders within it. ReqUnlock is a quest above 65535 or an unlock link below, and
        // LevelUnlock gates five entries. The column that names the category is ContentType.
        var challengeEntries = excel.GetSheet<ContentsNote>()
            .Where(n => n.Name.ExtractText().Length > 0 && n.ContentType.RowId != 0)
            .Select(n => new ContentsNoteRow(n.RowId, n.Name.ExtractText(), n.ContentType.RowId,
                n.ContentType.ValueNullable?.Name.ExtractText() ?? "", n.ContentType.ValueNullable?.SortKey ?? 0,
                n.MenuOrder, n.ReqUnlock.RowId, n.LevelUnlock))
            .OrderBy(n => n.CategorySortKey).ThenBy(n => n.MenuOrder).ThenBy(n => n.RowId)
            .ToList();

        // Exactly the InstanceContent rows with WeekRestriction 1 carry a weekly loot
        // lockout, two today; the Duty Finder selects by the ContentFinderCondition row, the unlock read takes this row.
        var lockouts = excel.GetSheet<InstanceContent>()
            .Where(i => i.WeekRestriction == 1 && i.ContentFinderCondition.RowId != 0)
            .Select(i => (Content: i.RowId, Condition: i.ContentFinderCondition.ValueNullable))
            .Where(x => x.Condition is { } c && c.Name.ExtractText().Length > 0)
            .Select(x => new LockoutRow(x.Condition!.Value.RowId, x.Content, x.Condition.Value.Name.ExtractText(), x.Condition.Value.SortKey))
            .OrderBy(l => l.SortKey)
            .ToList();

        // An irregular tomestone is a plain Item row; the hand file names the id, the sheet the name and
        // the stack size. A trove whose item row does not exist on this game version is simply not reduced.
        var items = excel.GetSheet<Item>();
        var troveItems = extras.Troves
            .Select(t => items.GetRowOrDefault(t.ItemId) is { } item ? new TroveItemRow(t.ItemId, item.Name.ExtractText(), (int)item.StackSize) : null)
            .OfType<TroveItemRow>()
            .ToList();

        // The Content category's duties are the named ContentFinderCondition rows of eight ContentType
        // rows on link type 1 (Dungeons 2, Guildhests 3, Trials 4, Raids 5, the Masked Carnivale 27, Ultimate Raids 28, V&C
        // Dungeon Finder 30, Chaotic Alliance Raid 37) and of three on link type 3 (Eureka 26, Save the Queen 29, Occult
        // Crescent 38). Types 2, 4 and 5 keep only rows the Duty Finder gives a heading, which drops the two obsolete special
        // events (70, 71). Link types 2 and 4 have no completion read and are not reduced; no row uses type 5 on this build.
        var duties = excel.GetSheet<ContentFinderCondition>()
            .Where(c => c.Name.ExtractText().Length > 0 && DutyInScope(c))
            .Select(c => new DutyRow(c.RowId, c.Name.ExtractText(), c.ContentLinkType, c.Content.RowId,
                c.ContentType.RowId, c.ContentType.ValueNullable?.Name.ExtractText() ?? "",
                c.ContentUICategory.RowId, c.ContentUICategory.ValueNullable?.Name.ExtractText() ?? "",
                c.RequiredExVersion.RowId, c.SortKey))
            .OrderBy(d => d.RowId)
            .ToList();

        // The four named series and the record rows each lists; row 0 of the contents sheet is empty and is
        // no record.
        var variantSeries = excel.GetSheet<VVDNotebookSeries>()
            .Where(s => s.Name.ExtractText().Length > 0)
            .Select(s => new VariantSeriesRow(s.RowId, s.Name.ExtractText(), s.Contents.Where(c => c.RowId != 0).Select(c => c.RowId).ToList()))
            .OrderBy(s => s.RowId)
            .ToList();

        // The log index is ClassJob.MonsterNote for a class (its own row, so ClassJobParent is itself; 0 to 11,
        // where crafters and gatherers carry 127 and jobs from Machinist on carry -1, which Lumina reads as a row id far above
        // 11) and GrandCompany.MonsterNote for a company (8 to 10). A class note is row class × 10,000 + rank × 10 + note + 1
        // with five ranks of ten; a company note is company × 1,000,000 + rank × 10 + note + 1 with three ranks. Count is the
        // kills each target slot needs, 0 for an unused slot.
        var monsterNotes = excel.GetSheet<MonsterNote>();
        var huntingLogs = new List<HuntingLogRow>();
        foreach (var job in excel.GetSheet<ClassJob>().Where(j => j.RowId != 0 && j.ClassJobParent.RowId == j.RowId && j.MonsterNote.RowId <= 11))
            huntingLogs.Add(new HuntingLogRow((int)job.MonsterNote.RowId, job.RowId, 0, job.Name.ExtractText(), HuntingNotes(monsterNotes, job.RowId * 10000, 5)));
        foreach (var company in excel.GetSheet<GrandCompany>().Where(g => g.RowId is >= 1 and <= 3 && g.MonsterNote.RowId <= 11))
            huntingLogs.Add(new HuntingLogRow((int)company.MonsterNote.RowId, 0, company.RowId, company.Name.ExtractText(), HuntingNotes(monsterNotes, company.RowId * 1000000, 3)));
        huntingLogs.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Rows 0 to 30; the highest row is the maximum level and row 30's ExpToNext is every extra level's price.
        var pvpSeriesLevels = excel.GetSheet<PvPSeriesLevel>().Select(l => new PvPSeriesLevelRow(l.RowId, l.ExpToNext)).OrderBy(l => l.RowId).ToList();

        // Every item whose sort category is 3 is a currency, 77 today, MGP (29) among them; the stack size is the holding
        // cap. The Content reader builds MGP from this list and the Money reader the rest. Each item's Icon is kept, which
        // the look draws beside the row.
        var currencies = items
            .Where(i => i.ItemSortCategory.RowId == 3 && i.Name.ExtractText().Length > 0)
            .Select(i => new CurrencyRow(i.RowId, i.Name.ExtractText(), (int)i.StackSize, i.Icon))
            .OrderBy(c => c.ItemId)
            .ToList();

        // The ContentType rows name the place groups (Eureka 26, Save the Queen 29 and Occult Crescent 38, as
        // their duties already do, and Deep Dungeons 21); Island Sanctuary is row 36 in the game's ContentType sheet.
        // Every named row is kept, 36 on this game version.
        var contentTypes = excel.GetSheet<ContentType>()
            .Where(t => t.Name.ExtractText().Length > 0)
            .Select(t => new ContentTypeRow(t.RowId, t.Name.ExtractText()))
            .OrderBy(t => t.RowId)
            .ToList();

        // MKDSupportJob's 24 named rows, 0 "Phantom Freelancer" to 23 "Phantom Necromancer", each with its own
        // LevelMax, and the JobIndex the order rule (10 + JobIndex) reads; JobIndex runs 1 to 24, Knight, Monk and Thief first,
        // read from the game's MKDSupportJob sheet, rows 0 to 23. Row 0 is a real row here.
        var phantomJobs = excel.GetSheet<MKDSupportJob>()
            .Where(j => j.Name.ExtractText().Length > 0)
            .Select(j => new PhantomJobRow(j.RowId, j.Name.ExtractText(), j.LevelMax, j.JobIndex))
            .OrderBy(j => j.RowId)
            .ToList();

        // WKSCosmoToolClass rows 1 to 11, Carpenter to Fisher, each listing 20 stage items; the name is the tool's
        // own WKSCosmoToolName row, "carpenter's tool" to "fisher's tool", the game's WKSCosmoToolName rows 3001 to 3011.
        // Row 0 names nothing and is no tool.
        var cosmicClasses = excel.GetSheet<WKSCosmoToolClass>()
            .Where(c => c.RowId != 0 && c.Name.RowId != 0)
            .Select(c => new CosmicClassRow(c.RowId, c.Name.ValueNullable?.Name.ExtractText() ?? "", c.Stages.Count(s => s.Item.RowId != 0)))
            .Where(c => c.Name.Length > 0)
            .OrderBy(c => c.RowId)
            .ToList();

        // MJIRank rows 0 to 20; ExpToNext is the step from that rank to the next and 0 at the cap.
        var islandRanks = excel.GetSheet<MJIRank>().Select(r => new IslandRankRow(r.RowId, (int)r.ExpToNext)).OrderBy(r => r.RowId).ToList();

        // DeepDungeon rows 1 to 4, each linking its aetherpool arm and armour into DeepDungeonEquipment, which
        // holds their names ("Aetherpool Arm", "Empyrean …", "Orthos …", "Illumed …"), the game's DeepDungeonEquipment rows
        // 1 to 8.
        var deepDungeons = excel.GetSheet<DeepDungeon>()
            .Where(d => d.Name.ExtractText().Length > 0)
            .Select(d => new DeepDungeonRow(d.RowId, d.Name.ExtractText(),
                d.AetherpoolArm.ValueNullable?.Name.ExtractText() ?? "", d.AetherpoolArmor.ValueNullable?.Name.ExtractText() ?? ""))
            .OrderBy(d => d.RowId)
            .ToList();

        // The MainCommand rows that name the Collections groups and six kinds, and the Money Currency group and three
        // storage rows, in the client's language; only the fifteen CollectionIds and MoneyIds names are kept.
        var commands = excel.GetSheet<MainCommand>();
        var mainCommands = CollectionIds.Commands.Concat(MoneyIds.Commands)
            .Select(id => commands.GetRowOrDefault(id) is { } command ? new MainCommandRow(id, command.Name.ExtractText()) : null)
            .OfType<MainCommandRow>()
            .Where(c => c.Name.Length > 0)
            .OrderBy(c => c.RowId)
            .ToList();

        // The Legacy kind (13) was only attainable in the defunct 1.x version, so a title granted only by
        // Legacy achievements leaves the title total, 51 of the 885 named.
        var achievementCategorySheet = excel.GetSheet<AchievementCategory>();
        bool Legacy(Achievement a) => a.AchievementCategory.RowId != 0
            && achievementCategorySheet.GetRowOrDefault(a.AchievementCategory.RowId) is { } category && category.AchievementKind.RowId == LegacyKind;
        var legacyOnlyTitles = excel.GetSheet<Achievement>().Where(a => a.Title.RowId != 0).GroupBy(a => a.Title.RowId)
            .Where(g => g.All(Legacy)).Select(g => g.Key).ToHashSet();

        // What each collection kind's total counts, in the Collection group's order.
        //   - Order is -1 on every placeholder, and 0 is a real entry for mounts (the company chocobo) and ornaments (the parasol),
        //     so those two keep Order 0 and up; minions and bardings keep Order above 0 (row 0, the Leather Saddle, is the default).
        //   - An emote counts only with a valid category and an unlock link: the 100 with link 0 are the starting set, which
        //     Dalamud reads as owned on every character.
        //   - A hairstyle is an unlock link over the purchasable rows below 2400; 2400 and up are face paints.
        //   - A facewear style counts when an item with the Glasses action (37312) reaches it through its Glasses row: 61 of 62.
        //   - A card counts when its resident row's Order is above 0; row 0 is named "0".
        var glasses = excel.GetSheet<Glasses>();
        var residents = excel.GetSheet<TripleTriadCardResident>();
        var collections = new List<CollectionKindRow>
        {
            new(CollectionIds.Mount, Sorted(excel.GetSheet<Mount>().Where(m => m.Singular.ExtractText().Length > 0 && m.Order >= 0).Select(m => m.RowId))),
            new(CollectionIds.Minion, Sorted(excel.GetSheet<Companion>().Where(c => c.Singular.ExtractText().Length > 0 && c.Order > 0).Select(c => c.RowId))),
            new(CollectionIds.Emote, Sorted(excel.GetSheet<Emote>().Where(e => e.Name.ExtractText().Length > 0 && e.EmoteCategory.RowId != 0 && e.EmoteCategory.IsValid && e.UnlockLink != 0).Select(e => e.RowId))),
            new(CollectionIds.Barding, Sorted(excel.GetSheet<BuddyEquip>().Where(b => b.Name.ExtractText().Length > 0 && b.Order > 0).Select(b => b.RowId))),
            new(CollectionIds.Hairstyle, Sorted(excel.GetSheet<CharaMakeCustomize>().Where(c => c.IsPurchasable && c.RowId < HairstyleRowEnd).Select(c => (uint)c.UnlockLink))),
            new(CollectionIds.Facewear, Sorted(items.Where(i => ItemActionType(i) == GlassesAction).Select(i => glasses.GetRowOrDefault(i.AdditionalData.RowId)?.Style.RowId).OfType<uint>())),
            new(CollectionIds.Ornament, Sorted(excel.GetSheet<Ornament>().Where(o => o.Singular.ExtractText().Length > 0 && o.Order >= 0).Select(o => o.RowId))),
            new(CollectionIds.Orchestrion, Sorted(excel.GetSheet<Orchestrion>().Where(o => o.Name.ExtractText().Length > 0).Select(o => o.RowId))),
            new(CollectionIds.Card, Sorted(excel.GetSheet<TripleTriadCard>().Where(c => residents.GetRowOrDefault(c.RowId) is { Order: > 0 }).Select(c => c.RowId))),
            // The items whose ItemAction's action is the framer's kit (29459), 348. The kit id is each item's
            // AdditionalData, which IUnlockState.IsItemUnlocked follows itself, so the item is the row.
            new(CollectionIds.FramerKit, Sorted(items.Where(i => ItemActionType(i) == FramersKitAction).Select(i => i.RowId))),
            new(CollectionIds.Title, Sorted(excel.GetSheet<Title>().Where(t => t.Masculine.ExtractText().Length > 0 && !legacyOnlyTitles.Contains(t.RowId)).Select(t => t.RowId))),
        };

        // The recipes below 30000 with a result, per class (8 + CraftType, Carpenter to Culinarian). The client's
        // completion array covers exactly these, so the master, society, delivery, sidequest, collectable and class recipes at
        // 30000 and up leave every total until it is confirmed the client records them.
        var logs = excel.GetSheet<Recipe>()
            .Where(r => r.RowId < CollectionIds.RecipeLogEnd && r.ItemResult.RowId != 0 && r.CraftType.RowId <= CollectionIds.LastCrafter - CollectionIds.FirstCrafter)
            .GroupBy(r => CollectionIds.FirstCrafter + r.CraftType.RowId)
            .OrderBy(g => g.Key)
            .Select(g => new LogRow(CollectionIds.Craft, g.Key, Sorted(g.Select(r => r.RowId))))
            .ToList();

        // The gathering log is the notebook's lists, not the item sheet: each list names its items in its first
        // Unknown0 slots. A list below 2000 is 40 × gathering type + level division, and one from 2000 up takes its type from
        // the id modulo 4: taken to be so; not yet confirmed. Types 0 and 1 are Miner's, 2 and 3 Botanist's. 551 and 692 items,
        // 90 in both.
        var gathered = new Dictionary<uint, List<uint>> { [CollectionIds.Miner] = [], [CollectionIds.Botanist] = [] };
        foreach (var list in excel.GetSheet<GatheringNotebookList>())
        {
            var type = list.RowId < SpecialGatheringLists ? list.RowId / GatheringTypeStride : list.RowId % 4;
            if (type > 3) continue;
            gathered[type <= 1 ? CollectionIds.Miner : CollectionIds.Botanist].AddRange(list.GatheringItem.Take(list.Unknown0).Select(g => g.RowId));
        }
        logs.Add(new LogRow(CollectionIds.Gather, CollectionIds.Miner, Sorted(gathered[CollectionIds.Miner])));
        logs.Add(new LogRow(CollectionIds.Gather, CollectionIds.Botanist, Sorted(gathered[CollectionIds.Botanist])));

        // The fishing log is the FishParameter rows in the log with an item, 1,517 (row 0 has neither item nor
        // spot). The spearfishing log is the SpearfishingItem rows below 30000 that are visible and have an item, 289 (30000 and up
        // are timeworn maps and the like). Both belong to Fisher.
        logs.Add(new LogRow(CollectionIds.Fish, CollectionIds.Fisher, Sorted(excel.GetSheet<FishParameter>().Where(f => f.IsInLog && f.Item.RowId != 0).Select(f => f.RowId))));
        logs.Add(new LogRow(CollectionIds.Spearfish, CollectionIds.Fisher,
            Sorted(excel.GetSheet<SpearfishingItem>().Where(s => s.RowId < SpearfishRowEnd && s.IsVisible && s.Item.RowId != 0).Select(s => s.RowId))));

        // The log split by the ExVersion of each vista's Level territory, blocks of 80, 62, 62, 45, 46 and 45.
        // AdventureExPhase names the quest that opens each block from Stormblood on, its lowest phase first: Dawntrail's is 70542,
        // whose phase 4, 70543, adds the last 17, read from the game's AdventureExPhase rows 0 to 4.
        var exVersions = excel.GetSheet<ExVersion>();
        var phaseQuests = excel.GetSheet<AdventureExPhase>().OrderBy(p => p.RowId).GroupBy(p => p.Expansion.RowId).ToDictionary(g => g.Key, g => g.First().Quest.RowId);
        var sightseeingLogs = excel.GetSheet<Adventure>()
            .Select(a => (a.RowId, Expansion: a.Level.ValueNullable?.Territory.ValueNullable?.ExVersion.RowId))
            .Where(a => a.Expansion != null)
            .GroupBy(a => a.Expansion!.Value)
            .OrderBy(g => g.Key)
            .Select(g => new SightseeingRow(g.Key, exVersions.GetRowOrDefault(g.Key)?.Name.ExtractText() ?? "", phaseQuests.GetValueOrDefault(g.Key), Sorted(g.Select(a => a.RowId))))
            .ToList();

        // One row per achievement category over its named achievements. The Legacy kind and the Ranking category
        // (per-season placings) leave, which gives 3,652 achievements in 73 categories; Seasonal Events and the achievements
        // hidden until done stay.
        var achievementKinds = excel.GetSheet<AchievementKind>();
        var achievementCategories = new List<AchievementCategoryRow>();
        foreach (var group in excel.GetSheet<Achievement>().Where(a => a.Name.ExtractText().Length > 0 && a.AchievementCategory.RowId != 0).GroupBy(a => a.AchievementCategory.RowId))
        {
            if (achievementCategorySheet.GetRowOrDefault(group.Key) is not { } category) continue;
            if (category.AchievementKind.RowId == LegacyKind || category.RowId == RankingCategory) continue;
            var kind = achievementKinds.GetRowOrDefault(category.AchievementKind.RowId);
            achievementCategories.Add(new AchievementCategoryRow(category.RowId, category.Name.ExtractText(), category.Order, category.AchievementKind.RowId,
                kind?.Name.ExtractText() ?? "", kind?.Order ?? 0, Sorted(group.Select(a => a.RowId))));
        }
        achievementCategories.Sort((a, b) => a.KindOrder != b.KindOrder ? a.KindOrder.CompareTo(b.KindOrder) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.RowId.CompareTo(b.RowId));

        // PvPRank's ExpRequired, rising to 226,210 at row 50 and 0 from 51 to 100, and PvPRankTransient's band
        // titles. Its first three unnamed columns are the Maelstrom's, the Twin Adder's and the Immortal Flames' ("Storm …",
        // "Serpent …", "Flame …"), and the other three repeat them, read from the game's PvPRankTransient rows 0 to 59.
        var transients = excel.GetSheet<PvPRankTransient>();
        var pvpRanks = excel.GetSheet<PvPRank>()
            .Select(r => new PvPRankRow(r.RowId, (int)r.ExpRequired, RankTitles(transients.GetRowOrDefault(r.RowId))))
            .OrderBy(r => r.RowId)
            .ToList();

        // ColosseumMatchRank's one unnamed column names the tiers, "-" at row 0, then Bronze to Ultima.
        var colosseumRanks = excel.GetSheet<ColosseumMatchRank>().Select(r => new ColosseumRankRow(r.RowId, r.Unknown0.ExtractText())).OrderBy(r => r.RowId).ToList();

        // Every TomestonesItem row with an item, 24, with its Tomestones row. Only the four whose Tomestones row is not 0
        // can be held; the other 20 share the live ones' currency slots or have none, and older tomestones are converted to Poetics.
        var tomestoneItems = excel.GetSheet<TomestonesItem>()
            .Where(t => t.Item.RowId != 0)
            .Select(t => new TomestoneItemRow(t.Item.RowId, t.Tomestones.RowId))
            .OrderBy(t => t.ItemId)
            .ToList();

        // The Addon rows that name the Money groups, storage rows and retainer field words, in the client's language;
        // only the nine MoneyIds names are kept.
        var addonSheet = excel.GetSheet<Addon>();
        var addons = MoneyIds.Addons
            .Select(id => addonSheet.GetRowOrDefault(id) is { } addon ? new AddonRow(id, addon.Text.ExtractText()) : null)
            .OfType<AddonRow>()
            .Where(a => a.Text.Length > 0)
            .OrderBy(a => a.RowId)
            .ToList();

        // The Cabinet rows that name an item, 3,844, with ids below 3,850; the armoire holds items, not slots, so these
        // are the total its row counts over. The rest of the sheet's 5,048 rows are empty.
        var armoireRows = excel.GetSheet<Cabinet>().Where(c => c.Item.RowId != 0).Select(c => c.RowId).OrderBy(id => id).ToList();

        return new CatalogRows(gameVersion, now.ToString("u"), quests, jobs, zones, tribes, companies, aozActions,
            roulettes, huntOrderTypes, satisfactionNpcs, tomestones, challengeEntries, lockouts, troveItems,
            duties, variantSeries, huntingLogs, pvpSeriesLevels, currencies,
            contentTypes, phantomJobs, cosmicClasses, islandRanks, deepDungeons,
            mainCommands, collections, logs, sightseeingLogs, achievementCategories, pvpRanks, colosseumRanks,
            tomestoneItems, addons, armoireRows);
    }

    /// <summary>The ContentType rows the Content category tracks, by link type, and the three whose rows need
    /// a Duty Finder heading to count.</summary>
    private static readonly HashSet<uint> InstanceDutyTypes = [2, 3, 4, 5, 27, 28, 30, 37];
    private static readonly HashSet<uint> HeadedDutyTypes = [2, 4, 5];
    private static readonly HashSet<uint> FieldDutyTypes = [26, 29, 38];

    /// <summary>The row boundaries and ids the Collections reductions turn on: hairstyles below row 2400,
    /// spearfish below 30000, the special gathering lists from 2000, 40 lists per gathering type below that, the Glasses (37312)
    /// and framer's kit (29459) item actions, the Legacy achievement kind (13) and the Ranking category (8).</summary>
    private const uint HairstyleRowEnd = 2400, SpearfishRowEnd = 30000, SpecialGatheringLists = 2000, GatheringTypeStride = 40,
        GlassesAction = 37312, FramersKitAction = 29459, LegacyKind = 13, RankingCategory = 8;

    /// <summary>The action an item's ItemAction row names, 0 when it has none.</summary>
    private static uint ItemActionType(Item item) => item.ItemAction.RowId == 0 ? 0 : item.ItemAction.ValueNullable?.Action.RowId ?? 0;

    /// <summary>Row ids, each once and in order, so a fixture diff reads as a change of rows rather than of order.</summary>
    private static List<uint> Sorted(IEnumerable<uint> ids) => ids.Distinct().OrderBy(id => id).ToList();

    /// <summary>A PvP rank's band title in each company's column, the first three of PvPRankTransient's six.</summary>
    private static IReadOnlyList<string> RankTitles(PvPRankTransient? row) =>
        row is { } t ? [t.Unknown0.ExtractText(), t.Unknown1.ExtractText(), t.Unknown2.ExtractText()] : [];

    private static bool DutyInScope(ContentFinderCondition c) => c.ContentLinkType switch
    {
        1 => InstanceDutyTypes.Contains(c.ContentType.RowId) && (!HeadedDutyTypes.Contains(c.ContentType.RowId) || c.ContentUICategory.RowId != 0),
        3 => FieldDutyTypes.Contains(c.ContentType.RowId),
        _ => false,
    };

    /// <summary>The notes of one log, rank by rank, each with the kills its targets need. A note row the sheet
    /// does not carry is reduced with no kills, so the note keeps its place in the rank and ContentRules.HuntingLogDone never
    /// counts it done; none is missing on this game version, which the fixture test pins.</summary>
    private static IReadOnlyList<HuntingNoteRow> HuntingNotes(ExcelSheet<MonsterNote> notes, uint baseRow, int ranks)
    {
        var list = new List<HuntingNoteRow>(ranks * 10);
        for (var rank = 0; rank < ranks; rank++)
        {
            for (var note = 0; note < 10; note++)
            {
                var row = notes.GetRowOrDefault(baseRow + (uint)(rank * 10 + note + 1));
                list.Add(new HuntingNoteRow(rank, note, row is { } r ? r.Count.Select(c => (int)c).ToList() : []));
            }
        }
        return list;
    }

    /// <summary>The InstanceContent ids of the conditions flagged for one roulette, in ContentFinderCondition row order. A
    /// roulette with no flag column of its own (none of the eleven today) has no members, and its gate then cannot be
    /// judged from the sheet, which the reader treats as unreadable rather than locked.</summary>
    private static IReadOnlyList<uint> RouletteMembers(uint rouletteId, IReadOnlyList<ContentFinderCondition> conditions)
    {
        var flagged = RouletteFlag(rouletteId);
        return flagged == null ? [] : conditions.Where(flagged).Select(c => c.Content.RowId).ToList();
    }

    /// <summary>The ContentRoulette row to the ContentFinderCondition column that marks its members.</summary>
    private static Func<ContentFinderCondition, bool>? RouletteFlag(uint rouletteId) => rouletteId switch
    {
        1 => c => c.LevelingRoulette,
        2 => c => c.HighLevelRoulette,
        3 => c => c.MSQRoulette,
        4 => c => c.GuildHestRoulette,
        5 => c => c.ExpertRoulette,
        6 => c => c.TrialRoulette,
        7 => c => c.DailyFrontlineChallenge,
        8 => c => c.LevelCapRoulette,
        9 => c => c.MentorRoulette,
        15 => c => c.AllianceRoulette,
        17 => c => c.NormalRaidRoulette,
        _ => null,
    };
}
