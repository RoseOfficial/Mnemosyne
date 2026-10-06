using System.Globalization;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Readers;

/// <summary>What a duty's two bits say about its row.</summary>
public enum DutyState { Locked, NotDone, Done }

/// <summary>Whether the house the character stands in is their own personal estate: the whole id, or the same world,
/// ward and plot, which the reader logs apart so the two can be told apart if the fallback is ever needed.</summary>
public enum HouseMatch { None, WholeId, WorldWardPlot }

/// <summary>A house id copied out of the client for the match: the packed id and the fields the fallback compares.</summary>
public readonly record struct HouseKey(ulong Id, ushort WorldId, byte WardIndex, byte PlotIndex, bool IsApartment);

/// <summary>The Content category's pure rules over reads the reader supplies, so every rule is tested without a client,
/// as ResetGates is for the Resets category. The reader applies the rule to each answer: an AtLogin row
/// that reads locked clears, an unreadable one keeps, and a NeedsWindow row that reads locked stores a gate lock.
/// It also covers the place levels, the island rank, the house-visit rules, the rule for a gate that can close again,
/// the Crystalline Conflict tier and the Grand Company PvP ranks.</summary>
public static class ContentRules
{
    /// <summary>Completion wins over unlock: a cleared duty is done whatever its unlock bit says, because an Unreal trial's
    /// unlock drops when the rotation moves on while the clear stands (taken to be true here; not yet confirmed in game). Otherwise unlocked is a row
    /// to show and locked a row to clear.</summary>
    public static DutyState Duty(bool unlocked, bool completed) =>
        completed ? DutyState.Done : unlocked ? DutyState.NotDone : DutyState.Locked;

    /// <summary>Records found in one Variant series: the bit for a VVDNotebookContents row is its row id minus one, because the
    /// array is sized RowCount - 1 and row 0 is empty. A row the array cannot answer for is not counted.</summary>
    public static int VariantRecords(IReadOnlyList<uint> contentsIds, Func<int, bool?> bit)
    {
        var found = 0;
        foreach (var id in contentsIds)
        {
            if (id != 0 && bit((int)id - 1) == true) found++;
        }
        return found;
    }

    /// <summary>Notes done in one hunting log: ten for every rank passed, plus the notes of the rank in progress whose every
    /// used target has met its needed kills. Rank is the manager's zero-based rank in progress (taken to be right; not yet confirmed in game); a
    /// rank at or past the log's last is a finished log and reads the cap, never over it; a negative rank is an unread one
    /// and counts nothing. A target slot the sheet needs no kills of is unused and is not asked; a note with no needed kills at
    /// all is a row the sheet lacks (the reducer keeps its place) and is never counted done.</summary>
    public static int HuntingLogDone(int rank, IReadOnlyList<HuntingNoteRow> notes, int ranks, Func<int, int, int> kills)
    {
        var cap = ranks * 10;
        if (rank >= ranks) return cap;
        if (rank < 0) return 0;
        var done = rank * 10;
        foreach (var note in notes)
        {
            if (note.Rank != rank || note.NeededKills.Count == 0) continue;   // a note the sheet lacks is never counted done
            var complete = true;
            for (var target = 0; target < note.NeededKills.Count; target++)
            {
                var needed = note.NeededKills[target];
                if (needed > 0 && kills(note.Note, target) < needed) { complete = false; break; }
            }
            if (complete) done++;
        }
        return Math.Min(done, cap);
    }

    /// <summary>The series rewards claimed over the rewards earned: claimed over the smaller of the current level and the
    /// series maximum, because levels past the maximum are extra levels with no reward to claim, and a claim above the earned
    /// level is clamped rather than read as a negative gap. With nothing earned the row reads 0 of 0 and is done at cap: there
    /// is nothing outstanding.</summary>
    public static (int Claimed, int Earned) PvpClaimed(int currentLevel, int claimedLevel, int maximum)
    {
        var earned = Math.Clamp(currentLevel, 0, maximum);
        return (Math.Clamp(claimedLevel, 0, earned), earned);
    }

    /// <summary>The experience that reaches the next series level from the current one: the level table's own row for the
    /// current level, clamped to the maximum for every level at or past it, because a level's own row is the experience needed
    /// to leave it, as `JobsReader` already reads `ParamGrow`'s current-level row the same way, and extra levels cost what the
    /// last level does. Null with no table.</summary>
    public static int? PvpExpToNext(int currentLevel, IReadOnlyList<PvPSeriesLevelRow> levels)
    {
        if (levels.Count == 0) return null;
        var maximum = levels.Max(l => l.RowId);
        var row = (uint)Math.Clamp(currentLevel, 0, (int)maximum);
        return levels.FirstOrDefault(l => l.RowId == row)?.ExpToNext;
    }

