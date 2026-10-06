using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

/// <summary>The board, built from each character's own checklist. Now is Sunday 12:00 UTC: the daily reset is 3 h away and the weekly one,
/// Tuesday 08:00, 1 d 20 h away.</summary>
public class BoardTests
{
    /// <summary>A character as the plugin hands it to the board: its record and its checklist, assembled at the given instant.</summary>
    private static (CharacterRecord Record, Mnemosyne.Core.Checklist.Checklist Checklist) Owes(CharacterRecord record, Catalog? catalog = null, DateTimeOffset? now = null, Settings? settings = null) =>
        (Record: record, Checklist: Assembler.Assemble(catalog ?? Items.Catalog(), record, settings ?? new Settings(), now ?? Items.Now));

    private static TaskBoard Build(TaskPreferences? preferences, params (CharacterRecord Record, Mnemosyne.Core.Checklist.Checklist Checklist)[] characters) =>
        Board.Build(characters, preferences ?? new TaskPreferences(), Items.Now);

    private static ItemDefinition Timer(string name) =>
        new($"timer:{name.ToLowerInvariant()}", Category.Resets, "Timers", name, ItemKind.Timer, ResetRule.None, 1, Freshness.AtLogin);

    [Fact]
    public void Membership_is_every_top_group_of_every_character_shown_and_an_ignored_or_done_task_is_never_listed()
    {
        // A task is listed while some character owes it in Ending soon, Today, This week or Running events. A hidden
        // character counts for nothing, even for a task only it owes; an ignored item never reaches a top group; a
        // character on which a task is done does not count.
        var aria = SummariserTests.Aria();                    // Today: tribal, cactpot; This week: tails; Ending soon: Moonfire, venture
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[Items.Moonfire.Id] = new DoneValue(true);
        kestrel.Ignored.Add(Items.Cactpot.Id);
        var hidden = Items.Record(3, "Hidden Alt");
        hidden.Hidden = true;
        hidden.Values[Items.Stickers.Id] = new CounterValue(0, 9, Items.Now);
        hidden.Values[Items.Roulette.Id] = new DoneValue(false);

        var board = Build(null, Owes(aria), Owes(kestrel), Owes(hidden));

        Assert.Equal([Items.Venture.Id, Items.Moonfire.Id], board.In(BoardGroup.EndingSoon).Select(l => l.Id));
        Assert.Equal([Items.Cactpot.Id, Items.Allowances.Id], board.In(BoardGroup.Today).Select(l => l.Id));
        Assert.Equal([Items.Stickers.Id], board.In(BoardGroup.ThisWeek).Select(l => l.Id));
        Assert.Empty(board.In(BoardGroup.Pinned));
        Assert.Empty(board.In(BoardGroup.Running));
        Assert.Empty(board.Hidden);
        Assert.Empty(board.Expired);
        Assert.DoesNotContain(board.Groups.SelectMany(g => g.Lines), l => l.Id == Items.Roulette.Id);   // only the hidden character owes it
        Assert.All(board.Groups.SelectMany(g => g.Lines), line => Assert.Equal([1UL], line.Characters.Select(c => c.Record.ContentId)));
    }

