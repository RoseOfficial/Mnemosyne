using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;

namespace Mnemosyne.Core.Tests;

/// <summary>The tone rules that decide every colour the look draws, a row's bar and its pulse, and the switch in progress. Every
/// checklist here is assembled as the window assembles it, so a tone is read from the very lists it draws.</summary>
public class ToneTests
{
    private static Mnemosyne.Core.Checklist.Checklist Assemble(Catalog catalog, CharacterRecord record, Settings? settings = null) =>
        Assembler.Assemble(catalog, record, settings ?? new Settings(), Items.Now);

    private static Entry Row(Mnemosyne.Core.Checklist.Checklist checklist, ItemDefinition item) => Row(checklist, item.Id);

    private static Entry Row(Mnemosyne.Core.Checklist.Checklist checklist, string id) =>
        checklist.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Single(e => e.Item.Id == id);

    /// <summary>The role golden's Mule: the Events category and the Resets "Daily" group off.</summary>
    private static (CharacterRecord Record, Settings Settings) AriaTheMule()
    {
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Role.CategoryKey(Category.Events));
        mule.Off.Add(Items.Roulette.GroupKey);
        var record = SummariserTests.Aria();
        record.Role = mule.Id;
        return (record, settings);
    }

    [Fact]
    public void A_rows_tone_is_done_quiet_or_the_top_group_the_checklist_placed_it_in()
    {
        // A row's tone, on Aria as the aria golden prints it: a done row reads Done wherever it is; an unknown or upcoming row reads
        // Quiet; a row in Today, This week or Ending soon reads that group's tone; a row the checklist placed in no top group reads Neutral.
        var checklist = Assemble(Items.Catalog(), SummariserTests.Aria());
        Tone Of(ItemDefinition item) => Tones.Of(Row(checklist, item), checklist);

        Assert.Equal(Tone.Done, Of(Items.Roulette));
        Assert.Equal(Tone.Done, Of(Items.Msq1));
        Assert.Equal(Tone.Done, Of(Items.Rising));
        Assert.Equal(Tone.Today, Of(Items.Allowances));
        Assert.Equal(Tone.Today, Of(Items.Cactpot));
        Assert.Equal(Tone.ThisWeek, Of(Items.Stickers));
        Assert.Equal(Tone.EndingSoon, Of(Items.Moonfire));
        Assert.Equal(Tone.EndingSoon, Of(Items.Venture));
        Assert.Equal(Tone.Neutral, Of(Items.Msq2));
        Assert.Equal(Tone.Neutral, Of(Items.Paladin));
        Assert.Equal(Tone.Neutral, Of(Items.Leves));
        Assert.Equal(Tone.Neutral, Of(Items.Map));
        Assert.Equal(Tone.Quiet, Of(Items.Starlight));
        Assert.Equal(Tone.Quiet, Of(Items.Eureka));
        Assert.Equal(Tone.Quiet, Of(Items.Achievement));
    }

    [Fact]
    public void A_running_event_reads_running_and_a_locked_missed_or_unread_row_reads_quiet_even_in_a_top_group()
    {
        // Quiet comes before the top group. A long event not done reads Running. A stored lock, a missed run and a reward event whose
        // window still needs its visit read Quiet; the last of them sits in Running events, where its amber "needs:" pill is the call
        // to action.
        var running = new ItemDefinition("event:long:2026", Category.Events, "Long Event", "Long Event 2026", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
            new Availability(Items.Now - TimeSpan.FromDays(1), Items.Now + TimeSpan.FromDays(20)));
        var record = Items.Record();
        record.Values[running.Id] = new DoneValue(false);
        record.Values[Items.Rising.Id] = new DoneValue(false);
        record.Values[Items.Cactpot.Id] = GateValue.Shut;
        var checklist = Assemble(new Catalog([running, Items.Rising, Items.Cactpot, Items.Yokai]), record);

        Assert.Equal(Tone.Running, Tones.Of(Row(checklist, running), checklist));
        Assert.Equal(EntryState.Past, Row(checklist, Items.Rising).State);
        Assert.Equal(Tone.Quiet, Tones.Of(Row(checklist, Items.Rising), checklist));
        Assert.True(Row(checklist, Items.Cactpot).Locked);
        Assert.Equal(Tone.Quiet, Tones.Of(Row(checklist, Items.Cactpot), checklist));
        Assert.Contains(checklist.Running, e => e.Item.Id == Items.Yokai.Id);
        Assert.Equal(Tone.Quiet, Tones.Of(Row(checklist, Items.Yokai), checklist));
    }

    [Fact]
    public void A_row_off_for_the_role_reads_quiet_whatever_its_state()
    {
        // A row off for the character's role is dimmed whatever its state, so it reads neither as work nor as done. Off for the role
        // therefore comes before Done: the done roulette and the done Rising read Quiet under the Mule, and so do the Moonfire Faire
        // and the Tribal allowances, which would be Ending soon and Today. A row the role keeps reads as before.
        var (record, settings) = AriaTheMule();
        var checklist = Assemble(Items.Catalog(), record, settings);
        Tone Of(ItemDefinition item) => Tones.Of(Row(checklist, item), checklist);

        Assert.Equal(Tone.Quiet, Of(Items.Roulette));
        Assert.Equal(Tone.Quiet, Of(Items.Rising));
        Assert.Equal(Tone.Quiet, Of(Items.Moonfire));
        Assert.Equal(Tone.Quiet, Of(Items.Allowances));
        Assert.Equal(Tone.ThisWeek, Of(Items.Stickers));
        Assert.Equal(Tone.EndingSoon, Of(Items.Venture));
        Assert.Equal(Tone.Done, Of(Items.Msq1));
    }

    [Fact]
    public void A_tone_is_looked_up_by_reference_so_a_searched_checklist_reads_the_same_tones_and_the_look_is_kept()
    {
        // The top groups hold the very Entry objects the sections list, and a search keeps them, so the look finds a row's group by
        // reference, in one dictionary lookup, never by scanning a list per row. It is built once per checklist and kept with it;
        // another assembly of the same record is another checklist, with a look of its own.
        var checklist = Assemble(Items.Catalog(), SummariserTests.Aria());
        Assert.Same(checklist.ThisWeek.Single(), Row(checklist, Items.Stickers));

        var searched = Search.Filter(checklist, "tails");
        Assert.Same(checklist.ThisWeek.Single(), searched.ThisWeek.Single());
        Assert.Equal(Tone.ThisWeek, Tones.Of(Row(searched, Items.Stickers), checklist));
        Assert.Same(ChecklistLook.For(checklist), ChecklistLook.For(checklist));
        Assert.NotSame(ChecklistLook.For(checklist), ChecklistLook.For(Assemble(Items.Catalog(), SummariserTests.Aria())));
    }

    [Fact]
    public void A_characters_urgency_is_ending_soon_then_today_then_this_week_when_weeklies_count_then_done()
    {
        // A character's urgency, the tone of its badge pill. It reads Done exactly when the badge is 0.
        Assert.Equal(Tone.EndingSoon, Tones.Urgency(Assemble(Items.Catalog(), SummariserTests.Aria()), weekliesCount: true));

        var record = Items.Record();
        record.Values[Items.Roulette.Id] = new DoneValue(false);
        record.Values[Items.Stickers.Id] = new CounterValue(3, 9, Items.Now);
        Assert.Equal(Tone.Today, Tones.Urgency(Assemble(new Catalog([Items.Roulette, Items.Stickers]), record), weekliesCount: true));

        var week = Assemble(new Catalog([Items.Stickers]), record);
        Assert.Equal(Tone.ThisWeek, Tones.Urgency(week, weekliesCount: true));
        Assert.Equal(Tone.Done, Tones.Urgency(week, weekliesCount: false));
        Assert.Equal(0, Assemble(new Catalog([Items.Stickers]), record, new Settings { WeekliesCount = false }).Badge);
        Assert.Equal(Tone.Done, Tones.Urgency(Assemble(Catalog.Empty, record), weekliesCount: true));
    }

    [Fact]
    public void A_categorys_and_a_groups_tally_count_the_rows_that_can_be_finished_and_leave_a_row_off_for_the_role_out()
    {
        // A category's and a group's done/total. Aria's Resets list six rows; four can be finished, the done roulette among them (the
        // levequest allowances accrue and the treasure map is a timer). Under the Mule the Daily rows count in neither number, so
        // Resets reads 0 / 1 and Daily 0 / 0; Events are off entirely.
        var aria = ChecklistLook.For(Assemble(Items.Catalog(), SummariserTests.Aria()));
        Assert.Equal(new Tally(1, 4), aria.Of(Category.Resets));
        Assert.Equal(new Tally(1, 3), aria.Of(Category.Resets, "Daily"));
        Assert.Equal(new Tally(0, 1), aria.Of(Category.Resets, "Weekly"));
        Assert.Equal(new Tally(1, 2), aria.Of(Category.Story));
        // Events has three rows (Moonfire's window open, Rising done, Starlight's window not yet open), but Starlight is Upcoming,
        // known but not doable yet, so it is left out; the bar reads 1 / 2, not 1 / 3.
        Assert.Equal(new Tally(1, 2), aria.Of(Category.Events));
        Assert.Equal(default, aria.Of(Category.MyTasks));

        var (record, settings) = AriaTheMule();
        var mule = ChecklistLook.For(Assemble(Items.Catalog(), record, settings));
        Assert.Equal(new Tally(0, 1), mule.Of(Category.Resets));
        Assert.Equal(new Tally(0, 0), mule.Of(Category.Resets, "Daily"));
        Assert.Equal(new Tally(0, 0), mule.Of(Category.Events));
        Assert.Equal(0.5f, new Tally(1, 2).Fraction);
        Assert.Equal(0f, new Tally(0, 0).Fraction);
    }

    [Fact]
    public void A_bar_counts_only_what_the_character_can_do_now()
    {
        // A bar counts only what it can do now: a locked row (not unlocked yet), a blank row (an AtLogin row with nothing stored
        // after its category was read), a missed (Past) run and an upcoming run are known but not available now, and are left out
        // of a bar; only what is known and doable now, done or still to do, counts.
        //
        // Resets with a done roulette, done stickers, a blank Tribal allowances row and a locked Mini cactpot: everything doable is
        // done, so it reads Tally(2, 2), a full bar.
        var probe = Items.Record();
        probe.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-12");
        probe.Values[Items.Stickers.Id] = new CounterValue(9, 9, Items.Now);
        probe.Values[Items.Cactpot.Id] = GateValue.Shut;
        // Items.Allowances is left unset: an AtLogin row with nothing stored, its category already read (Items.Record reads every category),
        // is blank (Assembler.IsBlank).
        var checklist = Assemble(new Catalog([Items.Roulette, Items.Allowances, Items.Stickers, Items.Cactpot]), probe);
        Assert.Null(Row(checklist, Items.Allowances).Value);
        Assert.True(Row(checklist, Items.Cactpot).Locked);
        Assert.Equal(new Tally(2, 2), ChecklistLook.For(checklist).Of(Category.Resets));

        // An Events group with a done run, a missed (Past) run and an upcoming run counts only the done run.
        var missed = new ItemDefinition("event:missed:2026", Category.Events, "Missed Event", "Missed Event 2026", ItemKind.Done, ResetRule.Window, 2, Freshness.AtLogin,
            new Availability(Items.Now - TimeSpan.FromDays(30), Items.Now - TimeSpan.FromDays(20)));
        var events = Items.Record();
        events.Values[Items.Rising.Id] = new DoneValue(true);
        var eventsChecklist = Assemble(new Catalog([Items.Rising, missed, Items.Starlight]), events);
        Assert.Equal(EntryState.Done, Row(eventsChecklist, Items.Rising).State);
        Assert.Equal(EntryState.Past, Row(eventsChecklist, missed).State);
        Assert.Equal(EntryState.Upcoming, Row(eventsChecklist, Items.Starlight).State);
        Assert.Equal(new Tally(1, 1), ChecklistLook.For(eventsChecklist).Of(Category.Events));

        // A fresh record with nothing read gives no bar on any category: every AtLogin row reads Unknown ("not read yet"), a windowed row
        // reads Unknown, Upcoming or Past the same as ever, and a gated row reads Unknown ("needs:", "visit:"). Cactpot is left out of this
        // catalog: an ObservedOnly row is exempt from "not read yet" (Assembler.Missing), which is exactly what the next check covers.
        var fresh = new CharacterRecord { ContentId = 9, Name = "Momo Lulu", HomeWorld = "Balmung", DataCenter = "Crystal", FirstSeen = Items.Now, LastSeen = Items.Now };
        var unread = ChecklistLook.For(Assemble(new Catalog([.. Items.Catalog().Items.Where(i => i.Id != Items.Cactpot.Id)]), fresh));
        foreach (var category in Enum.GetValues<Category>()) Assert.Equal(default, unread.Of(category));

        // An ObservedOnly row with nothing stored ("not seen") is State NotDone, not Unknown, so it stays available even unread.
        Assert.Equal(EntryState.NotDone, Row(Assemble(new Catalog([Items.Cactpot]), Items.Record()), Items.Cactpot).State);
        Assert.True(Tones.IsAvailable(Row(Assemble(new Catalog([Items.Cactpot]), Items.Record()), Items.Cactpot)));

        // A never-ticked My task ("0 of 5", "ready") is State NotDone with no value stored, and a Manual row is never blank, so it stays
        // available too.
        var taskSettings = new Settings();
        var errand = taskSettings.AddMyTask(Tasks.Draft("Weekly errand", MyTask.Once))!;
        var myTasks = Assemble(new Catalog([.. taskSettings.MyTasks.Items()]), Items.Record(), taskSettings);
        Assert.Equal(EntryState.NotDone, Row(myTasks, errand.Id).State);
        Assert.Equal(new Tally(0, 1), ChecklistLook.For(myTasks).Of(Category.MyTasks));
    }

    [Fact]
    public void Only_rows_that_can_be_finished_count_so_money_has_no_bar_and_resets_read_full_when_every_daily_and_weekly_is_done()
    {
        // A bar counts only rows you can finish (dailies, weeklies, quests, levels, counters with a goal, My tasks). A card with
        // nothing finishable, such as Money, shows no bar at all. Money counts nothing, a retainer's level included, and so has no
        // bar; nor does this synthetic catalog's Grand Company group, built with only the current-company row, which is information
        // (the real catalog's Grand Company group also carries the three company rank rows, Level rows, which do count, so it is
        // not barless there). A job's level counts; so does a My tasks "every" task, cooling down and done. With every daily and
        // weekly done, Resets read full, the accruing levequest allowances and the treasure map left out.
        var level = new ItemDefinition("retainer:0:level", Category.Money, "Retainers", "Retainer 1", ItemKind.Level, ResetRule.None, 0, Freshness.AtLogin);
        var gil = new ItemDefinition("currency:1", Category.Money, "Currencies", "Gil", ItemKind.Counter, ResetRule.None, 1, Freshness.AtLogin, DoneAtCap: false);
        var company = new ItemDefinition(CatalogBuilder.CurrentCompanyId, Category.Jobs, "Grand Company", "Grand Company", ItemKind.Done, ResetRule.None, 0, Freshness.AtLogin);
        var settings = new Settings();
        var craft = settings.AddMyTask(Tasks.Draft("Three-day craft", MyTask.Every, 72))!;
        var record = SummariserTests.Aria();
        record.Values[level.Id] = new LevelValue(50, 100);
        record.Values[gil.Id] = new CounterValue(1_234_567, 999_999_999, Items.Now);
        record.Values[company.Id] = new DoneValue(false);
        record.Values[craft.Id] = craft.TickValue(Items.Now - TimeSpan.FromHours(24))!;
        var checklist = Assemble(new Catalog([.. Items.Catalog().Items, level, gil, company, .. settings.MyTasks.Items()]), record, settings);
        var look = ChecklistLook.For(checklist);

        Assert.Equal(new Tally(0, 0), look.Of(Category.Money));
        Assert.Equal(new Tally(0, 0), look.Of(Category.Money, "Retainers"));
        Assert.False(Tones.CanBeDone(Row(checklist, level)));
        Assert.Equal(new Tally(0, 1), look.Of(Category.Jobs));
        Assert.Equal(new Tally(0, 0), look.Of(Category.Jobs, "Grand Company"));
        Assert.Equal(new Tally(1, 1), look.Of(Category.MyTasks));
        Assert.True(Tones.CanBeDone(Row(checklist, craft.Id)));
        Assert.False(Tones.CanBeDone(Row(checklist, Items.Map)));
        Assert.False(Tones.CanBeDone(Row(checklist, Items.Leves)));

        // A held counter outside Money is information too (MGP under Content): never done at its cap, so never finished.
        var mgp = new ItemDefinition("currency:29", Category.Content, "Gold Saucer", "MGP", ItemKind.Counter, ResetRule.None, 0, Freshness.AtLogin, DoneAtCap: false);
        var saucer = Items.Record();
        saucer.Values[mgp.Id] = new CounterValue(9_999_999, 9_999_999, Items.Now);
        var held = Assemble(new Catalog([mgp]), saucer);
        Assert.False(Tones.CanBeDone(Row(held, mgp)));
        Assert.Equal(new Tally(0, 0), ChecklistLook.For(held).Of(Category.Content));

        // Owning an estate is information, as the current Grand Company is. The four Housing rows the builder makes from the
        // fixture are never finishable, whatever they hold.
        var housing = CatalogBuilder.Build(new FixtureCatalogSource().Load(), CatalogExtras.Load()).InCategory(Category.Content)
            .Where(i => i.Id.StartsWith("housing:", StringComparison.Ordinal)).ToList();
        Assert.Equal(["housing:apartment", "housing:chambers", "housing:fc", "housing:personal"], housing.Select(i => i.Id).Order(StringComparer.Ordinal));
        Assert.All(housing, item => Assert.False(Tones.CanBeDone(new Entry(item, new DoneValue(true), EntryState.Done, "done", false, ""))));

        var done = Items.Record();
        done.Values[Items.Roulette.Id] = new DoneValue(true, "daily:2026-09-12");
        done.Values[Items.Allowances.Id] = new CounterValue(12, 12, Items.Now);
        done.Values[Items.Cactpot.Id] = new CounterValue(3, 3, Items.Now);
        done.Values[Items.Stickers.Id] = new CounterValue(9, 9, Items.Now);
        done.Values[Items.Leves.Id] = new CounterValue(40, 100, Items.Now);
        done.Values[Items.Map.Id] = new TimerValue(Items.Now + TimeSpan.FromDays(5));
        var resets = ChecklistLook.For(Assemble(new Catalog([Items.Roulette, Items.Allowances, Items.Cactpot, Items.Stickers, Items.Leves, Items.Map]), done)).Of(Category.Resets);
        Assert.Equal(new Tally(4, 4), resets);
        Assert.Equal(1f, resets.Fraction);
    }

    [Fact]
    public void A_board_lines_tone_is_its_own_groups_and_a_pinned_line_keeps_it()
    {
        // An All tasks line's tone is its group's. A line's own group is never Pinned, so the pinned stickers line, drawn under
        // Pinned, still reads This week. Pinned is a place, not a status: it reads Neutral, and the look draws its label gold.
        var aria = SummariserTests.Aria();
        var preferences = new TaskPreferences();
        preferences.Pin(Items.Stickers.Id);
        var board = Board.Build([(Record: aria, Checklist: Assemble(Items.Catalog(), aria))], preferences, Items.Now);

        var pinned = board.In(BoardGroup.Pinned).Single();
        Assert.Equal(Items.Stickers.Id, pinned.Id);
        Assert.Equal(Tone.ThisWeek, Tones.OfLine(pinned));
        Assert.All(board.In(BoardGroup.EndingSoon), line => Assert.Equal(Tone.EndingSoon, Tones.OfLine(line)));
        Assert.All(board.In(BoardGroup.Today), line => Assert.Equal(Tone.Today, Tones.OfLine(line)));
        Assert.NotEmpty(board.In(BoardGroup.Today));
        Assert.Equal(Tone.Running, Tones.OfGroup(BoardGroup.Running));
        Assert.Equal(Tone.Neutral, Tones.OfGroup(BoardGroup.Pinned));
    }

    [Fact]
    public void A_marker_is_amber_when_it_asks_for_a_visit_and_quiet_when_it_only_qualifies_the_value()
    {
        // A marker is an amber pill, e.g. visit: ..., and as seen or assumed is a quiet pill. A marker on a row off for the role
        // stays quiet, as the window draws it dimmed.
        var record = SummariserTests.Aria();
        record.Values[Items.Allowances.Id] = new CounterValue(4, 12, Items.Now - TimeSpan.FromDays(1));
        var checklist = Assemble(Items.Catalog(), record);

        Assert.Equal(Tone.Today, Tones.OfMarker(Row(checklist, Items.Eureka)));          // visit: Eureka
        Assert.Equal(Tone.Today, Tones.OfMarker(Row(checklist, Items.Achievement)));     // needs: Achievements
        Assert.Equal(Tone.Quiet, Tones.OfMarker(Row(checklist, Items.Cactpot)));         // as seen
        Assert.Equal("assumed", Row(checklist, Items.Allowances).Marker);
        Assert.Equal(Tone.Quiet, Tones.OfMarker(Row(checklist, Items.Allowances)));
        Assert.Equal(Tone.Neutral, Tones.OfMarker(Row(checklist, Items.Roulette)));      // no marker, no pill
        Assert.Equal(Tone.Quiet, Tones.OfMarker(Row(checklist, Items.Achievement) with { OffFor = "Mule" }));

        // Once the window has been visited, the marker only dates the value, and is quiet.
        var visited = SummariserTests.Aria();
        visited.Values[Items.Achievement.Id] = new DoneValue(false);
        var dated = Row(Assemble(Items.Catalog(), visited), Items.Achievement);
        Assert.Equal("as of last visit to Achievements", dated.Marker);
        Assert.Equal(Tone.Quiet, Tones.OfMarker(dated));
    }

    [Fact]
    public void A_needs_marker_says_which_window_to_open_and_no_other_marker_does()
    {
        var checklist = Assemble(Items.Catalog(), SummariserTests.Aria());
        Assert.Equal("needs: Achievements", Row(checklist, Items.Achievement).Marker);
        Assert.Equal("Open Achievements once on this character to read this row.", Tones.HintOf(Row(checklist, Items.Achievement)));
        Assert.Null(Tones.HintOf(Row(checklist, Items.Eureka)));      // "visit: Eureka" names a place, which its words already say
        Assert.Null(Tones.HintOf(Row(checklist, Items.Cactpot)));     // "as seen"
        Assert.Null(Tones.HintOf(Row(checklist, Items.Roulette)));    // no marker
    }

    [Fact]
    public void A_rows_bar_reads_its_count_as_its_words_do_its_experience_or_its_level_and_a_held_counter_or_a_plain_row_has_none()
    {
        // A job's experience, a counted My task, and any count with a cap are shown as a thin bar. The bar reads the count the words
        // read: Tribal allowances read before the daily reset read "0 of 12" and an empty bar, never the stored 4 of 12. A held
        // counter (gil) is never done at its cap and has no bar.
        var gil = new ItemDefinition("currency:1", Category.Money, "Currencies", "Gil", ItemKind.Counter, ResetRule.None, 1, Freshness.AtLogin, DoneAtCap: false);
        var settings = new Settings();
        var dailies = settings.AddMyTask(Tasks.Counted("Five dailies", MyTask.Daily, 5))!;
        var record = SummariserTests.Aria();
        record.Values[Items.Allowances.Id] = new CounterValue(4, 12, Items.Now - TimeSpan.FromDays(1));
        record.Values[gil.Id] = new CounterValue(1_234_567, 999_999_999, Items.Now);
        var checklist = Assemble(new Catalog([.. Items.Catalog().Items, gil, .. settings.MyTasks.Items()]), record, settings);
        Meter? Of(string id) => Tones.MeterOf(Row(checklist, id));

        Assert.Equal("0 of 12", Row(checklist, Items.Allowances).Display);
        Assert.Equal(new Meter(0, 12), Of(Items.Allowances.Id));
        Assert.Equal(new Meter(7, 9), Of(Items.Stickers.Id));
        Assert.Equal(new Meter(40, 100), Of(Items.Leves.Id));
        Assert.Equal(new Meter(72, 100), Of(Items.Paladin.Id));
        Assert.Equal(new Meter(0, 5), Of(dailies.Id));
        Assert.Null(Of(gil.Id));
        Assert.Null(Of(Items.Roulette.Id));
        Assert.Null(Of(Items.Venture.Id));

        record.Values[Items.Paladin.Id] = new LevelValue(72, 100, 1_200, 4_800);
        Assert.Equal(new Meter(1_200, 4_800), Tones.MeterOf(Row(Assemble(Items.Catalog(), record), Items.Paladin)));
        record.Values[Items.Paladin.Id] = new LevelValue(100, 100);
        Assert.Equal(new Meter(100, 100), Tones.MeterOf(Row(Assemble(Items.Catalog(), record), Items.Paladin)));
        Assert.Equal(0.25f, new Meter(1, 4).Fraction);
        Assert.Equal(1f, new Meter(15, 12).Fraction);
        Assert.Equal(0f, new Meter(3, 0).Fraction);
    }

    [Fact]
    public void A_row_pulses_while_it_is_in_ending_soon_and_ends_within_the_hour()
    {
        // A gentle pulse on a row that ends within the hour. A venture 30 minutes out and an event window closing in 45 pulse; a
        // venture two hours out does not, nor an overdue timer in Today, which has already ended.
        var closing = new ItemDefinition("event:closing:2026", Category.Events, "Closing Event", "Closing Event 2026", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
            new Availability(Items.Now - TimeSpan.FromDays(10), Items.Now + TimeSpan.FromMinutes(45)));
        var record = Items.Record();
        record.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromMinutes(30));
        record.Values[Items.Map.Id] = new TimerValue(Items.Now - TimeSpan.FromHours(1));
        record.Values[closing.Id] = new DoneValue(false);
        var checklist = Assemble(new Catalog([Items.Venture, Items.Map, closing]), record);
        bool Pulses(ItemDefinition item)
        {
            var entry = Row(checklist, item);
            return Tones.Pulses(entry, Tones.Of(entry, checklist), record, Items.Now);
        }

        Assert.True(Pulses(Items.Venture));
        Assert.True(Pulses(closing));
        Assert.Equal(Tone.Today, Tones.Of(Row(checklist, Items.Map), checklist));
        Assert.False(Pulses(Items.Map));

        record.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(2));
        var later = Assemble(new Catalog([Items.Venture]), record);
        var venture = later.EndingSoon.Single();
        Assert.False(Tones.Pulses(venture, Tones.Of(venture, later), record, Items.Now));

        // The tone guards the pulse: a done event closing in 45 minutes, and a venture 30 minutes out on a character whose role switches Money
        // off, end within the hour and do not pulse.
        record.Values[closing.Id] = new DoneValue(true);
        var finished = Assemble(new Catalog([closing]), record);
        var over = Row(finished, closing);
        Assert.Equal(Tone.Done, Tones.Of(over, finished));
        Assert.False(Tones.Pulses(over, Tones.Of(over, finished), record, Items.Now));
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Role.CategoryKey(Category.Money));
        record.Role = mule.Id;
        record.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromMinutes(30));
        var muled = Assemble(new Catalog([Items.Venture]), record, settings);
        var off = Row(muled, Items.Venture);
        Assert.Equal(Tone.Quiet, Tones.Of(off, muled));
        Assert.False(Tones.Pulses(off, Tones.Of(off, muled), record, Items.Now));
    }

    [Fact]
    public void A_switch_is_in_progress_from_the_click_until_that_character_is_logged_in_or_two_minutes_pass()
    {
        // The switch button pulses while a switch it asked for is in progress. Mnemosyne reads nothing back from Lifestream, so it
        // ends when the character asked for is the one logged in, or after SwitchRules.Patience.
        var asked = Items.Now;
        Assert.True(SwitchRules.InProgress(2, asked, 1, 2, asked + TimeSpan.FromSeconds(5)));
        Assert.True(SwitchRules.InProgress(2, asked, null, 2, asked + TimeSpan.FromSeconds(40)));     // logged out on the way
        Assert.False(SwitchRules.InProgress(2, asked, 2, 2, asked + TimeSpan.FromSeconds(50)));       // arrived
        Assert.False(SwitchRules.InProgress(2, asked, 1, 3, asked + TimeSpan.FromSeconds(5)));        // another character's button
        Assert.False(SwitchRules.InProgress(2, asked, 1, 2, asked + SwitchRules.Patience));           // gave up waiting
        Assert.Equal(TimeSpan.FromMinutes(2), SwitchRules.Patience);
    }

    [Fact]
    public void A_group_with_an_icon_row_keeps_the_icon_square_on_every_row_in_it()
    {
        // A row with no icon draws a blank square of the icon's size: a group where any row has an icon reserves the square on each
        // of its rows, so their names line up; a group with none has no square.
        var paladin = Items.Paladin with { IconId = CatalogBuilder.JobIconBase + 19 };
        var warrior = new ItemDefinition("job:2", Category.Jobs, "Tanks", "Warrior", ItemKind.Level, ResetRule.None, 2, Freshness.AtLogin);
        var look = ChecklistLook.For(Assemble(new Catalog([paladin, warrior, Items.Roulette]), SummariserTests.Aria()));

        Assert.True(look.HasIcons(Category.Jobs, "Tanks"));
        Assert.False(look.HasIcons(Category.Resets, "Daily"));
        Assert.False(look.HasIcons(Category.Money, "Retainers"));
    }
}
