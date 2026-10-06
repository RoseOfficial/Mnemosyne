using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class AssemblerTests
{
    private static readonly DateTimeOffset Now = Items.Now;
    private static string Key(string schedule) => ResetSchedules.PeriodKey(schedule, Now);

    private static IEnumerable<string> Ids(IEnumerable<Entry> entries) => entries.Select(e => e.Item.Id);

    [Fact]
    public void A_daily_done_this_period_is_off_today_and_one_done_last_period_is_on_it()
    {
        var r = Items.Record();
        r.Values[Items.Roulette.Id] = new DoneValue(true, Key(ResetSchedules.Daily));
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Roulette.Id, Ids(c.Today));

        r.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-11");
        c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.Contains(Items.Roulette.Id, Ids(c.Today));
        Assert.Equal(EntryState.NotDone, c.Sections.Single(s => s.Category == Category.Resets).Groups.Single(g => g.Name == "Daily").Entries.Single(e => e.Item.Id == Items.Roulette.Id).State);
    }

    [Fact]
    public void Weeklies_count_toward_the_badge_only_when_the_setting_says_so()
    {
        var r = Items.Record();
        r.Values[Items.Stickers.Id] = new CounterValue(3, 9, Now);
        var on = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        var off = Assembler.Assemble(Items.Catalog(), r, new Settings { WeekliesCount = false }, Now);
        Assert.Contains(Items.Stickers.Id, Ids(on.ThisWeek));
        Assert.Contains(Items.Stickers.Id, Ids(off.ThisWeek));
        Assert.Equal(on.Badge - 1, off.Badge);
    }

    [Fact]
    public void A_counter_read_before_the_boundary_is_shown_refilled_and_marked_assumed()
    {
        var r = Items.Record();
        r.Values[Items.Allowances.Id] = new CounterValue(12, 12, new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero)); // read before Saturday 15:00 UTC
        var entry = Assembler.Evaluate(Items.Allowances, r, Now);
        Assert.Equal("0 of 12", entry.Display);
        Assert.True(entry.Assumed);
        Assert.Equal("assumed", entry.Marker);
        Assert.Equal(EntryState.NotDone, entry.State);

        r.Values[Items.Allowances.Id] = new CounterValue(12, 12, Now);
        entry = Assembler.Evaluate(Items.Allowances, r, Now);
        Assert.Equal(EntryState.Done, entry.State);
        Assert.False(entry.Assumed);
    }

    [Fact]
    public void A_counter_that_carries_over_keeps_its_count_across_the_boundary()
    {
        // A Wondrous Tails journal is valid for two weeks, so a partly filled one still holds its stickers after the
        // Tuesday 08:00 that makes a new journal available. The boundary still decides placement.
        var carried = Items.Stickers with { Id = "reset:tails:carried", CarriesOver = true };
        var read = new CounterValue(5, 9, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));   // before Tuesday 2026-09-08 08:00
        var r = Items.Record();
        r.Values[carried.Id] = read;
        r.Values[Items.Stickers.Id] = read;

        var kept = Assembler.Evaluate(carried, r, Now);
        Assert.Equal("5 of 9", kept.Display);
        Assert.False(kept.Assumed);
        Assert.Equal("", kept.Marker);
        Assert.Equal(EntryState.NotDone, kept.State);

        var zeroed = Assembler.Evaluate(Items.Stickers, r, Now);   // the same value without the flag
        Assert.Equal("0 of 9", zeroed.Display);
        Assert.True(zeroed.Assumed);
        Assert.Equal("assumed", zeroed.Marker);

        // Below its cap it is still a thing to do this week: the flag suppresses the zeroing, not the placement.
        var c = Assembler.Assemble(new Catalog([carried, Items.Stickers]), r, new Settings(), Now);
        Assert.Contains(carried.Id, Ids(c.ThisWeek));
    }

    [Fact]
    public void A_held_counter_is_never_done_at_its_cap()
    {
        var gil = new ItemDefinition("currency:1", Category.Money, "Currencies", "Gil", ItemKind.Counter, ResetRule.None, 1, Freshness.AtLogin, DoneAtCap: false);
        var r = Items.Record();
        r.Values[gil.Id] = new CounterValue(999_999_999, 999_999_999, Now);
        var entry = Assembler.Evaluate(gil, r, Now);
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.True(entry.AtCap);

        var full = Items.Record();
        full.Values[Items.Allowances.Id] = new CounterValue(12, 12, Now);
        Assert.Equal(EntryState.Done, Assembler.Evaluate(Items.Allowances, full, Now).State);
    }

    [Fact]
    public void Accrual_grants_are_added_and_capped_and_a_full_counter_lands_in_today()
    {
        var r = Items.Record();
        r.Values[Items.Leves.Id] = new CounterValue(97, 100, new DateTimeOffset(2026, 9, 12, 11, 0, 0, TimeSpan.Zero)); // three grants since: 12:00, 00:00, 12:00
        var entry = Assembler.Evaluate(Items.Leves, r, Now);
        Assert.Equal("100 of 100", entry.Display);
        Assert.True(entry.Assumed);
        Assert.True(entry.AtCap);
        Assert.Equal(EntryState.NotDone, entry.State);
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.Contains(Items.Leves.Id, Ids(c.Today));

        r.Values[Items.Leves.Id] = new CounterValue(10, 100, Now);
        c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Leves.Id, Ids(c.Today));
    }

    [Fact]
    public void An_accrual_timer_whose_deadline_passed_is_in_today()
    {
        var r = Items.Record();
        r.Values[Items.Map.Id] = new TimerValue(Now - TimeSpan.FromHours(1));
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.Contains(Items.Map.Id, Ids(c.Today));
        r.Values[Items.Map.Id] = new TimerValue(Now + TimeSpan.FromHours(1));
        c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Map.Id, Ids(c.Today));
        Assert.Contains(Items.Map.Id, Ids(c.EndingSoon));            // one hour out is inside the three-day window
        r.Values[Items.Map.Id] = new TimerValue(Now + TimeSpan.FromDays(10));
        c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Map.Id, Ids(c.Today).Concat(Ids(c.EndingSoon)));
    }

    [Fact]
    public void Timers_enter_ending_soon_inside_the_window_including_overdue()
    {
        var r = Items.Record();
        r.Values[Items.Venture.Id] = new TimerValue(Now + TimeSpan.FromDays(2));
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(Items.Venture.Id, Ids(c.EndingSoon));
        Assert.Equal("in 2 d", c.EndingSoon.Single(e => e.Item.Id == Items.Venture.Id).Display);

        r.Values[Items.Venture.Id] = new TimerValue(Now - TimeSpan.FromHours(3));
        c = Assembler.Assemble(Items.Catalog(), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(Items.Venture.Id, Ids(c.EndingSoon));

        r.Values[Items.Venture.Id] = new TimerValue(Now + TimeSpan.FromDays(10));
        c = Assembler.Assemble(Items.Catalog(), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.DoesNotContain(Items.Venture.Id, Ids(c.EndingSoon));
    }

    [Fact]
    public void Events_are_upcoming_past_or_ending_soon_and_only_open_ones_count()
    {
        var r = Items.Record();
        r.Values[Items.Moonfire.Id] = new DoneValue(false);
        r.Values[Items.Rising.Id] = new DoneValue(false);
        r.Values[Items.Starlight.Id] = new DoneValue(false);
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(Items.Moonfire.Id, Ids(c.EndingSoon));            // closes 2026-09-15 08:00, within 3 days
        Assert.DoesNotContain(Items.Rising.Id, Ids(c.EndingSoon));         // closed on the 10th
        Assert.DoesNotContain(Items.Starlight.Id, Ids(c.EndingSoon));      // not open yet
        Assert.Equal(EntryState.Past, Assembler.Evaluate(Items.Rising, r, Now).State);
        Assert.Equal(EntryState.Upcoming, Assembler.Evaluate(Items.Starlight, r, Now).State);
        Assert.Single(c.EndingSoon);

        var narrow = Assembler.Assemble(Items.Catalog(), r, new Settings { EndingSoonDays = 1 }, Now);
        Assert.DoesNotContain(Items.Moonfire.Id, Ids(narrow.EndingSoon));   // closes in 1 d 20 h, outside a one-day window
        Assert.DoesNotContain(Items.Moonfire.Id, Ids(narrow.ThisWeek));    // open but not ending soon: no top group at all

        r.Values[Items.Moonfire.Id] = new DoneValue(true);
        c = Assembler.Assemble(Items.Catalog(), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Empty(c.EndingSoon);
    }

    [Fact]
    public void A_done_occurrence_whose_window_has_not_opened_reads_done_not_upcoming()
    {
        // The sheets carry a run before it is announced, so an occurrence can sit on a window that is still in the future
        // while the character has already finished it. Done wins at both ends of the window.
        var r = Items.Record();
        r.Values[Items.Starlight.Id] = new DoneValue(false);
        Assert.Equal(EntryState.Upcoming, Assembler.Evaluate(Items.Starlight, r, Now).State);

        r.Values[Items.Starlight.Id] = new DoneValue(true);
        var entry = Assembler.Evaluate(Items.Starlight, r, Now);
        Assert.Equal(EntryState.Done, entry.State);
        Assert.Equal("done", entry.Display);

        // A done window item is in no top group, so the badge is unchanged either way.
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Starlight.Id, Ids(c.Today).Concat(Ids(c.ThisWeek)).Concat(Ids(c.EndingSoon)));
    }

    [Fact]
    public void Gated_items_without_a_value_are_unknown_and_never_counted_but_observed_only_items_are()
    {
        var r = Items.Record();
        var achievement = Assembler.Evaluate(Items.Achievement, r, Now);
        Assert.Equal(EntryState.Unknown, achievement.State);
        Assert.Equal("needs: Achievements", achievement.Marker);
        var eureka = Assembler.Evaluate(Items.Eureka, r, Now);
        Assert.Equal("visit: Eureka", eureka.Marker);
        var cactpot = Assembler.Evaluate(Items.Cactpot, r, Now);
        Assert.Equal(EntryState.NotDone, cactpot.State);
        Assert.Equal("as seen", cactpot.Marker);

        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.Contains(Items.Cactpot.Id, Ids(c.Today));
        Assert.DoesNotContain(Items.Achievement.Id, Ids(c.Today).Concat(Ids(c.ThisWeek)).Concat(Ids(c.EndingSoon)));
    }

    [Fact]
    public void A_category_never_read_shows_not_read_yet()
    {
        var r = Items.Record();
        r.CategoryReadAt.Remove(nameof(Category.Story));
        var entry = Assembler.Evaluate(Items.Msq1, r, Now);
        Assert.Equal(EntryState.Unknown, entry.State);
        Assert.Equal("not read yet", entry.Marker);
    }

    [Fact]
    public void Ignored_items_and_groups_vanish_from_everything()
    {
        var r = Items.Record();
        r.Values[Items.Roulette.Id] = new DoneValue(false);
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        var before = c.Badge;
        Assert.Contains(Items.Roulette.Id, Ids(c.Today));

        r.Ignored.Add(Items.Roulette.Id);
        c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Roulette.Id, Ids(c.Today));
        Assert.Equal(before - 1, c.Badge);
        Assert.DoesNotContain(Items.Roulette.Id, c.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id));

        var s = new Settings();
        s.IgnoredEverywhere.Add(Items.Roulette.GroupKey);   // the whole "Daily" group of Resets
        r.Ignored.Clear();
        c = Assembler.Assemble(Items.Catalog(), r, s, Now);
        Assert.DoesNotContain(c.Sections.Single(x => x.Category == Category.Resets).Groups, g => g.Name == "Daily");
        Assert.Empty(c.Today);
    }

    [Fact]
    public void A_category_that_is_off_is_absent_and_uncounted()
    {
        var r = Items.Record();
        var s = new Settings();
        s.CategoriesOff.Add(nameof(Category.Resets));
        var c = Assembler.Assemble(Items.Catalog(), r, s, Now);
        Assert.DoesNotContain(c.Sections, x => x.Category == Category.Resets);
        Assert.Empty(c.Today);
    }

    [Fact]
    public void A_category_off_and_a_group_off_for_the_role_are_listed_with_its_name_and_never_placed()
    {
        // The Events category and the Resets "Daily" group are off for Mule. Aria's five things to do were the tribal
        // allowances and the cactpot (Daily), the stickers (Weekly), Moonfire (Events) and the venture (Money); the role leaves two.
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Role.CategoryKey(Category.Events));
        mule.Off.Add(Items.Roulette.GroupKey);
        var aria = SummariserTests.Aria();
        aria.Role = mule.Id;

        var c = Assembler.Assemble(Items.Catalog(), aria, settings, Now);

        Assert.Empty(c.Today);
        Assert.Equal([Items.Stickers.Id], Ids(c.ThisWeek));
        Assert.Equal([Items.Venture.Id], Ids(c.EndingSoon));
        Assert.Equal(2, c.Badge);
        var events = c.Sections.Single(s => s.Category == Category.Events).Groups.SelectMany(g => g.Entries).ToList();
        Assert.Equal(3, events.Count);                                                     // still listed, every one
        Assert.All(events, e => Assert.Equal("Mule", e.OffFor));
        var resets = c.Sections.Single(s => s.Category == Category.Resets).Groups;
        Assert.All(resets.Single(g => g.Name == "Daily").Entries, e => Assert.Equal("Mule", e.OffFor));
        Assert.All(resets.Single(g => g.Name == "Weekly").Entries, e => Assert.Null(e.OffFor));   // a group off is that group alone
        Assert.Equal("4 of 12", resets.Single(g => g.Name == "Daily").Entries.Single(e => e.Item.Id == Items.Allowances.Id).Display);   // still evaluated
        Assert.All(c.Sections.Where(s => s.Category is not (Category.Events or Category.Resets)).SelectMany(s => s.Groups).SelectMany(g => g.Entries),
            e => Assert.Null(e.OffFor));
    }

    [Fact]
    public void An_off_row_is_in_no_badge_no_matrix_count_no_all_tasks_line_and_no_reminder()
    {
        // Aria and Kestrel each owe the roulette and the cactpot today and Moonfire, which ends in 1 d 20 h. Kestrel's role
        // switches the Resets "Daily" group and the Events category off. Without it Kestrel has a daily-reset and an
        // event-ending reminder due, three things to do and a count in each matrix column; with it, none, and no All tasks
        // line lists Kestrel, while Aria, with no role, still owes all three on the same board.
        var settings = new Settings { RemindDailyReset = true, RemindEventEnding = true };
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Items.Roulette.GroupKey);
        mule.Off.Add(Role.CategoryKey(Category.Events));
        var catalog = Items.Catalog();
        var aria = Items.Record(1);
        var kestrel = Items.Record(2, "Kestrel Vane");
        foreach (var r in new[] { aria, kestrel })
        {
            r.Values[Items.Roulette.Id] = new DoneValue(false);
            r.Values[Items.Moonfire.Id] = new DoneValue(false);
            r.Values[Items.Rising.Id] = new DoneValue(true);
        }
        var owed = Assembler.Assemble(catalog, kestrel, settings, Now);
        Assert.Equal(3, owed.Badge);                                                       // the premise: roulette and cactpot today, Moonfire ending soon
        Assert.Equal(2, Reminders.Due(owed, kestrel, settings, Now).Count);
        var owedRow = Summariser.Summarise(catalog, kestrel, settings, Now, owed);
        Assert.Equal((2, 1, 1, 3), (owedRow.DailiesLeft, owedRow.EndingSoon, owedRow.EventsDone, owedRow.EventsAvailable));

        kestrel.Role = mule.Id;
        var off = Assembler.Assemble(catalog, kestrel, settings, Now);

        Assert.Equal(0, off.Badge);
        Assert.Empty(Reminders.Due(off, kestrel, settings, Now));
        var row = Summariser.Summarise(catalog, kestrel, settings, Now, off);
        Assert.Equal((0, 0, 0, 0), (row.DailiesLeft, row.WeekliesLeft, row.EndingSoon, row.Badge));
        Assert.Equal((0, 0), (row.EventsDone, row.EventsAvailable));                      // a catalog count skips it too
        var board = Board.Build([(aria, Assembler.Assemble(catalog, aria, settings, Now)), (kestrel, off)], new TaskPreferences(), Now);
        var lines = board.Groups.SelectMany(g => g.Lines).ToList();
        Assert.Equal(3, lines.Count);                                                      // Aria's roulette, cactpot and Moonfire
        Assert.All(lines, line => Assert.Equal([1UL], line.Characters.Select(c => c.Record.ContentId)));
    }

    [Fact]
    public void No_role_an_unknown_role_or_a_role_whose_only_switch_names_a_retired_group_gives_exactly_todays_checklist()
    {
        // An id this PC does not have (a deleted role, or a record imported without its settings) tracks everything, and so
        // does a role whose only switch names a group the catalog no longer has. Each gives the checklist of a character
        // with no role, row for row, with nothing marked off. A live role comes first, Mule with Events off, so an unknown
        // or empty id that wrongly resolved to any role would change the checklist and fail here.
        var aria = SummariserTests.Aria();
        var baseline = ChecklistText.Render(Assembler.Assemble(Items.Catalog(), aria, new Settings(), Now), aria, Now);
        var settings = new Settings();
        settings.Roles.Create("Mule")!.Off.Add(Role.CategoryKey(Category.Events));
        var seasonal = settings.Roles.Create("Seasonal")!;
        seasonal.Off.Add("group:Events/A retired event");

        foreach (var id in new string?[] { null, "", "gone", seasonal.Id })
        {
            aria.Role = id;
            var c = Assembler.Assemble(Items.Catalog(), aria, settings, Now);
            Assert.Equal(baseline, ChecklistText.Render(c, aria, Now));
            Assert.Equal(5, c.Badge);
            Assert.All(c.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries), e => Assert.Null(e.OffFor));
        }
    }

    [Fact]
    public void An_ignore_wins_over_a_role_and_a_role_never_lists_what_another_rule_left_out()
    {
        // Resets and Money are off for the role. The roulette is also ignored on this character, and the stickers' "Weekly"
        // group everywhere: both are hidden entirely, as an ignore always was, never listed dimmed. A category off for
        // everyone is still not rendered at all, and a blank Money row the role switches off is still left out.
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Role.CategoryKey(Category.Resets));
        mule.Off.Add(Role.CategoryKey(Category.Money));
        settings.IgnoredEverywhere.Add(Items.Stickers.GroupKey);
        settings.CategoriesOff.Add(nameof(Category.Content));
        var blank = new ItemDefinition("money:blank", Category.Money, "Currency", "A currency not held", ItemKind.Counter, ResetRule.None, 1,
            Freshness.AtLogin, DoneAtCap: false, HiddenWhenBlank: true);
        var catalog = new Catalog([.. Items.Catalog().Items, blank]);
        var aria = SummariserTests.Aria();
        aria.Ignored.Add(Items.Roulette.Id);
        aria.Role = mule.Id;

        var c = Assembler.Assemble(catalog, aria, settings, Now);

        var listed = c.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id).ToList();
        Assert.DoesNotContain(Items.Roulette.Id, listed);                                  // ignored on this character: gone, not dimmed
        Assert.DoesNotContain(Items.Stickers.Id, listed);                                  // its group ignored everywhere
        Assert.DoesNotContain(blank.Id, listed);                                           // blank, so left out whatever the role says
        Assert.DoesNotContain(c.Sections, s => s.Category == Category.Content);            // off for everyone
        Assert.Contains(Items.Allowances.Id, listed);                                      // what no other rule hides is listed, dimmed
        Assert.Contains(Items.Venture.Id, listed);
        Assert.Equal("Mule", c.Sections.Single(s => s.Category == Category.Resets).Groups.Single(g => g.Name == "Daily").Entries[0].OffFor);
    }

    [Fact]
    public void A_role_that_switches_everything_off_leaves_a_badge_of_0_with_every_row_still_listed()
    {
        // A role that switches every category off is allowed, and the character is then fully informational. Every row it
        // listed before is still listed, each off for the role; no top group holds anything, so nothing reaches All tasks;
        // its matrix counts are 0; its MSQ position still reads.
        var settings = new Settings();
        var archive = settings.Roles.Create("Archive")!;
        foreach (var category in Enum.GetValues<Category>()) archive.SetCategory(category, false, Items.Catalog());
        var aria = SummariserTests.Aria();
        var before = Assembler.Assemble(Items.Catalog(), aria, settings, Now);
        aria.Role = archive.Id;

        var c = Assembler.Assemble(Items.Catalog(), aria, settings, Now);

        Assert.Equal(0, c.Badge);
        Assert.Empty(c.Today);
        Assert.Empty(c.ThisWeek);
        Assert.Empty(c.EndingSoon);
        Assert.Empty(c.Running);
        var rows = c.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).ToList();
        Assert.Equal(before.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id), rows.Select(e => e.Item.Id));
        Assert.All(rows, e => Assert.Equal("Archive", e.OffFor));
        var row = Summariser.Summarise(Items.Catalog(), aria, settings, Now, c);
        Assert.Equal((0, 0, 0, 0, 0, 0, 0, 0),
            (row.JobsAtCap, row.JobsUnlocked, row.EventsDone, row.EventsAvailable, row.DailiesLeft, row.WeekliesLeft, row.EndingSoon, row.Badge));
        Assert.Equal("MSQ: Dawntrail: The First Step", row.MsqPosition);
        Assert.Equal("Archive", row.Role);
        Assert.Empty(Board.Build([(aria, c)], new TaskPreferences(), Now).Groups.SelectMany(g => g.Lines));
    }

    [Fact]
    public void A_renamed_role_keeps_its_characters_and_the_cached_checklist_reads_the_new_name()
    {
        // A record stores the role's id, so a rename never breaks it. The rename is an ordinary settings edit, which moves
        // the version the checklist cache keys on, so the next read is assembled again, and its rows and its Role column
        // read the new name. Rename's own rules (a taken name refused, its own name recased) are RoleTests'.
        var keeper = new SettingsKeeper(new RecordStore(new InMemoryFileSystem(), @"C:\cfg\Mnemosyne"), (_, _) => { });
        keeper.Update(s => s.Roles.Create("Mule")!.Off.Add(Items.Roulette.GroupKey));
        var id = keeper.Settings.Roles.Usable.Single().Id;
        var aria = SummariserTests.Aria();
        aria.Role = id;
        var cache = new ChecklistCache();
        var before = cache.Get(Items.Catalog(), aria, keeper.Settings, 1, keeper.Version, Now);
        Assert.Equal("Mule", before.Sections.Single(s => s.Category == Category.Resets).Groups.Single(g => g.Name == "Daily").Entries[0].OffFor);

        keeper.Update(s => s.Roles.Rename(id, "Retainer mule"));
        var after = cache.Get(Items.Catalog(), aria, keeper.Settings, 1, keeper.Version, Now);

        Assert.NotSame(before, after);
        Assert.Equal(id, aria.Role);
        Assert.All(after.Sections.Single(s => s.Category == Category.Resets).Groups.Single(g => g.Name == "Daily").Entries,
            e => Assert.Equal("Retainer mule", e.OffFor));
        Assert.Equal("Retainer mule", cache.GetRow(Items.Catalog(), aria, keeper.Settings, 1, keeper.Version, Now).Role);
    }

    [Fact]
    public void A_my_task_with_nothing_stored_is_not_done_and_attention_and_never_reads_not_read_yet()
    {
        // No reader will ever fill a Manual item, so a task never ticked is not done, or "ready" for an "every" task, even
        // on a record none of whose categories was ever read. A daily is in Today and a weekly in This week, as the game's;
        // a one-off and a ready "every" task are in Today, the one new placement rule.
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var granary = settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly))!;
        var mount = settings.AddMyTask(Tasks.Draft("Buy the mount", MyTask.Once))!;
        var craft = settings.AddMyTask(Tasks.Draft("Three-day craft", MyTask.Every, 72))!;
        var fresh = new CharacterRecord { ContentId = 1, Name = "Aria Whitewind", LastSeen = Now };   // nothing read, nothing stored

        var c = Assembler.Assemble(new Catalog(settings.MyTasks.Items()), fresh, settings, Now);

        Assert.Equal([chest.Id, mount.Id, craft.Id], Ids(c.Today));
        Assert.Equal([granary.Id], Ids(c.ThisWeek));
        Assert.Equal(4, c.Badge);
        var rows = c.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries;
        Assert.All(rows, e => Assert.Equal((EntryState.NotDone, ""), (e.State, e.Marker)));
        Assert.Equal(["", "", "", "ready"], rows.Select(e => e.Display));
    }

    [Fact]
    public void A_one_off_is_in_today_until_ticked_and_the_rule_is_for_manual_items_alone()
    {
        // A one-off My task is attention until it is ticked, then done for good. A game item with no reset that is not
        // done, an MSQ quest here, stays information: in its section and in no top group.
        var settings = new Settings();
        var mount = settings.AddMyTask(Tasks.Draft("Buy the mount", MyTask.Once))!;
        var catalog = new Catalog([Items.Msq1, Items.Msq2, .. settings.MyTasks.Items()]);
        var r = Items.Record();
        r.Values[Items.Msq1.Id] = new DoneValue(true);

        var owed = Assembler.Assemble(catalog, r, settings, Now);
        Assert.Equal([mount.Id], Ids(owed.Today));
        Assert.Equal(EntryState.NotDone, owed.Sections.Single(s => s.Category == Category.Story).Groups.Single().Entries.Single(e => e.Item.Id == Items.Msq2.Id).State);

        r.Values[mount.Id] = mount.TickValue(Now)!;
        var done = Assembler.Assemble(catalog, r, settings, Now + TimeSpan.FromDays(400));
        Assert.Empty(done.Today);
        Assert.Equal(0, done.Badge);
        Assert.True(done.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries.Single().IsDone);
    }

    [Fact]
    public void An_every_task_reads_ready_in_until_its_cooldown_ends_then_ready_in_today_and_is_never_ending_soon()
    {
        // A tick stores its deadline, the tick plus the length. Before it the task is done, reading "ready in 14 h", and in
        // no top group, though a game Timer that close would be Ending soon. From it on, the task reads "ready", is not
        // done, and is in Today.
        var settings = new Settings();
        var craft = settings.AddMyTask(Tasks.Draft("Craft", MyTask.Every, 24))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var r = Items.Record();
        r.Values[craft.Id] = craft.TickValue(Now - TimeSpan.FromHours(10))!;

        var cooling = Assembler.Assemble(catalog, r, settings, Now);
        var row = cooling.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries.Single();
        Assert.Equal((EntryState.Done, "ready in 14 h"), (row.State, row.Display));
        Assert.Empty(cooling.Today.Concat(cooling.EndingSoon));
        Assert.Equal(0, cooling.Badge);

        var ready = Assembler.Assemble(catalog, r, settings, Now + TimeSpan.FromHours(14));
        Assert.Equal((EntryState.NotDone, "ready"), (ready.Today.Single().State, ready.Today.Single().Display));
        Assert.Equal(1, ready.Badge);

        r.Values[craft.Id] = new TimerValue(Now + TimeSpan.FromDays(3));
        Assert.Equal("ready in 3 d", Assembler.Evaluate(catalog.TryGet(craft.Id)!, r, Now).Display);
    }

    [Fact]
    public void A_change_of_kind_reads_the_old_kinds_tick_as_not_done_and_a_rename_keeps_it()
    {
        // A change of kind starts the task fresh: a value of the old kind reads as not done, or "ready" for an "every" task,
        // and the next tick writes the new kind. A daily tick under a one-off is the case a plain Done rule would read as
        // done, so a stored period under a one-off is dropped. Nothing is deleted, so changing back reads the old tick
        // again until a new tick replaces it: a daily's within its period, a one-off's whenever the change back happens.
        // A rename keeps every tick. The catalog is rebuilt from the settings after each edit, as MyTaskCatalog does.
        var settings = new Settings();
        var task = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var r = Items.Record();
        Entry Row(DateTimeOffset? at = null) =>
            Assembler.Assemble(new Catalog(settings.MyTasks.Items()), r, settings, at ?? Now).Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries.Single();
        void Make(string kind, int? hours = null) => Assert.True(settings.MyTasks.Edit(task.Id, Tasks.Draft(task.Name, kind, hours)));

        r.Values[task.Id] = task.TickValue(Now)!;                                          // ticked as a daily
        Assert.True(settings.MyTasks.Edit(task.Id, Tasks.Draft("Company chest", MyTask.Daily)));
        Assert.True(Row().IsDone);                                                         // a rename keeps the tick
        Make(MyTask.Once);
        Assert.Equal((EntryState.NotDone, ""), (Row().State, Row().Display));             // a daily's tick is not a one-off's
        Make(MyTask.Daily);
        Assert.True(Row().IsDone);                                                         // back within the period: the same tick
        Make(MyTask.Weekly);
        Assert.False(Row().IsDone);                                                        // nor a weekly's
        Make(MyTask.Every, 24);
        Assert.Equal((EntryState.NotDone, "ready"), (Row().State, Row().Display));        // nor an "every" task's

        r.Values[task.Id] = task.TickValue(Now)!;                                          // ticked as "every"
        Assert.True(Row().IsDone);
        Make(MyTask.Once);
        Assert.False(Row().IsDone);
        r.Values[task.Id] = task.TickValue(Now)!;                                          // ticked as a one-off
        Assert.True(Row().IsDone);
        Make(MyTask.Daily);
        Assert.False(Row().IsDone);
        Assert.Equal(new DoneValue(true, null, Now), r.Values[task.Id]);                   // still stored: only the next tick replaces it
        Make(MyTask.Once);
        Assert.True(Row(Now + TimeSpan.FromDays(30)).IsDone);                              // back to a one-off a month later: its tick reads again
    }

    [Fact]
    public void A_task_is_for_everyone_a_role_or_picked_characters_and_a_deleted_role_means_nobody()
    {
        // A character a task is not for does not have the item at all: no row, no top group, no badge, no All tasks line,
        // and no My tasks section when no task is for it. A task for a role follows the character's current role, so it
        // moves with an assignment; with the role deleted, the task is for nobody, and its characters keep the role's id.
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        var everyone = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var mules = settings.AddMyTask(Tasks.Draft("Mule errand", MyTask.Daily, null, MyTask.ForRole(mule.Id)))!;
        var picked = settings.AddMyTask(Tasks.Draft("Aria's craft", MyTask.Daily, null, MyTask.ForPicked, 1))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var aria = Items.Record(1);
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Role = mule.Id;
        var momo = Items.Record(3, "Momo Lulu");
        IEnumerable<string> Listed(CharacterRecord r) =>
            Assembler.Assemble(catalog, r, settings, Now).Sections.Where(s => s.Category == Category.MyTasks).SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id);

        Assert.Equal([everyone.Id, picked.Id], Listed(aria));
        Assert.Equal([everyone.Id, mules.Id], Listed(kestrel));
        Assert.Equal([everyone.Id], Listed(momo));
        Assert.Equal(1, Assembler.Assemble(catalog, momo, settings, Now).Badge);
        var board = Board.Build(new[] { aria, kestrel, momo }.Select(r => (r, Assembler.Assemble(catalog, r, settings, Now))).ToList(), new TaskPreferences(), Now);
        var lines = board.Groups.SelectMany(g => g.Lines).ToDictionary(l => l.Id, l => l.Characters.Select(c => c.Record.ContentId).ToList());
        Assert.Equal([1UL, 2UL, 3UL], lines[everyone.Id]);
        Assert.Equal([2UL], lines[mules.Id]);
        Assert.Equal([1UL], lines[picked.Id]);

        momo.Role = mule.Id;                                                               // an assignment moves the task with it
        Assert.Equal([everyone.Id, mules.Id], Listed(momo));
        settings.Roles.Delete(mule.Id);                                                    // a deleted role: the task is for nobody
        Assert.Equal([everyone.Id], Listed(kestrel));
        Assert.Equal([everyone.Id], Listed(momo));
        Assert.Equal(mule.Id, kestrel.Role);

        settings.MyTasks.Delete(everyone.Id);                                              // no task is for Momo now: no section at all
        Assert.DoesNotContain(Assembler.Assemble(new Catalog(settings.MyTasks.Items()), momo, settings, Now).Sections, s => s.Category == Category.MyTasks);
    }

    [Fact]
    public void A_deleted_task_leaves_its_ticks_in_the_record_unread_and_its_id_is_never_given_to_another()
    {
        // A delete removes the definition, and the stored ticks stay in the records, unread, like a retired catalog item's
        // value. The next task gets a new id, so it never reads the old one's tick: a one-off made again is owed again.
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Once))!;
        var r = Items.Record();
        r.Values[chest.Id] = chest.TickValue(Now)!;
        settings.MyTasks.Delete(chest.Id);
        var again = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Once))!;

        var c = Assembler.Assemble(Tasks.Catalog(settings), r, settings, Now);

        Assert.NotEqual(chest.Id, again.Id);
        Assert.Contains(again.Id, Ids(c.Today));
        Assert.DoesNotContain(chest.Id, c.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id));
        Assert.Equal(new DoneValue(true, null, Now), r.Values[chest.Id]);                  // kept, unread
    }

    [Fact]
    public void Levels_are_done_at_max_and_staleness_follows_last_seen()
    {
        var r = Items.Record();
        r.Values[Items.Paladin.Id] = new LevelValue(100, 100);
        Assert.Equal(EntryState.Done, Assembler.Evaluate(Items.Paladin, r, Now).State);
        r.Values[Items.Paladin.Id] = new LevelValue(72, 100);
        var e = Assembler.Evaluate(Items.Paladin, r, Now);
        Assert.Equal(EntryState.NotDone, e.State);
        Assert.Equal("72 / 100", e.Display);

        r.LastSeen = Now - TimeSpan.FromDays(8);
        Assert.True(Assembler.Assemble(Items.Catalog(), r, new Settings(), Now).Stale);
        r.LastSeen = Now - TimeSpan.FromDays(6);
        Assert.False(Assembler.Assemble(Items.Catalog(), r, new Settings(), Now).Stale);
    }

    [Fact]
    public void An_entry_is_in_at_most_one_top_group()
    {
        var r = Items.Record();
        r.Values[Items.Map.Id] = new TimerValue(Now - TimeSpan.FromHours(1));   // accrual timer, passed: Today, not Ending soon
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        var all = Ids(c.Today).Concat(Ids(c.ThisWeek)).Concat(Ids(c.EndingSoon)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void A_gated_item_with_a_value_shows_the_value_as_of_the_last_visit()
    {
        var r = Items.Record();
        r.Values[Items.Achievement.Id] = new DoneValue(true);
        var entry = Assembler.Evaluate(Items.Achievement, r, Now);
        Assert.Equal(EntryState.Done, entry.State);
        Assert.Equal("as of last visit to Achievements", entry.Marker);
        r.Values[Items.Eureka.Id] = new LevelValue(30, 60);
        Assert.Equal("as of last visit to Eureka", Assembler.Evaluate(Items.Eureka, r, Now).Marker);
    }

    [Fact]
    public void A_running_window_item_waits_in_running_events_until_it_is_ending_soon()
    {
        // A running event is information, not attention. It waits in its own top group, out of the badge, and moves to
        // Ending soon only as it closes. Thirty days of Yo-kai Watch left is not a to-do this week.
        var running = new ItemDefinition("event:252:39", Category.Events, "Yo-kai Watch", "Yo-kai Watch 2026", ItemKind.Done, ResetRule.Window, 39, Freshness.AtLogin,
            new Availability(Now - TimeSpan.FromDays(2), Now + TimeSpan.FromDays(30)));
        var r = Items.Record();
        r.Values[running.Id] = new DoneValue(false);
        var c = Assembler.Assemble(new Catalog([running]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(running.Id, Ids(c.Running));
        Assert.Empty(c.Today);
        Assert.Empty(c.ThisWeek);
        Assert.Empty(c.EndingSoon);
        Assert.Equal(0, c.Badge);                     // an item in Running never lights the badge

        var entry = c.Running.Single();
        Assert.Equal(running.Id, entry.Item.Id);
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.Equal("ends in 30 d", entry.Display);
        // The top groups are a view, not a move: the same entry is still under its Events section.
        Assert.Same(entry, c.Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single());

        var ending = running with { Availability = new Availability(Now - TimeSpan.FromDays(2), Now + TimeSpan.FromDays(2)) };
        var soon = Assembler.Assemble(new Catalog([ending]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(ending.Id, Ids(soon.EndingSoon));
        Assert.Empty(soon.Running);
        Assert.Equal("ends in 2 d", soon.EndingSoon.Single().Display);
        Assert.Equal(1, soon.Badge);

        // A running event that is already done is in no top group at all, Running included.
        r.Values[running.Id] = new DoneValue(true);
        var finished = Assembler.Assemble(new Catalog([running]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Empty(finished.Running);
        Assert.Empty(finished.EndingSoon);
        Assert.Equal(0, finished.Badge);
    }

    [Fact]
    public void Running_events_are_listed_in_the_order_they_end()
    {
        // Catalog order would put "Later" first: the group names sort ordinally. The group is in end order instead.
        var late = new ItemDefinition("event:1:1", Category.Events, "Later", "Later event", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
            new Availability(Now - TimeSpan.FromDays(1), Now + TimeSpan.FromDays(20)));
        var early = new ItemDefinition("event:2:1", Category.Events, "Sooner", "Sooner event", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
            new Availability(Now - TimeSpan.FromDays(1), Now + TimeSpan.FromDays(9)));
        var r = Items.Record();
        r.Values[late.Id] = new DoneValue(false);
        r.Values[early.Id] = new DoneValue(false);
        var c = Assembler.Assemble(new Catalog([late, early]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Equal(new[] { early.Id, late.Id }, Ids(c.Running));
    }

    /// <summary>An event whose return reuses the 2016 quest: the same item with ReusedQuest set, windowed around Now.</summary>
    private static ItemDefinition Return(TimeSpan startIn, TimeSpan endIn, bool reusedQuest = true) =>
        new("event:252:39", Category.Events, "Yo-kai Watch", "Yo-kai Watch 2026", ItemKind.Done, ResetRule.Window, 39, Freshness.AtLogin,
            new Availability(Now + startIn, Now + endIn), ReusedQuest: reusedQuest);

    [Fact]
    public void A_return_completed_inside_its_own_window_is_simply_done()
    {
        var item = Return(-TimeSpan.FromDays(2), TimeSpan.FromDays(10));
        var r = Items.Record();
        r.Values[item.Id] = new DoneValue(true, null, Now - TimeSpan.FromDays(1));
        var entry = Assembler.Evaluate(item, r, Now);
        Assert.Equal(EntryState.Done, entry.State);
        Assert.Equal("done", entry.Display);
        Assert.Equal("", entry.Marker);

        var c = Assembler.Assemble(new Catalog([item]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Equal(0, c.Badge);
    }

    [Fact]
    public void A_return_done_only_in_an_earlier_one_is_still_upcoming_before_its_window_opens()
    {
        var item = Return(TimeSpan.FromDays(5), TimeSpan.FromDays(20));
        var r = Items.Record();
        r.Values[item.Id] = new DoneValue(true, null, Now - TimeSpan.FromDays(400));
        var entry = Assembler.Evaluate(item, r, Now);
        Assert.Equal(EntryState.Upcoming, entry.State);
        Assert.Equal("upcoming", entry.Display);
    }

    [Fact]
    public void A_return_done_only_in_an_earlier_one_runs_with_a_marker_inside_its_window()
    {
        // The quest was completed years ago, and the plugin never saw it flip inside this window, so this return is not done.
        var item = Return(-TimeSpan.FromDays(2), TimeSpan.FromDays(10));
        var r = Items.Record();
        r.Values[item.Id] = new DoneValue(true, null, Now - TimeSpan.FromDays(400));
        var entry = Assembler.Evaluate(item, r, Now);
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.Equal("ends in 10 d", entry.Display);
        // The marker claims only that the quests are done: the plugin cannot tell an earlier return from this one done
        // before it was installed.
        Assert.Equal("quests done; which return is unknown", entry.Marker);

        // A completion with no stamp at all (the quest was already complete when the plugin first read it) reads the same.
        r.Values[item.Id] = new DoneValue(true);
        Assert.Equal("quests done; which return is unknown", Assembler.Evaluate(item, r, Now).Marker);

        // Running, so it waits in Running events until it is ending soon.
        var c = Assembler.Assemble(new Catalog([item]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(item.Id, Ids(c.Running));
        Assert.Empty(c.EndingSoon);
        Assert.Empty(c.ThisWeek);
        var ending = Assembler.Assemble(new Catalog([Return(-TimeSpan.FromDays(2), TimeSpan.FromDays(2))]), r, new Settings { EndingSoonDays = 3 }, Now);
        Assert.Contains(item.Id, Ids(ending.EndingSoon));
    }

    [Fact]
    public void A_return_done_only_in_an_earlier_one_reads_done_after_its_window_not_missed()
    {
        var item = Return(-TimeSpan.FromDays(30), -TimeSpan.FromDays(10));
        var r = Items.Record();
        r.Values[item.Id] = new DoneValue(true, null, Now - TimeSpan.FromDays(400));
        var entry = Assembler.Evaluate(item, r, Now);
        Assert.Equal(EntryState.Done, entry.State);
        Assert.Equal("done", entry.Display);
        Assert.Equal("quests done", entry.Marker);

        // An event that does not reuse its quest is unchanged: a completion of any age is that occurrence's own.
        var ownQuest = Return(-TimeSpan.FromDays(30), -TimeSpan.FromDays(10), reusedQuest: false);
        var plain = Assembler.Evaluate(ownQuest, r, Now);
        Assert.Equal(EntryState.Done, plain.State);
        Assert.Equal("", plain.Marker);

        r.Values[item.Id] = new DoneValue(false);
        Assert.Equal(EntryState.Past, Assembler.Evaluate(ownQuest, r, Now).State);
        Assert.Equal(EntryState.Past, Assembler.Evaluate(item, r, Now).State);
    }

    [Fact]
    public void A_reward_counter_with_no_value_needs_its_window_even_after_the_run_has_closed()
    {
        var r = Items.Record();
        var catalog = new Catalog([Items.Yokai]);
        var duringChecklist = Assembler.Assemble(catalog, r, new Settings(), Now);
        var during = duringChecklist.Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Unknown, during.State);
        Assert.Equal("needs: Achievements", during.Marker);
        Assert.Contains(during, duringChecklist.Running);

        var afterChecklist = Assembler.Assemble(catalog, r, new Settings(), Now.AddMonths(2));
        var after = afterChecklist.Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Unknown, after.State);
        Assert.Equal("needs: Achievements", after.Marker);
        Assert.DoesNotContain(after, afterChecklist.Today);
        Assert.DoesNotContain(after, afterChecklist.ThisWeek);
        Assert.DoesNotContain(after, afterChecklist.EndingSoon);
        Assert.DoesNotContain(after, afterChecklist.Running);
    }

    [Fact]
    public void A_running_reward_counter_shows_its_count_and_its_end_and_sits_in_running_events()
    {
        var r = Items.Record();
        r.Values[Items.Yokai.Id] = new CounterValue(12, 39, Now);
        var catalog = new Catalog([Items.Yokai]);
        var checklist = Assembler.Assemble(catalog, r, new Settings(), Now);
        var entry = checklist.Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.StartsWith("12 of 39, ends ", entry.Display);
        Assert.Equal("as of last visit to Achievements", entry.Marker);
        Assert.Contains(entry, checklist.Running);
        Assert.Empty(checklist.EndingSoon);
    }

    [Fact]
    public void A_reward_counter_at_its_cap_is_done_during_and_after_its_window()
    {
        var r = Items.Record();
        r.Values[Items.Yokai.Id] = new CounterValue(39, 39, Now);
        var catalog = new Catalog([Items.Yokai]);
        var during = Assembler.Assemble(catalog, r, new Settings(), Now).Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Done, during.State);
        var after = Assembler.Assemble(catalog, r, new Settings(), Now.AddMonths(2)).Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Done, after.State);
        Assert.Equal("39 of 39", after.Display);
    }

    [Fact]
    public void A_reward_counter_below_its_cap_after_the_window_is_missed()
    {
        var r = Items.Record();
        r.Values[Items.Yokai.Id] = new CounterValue(12, 39, Now);
        var catalog = new Catalog([Items.Yokai]);
        var after = Assembler.Assemble(catalog, r, new Settings(), Now.AddMonths(2)).Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Past, after.State);
        Assert.Equal("12 of 39, missed", after.Display);
    }

    [Fact]
    public void A_done_flag_stored_under_a_gated_counter_is_ignored_during_and_after_the_window()
    {
        // The item was a Done occurrence in an earlier catalog and is a reward Counter now, so a record can still hold a
        // DoneValue under its id. That value says nothing about the reward count: the row must read as never read.
        var r = Items.Record();
        r.Values[Items.Yokai.Id] = new DoneValue(true);
        var catalog = new Catalog([Items.Yokai]);
        var during = Assembler.Assemble(catalog, r, new Settings(), Now).Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Unknown, during.State);
        Assert.Equal("needs: Achievements", during.Marker);
        var after = Assembler.Assemble(catalog, r, new Settings(), Now.AddMonths(2)).Sections.Single(s => s.Category == Category.Events).Groups.Single().Entries.Single();
        Assert.Equal(EntryState.Unknown, after.State);
        Assert.Equal("needs: Achievements", after.Marker);
    }

    [Fact]
    public void A_done_flag_stored_under_an_ungated_counter_never_reads_as_done()
    {
        // The same mismatch on a counter read at login: the stale flag must not stand in for a count in the meantime.
        var item = Items.Yokai with { Freshness = Freshness.AtLogin };
        var r = Items.Record();
        r.Values[item.Id] = new DoneValue(true);
        var entry = Assembler.Evaluate(item, r, Now);
        Assert.NotEqual(EntryState.Done, entry.State);
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.Equal("", entry.Display);
    }

    [Fact]
    public void A_periodic_timer_is_placed_only_when_it_is_due_or_close()
    {
        // Squadron training: a Timer whose item carries a Daily rule. A Timer is never "done"; it is placed in Today, or
        // Ending soon, only when its own occurrence is due or close, not for its whole run.
        var training = new ItemDefinition("reset:squadron:training", Category.Resets, "Timers", "Squadron training", ItemKind.Timer,
            ResetRule.DailyOn(ResetSchedules.GrandCompany), 2, Freshness.AtLogin);
        var catalog = new Catalog([training]);
        var r = Items.Record();

        r.Values[training.Id] = new TimerValue(Now + TimeSpan.FromDays(10));
        var far = Assembler.Assemble(catalog, r, new Settings(), Now);
        Assert.Empty(far.Today);
        Assert.Empty(far.EndingSoon);
        Assert.Equal(0, far.Badge);

        r.Values[training.Id] = new TimerValue(Now + TimeSpan.FromDays(2));
        Assert.Contains(training.Id, Ids(Assembler.Assemble(catalog, r, new Settings(), Now).EndingSoon));

        r.Values[training.Id] = new TimerValue(Now - TimeSpan.FromHours(1));
        var passed = Assembler.Assemble(catalog, r, new Settings(), Now);
        Assert.Contains(training.Id, Ids(passed.Today));
        Assert.Empty(passed.EndingSoon);
    }

    [Fact]
    public void An_observed_item_with_a_window_reads_its_window_before_it_opens()
    {
        // The jumbo cactpot is an ObservedOnly item with a window. The window's Upcoming and Past tests come first, so an
        // unopened window never reads "not seen" as though the draw had been missed.
        var jumbo = new ItemDefinition("reset:cactpot:jumbo", Category.Resets, "Jumbo Cactpot", "Jumbo Cactpot tickets", ItemKind.Counter,
            ResetRule.Window, 0, Freshness.Observed, new Availability(Now + TimeSpan.FromDays(1), Now + TimeSpan.FromDays(8)));
        var entry = Assembler.Evaluate(jumbo, Items.Record(), Now);
        Assert.Equal(EntryState.Upcoming, entry.State);
        Assert.Equal("upcoming", entry.Display);
        Assert.Equal("as seen", entry.Marker);

        var open = jumbo with { Availability = new Availability(Now - TimeSpan.FromDays(1), Now + TimeSpan.FromDays(6)) };
        Assert.Equal(EntryState.NotDone, Assembler.Evaluate(open, Items.Record(), Now).State);
    }

    [Fact]
    public void An_at_login_item_with_no_stored_value_is_in_no_top_group_but_an_observed_one_still_is()
    {
        // Today holds every Daily item whose *stored* period key is not the current one. An AtLogin item no reader has
        // reported has nothing stored, so it is not a thing to do — it is a thing not known. This is what lets the Resets
        // reader stay silent about a hunt board the character has not unlocked without parking it in Today for ever.
        // ObservedOnly is the exception: no reader can ever report it, so its absence is the reading, and the row belongs
        // in Today with its "as seen" marker.
        var r = Items.Record();
        var quiet = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Roulette.Id, Ids(quiet.Today));
        Assert.DoesNotContain(Items.Stickers.Id, Ids(quiet.ThisWeek));
        Assert.DoesNotContain(Items.Moonfire.Id, Ids(quiet.EndingSoon));
        Assert.Contains(Items.Cactpot.Id, Ids(quiet.Today));
        Assert.Equal(1, quiet.Badge);
        Assert.Equal(EntryState.NotDone, Assembler.Evaluate(Items.Roulette, r, Now).State);   // the section still shows it

        r.Values[Items.Roulette.Id] = new DoneValue(false);
        Assert.Contains(Items.Roulette.Id, Ids(Assembler.Assemble(Items.Catalog(), r, new Settings(), Now).Today));
    }

    [Fact]
    public void An_accrual_counter_grants_from_the_clients_own_anchor_when_it_has_one()
    {
        // The client knows when the next three allowances arrive; Core's leve schedule is the fallback. The anchor is
        // stored with the value so it survives a logout, which is the only time the accrual math matters at all.
        var read = new DateTimeOffset(2026, 9, 13, 1, 0, 0, TimeSpan.Zero);
        var r = Items.Record();

        // Anchored two hours from now: no grant yet, then one at the anchor and one at each schedule boundary after it.
        r.Values[Items.Leves.Id] = new CounterValue(40, 100, read, Now + TimeSpan.FromHours(2));
        Assert.Equal("40 of 100", Assembler.Evaluate(Items.Leves, r, Now).Display);
        Assert.Equal("43 of 100", Assembler.Evaluate(Items.Leves, r, Now + TimeSpan.FromHours(3)).Display);
        Assert.Equal("46 of 100", Assembler.Evaluate(Items.Leves, r, Now + TimeSpan.FromHours(12)).Display);

        // With no anchor the schedule's own boundaries are used, exactly as before: 12:00 on the 13th is one grant.
        r.Values[Items.Leves.Id] = new CounterValue(40, 100, read);
        Assert.Equal("43 of 100", Assembler.Evaluate(Items.Leves, r, Now).Display);
        Assert.True(Assembler.Evaluate(Items.Leves, r, Now).Assumed);
    }

    [Fact]
    public void An_item_named_by_the_record_shows_that_name_everywhere()
    {
        var venture = new ItemDefinition("reset:venture:2", Category.Resets, "Timers", "Venture: retainer 3", ItemKind.Timer,
            ResetRule.None, 12, Freshness.AtLogin);
        var r = Items.Record();
        r.Values[venture.Id] = new TimerValue(Now + TimeSpan.FromHours(2));

        var unnamed = Assembler.Evaluate(venture, r, Now);
        Assert.Equal("", unnamed.Label);
        Assert.Equal("Venture: retainer 3", unnamed.Name);

        r.ItemNames[venture.Id] = "Kupo";
        var named = Assembler.Evaluate(venture, r, Now);
        Assert.Equal("Kupo", named.Name);
        Assert.Equal("[ ] Kupo — in 2 h", ChecklistText.Line(named));
        Assert.Single(Search.Filter(Assembler.Assemble(new Catalog([venture]), r, new Settings(), Now), "kupo").Sections
            .Single(s => s.Category == Category.Resets).Groups.Single().Entries);
    }

    [Fact]
    public void A_level_with_progress_shows_it_on_a_second_line()
    {
        var r = Items.Record();
        r.Values[Items.Paladin.Id] = new LevelValue(72, 100, 12_000, 20_000);
        var entry = Assembler.Evaluate(Items.Paladin, r, Now);
        Assert.Equal("72 / 100", entry.Display);
        Assert.Equal("12,000 / 20,000 to the next", entry.Detail);
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.Equal("[ ] Paladin — 72 / 100 · 12,000 / 20,000 to the next", ChecklistText.Line(entry));

        // At the cap there is no next step, so there is no second line and nothing else changes.
        r.Values[Items.Paladin.Id] = new LevelValue(100, 100, 0, 0);
        var capped = Assembler.Evaluate(Items.Paladin, r, Now);
        Assert.Equal("", capped.Detail);
        Assert.Equal(EntryState.Done, capped.State);

        r.Values[Items.Paladin.Id] = new LevelValue(72, 100);
        Assert.Equal("", Assembler.Evaluate(Items.Paladin, r, Now).Detail);
    }

    [Fact]
    public void A_periodic_item_inside_a_window_is_judged_in_its_current_period()
    {
        // The Fashion Report is both windowed and weekly. A stale completion, from last week's period key, reads not done
        // with the window's own display while its window is open, not a bare not-done, and reads missed once the window
        // has closed. Only the Past and Upcoming assertions exercise those cases directly; the arms in between already
        // correct the value along the way.
        var open = new Availability(Now - TimeSpan.FromDays(1), Now + TimeSpan.FromDays(2));
        var report = new ItemDefinition("reset:fashion-report", Category.Resets, "Fashion Report", "Fashion Report", ItemKind.Done, ResetRule.Weekly, 0, Freshness.AtLogin, open);
        var r = Items.Record();
        r.Values[report.Id] = new DoneValue(true, "weekly:2026-09-01");                    // last week's period
        var stale = Assembler.Evaluate(report, r, Now);
        Assert.Equal(EntryState.NotDone, stale.State);
        Assert.Equal("ends in 2 d", stale.Display);
        r.Values[report.Id] = new DoneValue(true, Key(ResetSchedules.Weekly));
        Assert.Equal(EntryState.Done, Assembler.Evaluate(report, r, Now).State);

        var tickets = new ItemDefinition("reset:tickets", Category.Resets, "Fashion Report", "Tickets", ItemKind.Counter, ResetRule.Weekly, 1, Freshness.AtLogin, open);
        r.Values[tickets.Id] = new CounterValue(4, 4, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));   // capped last week
        var refilled = Assembler.Evaluate(tickets, r, Now);
        Assert.Equal(EntryState.NotDone, refilled.State);
        Assert.Equal("0 of 4, ends in 2 d", refilled.Display);
        Assert.True(refilled.Assumed);

        // Past the window, last week's completion is not this week's: missed, not done.
        var closed = report with { Availability = new Availability(Now - TimeSpan.FromDays(5), Now - TimeSpan.FromDays(1)) };
        r.Values[report.Id] = new DoneValue(true, "weekly:2026-09-01");
        Assert.Equal(EntryState.Past, Assembler.Evaluate(closed, r, Now).State);
        // Before the window opens it is upcoming, whatever last week said.
        var future = report with { Availability = new Availability(Now + TimeSpan.FromDays(1), Now + TimeSpan.FromDays(3)) };
        Assert.Equal(EntryState.Upcoming, Assembler.Evaluate(future, r, Now).State);
    }

    [Fact]
    public void A_timer_that_reads_ready_when_passed_says_so_and_the_rest_stay_overdue()
    {
        // The treasure map: the client's next-map instant on a character with no cooldown is the login instant, so
        // "overdue by 3 h" would be the time since login dressed as lateness.
        var map = Items.Map with { ReadyWhenPassed = true };
        var r = Items.Record();
        r.Values[map.Id] = new TimerValue(Now - TimeSpan.FromHours(3));
        var ready = Assembler.Evaluate(map, r, Now);
        Assert.Equal("ready", ready.Display);
        Assert.Equal(EntryState.NotDone, ready.State);
        r.Values[map.Id] = new TimerValue(Now + TimeSpan.FromHours(3));
        Assert.Equal("in 3 h", Assembler.Evaluate(map, r, Now).Display);

        r.Values[Items.Venture.Id] = new TimerValue(Now - TimeSpan.FromHours(3));
        Assert.Equal("overdue by 3 h", Assembler.Evaluate(Items.Venture, r, Now).Display);
    }

    [Fact]
    public void A_locked_row_says_it_is_locked_and_a_row_not_yet_done_does_not()
    {
        // A stored lock drew exactly like unfinished work; the window dims it and says "not unlocked yet".
        var r = Items.Record();
        r.Values[Items.Cactpot.Id] = GateValue.Shut;
        Assert.True(Assembler.Evaluate(Items.Cactpot, r, Now).Locked);
        r.Values[Items.Cactpot.Id] = GateValue.Open;
        Assert.False(Assembler.Evaluate(Items.Cactpot, r, Now).Locked);
        r.Values.Remove(Items.Cactpot.Id);
        Assert.False(Assembler.Evaluate(Items.Cactpot, r, Now).Locked);
    }

    [Fact]
    public void A_locked_observed_row_is_blank_and_in_no_top_group()
    {
        // Nothing stored on an ObservedOnly row reads "not seen" and sits in Today, which is right for a character who
        // could buy a ticket and wrong for one who cannot. The gate reader stores the lock; the assembler reads it as blank.
        var r = Items.Record();
        r.Values[Items.Cactpot.Id] = GateValue.Shut;
        var entry = Assembler.Evaluate(Items.Cactpot, r, Now);
        Assert.Equal(EntryState.NotDone, entry.State);
        Assert.Equal("", entry.Display);
        Assert.Equal("", entry.Marker);
        var c = Assembler.Assemble(Items.Catalog(), r, new Settings(), Now);
        Assert.DoesNotContain(Items.Cactpot.Id, Ids(c.Today));
        Assert.Contains(Items.Cactpot.Id, c.Sections.Single(s => s.Category == Category.Resets).Groups.SelectMany(g => g.Entries).Select(e => e.Item.Id));

        // An open gate that somehow reached the file is nothing stored: "not seen", back in Today.
        r.Values[Items.Cactpot.Id] = GateValue.Open;
        Assert.Equal("not seen", Assembler.Evaluate(Items.Cactpot, r, Now).Display);
        Assert.Contains(Items.Cactpot.Id, Ids(Assembler.Assemble(Items.Catalog(), r, new Settings(), Now).Today));
    }

    [Fact]
    public void An_observed_count_zeroes_at_its_boundary_without_reading_assumed()
    {
        // Two tickets seen yesterday: today none has been seen, which is a fact about this period, not a guess about a refill.
        var r = Items.Record();
        r.Values[Items.Cactpot.Id] = new CounterValue(2, 3, new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero));
        var entry = Assembler.Evaluate(Items.Cactpot, r, Now);
        Assert.Equal("0 of 3", entry.Display);
        Assert.Equal("as seen", entry.Marker);
        Assert.False(entry.Assumed);
        Assert.Contains(Items.Cactpot.Id, Ids(Assembler.Assemble(Items.Catalog(), r, new Settings(), Now).Today));

        r.Values[Items.Cactpot.Id] = new CounterValue(3, 3, Now);
        var full = Assembler.Evaluate(Items.Cactpot, r, Now);
        Assert.Equal(EntryState.Done, full.State);
        Assert.Equal("3 of 3", full.Display);
    }

    [Fact]
    public void The_jumbo_draw_is_placed_by_the_records_data_centre_and_an_unknown_one_is_never_placed()
    {
        // Sunday 2026-09-13 12:00 UTC. Crystal draws Sunday 02:00, so a count read on Saturday evening is last period's; Light
        // draws Saturday 19:00, so the same read is this period's.
        var jumbo = new ItemDefinition("reset:cactpot:jumbo", Category.Resets, "Jumbo Cactpot", "Jumbo Cactpot tickets", ItemKind.Counter,
            ResetRule.WeeklyOn(ResetSchedules.Jumbo), 0, Freshness.Observed);
        var catalog = new Catalog([jumbo]);
        var saturdayEvening = new DateTimeOffset(2026, 9, 12, 20, 0, 0, TimeSpan.Zero);

        var crystal = Items.Record();   // Items.Record's data centre is Crystal
        crystal.Values[jumbo.Id] = new CounterValue(3, 3, saturdayEvening);
        Assert.Equal("0 of 3", Assembler.Evaluate(jumbo, crystal, Now).Display);
        Assert.Contains(jumbo.Id, Ids(Assembler.Assemble(catalog, crystal, new Settings(), Now).ThisWeek));

        var light = Items.Record(2, "Kestrel Vane");
        light.DataCenter = "Light";
        light.Values[jumbo.Id] = new CounterValue(3, 3, saturdayEvening);
        Assert.Equal(EntryState.Done, Assembler.Evaluate(jumbo, light, Now).State);
        Assert.Empty(Assembler.Assemble(catalog, light, new Settings(), Now).ThisWeek);

        // A data centre the table does not know has no draw: the count is shown as read, and the row is attention nowhere.
        var unknown = Items.Record(3, "Momo Lulu");
        unknown.DataCenter = "Shirogane";
        unknown.Values[jumbo.Id] = new CounterValue(1, 3, saturdayEvening);
        var elsewhere = Assembler.Assemble(catalog, unknown, new Settings(), Now);
        Assert.Equal("1 of 3", Assembler.Evaluate(jumbo, unknown, Now).Display);
        Assert.Empty(elsewhere.ThisWeek);
        Assert.Equal(0, elsewhere.Badge);
    }

    private static readonly ItemDefinition WeeklyReport = new("reset:fashion-report", Category.Resets, "Fashion Report", "Fashion Report", ItemKind.Done, ResetRule.Weekly, 0,
        Freshness.Window("Fashion Report"), Recurrence: new Recurrence(TimeSpan.FromDays(3), TimeSpan.FromDays(7)));

    [Fact]
    public void A_recurring_window_is_upcoming_then_running_then_ending_soon_and_done_only_this_week()
    {
        // Judging opens Friday 08:00 UTC, 72 h after the Tuesday 08:00 reset, and closes at the next reset. The window
        // recurs with the schedule, so a completion is this week's only under this week's key, and "past" never happens:
        // the window closes as the period rolls.
        var catalog = new Catalog([WeeklyReport]);
        var r = Items.Record();
        r.Values[WeeklyReport.Id] = new DoneValue(false);
        var wednesday = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var friday = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var tuesdayReset = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

        Assert.Equal(EntryState.Upcoming, Assembler.Evaluate(WeeklyReport, r, wednesday).State);
        Assert.Empty(Assembler.Assemble(catalog, r, new Settings(), wednesday).ThisWeek);

        var open = Assembler.Assemble(catalog, r, new Settings(), friday);
        Assert.Equal("ends in 3 d 20 h", Assembler.Evaluate(WeeklyReport, r, friday).Display);
        Assert.Contains(WeeklyReport.Id, Ids(open.Running));
        Assert.Empty(open.ThisWeek);
        Assert.Equal(0, open.Badge);

        var closing = Assembler.Assemble(catalog, r, new Settings(), Now);   // Sunday: inside the three-day ending-soon window
        Assert.Contains(WeeklyReport.Id, Ids(closing.EndingSoon));
        Assert.Equal(1, closing.Badge);

        r.Values[WeeklyReport.Id] = new DoneValue(true, Key(ResetSchedules.Weekly));
        Assert.Equal(EntryState.Done, Assembler.Evaluate(WeeklyReport, r, Now).State);
        Assert.Equal(0, Assembler.Assemble(catalog, r, new Settings(), Now).Badge);
        Assert.Equal(EntryState.Upcoming, Assembler.Evaluate(WeeklyReport, r, tuesdayReset).State);   // last week's completion, next week's window

        // With nothing stored the row still needs its window: it waits in Running events with its marker while open.
        var unvisited = Items.Record(4, "Unvisited");
        var needs = Assembler.Assemble(catalog, unvisited, new Settings(), friday);
        Assert.Equal("needs: Fashion Report", Assert.Single(needs.Running).Marker);
        Assert.Empty(Assembler.Assemble(catalog, unvisited, new Settings(), wednesday).Running);
    }

    [Fact]
    public void Running_events_sort_a_recurring_window_by_its_current_end()
    {
        // Assemble sorted Running events by the fixed Availability's end; the Fashion Report has none and threw. The report
        // closes on Tuesday, Yo-kai Watch in October, so the report lists first.
        var r = Items.Record();
        r.Values[WeeklyReport.Id] = new DoneValue(false);
        r.Values[Items.Yokai.Id] = new CounterValue(1, 39, Now);
        var friday = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var running = Assembler.Assemble(new Catalog([Items.Yokai, WeeklyReport]), r, new Settings(), friday).Running;
        Assert.Equal([WeeklyReport.Id, Items.Yokai.Id], Ids(running));
    }

    [Fact]
    public void A_held_counter_in_a_window_is_information_never_attention_and_reads_ended_after()
    {
        // The trove counts tomestones held: nothing to finish, so the row never reaches Ending soon or the badge while the
        // trove runs, and afterwards it reads "ended", not "missed", because the tomestones can still be spent.
        var trove = new ItemDefinition("event:trove:51750", Category.Events, "Moogle Treasure Trove", "Irregular Tomestone of Astronomy I", ItemKind.Counter,
            ResetRule.Window, 0, Freshness.AtLogin, new Availability(Now - TimeSpan.FromDays(4), Now + TimeSpan.FromDays(1)), DoneAtCap: false);
        var catalog = new Catalog([trove]);
        var r = Items.Record();
        r.Values[trove.Id] = new CounterValue(120, 999, Now);
        var running = Assembler.Assemble(catalog, r, new Settings(), Now);
        Assert.Contains(trove.Id, Ids(running.Running));
        Assert.Empty(running.EndingSoon);
        Assert.Equal(0, running.Badge);
        Assert.Equal("120 of 999, ends in 1 d", Assembler.Evaluate(trove, r, Now).Display);

        var ended = Assembler.Evaluate(trove, r, Now + TimeSpan.FromDays(2));
        Assert.Equal(EntryState.Past, ended.State);
        Assert.Equal("120 of 999, ended", ended.Display);

        // A used counter in the same window still closes into Ending soon and reads missed, as the reward events do.
        var used = trove with { Id = "event:used", DoneAtCap = true };
        r.Values[used.Id] = new CounterValue(1, 3, Now);
        Assert.Contains(used.Id, Ids(Assembler.Assemble(new Catalog([used]), r, new Settings(), Now).EndingSoon));
        Assert.Equal("1 of 3, missed", Assembler.Evaluate(used, r, Now + TimeSpan.FromDays(2)).Display);

        // The matrix counts events done over available; a held counter is neither and is not an event to count.
        var row = Summariser.Summarise(new Catalog([trove, Items.Moonfire]), r, new Settings(), Now);
        Assert.Equal((0, 1), (row.EventsDone, row.EventsAvailable));
    }

    [Fact]
    public void A_row_hidden_when_blank_is_listed_only_while_it_says_something()
    {
        // A row flagged hidden-when-blank is left out while nothing is stored on it after its category was read, or while
        // a lock is stored. "not read yet" and "needs: <window>" still say something, and an unflagged row, gil, always shows.
        var gil = new ItemDefinition("currency:1", Category.Money, "Currency", "Gil", ItemKind.Counter, ResetRule.None, 0, Freshness.AtLogin, DoneAtCap: false);
        var wolf = gil with { Id = "currency:25", Name = "Wolf Mark", Order = 1, HiddenWhenBlank = true };
        var poetics = gil with { Id = "currency:28", Name = "Allagan Tomestone of Poetics", Order = 2, HiddenWhenBlank = true };
        var saddlebag = new ItemDefinition("storage:saddlebag", Category.Money, "Storage", "Chocobo Saddlebag", ItemKind.Counter, ResetRule.None, 0,
            Freshness.Window("Chocobo Saddlebag"), GroupOrder: 300, DoneAtCap: false, HiddenWhenBlank: true);
        var dresser = saddlebag with { Id = "storage:dresser", Name = "Glamour Dresser", Order = 1, Freshness = Freshness.Window("Glamour Dresser") };
        var catalog = new Catalog([gil, wolf, poetics, saddlebag, dresser]);
        var r = Items.Record();
        r.Values[gil.Id] = new CounterValue(0, 999_999_999, Now);
        r.Values[poetics.Id] = new CounterValue(1_200, 2_000, Now);
        r.Values[saddlebag.Id] = GateValue.Shut;
        IEnumerable<string> Listed() => Assembler.Assemble(catalog, r, new Settings(), Now).Sections.Single(s => s.Category == Category.Money)
            .Groups.SelectMany(g => g.Entries).Select(e => $"{e.Item.Id} {e.Display} {e.Marker}".TrimEnd());
        Assert.Equal(["currency:1 0 of 999999999", "currency:28 1200 of 2000", "storage:dresser unknown needs: Glamour Dresser"], Listed());
        r.CategoryReadAt.Remove(nameof(Category.Money));
        Assert.Equal(["currency:1 0 of 999999999", "currency:25 unknown not read yet", "currency:28 1200 of 2000", "storage:dresser unknown needs: Glamour Dresser"], Listed());
    }

    private static Entry CountedRow(Settings settings, CharacterRecord record, DateTimeOffset now, string id) =>
        Assembler.Assemble(new Catalog(settings.MyTasks.Items()), record, settings, now)
            .Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries.Single(e => e.Item.Id == id);

    [Fact]
    public void A_counted_task_reads_its_count_against_its_number_and_is_done_at_it()
    {
        // Nothing stored reads "0 of 5", a count its clicks, and the number is done. Not done, a daily is in Today, a
        // weekly in This week and a one-off in Today, as their plain forms are; done, none is. A count by hand is a fact,
        // so it never reads "assumed".
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var granary = settings.AddMyTask(Tasks.Counted("Granary", MyTask.Weekly, 3))!;
        var mounts = settings.AddMyTask(Tasks.Counted("Two mounts", MyTask.Once, 2))!;
        var record = Items.Record();
        var catalog = new Catalog(settings.MyTasks.Items());

        var fresh = Assembler.Assemble(catalog, record, settings, Now);
        Assert.Equal([deliver.Id, mounts.Id], Ids(fresh.Today));
        Assert.Equal([granary.Id], Ids(fresh.ThisWeek));
        Assert.Equal(["0 of 5", "0 of 3", "0 of 2"], fresh.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries.Select(e => e.Display));

        record.Values[deliver.Id] = new CounterValue(2, 5, Now);
        var two = CountedRow(settings, record, Now, deliver.Id);
        Assert.Equal((EntryState.NotDone, "2 of 5", "", false), (two.State, two.Display, two.Marker, two.Assumed));

        record.Values[deliver.Id] = new CounterValue(5, 5, Now);
        record.Values[granary.Id] = new CounterValue(3, 3, Now);
        record.Values[mounts.Id] = new CounterValue(2, 2, Now);
        var done = Assembler.Assemble(catalog, record, settings, Now);
        Assert.Empty(done.Today);
        Assert.Empty(done.ThisWeek);
        Assert.Equal(0, done.Badge);
        Assert.All(done.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries, e => Assert.Equal(EntryState.Done, e.State));
        Assert.Equal("5 of 5", CountedRow(settings, record, Now, deliver.Id).Display);
    }

    [Fact]
    public void A_count_starts_over_at_its_tasks_own_reset_and_a_one_off_keeps_it()
    {
        // The daily's count was clicked yesterday, before the 15:00 UTC reset, so today it reads "0 of 5", with no
        // "assumed" marker; the weekly's was clicked this week, after Tuesday 08:00, so it holds; the one-off's was
        // clicked a week ago and never resets.
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var granary = settings.AddMyTask(Tasks.Counted("Granary", MyTask.Weekly, 3))!;
        var mounts = settings.AddMyTask(Tasks.Counted("Two mounts", MyTask.Once, 2))!;
        var record = Items.Record();
        record.Values[deliver.Id] = new CounterValue(4, 5, Now - TimeSpan.FromDays(1));
        record.Values[granary.Id] = new CounterValue(2, 3, Now - TimeSpan.FromDays(2));
        record.Values[mounts.Id] = new CounterValue(1, 2, Now - TimeSpan.FromDays(7));

        var daily = CountedRow(settings, record, Now, deliver.Id);
        Assert.Equal((EntryState.NotDone, "0 of 5", "", false), (daily.State, daily.Display, daily.Marker, daily.Assumed));
        Assert.Equal("2 of 3", CountedRow(settings, record, Now, granary.Id).Display);
        Assert.Equal("1 of 2", CountedRow(settings, record, Now, mounts.Id).Display);
    }

    [Fact]
    public void The_number_on_the_item_wins_over_the_one_stored_with_the_last_click()
    {
        // Raised from 3 to 5 after three clicks: "3 of 5", not done. Lowered from 5 to 3 after four: done, reading
        // "3 of 3", never "4 of 3".
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var record = Items.Record();
        record.Values[deliver.Id] = new CounterValue(3, 3, Now);
        var raised = CountedRow(settings, record, Now, deliver.Id);
        Assert.Equal((EntryState.NotDone, "3 of 5"), (raised.State, raised.Display));

        var draft = deliver.Draft();
        draft.Target = 3;
        settings.MyTasks.Edit(deliver.Id, draft);
        record.Values[deliver.Id] = new CounterValue(4, 5, Now);
        var lowered = CountedRow(settings, record, Now, deliver.Id);
        Assert.Equal((EntryState.Done, "3 of 3"), (lowered.State, lowered.Display));
    }

    [Fact]
    public void Turning_counting_on_or_off_starts_the_task_fresh()
    {
        // A change of kind starts the task fresh: a plain tick under a counted task is a value of the wrong shape, so it
        // reads "0 of 5"; a count under a plain task reads not done.
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var record = Items.Record();
        record.Values[deliver.Id] = new DoneValue(true, ResetSchedules.PeriodKey(ResetSchedules.Daily, Now), Now);
        record.Values[chest.Id] = new CounterValue(5, 5, Now);

        var counted = CountedRow(settings, record, Now, deliver.Id);
        var plain = CountedRow(settings, record, Now, chest.Id);
        Assert.Equal((EntryState.NotDone, "0 of 5"), (counted.State, counted.Display));
        Assert.Equal((EntryState.NotDone, ""), (plain.State, plain.Display));
    }
}
