using System.Text.Json;
using System.Text.Json.Nodes;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

/// <summary>The export bundle, its atomic write, and one test per rule of the import table. The apply step is TrackerTests' import test.</summary>
public class BundleTests
{
    private const string Game = "2026.09.01.0000.0000";

    private static string Export(IEnumerable<CharacterRecord> records, Settings? settings = null) =>
        Bundle.Export(records, settings ?? new Settings(), "0.1.0.0", Game, Items.Now);

    /// <summary>This PC: its tracked records, the content ids whose file is from a newer build, the character logged in, and its settings.</summary>
    private static ImportContext Here(IReadOnlyList<CharacterRecord> local, ulong? current = null, Settings? settings = null, bool settingsReadOnly = false, IReadOnlyCollection<ulong>? newer = null) =>
        new(local, id => newer?.Contains(id) == true, current, settings ?? new Settings(), settingsReadOnly);

    /// <summary>An export of Aria (1) and Kestrel Vane (2), changed by hand.</summary>
    private static string Edited(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(Export([Items.Record(1), Items.Record(2, "Kestrel Vane")]))!.AsObject();
        change(root);
        return root.ToJsonString();
    }

    /// <summary>A whole "every" task as the settings file holds it, then changed by hand.</summary>
    private static JsonObject MyTaskNode(Action<JsonObject> change)
    {
        var task = new JsonObject { ["id"] = "mine:0123456789ab", ["name"] = "Three-day craft", ["kind"] = "every", ["hours"] = 72, ["for"] = "all", ["picked"] = new JsonArray() };
        change(task);
        return task;
    }

    [Fact]
    public void The_export_holds_every_record_hidden_ones_included_and_the_settings_less_this_pcs_two_fields()
    {
        var hidden = Items.Record(2, "Kestrel Vane");
        hidden.Hidden = true;
        var settings = new Settings { RemindDailyReset = true, FirstRunNoticeDismissed = true, MainWindowOpen = true };
        settings.IgnoredEverywhere.Add("quest:2");

        var root = JsonNode.Parse(Export([hidden, SummariserTests.Aria()], settings))!.AsObject();

        Assert.Equal("mnemosyne-export", (string?)root["format"]);
        Assert.Equal(1, (int?)root["exportSchemaVersion"]);
        Assert.Equal("0.1.0.0", (string?)root["pluginVersion"]);
        Assert.Equal(Game, (string?)root["gameVersion"]);
        var records = root["records"]!.AsArray();
        Assert.Equal([1UL, 2UL], records.Select(r => (ulong)r!["contentId"]!));   // content-id order, the hidden one included
        Assert.True((bool)records[1]!["hidden"]!);
        Assert.Equal(CharacterRecord.CurrentSchemaVersion, (int)records[0]!["schemaVersion"]!);
        var exported = root["settings"]!.AsObject();
        Assert.Equal(Settings.CurrentSchemaVersion, (int)exported["schemaVersion"]!);
        Assert.True((bool)exported["remindDailyReset"]!);
        Assert.Equal("quest:2", (string?)exported["ignoredEverywhere"]![0]);
        Assert.False(exported.ContainsKey("firstRunNoticeDismissed"));   // this PC's, never exported
        Assert.False(exported.ContainsKey("mainWindowOpen"));
        Assert.Equal("mnemosyne-20260913-120000.json", Bundle.DefaultFileName(Items.Now));
    }

