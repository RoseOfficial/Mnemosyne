using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class TrackerTests
{
    private const string Dir = @"C:\cfg\Mnemosyne";
    private readonly InMemoryFileSystem _fs = new();
    private readonly FakeClock _clock = new(Items.Now);
    private readonly List<string> _log = new();

    private Tracker Tracker(params IProgressReader[] readers) => new(_clock, new RecordStore(_fs, Dir), readers, "2026.09.01.0000.0000", _log.Add);

    [Fact]
    public void Login_creates_the_record_runs_the_login_readers_stamps_and_saves()
    {
        var story = new FakeReader(Category.Story, ReaderTrigger.Login | ReaderTrigger.Unlock);
        story.Next.Add(new Reading("quest:1", new DoneValue(true)));
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval);
        var t = Tracker(story, money);

        t.Login(new FakeIdentity());

        Assert.NotNull(t.Current);
        Assert.Equal("Aria Whitewind", t.Current!.Name);
        Assert.Equal("Crystal", t.Current.DataCenter);
        Assert.Equal(Items.Now, t.Current.FirstSeen);
        Assert.Equal(new DoneValue(true), t.Current.Values["quest:1"]);
        Assert.Equal(Items.Now, t.Current.CategoryReadAt["Story"]);
        Assert.Equal(Items.Now, t.Current.CategoryReadAt["Money"]);
        Assert.Equal(1, story.Calls);
        Assert.Equal(1, money.Calls);
        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "1.json")));
        Assert.Single(t.All);
    }

    [Fact]
    public void A_second_login_loads_the_existing_record_and_refreshes_identity_and_last_seen()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        t.Logout();
        Assert.Null(t.Current);

        _clock.Advance(TimeSpan.FromDays(2));
        var t2 = Tracker();
        t2.Login(new FakeIdentity { Name = "Aria Renamed" });
        Assert.Equal("Aria Renamed", t2.Current!.Name);
        Assert.Equal(Items.Now, t2.Current.FirstSeen);
        Assert.Equal(_clock.UtcNow, t2.Current.LastSeen);
        Assert.Single(t2.All);
    }

    [Fact]
    public void Login_is_refused_until_the_identity_is_loaded()
    {
        var t = Tracker();
        t.Login(new FakeIdentity { IsLoaded = false });
        Assert.Null(t.Current);
        Assert.Contains(_log, l => l.Contains("not loaded"));
    }

    [Fact]
    public void Tick_runs_interval_readers_and_dirty_categories_then_saves_once()
    {
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval);
        var story = new FakeReader(Category.Story, ReaderTrigger.Login | ReaderTrigger.Unlock);
        var jobs = new FakeReader(Category.Jobs, ReaderTrigger.Login | ReaderTrigger.LevelOrJob);
        var t = Tracker(money, story, jobs);
        t.Login(new FakeIdentity());
        var version = t.Version;
        _fs.Files.Clear();
        var writesBefore = _fs.Writes;

        _clock.Advance(TimeSpan.FromSeconds(5));
        money.Next.Add(new Reading("currency:1", new CounterValue(100, 2000, _clock.UtcNow)));
        t.Tick();
        Assert.Equal(2, money.Calls);
        Assert.Equal(1, story.Calls);
        Assert.Equal(1, jobs.Calls);
        Assert.Equal(_clock.UtcNow, t.Current!.CategoryReadAt["Money"]);
        Assert.True(t.Version > version);
        Assert.Single(_fs.Files);                       // the record, and no .tmp left behind
        Assert.Equal(writesBefore + 1, _fs.Writes);     // exactly one write for a tick that changed a value

        t.Tick();                                       // nothing changed: no write at all
        Assert.Equal(3, money.Calls);
        Assert.Equal(writesBefore + 1, _fs.Writes);

        t.MarkDirty(ReaderTrigger.Unlock);
        t.Tick();
        Assert.Equal(2, story.Calls);
        Assert.Equal(1, jobs.Calls);
    }

    [Fact]
    public void A_counter_whose_only_change_is_its_read_instant_is_stored_but_not_written()
    {
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval) { Stamp = () => _clock.UtcNow };
        money.Next.Add(new Reading("currency:1", new CounterValue(100, 2000, _clock.UtcNow)));
        var t = Tracker(money);
        t.Login(new FakeIdentity());
        var writes = _fs.Writes;
        for (var i = 0; i < 3; i++) { _clock.Advance(TimeSpan.FromSeconds(5)); t.Tick(); }
        Assert.Equal(writes, _fs.Writes);
        Assert.Equal(_clock.UtcNow, ((CounterValue)t.Current!.Values["currency:1"]).ReadAt);

        money.Next[0] = new Reading("currency:1", new CounterValue(150, 2000, _clock.UtcNow));
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);
    }

    [Fact]
    public void Tick_without_a_character_does_nothing()
    {
        var money = new FakeReader(Category.Money, ReaderTrigger.Interval);
        var t = Tracker(money);
        t.Tick();
        Assert.Equal(0, money.Calls);
    }

    [Fact]
    public void A_throwing_reader_is_disabled_for_the_session_with_the_game_version()
    {
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval) { Throw = true };
        var t = Tracker(money);
        t.Login(new FakeIdentity());
        Assert.Equal(1, money.Calls);
        Assert.Contains("2026.09.01.0000.0000", t.DisabledReason(Category.Money));
        Assert.False(t.Current!.CategoryReadAt.ContainsKey("Money"));

        t.Tick();
        Assert.Equal(1, money.Calls);
        Assert.Single(_log, l => l.Contains("disabled"));
    }

    [Fact]
    public void A_reader_reports_only_what_it_read_and_earlier_values_survive()
    {
        var story = new FakeReader(Category.Story, ReaderTrigger.Login | ReaderTrigger.Unlock);
        story.Next.Add(new Reading("quest:1", new DoneValue(true)));
        story.Next.Add(new Reading("quest:2", new DoneValue(false)));
        var t = Tracker(story);
        t.Login(new FakeIdentity());

        story.Next.Clear();
        story.Next.Add(new Reading("quest:2", new DoneValue(true)));
        t.MarkDirty(ReaderTrigger.Unlock);
        t.Tick();
        Assert.Equal(new DoneValue(true), t.Current!.Values["quest:1"]);
        // quest:2 was known not done and is now done, so the merge stamps the instant the flip was observed.
        Assert.Equal(new DoneValue(true, null, Items.Now), t.Current.Values["quest:2"]);
    }

    [Fact]
    public void Edits_from_the_window_change_the_record_bump_the_version_and_save()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        var second = new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" };
        t.Logout();
        t.Login(second);
        Assert.Equal(2, t.All.Count);

        var v = t.Version;
        t.Rename(1, "Aria");
        Assert.Equal("Aria", t.All.Single(r => r.ContentId == 1).Label);
        t.Hide(1, true);
        Assert.True(t.All.Single(r => r.ContentId == 1).Hidden);
        t.Ignore(2, "quest:1", true);
        Assert.Contains("quest:1", t.Current!.Ignored);
        t.Ignore(2, "quest:1", false);
        Assert.DoesNotContain("quest:1", t.Current.Ignored);
        Assert.True(t.Version > v);

        t.Forget(1);
        Assert.Single(t.All);
        Assert.False(_fs.Files.ContainsKey(Path.Combine(Dir, "1.json")));
    }

    [Fact]
    public void An_edit_that_cannot_be_saved_is_kept_and_reported()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        _fs.FailWrites = true;
        t.Rename(1, "Aria");
        Assert.Equal("Aria", t.Current!.Label);
        Assert.Contains(t.Store.Problems, p => p.Contains("not saved"));
    }

    [Fact]
    public void Setting_a_role_moves_that_records_version_alone_and_saves_it()
    {
        // Assigning moves that record's version only, so only its checklist is assembled again, and the record is saved as any
        // window edit is. None is a null, which the file does not write.
        var t = Tracker();
        t.Login(new FakeIdentity());
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });
        var aria = t.VersionOf(1);
        var kestrel = t.VersionOf(2);

        t.SetRole(2, "r1");

        Assert.Equal("r1", t.All.Single(r => r.ContentId == 2).Role);
        Assert.Equal(aria, t.VersionOf(1));
        Assert.True(t.VersionOf(2) > kestrel);
        Assert.Contains("\"role\": \"r1\"", _fs.Files[Path.Combine(Dir, "2.json")]);

        t.SetRole(2, null);
        Assert.Null(t.Current!.Role);
        Assert.DoesNotContain("\"role\"", _fs.Files[Path.Combine(Dir, "2.json")]);
        t.SetRole(99, "r1");                                                         // no such record: nothing happens
        Assert.Equal(2, t.All.Count);
    }

    [Fact]
    public void Ticking_a_task_stores_its_kinds_value_on_any_character_moves_that_records_version_alone_and_unticking_removes_it()
    {
        // A tick is the player's own bookkeeping, for any character, logged in or not, through the Tracker, which moves that
        // record's version alone and saves it, at the Tracker's own now. A second click removes the value. A task that is not
        // valid stores nothing, and a record the Tracker does not have is left alone.
        var t = Tracker();
        t.Login(new FakeIdentity());
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });              // Aria is not logged in
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var mount = settings.AddMyTask(Tasks.Draft("Buy the mount", MyTask.Once))!;
        var craft = settings.AddMyTask(Tasks.Draft("Craft", MyTask.Every, 72))!;
        var aria = t.VersionOf(1);
        var kestrel = t.VersionOf(2);

        t.TickTask(1, chest);
        t.TickTask(1, mount);
        t.TickTask(1, craft);

        var record = t.All.Single(r => r.ContentId == 1);
        Assert.Equal(new DoneValue(true, "daily:2026-09-12", Items.Now), record.Values[chest.Id]);
        Assert.Equal(new DoneValue(true, null, Items.Now), record.Values[mount.Id]);
        Assert.Equal(new TimerValue(Items.Now + TimeSpan.FromHours(72)), record.Values[craft.Id]);
        Assert.True(t.VersionOf(1) > aria);
        Assert.Equal(kestrel, t.VersionOf(2));
        Assert.Contains($"\"{craft.Id}\"", _fs.Files[Path.Combine(Dir, "1.json")]);

        t.UntickTask(1, craft.Id);
        Assert.False(record.Values.ContainsKey(craft.Id));
        Assert.DoesNotContain($"\"{craft.Id}\"", _fs.Files[Path.Combine(Dir, "1.json")]);
        t.TickTask(1, new MyTask { Id = "mine:aaaaaaaaaaaa", Name = "Monthly", Kind = "monthly" });  // not valid: nothing stored
        Assert.False(record.Values.ContainsKey("mine:aaaaaaaaaaaa"));
        t.TickTask(99, chest);                                                            // no such record: nothing happens
        Assert.Equal(2, t.All.Count);
    }

    [Fact]
    public void A_daily_ticked_a_minute_before_the_reset_is_due_again_at_the_reset()
    {
        // A daily ticked at 14:59 UTC belongs to the period that ends at 15:00, so it is due again one minute later, as a game
        // daily done at 14:59 is.
        var t = Tracker();
        t.Login(new FakeIdentity());
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        _clock.UtcNow = new DateTimeOffset(2026, 9, 13, 14, 59, 0, TimeSpan.Zero);

        t.TickTask(1, chest);

        Assert.Equal(new DoneValue(true, "daily:2026-09-12", _clock.UtcNow), t.Current!.Values[chest.Id]);
        Assert.Empty(Assembler.Assemble(catalog, t.Current!, settings, _clock.UtcNow).Today);
        var reset = Assembler.Assemble(catalog, t.Current!, settings, new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal([chest.Id], reset.Today.Select(e => e.Item.Id));
        Assert.Equal(1, reset.Badge);
    }

    [Fact]
    public void An_every_task_clicked_before_it_is_ready_is_unticked_and_the_next_tick_starts_a_fresh_cooldown()
    {
        // A cooling-down "every" task reads done, so its box is ticked, and a click there unticks it: the value goes, and it reads
        // "ready" in Today at once. The next tick starts a whole cooldown from that click, not from the first.
        var t = Tracker();
        t.Login(new FakeIdentity());
        var settings = new Settings();
        var craft = settings.AddMyTask(Tasks.Draft("Craft", MyTask.Every, 24))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        Entry Row() => Assembler.Assemble(catalog, t.Current!, settings, _clock.UtcNow).Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries.Single();

        t.TickTask(1, craft);
        _clock.Advance(TimeSpan.FromHours(10));
        Assert.Equal((EntryState.Done, "ready in 14 h"), (Row().State, Row().Display));

        t.UntickTask(1, craft.Id);                                                        // the click on a ticked box
        Assert.Equal((EntryState.NotDone, "ready"), (Row().State, Row().Display));
        Assert.Contains(craft.Id, Assembler.Assemble(catalog, t.Current!, settings, _clock.UtcNow).Today.Select(e => e.Item.Id));

        t.TickTask(1, craft);                                                              // a new tick: a whole cooldown from now
        Assert.Equal(new TimerValue(Items.Now + TimeSpan.FromHours(34)), t.Current!.Values[craft.Id]);
        Assert.Equal("ready in 1 d", Row().Display);
    }

    [Fact]
    public void A_picked_character_that_was_forgotten_stays_picked_matching_nobody_and_has_the_task_again_when_it_logs_in_again()
    {
        // Forget deletes the record, never the settings: the id stays in the task's list, matching no record, and the settings
        // page shows it as "a character not on this PC". Logging into the character again makes a fresh record with the same
        // content id, which has the task again, not ticked: its tick went with its file.
        var t = Tracker();
        t.Login(new FakeIdentity());
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });
        var settings = new Settings();
        var errand = settings.AddMyTask(Tasks.Draft("Alt errand", MyTask.Once, null, MyTask.ForPicked, 1, 2))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        t.TickTask(1, errand);

        t.Forget(1);

        Assert.Equal([1UL, 2UL], errand.Picked);
        Assert.DoesNotContain(t.All, r => r.ContentId == 1);
        Assert.Equal([2UL], t.All.Where(r => Assembler.Assemble(catalog, r, settings, Items.Now).Today.Any(e => e.Item.Id == errand.Id)).Select(r => r.ContentId));
        t.Logout();
        t.Login(new FakeIdentity());                                                       // Aria again: a fresh record
        Assert.Equal([errand.Id], Assembler.Assemble(catalog, t.Current!, settings, Items.Now).Today.Select(e => e.Item.Id));
    }

    [Fact]
    public void Forgetting_the_current_character_ends_tracking_and_drops_the_file()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        var versionBeforeForget = t.VersionOf(1);
        t.Forget(1);
        Assert.Null(t.Current);
        Assert.Empty(t.All);
        Assert.True(t.VersionOf(1) > versionBeforeForget);
    }

    [Fact]
    public void A_record_from_a_newer_build_is_not_tracked()
    {
        _fs.Files[Path.Combine(Dir, "1.json")] = "{\"schemaVersion\":99,\"contentId\":1}";
        var t = Tracker();
        t.Login(new FakeIdentity());
        Assert.Null(t.Current);
        Assert.Empty(t.All);
        Assert.Contains(_log, l => l.Contains("newer build"));
    }

    [Fact]
    public void An_import_adds_and_replaces_records_and_saves_them_but_never_the_logged_in_character()
    {
        // The apply step goes through the Tracker, which owns the list, the versions and the pending saves. The import plan
        // already keeps the logged-in character and a newer build's file; the Tracker refuses both again, because a switch can
        // fall between planning and the click.
        var t = Tracker();
        t.Login(new FakeIdentity());                                                      // Aria, content id 1, logged in
        var versionBefore = t.VersionOf(2);

        Assert.Equal(1, t.Import([Items.Record(2, "Kestrel Vane"), Items.Record(1, "Aria From The File")]));

        Assert.Equal(["Aria Whitewind", "Kestrel Vane"], t.All.Select(r => r.Label));    // sorted; the logged-in record untouched
        Assert.Same(t.Current, t.All.Single(r => r.ContentId == 1));
        Assert.True(t.VersionOf(2) > versionBefore);
        Assert.Contains("Kestrel Vane", _fs.Files[Path.Combine(Dir, "2.json")]);

        Assert.Equal(1, t.Import([Items.Record(2, "Kestrel Renamed")]));                // a replace: one record for the id, the new one
        Assert.Equal("Kestrel Renamed", t.All.Single(r => r.ContentId == 2).Name);
        Assert.Equal(2, t.All.Count);

        const string Future = "{\"schemaVersion\":99,\"contentId\":7}";
        _fs.Files[Path.Combine(Dir, "7.json")] = Future;
        Assert.Equal(0, t.Import([Items.Record(7, "Future Self")]));                     // a newer build's file is never overwritten
        Assert.Equal(Future, _fs.Files[Path.Combine(Dir, "7.json")]);
    }

    [Fact]
    public void Logout_without_a_character_changes_nothing()
    {
        var t = Tracker();
        var v = t.Version;
        t.Logout();
        Assert.Equal(v, t.Version);
        Assert.Empty(_fs.Files);
    }

    [Fact]
    public void Characters_stay_sorted_by_label()
    {
        var t = Tracker();
        t.Login(new FakeIdentity { ContentId = 1, Name = "Zed Zero" });
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Aria Whitewind" });
        Assert.Equal(["Aria Whitewind", "Zed Zero"], t.All.Select(r => r.Label));
        t.Rename(1, "Aaa");
        Assert.Equal(["Aaa", "Aria Whitewind"], t.All.Select(r => r.Label));
    }

    [Fact]
    public void A_throw_in_one_reader_disables_only_that_reader()
    {
        var good = new FakeReader(Category.Story, ReaderTrigger.Login | ReaderTrigger.Interval) { Name = "good" };
        good.Next.Add(new Reading("quest:1", new DoneValue(true)));
        var bad = new FakeReader(Category.Story, ReaderTrigger.Login | ReaderTrigger.Interval) { Name = "bad", Throw = true };
        var t = Tracker(good, bad);

        t.Login(new FakeIdentity());
        Assert.Equal(new DoneValue(true), t.Current!.Values["quest:1"]);
        Assert.Single(t.Disabled);
        Assert.Equal("bad", t.Disabled.Single().Name);
        Assert.Contains("bad", t.DisabledReason(Category.Story)!);
        Assert.DoesNotContain("good", t.DisabledReason(Category.Story)!);
        Assert.Null(t.DisabledReason(Category.Money));

        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(2, good.Calls);
        Assert.Equal(1, bad.Calls);
    }

    [Fact]
    public void A_failed_edit_on_another_record_is_retried_on_the_next_tick()
    {
        var t = Tracker();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });
        t.Logout();
        t.Login(new FakeIdentity());
        _fs.FailWrites = true;
        t.Rename(2, "Kes");
        Assert.Equal("Kes", t.All.Single(r => r.ContentId == 2).Label);
        _fs.Files.Remove(Path.Combine(Dir, "2.json"));

        _fs.FailWrites = false;
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Contains("\"displayName\": \"Kes\"", _fs.Files[Path.Combine(Dir, "2.json")]);
    }

    [Fact]
    public void A_permanently_failing_save_reports_and_logs_once_then_clears_on_success()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        _fs.FailWrites = true;
        t.Rename(1, "Aria");

        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Single(t.Store.Problems, p => p.Contains("not saved"));   // two more ticks, still the one report
        Assert.Single(_log, l => l.Contains("not saved"));               // ... and the one log line

        _fs.FailWrites = false;
        var writes = _fs.Writes;
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);                             // the retried save lands exactly once

        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);                             // the mark is cleared: nothing left to write
    }

    [Fact]
    public void Records_load_sorted_by_label()
    {
        var first = Tracker();
        first.Login(new FakeIdentity { ContentId = 1, Name = "Zed Zero" });
        first.Logout();
        first.Login(new FakeIdentity { ContentId = 2, Name = "Aria Whitewind" });
        first.Rename(1, "Aaa");

        Assert.Equal(["Aaa", "Aria Whitewind"], Tracker().All.Select(r => r.Label));
    }

    [Fact]
    public void A_reader_that_did_not_read_leaves_the_category_stamp_alone()
    {
        var silent = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval) { ReturnsNull = true };
        var t = Tracker(silent);
        t.Login(new FakeIdentity());
        Assert.Equal(1, silent.Calls);
        Assert.False(t.Current!.CategoryReadAt.ContainsKey("Money"));

        silent.ReturnsNull = false;
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(_clock.UtcNow, t.Current.CategoryReadAt["Money"]);   // read nothing still stamps
    }

    [Fact]
    public void A_reminder_is_recorded_once_per_occasion_and_survives_to_disk()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        var file = Path.Combine(Dir, "1.json");

        t.Remind("daily-reset", "daily:2026-09-13");
        var version = t.VersionOf(1);
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Contains("\"daily:2026-09-13\"", _fs.Files[file]);

        // The same occasion again says nothing new, so nothing moves and nothing is written.
        var writes = _fs.Writes;
        t.Remind("daily-reset", "daily:2026-09-13");
        Assert.Equal(version, t.VersionOf(1));
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes, _fs.Writes);

        // A new occasion is a new thing to stay quiet about: the version moves and the record reaches disk.
        t.Remind("daily-reset", "daily:2026-09-14");
        Assert.NotEqual(version, t.VersionOf(1));
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);
        Assert.Contains("\"daily:2026-09-14\"", _fs.Files[file]);
    }

    [Fact]
    public void An_observation_changes_the_current_record_and_saves_on_the_next_tick()
    {
        var t = Tracker();
        t.Login(new FakeIdentity());
        var writes = _fs.Writes;
        var version = t.VersionOf(1);
        var mini = new Observation(ObservationKind.OneMore, 1, 3, ResetSchedules.Daily);

        t.Observe("reset:cactpot:mini", mini);
        Assert.Equal(new CounterValue(1, 3, _clock.UtcNow), t.Current!.Values["reset:cactpot:mini"]);
        Assert.Equal(version + 1, t.VersionOf(1));
        Assert.Equal(writes, _fs.Writes);          // the save waits for the tick, like every merge's
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);
        Assert.Contains("\"kind\": \"counter\"", _fs.Files[Path.Combine(Dir, "1.json")]);

        // The jumbo sentinel resolves against the record's data centre (Crystal); the same floor twice is not a change.
        var jumbo = new Observation(ObservationKind.AtLeast, 2, 3, ResetSchedules.Jumbo);
        t.Observe("reset:cactpot:jumbo", jumbo);
        var after = t.VersionOf(1);
        t.Observe("reset:cactpot:jumbo", jumbo);
        Assert.Equal(after, t.VersionOf(1));
        Assert.Equal(2, ((CounterValue)t.Current.Values["reset:cactpot:jumbo"]).Current);

        t.Logout();
        t.Observe("reset:cactpot:mini", mini);   // nobody is logged in: nothing happens
        Assert.Null(t.Current);
    }

    [Fact]
    public void The_first_observation_of_a_new_period_is_stored_even_when_the_count_repeats()
    {
        // One ticket yesterday afternoon, one ticket today: both counts are 1, but the second is a new period's, and a guard
        // that compared counts alone would keep yesterday's instant and let the assembler zero today's ticket.
        var t = Tracker();
        _clock.UtcNow = new DateTimeOffset(2026, 9, 12, 14, 0, 0, TimeSpan.Zero);   // Saturday, an hour before the 15:00 UTC reset
        t.Login(new FakeIdentity());
        var mini = new Observation(ObservationKind.OneMore, 1, 3, ResetSchedules.Daily);
        t.Observe("reset:cactpot:mini", mini);
        var version = t.VersionOf(1);

        _clock.UtcNow = new DateTimeOffset(2026, 9, 12, 15, 30, 0, TimeSpan.Zero);
        t.Observe("reset:cactpot:mini", mini);
        var stored = (CounterValue)t.Current!.Values["reset:cactpot:mini"];
        Assert.Equal(1, stored.Current);
        Assert.Equal(_clock.UtcNow, stored.ReadAt);      // today's instant, so the count is today's
        Assert.Equal(version + 1, t.VersionOf(1));

        // The same ticket seen again in the same period is still no change.
        _clock.UtcNow = new DateTimeOffset(2026, 9, 12, 15, 31, 0, TimeSpan.Zero);
        t.Observe("reset:cactpot:mini", new Observation(ObservationKind.AtLeast, 1, 3, ResetSchedules.Daily));
        Assert.Equal(version + 1, t.VersionOf(1));
    }

    [Fact]
    public void Fire_runs_the_window_readers_on_this_frame_and_saves_on_the_next_tick()
    {
        // An edge-gated row is readable only while its window is open: the Post handler fires the Window readers now rather
        // than marking dirty, and the save waits for the tick like every merge's.
        var window = new FakeReader(Category.Resets, ReaderTrigger.Window) { Name = "window" };
        window.Next.Add(new Reading("reset:gc:supply", new DoneValue(true, "grand-company:2026-09-12")));
        var interval = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval) { Name = "interval" };
        var t = Tracker(window, interval);

        t.Fire(ReaderTrigger.Window);              // nobody logged in: nothing runs
        Assert.Equal(0, window.Calls);

        t.Login(new FakeIdentity());
        Assert.Equal(0, window.Calls);             // Login is not one of its triggers
        var writes = _fs.Writes;
        var version = t.VersionOf(1);

        t.Fire(ReaderTrigger.Window);
        Assert.Equal(1, window.Calls);
        Assert.Equal(1, interval.Calls);           // only its Login run
        Assert.Equal(new DoneValue(true, "grand-company:2026-09-12"), t.Current!.Values["reset:gc:supply"]);
        Assert.Equal(version + 1, t.VersionOf(1));
        Assert.Equal(writes, _fs.Writes);

        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);
        Assert.Equal(1, window.Calls);             // Interval is not one of its triggers either
        Assert.Equal(2, interval.Calls);
    }

    [Fact]
    public void A_reader_is_told_which_of_its_triggers_fired()
    {
        // A reader whose full read is costly reads in full on the tick only when a fingerprint moved, so it has to know whether a
        // login, an unlock or only the tick asked. The Tracker passes the reader's own triggers that fired.
        var reader = new FakeReader(Category.Collections, ReaderTrigger.Login | ReaderTrigger.Unlock | ReaderTrigger.Interval);
        var t = Tracker(reader);
        t.Login(new FakeIdentity());
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        t.MarkDirty(ReaderTrigger.Unlock | ReaderTrigger.LevelOrJob);   // LevelOrJob is not one of its triggers
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        t.Fire(ReaderTrigger.Window);                                   // not one of its triggers: not run at all
        t.Fire(ReaderTrigger.Unlock);
        Assert.Equal([ReaderTrigger.Login, ReaderTrigger.Interval, ReaderTrigger.Interval | ReaderTrigger.Unlock, ReaderTrigger.Unlock], reader.Fired);
        Assert.Equal(4, reader.Calls);
    }

    [Fact]
    public void A_new_character_gets_the_role_given_for_new_ones_and_a_known_character_keeps_its_own()
    {
        // The default role: a character seen for the first time gets it; a character already known is never changed by it.
        var t = Tracker();
        t.Login(new FakeIdentity(), "r1");
        Assert.Equal("r1", t.Current!.Role);
        t.SetRole(1, "r2");
        t.Logout();
        t.Login(new FakeIdentity(), "r1");                                               // known now: its own role stays
        Assert.Equal("r2", t.Current!.Role);
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });              // no default given: no role
        Assert.Null(t.Current!.Role);
    }

    [Fact]
    public void A_bulk_assignment_gives_every_named_character_the_role_and_moves_only_their_versions()
    {
        // Bulk role: the ticked characters of All characters, in one call. A content id the Tracker does not have is skipped;
        // null clears the role.
        var t = Tracker();
        t.Login(new FakeIdentity());
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 3, Name = "Momo Lulu" });
        var momo = t.VersionOf(3);

        t.SetRoles([1, 2, 99], "r1");

        Assert.Equal("r1", t.All.Single(r => r.ContentId == 1).Role);
        Assert.Equal("r1", t.All.Single(r => r.ContentId == 2).Role);
        Assert.Null(t.All.Single(r => r.ContentId == 3).Role);
        Assert.Equal(momo, t.VersionOf(3));
        Assert.Equal(3, t.All.Count);

        t.SetRoles([1, 2], null);
        Assert.All(t.All, r => Assert.Null(r.Role));
    }

    [Fact]
    public void Counting_a_task_adds_and_takes_back_on_any_character_and_moves_that_records_version_alone()
    {
        // A click on a counted task's box, for any character, logged in or not, through the Tracker, which stores the new count at
        // its own now, moves that record's version alone and saves it. UntickTask is "Reset to 0". A plain task, and a record the
        // Tracker does not have, change nothing.
        var t = Tracker();
        t.Login(new FakeIdentity());
        t.Logout();
        t.Login(new FakeIdentity { ContentId = 2, Name = "Kestrel Vane" });              // Aria is not logged in
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 3))!;
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var aria = t.VersionOf(1);
        var kestrel = t.VersionOf(2);
        var record = t.All.Single(r => r.ContentId == 1);

        t.CountTask(1, deliver, 1);
        t.CountTask(1, deliver, 1);
        Assert.Equal(new CounterValue(2, 3, Items.Now), record.Values[deliver.Id]);
        Assert.True(t.VersionOf(1) > aria);
        Assert.Equal(kestrel, t.VersionOf(2));
        Assert.Contains($"\"{deliver.Id}\"", _fs.Files[Path.Combine(Dir, "1.json")]);

        t.CountTask(1, deliver, 1);
        t.CountTask(1, deliver, 1);                                                      // past the number: stays at 3
        Assert.Equal(new CounterValue(3, 3, Items.Now), record.Values[deliver.Id]);
        t.CountTask(1, deliver, -1);
        Assert.Equal(new CounterValue(2, 3, Items.Now), record.Values[deliver.Id]);

        t.UntickTask(1, deliver.Id);                                                     // Reset to 0
        Assert.False(record.Values.ContainsKey(deliver.Id));

        t.CountTask(1, chest, 1);                                                        // not counted: nothing stored
        Assert.False(record.Values.ContainsKey(chest.Id));
        t.CountTask(99, deliver, 1);                                                     // no such record: nothing happens
        Assert.Equal(2, t.All.Count);
    }

    [Fact]
    public void A_click_at_14_59_counts_for_the_ending_day_and_one_at_15_00_starts_the_new_day_at_1()
    {
        // The count belongs to the period it is clicked in, as a plain tick does.
        var t = Tracker();
        t.Login(new FakeIdentity());
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 3))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        _clock.UtcNow = new DateTimeOffset(2026, 9, 13, 14, 59, 0, TimeSpan.Zero);

        t.CountTask(1, deliver, 1);
        t.CountTask(1, deliver, 1);
        Assert.Equal("2 of 3", Assembler.Assemble(catalog, t.Current!, settings, _clock.UtcNow).Today.Single().Display);

        _clock.UtcNow = new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal("0 of 3", Assembler.Assemble(catalog, t.Current!, settings, _clock.UtcNow).Today.Single().Display);
        t.CountTask(1, deliver, 1);
        Assert.Equal(new CounterValue(1, 3, _clock.UtcNow), t.Current!.Values[deliver.Id]);
    }

    [Fact]
    public void A_read_that_changes_the_record_sets_todays_gil_point_and_saves_it_with_that_change()
    {
        // The login read stores the gil and today's point in one save. A tick that changes nothing writes no point and no file.
        // The retainers' gil, once read, joins today's point in that tick's one write. Past midnight UTC a tick that changes
        // nothing leaves the new day to carry the last amount; the first change starts its point, and the last day keeps its own.
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval);
        money.Next.Add(new Reading("currency:1", new CounterValue(100, 999_999_999, _clock.UtcNow)));
        var t = Tracker(money);
        var file = Path.Combine(Dir, "1.json");

        t.Login(new FakeIdentity());
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 100 }, t.Current!.GilHistory);
        Assert.Contains("\"2026-09-13\": 100", _fs.Files[file]);

        var writes = _fs.Writes;
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes, _fs.Writes);

        money.Next.Add(new Reading(MoneyIds.RetainerId(0, MoneyIds.RetainerGil), new CounterValue(50, 999_999_999, _clock.UtcNow)));
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(writes + 1, _fs.Writes);
        Assert.Contains("\"2026-09-13\": 150", _fs.Files[file]);

        _clock.UtcNow = new DateTimeOffset(2026, 9, 14, 0, 0, 5, TimeSpan.Zero);
        t.Tick();
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 150 }, t.Current.GilHistory);
        money.Next[0] = new Reading("currency:1", new CounterValue(90, 999_999_999, _clock.UtcNow));
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 150, ["2026-09-14"] = 140 }, t.Current.GilHistory);
        Assert.Equal(writes + 2, _fs.Writes);
    }

    [Fact]
    public void A_hidden_character_that_is_played_still_gets_its_gil_points()
    {
        // Hiding takes a character out of the list, not out of its history: its reads while played still set its points, it is in
        // no total while hidden, and unhiding it brings its whole line back.
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval);
        money.Next.Add(new Reading("currency:1", new CounterValue(100, 999_999_999, _clock.UtcNow)));
        var t = Tracker(money);
        t.Login(new FakeIdentity());
        t.Hide(1, true);

        money.Next[0] = new Reading("currency:1", new CounterValue(250, 999_999_999, _clock.UtcNow));
        _clock.UtcNow = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        t.Tick();

        Assert.True(t.Current!.Hidden);
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 100, ["2026-09-15"] = 250 }, t.Current.GilHistory);
        Assert.Null(GilPoints.TotalOn(t.All, new DateOnly(2026, 9, 15)));
        t.Hide(1, false);
        Assert.Equal(250L, GilPoints.TotalOn(t.All, new DateOnly(2026, 9, 15)));
        Assert.Equal(100L, GilPoints.TotalOn(t.All, new DateOnly(2026, 9, 14)));
    }

    [Fact]
    public void Loading_seeds_each_record_with_own_gil_and_no_history_in_memory_and_writes_no_file()
    {
        // A record stored with gil and no history is on the graph from the first load: one point, its own gil and retainers',
        // dated its last-seen day. Nothing is written at load, and the files stay as they were until the record's next save,
        // which carries the seed. A record with history keeps it; one with no gil stored gets none.
        var store = new RecordStore(_fs, Dir);
        var aria = Items.Record();
        aria.LastSeen = new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero);
        TotalsTests.Gil(aria, 1_000_000, 200_000);
        var kestrel = Items.Record(2, "Kestrel Vane");
        TotalsTests.Gil(kestrel, 8_500);
        kestrel.GilHistory = new() { ["2026-09-01"] = 7_000 };
        var momo = Items.Record(3, "Momo Lulu");                                           // Money never read
        foreach (var record in new[] { aria, kestrel, momo }) store.Save(record);
        var files = _fs.Files.ToDictionary(f => f.Key, f => f.Value);
        var writes = _fs.Writes;

        var t = Tracker();

        Assert.Equal(new Dictionary<string, long> { ["2026-09-10"] = 1_200_000 }, t.All.Single(r => r.ContentId == 1).GilHistory);
        Assert.Equal(new Dictionary<string, long> { ["2026-09-01"] = 7_000 }, t.All.Single(r => r.ContentId == 2).GilHistory);
        Assert.Null(t.All.Single(r => r.ContentId == 3).GilHistory);
        Assert.Equal(writes, _fs.Writes);
        Assert.Equal(files, _fs.Files);

        t.Login(new FakeIdentity());                                                       // Aria's next save carries the seed
        Assert.Contains("\"2026-09-10\": 1200000", _fs.Files[Path.Combine(Dir, "1.json")]);
    }

    [Fact]
    public void An_import_seeds_a_record_with_no_gil_history_and_keeps_an_imported_history_whole()
    {
        // An import replaces a record whole, history included, as it does today; a record the file brings with gil and no
        // history is seeded before its save, so it is on the graph at once too.
        var t = Tracker();
        var kestrel = Items.Record(2, "Kestrel Vane");
        TotalsTests.Gil(kestrel, 8_500);
        var momo = Items.Record(3, "Momo Lulu");
        TotalsTests.Gil(momo, 5_000);
        momo.GilHistory = new() { ["2026-08-01"] = 4_000, ["2026-09-01"] = 4_500 };

        Assert.Equal(2, t.Import([kestrel, momo]));

        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 8_500 }, t.All.Single(r => r.ContentId == 2).GilHistory);
        Assert.Equal(new Dictionary<string, long> { ["2026-08-01"] = 4_000, ["2026-09-01"] = 4_500 }, t.All.Single(r => r.ContentId == 3).GilHistory);
        Assert.Contains("\"2026-09-13\": 8500", _fs.Files[Path.Combine(Dir, "2.json")]);
    }

    [Fact]
    public void A_changed_record_on_a_new_day_writes_that_days_gil_point_even_with_the_same_gil()
    {
        // A tick on the 14th that changes nothing writes no point; a quest read later that day changes the record, and though the
        // gil is what it was, the 14th has no point, so that change writes one, at the same amount. It changes nothing drawn.
        var money = new FakeReader(Category.Money, ReaderTrigger.Login | ReaderTrigger.Interval);
        money.Next.Add(new Reading("currency:1", new CounterValue(100, 999_999_999, _clock.UtcNow)));
        var story = new FakeReader(Category.Story, ReaderTrigger.Login | ReaderTrigger.Unlock);
        var t = Tracker(money, story);
        t.Login(new FakeIdentity());

        _clock.UtcNow = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        t.Tick();
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 100 }, t.Current!.GilHistory);

        story.Next.Add(new Reading("quest:1", new DoneValue(true)));
        t.MarkDirty(ReaderTrigger.Unlock);
        t.Tick();
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 100, ["2026-09-14"] = 100 }, t.Current.GilHistory);
    }

    [Fact]
    public void A_character_whose_record_cannot_be_read_is_not_tracked_and_its_file_is_kept()
    {
        new RecordStore(_fs, Dir).Save(Items.Record(1));
        var before = _fs.Files[Path.Combine(Dir, "1.json")];
        _fs.Unreadable.Add(Path.Combine(Dir, "1.json"));
        var t = Tracker();
        t.Login(new FakeIdentity());
        _clock.Advance(TimeSpan.FromSeconds(5));
        t.Tick();
        Assert.Null(t.Current);
        Assert.Empty(t.All);
        Assert.Equal(before, _fs.Files[Path.Combine(Dir, "1.json")]);
        Assert.Contains(_log, l => l.Contains("could not be read"));
    }

    [Fact]
    public void A_fault_is_told_through_its_own_line_and_names_the_file_alone()
    {
        var faults = new List<string>();
        var money = new FakeReader(Category.Money, ReaderTrigger.Login) { Throw = true };
        var t = new Tracker(_clock, new RecordStore(_fs, Dir), [money], "2026.09.01.0000.0000", _log.Add, faults.Add);
        t.Login(new FakeIdentity());
        _fs.FailWrites = true;
        t.Rename(1, "Aria");
        Assert.Equal(2, faults.Count);
        Assert.Contains("disabled", faults[0]);
        Assert.StartsWith("1.json: not saved (disk full)", faults[1]);
        Assert.DoesNotContain(_log, l => l.Contains("disabled") || l.Contains("not saved"));
    }
}
