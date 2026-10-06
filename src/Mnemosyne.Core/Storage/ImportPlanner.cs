using System.Text.Json;
using System.Text.Json.Nodes;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Storage;

/// <summary>What an import does with one character in the file.</summary>
public enum ImportAction { Add, Replace, Keep }

/// <summary>One character in the file, what the import does with it, and why, in the words the summary shows.</summary>
public sealed record ImportEntry(CharacterRecord Record, ImportAction Action, string Why);

/// <summary>This PC when the import is planned: the tracked records, which content ids have a file that could not be read or is from a
/// newer build (never tracked, so not in Local), the character logged in, and the settings with whether their file could not be read or is
/// from a newer build.</summary>
public sealed record ImportContext(IReadOnlyList<CharacterRecord> Local, Func<ulong, bool> IsReadOnly, ulong? Current, Settings Settings, bool SettingsReadOnly);

/// <summary>An import planned and not yet applied: either a refusal, or an entry per character in the file and the settings to take, if any.</summary>
public sealed record ImportPlan(string? Refusal, IReadOnlyList<ImportEntry> Entries, Settings? Settings, string SettingsWhy)
{
    /// <summary>The records Tracker.Import adds or replaces.</summary>
    public IReadOnlyList<CharacterRecord> ToApply => Entries.Where(e => e.Action != ImportAction.Keep).Select(e => e.Record).ToList();

    /// <summary>What the settings page shows before anything is applied: the counts, one line per character, and the settings' line.</summary>
    public IReadOnlyList<string> Summary()
    {
        if (Refusal != null) return [$"Nothing was imported. {Refusal}"];
        int Count(ImportAction action) => Entries.Count(e => e.Action == action);
        var lines = new List<string>
        {
            $"{Entries.Count} character{(Entries.Count == 1 ? "" : "s")} in the file: {Count(ImportAction.Add)} to add, " +
            $"{Count(ImportAction.Replace)} to replace, {Count(ImportAction.Keep)} kept as this PC has it.",
        };
        foreach (var entry in Entries) lines.Add($"{entry.Action} {entry.Record.Label} ({entry.Record.HomeWorld}): {entry.Why}.");
        lines.Add(SettingsWhy);
        return lines;
    }
}

/// <summary>The import, planned as a pure function over the file's text and this PC's state, so every rule of the table is a unit test.
/// Everything is validated before anything is planned. A file that is not an export, or holds a newer schema, a malformed record, a
/// repeated content id or content id 0, is refused whole, and the refusal names what it found. Then, per character: added when not on
/// this PC, kept when logged in or when this PC's file could not be read or is from a newer build, and otherwise the copy seen later
/// wins, whole, a tie keeping this PC's, and the summary says what the winning copy replaces. The settings are taken only when asked,
/// never over a file that could not be read or is from a newer build, and keep this PC's two machine-local fields; a file that holds none
/// replaces nothing. Nothing here writes; Tracker.Import applies what this returns.</summary>
public static class ImportPlanner
{
    /// <summary>A planner-only copy of Json.Options that also refuses a JSON null for a member that is not nullable, so a
    /// hand-edited or foreign file's null in a record's or the settings' name or collection refuses the whole import instead of reaching the
    /// checklist cache as a null reference later. The load path keeps Json.Options unchanged, so an older file loads exactly as today.</summary>
    private static readonly JsonSerializerOptions PlannerOptions = new(Json.Options) { RespectNullableAnnotations = true };