    /// <summary>From the Crystal tier up, standing is the Crystal Credit rather than the riser.</summary>
    public const int CrystalTier = 6;

    /// <summary>The Crystalline Conflict tier: the current rank over the highest ColosseumMatchRank row (8, Ultima), the
    /// byte read as the row (taken to be right; not yet confirmed in game). The label names the tier and the riser, or from Crystal up the Crystal Credit, and the
    /// season. No Reset rule fits, because the server applies a new season's drop at login and no sheet dates a season, so the
    /// season in the label is what makes a stale value visible. Nothing when the season or the rank is zero: the character has
    /// no tier this season and the row clears. No second line: no sheet or page states the stars' maximum.</summary>
    public static (LevelValue Level, string Label)? CrystallineTier(string name, int season, int rank, int riser, int credit, IReadOnlyList<ColosseumRankRow> tiers)
    {
        if (season <= 0 || rank <= 0 || tiers.Count == 0) return null;
        var top = (int)tiers.Max(t => t.RowId);
        var tier = tiers.FirstOrDefault(t => t.RowId == (uint)rank)?.Name ?? $"tier {rank}";
        var standing = rank >= CrystalTier ? $"{credit.ToString("N0", CultureInfo.InvariantCulture)} credit" : riser.ToString(CultureInfo.InvariantCulture);
        return (new LevelValue(Math.Min(rank, top), top), $"{name}: {tier} {standing}, season {season}");
    }

    /// <summary>A Grand Company PvP rank: the company's rank over the highest PvPRank row whose ExpRequired is above zero
    /// (50), the cap the achievement ladder also stops at. The second line is the experience in the rank in progress over the step
    /// to the next, both from ExpRequired read as cumulative totals (inferred from the sheet's shape; taken to be right, not yet
    /// confirmed in game). It is clamped at zero, so a
    /// misread shows as no progress rather than a negative number; the reader logs the raw fields. The title is the rank's band
    /// in the company's own column. Nothing when the rank and the experience are both zero: the character has never ranked with
    /// that company, and the row clears (taken to be right, as the same rule elsewhere does). A rank past the cap reads the cap.</summary>
    public static (LevelValue Level, string Title)? PvpRank(int rank, int experience, int company, IReadOnlyList<PvPRankRow> ranks)
    {
        if (rank <= 0 && experience <= 0) return null;
        var cap = ranks.Where(r => r.ExpRequired > 0).Select(r => (int)r.RowId).DefaultIfEmpty(0).Max();
        if (cap == 0) return null;
        var row = (uint)Math.Max(rank, 0);
        var current = ranks.FirstOrDefault(r => r.RowId == row);
        var next = ranks.FirstOrDefault(r => r.RowId == row + 1);
        var level = rank < cap && current != null && next != null && next.ExpRequired > current.ExpRequired
            ? new LevelValue(rank, cap, Math.Clamp(experience - current.ExpRequired, 0, next.ExpRequired - current.ExpRequired), next.ExpRequired - current.ExpRequired)
            : new LevelValue(Math.Min(rank, cap), cap);
        var title = current != null && company >= 1 && company <= current.Titles.Count ? current.Titles[company - 1] : "";
        return (level, title);
    }

    /// <summary>What an owned estate's row is labelled: the row's own name, then the district's place name, the ward and the plot
    /// as the game numbers them, from one, with the room for an apartment or for private chambers inside a plot (the
    /// indexes are zero-based, confirmed in game). The label is drawn in place of the row's name, so the estate's
    /// name leads it: a free company estate and the chambers in it share one address and must not read as one row.</summary>
    public static string HouseLabel(string estate, string district, int wardIndex, int plotIndex, bool apartment, int roomNumber)
    {
        if (apartment) return $"{estate}: {district}, ward {wardIndex + 1}, room {roomNumber}";
        var plot = $"{estate}: {district}, ward {wardIndex + 1}, plot {plotIndex + 1}";
        return roomNumber > 0 ? $"{plot}, room {roomNumber}" : plot;
    }

    /// <summary>A level the client holds only inside its content, as a Level value: nothing while the content does
    /// not hold it, which the client answers as 0 (PlayerState.GetContentValue returns 0 for a key it does not hold; an island rank
    /// and a racing rank start at 1), so a zero never overwrites the level from the last visit. The step in progress is the
    /// second line when the client gives it and the level is below its cap.</summary>
    public static LevelValue? Level(int current, int max, int? progress = null, int? needed = null)
    {
        if (current <= 0 || max <= 0) return null;
        return current < max && progress is { } step && needed is > 0 ? new LevelValue(current, max, step, needed) : new LevelValue(current, max);
    }