    [Fact]
    public void A_task_in_two_groups_on_two_characters_is_listed_once_in_the_more_urgent_group_with_both_under_it()
    {
        // The treasure map is close on Aria (Ending soon) and overdue on Kestrel (Today). One line, under Ending soon,
        // which ranks first, holding both; its time is the earliest deadline among them, Kestrel's. Kestrel, whose group
        // is the less urgent, is given first, so a group taken from the first character would put the line under Today
        // and fail here.
        var aria = Items.Record(1);
        aria.Values[Items.Map.Id] = new TimerValue(Items.Now + TimeSpan.FromDays(1));
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[Items.Map.Id] = new TimerValue(Items.Now - TimeSpan.FromHours(2));
        Assert.Contains(Owes(aria).Checklist.EndingSoon, e => e.Item.Id == Items.Map.Id);     // the premise, on each character's own list
        Assert.Contains(Owes(kestrel).Checklist.Today, e => e.Item.Id == Items.Map.Id);

        var board = Build(null, Owes(kestrel), Owes(aria));

        var line = Assert.Single(board.In(BoardGroup.EndingSoon), l => l.Id == Items.Map.Id);
        Assert.DoesNotContain(board.In(BoardGroup.Today), l => l.Id == Items.Map.Id);
        Assert.Equal(BoardGroup.EndingSoon, line.Group);
        Assert.Equal([2UL, 1UL], line.Characters.Select(c => c.Record.ContentId));
        Assert.Equal(2, line.Count);
        Assert.Equal(Items.Now - TimeSpan.FromHours(2), line.Urgent);
        Assert.Equal("overdue by 2 h", line.When);
    }

    [Fact]
    public void The_most_urgent_time_is_the_earliest_deadline_in_ending_soon_and_running_and_the_tasks_own_next_reset_in_today_and_this_week()
    {
        // A Timer shows its deadline in every group, since a Timer is a deadline, not a period; one that says so reads
        // "ready" once passed, as the catalog's treasure map does (CatalogBuilder: ResetRule.None, ReadyWhenPassed).
        var longEvent = new ItemDefinition("event:long", Category.Events, "Long", "A long event", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
            new Availability(Items.Now - TimeSpan.FromDays(10), Items.Now + TimeSpan.FromDays(20)));
        var readyMap = new ItemDefinition("reset:treasure-map", Category.Resets, "Allowances", "Treasure map", ItemKind.Timer, ResetRule.None, 1, Freshness.AtLogin,
            ReadyWhenPassed: true);
        var catalog = new Catalog([.. Items.Catalog().Items, longEvent, readyMap]);
        var aria = SummariserTests.Aria();                     // the venture ends in 18 h here
        aria.Values[longEvent.Id] = new DoneValue(false);
        aria.Values[readyMap.Id] = new TimerValue(Items.Now - TimeSpan.FromHours(3));
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(5));
        kestrel.Values[longEvent.Id] = new DoneValue(false);
        kestrel.Values[Items.Map.Id] = new TimerValue(Items.Now - TimeSpan.FromDays(1) - TimeSpan.FromHours(2));   // overdue: lands in Today, not Ending soon

        var board = Build(null, Owes(aria, catalog), Owes(kestrel, catalog));

