using System.Text.Json.Nodes;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

public class RecordStoreTests
{
    private const string Dir = @"C:\cfg\Mnemosyne";
    private readonly InMemoryFileSystem _fs = new();
    private RecordStore Store() => new(_fs, Dir);

    [Fact]
    public void Save_then_load_round_trips_every_value_kind_and_names_the_file_by_content_id()
    {
        var r = SummariserTests.Aria();
        r.Ignored.Add("quest:2");
        r.Observed["reset:cactpot"] = new ObservedValue(2, Items.Now, "daily:2026-09-12");
        r.HousingLastEntered = Items.Now;
        Store().Save(r);

        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "1.json")));
        Assert.DoesNotContain(_fs.Files.Keys, k => k.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Contains("\"schemaVersion\"", _fs.Files[Path.Combine(Dir, "1.json")]);
        Assert.DoesNotContain("\"nameAtWorld\"", _fs.Files[Path.Combine(Dir, "1.json")]);

        var back = Store().LoadAll().Single();
        Assert.Equal(1UL, back.ContentId);
        Assert.Equal("Aria Whitewind", back.Name);
        Assert.Equal(new DoneValue(true, "daily:2026-09-12"), back.Values["reset:roulette:1"]);
        Assert.Equal(new CounterValue(4, 12, Items.Now), back.Values["reset:tribal"]);
        Assert.Equal(new LevelValue(72, 100), back.Values["job:1"]);
        Assert.Equal(new TimerValue(Items.Now + TimeSpan.FromHours(18)), back.Values["timer:venture:1"]);
        Assert.Contains("quest:2", back.Ignored);
        Assert.Equal(2, back.Observed["reset:cactpot"].Count);
        Assert.Equal(Items.Now, back.HousingLastEntered);
        Assert.Equal(Items.Now, back.CategoryReadAt["Story"]);
    }

    [Fact]
    public void Load_all_skips_settings_and_unrelated_files()
    {
        Store().Save(Items.Record(5));
        Store().SaveSettings(new Settings());
        _fs.Files[Path.Combine(Dir, "notes.json")] = "{}";
        var all = Store().LoadAll();
        Assert.Equal([5UL], all.Select(r => r.ContentId));
    }

    [Fact]
    public void A_corrupt_file_is_quarantined_and_reported_and_the_others_still_load()
    {
        Store().Save(Items.Record(5));
        _fs.Files[Path.Combine(Dir, "6.json")] = "{ this is not json";
        var store = Store();
        var all = store.LoadAll();
        Assert.Equal([5UL], all.Select(r => r.ContentId));
        Assert.False(_fs.Files.ContainsKey(Path.Combine(Dir, "6.json")));
        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "6.json.corrupt")));
        Assert.Equal("{ this is not json", _fs.Files[Path.Combine(Dir, "6.json.corrupt")]);
        Assert.Single(store.Problems);
        Assert.Contains("6.json", store.Problems[0]);
    }

    [Fact]
    public void A_non_integer_gil_history_value_quarantines_the_record_and_the_others_still_load_with_their_points()
    {
        // A malformed gilHistory value is as corrupt as any other field.
        var good = Items.Record(5);
        good.GilHistory = new() { ["2026-09-12"] = 1000 };
        Store().Save(good);
        _fs.Files[Path.Combine(Dir, "6.json")] = "{\"schemaVersion\":2,\"contentId\":6,\"name\":\"Bad\",\"gilHistory\":{\"2026-09-12\":\"lots\"}}";
        var store = Store();
        var all = store.LoadAll();
        Assert.Equal([5UL], all.Select(r => r.ContentId));
        Assert.Equal(1000, all.Single().GilHistory!["2026-09-12"]);
        Assert.False(_fs.Files.ContainsKey(Path.Combine(Dir, "6.json")));
        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "6.json.corrupt")));
        Assert.Single(store.Problems);
        Assert.Contains("6.json", store.Problems[0]);
    }

    [Fact]
    public void A_file_from_a_newer_build_is_left_untouched_and_reported()
    {
        var text = "{\"schemaVersion\":99,\"contentId\":7,\"name\":\"Future\"}";
        _fs.Files[Path.Combine(Dir, "7.json")] = text;
        var store = Store();
        Assert.Null(store.Load(7));
        Assert.Equal(text, _fs.Files[Path.Combine(Dir, "7.json")]);
        Assert.Contains("newer", store.Problems.Single());
    }

    [Fact]
    public void A_file_without_a_schema_version_is_treated_as_version_one()
    {
        _fs.Files[Path.Combine(Dir, "8.json")] = "{\"contentId\":8,\"name\":\"Old\",\"homeWorld\":\"Balmung\"}";
        var r = Store().Load(8);
        Assert.NotNull(r);
        Assert.Equal(CharacterRecord.CurrentSchemaVersion, r!.SchemaVersion);
        Assert.Equal("Old", r.Name);
    }

    [Fact]
    public void Unknown_keys_survive_a_load_and_a_save()
    {
        _fs.Files[Path.Combine(Dir, "9.json")] = "{\"schemaVersion\":1,\"contentId\":9,\"name\":\"Keeps\",\"futureKey\":{\"x\":5}}";
        var store = Store();
        var r = store.Load(9)!;
        store.Save(r);
        var node = JsonNode.Parse(_fs.Files[Path.Combine(Dir, "9.json")])!.AsObject();
        Assert.Equal(5, node["futureKey"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void An_unreadable_file_is_reported_and_not_quarantined()
    {
        Store().Save(Items.Record(5));
        _fs.Unreadable.Add(Path.Combine(Dir, "5.json"));
        var store = Store();
        Assert.Null(store.Load(5));
        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "5.json")));
        Assert.Contains("unreadable", store.Problems.Single());
    }

    [Fact]
    public void Delete_removes_the_file_and_tolerates_a_missing_one()
    {
        Store().Save(Items.Record(5));
        var store = Store();
        store.Delete(5);
        store.Delete(5);
        Assert.False(_fs.Files.ContainsKey(Path.Combine(Dir, "5.json")));
    }

    [Fact]
    public void Delete_refuses_a_record_that_could_not_be_read_and_reports_it_but_still_deletes_a_normal_record()
    {
        Store().Save(Items.Record(5));
        _fs.Unreadable.Add(Path.Combine(Dir, "5.json"));
        Store().Save(Items.Record(6));
        var store = Store();

        store.Delete(5);
        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "5.json")));
        Assert.Contains(store.Problems, p => p.Contains("not deleted"));

        store.Delete(6);
        Assert.False(_fs.Files.ContainsKey(Path.Combine(Dir, "6.json")));
    }

    [Fact]
    public void Settings_default_when_missing_round_trip_and_quarantine_when_corrupt()
    {
        var store = Store();
        var s = store.LoadSettings();
        Assert.Equal(7, s.StalenessDays);

        s.StalenessDays = 10;
        s.CategoriesOff.Add("Money");
        store.SaveSettings(s);
        var back = Store().LoadSettings();
        Assert.Equal(10, back.StalenessDays);
        Assert.Contains("Money", back.CategoriesOff);

        _fs.Files[Path.Combine(Dir, "settings.json")] = "nope";
        store = Store();
        Assert.Equal(7, store.LoadSettings().StalenessDays);
        Assert.True(_fs.Files.ContainsKey(Path.Combine(Dir, "settings.json.corrupt")));
        Assert.Single(store.Problems);
    }

    [Fact]
    public void Newer_settings_run_on_defaults_and_are_never_overwritten()
    {
        var text = "{\"schemaVersion\":99,\"stalenessDays\":42}";
        _fs.Files[Path.Combine(Dir, "settings.json")] = text;
        var store = Store();
        var s = store.LoadSettings();
        Assert.Equal(7, s.StalenessDays);
        Assert.True(store.SettingsReadOnly);
        s.StalenessDays = 1;
        store.SaveSettings(s);
        store.SaveSettings(s);
        Assert.Equal(text, _fs.Files[Path.Combine(Dir, "settings.json")]);
        Assert.Equal(2, store.Problems.Count);
    }

    [Fact]
    public void Settings_stay_at_schema_1_so_a_tasks_map_round_trips_through_a_save_and_a_version_2_file_is_left_untouched()
    {
        // The "tasks" map needs no schema step. A missing map reads as empty; a map that is there loads and saves back whole, at schema
        // 1, which is what lets a rollback to an older build carry it through Settings.Extra. A file from a build newer than this one
        // is left untouched and runs on defaults, as any newer file is.
        var path = Path.Combine(Dir, "settings.json");
        _fs.Files[path] = "{\"schemaVersion\":1,\"stalenessDays\":10,\"tasks\":{\"reset:roulette:1\":{\"pinned\":true}}}";
        var store = Store();
        var s = store.LoadSettings();
        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal(10, s.StalenessDays);
        Assert.True(s.Tasks.Get("reset:roulette:1").Pinned);
        store.SaveSettings(s);
        Assert.Contains("\"schemaVersion\": 1", _fs.Files[path]);
        Assert.Contains("\"pinned\": true", _fs.Files[path]);

        var newer = "{\"schemaVersion\":2,\"tasks\":{\"reset:roulette:1\":{\"pinned\":true}}}";
        _fs.Files[path] = newer;
        var readOnly = Store();
        Assert.Empty(readOnly.LoadSettings().Tasks);
        Assert.True(readOnly.SettingsReadOnly);
        readOnly.SaveSettings(new Settings());
        Assert.Equal(newer, _fs.Files[path]);
    }

    [Fact]
    public void An_unknown_settings_key_survives_a_load_and_a_save_the_mechanism_a_rollback_relies_on()
    {
        // Staying at schema 1 relies on Settings.Extra carrying a field this build does not know through a load and a save untouched,
        // proven generically here since this build already knows "tasks" itself. The same mechanism as CharacterRecord's own
        // Unknown_keys_survive_a_load_and_a_save.
        var path = Path.Combine(Dir, "settings.json");
        _fs.Files[path] = "{\"schemaVersion\":1,\"stalenessDays\":10,\"futureField\":{\"x\":5}}";
        var store = Store();
        store.SaveSettings(store.LoadSettings());
        var node = JsonNode.Parse(_fs.Files[path])!.AsObject();
        Assert.Equal(5, node["futureField"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void A_null_tasks_or_a_null_entry_reads_as_no_adjustments_and_a_tasks_value_that_cannot_be_read_quarantines_the_file()
    {
        // A hand-edited file. A null map or a null entry reads as the defaults, and the rest of the settings still load. A value the
        // reader cannot take makes the file malformed: it is moved aside and reported like any other, never thrown.
        var path = Path.Combine(Dir, "settings.json");
        _fs.Files[path] = "{\"schemaVersion\":1,\"stalenessDays\":10,\"tasks\":null}";
        var s = Store().LoadSettings();
        Assert.Equal(10, s.StalenessDays);
        Assert.Empty(s.Tasks);
        s.Tasks.Pin("reset:roulette:1");                                        // a live map that can be adjusted, not a null
        Assert.True(s.Tasks.Get("reset:roulette:1").Pinned);

        _fs.Files[path] = "{\"schemaVersion\":1,\"tasks\":{\"reset:roulette:1\":null,\"reset:tails\":{\"pinned\":true}}}";
        var entries = Store().LoadSettings().Tasks;
        Assert.Equal(TaskPreference.Default, entries.Get("reset:roulette:1"));
        Assert.True(entries.Get("reset:tails").Pinned);

        foreach (var bad in new[]
                 {
                     "{\"schemaVersion\":1,\"tasks\":5}",
                     "{\"schemaVersion\":1,\"tasks\":{\"reset:tails\":{\"priority\":\"urgent\"}}}",
                     "{\"schemaVersion\":1,\"tasks\":{\"reset:tails\":{\"snoozedUntil\":\"soon\"}}}",
                 })
        {
            _fs.Files.Clear();
            _fs.Files[path] = bad;
            var store = Store();
            Assert.Empty(store.LoadSettings().Tasks);
            Assert.Equal(bad, _fs.Files[path + ".corrupt"]);
            Assert.Single(store.Problems);
        }
    }

    [Fact]
    public void Roles_and_a_records_role_need_no_schema_step_and_round_trip_at_settings_1_and_records_2()
    {
        // No schema bump. The settings stay at 1 and the records at 2; "roles" and "role" load and save back whole at those versions; a
        // key a role does not know is carried as the settings' and the record's own are; a dangling role id is kept; a record with no
        // role writes no "role" at all. A build that does not know either field carries it through its [JsonExtensionData], which
        // Unknown_keys_survive_a_load_and_a_save and An_unknown_settings_key_survives_a_load_and_a_save_the_mechanism_a_rollback_relies_on pin.
        Assert.Equal(1, Settings.CurrentSchemaVersion);
        Assert.Equal(2, CharacterRecord.CurrentSchemaVersion);
        var settingsPath = Path.Combine(Dir, "settings.json");
        _fs.Files[settingsPath] = "{\"schemaVersion\":1,\"roles\":[{\"id\":\"r1\",\"name\":\"Mule\",\"off\":[\"category:Events\",\"group:Resets/Daily\"],\"colour\":\"grey\"}]}";
        var store = Store();
        var settings = store.LoadSettings();
        var mule = Assert.Single(settings.Roles);
        Assert.Equal(("r1", "Mule"), (mule.Id, mule.Name));
        Assert.Equal(["category:Events", "group:Resets/Daily"], mule.Off.Order(StringComparer.Ordinal));
        store.SaveSettings(settings);
        var saved = JsonNode.Parse(_fs.Files[settingsPath])!.AsObject();
        Assert.Equal(1, (int)saved["schemaVersion"]!);
        Assert.Equal("r1", (string?)saved["roles"]![0]!["id"]);
        Assert.Equal("grey", (string?)saved["roles"]![0]!["colour"]);                // a role's own unknown key survives too

        var recordPath = Path.Combine(Dir, "9.json");
        _fs.Files[recordPath] = "{\"schemaVersion\":2,\"contentId\":9,\"name\":\"Keeps\",\"role\":\"r1\"}";
        var record = store.Load(9)!;
        Assert.Equal("r1", record.Role);
        Assert.Same(mule, settings.RoleOf(record));
        store.Save(record);
        Assert.Equal("r1", (string?)JsonNode.Parse(_fs.Files[recordPath])!["role"]);
        Assert.Equal(2, (int)JsonNode.Parse(_fs.Files[recordPath])!["schemaVersion"]!);

        record.Role = "gone";                                                        // a dangling id is kept
        store.Save(record);
        Assert.Equal("gone", (string?)JsonNode.Parse(_fs.Files[recordPath])!["role"]);
        record.Role = null;
        store.Save(record);
        Assert.False(JsonNode.Parse(_fs.Files[recordPath])!.AsObject().ContainsKey("role"));
    }

    [Fact]
    public void A_hand_edited_roles_list_reads_a_null_as_none_skips_an_entry_it_cannot_use_and_quarantines_a_value_that_is_not_a_list()
    {
        // A hand edit. A null list reads as no roles. A null entry, or a role with no id, is kept in the file and never used, so no
        // checklist meets a null; a null switch set reads as everything on. A value that is not a list makes the file malformed: it is
        // moved aside and reported like any other. The import is stricter (BundleTests).
        var path = Path.Combine(Dir, "settings.json");
        _fs.Files[path] = "{\"schemaVersion\":1,\"stalenessDays\":10,\"roles\":null}";
        var none = Store().LoadSettings();
        Assert.Equal(10, none.StalenessDays);
        Assert.Empty(none.Roles);
        Assert.NotNull(none.Roles.Create("Mule"));                                   // a live list, not a null

        _fs.Files[path] = "{\"schemaVersion\":1,\"roles\":[null,{\"name\":\"No id\"},{\"id\":\"r1\",\"name\":\"Mule\",\"off\":null}]}";
        var roles = Store().LoadSettings().Roles;
        Assert.Equal(3, roles.Count);
        var usable = Assert.Single(roles.Usable);
        Assert.Equal("r1", usable.Id);
        Assert.Empty(usable.Off);
        Assert.Same(usable, roles.Find("r1"));
        Assert.Null(roles.Find(""));

        foreach (var bad in new[] { "{\"schemaVersion\":1,\"roles\":5}", "{\"schemaVersion\":1,\"roles\":{\"id\":\"r1\"}}" })
        {
            _fs.Files.Clear();
            _fs.Files[path] = bad;
            var store = Store();
            Assert.Empty(store.LoadSettings().Roles);
            Assert.Equal(bad, _fs.Files[path + ".corrupt"]);
            Assert.Single(store.Problems);
        }
    }

    [Fact]
    public void My_tasks_and_their_ticks_need_no_schema_step_and_round_trip_at_settings_1_and_records_2()
    {
        // No schema bump. "myTasks" loads and saves back whole at settings schema 1, a task's own unknown key included; a length is
        // written only for "every". A tick is an ordinary value in the record, at record schema 2. A build that does not know either
        // carries it through its [JsonExtensionData], which Unknown_keys_survive_a_load_and_a_save and
        // An_unknown_settings_key_survives_a_load_and_a_save_the_mechanism_a_rollback_relies_on pin.
        Assert.Equal(1, Settings.CurrentSchemaVersion);
        Assert.Equal(2, CharacterRecord.CurrentSchemaVersion);
        var settingsPath = Path.Combine(Dir, "settings.json");
        _fs.Files[settingsPath] = "{\"schemaVersion\":1,\"myTasks\":[{\"id\":\"mine:0123456789ab\",\"name\":\"Three-day craft\",\"kind\":\"every\",\"hours\":72,\"for\":\"picked\",\"picked\":[1,2],\"colour\":\"teal\"}]}";
        var store = Store();
        var settings = store.LoadSettings();
        var craft = Assert.Single(settings.MyTasks.Usable);
        Assert.Equal(("mine:0123456789ab", "Three-day craft", MyTask.Every, MyTask.ForPicked), (craft.Id, craft.Name, craft.Kind, craft.For));
        Assert.Equal(72, craft.Hours);
        Assert.Equal([1UL, 2UL], craft.Picked);
        store.SaveSettings(settings);
        var saved = JsonNode.Parse(_fs.Files[settingsPath])!.AsObject();
        Assert.Equal(1, (int)saved["schemaVersion"]!);
        Assert.Equal("mine:0123456789ab", (string?)saved["myTasks"]![0]!["id"]);
        Assert.Equal("every", (string?)saved["myTasks"]![0]!["kind"]);
        Assert.Equal("teal", (string?)saved["myTasks"]![0]!["colour"]);                     // a task's own unknown key survives too

        var aria = Items.Record();
        aria.Values[craft.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(72));
        aria.Values["mine:aaaaaaaaaaaa"] = new DoneValue(true, "daily:2026-09-12", Items.Now);
        store.Save(aria);
        Assert.Equal(2, (int)JsonNode.Parse(_fs.Files[Path.Combine(Dir, "1.json")])!["schemaVersion"]!);
        var back = Store().Load(1)!;
        Assert.Equal(new TimerValue(Items.Now + TimeSpan.FromHours(72)), back.Values[craft.Id]);
        Assert.Equal(new DoneValue(true, "daily:2026-09-12", Items.Now), back.Values["mine:aaaaaaaaaaaa"]);

        var daily = new Settings();
        daily.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily));
        store.SaveSettings(daily);
        Assert.False(JsonNode.Parse(_fs.Files[settingsPath])!["myTasks"]![0]!.AsObject().ContainsKey("hours"));
    }

    [Fact]
    public void A_hand_edited_my_tasks_list_reads_a_null_as_none_keeps_what_it_cannot_use_and_quarantines_a_value_that_is_not_a_list()
    {
        // A hand edit. A null list reads as no tasks. A null entry and a kind this build does not know (a later build's) load, and are
        // never used; a null picked list reads as nobody picked, and a missing "for" as every character. A value that is not a list
        // makes the file malformed: it is moved aside and reported like any other. The import is stricter (BundleTests).
        var path = Path.Combine(Dir, "settings.json");
        _fs.Files[path] = "{\"schemaVersion\":1,\"stalenessDays\":10,\"myTasks\":null}";
        var none = Store().LoadSettings();
        Assert.Equal(10, none.StalenessDays);
        Assert.Empty(none.MyTasks);
        Assert.NotNull(none.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily)));             // a live list, not a null

        _fs.Files[path] = "{\"schemaVersion\":1,\"myTasks\":[null,{\"id\":\"mine:aaaaaaaaaaaa\",\"name\":\"Monthly\",\"kind\":\"monthly\"},{\"id\":\"mine:bbbbbbbbbbbb\",\"name\":\"Granary\",\"kind\":\"weekly\",\"picked\":null}]}";
        var tasks = Store().LoadSettings().MyTasks;
        Assert.Equal(3, tasks.Count);
        var granary = Assert.Single(tasks.Usable);
        Assert.Equal(("mine:bbbbbbbbbbbb", MyTask.ForAll), (granary.Id, granary.For));
        Assert.Empty(granary.Picked);

        foreach (var bad in new[] { "{\"schemaVersion\":1,\"myTasks\":5}", "{\"schemaVersion\":1,\"myTasks\":{\"id\":\"mine:aaaaaaaaaaaa\"}}" })
        {
            _fs.Files.Clear();
            _fs.Files[path] = bad;
            var store = Store();
            Assert.Empty(store.LoadSettings().MyTasks);
            Assert.Equal(bad, _fs.Files[path + ".corrupt"]);
            Assert.Single(store.Problems);
        }
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1\",\"contentId\":11}")]
    [InlineData("{\"schemaVersion\":1.5,\"contentId\":11}")]
    [InlineData("{\"schemaVersion\":0,\"contentId\":11}")]
    [InlineData("{\"schemaVersion\":1,\"contentId\":11,\"contentId\":12}")]
    [InlineData("[1,2,3]")]
    public void A_malformed_record_is_quarantined_not_thrown(string text)
    {
        _fs.Files[Path.Combine(Dir, "11.json")] = text;
        var store = Store();
        Assert.Null(store.Load(11));
        Assert.False(_fs.Files.ContainsKey(Path.Combine(Dir, "11.json")));
        Assert.Equal(text, _fs.Files[Path.Combine(Dir, "11.json.corrupt")]);
        Assert.Single(store.Problems);
    }

    [Fact]
    public void A_second_quarantine_of_the_same_file_keeps_the_first()
    {
        _fs.Files[Path.Combine(Dir, "12.json")] = "first";
        Store().Load(12);
        _fs.Files[Path.Combine(Dir, "12.json")] = "second";
        Store().Load(12);
        Assert.Equal("first", _fs.Files[Path.Combine(Dir, "12.json.corrupt")]);
        Assert.Equal("second", _fs.Files[Path.Combine(Dir, "12.json.corrupt-2")]);
    }

    [Fact]
    public void A_record_from_a_newer_build_is_never_rewritten()
    {
        var text = "{\"schemaVersion\":99,\"contentId\":7,\"name\":\"Future\"}";
        _fs.Files[Path.Combine(Dir, "7.json")] = text;
        var store = Store();
        Assert.True(store.IsReadOnly(7));
        store.Save(new CharacterRecord { ContentId = 7, Name = "Overwrite" });
        Assert.Equal(text, _fs.Files[Path.Combine(Dir, "7.json")]);
        Assert.Contains(store.Problems, p => p.Contains("not saved"));
        Assert.False(store.IsReadOnly(8));
    }

    [Fact]
    public void Load_all_is_ordered_by_content_id_and_ignores_look_alike_extensions()
    {
        Store().Save(Items.Record(9));
        Store().Save(Items.Record(3));
        _fs.Files[Path.Combine(Dir, "4.jsonbak")] = "{}";
        Assert.Equal([3UL, 9UL], Store().LoadAll().Select(r => r.ContentId));
    }

    [Fact]
    public void An_identical_problem_is_reported_once()
    {
        var store = Store();
        store.Report("C:\\cfg\\Mnemosyne\\1.json: not saved (disk full)");
        store.Report("C:\\cfg\\Mnemosyne\\1.json: not saved (disk full)");
        store.Report("C:\\cfg\\Mnemosyne\\2.json: not saved (disk full)");
        Assert.Equal(2, store.Problems.Count);
    }

    [Fact]
    public void A_level_value_carries_its_progress_through_storage()
    {
        var store = Store();
        var r = Items.Record();
        r.Values["job:1"] = new LevelValue(72, 100, 12_000, 20_000);
        r.Values["tribe:1"] = new LevelValue(3, 8, 400, 1_200);
        r.Values["reset:leve-allowance"] = new CounterValue(40, 100, Items.Now, Items.Now.AddHours(2));
        r.ItemNames["reset:venture:0"] = "Kupo";
        r.Reminded["daily-reset"] = "daily:2026-09-13";
        store.Save(r);

        var back = store.Load(1)!;
        Assert.Equal(new LevelValue(72, 100, 12_000, 20_000), back.Values["job:1"]);
        Assert.Equal(new LevelValue(3, 8, 400, 1_200), back.Values["tribe:1"]);
        Assert.Equal("Kupo", back.ItemNames["reset:venture:0"]);
        Assert.Equal("daily:2026-09-13", back.Reminded["daily-reset"]);
        // The levequest anchor exists so it survives a logout, which is this line.
        Assert.Equal(new CounterValue(40, 100, Items.Now, Items.Now.AddHours(2)), back.Values["reset:leve-allowance"]);
        // A counter with no anchor at all still loads and falls back to the schedule.
        Assert.Equal(new CounterValue(40, 100, Items.Now), System.Text.Json.JsonSerializer.Deserialize<ItemValue>(
            """{"kind":"counter","current":40,"cap":100,"readAt":"2026-09-13T12:00:00+00:00"}""", Mnemosyne.Core.Storage.Json.Options));
        // A value with no progress at all still loads.
        Assert.Equal(new LevelValue(50, 100),
            System.Text.Json.JsonSerializer.Deserialize<ItemValue>("""{"kind":"level","current":50,"max":100}""", Mnemosyne.Core.Storage.Json.Options));
    }

    [Fact]
    public void A_gate_value_round_trips_and_a_schema_one_file_migrates_to_two()
    {
        var store = Store();
        var r = Items.Record();
        r.Values["reset:cactpot:mini"] = GateValue.Shut;
        r.Values["reset:cactpot:jumbo"] = new CounterValue(2, 3, Items.Now);
        store.Save(r);
        var text = _fs.Files[Path.Combine(Dir, "1.json")];
        Assert.Contains("\"kind\": \"gate\"", text);
        Assert.Contains("\"schemaVersion\": 2", text);
        var back = Store().Load(1)!;
        Assert.Equal(GateValue.Shut, back.Values["reset:cactpot:mini"]);
        Assert.Equal(new CounterValue(2, 3, Items.Now), back.Values["reset:cactpot:jumbo"]);

        // A file that says schema 1 and holds no gate loads through the no-op step and is written back as 2.
        _fs.Files[Path.Combine(Dir, "5.json")] = "{\"schemaVersion\":1,\"contentId\":5,\"name\":\"Old\",\"values\":{\"reset:roulette:1\":{\"kind\":\"done\",\"done\":true}}}";
        var migrating = Store();
        var old = migrating.Load(5)!;
        Assert.Equal(2, old.SchemaVersion);
        Assert.Equal(new DoneValue(true), old.Values["reset:roulette:1"]);
        migrating.Save(old);
        Assert.Contains("\"schemaVersion\": 2", _fs.Files[Path.Combine(Dir, "5.json")]);
    }

    [Fact]
    public void A_problem_names_its_file_alone_and_never_the_folder()
    {
        Store().Save(Items.Record(5));
        _fs.Unreadable.Add(Path.Combine(Dir, "5.json"));
        var store = Store();
        store.Load(5);
        var told = store.Report($"{Path.Combine(Dir, "6.json")}: not saved (could not write {Path.Combine(Dir, "6.json.tmp")})");
        store.Report($"{Dir} is not writable");
        Assert.Equal("6.json: not saved (could not write 6.json.tmp)", told);
        Assert.Equal(["5.json: unreadable (unreadable: 5.json); left untouched", "6.json: not saved (could not write 6.json.tmp)", "the configuration folder is not writable"],
            store.Problems);
    }

    [Fact]
    public void An_unreadable_record_is_never_written_over()
    {
        Store().Save(Items.Record(5));
        var before = _fs.Files[Path.Combine(Dir, "5.json")];
        _fs.Unreadable.Add(Path.Combine(Dir, "5.json"));
        var store = Store();
        Assert.Empty(store.LoadAll());
        Assert.True(store.IsReadOnly(5));
        var fresh = Items.Record(5);
        fresh.Name = "Fresh";
        store.Save(fresh);
        Assert.Equal(before, _fs.Files[Path.Combine(Dir, "5.json")]);
    }

    [Fact]
    public void An_unreadable_settings_file_is_never_written_over()
    {
        const string text = "{\"schemaVersion\":1,\"stalenessDays\":42}";
        _fs.Files[Path.Combine(Dir, "settings.json")] = text;
        _fs.Unreadable.Add(Path.Combine(Dir, "settings.json"));
        var store = Store();
        var settings = store.LoadSettings();
        Assert.Equal(7, settings.StalenessDays);
        Assert.True(store.SettingsReadOnly);
        store.SaveSettings(settings);
        Assert.Equal(text, _fs.Files[Path.Combine(Dir, "settings.json")]);
    }

    [Fact]
    public void A_folder_that_cannot_be_listed_loads_no_record_and_writes_over_none()
    {
        Store().Save(Items.Record(5));
        var before = _fs.Files[Path.Combine(Dir, "5.json")];
        _fs.FailList = true;
        var store = Store();
        Assert.Empty(store.LoadAll());
        Assert.Contains("could not be listed", Assert.Single(store.Problems));
        var changed = Items.Record(5);
        changed.Name = "Changed";
        store.Save(changed);
        Assert.True(store.IsReadOnly(5));
        Assert.Equal(before, _fs.Files[Path.Combine(Dir, "5.json")]);
    }

    [Fact]
    public void A_corrupt_file_that_cannot_be_moved_aside_is_left_untouched_and_never_written_over()
    {
        _fs.Files[Path.Combine(Dir, "6.json")] = "{ this is not json";
        _fs.Files[Path.Combine(Dir, "settings.json")] = "{ nor this";
        _fs.FailMoves = true;
        var store = Store();
        Assert.Empty(store.LoadAll());
        var settings = store.LoadSettings();
        Assert.True(store.IsReadOnly(6));
        Assert.True(store.SettingsReadOnly);
        store.Save(Items.Record(6));
        store.SaveSettings(settings);
        Assert.Equal("{ this is not json", _fs.Files[Path.Combine(Dir, "6.json")]);
        Assert.Equal("{ nor this", _fs.Files[Path.Combine(Dir, "settings.json")]);
        Assert.All(store.Problems.Take(2), p => Assert.Contains("left untouched", p));
    }
}
