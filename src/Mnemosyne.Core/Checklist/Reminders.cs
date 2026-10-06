using System.Globalization;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Checklist;

/// <summary>One thing worth saying once. Key identifies the reminder for de-duplication, Occasion is what has to change
/// before it may be said again, and Text is the line.</summary>
public sealed record Reminder(string Key, string Occasion, string Text);

/// <summary>Which of the opt-in reminders are due for one character right now. A pure function of the
/// checklist, the record, the settings and the clock, so the plugin decides nothing and this is testable without a client.
///
/// De-duplication is by occasion rather than by a timestamp: the daily-reset reminder's occasion is the daily period key,
/// an event's is the instant its run closes, and a Wondrous Tails journal's is its expiry. A reminder fires again only when
/// its occasion is a new one, so a relog, a settings edit or a five-second tick never repeats it, and a new period, a new
/// run or a new journal always does. The house-visit reminder's occasion is the deadline, so it fires once per stamp: a
/// visit moves the deadline and makes the next warning a new occasion. The My tasks line's occasion is the period that
/// is ending, so it is said once in the hour before each reset.</summary>
public static class Reminders
{
    public const string DailyResetKey = "daily-reset";
    public const string WondrousTailsKey = "wondrous-tails";
    public const string HouseVisitKey = "house-visit";
    public const string EventEndingPrefix = "event-ending:";
    public const string MyTasksDailyKey = "my-tasks-daily";
    public const string MyTasksWeeklyKey = "my-tasks-weekly";

    /// <summary>How long before a reset the My tasks line may be said.</summary>
    public static readonly TimeSpan BeforeResetWindow = TimeSpan.FromHours(1);

    public static IReadOnlyList<Reminder> Due(Checklist checklist, CharacterRecord record, Settings settings, DateTimeOffset now)
    {
        var due = new List<Reminder>();

        // Not "the boundary was crossed while the plugin watched", but "the period turned over and there is still
        // something in it": that fires correctly for a character logged in for the first time since yesterday.
        if (settings.RemindDailyReset && checklist.Today.Count > 0)
        {
            var count = checklist.Today.Count;
            Add(due, record, new Reminder(DailyResetKey, ResetSchedules.PeriodKey(ResetSchedules.Daily, now),
                $"{record.Label}: the daily reset has passed, and {count} thing{(count == 1 ? " is" : "s are")} still to do today."));
        }

        if (settings.RemindEventEnding)
        {
            foreach (var entry in checklist.EndingSoon)
            {
                if (entry.Item.Category != Category.Events || entry.Item.Availability is not { } window) continue;
                Add(due, record, new Reminder(EventEndingPrefix + entry.Item.Id, Occasion(window.End),
                    $"{record.Label}: {entry.Name} ends {Relative.Until(window.End, now)}."));
            }
        }

        if (settings.RemindWondrousTails
            && checklist.EndingSoon.FirstOrDefault(e => e.Item.Id == CatalogBuilder.WondrousTailsJournalId) is { Value: TimerValue journal })
        {
            // EndingSoon carries an overdue timer too (Assembler's own summary), and Relative.Until would otherwise read
            // "expires overdue by 2 d", which is not a sentence. A journal past its deadline reads as
            // expired instead, which is also the more useful thing to say, since there is nothing left to do about it.
            var text = journal.Deadline <= now
                ? $"{record.Label}: the Wondrous Tails journal has expired."
                : $"{record.Label}: the Wondrous Tails journal expires {Relative.Until(journal.Deadline, now)}.";
            Add(due, record, new Reminder(WondrousTailsKey, Occasion(journal.Deadline), text));
        }

        // The personal estate's visit timer. It is in Ending soon from its own warning window, the game's own thirty-day
        // warning, so the reminder reads it there as the journal's does above: an ignored row, a Content category turned
        // off, a locked row and an unstamped one never reach that list. A passed deadline is said as what may have happened.
        if (settings.RemindHouseVisit
            && checklist.EndingSoon.FirstOrDefault(e => e.Item.Id == CatalogBuilder.HouseVisitId) is { Value: TimerValue visit })
        {
            var text = visit.Deadline <= now
                ? $"{record.Label}: the house is past its visit deadline and may have been demolished."
                : $"{record.Label}: the house needs a visit; it may be demolished {Relative.Until(visit.Deadline, now)}.";
            Add(due, record, new Reminder(HouseVisitKey, Occasion(visit.Deadline), text));
        }

        // An hour before the daily and the weekly reset, the character's daily or weekly My tasks still undone. It reads the top groups, so
        // a task done, off for the role, ignored or not for this character is never named. The occasion is the period that is ending, so it is
        // said once per period and a relog inside the hour stays quiet.
        if (settings.RemindMyTasksBeforeReset)
        {
            BeforeReset(due, record, checklist.Today, ResetKind.Daily, ResetSchedules.Daily, MyTasksDailyKey, "daily", now);
            BeforeReset(due, record, checklist.ThisWeek, ResetKind.Weekly, ResetSchedules.Weekly, MyTasksWeeklyKey, "weekly", now);
        }

        return due;
    }

    private static string Occasion(DateTimeOffset instant) => instant.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>One line naming the My tasks on this reset that are placed, and so owed, when the reset is at most BeforeResetWindow away.
    /// A counted task carries its count, "Deliver (2 of 5)".</summary>
    private static void BeforeReset(List<Reminder> due, CharacterRecord record, IReadOnlyList<Entry> placed, ResetKind kind, string schedule, string key,
        string words, DateTimeOffset now)
    {
        if (ResetSchedules.NextReset(schedule, now) - now > BeforeResetWindow) return;
        var owed = placed
            .Where(e => e.Item.Freshness.Kind == FreshnessKind.Manual && e.Item.Reset.Kind == kind)
            .Select(e => e.Item.Kind == ItemKind.Counter ? $"{e.Name} ({e.Display})" : e.Name)
            .ToList();
        if (owed.Count == 0) return;
        Add(due, record, new Reminder(key, ResetSchedules.PeriodKey(schedule, now),
            $"{record.Label}: still to do before the {words} reset: {string.Join(", ", owed)}."));
    }

    private static void Add(List<Reminder> due, CharacterRecord record, Reminder reminder)
    {
        if (record.Reminded.TryGetValue(reminder.Key, out var said) && said == reminder.Occasion) return;
        due.Add(reminder);
    }
}
