using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;
using ClientInstanceContent = FFXIVClientStructs.FFXIV.Client.Game.UI.InstanceContent;

namespace Mnemosyne.Interop.Readers;

/// <summary>Every Resets item whose value is in memory once the player state is loaded — the eleven daily
/// roulettes, the sixteen daily and six weekly hunt bills, the allied society and levequest allowances, the treasure map,
/// squadron and Wondrous Tails timers, custom deliveries, the weekly tomestone count and Faux Hollows. One reader for the
/// whole category: every item shares the player-state guard, one clock read and one walk of the catalog, and the daily and
/// weekly halves want the same triggers. Runs on the interval tick, so a roulette finished live updates without a relog:
/// the read lands on the next interval tick, within the tick interval, never on the frame the duty ends. DutyCompleted is
/// named as well so that a reader which does not ask for Interval could run on that event alone; for this reader it decides
/// nothing, because Interval already admits every tick. Nothing in this file writes, requests, or opens anything.
///
/// The rule the whole file follows is what a locked item does to a stored value: a row this character has not
/// unlocked reads nothing, is blank, and sits in no top group. Every row has a gate. A gate that answers "locked" with the
/// player state loaded emits a clearing reading, which forgets the stored value, its stamp and its name, because
/// a stored value keeps a row in the top groups for good. A gate that cannot be read — a null client pointer, a sheet row that failed to resolve, a
/// manager not ready — emits nothing, and the stored value stays, exactly as a null Read() keeps the category stamp. The
/// two are never confused: the retainer list not being ready at login used to wipe every stored venture deadline, and that
/// is the case the distinction exists for. Every gate is monotonic, so a cleared row cannot flap. The two cactpot rows get
/// their gate here as a stored gate value, because a clearing reading cannot hide a row whose empty state reads "not seen".
/// This reader reads the retainer roster through Roster.Read, which the Money reader also calls: two calls and two
/// RosterGuard instances, not one shared read. The two agree today because both readers run in
/// the same Tracker.Run frame with the same triggers, so they see identical inputs in the same order; nothing enforces
/// that if a future trigger change split them. It trusts the roster only once it is this character's, and trusts an empty
/// one only once this login has shown the roster non-empty (RosterGuard).</summary>
public sealed class ResetsReader(Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    private readonly Dictionary<uint, HuntOrderTypeRow> _bills = rows.HuntOrderTypes.ToDictionary(t => t.RowId);
    private readonly Dictionary<uint, SatisfactionNpcRow> _clients = rows.SatisfactionNpcs.ToDictionary(n => n.RowId);
    private readonly Dictionary<uint, RouletteRow> _roulettes = rows.Roulettes.ToDictionary(r => r.RowId);
    private readonly Dictionary<uint, RouletteGate> _gates = extras.RouletteGates.ToDictionary(g => g.RouletteId);
    private readonly int _tomestoneFallback = rows.Tomestones.OrderBy(t => t.ItemId).Select(t => t.WeeklyLimit).FirstOrDefault();
    private readonly HashSet<uint> _huntRangeReported = [];
    private bool _leveAnchorReported;

    // Roster.Read is called again here, the same read the Money reader makes; the two guards agree by construction, not by
    // sharing. A roster left in the manager by the previous character is never read as this character's. A ready
    // roster with no retainers is believed only once this login has shown the roster non-empty. The guard keeps every venture slot as
    // stored until then.
    private readonly RosterGuard _rosterGuard = new();

    /// <summary>The client's per-duty unlock and completion bits, both pure statics that take an InstanceContent row id.
    /// Held as delegates once so the roulette gate allocates nothing on the tick.</summary>
    private static readonly Func<uint, bool> ContentUnlocked = UIState.IsInstanceContentUnlocked;
    private static readonly Func<uint, bool> ContentCompleted = UIState.IsInstanceContentCompleted;

    public string Name => "ResetsReader";
    public Category Category => Category.Resets;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.DutyCompleted | ReaderTrigger.Interval;

    /// <summary>A read that says nothing of its trigger is a login's: the full read, which also starts the roster guard's proof over.</summary>
    public IReadOnlyList<Reading>? Read() => Read(ReaderTrigger.Login);

    public IReadOnlyList<Reading>? Read(ReaderTrigger fired)
    {
        // Nothing is read while the player state is not loaded, and the category's read stamp does not move.
        // This guard stays the first line: every IUnlockState and IPlayerState member answers false or 0 before it, which
        // reads exactly like "locked" and would clear real values.
        if (!player.IsLoaded) return null;
        var now = clock.UtcNow;
        var orders = data.GetSubrowExcelSheet<MobHuntOrder>();
        var quests = data.GetExcelSheet<Quest>();
        var jobs = data.GetExcelSheet<ClassJob>();
        var deliveries = Deliveries();
        var bingo = Bingo();
        var timers = PlayerTimers();
        // The retainer manager is a direct static address, so a login's read always reaches the guard with its login flag.
        var snapshot = Roster.Read();
        var retainers = snapshot is { } read && _rosterGuard.Trust(player.ContentId, (fired & ReaderTrigger.Login) != 0, read.Ready, read.Ids) ? read : null;
        var gates = Gates(quests, jobs);
        var readings = new List<Reading>();

        // True when the row may be read. A locked gate clears the row here and an unreadable one adds nothing, which is
        // the whole of the locked-item rule in one place.
        bool Open(bool? gate, ItemDefinition item)
        {
            if (gate == false) readings.Add(new Reading(item.Id, null));
            return gate == true;
        }

        foreach (var item in catalog.InCategory(Category))
        {
            // Every id is parsed with TryParse: a malformed one skips its own item, where a throw would disable the whole
            // reader for the session.
            var parts = item.Id.Split(':');
            if (parts.Length < 2) continue;
            switch (parts[1])
            {
                case "roulette":
                    if (Number(parts, 2, out var roulette) && roulette <= byte.MaxValue && Open(RouletteOpen(roulette, gates), item)
                        && RouletteComplete((byte)roulette) is { } rouletteDone)
                        readings.Add(Readings.Done(item, rouletteDone, now));
                    break;

                case "hunt":
                    if (Number(parts, 2, out var mark) && mark <= byte.MaxValue && _bills.TryGetValue(mark, out var bill))
                    {
                        var state = HuntBill((byte)mark, bill, orders, out var outOfRange);
                        if (outOfRange is { } wrong) ReportHuntOrderRange(bill, wrong);
                        switch (state)
                        {
                            case HuntBillState.Locked: readings.Add(new Reading(item.Id, null)); break;
                            case HuntBillState.Done: readings.Add(Readings.Done(item, true, now)); break;
                            case HuntBillState.NotDone: readings.Add(Readings.Done(item, false, now)); break;
                        }
                    }
                    break;

                case "tribe-allowance":
                    if (Open(gates.TribeOpen, item) && TribeAllowanceLeft() is { } left)
                    {
                        var cap = extras.ResetCaps.AlliedSocietyAllowances;
                        readings.Add(new Reading(item.Id, new CounterValue(Math.Clamp(cap - (int)left, 0, cap), cap, now)));
                    }
                    break;

                case "leve-allowance":
                    if (Open(gates.LeveOpen, item) && LeveAllowances() is { } allowances)
                    {
                        var anchor = NextLeveGrant() is { } stamp ? DateTimeOffset.FromUnixTimeSeconds(stamp) : (DateTimeOffset?)null;
                        ReportLeveAnchor(anchor, now);
                        readings.Add(new Reading(item.Id, new CounterValue(allowances, extras.ResetCaps.LevequestAllowances, now, anchor)));
                    }
                    break;

                case "treasure-map":
                    // The client fills the field at login on its own, and for a character with no cooldown it
                    // holds the login instant, so the value cannot gate the row; the gatherer level does. What the value
                    // reads during a running cooldown is not yet settled, and a zero is still a value
                    // the plugin does not have, never a deadline of 1970.
                    if (Open(gates.MapOpen, item) && NextMapAllowance() is { } map)
                        readings.Add(new Reading(item.Id, new TimerValue(DateTimeOffset.FromUnixTimeSeconds(map))));
                    break;

                case "squadron" when parts.Length > 2 && timers is { } squadron:
                {
                    var stamp = parts[2] switch
                    {
                        "mission" => squadron.SquadronMission,
                        "training" => squadron.SquadronTraining,
                        _ => (int?)null,
                    };
                    if (stamp is { } seconds) readings.Add(Deadline(item, seconds));
                    break;
                }

                case "delivery" when parts.Length > 2 && parts[2] == "all":
                    // The total a character can reach is the unlocked clients' sum, capped by the client's own twelve, so a
                    // one-client character reads done at six rather than never at twelve. No client unlocked
                    // clears the row; a client whose quest could not be resolved leaves it alone, because the reachable
                    // total is then unknown.
                    if (gates.ClientsUnreadable) break;
                    if (gates.UnlockedClientCaps.Count == 0) { readings.Add(new Reading(item.Id, null)); break; }
                    if (deliveries is { } total && ResetGates.DeliveryTotalCap(total.Cap, gates.UnlockedClientCaps) is { } totalCap)
                        readings.Add(new Reading(item.Id, new CounterValue(total.Used, totalCap, now)));
                    break;

                case "delivery":
                    if (Number(parts, 2, out var npc) && _clients.TryGetValue(npc, out var client) && Open(gates.Clients.GetValueOrDefault(npc), item)
                        && deliveries is { } perClient && npc - 1 < perClient.PerClient.Length)
                        readings.Add(new Reading(item.Id, new CounterValue(perClient.PerClient[npc - 1], client.DeliveriesPerWeek, now)));
                    break;

                case "bingo" when parts.Length > 2 && bingo is { } tails:
                    // No journal is "this character has no such thing" for all three rows, and the second-chance row now
                    // shares the gate its siblings had. Whether the client's flag tells never-unlocked
                    // from none-held is not yet settled; either way the rows clear rather than count.
                    if (!tails.HasJournal)
                    {
                        if (parts[2] is "stickers" or "second-chance" or "expiry") readings.Add(new Reading(item.Id, null));
                        break;
                    }
                    switch (parts[2])
                    {
                        case "stickers":
                            readings.Add(new Reading(item.Id, new CounterValue(tails.Stickers, extras.ResetCaps.WondrousTailsStickers, now)));
                            break;
                        case "second-chance":
                            readings.Add(new Reading(item.Id, new CounterValue(tails.SecondChance, extras.ResetCaps.WondrousTailsSecondChance, now)));
                            break;
                        case "expiry" when tails.ExpiresUnix > 0:
                            readings.Add(new Reading(item.Id, new TimerValue(DateTimeOffset.FromUnixTimeSeconds(tails.ExpiresUnix))));
                            break;
                    }
                    break;

                case "hollows" when timers is { } hollows:
                    // Neither Faux Hollows field is documented. "Done this week" is derived from the timestamp
                    // falling inside the current weekly period, which needs no reading of the state's encoding at all. A
                    // zero stamp with the state loaded is "never played", perhaps "never unlocked", and either way a
                    // stored answer must not outlive it.
                    if (hollows.FauxHollows > 0)
                    {
                        var played = DateTimeOffset.FromUnixTimeSeconds(hollows.FauxHollows);
                        readings.Add(Readings.Done(item, played >= ResetSchedules.PeriodStart(ResetSchedules.Weekly, now), now));
                    }
                    else readings.Add(new Reading(item.Id, null));
                    break;

                case "tomestone":
                    if (Open(gates.TomestoneOpen, item) && WeeklyTomestone() is { } tomestone)
                    {
                        var limit = tomestone.Limit > 0 ? tomestone.Limit : _tomestoneFallback;
                        if (limit > 0) readings.Add(new Reading(item.Id, new CounterValue(tomestone.Acquired, limit, now)));
                    }
                    break;

                case "venture" when retainers is { } roster:
                    // Ten slots in the catalog, and a roster of up to ten per character read by sorted index. This reader
                    // reads the roster once through Roster.Read, shared with the Money reader, behind a per-character
                    // guard. The guard keeps every slot while the roster is the previous character's, and while a ready
                    // roster with no retainers has not yet been shown non-empty since this login. A slot at or beyond the
                    // count is empty and clears; a retainer with no venture keeps its name on the row and clears the
                    // deadline, so the row says whose slot is idle.
                    if (Number(parts, 2, out var slot))
                    {
                        if (slot >= roster.Count) { readings.Add(new Reading(item.Id, null)); break; }
                        switch (roster.Retainers[(int)slot])
                        {
                            case { VentureComplete: > 0 } running:
                                readings.Add(new Reading(item.Id, new TimerValue(DateTimeOffset.FromUnixTimeSeconds(running.VentureComplete)), Label: running.Name));
                                break;
                            case { } idle:
                                readings.Add(new Reading(item.Id, null, Label: idle.Name));
                                break;
                        }
                    }
                    break;

                case "cactpot" when parts.Length > 2:
                    // The two cactpot rows can only be observed, so a locked gate has to be stored where the
                    // assembler reads and an open one has to lift it without touching an observed count; Merge does both.
                    // The gate is the broker's own unlock quest; an unresolvable quest row says nothing.
                    var brokerQuest = parts[2] switch { "mini" => extras.UnlockQuests.MiniCactpot, "jumbo" => extras.UnlockQuests.JumboCactpot, _ => 0u };
                    if (brokerQuest != 0 && quests.GetRowOrDefault(brokerQuest) is { } broker)
                        readings.Add(new Reading(item.Id, unlocks.IsQuestCompleted(broker) ? GateValue.Open : GateValue.Shut));
                    break;
            }
        }
        return readings;
    }

    /// <summary>A timestamp read with the state loaded: a deadline when set, a clearing reading when zero. Zero means
    /// "nothing running" and, before the feature is unlocked, "never anything", and a stored deadline must not outlive
    /// either.</summary>
    private static Reading Deadline(ItemDefinition item, long unixSeconds) =>
        unixSeconds > 0 ? new Reading(item.Id, new TimerValue(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))) : new Reading(item.Id, null);

    private static bool Number(string[] parts, int index, out uint value)
    {
        value = 0;
        return index < parts.Length && uint.TryParse(parts[index], out value);
    }

    /// <summary>Says once per mark that the client's available hunt-order id fell outside the MobHuntOrder range the sheet
    /// gives that board, which means the id is not the absolute row id this reader takes it for. The bill reads nothing
    /// while that is true, because a lookup on another board's row would answer plausibly and wrongly.</summary>
    private void ReportHuntOrderRange(HuntOrderTypeRow bill, uint available)
    {
        if (!_huntRangeReported.Add(bill.RowId)) return;
        log($"hunt bill {bill.RowId} offered order {available}, outside its own rows {bill.OrderStart} to {bill.OrderStart + bill.OrderAmount - 1}; the bill is not read");
    }

    /// <summary>Says once per session when the client's next levequest grant is not a boundary of Core's own schedule. The
    /// two describe the same thing, so a disagreement means the game's 00:00 and 12:00 UTC grants have changed and the
    /// schedule needs a new source; the client's value is used either way.</summary>
    private void ReportLeveAnchor(DateTimeOffset? anchor, DateTimeOffset now)
    {
        if (_leveAnchorReported || anchor is not { } client) return;
        _leveAnchorReported = true;
        var scheduled = ResetSchedules.NextReset(ResetSchedules.Leve, now);
        if ((client - scheduled).Duration() > TimeSpan.FromMinutes(1))
            log($"the client says the next levequest grant is {client:u}; the leve schedule says {scheduled:u}; using the client's");
    }

    /// <summary>Every whole-character gate, read once per tick ahead of the item walk. Each is true, false
    /// for "locked", or null for "could not be read". Clients maps each delivery client to its own answer; the caps of the
    /// unlocked ones feed the total row.</summary>
    private readonly record struct GateState(bool? TribeOpen, bool? LeveOpen, bool? TomestoneOpen, bool? MapOpen, bool UiStatePresent, bool BattleMentor,
        Dictionary<uint, bool?> Clients, IReadOnlyList<int> UnlockedClientCaps, bool ClientsUnreadable);

    private GateState Gates(ExcelSheet<Quest> quests, ExcelSheet<ClassJob> jobs)
    {
        var clients = new Dictionary<uint, bool?>(_clients.Count);
        var caps = new List<int>();
        var unreadable = false;
        foreach (var client in rows.SatisfactionNpcs)
        {
            var open = ClientOpen(client, quests);
            clients[client.RowId] = open;
            if (open == true) caps.Add(client.DeliveriesPerWeek);
            if (open == null) unreadable = true;
        }
        // A job whose sheet row does not resolve has no level to offer, and a gate with no level to read cannot be judged:
        // if no non-limited combat job resolves the tomestone gate is null, and if none of the three gatherers resolves
        // the map gate is null, so an unreadable sheet keeps both rows rather than clearing them.
        var combatLevels = new Dictionary<uint, int>();
        foreach (var job in rows.ClassJobs)
        {
            if (job.DohDolIndex == -1 && !job.IsLimitedJob && Level(jobs, job.RowId) is { } level) combatLevels[job.RowId] = level;
        }
        var combat = rows.ClassJobs.Where(j => combatLevels.ContainsKey(j.RowId)).ToList();
        var gatherers = ResetGates.GathererClassJobs.Select(id => Level(jobs, id)).Where(l => l != null).Select(l => l!.Value).ToList();
        return new GateState(
            AnyTribeRank(rows.BeastTribes),
            LeveOpen(quests),
            combat.Count == 0 ? null : ResetGates.TomestoneOpen(combat, j => combatLevels[j.RowId], extras.LevelCap),
            gatherers.Count == 0 ? null : ResetGates.MapOpen(gatherers, extras.TreasureMapGatherLevel),
            UiStatePresent(),
            player.IsBattleMentor,
            clients, caps, unreadable);
    }

    /// <summary>Whether one roulette is open. No hand-file gate is open, and the builder said so at start. The
    /// member rules need the client's duty bits, so they are unreadable while UIState is absent; the mentor rule is not.
    /// Cost per tick: one bit read per member visited, and the rules stop early, so a character with nothing unlocked
    /// visits every member of the six count rules (about 210 reads) while a character with everything unlocked visits
    /// about 25; each read is one unmanaged call into a bit lookup.</summary>
    private bool? RouletteOpen(uint roulette, in GateState gates)
    {
        var gate = _gates.GetValueOrDefault(roulette);
        if (gate == null) return true;
        if (gate.Rule != RouletteRule.BattleMentor && !gates.UiStatePresent) return null;
        var members = _roulettes.TryGetValue(roulette, out var row) ? row.MemberContentIds : [];
        return ResetGates.RouletteOpen(gate, members, ContentUnlocked, ContentCompleted, gates.BattleMentor);
    }

    /// <summary>A delivery client is open once its own unlock quest is complete. A quest of zero is ungated; a quest
    /// row the sheet does not carry cannot be judged.</summary>
    private bool? ClientOpen(SatisfactionNpcRow client, ExcelSheet<Quest> quests)
    {
        if (client.QuestRequired == 0) return true;
        return quests.GetRowOrDefault(client.QuestRequired) is { } quest ? unlocks.IsQuestCompleted(quest) : null;
    }

    /// <summary>The levequest allowance is open once any of the hand file's city unlock quests is complete. If none
    /// of them resolves to a sheet row the gate cannot be judged.</summary>
    private bool? LeveOpen(ExcelSheet<Quest> quests)
    {
        var resolved = false;
        foreach (var id in extras.LevequestUnlockQuests)
        {
            if (quests.GetRowOrDefault(id) is not { } quest) continue;
            resolved = true;
            if (unlocks.IsQuestCompleted(quest)) return true;
        }
        return resolved ? false : null;
    }

    /// <summary>A job's level through the service, or null when its sheet row does not resolve.</summary>
    private int? Level(ExcelSheet<ClassJob> jobs, uint classJobId) =>
        jobs.GetRowOrDefault(classJobId) is { } row ? player.GetClassJobLevel(row) : null;

    private static unsafe bool? RouletteComplete(byte roulette)
    {
        var content = ClientInstanceContent.Instance();
        return content == null ? null : content->IsRouletteComplete(roulette);
    }

    /// <summary>Whether the client's UI state exists, which the per-duty unlock and completion statics read through.</summary>
    private static unsafe bool UiStatePresent() => UIState.Instance() != null;

    /// <summary>Whether any allied society stands at rank 1 or above, the neutral rank a society's own unlock quest grants:
    /// the gate on the allowance row, which is one counter for the whole character. The rank is the pure getter
    /// the quest reader already calls for every society; a row above 255 cannot be asked and is skipped.</summary>
    private static unsafe bool? AnyTribeRank(IReadOnlyList<BeastTribeRow> tribes)
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        foreach (var tribe in tribes)
        {
            if (tribe.RowId <= byte.MaxValue && state->GetBeastTribeRank((byte)tribe.RowId) >= 1) return true;
        }
        return false;
    }

    /// <summary>What a hunt board's row does: Locked is a board this character has not unlocked, which clears the row;
    /// Unreadable is a missing manager, no bill on offer, an order id outside the board's own rows or a missing sheet row,
    /// which keeps it; Done and NotDone are read.</summary>
    private enum HuntBillState { Unreadable, Locked, NotDone, Done }

    /// <summary>Whether this period's bill for a mark has been taken and finished. The game never clears a finished bill's
    /// data, so completion is derived rather than read from a flag: the obtained order must be the one currently on offer,
    /// and every target's kill count must have met that order's NeededKills.</summary>
    private static unsafe HuntBillState HuntBill(byte markIndex, HuntOrderTypeRow bill, SubrowExcelSheet<MobHuntOrder> orders, out uint? outOfRange)
    {
        outOfRange = null;
        var hunt = MobHunt.Instance();
        if (hunt == null) return HuntBillState.Unreadable;
        if (!hunt->IsMarkBillUnlocked(markIndex)) return HuntBillState.Locked;
        var available = hunt->GetAvailableHuntOrderRowId(markIndex);
        if (available <= 0) return HuntBillState.Unreadable;
        // The accessor is taken to return an absolute MobHuntOrder row id, and the sheet's own OrderStart and OrderAmount
        // say which ids are this board's. An id outside them means it is relative to the board after all, and the lookup
        // below would land on another board's targets and answer plausibly and wrongly. Say so once and read nothing.
        if (available < bill.OrderStart || available >= bill.OrderStart + bill.OrderAmount) { outOfRange = (uint)available; return HuntBillState.Unreadable; }
        if (hunt->GetObtainedHuntOrderRowId(markIndex) != available) return HuntBillState.NotDone;
        if (orders.GetRowOrDefault((uint)available) is not { } targets) return HuntBillState.Unreadable;
        for (var target = 0; target < targets.Count; target++)
        {
            if (hunt->GetKillCount(markIndex, (byte)target) < targets[target].NeededKills) return HuntBillState.NotDone;
        }
        return HuntBillState.Done;
    }

    /// <summary>The allowances left across every allied society, for the whole character. The per-questgiver route needs a
    /// raw iterator and a caller-supplied output buffer, which is why this reader counts one number instead.</summary>
    private static unsafe uint? TribeAllowanceLeft()
    {
        var quests = QuestManager.Instance();
        return quests == null ? null : quests->GetBeastTribeAllowance();
    }

    private static unsafe byte? LeveAllowances()
    {
        var quests = QuestManager.Instance();
        return quests == null ? null : quests->NumLeveAllowances;
    }

    /// <summary>Always the unix overload: the DateTime one is documented as the local time zone and every stored instant is
    /// kept in UTC. Zero means the client has not been told.</summary>
    private static long? NextLeveGrant() => QuestManager.GetNextLeveAllowancesUnixTimestamp() is var stamp and > 0 ? stamp : null;

    /// <summary>Zero or less means the field was never filled; the client fills it at login on its own. Never
    /// RequestResetTimestamps().</summary>
    private static unsafe long? NextMapAllowance()
    {
        var state = UIState.Instance();
        if (state == null) return null;
        var stamp = state->GetNextMapAllowanceTimestamp();
        return stamp > 0 ? stamp : null;
    }

    /// <summary>Tomestones earned in the current reset cycle, over the client's own weekly limit. The limit is read from the
    /// client rather than the sheet so the next tier needs no change.</summary>
    private static unsafe (int Acquired, int Limit)? WeeklyTomestone()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? null : (inventory->GetWeeklyAcquiredTomestoneCount(), InventoryManager.GetLimitedTomestoneWeeklyLimit());
    }

    /// <summary>The custom-delivery manager in one read. Cap is used plus remaining as the client states it, the twelve
    /// living as a literal inside the shipped wrapper; the total row then lowers it to what the unlocked clients can reach.
    /// Index i of PerClient is SatisfactionNpc row i + 1.</summary>
    private readonly record struct DeliveryState(int Used, int Cap, byte[] PerClient);

    private static unsafe DeliveryState? Deliveries()
    {
        var manager = SatisfactionSupplyManager.Instance();
        if (manager == null) return null;
        var used = manager->GetUsedAllowances();
        return new DeliveryState(used, used + manager->GetRemainingAllowances(), manager->UsedAllowances.ToArray());
    }

    /// <summary>Wondrous Tails in one read, so the four values cannot come from two different frames.</summary>
    private readonly record struct BingoState(int Stickers, int SecondChance, bool HasJournal, long ExpiresUnix);

    private static unsafe BingoState? Bingo()
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        return new BingoState(state->WeeklyBingoNumPlacedStickers, (int)state->WeeklyBingoNumSecondChancePoints,
            state->HasWeeklyBingoJournal, state->GetWeeklyBingoExpireUnixTimestamp());
    }

    /// <summary>The squadron and Faux Hollows stamps, all plain ints on the always-present player state.</summary>
    private readonly record struct PlayerTimerState(int SquadronMission, int SquadronTraining, int FauxHollows);

    private static unsafe PlayerTimerState? PlayerTimers()
    {
        var state = PlayerState.Instance();
        return state == null
            ? null
            : new PlayerTimerState(state->SquadronMissionCompletionTimestamp, state->SquadronTrainingCompletionTimestamp, state->FauxHollowsTimestamp);
    }
}