        var venture = board.In(BoardGroup.EndingSoon).Single(l => l.Id == Items.Venture.Id);
        Assert.Equal(Items.Now + TimeSpan.FromHours(5), venture.Urgent);            // the earliest of 18 h and 5 h
        Assert.Equal("ends in 5 h", venture.When);
        Assert.Null(venture.NextReset);                                              // a timer with no schedule
        var ready = board.In(BoardGroup.EndingSoon).Single(l => l.Id == readyMap.Id);
        Assert.Equal(Items.Now - TimeSpan.FromHours(3), ready.Urgent);
        Assert.Equal("ready", ready.When);                                           // never "overdue by 3 h"
        Assert.Equal("ready", Assert.Single(ready.Characters).Entry.Display);
        var running = board.In(BoardGroup.Running).Single();
        Assert.Equal(longEvent.Id, running.Id);
        Assert.Equal("ends in 20 d", running.When);
        var tribal = board.In(BoardGroup.Today).Single(l => l.Id == Items.Allowances.Id);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero), tribal.Urgent);
        Assert.Equal("resets in 3 h", tribal.When);
        var tails = board.In(BoardGroup.ThisWeek).Single();
        Assert.Equal(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero), tails.NextReset);
        Assert.Equal("resets in 1 d 20 h", tails.When);
        var overdueTimer = board.In(BoardGroup.Today).Single(l => l.Id == Items.Map.Id);   // an Accrual Timer, overdue, in Today (e.g. squadron training)
        Assert.Equal(Items.Now - TimeSpan.FromDays(1) - TimeSpan.FromHours(2), overdueTimer.Urgent);
        Assert.Equal("overdue by 1 d 2 h", overdueTimer.When);                             // its own deadline, never "resets in"
    }

    [Fact]
    public void A_task_owed_on_two_data_centres_takes_the_earliest_of_their_resets_whichever_character_is_given_first()
    {
        // The Jumbo Cactpot's draw is per data centre (ResetSchedules.Jumbo): North America draws on Sunday at 02:00 UTC
        // and Europe on Saturday at 19:00. From Sunday 12:00 the European draw comes first, so the line's reset, and a
        // snooze taken from it, is Saturday's, in either order.
        var jumbo = new ItemDefinition("reset:cactpot:jumbo", Category.Resets, "Jumbo Cactpot", "Jumbo Cactpot tickets", ItemKind.Counter,
            ResetRule.WeeklyOn(ResetSchedules.Jumbo), 0, Freshness.Observed);
        var catalog = new Catalog([jumbo]);
        var crystal = Items.Record(1);                                  // Crystal: the North American draw
        var light = Items.Record(2, "Kestrel Vane");
        light.DataCenter = "Light";                                     // the European draw
        var saturday = new DateTimeOffset(2026, 9, 19, 19, 0, 0, TimeSpan.Zero);

        foreach (var order in new[] { new[] { crystal, light }, new[] { light, crystal } })
        {
            var line = Assert.Single(Build(null, order.Select(record => Owes(record, catalog)).ToArray()).In(BoardGroup.ThisWeek));
            Assert.Equal(2, line.Count);
            Assert.Equal(saturday, line.NextReset);
            Assert.Equal("resets in 6 d 7 h", line.When);
        }
    }

    [Fact]
    public void The_default_order_runs_by_priority_then_the_most_urgent_time_then_the_most_characters_then_the_name()
    {
        // One case per sort key, all in Ending soon: Golf is High and Foxtrot Low, whatever their times; Bravo is sooner
        // than the rest; at one instant Delta has two characters; at one instant and one count, the name decides.
        (string Name, int Hours)[] timers = [("Alpha", 10), ("Bravo", 5), ("Charlie", 10), ("Delta", 10), ("Echo", 10), ("Foxtrot", 1), ("Golf", 20)];
        var aria = Items.Record(1);
        foreach (var (name, hours) in timers) aria.Values[Timer(name).Id] = new TimerValue(Items.Now + TimeSpan.FromHours(hours));
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[Timer("Delta").Id] = new TimerValue(Items.Now + TimeSpan.FromHours(10));
        var preferences = new TaskPreferences();
        preferences.SetPriority(Timer("Golf").Id, TaskPriority.High);
        preferences.SetPriority(Timer("Foxtrot").Id, TaskPriority.Low);
        var catalog = new Catalog(timers.Select(t => Timer(t.Name)));

        var board = Build(preferences, Owes(aria, catalog), Owes(kestrel, catalog));

        Assert.Equal(["Golf", "Bravo", "Delta", "Alpha", "Charlie", "Echo", "Foxtrot"], board.In(BoardGroup.EndingSoon).Select(l => l.Name));

        // The name key alone. Above, each id is its lowercased name and the catalog lists the timers in name order, so the id tie-break and the
        // catalog's order would give the same answer. Real ids are sheet ids, whose order is not the names'. Here two tasks share one instant
        // and one character, and their names run opposite to their ids and to the catalog's order: Zulu is "timer:a" and listed first.
        var zulu = Timer("Zulu") with { Id = "timer:a" };
        var alpha = Timer("Alpha") with { Id = "timer:z" };
        var single = Items.Record(1);
        single.Values[zulu.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(10));
        single.Values[alpha.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(10));
        var named = Build(null, Owes(single, new Catalog([zulu, alpha])));
        Assert.Equal(["Alpha", "Zulu"], named.In(BoardGroup.EndingSoon).Select(l => l.Name));
    }

    [Fact]
    public void A_pinned_task_moves_to_pinned_is_listed_only_while_owed_and_comes_back_pinned_after_nobody_owed_it()
    {
        // The board changes nothing it is given, so the pin stays saved while nobody owes the task.
        var preferences = new TaskPreferences();
        preferences.Pin(Items.Stickers.Id);
        preferences.Pin("event:gone:2025");                                        // an id the catalog no longer has: kept, does nothing
        var aria = SummariserTests.Aria();

        var board = Build(preferences, Owes(aria));
        Assert.Equal([Items.Stickers.Id], board.In(BoardGroup.Pinned).Select(l => l.Id));
        Assert.Equal(BoardGroup.ThisWeek, board.In(BoardGroup.Pinned)[0].Group);   // its own group travels with it
        Assert.Empty(board.In(BoardGroup.ThisWeek));

        aria.Values[Items.Stickers.Id] = new CounterValue(9, 9, Items.Now);       // done on the only character that owed it
        var done = Build(preferences, Owes(aria));
        Assert.DoesNotContain(done.Groups.SelectMany(g => g.Lines), l => l.Id == Items.Stickers.Id);
        Assert.Empty(done.Hidden);
        Assert.True(preferences.Get(Items.Stickers.Id).Pinned);
        Assert.True(preferences.Get("event:gone:2025").Pinned);

        // Past the weekly reset the count is assumed back at zero, the task is owed again, and it returns under Pinned.
        var tuesday = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);
        var back = Board.Build([Owes(aria, now: tuesday)], preferences, tuesday);
        Assert.Equal([Items.Stickers.Id], back.In(BoardGroup.Pinned).Select(l => l.Id));
    }

    [Fact]
    public void Priority_orders_within_a_group_and_among_pins_after_each_pins_own_group()
    {
        // Every task starts at Normal priority. A pin keeps its priority, which orders it among the pins after each pin's own group.
        var aria = SummariserTests.Aria();
        var preferences = new TaskPreferences();
        preferences.SetPriority(Items.Allowances.Id, TaskPriority.High);
        Assert.Equal([Items.Allowances.Id, Items.Cactpot.Id], Build(preferences, Owes(aria)).In(BoardGroup.Today).Select(l => l.Id));

        preferences.Pin(Items.Stickers.Id);                                        // This week, High
        preferences.SetPriority(Items.Stickers.Id, TaskPriority.High);
        preferences.Pin(Items.Cactpot.Id);                                         // Today, Low
        preferences.SetPriority(Items.Cactpot.Id, TaskPriority.Low);
        preferences.Pin(Items.Allowances.Id);                                      // Today, High
        preferences.Pin(Items.Moonfire.Id);                                        // Ending soon, Normal
        var pinned = Build(preferences, Owes(aria)).In(BoardGroup.Pinned);
        Assert.Equal([Items.Moonfire.Id, Items.Allowances.Id, Items.Cactpot.Id, Items.Stickers.Id], pinned.Select(l => l.Id));
    }

    [Fact]
    public void A_snooze_holds_until_the_tasks_own_next_reset_one_minute_away_or_not_and_its_passed_instant_is_returned_for_saving()
    {
        // At 14:59 the tribal allowances snooze until their daily reset at 15:00, and Moonfire, an event with no schedule,
        // until the same daily reset. Both leave the list for the Hidden (n) lines until then.
        var aria = SummariserTests.Aria();
        var at = new DateTimeOffset(2026, 9, 13, 14, 59, 0, TimeSpan.Zero);
        var fifteen = new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);
        var preferences = new TaskPreferences();
        var before = Board.Build([Owes(aria, now: at)], preferences, at);
        foreach (var line in before.Groups.SelectMany(g => g.Lines).Where(l => l.Id == Items.Allowances.Id || l.Id == Items.Moonfire.Id))
            preferences.Snooze(line.Id, line.NextReset, at);
        Assert.Equal(fifteen, preferences.Get(Items.Allowances.Id).SnoozedUntil);
        Assert.Equal(fifteen, preferences.Get(Items.Moonfire.Id).SnoozedUntil);

        var snoozed = Board.Build([Owes(aria, now: at)], preferences, at);
        Assert.DoesNotContain(snoozed.Groups.SelectMany(g => g.Lines), l => l.Id == Items.Allowances.Id || l.Id == Items.Moonfire.Id);
        Assert.Equal([Items.Moonfire.Id, Items.Allowances.Id], snoozed.Hidden.Select(l => l.Id));   // each after its own group
        Assert.Empty(snoozed.Expired);

        var justBefore = fifteen - TimeSpan.FromTicks(1);
        Assert.Equal(2, Board.Build([Owes(aria, now: justBefore)], preferences, justBefore).Hidden.Count);
        var expired = Board.Build([Owes(aria, now: fifteen)], preferences, fifteen);            // exactly at the instant: back, and named for saving
        Assert.Contains(expired.In(BoardGroup.EndingSoon), l => l.Id == Items.Moonfire.Id);
        Assert.Contains(expired.In(BoardGroup.Today), l => l.Id == Items.Allowances.Id);
        Assert.Empty(expired.Hidden);
        Assert.Equal([Items.Moonfire.Id, Items.Allowances.Id], expired.Expired);
        foreach (var id in expired.Expired) preferences.Wake(id);
        Assert.Empty(preferences);
    }

    [Fact]
    public void Hide_and_snooze_win_over_pin_and_the_hidden_lines_hold_the_hidden_and_the_snoozed_tasks_until_unhidden_or_woken()
    {
        // A pinned task that is hidden or snoozed is out of the list and keeps its pin.
        var aria = SummariserTests.Aria();
        var preferences = new TaskPreferences();
        preferences.Pin(Items.Stickers.Id);
        preferences.Hide(Items.Stickers.Id);
        preferences.Pin(Items.Venture.Id);
        preferences.Snooze(Items.Venture.Id, null, Items.Now);
        preferences.Hide(Items.Cactpot.Id);

        var board = Build(preferences, Owes(aria));
        Assert.Empty(board.In(BoardGroup.Pinned));
        Assert.Equal([Items.Venture.Id, Items.Cactpot.Id, Items.Stickers.Id], board.Hidden.Select(l => l.Id));   // each by its own group
        Assert.True(board.Hidden[0].Preference.Pinned);
        Assert.Equal([Items.Allowances.Id], board.In(BoardGroup.Today).Select(l => l.Id));

        preferences.Unhide(Items.Stickers.Id);
        preferences.Wake(Items.Venture.Id);
        preferences.Unhide(Items.Cactpot.Id);
        var back = Build(preferences, Owes(aria));
        Assert.Empty(back.Hidden);
        Assert.Equal([Items.Venture.Id, Items.Stickers.Id], back.In(BoardGroup.Pinned).Select(l => l.Id));
        Assert.Equal([Items.Cactpot.Id, Items.Allowances.Id], back.In(BoardGroup.Today).Select(l => l.Id));
    }

    [Fact]
    public void An_item_numbered_per_character_is_one_line_per_slot_under_the_catalogs_name_with_each_characters_own_label()
    {
        // Venture slot 1 is a different retainer on each character. The line groups the slot under the catalog's name;
        // the expanded list names each character's own retainer (Entry.Label) with its own value.
        var aria = Items.Record(1);
        aria.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(18));
        aria.ItemNames[Items.Venture.Id] = "Mira";
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(2));
        kestrel.ItemNames[Items.Venture.Id] = "Tomo";

        var line = Assert.Single(Build(null, Owes(aria), Owes(kestrel)).In(BoardGroup.EndingSoon));

        Assert.Equal("Venture: Retainer 1", line.Name);
        Assert.Equal(["Mira", "Tomo"], line.Characters.Select(c => c.Entry.Name));
        Assert.Equal(["in 18 h", "in 2 h"], line.Characters.Select(c => c.Entry.Display));
    }

    [Fact]
    public void The_expanded_list_keeps_the_order_it_is_given_and_carries_each_characters_stale_mark()
    {
        // The characters under a line run in sidebar order, which the plugin passes in (Plugin.Characters). A character
        // not seen for longer than the staleness setting still owes its dailies, since resets roll over by the clock,
        // and carries its stale mark.
        var aria = Items.Record(1);
        var kestrel = Items.Record(2, "Kestrel Vane");
        var old = Items.Record(3, "Old Alt");
        old.LastSeen = Items.Now - TimeSpan.FromDays(30);
        old.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-08-13");   // done a month ago: owed again today

        var board = Build(null, Owes(kestrel), Owes(old), Owes(aria));

        var cactpot = board.In(BoardGroup.Today).Single(l => l.Id == Items.Cactpot.Id);
        Assert.Equal([2UL, 3UL, 1UL], cactpot.Characters.Select(c => c.Record.ContentId));
        Assert.Equal([false, true, false], cactpot.Characters.Select(c => c.Stale));
        Assert.Equal(3, cactpot.Count);
        var roulette = board.In(BoardGroup.Today).Single(l => l.Id == Items.Roulette.Id);
        Assert.True(Assert.Single(roulette.Characters).Stale);
    }

    [Fact]
    public void A_never_ticked_ready_every_task_sorts_with_one_whose_deadline_has_passed()
    {
        // An "every" task never ticked has no deadline (Assembler.WindowOf is null for a Manual item), so Board.Line reads
        // nothing stored as urgent now. One ticked and whose deadline has just passed sorts by that past deadline. Both
        // read "ready" and are the same state, so a deadline of exactly now sorts with it: the two tie, and the tie is
        // broken as any other (the name key), not by which one was ever ticked.
        var settings = new Settings();
        var never = settings.AddMyTask(Tasks.Draft("Never ticked", MyTask.Every, 24))!;
        var passed = settings.AddMyTask(Tasks.Draft("Passed", MyTask.Every, 24))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var aria = Items.Record(1);
        aria.Values[passed.Id] = new TimerValue(Items.Now);        // its deadline has just passed, exactly now
        // never has nothing stored

        var board = Build(null, Owes(aria, catalog, settings: settings));

        var today = board.In(BoardGroup.Today);
        Assert.Equal([never.Id, passed.Id], today.Select(l => l.Id));     // tied on Urgent; "Never ticked" wins the name tie-break
        Assert.Equal(Items.Now, today.Single(l => l.Id == never.Id).Urgent);
        Assert.Equal(Items.Now, today.Single(l => l.Id == passed.Id).Urgent);
    }

    [Fact]
    public void A_counted_task_under_its_line_shows_each_characters_own_count()
    {
        // All tasks shows each owing character's own count, "Aria · 2 of 5" in the window, through the text form's Character line.
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var aria = Items.Record();
        aria.Values[deliver.Id] = new CounterValue(2, 5, Items.Now);
        var kestrel = Items.Record(2, "Kestrel Vane");

        var board = Board.Build(
            [
                (Record: aria, Checklist: Assembler.Assemble(catalog, aria, settings, Items.Now)),
                (Record: kestrel, Checklist: Assembler.Assemble(catalog, kestrel, settings, Items.Now)),
            ],
            new TaskPreferences(), Items.Now);

        var line = Assert.Single(board.In(BoardGroup.Today));
        Assert.Equal([$"{aria.Label}: Deliver — 2 of 5", $"{kestrel.Label}: Deliver — 0 of 5"], line.Characters.Select(BoardText.Character));
    }
}