    /// <summary>The Island Sanctuary rank, read only for the character's own island (IslandState.CanEditIsland, taken to be right
    /// from another plugin's own-island guard), so a visit to someone else's island never replaces the character's rank. The cap is the highest MJIRank
    /// row, and the rank's own row's ExpToNext is the step toward the next, 0 at the cap.</summary>
    public static LevelValue? IslandRank(bool ownIsland, int rank, int experience, IReadOnlyList<IslandRankRow> ranks)
    {
        if (!ownIsland || ranks.Count == 0) return null;
        var cap = (int)ranks.Max(r => r.RowId);
        return Level(rank, cap, experience, ranks.FirstOrDefault(r => r.RowId == (uint)Math.Max(rank, 0))?.ExpToNext);
    }

    /// <summary>The house-visit deadline: the demolition period after the minute the character was last seen inside. The instant is
    /// truncated to the minute, so an hour inside changes the stored value once a minute rather than on every tick, and the
    /// countdown can only run early, never late.</summary>
    public static DateTimeOffset HouseVisitDeadline(DateTimeOffset seenInside, int demolitionDays) =>
        new DateTimeOffset(seenInside.UtcTicks - seenInside.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero) + TimeSpan.FromDays(demolitionDays);

    /// <summary>Whether the house the character stands in is their own personal estate: the whole id, or, since an interior's territory
    /// may differ from its ward's, the same world, ward and plot (taken to be right; not yet confirmed in game). An apartment is never a match, because apartments
    /// are exempt from demolition, and a zero id is not a house.</summary>
    public static HouseMatch SameHouse(HouseKey owned, HouseKey inside)
    {
        if (owned.Id == 0 || inside.Id == 0 || owned.IsApartment || inside.IsApartment) return HouseMatch.None;
        if (owned.Id == inside.Id) return HouseMatch.WholeId;
        return owned.WorldId == inside.WorldId && owned.WardIndex == inside.WardIndex && owned.PlotIndex == inside.PlotIndex
            ? HouseMatch.WorldWardPlot
            : HouseMatch.None;
    }

    /// <summary>How far apart two closed reads must be for both to count: longer than a frame, so two reads of one framework update
    /// (a login capture and an interval tick) or of the two frames a loading screen collapses into count once, and shorter than the
    /// shortest live interval (the setting is clamped to 1 to 60 seconds), so consecutive ticks always count.</summary>
    public static readonly TimeSpan RevocableGateSeparation = TimeSpan.FromSeconds(1);

    /// <summary>The answer of a gate that can close again, the personal estate and the racing chocobo: a closed answer clears
    /// a value, and the house's stamp is one no reader can read back, so "closed" is believed only on a second closed read, and a
    /// closed read counts only when it comes at least RevocableGateSeparation after the last counted one; one frame's sentinel, read
    /// once or twice, changes nothing. closedReads and lastClosedAt are the caller's own state, updated
    /// here: any other answer resets the count. The cost is that a real close, and a first lock for a different character, lands on the next read
    /// after that, normally the next live tick.</summary>
    public static bool? RevocableGate(bool? open, ref int closedReads, ref DateTimeOffset lastClosedAt, DateTimeOffset now)
    {
        if (open != false)
        {
            closedReads = 0;
            return open;
        }
        if (closedReads < 2 && (closedReads == 0 || now - lastClosedAt >= RevocableGateSeparation))
        {
            closedReads++;
            lastClosedAt = now;
        }
        return closedReads < 2 ? null : false;
    }
}

/// <summary>The count a gate that can close again keeps between reads, owned by one character: RevocableGate's own
/// closedReads and lastClosedAt, plus the content id they were last counted for. A read for a different content id than the
/// one the state was last evaluated for starts the count over, so a login-frame sentinel seen once or twice on a character
/// just switched to can never inherit a count the previous character already built to two: without this, a character with
/// no personal estate or no racing chocobo believed at logout could erase a stamp on the very
/// next character logged into, which only a fresh visit restores. RevocableGate keeps its own contract; this wraps it with
/// the character key, so the reader passes the player's content id in place of the two loose fields it used to hold.</summary>
public sealed class RevocableGateState
{
    private ulong _owner;
    private int _closedReads;
    private DateTimeOffset _lastClosedAt;

    public bool? Evaluate(ulong owner, bool? open, DateTimeOffset now)
    {
        if (owner != _owner)
        {
            _owner = owner;
            _closedReads = 0;
        }
        return ContentRules.RevocableGate(open, ref _closedReads, ref _lastClosedAt, now);
    }
}
