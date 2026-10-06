using System.Text.Json;
using System.Text.Json.Nodes;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Storage;

/// <summary>One JSON file per character record, named by content id, plus settings.json, all in one folder. Every load runs the migration
/// chain; a file from a newer build is left untouched and reported; a file that cannot be parsed is moved aside as .corrupt and reported;
/// every write goes to .tmp and is then moved into place. Problems accumulate for the settings page to show, each naming its file alone.
/// Nothing here throws for a folder or a file the system refuses: the plugin then runs on what it could read, and writes over nothing it
/// could not read.</summary>
public sealed class RecordStore(IFileSystem fs, string directory)
{
    private readonly List<string> _problems = [];
    private readonly HashSet<ulong> _seen = [];
    private readonly HashSet<ulong> _newer = [];
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    /// <summary>True once the folder could not be listed: every record may be one that was never read, so none is written this session.</summary>
    private bool _unlisted;

    public string Directory { get; } = directory;
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Adds a problem for the settings page, once however many times it happens: a failed edit on an unwritable folder is retried on
    /// every tick, and a page listing the same line forty times says nothing the first line did not. Returns the text as shown, with the
    /// folder taken out (Shorten), so the caller can log the same words.</summary>
    public string Report(string problem)
    {
        var shown = Shorten(problem);
        if (_reported.Add(shown)) _problems.Add(shown);
        return shown;
    }

    /// <summary>True when settings.json is from a newer build, or could not be read or moved aside; the file is then never overwritten.</summary>
    public bool SettingsReadOnly { get; private set; }

    /// <summary>True when this content id's file must not be written: it is from a newer build, it could not be read or moved aside, or the
    /// folder could not be listed.</summary>
    public bool IsReadOnly(ulong contentId)
    {
        if (_unlisted) return true;
        if (!_seen.Contains(contentId)) Load(contentId);
        return _newer.Contains(contentId);
    }

    public string PathFor(ulong contentId) => Path.Combine(Directory, contentId + ".json");
    private string SettingsPath => Path.Combine(Directory, "settings.json");

    public CharacterRecord? Load(ulong contentId)
    {
        _seen.Add(contentId);
        var path = PathFor(contentId);
        return fs.FileExists(path) ? LoadRecordFile(path) : null;
    }

    /// <summary>Every record in the folder, in content id order. A folder the system will not create or list gives no record and a problem,
    /// never an exception, so the plugin still loads.</summary>
    public IReadOnlyList<CharacterRecord> LoadAll()
    {
        IReadOnlyList<string> paths;
        try
        {
            fs.CreateDirectory(Directory);
            paths = fs.ListFiles(Directory, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _unlisted = true;
            Add($"the configuration folder could not be listed ({ex.Message}); no character is loaded or saved this session");
            return [];
        }
        var list = new List<CharacterRecord>();
        foreach (var path in paths)
        {
            if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!ulong.TryParse(Path.GetFileNameWithoutExtension(path), out _)) continue;
            if (LoadRecordFile(path) is { } record) list.Add(record);
        }
        return list.OrderBy(r => r.ContentId).ToList();
    }

    public void Save(CharacterRecord record)
    {
        if (IsReadOnly(record.ContentId))
        {
            Report(_unlisted
                ? $"{PathFor(record.ContentId)}: not saved because the configuration folder could not be listed"
                : $"{PathFor(record.ContentId)}: not saved because the file is from a newer build or could not be read");
            return;
        }
        WriteAtomic(PathFor(record.ContentId), JsonSerializer.Serialize(record, Json.Options));
    }

    public void Delete(ulong contentId)
    {
        if (IsReadOnly(contentId))
        {
            Report(_unlisted
                ? $"{PathFor(contentId)}: not deleted because the configuration folder could not be listed"
                : $"{PathFor(contentId)}: not deleted because the file is from a newer build or could not be read");
            return;
        }
        var path = PathFor(contentId);
        if (fs.FileExists(path)) fs.Delete(path);
    }

    public Settings LoadSettings()
    {
        if (!fs.FileExists(SettingsPath)) return new Settings();
        var root = ReadObject(SettingsPath);
        if (root == null) return new Settings();
        try
        {
            var stored = ReadSchemaVersion(root);
            if (stored == null) { Quarantine(SettingsPath, "schemaVersion is not a positive integer"); return new Settings(); }
            if (stored > Settings.CurrentSchemaVersion)
            {
                SettingsReadOnly = true;
                Report($"{SettingsPath}: schema {stored} is newer than this build's {Settings.CurrentSchemaVersion}; running on defaults and leaving the file untouched");
                return new Settings();
            }
            return MigrateSettings(root, stored.Value, Json.Options);
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            Quarantine(SettingsPath, ex.Message);
            return new Settings();
        }
    }

