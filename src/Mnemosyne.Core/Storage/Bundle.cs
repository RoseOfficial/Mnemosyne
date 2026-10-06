using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Storage;

/// <summary>The export bundle. One JSON file holds every record, hidden ones included, and the
/// settings less the two machine-local fields. Each object keeps its own schemaVersion, so the import runs each through the migration chain
/// and newer-version guard a file load uses. The file is written only where the player picks, and nothing is sent anywhere.</summary>
public static class Bundle
{
    public const string Format = "mnemosyne-export";

    /// <summary>The bundle's own shape. A later build that changes it raises this, and an older build then refuses the whole file.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The settings that describe this PC rather than its characters: never exported, and never taken from a file.</summary>
    private static readonly string[] MachineLocal = ["firstRunNoticeDismissed", "mainWindowOpen"];

    /// <summary>The whole bundle as text: the stamps, the settings, then every record in content-id order, each as the record store would
    /// write it, keys this build does not know included. Null settings leave them out: the caller passes null while this PC's settings file
    /// could not be read or is from a newer build, because what it runs on then is defaults, and a file carrying them would reset another
    /// PC's settings.</summary>
    public static string Export(IEnumerable<CharacterRecord> records, Settings? settings, string pluginVersion, string gameVersion, DateTimeOffset now)
    {
        var list = new JsonArray();
        foreach (var record in records.OrderBy(r => r.ContentId)) list.Add(JsonSerializer.SerializeToNode(record, Json.Options));
        var root = new JsonObject
        {
            ["format"] = Format,
            ["exportSchemaVersion"] = CurrentSchemaVersion,
            ["pluginVersion"] = pluginVersion,
            ["gameVersion"] = gameVersion,
            ["exportedAt"] = now,
        };
        if (settings != null)
        {
            var settingsNode = JsonSerializer.SerializeToNode(settings, Json.Options)!.AsObject();
            foreach (var key in MachineLocal) settingsNode.Remove(key);
            // A role the import would refuse (a null entry, no id, a blank name), which only a hand edit leaves and
            // Assemble never uses, is left out, so this PC's own export is never refused for it. The whole roles keep their order; this PC's
            // own list is not changed.
            settingsNode["roles"] = JsonSerializer.SerializeToNode(settings.Roles.Where(RoleList.IsWhole).ToList(), Json.Options);
            // A task the import would refuse, which only a hand edit or a later build leaves and no checklist ever
            // uses, is left out the same way.
            settingsNode["myTasks"] = JsonSerializer.SerializeToNode(settings.MyTasks.Where(MyTask.IsWhole).ToList(), Json.Options);
            root["settings"] = settingsNode;
        }
        root["records"] = list;
        return root.ToJsonString(Json.Options);
    }

    /// <summary>The name the save dialog offers, stamped to the second in UTC, as the dump files are, so a second export never offers the
    /// first one's name.</summary>
    public static string DefaultFileName(DateTimeOffset now) => "mnemosyne-" + now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";

    /// <summary>Whether writing the export to this path would replace a file. Dalamud's save dialog never asks, so the settings page asks
    /// the player whenever this is true and writes only on the player's answer.</summary>
    public static bool NeedsConfirmation(IFileSystem fs, string path) => fs.FileExists(path);
}