    public static ImportPlan Plan(string text, ImportContext here, bool replaceSettings)
    {
        JsonObject root;
        try
        {
            // Duplicate-property detection is off by default, and JsonObject otherwise builds its dictionary lazily, so a
            // repeated key used to throw ArgumentException on the first read after this try rather than here. Refusing duplicates at parse time
            // catches a repeated key anywhere in the document, at the root, in a record or in the settings, inside this one try.
            if (JsonNode.Parse(text, null, new JsonDocumentOptions { AllowDuplicateProperties = false }) is not JsonObject parsed)
                return Refuse("The file is not a Mnemosyne export: it is not a JSON object.");
            root = parsed;
        }
        catch (Exception ex) when (RecordStore.IsMalformed(ex))
        {
            return Refuse($"The file is not JSON ({Trim(ex.Message)}).");
        }
        if (root["format"] is not JsonValue format || !format.TryGetValue<string>(out var name) || name != Bundle.Format)
            return Refuse($"The file is not a Mnemosyne export: it has no \"format\": \"{Bundle.Format}\".");
        if (root["exportSchemaVersion"] is not JsonValue exportNode || !exportNode.TryGetValue<int>(out var exportVersion) || exportVersion < 1)
            return Refuse("The file has no valid exportSchemaVersion.");
        if (exportVersion > Bundle.CurrentSchemaVersion)
            return Refuse($"The file is from a newer Mnemosyne (export schema {exportVersion}; this build reads {Bundle.CurrentSchemaVersion}).");

        // An export made over a settings file that could not be read or is from a newer build holds none (Bundle.Export); settings that
        // are there are validated whether or not they will be taken.
        Settings? theirs = null;
        if (root["settings"] is { } settingsValue)
        {
            if (settingsValue is not JsonObject settingsNode) return Refuse("Its settings are not a JSON object.");
            if (RecordStore.ReadSchemaVersion(settingsNode) is not { } settingsVersion) return Refuse("Its settings' schemaVersion is not a positive integer.");
            if (settingsVersion > Settings.CurrentSchemaVersion)
                return Refuse($"Its settings are from a newer Mnemosyne (schema {settingsVersion}; this build reads {Settings.CurrentSchemaVersion}).");
            try
            {
                theirs = RecordStore.MigrateSettings(settingsNode, settingsVersion, PlannerOptions);
            }
            catch (Exception ex) when (RecordStore.IsMalformed(ex))
            {
                return Refuse($"Its settings could not be read ({Trim(ex.Message)}).");
            }
            // Every role needs an id and a name. The stricter options check a property, not a list's entries, so a null
            // entry and a role missing either are refused here, by name. A role with no id could never be assigned. Bundle.Export leaves such a
            // role out by the same test, so this PC's own export is never refused for one.
            if (!theirs.Roles.All(RoleList.IsWhole))
                return Refuse("Its settings hold a role with no id or no name.");
            // Every task needs an id of "mine:" and 12 lower-case hex characters, a usable name, a known kind, an
            // "every" length of 1 to 720 hours and a known "for". The stricter options check a property, not a list's entries, so a null entry and
            // a task that breaks a rule are refused here, by name. Bundle.Export leaves such a task out by the same test, so this PC's own export is
            // never refused for one.
            // "No id" undersold what this also refuses, a null entry and a malformed id such as "mine:1" among them, which do
            // have an id. The words now cover every shape IsWhole rejects.
            if (!theirs.MyTasks.All(MyTask.IsWhole))
                return Refuse("Its settings hold a My task this build cannot use (no usable id or name, an unknown kind, …).");
            // The settings page clamps these three before they ever reach Assembler.cs's TimeSpan.FromDays; an import is a second way
            // in, so it is clamped to the same bounds here, whether or not the file's settings end up replacing this PC's.
            theirs.StalenessDays = Math.Clamp(theirs.StalenessDays, 1, 365);
            theirs.EndingSoonDays = Math.Clamp(theirs.EndingSoonDays, 1, 60);
            theirs.LiveReadSeconds = Math.Clamp(theirs.LiveReadSeconds, 1, 60);
        }

        if (root["records"] is not JsonArray list) return Refuse("The file holds no records list.");
        var records = new List<CharacterRecord>(list.Count);
        var ids = new HashSet<ulong>();
        foreach (var node in list)
        {
            if (node is not JsonObject recordNode) return Refuse("A record in the file is not a JSON object.");
            var who = Describe(recordNode);
            if (RecordStore.ReadSchemaVersion(recordNode) is not { } recordVersion) return Refuse($"The record of {who} has a schemaVersion that is not a positive integer.");
            if (recordVersion > CharacterRecord.CurrentSchemaVersion)
                return Refuse($"The record of {who} is from a newer Mnemosyne (schema {recordVersion}; this build reads {CharacterRecord.CurrentSchemaVersion}).");
            CharacterRecord record;
            try
            {
                record = RecordStore.MigrateRecord(recordNode, recordVersion, PlannerOptions);
            }
            catch (Exception ex) when (RecordStore.IsMalformed(ex))
            {
                return Refuse($"The record of {who} could not be read ({Trim(ex.Message)}).");
            }
            if (record.ContentId == 0) return Refuse($"The record of {who} has content id 0.");
            if (!ids.Add(record.ContentId)) return Refuse($"The file holds content id {record.ContentId} twice.");
            records.Add(record);
        }

        var local = here.Local.ToDictionary(r => r.ContentId);
        var entries = records.Select(record => Entry(record, here, local)).ToList();
        if (theirs == null) return new ImportPlan(null, entries, null, "Settings: the file holds none, so this PC's are kept.");
        if (!replaceSettings) return new ImportPlan(null, entries, null, "Settings: kept as they are on this PC.");
        if (here.SettingsReadOnly) return new ImportPlan(null, entries, null, "Settings: kept, because this PC's settings file could not be read or is from a newer build.");
        theirs.FirstRunNoticeDismissed = here.Settings.FirstRunNoticeDismissed;
        theirs.MainWindowOpen = here.Settings.MainWindowOpen;
        // The box also replaces the All tasks adjustments, the roles and the My tasks,
        // and the summary is where the player decides, so it says so. The roles take real work to rebuild, a tick tree
        // then per-character assignments, and so do the tasks, so the summary also counts what each side holds, not only that they are included.
        var roleCount = here.Settings.Roles.Count;
        var taskCount = here.Settings.MyTasks.Count;
        return new ImportPlan(null, entries, theirs,
            $"Settings: replaced by the file's, All tasks adjustments and roles included (the file has {theirs.Roles.Count}; this PC's {roleCount} " +
            $"{(roleCount == 1 ? "is" : "are")} replaced), and My tasks too (the file has {theirs.MyTasks.Count}; this PC's {taskCount} " +
            $"{(taskCount == 1 ? "is" : "are")} replaced), keeping this PC's first-run notice and window state.");
    }