    public void SaveSettings(Settings settings)
    {
        if (SettingsReadOnly)
        {
            Report($"{SettingsPath}: not saved because the file is from a newer build or could not be read");
            return;
        }
        WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, Json.Options));
    }

    private CharacterRecord? LoadRecordFile(string path)
    {
        var root = ReadObject(path);
        if (root == null) return null;
        try
        {
            var stored = ReadSchemaVersion(root);
            if (stored == null) { Quarantine(path, "schemaVersion is not a positive integer"); return null; }
            if (stored > CharacterRecord.CurrentSchemaVersion)
            {
                if (ulong.TryParse(Path.GetFileNameWithoutExtension(path), out var id)) { _seen.Add(id); _newer.Add(id); }
                Add($"{path}: schema {stored} is newer than this build's {CharacterRecord.CurrentSchemaVersion}; left untouched");
                return null;
            }
            var record = MigrateRecord(root, stored.Value, Json.Options);
            _seen.Add(record.ContentId);
            return record;
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            Quarantine(path, ex.Message);
            return null;
        }
    }

    private JsonObject? ReadObject(string path)
    {
        string text;
        try
        {
            text = fs.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held by another program, or refused: it may hold a character's whole history, so it is never written over this session.
            Add($"{path}: unreadable ({ex.Message}); left untouched");
            Protect(path);
            return null;
        }
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? throw new JsonException("not a JSON object");
        }
        catch (Exception ex) when (IsMalformed(ex))
        {
            Quarantine(path, ex.Message);
            return null;
        }
    }

    /// <summary>A missing schemaVersion reads as 1; anything that is not a positive integer is null, which the callers treat as corrupt.
    /// Shared with the import planner.</summary>
    internal static int? ReadSchemaVersion(JsonObject root)
    {
        var node = root["schemaVersion"];
        if (node == null) return 1;
        return node is JsonValue value && value.TryGetValue<int>(out var n) && n >= 1 ? n : null;
    }

    /// <summary>The migration chain and the deserialize a record passes after its schema check: a file's at load, and each record of an
    /// import, so both paths read a record exactly the same way. Throws what IsMalformed names. The options are a parameter so the load path
    /// can keep Json.Options while the import planner passes its own stricter copy.</summary>
    internal static CharacterRecord MigrateRecord(JsonObject root, int stored, JsonSerializerOptions options)
    {
        for (var v = stored; v < CharacterRecord.CurrentSchemaVersion; v++) RecordMigrations.Steps[v - 1](root);
        root["schemaVersion"] = CharacterRecord.CurrentSchemaVersion;
        return root.Deserialize<CharacterRecord>(options) ?? throw new JsonException("empty record");
    }

    /// <summary>The same for the settings object: the settings file's at load, and an import's.</summary>
    internal static Settings MigrateSettings(JsonObject root, int stored, JsonSerializerOptions options)
    {
        for (var v = stored; v < Settings.CurrentSchemaVersion; v++) SettingsMigrations.Steps[v - 1](root);
        root["schemaVersion"] = Settings.CurrentSchemaVersion;
        return root.Deserialize<Settings>(options) ?? new Settings();
    }

    /// <summary>Everything the JSON node and serializer APIs throw for a malformed object. Shared with the import planner.</summary>
    internal static bool IsMalformed(Exception ex) =>
        ex is JsonException or InvalidOperationException or ArgumentException or FormatException or NotSupportedException;

    /// <summary>A file that cannot be parsed is moved aside, never deleted. When even the move is refused, it is left where it is and marked
    /// read-only for the session, so nothing is ever written over a file Mnemosyne could not read.</summary>
    private void Quarantine(string path, string why)
    {
        var to = path + ".corrupt";
        for (var n = 2; fs.FileExists(to); n++) to = path + ".corrupt-" + n;
        try
        {
            fs.Move(path, to);
            Add($"{path}: could not be read ({why}); moved to {to}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Add($"{path}: could not be read ({why}) or moved aside ({ex.Message}); left untouched");
            Protect(path);
        }
    }

    /// <summary>Marks a file Mnemosyne could not read as never to be written this session: the settings file through SettingsReadOnly, a
    /// record through the same mark a newer build's record gets, which IsReadOnly reads and the Tracker honours at login.</summary>
    private void Protect(string path)
    {
        if (path == SettingsPath) SettingsReadOnly = true;
        else if (ulong.TryParse(Path.GetFileNameWithoutExtension(path), out var id)) { _seen.Add(id); _newer.Add(id); }
    }

    private void WriteAtomic(string path, string text)
    {
        fs.CreateDirectory(Directory);
        AtomicFile.Write(fs, path, text);
    }

    /// <summary>A problem found while loading: always listed, since each names a different file.</summary>
    private void Add(string problem) => _problems.Add(Shorten(problem));

    /// <summary>The text with this store's folder taken out (FileNames.Hide): a file inside it is named alone, and the folder itself is "the
    /// configuration folder". The system's own messages are shortened too, since they often repeat the path.</summary>
    private string Shorten(string text) => FileNames.Hide(text, Directory, "the configuration folder");
}

/// <summary>Step v-1 upgrades a version-v object to v+1; every schema change appends one step.</summary>
public static class RecordMigrations
{
    public static readonly IReadOnlyList<Action<JsonObject>> Steps =
    [
        // 1 to 2: nothing to rewrite. The version marks that a "gate" value kind may appear, so an older build leaves the file alone
        // instead of moving it aside as corrupt.
        static _ => { },
    ];
}

/// <summary>Empty: the settings schema is still 1. Fields added since (the task adjustments, roles, My tasks) need no step, since a missing
/// field reads as empty and a build that does not know a field carries it through Settings.Extra.</summary>
public static class SettingsMigrations
{
    public static readonly IReadOnlyList<Action<JsonObject>> Steps = [];
}
