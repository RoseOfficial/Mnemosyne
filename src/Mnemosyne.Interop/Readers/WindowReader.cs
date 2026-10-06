using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;
using ClientContentsNote = FFXIVClientStructs.FFXIV.Client.Game.UI.ContentsNote;
using SheetInstanceContent = Lumina.Excel.Sheets.InstanceContent;

namespace Mnemosyne.Interop.Readers;

/// <summary>The six game windows whose rows can be read only while the window is open. One reader instance per window, so a layout change
/// behind one of them switches off that instance alone.</summary>
public enum GameWindow { GrandCompanySupply, ChallengeLog, FashionReport, DomanEnclave, MaskedCarnivale, DutyFinder }

/// <summary>One Resets row family that is readable only after its window has been opened. Every instance runs
/// at login, on the interval tick and on the Window trigger the plugin fires from the window's own Post event, and
/// each first asks the row's unlock gate for a gated row: locked stores a gate
/// lock, because nothing stored on a NeedsWindow row reads "needs: <window>" and is not blank; unreadable says nothing. Then
/// the window's load flag decides the merge: a state that survives the window closing for the challenge log and the
/// Doman Enclave, the agent being active for the rest, which is why those are read inside the Post handler. A row whose gate
/// is open and whose window is not readable gets an open gate reading, which lifts a stale lock and changes nothing else.
/// A window reader never emits a clearing reading. Every client member used here is FFXIVClientStructs' own, and nothing in
/// this file writes, requests, opens or shows anything.</summary>
public sealed class WindowReader(GameWindow window, Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    private readonly IReadOnlyList<ItemDefinition> _items = catalog.InCategory(Category.Resets).Where(i => i.Id.StartsWith(Prefix(window), StringComparison.Ordinal)).ToList();
    // Each window builds only the lookup it reads, as ContentReader's families do.
    private readonly Dictionary<uint, ContentsNoteRow> _entries = window == GameWindow.ChallengeLog ? rows.ChallengeEntries.ToDictionary(e => e.RowId) : new Dictionary<uint, ContentsNoteRow>();
    private readonly Dictionary<uint, LockoutRow> _lockouts = window == GameWindow.DutyFinder ? rows.Lockouts.ToDictionary(l => l.ConditionId) : new Dictionary<uint, LockoutRow>();
    private bool _fashionReported;

    public string Name => $"WindowReader({window})";
    public Category Category => Category.Resets;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Interval | ReaderTrigger.Window;

    public IReadOnlyList<Reading>? Read()
    {
        // Nothing is read while the player state is not loaded. Every gate below answers "locked" before it.
        if (!player.IsLoaded) return null;
        var now = clock.UtcNow;
        return window switch
        {
            // Each window fetches only the sheets it reads: every window fire runs all six instances, and four agents were added
            // to that trigger.
            GameWindow.GrandCompanySupply => GrandCompany(now, data.GetExcelSheet<ClassJob>()),
            GameWindow.ChallengeLog => ChallengeLog(now, data.GetExcelSheet<Quest>(), data.GetExcelSheet<ClassJob>()),
            GameWindow.FashionReport => Fashion(now, data.GetExcelSheet<Quest>()),
            GameWindow.DomanEnclave => Enclave(now, data.GetExcelSheet<Quest>()),
            GameWindow.MaskedCarnivale => Carnivale(now, data.GetExcelSheet<Quest>()),
            GameWindow.DutyFinder => DutyFinder(now),
            _ => throw new ArgumentOutOfRangeException(nameof(window), window, "unknown game window"),
        };
    }

    /// <summary>The id family each window fills.</summary>
    private static string Prefix(GameWindow window) => window switch
    {
        GameWindow.GrandCompanySupply => "reset:gc:",
        GameWindow.ChallengeLog => "reset:challenge:",
        GameWindow.FashionReport => "reset:fashion-report",
        GameWindow.DomanEnclave => "reset:enclave",
        GameWindow.MaskedCarnivale => "reset:carnivale:",
        GameWindow.DutyFinder => "reset:lockout:",
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "unknown game window"),
    };

    /// <summary>Two Done rows on the supply agent's item array, valid only while the agent is active: an edge. Gate:
    /// a Grand Company, then a crafter at level 1 for supply and a gatherer for provisioning, the per-class half
    /// taken to be so; not yet confirmed. Done for a half is the DailyDuty plugin's rule verbatim: every offered entry of the
    /// half turned in. A half with no offered entry cannot be judged and gets the open gate reading, as a closed window does.</summary>
    private IReadOnlyList<Reading> GrandCompany(DateTimeOffset now, ExcelSheet<ClassJob> jobs)
    {
        // GrandCompany is row 0 for a character in none; the loaded guard has already run.
        if (player.GrandCompany.RowId == 0) return Lock();
        var crafter = Highest(jobs, ResetGates.CrafterClassJobs);
        var gatherer = Highest(jobs, ResetGates.GathererClassJobs);
        var supply = SupplySnapshot();
        var readings = new List<Reading>();
        foreach (var item in _items)
        {
            var isSupply = item.Id == "reset:gc:supply";
            var highest = isSupply ? crafter : gatherer;
            if (highest == null) continue;                                                        // no class row resolved: not judged
            if (highest < 1) { readings.Add(new Reading(item.Id, GateValue.Shut)); continue; }    // locked: no class of the half
            var done = isSupply ? supply?.SupplyDone : supply?.ProvisioningDone;
            readings.Add(done is { } d ? Readings.Done(item, d, now) : new Reading(item.Id, GateValue.Open));
        }
        return readings;
    }

    private readonly record struct SupplyState(bool? SupplyDone, bool? ProvisioningDone);

    /// <summary>Agent 96, active, NumItems at least eleven; positions 0 to 7 are supply, 8 to 10 provisioning,
    /// 11 and up expert delivery, FFXIVClientStructs' GrandCompanyItem; an
    /// entry with ItemId 0 is not offered; IsTurnInAvailable is TurnInAvailable == 0. Null when the window is not open.</summary>
    private static unsafe SupplyState? SupplySnapshot()
    {
        var agent = AgentGrandCompanySupply.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->ItemArray == null || agent->NumItems < 11) return null;
        int supplyOffered = 0, supplyLeft = 0, provisioningOffered = 0, provisioningLeft = 0;
        for (var i = 0; i < agent->NumItems; i++)
        {
            var entry = agent->ItemArray + i;
            if (entry->ItemId == 0) continue;
            if (entry->Position is >= 0 and <= 7) { supplyOffered++; if (entry->IsTurnInAvailable) supplyLeft++; }
            else if (entry->Position is >= 8 and <= 10) { provisioningOffered++; if (entry->IsTurnInAvailable) provisioningLeft++; }
        }
        return new SupplyState(supplyOffered == 0 ? null : supplyLeft == 0, provisioningOffered == 0 ? null : provisioningLeft == 0);
    }

    /// <summary>Ninety Done rows on the challenge log's completion bits, read once the client reports the log loaded, a state
    /// that survives the window closing. Gate: the whole log on its unlock quest, then each entry on its
    /// ReqUnlock — a quest above 65535 or an unlock link below, both answered by one Dalamud member,
    /// IUnlockState.IsUnlockLinkUnlocked(uint): "The unlock link id or quest id (quest ids in this case are over 65536)" — and
    /// its LevelUnlock against the highest class or job level: taken to be so; not yet confirmed.</summary>
    private IReadOnlyList<Reading>? ChallengeLog(DateTimeOffset now, ExcelSheet<Quest> quests, ExcelSheet<ClassJob> jobs)
    {
        var whole = QuestOpen(extras.UnlockQuests.ChallengeLog, quests);
        if (whole == null) return null;
        if (whole == false) return Lock();
        var highest = Highest(jobs, rows.ClassJobs.Select(j => j.RowId));
        var loaded = ChallengeLogLoaded();
        var readings = new List<Reading>();
        foreach (var item in _items)
        {
            if (!Number(item.Id, 2, out var row) || !_entries.TryGetValue(row, out var entry)) continue;
            var unlocked = entry.ReqUnlock == 0 || unlocks.IsUnlockLinkUnlocked(entry.ReqUnlock);
            var open = ResetGates.ChallengeEntryOpen(unlocked, entry.LevelUnlock, highest);
            if (open == null) continue;
            if (open == false) { readings.Add(new Reading(item.Id, GateValue.Shut)); continue; }
            readings.Add(loaded == true && ChallengeComplete((int)row) is { } done ? Readings.Done(item, done, now) : new Reading(item.Id, GateValue.Open));
        }
        return readings;
    }

    /// <summary>ContentsNote.Instance() is a direct static address and State is Invalid, Requested or Loaded;
    /// whether Loaded is reached at login is not yet settled. Null when the struct is absent.</summary>
    private static unsafe bool? ChallengeLogLoaded()
    {
        var note = ClientContentsNote.Instance();
        return note == null ? null : note->State == ClientContentsNote.ContentsNoteState.Loaded;
    }

    private static unsafe bool? ChallengeComplete(int rowId)
    {
        var note = ClientContentsNote.Instance();
        return note == null ? null : note->IsContentNoteComplete(rowId);
    }

    /// <summary>One Done row on the Fashion agent's copy of the week's numbers, read while the agent is active. Gate:
    /// the unlock quest. The done rule is FashionReport.Rule: a score of 80 or more.</summary>
    private IReadOnlyList<Reading>? Fashion(DateTimeOffset now, ExcelSheet<Quest> quests)
    {
        if (_items.Count == 0) return [];
        var open = QuestOpen(extras.UnlockQuests.FashionReport, quests);
        if (open == null) return null;
        if (open == false) return Lock();
        if (FashionSnapshot() is not { } report) return Open();
        ReportFashionOnce(report, ManagerSnapshot());
        var done = FashionReport.IsDone(FashionReport.Rule, report.Remaining, report.HighScore, extras.FashionReport);
        return [Readings.Done(_items[0], done, now)];
    }

    /// <summary>Agent 310 holds its own copy of the manager's data and the manager has no load flag, so the
    /// agent is read while active. FFXIVClientStructs' AgentFashion:
    /// FashionCheckData.Remaining and .HighScore are bytes.</summary>
    private static unsafe (int Remaining, int HighScore)? FashionSnapshot()
    {
        var agent = AgentFashion.Instance();
        return agent == null || !agent->IsAgentActive() ? null : (agent->FashionCheckData.Remaining, agent->FashionCheckData.HighScore);
    }

    /// <summary>The manager's copy of the same two numbers, for cross-checking against the agent. Instance() is a member
    /// function that returns a pointer and changes nothing; null when it returns none. FFXIVClientStructs'
    /// FashionCheckManager.</summary>
    private static unsafe (int Remaining, int HighScore)? ManagerSnapshot()
    {
        var manager = FashionCheckManager.Instance();
        return manager == null ? null : (manager->PlayerInfo.Remaining, manager->PlayerInfo.HighScore);
    }

    /// <summary>Logs the agent's and the manager's numbers once per session on the first read, showing which struct is filled
    /// and when.</summary>
    private void ReportFashionOnce((int Remaining, int HighScore) agent, (int Remaining, int HighScore)? manager)
    {
        if (_fashionReported) return;
        _fashionReported = true;
        log(manager is { } m
            ? $"fashion report: agent remaining {agent.Remaining}, high score {agent.HighScore}; manager remaining {m.Remaining}, high score {m.HighScore}"
            : $"fashion report: agent remaining {agent.Remaining}, high score {agent.HighScore}; the manager is absent");
    }

    /// <summary>One Counter row, donated over the weekly allowance, both from the client. Gate: the unlock quest.</summary>
    private IReadOnlyList<Reading>? Enclave(DateTimeOffset now, ExcelSheet<Quest> quests)
    {
        if (_items.Count == 0) return [];
        var open = QuestOpen(extras.UnlockQuests.DomanEnclave, quests);
        if (open == null) return null;
        if (open == false) return Lock();
        if (EnclaveSnapshot() is not { } enclave) return Open();
        return [new Reading(_items[0].Id, new CounterValue(enclave.Donated, enclave.Allowance, now))];
    }

    /// <summary>Instance() dereferences a pointer to a pointer and may be null; IsLoaded is
    /// the state that survives the window closing; an Allowance of 0 is unknown, never a full allowance, so it reads as not loaded.</summary>
    private static unsafe (int Donated, int Allowance)? EnclaveSnapshot()
    {
        var manager = DomanEnclaveManager.Instance();
        if (manager == null || !manager->IsLoaded || manager->State.Allowance == 0) return null;
        return (manager->State.Donated, manager->State.Allowance);
    }

    /// <summary>Three Done rows on the briefing agent's weekly flags, valid only while the agent is active: an edge.
    /// Gate: the unlock quest, one for all three.</summary>
    private IReadOnlyList<Reading>? Carnivale(DateTimeOffset now, ExcelSheet<Quest> quests)
    {
        var open = QuestOpen(extras.UnlockQuests.MaskedCarnivale, quests);
        if (open == null) return null;
        if (open == false) return Lock();
        if (CarnivaleSnapshot() is not { } completion) return Open();
        var readings = new List<Reading>();
        foreach (var item in _items)
        {
            if (!Number(item.Id, 2, out var challenge) || challenge > 2) continue;
            // The flag bits are not the enum value shifted (bit 0 is an unknown flag), so map by name.
            var flag = challenge switch { 0 => AozWeeklyFlags.Novice, 1 => AozWeeklyFlags.Moderate, _ => AozWeeklyFlags.Advanced };
            readings.Add(Readings.Done(item, (completion & flag) != 0, now));
        }
        return readings;
    }

    /// <summary>Agent 333 while active; WeeklyCompletion is the typed flags field. FFXIVClientStructs'
    /// AgentAozContentBriefing.</summary>
    private static unsafe AozWeeklyFlags? CarnivaleSnapshot()
    {
        var agent = AgentAozContentBriefing.Instance();
        return agent == null || !agent->IsAgentActive() ? null : agent->WeeklyCompletion;
    }

    /// <summary>Two Done rows, one per week-restricted duty, filled only from the player's own Duty Finder selection:
    /// the agent holds reward state for the selected duty alone. Gate: the duty's own unlock. What this accepts: a player
    /// selects the duty before the run, so the row reads "not done" until the next selection.</summary>
    private IReadOnlyList<Reading> DutyFinder(DateTimeOffset now)
    {
        var contents = data.GetExcelSheet<SheetInstanceContent>();
        var selected = DutyFinderSnapshot();
        var readings = new List<Reading>();
        foreach (var item in _items)
        {
            if (!Number(item.Id, 2, out var condition) || !_lockouts.TryGetValue(condition, out var lockout)) continue;
            if (contents.GetRowOrDefault(lockout.ContentId) is not { } content) continue;                            // unresolved row: not judged
            if (!unlocks.IsInstanceContentUnlocked(content)) { readings.Add(new Reading(item.Id, GateValue.Shut)); continue; }
            readings.Add(selected is { } duty && duty.Condition == condition ? Readings.Done(item, duty.Collected > 0, now) : new Reading(item.Id, GateValue.Open));
        }
        return readings;
    }

    /// <summary>SelectedDuty carries a type and an id that is a ContentFinderCondition
    /// row for Regular and a ContentRoulette row for Roulette, so both are tested; NumCollectedRewards above zero is
    /// the DailyDuty plugin's "done this week". Refresh, UpdateAddon, the Load* and Open* members are never called.
    /// FFXIVClientStructs' AgentContentsFinder, ContentsId, ContentsType.</summary>
    private static unsafe (uint Condition, int Collected)? DutyFinderSnapshot()
    {
        var agent = AgentContentsFinder.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->SelectedDuty.ContentType != ContentsType.Regular) return null;
        return (agent->SelectedDuty.Id, agent->NumCollectedRewards);
    }

    /// <summary>The lock for every row of this window: the character has not unlocked it, and the Assembler reads the lock as a
    /// blank row in no top group.</summary>
    private IReadOnlyList<Reading> Lock() => _items.Select(i => new Reading(i.Id, GateValue.Shut)).ToList();

    /// <summary>The open gate for every row of this window: the gate is open and the window is not readable now. Merge lifts a
    /// stored lock on it and otherwise changes nothing, so the reading costs nothing and needs no visit to clear a stale lock.</summary>
    private IReadOnlyList<Reading> Open() => _items.Select(i => new Reading(i.Id, GateValue.Open)).ToList();

    /// <summary>A quest gate: true or false once the quest row resolves, null when it does not (never judged from an
    /// unresolved row).</summary>
    private bool? QuestOpen(uint questId, ExcelSheet<Quest> quests) =>
        questId != 0 && quests.GetRowOrDefault(questId) is { } quest ? unlocks.IsQuestCompleted(quest) : null;

    /// <summary>The highest level among some classes through the service, or null when none of their sheet rows resolves.</summary>
    private int? Highest(ExcelSheet<ClassJob> jobs, IEnumerable<uint> classJobs)
    {
        int? highest = null;
        foreach (var id in classJobs)
        {
            if (jobs.GetRowOrDefault(id) is { } row) highest = Math.Max(highest ?? 0, player.GetClassJobLevel(row));
        }
        return highest;
    }

    private static bool Number(string id, int index, out uint value)
    {
        value = 0;
        var parts = id.Split(':');
        return index < parts.Length && uint.TryParse(parts[index], out value);
    }
}