    [Fact]
    public void A_failed_write_leaves_the_old_file_whole_and_a_good_one_replaces_it_with_no_tmp_left()
    {
        var fs = new InMemoryFileSystem();
        var path = Path.Combine(@"C:\exports", "mnemosyne.json");
        fs.Files[path] = "old";
        fs.FailWrites = true;
        Assert.Throws<IOException>(() => AtomicFile.Write(fs, path, "new"));
        Assert.Equal("old", fs.Files[path]);
        fs.FailWrites = false;
        AtomicFile.Write(fs, path, "new");
        Assert.Equal("new", fs.Files[path]);
        Assert.DoesNotContain(fs.Files.Keys, k => k.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void An_existing_file_is_replaced_only_after_the_player_says_so()
    {
        // Dalamud's save dialog replaces a chosen file without a question. The settings page asks the player first whenever this
        // answers true, and writes only after the player's answer; asking touches nothing.
        var fs = new InMemoryFileSystem();
        var path = Path.Combine(@"C:\exports", "mnemosyne.json");
        Assert.False(Bundle.NeedsConfirmation(fs, path));
        fs.Files[path] = "old";
        Assert.True(Bundle.NeedsConfirmation(fs, path));
        Assert.Equal("old", fs.Files[path]);
        Assert.Equal(0, fs.Writes);
    }

    [Fact]
    public void An_export_made_over_a_newer_settings_file_leaves_the_settings_out_and_the_import_says_so()
    {
        // With this PC's settings file from a newer build, Mnemosyne runs on defaults, and a file carrying them would reset another
        // PC's settings. So the export leaves them out, and an import of it replaces nothing and says so; the records still arrive.
        var text = Bundle.Export([Items.Record(1)], null, "0.1.0.0", Game, Items.Now);
        Assert.False(JsonNode.Parse(text)!.AsObject().ContainsKey("settings"));
        var plan = ImportPlanner.Plan(text, Here([]), replaceSettings: true);
        Assert.Null(plan.Refusal);
        Assert.Null(plan.Settings);
        Assert.Equal("Settings: the file holds none, so this PC's are kept.", plan.SettingsWhy);
        Assert.Single(plan.ToApply);
    }

    [Fact]
    public void A_character_not_on_this_pc_is_added_whole_with_every_value_kind()
    {
        // Row 3 of the import table, and the round trip: what the export writes, the import reads back unchanged.
        var aria = SummariserTests.Aria();
        aria.Values["reset:gate"] = GateValue.Shut;
        aria.ItemNames["timer:venture:1"] = "Mira";
        aria.Reminded["daily"] = "daily:2026-09-12";
        aria.Ignored.Add("quest:2");
        aria.DisplayName = "Aria";

        var plan = ImportPlanner.Plan(Export([aria]), Here([]), replaceSettings: false);

        Assert.Null(plan.Refusal);
        Assert.Equal(ImportAction.Add, Assert.Single(plan.Entries).Action);
        Assert.Equal(JsonSerializer.Serialize(aria, Json.Options), JsonSerializer.Serialize(Assert.Single(plan.ToApply), Json.Options));
    }

    [Fact]
    public void A_file_that_is_not_a_mnemosyne_export_is_refused()
    {
        // Row 1: refused and reported; the file is not moved or renamed, since it is not Mnemosyne's file.
        foreach (var text in new[] { "{ this is not json", "", "[1, 2]", "null", "{\"exportSchemaVersion\":1}", "{\"format\":\"something-else\",\"exportSchemaVersion\":1}" })
        {
            var plan = ImportPlanner.Plan(text, Here([Items.Record()]), replaceSettings: true);
            Assert.NotNull(plan.Refusal);
            Assert.Empty(plan.Entries);
            Assert.Null(plan.Settings);
            Assert.Equal($"Nothing was imported. {plan.Refusal}", Assert.Single(plan.Summary()));
        }
    }

    [Fact]
    public void A_newer_export_settings_or_record_schema_refuses_the_whole_import_and_names_it()
    {
        // Row 2: the whole import is refused and the report names the newer object; the older record beside it is not taken either.
        var local = new[] { Items.Record(1) };
        var export = ImportPlanner.Plan(Edited(r => r["exportSchemaVersion"] = Bundle.CurrentSchemaVersion + 1), Here(local), true);
        Assert.Contains($"export schema {Bundle.CurrentSchemaVersion + 1}", export.Refusal);
        var settings = ImportPlanner.Plan(Edited(r => r["settings"]!["schemaVersion"] = Settings.CurrentSchemaVersion + 1), Here(local), false);
        Assert.Contains("settings", settings.Refusal);
        var record = ImportPlanner.Plan(Edited(r => r["records"]![1]!["schemaVersion"] = CharacterRecord.CurrentSchemaVersion + 1), Here(local), false);
        Assert.Contains("Kestrel Vane", record.Refusal);
        Assert.All(new[] { export, settings, record }, plan => Assert.Empty(plan.Entries));
    }

    [Fact]
    public void A_malformed_record_a_repeated_content_id_or_content_id_0_refuses_the_whole_import()
    {
        // Validate everything, then apply: one bad object anywhere refuses the file, and the settings are validated even when they
        // would not be replaced.
        var cases = new (string Text, string Named)[]
        {
            (Edited(r => r["records"]![1]!["values"] = "not a dictionary"), "Kestrel Vane"),
            (Edited(r => r["records"]![1]!["contentId"] = 1), "content id 1 twice"),
            (Edited(r => r["records"]![0]!["contentId"] = 0), "content id 0"),
            (Edited(r => r["records"] = "none"), "no records list"),
            (Edited(r => r["records"]!.AsArray().Add(JsonValue.Create(5))), "not a JSON object"),
            (Edited(r => r["settings"]!["stalenessDays"] = "seven"), "settings could not be read"),
            // A repeated key at the root, in a record or in the settings is refused as invalid JSON (JsonObject builds its dictionary
            // lazily, on the first indexer read after the parse, so the duplicate surfaces only then). These are raw text rather
            // than Edited: JsonObject cannot hold two entries for one key, so a duplicate can only be written by hand.
            ("""{"format":"mnemosyne-export","format":"mnemosyne-export","exportSchemaVersion":1,"records":[]}""", "not JSON"),
            ("""{"format":"mnemosyne-export","exportSchemaVersion":1,"records":[{"contentId":1,"contentId":1,"name":"Aria","homeWorld":"Balmung"}]}""", "not JSON"),
            ("""{"format":"mnemosyne-export","exportSchemaVersion":1,"records":[],"settings":{"schemaVersion":1,"schemaVersion":1}}""", "not JSON"),
            // A JSON null for a record's or the settings' non-nullable name or collection is refused: left unchecked it would pass
            // validation, be applied and saved, only to throw a NullReferenceException out of the checklist cache on every frame.
            (Edited(r => r["records"]![1]!["values"] = null), "Kestrel Vane"),
            (Edited(r => r["settings"]!["ignoredEverywhere"] = null), "settings could not be read"),
            // The tasks map under the planner's stricter options. A null map refuses, as a null collection does, and so does an
            // entry the reader cannot take.
            (Edited(r => r["settings"]!["tasks"] = null), "settings could not be read"),
            (Edited(r => r["settings"]!["tasks"] = new JsonObject { ["reset:tails"] = new JsonObject { ["priority"] = "urgent" } }), "settings could not be read"),
            // The roles under the planner's stricter options. A value that is not a list, a null list and a role's null name refuse
            // as any malformed setting does. A role with no id or no name, or a null entry, which the stricter options do not
            // check inside a list, is refused by name.
            (Edited(r => r["settings"]!["roles"] = 5), "settings could not be read"),
            (Edited(r => r["settings"]!["roles"] = new JsonObject { ["id"] = "r1", ["name"] = "Mule" }), "settings could not be read"),
            (Edited(r => r["settings"]!["roles"] = null), "settings could not be read"),
            (Edited(r => r["settings"]!["roles"] = new JsonArray(new JsonObject { ["id"] = "r1", ["name"] = null })), "settings could not be read"),
            (Edited(r => r["settings"]!["roles"] = new JsonArray(new JsonObject { ["name"] = "Mule" })), "a role with no id or no name"),
            (Edited(r => r["settings"]!["roles"] = new JsonArray(new JsonObject { ["id"] = "r1", ["name"] = " " })), "a role with no id or no name"),
            (Edited(r => r["settings"]!["roles"] = new JsonArray((JsonNode?)null)), "a role with no id or no name"),
            // The tasks under the planner's stricter options. A value that is not a list, a null list, and a task's null name or null
            // picked list refuse as any malformed setting does. A null entry, and a task with no id or one that is not "mine:" and 12
            // lower-case hex characters, no usable name, an unknown kind, an "every" length outside 1 to 720 hours or an unknown
            // "for", which the stricter options do not check inside a list, are refused by name.
            (Edited(r => r["settings"]!["myTasks"] = 5), "settings could not be read"),
            (Edited(r => r["settings"]!["myTasks"] = null), "settings could not be read"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["name"] = null))), "settings could not be read"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["picked"] = null))), "settings could not be read"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray((JsonNode?)null)), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t.Remove("id")))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["id"] = "quest:1"))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["id"] = "mine:1"))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["name"] = " "))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["name"] = "Chest #1"))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["kind"] = "monthly"))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["hours"] = 0))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["hours"] = 721))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t.Remove("hours")))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["for"] = "friends"))), "a My task this build cannot use"),
            (Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(t => t["for"] = "role:"))), "a My task this build cannot use"),
        };
        foreach (var (text, named) in cases)
        {
            var plan = ImportPlanner.Plan(text, Here([]), replaceSettings: false);
            Assert.Contains(named, plan.Refusal);
            Assert.Empty(plan.Entries);
        }
    }

    [Fact]
    public void Imported_settings_out_of_range_are_clamped_to_the_settings_pages_own_bounds()
    {
        // The settings page clamps StalenessDays to 1-365, EndingSoonDays to 1-60 and LiveReadSeconds to 1-60 (SettingsWindow.cs).
        // An import is a second way for an out-of-range number to reach Assembler.cs's TimeSpan.FromDays, which throws past its own range.
        var theirs = new Settings { StalenessDays = 9999, EndingSoonDays = 0, LiveReadSeconds = -5 };
        var plan = ImportPlanner.Plan(Export([Items.Record(1)], theirs), Here([]), replaceSettings: true);
        Assert.Null(plan.Refusal);
        Assert.Equal(365, plan.Settings!.StalenessDays);
        Assert.Equal(1, plan.Settings.EndingSoonDays);
        Assert.Equal(1, plan.Settings.LiveReadSeconds);
    }

    [Fact]
    public void A_character_whose_file_here_is_from_a_newer_build_is_kept()
    {
        // Row 4: a newer build's file is never overwritten; it is not tracked here, so it is not in the local list either.
        var plan = ImportPlanner.Plan(Export([Items.Record(7, "Future Self")]), Here([], newer: [7]), false);
        var entry = Assert.Single(plan.Entries);
        Assert.Equal(ImportAction.Keep, entry.Action);
        Assert.Contains("newer build", entry.Why);
        Assert.Contains("could not be read", entry.Why);
        Assert.Empty(plan.ToApply);
    }

    [Fact]
    public void The_copy_seen_later_wins_whole_and_a_tie_keeps_this_pcs()
    {
        // Row 5: whole, never a per-item merge, since values carry their own read instants.
        var mine = Items.Record(1);
        mine.DisplayName = "Mine";
        var older = Items.Record(1);
        older.LastSeen = Items.Now - TimeSpan.FromDays(1);
        var newer = Items.Record(1);
        newer.LastSeen = Items.Now + TimeSpan.FromDays(1);
        newer.Values["quest:1"] = new DoneValue(true);

        Assert.Equal(ImportAction.Keep, ImportPlanner.Plan(Export([older]), Here([mine]), false).Entries.Single().Action);
        Assert.Equal(ImportAction.Keep, ImportPlanner.Plan(Export([Items.Record(1)]), Here([mine]), false).Entries.Single().Action);   // a tie
        var entry = ImportPlanner.Plan(Export([newer]), Here([mine]), false).Entries.Single();
        Assert.Equal(ImportAction.Replace, entry.Action);
        Assert.Null(entry.Record.DisplayName);   // the file's copy, whole: this PC's display name goes with this PC's copy
        Assert.Equal(new DoneValue(true), entry.Record.Values["quest:1"]);
        // The summary says what is lost before anything is applied: the role, which the replacing copy also brings, is named
        // alongside the rest, and the gil history, which the game cannot give back, is named too.
        Assert.Equal("the file's copy was seen later; it replaces this PC's whole, display name, hidden flag, ignores, role and gil history included", entry.Why);
    }

    [Fact]
    public void A_replace_names_role_in_its_reason_and_a_file_copy_with_no_role_wipes_this_pcs()
    {
        // The wording says the replacing copy brings its role too; this pins that against the real field, not only the sentence.
        // This PC's copy has a role; the file's copy was seen later and has none, so the role that "goes with this PC's copy"
        // (as DisplayName does above) must NOT survive — a replace is whole, never a per-field merge (row 5).
        var mine = Items.Record(1);
        mine.Role = "old-role-id";
        var newer = Items.Record(1);
        newer.LastSeen = Items.Now + TimeSpan.FromDays(1);

        var entry = ImportPlanner.Plan(Export([newer]), Here([mine]), false).Entries.Single();
        Assert.Equal(ImportAction.Replace, entry.Action);
        Assert.Null(entry.Record.Role);
        Assert.Contains("role", entry.Why);
        Assert.Equal("the file's copy was seen later; it replaces this PC's whole, display name, hidden flag, ignores, role and gil history included", entry.Why);
    }

    [Fact]
    public void The_logged_in_character_keeps_this_pcs_record_even_when_the_files_is_newer()
    {
        // Row 6: the live read is fresher, and replacing Tracker.Current mid-session would race the tick.
        var newer = Items.Record(1);
        newer.LastSeen = Items.Now + TimeSpan.FromDays(1);
        var plan = ImportPlanner.Plan(Export([newer, Items.Record(2, "Kestrel Vane")]), Here([Items.Record(1)], current: 1), false);
        var aria = plan.Entries.Single(e => e.Record.ContentId == 1);
        Assert.Equal(ImportAction.Keep, aria.Action);
        Assert.Contains("logged in", aria.Why);
        Assert.Equal([2UL], plan.ToApply.Select(r => r.ContentId));
    }

    [Fact]
    public void A_hidden_character_arrives_hidden()
    {
        // Row 7: Hidden travels inside the record.
        var hidden = Items.Record(3, "Kestrel Vane");
        hidden.Hidden = true;
        Assert.True(ImportPlanner.Plan(Export([hidden]), Here([]), false).ToApply.Single().Hidden);
    }

    [Fact]
    public void A_forgotten_character_comes_back_and_the_summary_names_every_character_before_anything_is_applied()
    {
        // Row 8: forget keeps no tombstone, so an export made before the forget brings the character back; the summary says so first.
        var summary = ImportPlanner.Plan(Export([Items.Record(1), Items.Record(2, "Kestrel Vane")]), Here([Items.Record(1)]), false).Summary();
        Assert.Equal("2 characters in the file: 1 to add, 0 to replace, 1 kept as this PC has it.", summary[0]);
        Assert.Contains("Add Kestrel Vane (Balmung): not on this PC.", summary);
        Assert.Contains("Keep Aria Whitewind (Balmung): this PC's copy was seen as late or later.", summary);
        Assert.Equal("Settings: kept as they are on this PC.", summary[^1]);
    }

    [Fact]
    public void Settings_are_replaced_only_when_asked_keep_this_pcs_two_fields_and_never_over_a_newer_file()
    {
        // Row 9.
        var theirs = new Settings { StalenessDays = 30, RemindDailyReset = true };
        var mine = new Settings { FirstRunNoticeDismissed = true, MainWindowOpen = true };
        var text = Export([Items.Record(1)], theirs);

        Assert.Null(ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: false).Settings);
        var replaced = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: true).Settings!;
        Assert.Equal(30, replaced.StalenessDays);
        Assert.True(replaced.RemindDailyReset);
        Assert.True(replaced.FirstRunNoticeDismissed);   // this PC's, never the file's
        Assert.True(replaced.MainWindowOpen);
        var readOnly = ImportPlanner.Plan(text, Here([], settings: mine, settingsReadOnly: true), replaceSettings: true);
        Assert.Null(readOnly.Settings);
        Assert.Contains("newer build", readOnly.SettingsWhy);
        Assert.Contains("could not be read", readOnly.SettingsWhy);
        Assert.Single(readOnly.ToApply);                  // the records still import
    }

    [Fact]
    public void A_settings_file_at_schema_1_with_no_tasks_imports_without_refusal_either_way()
    {
        // A settings file at schema 1 with no "tasks" key at all, exercised directly rather than only through the shared
        // MigrateSettings. Kept: this PC's stay, so plan.Settings is null. Replaced: schema 1, the file's values, and an empty
        // tasks map, since such a file carries none.
        var text = Bundle.Export([Items.Record(1)], new Settings { StalenessDays = 30 }, "0.1.0.0", Game, Items.Now);
        var root = JsonNode.Parse(text)!.AsObject();
        root["settings"]!.AsObject().Remove("tasks");   // a 6a-shaped settings object: no "tasks" key at all
        text = root.ToJsonString();

        Assert.Null(ImportPlanner.Plan(text, Here([]), replaceSettings: false).Settings);
        var replaced = ImportPlanner.Plan(text, Here([]), replaceSettings: true);
        Assert.Null(replaced.Refusal);
        Assert.Equal(1, replaced.Settings!.SchemaVersion);
        Assert.Equal(30, replaced.Settings.StalenessDays);
        Assert.Empty(replaced.Settings.Tasks);
    }

    [Fact]
    public void An_export_carries_the_adjustments_and_an_import_takes_them_only_with_the_settings_box()
    {
        // The adjustments are part of the settings, so they travel in the bundle. Without "Also replace my settings" they stay
        // this PC's; with it the file's map replaces this PC's whole. SnoozedUntil is an instant, so it arrives as the same
        // instant on another PC. A null entry inside the map, as a hand edit leaves it, is taken and reads as no adjustment, as
        // it does on load; only a null map refuses (the refusal cases above).
        var theirs = new Settings();
        theirs.Tasks.Pin("reset:roulette:1");
        theirs.Tasks.SetPriority("reset:tails", TaskPriority.Low);
        theirs.Tasks.Snooze("event:moonfire:2026", null, Items.Now);
        var mine = new Settings();
        mine.Tasks.Hide("reset:tribal");
        var root = JsonNode.Parse(Export([Items.Record(1)], theirs))!.AsObject();
        Assert.True((bool)root["settings"]!["tasks"]!["reset:roulette:1"]!["pinned"]!);
        root["settings"]!["tasks"]!["reset:leves"] = null;
        var text = root.ToJsonString();

        Assert.Null(ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: false).Settings);   // kept: the import takes nothing
        var plan = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: true);
        Assert.Null(plan.Refusal);
        var taken = plan.Settings!;
        Assert.True(taken.Tasks.Get("reset:roulette:1").Pinned);
        Assert.Equal(TaskPriority.Low, taken.Tasks.Get("reset:tails").Priority);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero), taken.Tasks.Get("event:moonfire:2026").SnoozedUntil);
        Assert.Equal(TaskPreference.Default, taken.Tasks.Get("reset:leves"));                            // the null entry: no adjustment
        Assert.False(taken.Tasks.Get("reset:tribal").Hidden);                                              // the file's map, whole
    }

    [Fact]
    public void An_export_carries_the_roles_and_each_records_role_and_an_import_without_the_settings_keeps_the_id_as_an_unknown_role()
    {
        // A record's role travels with the record. The roles travel with the settings, so they arrive only with "Also replace my
        // settings", and the summary says so. A record taken without them keeps its id and behaves as no role here until they arrive.
        var theirs = new Settings();
        var mule = theirs.Roles.Create("Mule")!;
        mule.Off.Add("category:Events");
        var aria = Items.Record(1);
        aria.Role = mule.Id;
        var root = JsonNode.Parse(Export([aria], theirs))!.AsObject();
        Assert.Equal(mule.Id, (string?)root["records"]![0]!["role"]);
        Assert.Equal("Mule", (string?)root["settings"]!["roles"]![0]!["name"]);
        var text = root.ToJsonString();

        var mine = new Settings();
        mine.Roles.Create("Old1");
        mine.Roles.Create("Old2");
        var kept = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: false);
        Assert.Null(kept.Refusal);
        Assert.Null(kept.Settings);
        var arrived = Assert.Single(kept.ToApply);
        Assert.Equal(mule.Id, arrived.Role);
        Assert.Null(mine.RoleOf(arrived));                                           // unknown here: no role

        var replaced = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: true);
        Assert.Equal("Mule", replaced.Settings!.RoleOf(Assert.Single(replaced.ToApply))?.Name);
        Assert.Equal(["category:Events"], replaced.Settings.Roles.Single().Off.Order(StringComparer.Ordinal));
        // The roles take real work to rebuild, so the summary counts what each side holds, not only that they are included: the
        // file's 1 role replaces this PC's 2.
        Assert.Contains("roles included (the file has 1; this PC's 2 are replaced)", replaced.SettingsWhy);
    }

    [Fact]
    public void An_import_with_no_roles_at_all_wipes_every_role_this_pc_had_and_the_summary_names_the_count()
    {
        // An import with no roles at all — a pre-roles backup, or one made after every role was deleted — replaces this PC's roles
        // with none, and the summary reports it: "the file has 0; this PC's 2 are replaced".
        var mine = new Settings();
        mine.Roles.Create("Old1");
        mine.Roles.Create("Old2");
        var text = Export([Items.Record(1)], new Settings());   // an export made over settings with no roles at all
        var plan = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: true);
        Assert.Null(plan.Refusal);
        Assert.Empty(plan.Settings!.Roles);
        Assert.Contains("roles included (the file has 0; this PC's 2 are replaced)", plan.SettingsWhy);
    }

    [Fact]
    public void The_settings_lines_role_count_reads_singular_when_this_pcs_own_count_is_one()
    {
        // The "is"/"are" branch on this PC's own count.
        var mine = new Settings();
        mine.Roles.Create("Solo");
        var theirs = new Settings();
        theirs.Roles.Create("A");
        theirs.Roles.Create("B");
        var text = Export([Items.Record(1)], theirs);
        var plan = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: true);
        Assert.Contains("roles included (the file has 2; this PC's 1 is replaced)", plan.SettingsWhy);
    }

    [Fact]
    public void An_export_leaves_out_a_role_the_import_would_refuse_so_this_pcs_own_export_still_imports()
    {
        // A null entry, a role with no id and a role with a blank name load from a hand-edited file and are never used. The planner
        // refuses each, and it validates the settings even when they are not taken, so one of them in the export would refuse
        // this PC's whole export, records included, on any PC. The export leaves them out and keeps the whole roles in their
        // order; this PC's own list is not changed by exporting it.
        var settings = new Settings();
        settings.Roles.Create("Mule");
        settings.Roles.Add(null!);
        settings.Roles.Add(new Role { Name = "No id" });
        settings.Roles.Add(new Role { Id = "r2", Name = " " });
        settings.Roles.Create("Alt");
        var text = Export([Items.Record(1)], settings);

        Assert.Equal(["Mule", "Alt"], JsonNode.Parse(text)!["settings"]!["roles"]!.AsArray().Select(r => (string?)r!["name"]));
        Assert.Null(ImportPlanner.Plan(text, Here([]), replaceSettings: false).Refusal);
        var plan = ImportPlanner.Plan(text, Here([]), replaceSettings: true);
        Assert.Null(plan.Refusal);
        Assert.Equal(["Mule", "Alt"], plan.Settings!.Roles.Select(r => r.Name));
        Assert.Equal(5, settings.Roles.Count);
    }

    [Fact]
    public void An_export_carries_my_tasks_and_their_ticks_and_an_import_takes_the_tasks_only_with_the_settings_box()
    {
        // The ticks are values in the records, so they travel with the records. The definitions travel with the settings, so
        // they arrive only with "Also replace my settings", and the summary counts both sides. The file's tasks keep their ids,
        // so a record's tick is read by the task it was made for, and no task this PC made shares an id with them. The refusal
        // cases' own whole task imports, so each refusal is its edit's.
        var theirs = new Settings();
        var chest = theirs.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var aria = Items.Record(1);
        aria.Values[chest.Id] = chest.TickValue(Items.Now)!;
        var root = JsonNode.Parse(Export([aria], theirs))!.AsObject();
        Assert.Equal("done", (string?)root["records"]![0]!["values"]![chest.Id]!["kind"]);
        Assert.Equal(chest.Id, (string?)root["settings"]!["myTasks"]![0]!["id"]);
        var text = root.ToJsonString();

        var mine = new Settings();
        for (var i = 0; i < 5; i++) mine.AddMyTask(Tasks.Draft("Mine " + i, MyTask.Once));
        var kept = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: false);
        Assert.Null(kept.Refusal);
        Assert.Null(kept.Settings);
        Assert.Equal(chest.TickValue(Items.Now), Assert.Single(kept.ToApply).Values[chest.Id]);   // the tick arrives with its record

        var replaced = ImportPlanner.Plan(text, Here([], settings: mine), replaceSettings: true);
        var arrived = Assert.Single(replaced.Settings!.MyTasks);
        Assert.Equal((chest.Id, "FC chest"), (arrived.Id, arrived.Name));
        Assert.DoesNotContain(arrived.Id, mine.MyTasks.Select(t => t.Id));                 // no task this PC made shares its id
        Assert.Contains("My tasks too (the file has 1; this PC's 5 are replaced)", replaced.SettingsWhy);
        Assert.Contains("roles included (the file has 0; this PC's 0 are replaced)", replaced.SettingsWhy);

        Assert.Null(ImportPlanner.Plan(Edited(r => r["settings"]!["myTasks"] = new JsonArray(MyTaskNode(_ => { }))), Here([]), replaceSettings: true).Refusal);
    }

    [Fact]
    public void An_export_leaves_out_a_my_task_the_import_would_refuse_so_this_pcs_own_export_still_imports()
    {
        // As with roles: a null entry, an id that is not "mine:" and 12 lower-case hex characters, and a kind this build does not
        // know load from a hand-edited file, or a later build's, and are never used. The planner refuses each, and it validates
        // the settings even when they are not taken, so the export leaves them out and keeps the whole tasks in their order.
        // This PC's own list is not changed by exporting it.
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        settings.MyTasks.Add(null!);
        settings.MyTasks.Add(new MyTask { Id = "quest:1", Name = "Not a task id", Kind = MyTask.Daily });
        settings.MyTasks.Add(new MyTask { Id = "mine:bbbbbbbbbbbb", Name = "Monthly", Kind = "monthly" });
        var granary = settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly))!;
        var text = Export([Items.Record(1)], settings);

        Assert.Equal(["FC chest", "Granary"], JsonNode.Parse(text)!["settings"]!["myTasks"]!.AsArray().Select(t => (string?)t!["name"]));
        Assert.Null(ImportPlanner.Plan(text, Here([]), replaceSettings: false).Refusal);
        var plan = ImportPlanner.Plan(text, Here([]), replaceSettings: true);
        Assert.Null(plan.Refusal);
        Assert.Equal([chest.Id, granary.Id], plan.Settings!.MyTasks.Select(t => t.Id));
        Assert.Equal(5, settings.MyTasks.Count);
    }
}
