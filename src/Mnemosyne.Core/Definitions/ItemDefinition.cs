using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Definitions;

/// <summary>The four kinds of item.</summary>
public enum ItemKind { Done, Counter, Level, Timer }

/// <summary>How a value can be obtained. Manual: only a tick by hand sets it, and no reader ever
/// touches it.</summary>
public enum FreshnessKind { AtLogin, NeedsWindow, NeedsPlace, ObservedOnly, Manual }

/// <summary>Gate names the window or the territory; empty for AtLogin, ObservedOnly and Manual.</summary>
public sealed record Freshness(FreshnessKind Kind, string Gate = "")
{
    public static readonly Freshness AtLogin = new(FreshnessKind.AtLogin);
    public static readonly Freshness Observed = new(FreshnessKind.ObservedOnly);
    public static readonly Freshness Manual = new(FreshnessKind.Manual);
    public static Freshness Window(string gate) => new(FreshnessKind.NeedsWindow, gate);
    public static Freshness Place(string territory) => new(FreshnessKind.NeedsPlace, territory);
}

/// <summary>The five kinds of reset rule.</summary>
public enum ResetKind { None, Daily, Weekly, Accrual, Window }

/// <summary>Schedule is a ResetSchedules name. Accrual items gain AccrualAmount at every grant of their schedule, up to the value's cap.</summary>
public sealed record ResetRule(ResetKind Kind, string Schedule = "", int AccrualAmount = 0)
{
    public static readonly ResetRule None = new(ResetKind.None);
    public static readonly ResetRule Daily = new(ResetKind.Daily, ResetSchedules.Daily);
    public static readonly ResetRule Weekly = new(ResetKind.Weekly, ResetSchedules.Weekly);
    public static readonly ResetRule Window = new(ResetKind.Window);
    public static ResetRule DailyOn(string schedule) => new(ResetKind.Daily, schedule);
    public static ResetRule WeeklyOn(string schedule) => new(ResetKind.Weekly, schedule);
    public static ResetRule Accrual(string schedule, int amount) => new(ResetKind.Accrual, schedule, amount);
}

/// <summary>When an item can be done. Both ends UTC; End is exclusive.</summary>
public sealed record Availability(DateTimeOffset Start, DateTimeOffset End);

/// <summary>A window that recurs with the item's own reset schedule: it opens OpensAfter the period start and closes
/// ClosesAfter it, so no dates are carried and it closes as the period rolls, which is why "past" never happens for it.
/// The Fashion Report's judging opens 72 hours after the Tuesday 08:00 UTC reset and closes at the next one. Both ends
/// UTC; End is exclusive, as Availability's is.</summary>
public sealed record Recurrence(TimeSpan OpensAfter, TimeSpan ClosesAfter)
{
    /// <summary>The occurrence of this window in the period that contains now, on the given schedule.</summary>
    public Availability WindowFor(string schedule, DateTimeOffset now)
    {
        var start = ResetSchedules.PeriodStart(schedule, now);
        return new Availability(start + OpensAfter, start + ClosesAfter);
    }
}

/// <summary>IsMsq marks the quests the matrix uses for "MSQ position". GroupOrder is the display order of
/// the group within its category (0 when none). DoneAtCap is true for used and progress counters, which are done at their
/// cap, and false for held counters such as currencies, seals and free space, which never are. ReusedQuest marks an event
/// occurrence whose event reuses one set of quest rows across its returns, so a completion only counts for this occurrence
/// when the plugin saw it happen inside this window. CarriesOver marks a periodic Counter whose stored
/// count survives its own reset boundary — the boundary still decides which top group the row lands in, but it never
/// zeroes the count. ReadyWhenPassed marks a Timer whose passed deadline means the thing is available again, so it
/// reads "ready" rather than "overdue by": the treasure map, whose deadline on a character with no cooldown is the login
/// instant. Recurrence is a window derived from the item's reset schedule for the current period, for an item
/// that is doable in only part of its period; it excludes a fixed Availability. RemindWithin is the own warning window
/// of a Timer with no reset rule: the Timer enters Ending soon this close to its deadline in place of the ending-soon
/// setting, and its opt-in reminder fires then. The house visit's 15 days are the game's own 30-day warning against its
/// 45-day demolition. HiddenWhenBlank marks a row listed only while it says
/// something: it is left out of its section while nothing is stored on it after its category was read, or while a
/// lock is stored, which is how a Money row for a currency the player does not hold, or a retainer slot the player does not use,
/// stays out of the player's way. Every Money row but gil carries it. Target is a counted My task's number:
/// a Manual Counter is done at it, and reads its count against it rather than the cap stored with the last
/// click, so an edit of the number reaches every character at once. Null for every other item. IconId is the game icon
/// drawn beside the row: a job row's framed job icon and a currency row's item
/// icon, both from the sheets already read, and null for every other row. No text form prints it.</summary>
public sealed record ItemDefinition(
    string Id,
    Category Category,
    string Group,
    string Name,
    ItemKind Kind,
    ResetRule Reset,
    int Order,
    Freshness Freshness,
    Availability? Availability = null,
    bool IsMsq = false,
    int GroupOrder = 0,
    bool DoneAtCap = true,
    bool ReusedQuest = false,
    bool CarriesOver = false,
    bool ReadyWhenPassed = false,
    Recurrence? Recurrence = null,
    TimeSpan? RemindWithin = null,
    bool HiddenWhenBlank = false,
    int? Target = null,
    uint? IconId = null)
{
    /// <summary>The id an ignore of the whole group is stored under.</summary>
    public string GroupKey => $"group:{Category}/{Group}";
}