    private static ImportEntry Entry(CharacterRecord record, ImportContext here, IReadOnlyDictionary<ulong, CharacterRecord> local)
    {
        if (record.ContentId == here.Current) return new(record, ImportAction.Keep, "logged in now, so this PC's live record stays");
        if (here.IsReadOnly(record.ContentId)) return new(record, ImportAction.Keep, "this PC's file could not be read or is from a newer build and is never overwritten");
        if (!local.TryGetValue(record.ContentId, out var mine)) return new(record, ImportAction.Add, "not on this PC");
        return record.LastSeen > mine.LastSeen
            ? new(record, ImportAction.Replace, "the file's copy was seen later; it replaces this PC's whole, display name, hidden flag, ignores, role and gil history included")
            : new(record, ImportAction.Keep, "this PC's copy was seen as late or later");
    }

    private static ImportPlan Refuse(string why) => new(why, [], null, "");

    private static string Trim(string message) => message.TrimEnd('.', ' ');

    /// <summary>A record named for a refusal before it is read: its name and content id as the file holds them.</summary>
    private static string Describe(JsonObject record)
    {
        var name = record["name"] is JsonValue value && value.TryGetValue<string>(out var n) && n.Length > 0 ? n : "an unnamed character";
        return $"{name} ({record["contentId"]?.ToJsonString() ?? "no content id"})";
    }
}
