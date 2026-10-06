using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class ReminderTests
{
    private static readonly DateTimeOffset Now = Items.Now;

    private static readonly ItemDefinition Journal = new(CatalogBuilder.WondrousTailsJournalId, Category.Resets, "Timers",
        "Wondrous Tails journal", ItemKind.Timer, ResetRule.None, 0, Freshness.AtLogin);

    private static IReadOnlyList<Reminder> Due(Catalog catalog, CharacterRecord record, Settings settings, DateTimeOffset now) =>
        Reminders.Due(Assembler.Assemble(catalog, record, settings, now), record, settings, now);

    [Fact]
    public void The_daily_reset_reminder_fires_once_per_period_and_only_with_something_left()
    {
        var settings = new Settings { RemindDailyReset = true };
        var record = Items.Record();
        record.Values[Items.Roulette.Id] = new DoneValue(true, ResetSchedules.PeriodKey(ResetSchedules.Daily, Now));
        Assert.Empty(Due(new Catalog([Items.Roulette]), record, settings, Now));   // nothing left today

        record.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-11");
        var first = Assert.Single(Due(new Catalog([Items.Roulette]), record, settings, Now));
        Assert.Equal("daily-reset", first.Key);
        Assert.Equal(ResetSchedules.PeriodKey(ResetSchedules.Daily, Now), first.Occasion);
        Assert.Contains("Aria Whitewind", first.Text);

        record.Reminded[first.Key] = first.Occasion;
        Assert.Empty(Due(new Catalog([Items.Roulette]), record, settings, Now));                        // said already
        Assert.Single(Due(new Catalog([Items.Roulette]), record, settings, Now.AddDays(1)));            // the next period
    }

    [Fact]
    public void The_event_ending_reminder_fires_once_per_run()
    {
        var settings = new Settings { RemindEventEnding = true };
        var record = Items.Record();
        record.Values[Items.Rising.Id] = new DoneValue(false);
        var catalog = new Catalog([Items.Rising]);   // ends 2026-09-10 08:00, so read it two days before its close

        var when = Items.Rising.Availability!.End - TimeSpan.FromDays(2);
        var reminder = Assert.Single(Due(catalog, record, settings, when));
        Assert.Equal("event-ending:" + Items.Rising.Id, reminder.Key);
        Assert.Contains("The Rising 2026", reminder.Text);

        record.Reminded[reminder.Key] = reminder.Occasion;
        Assert.Empty(Due(catalog, record, settings, when));
        Assert.Empty(Due(catalog, record, settings, when.AddHours(6)));   // the same run, a later tick

        record.Values[Items.Rising.Id] = new DoneValue(true);
        Assert.Empty(Due(catalog, record, settings, when));               // done, so no longer ending soon
    }

    [Fact]
    public void The_wondrous_tails_reminder_fires_once_per_journal()
    {
        var settings = new Settings { RemindWondrousTails = true };
        var record = Items.Record();
        var catalog = new Catalog([Journal]);
        var deadline = Now + TimeSpan.FromDays(2);
        record.Values[Journal.Id] = new TimerValue(deadline);

        var reminder = Assert.Single(Due(catalog, record, settings, Now));
        Assert.Equal("wondrous-tails", reminder.Key);
        Assert.Contains("expires", reminder.Text);
        record.Reminded[reminder.Key] = reminder.Occasion;
        Assert.Empty(Due(catalog, record, settings, Now));

        // A new journal is a new deadline, so the reminder is a new occasion.
        record.Values[Journal.Id] = new TimerValue(deadline + TimeSpan.FromDays(7));
        Assert.Empty(Due(catalog, record, settings, Now));                                   // not ending soon yet
        Assert.Single(Due(catalog, record, settings, Now + TimeSpan.FromDays(7)));

        // A journal whose deadline has already passed reads as expired, not as an ungrammatical negative countdown.
        record.Values[Journal.Id] = new TimerValue(Now - TimeSpan.FromHours(1));
        var expired = Assert.Single(Due(catalog, record, settings, Now));
        Assert.Contains("has expired", expired.Text);
        Assert.DoesNotContain("overdue", expired.Text);
    }

    [Fact]
    public void Reminders_that_are_off_never_fire()
    {
        // `when` is 2026-09-08 08:00, two days before The Rising 2026 closes. The daily period there is "daily:2026-09-07",
        // so the stored "daily:2026-09-11" is not the current one and the roulette is still on Today.
        var record = Items.Record();
        record.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-11");
        record.Values[Items.Rising.Id] = new DoneValue(false);
        var catalog = new Catalog([Items.Roulette, Items.Rising, Journal]);
        var when = Items.Rising.Availability!.End - TimeSpan.FromDays(2);
        var all = new Settings { RemindDailyReset = true, RemindEventEnding = true, RemindWondrousTails = true };

        // A journal that expires a day after `when` is inside the three-day window, so all three fire.
        record.Values[Journal.Id] = new TimerValue(when + TimeSpan.FromDays(1));
        Assert.Empty(Due(catalog, record, new Settings(), when));    // every Remind* default is off
        Assert.Equal(3, Due(catalog, record, all, when).Count);

        // A journal six days out is not ending soon, so the Wondrous Tails reminder has nothing to fire about and the
        // other two are unaffected.
        record.Values[Journal.Id] = new TimerValue(when + TimeSpan.FromDays(6));
        Assert.Equal(2, Due(catalog, record, all, when).Count);
        Assert.DoesNotContain(Due(catalog, record, all, when), r => r.Key == Reminders.WondrousTailsKey);
    }

    private static readonly ItemDefinition HouseVisit = new(CatalogBuilder.HouseVisitId, Category.Content, "Housing", "House visit due",
        ItemKind.Timer, ResetRule.None, 10, Freshness.Place("your house"), RemindWithin: TimeSpan.FromDays(15));

    [Fact]
    public void The_house_visit_reminder_fires_once_per_stamp_from_the_games_own_thirty_day_warning()
    {
        var settings = new Settings { RemindHouseVisit = true };
        var record = Items.Record();
        var catalog = new Catalog([HouseVisit]);

        // Last seen inside twenty days ago: twenty-five days left, before the game's own thirty-day warning.
        record.Values[HouseVisit.Id] = new TimerValue(Now + TimeSpan.FromDays(25));
        Assert.Empty(Due(catalog, record, settings, Now));

        // Thirty-one days: fourteen left, inside the warning.
        record.Values[HouseVisit.Id] = new TimerValue(Now + TimeSpan.FromDays(14));
        var reminder = Assert.Single(Due(catalog, record, settings, Now));
        Assert.Equal(Reminders.HouseVisitKey, reminder.Key);
        Assert.Contains("Aria Whitewind", reminder.Text);
        Assert.Contains("in 14 d", reminder.Text);
        record.Reminded[reminder.Key] = reminder.Occasion;
        Assert.Empty(Due(catalog, record, settings, Now));                           // said already
        Assert.Empty(Due(catalog, record, settings, Now + TimeSpan.FromDays(3)));    // the same stamp, later ticks

        // A visit moves the deadline: a new occasion, silent until its own warning.
        record.Values[HouseVisit.Id] = new TimerValue(Now + TimeSpan.FromDays(45));
        Assert.Empty(Due(catalog, record, settings, Now));
        Assert.Single(Due(catalog, record, settings, Now + TimeSpan.FromDays(31)));

        // Past the deadline it says what may have happened, not "overdue by".
        record.Values[HouseVisit.Id] = new TimerValue(Now - TimeSpan.FromDays(1));
        var late = Assert.Single(Due(catalog, record, settings, Now));
        Assert.Contains("may have been demolished", late.Text);
        Assert.DoesNotContain("overdue", late.Text);
    }

    [Fact]
    public void The_house_visit_reminder_is_silent_when_off_locked_unstamped_or_ignored()
    {
        var record = Items.Record();
        var catalog = new Catalog([HouseVisit]);
        record.Values[HouseVisit.Id] = new TimerValue(Now + TimeSpan.FromDays(3));
        Assert.Empty(Due(catalog, record, new Settings(), Now));                     // off by default
        var on = new Settings { RemindHouseVisit = true };
        Assert.Single(Due(catalog, record, on, Now));

        record.Ignored.Add(HouseVisit.Id);
        Assert.Empty(Due(catalog, record, on, Now));                                 // ignored on this character
        record.Ignored.Clear();
        record.Values[HouseVisit.Id] = GateValue.Shut;
        Assert.Empty(Due(catalog, record, on, Now));                                 // no personal estate
        record.Values.Remove(HouseVisit.Id);
        Assert.Empty(Due(catalog, record, on, Now));                                 // never stamped: unknown, never a guess
    }

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    [Fact]
    public void The_my_tasks_reminder_names_the_undone_dailies_in_the_hour_before_the_daily_reset_once()
    {
        // Off by default. On, it says nothing at 13:59 UTC, then one line from 14:00 naming the undone daily My tasks in the player's
        // order, a counted one with its count, the done ones and every other kind left out; once per period; nothing from 15:00, when
        // the reset has passed, and nothing when every daily is done.
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var fed = settings.AddMyTask(Tasks.Draft("Feed the chocobo", MyTask.Daily))!;
        settings.AddMyTask(Tasks.Draft("Buy the mount", MyTask.Once));
        settings.AddMyTask(Tasks.Draft("Hourly check", MyTask.Every, 1));
        settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly));
        var catalog = new Catalog(settings.MyTasks.Items());
        var record = Items.Record();
        record.Values[deliver.Id] = new CounterValue(2, 5, At(13, 9));
        record.Values[fed.Id] = new DoneValue(true, ResetSchedules.PeriodKey(ResetSchedules.Daily, At(13, 9)), At(13, 9));

        Assert.Empty(Due(catalog, record, settings, At(13, 14, 30)));                       // off by default
        settings.RemindMyTasksBeforeReset = true;
        Assert.Empty(Due(catalog, record, settings, At(13, 13, 59)));                       // not yet
        var line = Assert.Single(Due(catalog, record, settings, At(13, 14)));
        Assert.Equal(Reminders.MyTasksDailyKey, line.Key);
        Assert.Equal("daily:2026-09-12", line.Occasion);
        Assert.Equal($"{record.Label}: still to do before the daily reset: FC chest, Deliver (2 of 5).", line.Text);
        Assert.Single(Due(catalog, record, settings, At(13, 14, 59)));

        record.Reminded[line.Key] = line.Occasion;
        Assert.Empty(Due(catalog, record, settings, At(13, 14, 59)));                       // said already this period
        Assert.Empty(Due(catalog, record, settings, At(13, 15)));                           // the reset has passed
        Assert.Equal("daily:2026-09-13", Assert.Single(Due(catalog, record, settings, At(14, 14, 30))).Occasion);   // the next day's hour

        record.Values[chest.Id] = new DoneValue(true, "daily:2026-09-13", At(14, 10));
        record.Values[deliver.Id] = new CounterValue(5, 5, At(14, 10));
        record.Values[fed.Id] = new DoneValue(true, "daily:2026-09-13", At(14, 10));
        Assert.Empty(Due(catalog, record, settings, At(14, 14, 30)));                       // everything done
    }

    [Fact]
    public void The_my_tasks_reminder_names_the_undone_weeklies_in_the_hour_before_the_weekly_reset()
    {
        // The weekly reset is Tuesday 08:00 UTC, so the line comes from 07:00 that Tuesday, naming the weekly My tasks alone, once per week.
        var settings = new Settings { RemindMyTasksBeforeReset = true };
        settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly));
        settings.AddMyTask(Tasks.Counted("Hunts", MyTask.Weekly, 3));
        var catalog = new Catalog(settings.MyTasks.Items());
        var record = Items.Record();

        Assert.Empty(Due(catalog, record, settings, At(15, 6, 59)));
        var line = Assert.Single(Due(catalog, record, settings, At(15, 7, 30)));
        Assert.Equal(Reminders.MyTasksWeeklyKey, line.Key);
        Assert.Equal("weekly:2026-09-08", line.Occasion);
        Assert.Equal($"{record.Label}: still to do before the weekly reset: Granary, Hunts (0 of 3).", line.Text);
        Assert.Empty(Due(catalog, record, settings, At(15, 8)));
    }

    [Fact]
    public void The_my_tasks_reminder_leaves_out_a_task_off_for_the_role_or_not_for_the_character()
    {
        // The line reads the character's own top groups, so a task off for its role (never placed) and a task picked for another
        // character (not in its checklist at all) are never named; with nothing left to name there is no line.
        var settings = new Settings { RemindMyTasksBeforeReset = true };
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Role.CategoryKey(Category.MyTasks));
        settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily));
        settings.AddMyTask(Tasks.Draft("Alt errand", MyTask.Daily, null, MyTask.ForPicked, 2));
        var catalog = new Catalog(settings.MyTasks.Items());
        var record = Items.Record();

        Assert.Equal($"{record.Label}: still to do before the daily reset: FC chest.", Assert.Single(Due(catalog, record, settings, At(13, 14, 30))).Text);
        record.Role = mule.Id;
        Assert.Empty(Due(catalog, record, settings, At(13, 14, 30)));
    }
}
