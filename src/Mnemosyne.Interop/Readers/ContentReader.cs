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

/// <summary>The seven families of Content rows this reader covers. One reader instance per family, so a moved
/// member behind one of them disables that family alone, and each names only the triggers its rows move on.</summary>
public enum ContentFamily { Duties, HuntingLog, Housing, GoldSaucer, Pvp, HouseVisit, Racing }

/// <summary>The Content rows whose values are in memory once the player state is loaded, plus the PvP rows behind the
/// PvP Profile window. Duties and the Variant records run at login, after an unlock or a finished duty, and on a territory change,
/// never on the tick: 440 rows are two bit reads each, and nothing changes them between those events. The territory
/// trigger re-reads on the way out of any duty, so a completion bit that lags the duty-completed event by more than a tick is
/// still caught the moment the character leaves, rather than waiting for the next login or unlock. The hunting
/// log and MGP run on the tick, because kills and MGP move during play. The estates run at login, on a territory change, where
/// a purchase or a sale is seen, and on the tick, because nothing says when the ownership table fills after login. The PvP rows
/// follow WindowReader: a locked gate stores a gate lock, an unloaded profile sends an open gate, a loaded one sends the values.
/// Every gate follows the rule that a locked row clears and an unreadable one keeps what was stored, and every pointer read sits
/// in a private static unsafe helper, or in the shared HeldItems.Held for MGP, behind the player-state guard. Nothing here writes,
/// requests, opens or moves anything: HousingManager.MoveToEntry, AgentMonsterNote.OpenWithData and the ContentUI loaders are
/// never called. The house-visit timer is stamped while the character stands inside their own personal estate, and the racing
/// chocobo rank is read behind the Gold Saucer window, each its own family; both gates can close again, so a closed one clears
/// and then locks, and each is believed closed only on a second closed read at least a second after the first. The PvP family
/// also carries the Crystalline Conflict tier and the Grand Company PvP ranks, read from the same profile behind the same
/// gate.</summary>
public sealed class ContentReader(ContentFamily family, Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    private readonly IReadOnlyList<ItemDefinition> _items = catalog.InCategory(Category.Content).Where(i => Prefixes(family).Any(p => i.Id.StartsWith(p, StringComparison.Ordinal))).ToList();
    // Each family builds only the lookups it reads.
    private readonly Dictionary<uint, DutyRow> _duties = family == ContentFamily.Duties ? rows.Duties.ToDictionary(d => d.RowId) : new Dictionary<uint, DutyRow>();
    private readonly Dictionary<uint, VariantSeriesRow> _series = family == ContentFamily.Duties ? rows.VariantSeries.ToDictionary(s => s.RowId) : new Dictionary<uint, VariantSeriesRow>();
    private readonly Dictionary<uint, uint> _seriesDungeon = family == ContentFamily.Duties ? extras.VariantSeries.ToDictionary(v => v.SeriesId, v => v.ContentId) : new Dictionary<uint, uint>();
    private readonly Dictionary<uint, HuntingLogRow> _classLogs = family == ContentFamily.HuntingLog ? rows.HuntingLogs.Where(h => h.ClassJobId != 0).ToDictionary(h => h.ClassJobId) : new Dictionary<uint, HuntingLogRow>();
    private readonly Dictionary<uint, HuntingLogRow> _companyLogs = family == ContentFamily.HuntingLog ? rows.HuntingLogs.Where(h => h.GrandCompanyId != 0).ToDictionary(h => h.GrandCompanyId) : new Dictionary<uint, HuntingLogRow>();
    private readonly Dictionary<uint, CurrencyRow> _currencies = family == ContentFamily.GoldSaucer ? rows.Currencies.ToDictionary(c => c.ItemId) : new Dictionary<uint, CurrencyRow>();
    private bool _housingReported, _housingReportedOwned, _pvpUnloadedReported, _pvpLoadedReported;
    private ulong _lastIndoorReported;
    private string? _racingReported;
    // The character the two log-once fields above were last reported for: a switch clears them, so the
    // new character's first read still logs its evidence line, rather than being silently suppressed by a match with the
    // previous character. Shared because only one of the two families this covers, HouseVisit or Racing, runs per instance.
    private ulong _reportedOwner;
    // The character the PvP rank fields were last logged for: a switch logs the new character's once.
    private ulong _ranksReportedFor;
    // The two gates that can close again, the personal estate and the racing chocobo, each keyed on the character it reads:
    // a read for a different content id starts its count over, so a character just switched to can never have its first
    // closed read believed on a count the previous character had already run up to two.
    private readonly RevocableGateState _noEstateGate = new(), _noChocoboGate = new();

    /// <summary>PlayerState.GoldSaucerContentStatus flag 2, "Has Race Chocobo" (flag 1 is "Chocobo Race Unlocked"), from
    /// FFXIVClientStructs' PlayerState, the field's remarks.</summary>
    private const byte HasRaceChocobo = 2;

    /// <summary>The client's per-content unlock and completion bits, pure statics over the linked content row, held as
    /// delegates once so the walk allocates nothing. FFXIVClientStructs' UIState: IsInstanceContentUnlocked/Completed(uint
    /// instanceContentId), IsPublicContentUnlocked/Completed(uint publicContentId).</summary>
    private static readonly Func<uint, bool> InstanceUnlocked = UIState.IsInstanceContentUnlocked;
    private static readonly Func<uint, bool> InstanceCompleted = UIState.IsInstanceContentCompleted;
    private static readonly Func<uint, bool> PublicUnlocked = UIState.IsPublicContentUnlocked;
    private static readonly Func<uint, bool> PublicCompleted = UIState.IsPublicContentCompleted;

    /// <summary>"no house" is TerritoryTypeId 0xFFFF, taken to be so from other plugins reading housing; not yet confirmed
    /// independently.</summary>
    private const ushort NoHouse = 0xFFFF;

    public string Name => $"ContentReader({family})";
    public Category Category => Category.Content;
    public ReaderTrigger Triggers => family switch
    {
        ContentFamily.Duties => ReaderTrigger.Login | ReaderTrigger.Unlock | ReaderTrigger.DutyCompleted | ReaderTrigger.Territory,
        ContentFamily.HuntingLog => ReaderTrigger.Login | ReaderTrigger.Interval,
        ContentFamily.Housing => ReaderTrigger.Login | ReaderTrigger.Territory | ReaderTrigger.Interval,
        ContentFamily.GoldSaucer => ReaderTrigger.Login | ReaderTrigger.Interval,
        ContentFamily.Pvp => ReaderTrigger.Login | ReaderTrigger.Interval | ReaderTrigger.Window,
        ContentFamily.HouseVisit => ReaderTrigger.Login | ReaderTrigger.Territory | ReaderTrigger.Interval,
        ContentFamily.Racing => ReaderTrigger.Login | ReaderTrigger.Interval | ReaderTrigger.Window,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown content family"),
    };

    public IReadOnlyList<Reading>? Read()
    {
        // Nothing is read while the player state is not loaded. Every gate below answers "locked" before it.
        if (!player.IsLoaded) return null;
        var now = clock.UtcNow;
        return family switch
        {
            ContentFamily.Duties => Duties(now),
            ContentFamily.HuntingLog => HuntingLog(now),
            ContentFamily.Housing => Housing(),
            ContentFamily.GoldSaucer => GoldSaucer(now),
            ContentFamily.Pvp => Pvp(now),
            ContentFamily.HouseVisit => HouseVisit(now),
            ContentFamily.Racing => Racing(now),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown content family"),
        };
    }

    /// <summary>The id families each instance fills.</summary>
    private static string[] Prefixes(ContentFamily family) => family switch
    {
        ContentFamily.Duties => ["duty:", "vvd:"],
        ContentFamily.HuntingLog => ["hunt:"],
        ContentFamily.Housing => ["housing:"],
        ContentFamily.GoldSaucer => ["currency:"],
        ContentFamily.Pvp => ["pvp:"],
        ContentFamily.HouseVisit => [CatalogBuilder.HouseVisitId],
        ContentFamily.Racing => [CatalogBuilder.RacingId],
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "unknown content family"),
    };

    /// <summary>Every duty on its two bits, and every Variant series on its record bits. The statics read through the client's UI
    /// state, so nothing is judged while that is absent. A duty's row clears when locked, is done when its completion bit
    /// is set whatever its unlock bit says, and is not done otherwise. A Variant series is gated on its own dungeon's
    /// unlock through the hand file's map; a series the map does not name is read ungated, as the builder said.</summary>
    private IReadOnlyList<Reading>? Duties(DateTimeOffset now)
    {
        if (!UiStatePresent()) return null;
        var records = VariantRecordBits();
        var readings = new List<Reading>(_items.Count);
        foreach (var item in _items)
        {
            var parts = item.Id.Split(':');
            if (parts.Length != 2 || !uint.TryParse(parts[1], out var row)) continue;
            switch (parts[0])
            {
                case "duty" when _duties.TryGetValue(row, out var duty):
                {
                    var (unlocked, completed) = duty.LinkType switch
                    {
                        1 => (InstanceUnlocked, InstanceCompleted),
                        3 => (PublicUnlocked, PublicCompleted),
                        _ => ((Func<uint, bool>?)null, (Func<uint, bool>?)null),
                    };
                    if (unlocked == null || completed == null) break;   // no completion read for this link type: not judged
                    switch (ContentRules.Duty(unlocked(duty.ContentId), completed(duty.ContentId)))
                    {
                        case DutyState.Locked: readings.Add(new Reading(item.Id, null)); break;
                        case DutyState.NotDone: readings.Add(Readings.Done(item, false, now)); break;
                        case DutyState.Done: readings.Add(Readings.Done(item, true, now)); break;
                    }
                    break;
                }
                case "vvd" when _series.TryGetValue(row, out var series):
                {
                    if (_seriesDungeon.TryGetValue(row, out var dungeon) && !InstanceUnlocked(dungeon)) { readings.Add(new Reading(item.Id, null)); break; }
                    if (records == null) break;
                    readings.Add(new Reading(item.Id, new CounterValue(ContentRules.VariantRecords(series.ContentsIds, records), series.ContentsIds.Count, now)));
                    break;
                }
            }
        }
        return readings;
    }

    /// <summary>Whether the client's UI state exists, which the per-content statics read through.</summary>
    private static unsafe bool UiStatePresent() => UIState.Instance() != null;

    /// <summary>The 49 Variant record bits on the player state, one per VVDNotebookContents row at row id minus
    /// one, copied into managed memory so the rule can ask for them without a pointer. Null when the state is absent; a bit outside
    /// the array answers null and is not counted. FFXIVClientStructs' PlayerState.CompletedVVDNotebookContentsBitArray, whose
    /// BitArray exposes BitCount and TryGet(int, out bool).</summary>
    private static unsafe Func<int, bool?>? VariantRecordBits()
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        var array = state->CompletedVVDNotebookContentsBitArray;
        var bits = new bool?[array.BitCount];
        for (var i = 0; i < bits.Length; i++) bits[i] = array.TryGet(i, out var set) ? set : null;
        return i => i >= 0 && i < bits.Length ? bits[i] : null;
    }

    /// <summary>Twelve Counters over the monster note manager's twelve rank blocks, index for index. Gate: a class at level 1
    /// or above for a class log, a company rank of 1 or above for a company log, through the service; a class or company whose sheet
    /// row does not resolve is not judged. The manager is a direct static, taken to be filled at login; Rank is taken to be
    /// zero-based; neither is yet confirmed against the game's own log.</summary>
    private IReadOnlyList<Reading> HuntingLog(DateTimeOffset now)
    {
        var jobs = data.GetExcelSheet<ClassJob>();
        var companies = data.GetExcelSheet<GrandCompany>();
        var manager = HuntingLogSnapshot();
        var readings = new List<Reading>(_items.Count);
        foreach (var item in _items)
        {
            var parts = item.Id.Split(':');
            if (parts.Length != 3 || !uint.TryParse(parts[2], out var row)) continue;
            var log = parts[1] switch { "class" => _classLogs.GetValueOrDefault(row), "gc" => _companyLogs.GetValueOrDefault(row), _ => null };
            if (log == null) continue;
            var open = log.ClassJobId != 0
                ? jobs.GetRowOrDefault(log.ClassJobId) is { } job ? player.GetClassJobLevel(job) >= 1 : (bool?)null
                : companies.GetRowOrDefault(log.GrandCompanyId) is { } company ? player.GetGrandCompanyRank(company) >= 1 : (bool?)null;
            if (open == null) continue;
            if (open == false) { readings.Add(new Reading(item.Id, null)); continue; }
            if (manager == null || log.Index < 0 || log.Index >= manager.Length) continue;
            var block = manager[log.Index];
            var ranks = log.Notes.Count / 10;
            var done = ContentRules.HuntingLogDone(block.Rank, log.Notes, ranks, (note, target) => block.Kills[note * 4 + target]);
            readings.Add(new Reading(item.Id, new CounterValue(done, ranks * 10, now)));
        }
        return readings;
    }

    /// <summary>One rank block of the hunting log as the client holds it: the zero-based rank in progress and the kills of the ten
    /// notes' four target slots, flattened note-major.</summary>
    private readonly record struct HuntingBlock(int Rank, int[] Kills);

    /// <summary>MonsterNoteManager.Instance() is a direct static address; RankData holds twelve MonsterNoteRankInfo,
    /// each with ten RankData entries of four byte Counts, a long Flags, an int Rank and an int Index. Copied out in one read so the
    /// twelve rows come from one frame. FFXIVClientStructs' MonsterNoteManager, MonsterNoteRankInfo, RankData.
    /// AgentMonsterNote.OpenWithData is never called.</summary>
    private static unsafe HuntingBlock[]? HuntingLogSnapshot()
    {
        var manager = MonsterNoteManager.Instance();
        if (manager == null) return null;
        var blocks = new HuntingBlock[manager->RankData.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            ref var info = ref manager->RankData[i];
            var kills = new int[info.RankData.Length * 4];
            for (var note = 0; note < info.RankData.Length; note++)
            {
                var counts = info.RankData[note].Counts;
                for (var target = 0; target < 4 && target < counts.Length; target++) kills[note * 4 + target] = counts[target];
            }
            blocks[i] = new HuntingBlock(info.Rank, kills);
        }
        return blocks;
    }

    /// <summary>Four Done rows on the static ownership call, one per estate type. An owned estate reads done as
    /// "&lt;estate&gt;: &lt;district&gt;, ward n, plot m" (the room for an apartment or for chambers); "none" clears the row and its
    /// label, which is also how a sold or demolished estate leaves the list, so this one non-monotonic AtLogin gate is served
    /// exactly by a clearing reading. Nothing says when the table behind the static fills after login, so the family also
    /// runs on the tick, and an all-zero id is read as "not filled yet" and keeps whatever is stored, never as an owned estate in
    /// territory 0 and never as none: taken to be so; not yet confirmed. Only the 0xFFFF sentinel clears. The raw ids are logged
    /// on the first read and again on the first read that finds any estate, showing both states; they are formatted only on a
    /// read that is logged, because on a character who owns nothing every tick would otherwise format four lines for nothing.</summary>
    private IReadOnlyList<Reading> Housing()
    {
        var territories = data.GetExcelSheet<TerritoryType>();
        var readings = new List<Reading>(_items.Count);
        var answers = _housingReportedOwned ? null : new List<(string Id, OwnedEstate House)>(_items.Count);
        var anyOwned = false;
        foreach (var item in _items)
        {
            var type = item.Id switch
            {
                "housing:personal" => EstateType.PersonalEstate,
                "housing:fc" => EstateType.FreeCompanyEstate,
                "housing:apartment" => EstateType.ApartmentRoom,
                "housing:chambers" => EstateType.PersonalChambers,
                _ => (EstateType?)null,
            };
            if (type is not { } estate) continue;
            var house = OwnedHouse(estate);
            answers?.Add((item.Id, house));
            if (house.Id == 0) continue;                                                                    // not filled yet: not judged
            if (house.TerritoryTypeId == NoHouse) { readings.Add(new Reading(item.Id, null)); continue; }   // none: the row clears
            anyOwned = true;
            var district = territories.GetRowOrDefault(house.TerritoryTypeId)?.PlaceName.ValueNullable?.Name.ExtractText() ?? $"territory {house.TerritoryTypeId}";
            readings.Add(new Reading(item.Id, new DoneValue(true), Label: ContentRules.HouseLabel(item.Name, district, house.WardIndex, house.PlotIndex, house.IsApartment, house.RoomNumber)));
        }
        if (answers is { Count: > 0 } && (!_housingReported || anyOwned))
        {
            _housingReported = true;
            _housingReportedOwned = anyOwned;
            log($"housing ({(anyOwned ? "an estate found" : "none found")}): " + string.Join("; ", answers.Select(a =>
                $"{a.Id} id=0x{a.House.Id:X16} territory={a.House.TerritoryTypeId} ward={a.House.WardIndex} plot={a.House.PlotIndex} room={a.House.RoomNumber}")));
        }
        return readings;
    }

    /// <summary>One estate's owned id, copied out of the static call, which needs no HousingManager instance and touches nothing.
    /// FFXIVClientStructs' HousingManager: public static HouseId GetOwnedHouseId(EstateType type, int sharedEstateIndex = -1);
    /// HouseId.Id (ulong), .TerritoryTypeId (ushort), .WorldId (ushort), .WardIndex (byte), .PlotIndex (byte), .IsApartment
    /// (bool), .RoomNumber (short); EstateType { FreeCompanyEstate, PersonalChambers, PersonalEstate, Unknown3, SharedEstate,
    /// ApartmentBuilding, ApartmentRoom }. That the apartment is ApartmentRoom is taken to be so; not yet confirmed. MoveToEntry
    /// is never called.</summary>
    private readonly record struct OwnedEstate(ulong Id, ushort TerritoryTypeId, ushort WorldId, byte WardIndex, byte PlotIndex, bool IsApartment, short RoomNumber);

    private static unsafe OwnedEstate OwnedHouse(EstateType type)
    {
        var id = HousingManager.GetOwnedHouseId(type);
        return new OwnedEstate(id.Id, id.TerritoryTypeId, id.WorldId, id.WardIndex, id.PlotIndex, id.IsApartment, id.RoomNumber);
    }

    /// <summary>The house-visit timer: stamped while the character stands inside their own personal estate, with the deadline the
    /// demolition period after the minute the character was seen there, and kept afterwards with "as of last visit to your
    /// house". The personal estate only: a free company estate is reset by any member, and an apartment is exempt. The gate is
    /// ownership, which can close again: no personal estate clears the deadline and then locks the row, but only on a second
    /// "none" read at least a second after the first counted one, because the deadline is a value no reader can read back and two
    /// reads of one frame must not count twice; an all-zero id is the ownership table not yet filled and says nothing. The count is
    /// kept per character, so a switch to a character with no personal estate can never believe its first
    /// login-frame sentinel on a count the previous character had already run up to two. The indoor id is compared with the owned
    /// one whole, or on world, ward and plot (ContentRules.SameHouse; taken to be so, not yet confirmed), and both ids are logged
    /// each time the indoor id changes, showing which.</summary>
    private IReadOnlyList<Reading> HouseVisit(DateTimeOffset now)
    {
        if (player.ContentId != _reportedOwner) { _reportedOwner = player.ContentId; _lastIndoorReported = 0; }   // a switch logs the new character's evidence again
        var owned = OwnedHouse(EstateType.PersonalEstate);
        // A "none" clears a stamp no reader can read back, so it is believed on a second read at a later instant, never on one frame.
        var none = owned.Id != 0 && owned.TerritoryTypeId == NoHouse;
        var open = _noEstateGate.Evaluate(player.ContentId, owned.Id == 0 ? (bool?)null : !none, now);
        TimerValue? stamp = null;
        if (open == true && CurrentIndoorHouse() is { } inside)
        {
            var match = ContentRules.SameHouse(new HouseKey(owned.Id, owned.WorldId, owned.WardIndex, owned.PlotIndex, owned.IsApartment), inside);
            if (inside.Id != _lastIndoorReported)
            {
                _lastIndoorReported = inside.Id;
                log($"house visit: owned id=0x{owned.Id:X16} world={owned.WorldId} ward={owned.WardIndex} plot={owned.PlotIndex}; inside id=0x{inside.Id:X16} world={inside.WorldId} ward={inside.WardIndex} plot={inside.PlotIndex} apartment={inside.IsApartment}; match {match}");
            }
            if (match != HouseMatch.None) stamp = new TimerValue(ContentRules.HouseVisitDeadline(now, extras.HouseVisit.DemolitionDays));
        }
        var readings = new List<Reading>(_items.Count * 2);
        foreach (var item in _items) readings.AddRange(Readings.Gated(item.Id, open, stamp, revocable: true));
        return readings;
    }

    /// <summary>The id of the house the character stands inside, copied out; null outside any house or while the housing manager is
    /// absent, which it is outside housing. FFXIVClientStructs' HousingManager: static HousingManager* Instance(), bool
    /// IsInside(), HouseId GetCurrentIndoorHouseId(); HouseId.WorldId (ushort). MoveToEntry is never called.</summary>
    private static unsafe HouseKey? CurrentIndoorHouse()
    {
        var manager = HousingManager.Instance();
        if (manager == null || !manager->IsInside()) return null;
        var id = manager->GetCurrentIndoorHouseId();
        return new HouseKey(id.Id, id.WorldId, id.WardIndex, id.PlotIndex, id.IsApartment);
    }

    /// <summary>The racing chocobo's rank: merged only while the racing manager reports Loaded, which the Gold Saucer
    /// window's Chocobo tab brings about; the Window trigger reads on the frame the window fires and the tick reads again. The gate is
    /// the player state's "Has Race Chocobo" flag, which a retired chocobo turns off, so a closed gate clears and then locks, believed
    /// only on a second "no chocobo" read at least a second after the first, as the house's "none" is: one window opening's several
    /// Post events count once, and the count is kept per character, so a switch to a character with no
    /// racing chocobo can never believe its first read on a count the previous character had already run up to two. The
    /// status and the manager's state are logged whenever either changes, showing the
    /// state at login, after the window and after the tab. Null when the player state struct is absent.</summary>
    private IReadOnlyList<Reading>? Racing(DateTimeOffset now)
    {
        if (player.ContentId != _reportedOwner) { _reportedOwner = player.ContentId; _racingReported = null; }   // a switch logs the new character's evidence again
        if (GoldSaucerStatus() is not { } status) return null;
        var hasChocobo = (status & HasRaceChocobo) != 0;
        // A "no chocobo" clears the stored rank, so it too is believed on a second read at a later instant.
        var open = _noChocoboGate.Evaluate(player.ContentId, hasChocobo, now);
        var race = RacingSnapshot();
        ReportRacing(status, race);
        var value = open == true && race is { Loaded: true } r ? ContentRules.Level(r.Rank, extras.ContentMaxima.RacingRank, r.Experience, r.ExperienceMax) : null;
        var readings = new List<Reading>(_items.Count * 2);
        foreach (var item in _items) readings.AddRange(Readings.Gated(item.Id, open, value, revocable: true));
        return readings;
    }

    private readonly record struct RaceState(bool Loaded, string State, int Rank, int Experience, int ExperienceMax);

    private static unsafe byte? GoldSaucerStatus()
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        return state->GoldSaucerContentStatus;
    }

    /// <summary>RaceChocoboManager.Instance() is a direct static address; State, Rank, ExperienceCurrent and
    /// ExperienceMax are plain fields. FFXIVClientStructs' RaceChocoboManager.
    /// GetAttributesCurrent/Maximum/Stars, GetRating and GetPedigreeLevel are never called.</summary>
    private static unsafe RaceState? RacingSnapshot()
    {
        var manager = RaceChocoboManager.Instance();
        if (manager == null) return null;
        return new RaceState(manager->State == RaceChocoboManager.RaceChocoboState.Loaded, manager->State.ToString(), manager->Rank, manager->ExperienceCurrent, manager->ExperienceMax);
    }

    private void ReportRacing(byte status, RaceState? race)
    {
        var said = $"{status}:{race?.State}";
        if (said == _racingReported) return;
        _racingReported = said;
        log($"racing: gold saucer status {status} (has chocobo {(status & HasRaceChocobo) != 0}); manager {(race is { } r ? $"{r.State}, rank {r.Rank}, experience {r.Experience}/{r.ExperienceMax}" : "absent")}");
    }

    /// <summary>MGP as a held counter over the item's stack size: the inventory manager's count of item 29, read as the
    /// trove reader reads its tomestone. Gate: the Gold Saucer's unlock quest; locked clears. A zero is a value once the Currency
    /// container reports loaded and nothing before, so a character with the Saucer unlocked and no MGP reads 0 rather than blank.</summary>
    private IReadOnlyList<Reading>? GoldSaucer(DateTimeOffset now)
    {
        var quests = data.GetExcelSheet<Quest>();
        var open = QuestOpen(extras.ContentUnlockQuests.GoldSaucer, quests);
        if (open == null) return null;
        var readings = new List<Reading>(_items.Count);
        foreach (var item in _items)
        {
            var parts = item.Id.Split(':');
            if (parts.Length != 2 || !uint.TryParse(parts[1], out var itemId) || !_currencies.TryGetValue(itemId, out var currency)) continue;
            if (open == false) { readings.Add(new Reading(item.Id, null)); continue; }
            switch (HeldItems.Held(itemId))
            {
                case { Count: > 0 } held: readings.Add(new Reading(item.Id, new CounterValue(held.Count, currency.StackSize, now))); break;
                case { Loaded: true }: readings.Add(new Reading(item.Id, new CounterValue(0, currency.StackSize, now))); break;
            }
        }
        return readings;
    }

    /// <summary>Three rows on the PvP profile, read only while it reports loaded. Gate: any of the three "A Pup No Longer"
    /// quests; locked stores a gate lock, because a NeedsWindow row with nothing stored reads "needs: PvP Profile" and is not blank;
    /// an unloaded profile sends the open gate, which lifts a stale lock and changes nothing else. Whether the profile is
    /// loaded at login is not yet settled; the log records the answer once each way. A character with no previous series has
    /// nothing to claim from one: that row reads "0 of 0", done, the same "nothing outstanding" reading `pvp:claimed` gives with
    /// nothing earned, rather than a gate lock — a lock would only be lifted by this family's own open-gate reading and so would
    /// survive a series that ends after a character last had something to claim from it.</summary>
    private IReadOnlyList<Reading>? Pvp(DateTimeOffset now)
    {
        var quests = data.GetExcelSheet<Quest>();
        var open = AnyQuestOpen(extras.ContentUnlockQuests.Pvp, quests);
        if (open == null) return null;
        if (open == false) return Lock();
        var profile = PvpSnapshot();
        ReportPvpOnce(profile);
        if (profile is not { } p) return Open();
        ReportRanks(p);
        // No level table is no maximum to judge against: the rows cannot be read now, and the open gate still lifts a stale lock.
        if (rows.PvPSeriesLevels.Count == 0) return Open();
        var maximum = (int)rows.PvPSeriesLevels.Max(l => l.RowId);
        var readings = new List<Reading>(_items.Count);
        foreach (var item in _items)
        {
            switch (item.Id)
            {
                case "pvp:series":
                    readings.Add(new Reading(item.Id, new LevelValue(p.CurrentRank, maximum, p.Experience, ContentRules.PvpExpToNext(p.CurrentRank, rows.PvPSeriesLevels))));
                    break;
                case "pvp:claimed":
                {
                    var (claimed, earned) = ContentRules.PvpClaimed(p.CurrentRank, p.ClaimedRank, maximum);
                    readings.Add(new Reading(item.Id, new CounterValue(claimed, earned, now)));
                    break;
                }
                case "pvp:claimed-previous":
                {
                    var (claimed, earned) = p.HasPrevious
                        ? ContentRules.PvpClaimed(p.PreviousRank, p.PreviousClaimedRank, maximum)
                        : (0, 0);
                    readings.Add(new Reading(item.Id, new CounterValue(claimed, earned, now)));
                    break;
                }
                case CatalogBuilder.CrystallineConflictId:
                    // The tier, labelled with its riser or credit and its season; no season or no rank clears.
                    readings.Add(ContentRules.CrystallineTier(item.Name, p.CcSeason, p.CcRank, p.CcRiser, p.CcCredit, rows.ColosseumRanks) is { } tier
                        ? new Reading(item.Id, tier.Level, Label: tier.Label)
                        : new Reading(item.Id, null));
                    break;
                default:
                    // A company's rank over 50 with its band title; a company never ranked in clears. GrandCompany rows
                    // 1 to 3 are the Maelstrom, the Twin Adder and the Immortal Flames, the profile's fields in that order.
                    if (item.Id.StartsWith(CatalogBuilder.PvpRankPrefix, StringComparison.Ordinal)
                        && uint.TryParse(item.Id.AsSpan(CatalogBuilder.PvpRankPrefix.Length), out var company) && company is >= 1 and <= 3)
                    {
                        var index = (int)company - 1;
                        readings.Add(ContentRules.PvpRank(p.CompanyRanks[index], p.CompanyExperience[index], (int)company, rows.PvPRanks) is { } rank
                            ? new Reading(item.Id, rank.Level, Label: rank.Title.Length > 0 ? $"{item.Name}: {rank.Title}" : null)
                            : new Reading(item.Id, null));
                    }
                    break;
            }
        }
        return readings;
    }

    private readonly record struct PvpState(int CurrentRank, int ClaimedRank, int Experience, bool HasPrevious, int PreviousRank, int PreviousClaimedRank,
        int CcSeason, int CcRank, int CcRiser, int CcStars, int CcCredit, int CcHighestRank, int CcHighestRiser, int[] CompanyRanks, int[] CompanyExperience);

    /// <summary>PvPProfile.Instance() is a direct static address whose first field is IsLoaded; the six numbers are
    /// plain fields and are read as such, the previous-series flag is the member function. Null when the struct is absent or not
    /// loaded. FFXIVClientStructs' PvPProfile: bool IsLoaded; byte
    /// SeriesCurrentRank, SeriesClaimedRank, PreviousSeriesRank, PreviousSeriesClaimedRank; ushort SeriesExperience; bool
    /// HasPreviousSeriesRank(); byte CrystallineConflictSeason, CrystallineConflictCurrentRank, CurrentRiser, CurrentRisingStars,
    /// HighestRank, HighestRiser; ushort CrystallineConflictCurrentCrystalCredit; byte RankMaelstrom, RankTwinAdder,
    /// RankImmortalFlames; uint ExperienceMaelstrom, ExperienceTwinAdder, ExperienceImmortalFlames.</summary>
    private static unsafe PvpState? PvpSnapshot()
    {
        var profile = PvPProfile.Instance();
        if (profile == null || !profile->IsLoaded) return null;
        return new PvpState(profile->SeriesCurrentRank, profile->SeriesClaimedRank, profile->SeriesExperience,
            profile->HasPreviousSeriesRank(), profile->PreviousSeriesRank, profile->PreviousSeriesClaimedRank,
            profile->CrystallineConflictSeason, profile->CrystallineConflictCurrentRank, profile->CrystallineConflictCurrentRiser,
            profile->CrystallineConflictCurrentRisingStars, profile->CrystallineConflictCurrentCrystalCredit,
            profile->CrystallineConflictHighestRank, profile->CrystallineConflictHighestRiser,
            [profile->RankMaelstrom, profile->RankTwinAdder, profile->RankImmortalFlames],
            [(int)profile->ExperienceMaelstrom, (int)profile->ExperienceTwinAdder, (int)profile->ExperienceImmortalFlames]);
    }

    /// <summary>Logs the first read that finds the profile unloaded and the first that finds it loaded, with the numbers, showing
    /// whether PvPProfile.IsLoaded is true at login or only after the PvP Profile window.</summary>
    private void ReportPvpOnce(PvpState? profile)
    {
        if (profile is { } p && !_pvpLoadedReported)
        {
            _pvpLoadedReported = true;
            log($"pvp profile: loaded {(_pvpUnloadedReported ? "after reading unloaded earlier this session" : "at the first read")}; series {p.CurrentRank} claimed {p.ClaimedRank} exp {p.Experience}; previous {(p.HasPrevious ? $"{p.PreviousRank} claimed {p.PreviousClaimedRank}" : "none")}");
        }
        if (profile == null && !_pvpUnloadedReported)
        {
            _pvpUnloadedReported = true;
            log("pvp profile: not loaded at the first read");
        }
    }

    /// <summary>Logs the two rows' raw fields once per character, comparing the tier and each company's
    /// rank with the PvP Profile, since the rules read the tier byte as the row and the experience as cumulative.</summary>
    private void ReportRanks(PvpState p)
    {
        if (player.ContentId == _ranksReportedFor) return;
        _ranksReportedFor = player.ContentId;
        log($"pvp ranks: crystalline conflict season {p.CcSeason}, rank {p.CcRank}, riser {p.CcRiser}, stars {p.CcStars}, credit {p.CcCredit} (highest rank {p.CcHighestRank}, riser {p.CcHighestRiser}); " +
            $"grand company 1 rank {p.CompanyRanks[0]} experience {p.CompanyExperience[0]}, 2 rank {p.CompanyRanks[1]} experience {p.CompanyExperience[1]}, 3 rank {p.CompanyRanks[2]} experience {p.CompanyExperience[2]}");
    }

    /// <summary>The lock for every row of this family: the character has not unlocked it, and the Assembler reads the lock as a blank
    /// row in no top group.</summary>
    private IReadOnlyList<Reading> Lock() => _items.Select(i => new Reading(i.Id, GateValue.Shut)).ToList();

    /// <summary>The open gate for every row of this family: the gate is open and the window is not readable now. Merge lifts a stored
    /// lock on it and otherwise changes nothing.</summary>
    private IReadOnlyList<Reading> Open() => _items.Select(i => new Reading(i.Id, GateValue.Open)).ToList();

    /// <summary>A quest gate: true or false once the quest row resolves, null when it does not (never judged from an
    /// unresolved row).</summary>
    private bool? QuestOpen(uint questId, ExcelSheet<Quest> quests) =>
        questId != 0 && quests.GetRowOrDefault(questId) is { } quest ? unlocks.IsQuestCompleted(quest) : null;

    /// <summary>Open once any listed quest is complete, as the levequest gate is; null when none of them resolves.</summary>
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
}
