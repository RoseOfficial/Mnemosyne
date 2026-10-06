using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Storage;
using Mnemosyne.Core.Time;
using Mnemosyne.Look;

namespace Mnemosyne;

/// <summary>Export and import on the settings page. Export writes one bundle (Core's Bundle) to the path the player picks, through
/// AtomicFile, and asks before replacing a file whenever Bundle.NeedsConfirmation says so, because Dalamud's file dialog cannot. While this
/// PC's settings file could not be read or is from a newer build the export leaves the settings out. Import reads the file the player picks
/// and shows an import plan (Core's ImportPlanner) before anything is applied. It plans again at "Apply import" with the state of that
/// moment, then applies through Tracker.Import and, when asked, Plugin.ReplaceSettings. A file is planned when picked, when the settings
/// box changes and at the click, never on every frame. Nothing is sent anywhere: the only file written is the one the player chose. Both
/// dialogs open in Documents every time. Dalamud's own file dialog manager never asks its own confirm-overwrite dialog, and under the
/// ".json" filter a typed name's last extension is replaced by ".json", so the path the callback gets always ends in ".json".</summary>
public sealed class Transfer(Plugin plugin, IFileSystem fs, IClock clock)
{
    private const string Filter = ".json";
    private static readonly string Documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private readonly FileDialogManager _dialogs = new();
    private string? _overwrite;          // an export path that already exists, waiting for the player's answer
    private string? _importText;         // the file picked for import, kept until the player applies or closes it
    private ImportPlan? _plan;
    private bool _replaceSettings;
    private IReadOnlyList<string> _lines = [];

    /// <summary>Every frame, from UiBuilder.Draw, the settings window open or not: the open dialog, if any, draws and runs its callback here.</summary>
    public void DrawDialogs() => _dialogs.Draw();

    /// <summary>The settings page's section: two buttons, the privacy line beside them, then the pending question or plan, or the last result.</summary>
    public void Draw()
    {
        // The settings page's Data card, drawn by the page around this section.
        Blocks.SectionLabel(plugin.Fonts.Label, "Data");
        if (ImGui.Button("Export..."))
            _dialogs.SaveFileDialog("Export Mnemosyne data", Filter, Bundle.DefaultFileName(clock.UtcNow), Filter, OnExportPicked, Documents);
        ImGui.SameLine();
        if (ImGui.Button("Import..."))
            _dialogs.OpenFileDialog("Import Mnemosyne data", Filter, (ok, paths) => OnImportPicked(ok, paths.FirstOrDefault()), 1, Documents);
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextDim))
            ImGui.TextWrapped("The file holds every character's name, world, content id and progress, and your settings. Mnemosyne writes it only where you choose and never sends it anywhere.");

        if (_overwrite is { } path)
        {
            ImGui.TextWrapped($"{Path.GetFileName(path)} already exists. Replace it?");
            if (ImGui.Button("Replace")) { _overwrite = null; Export(path); }
            ImGui.SameLine();
            if (ImGui.Button("Keep the old file")) { _overwrite = null; _lines = ["Export cancelled; the existing file was kept."]; }
        }
        if (_plan is { } plan)
        {
            foreach (var line in plan.Summary()) ImGui.TextWrapped(line);
            if (plan.Refusal == null)
            {
                if (Blocks.Toggle("Also replace my settings", ref _replaceSettings)) _plan = Plan();
                if (ImGui.Button("Apply import")) Apply();
                ImGui.SameLine();
            }
            if (ImGui.Button("Close")) Clear();
        }
        foreach (var line in _lines) ImGui.TextWrapped(line);
    }

    private void OnExportPicked(bool ok, string path)
    {
        if (!ok) return;
        Clear();
        if (Bundle.NeedsConfirmation(fs, path)) _overwrite = path;
        else Export(path);
    }

    private void Export(string path)
    {
        try
        {
            // A settings file that could not be read or is from a newer build means this PC runs on
            // defaults, which an export must not carry to another PC.
            var settings = plugin.Store.SettingsReadOnly ? null : plugin.Settings;
            AtomicFile.Write(fs, path, Bundle.Export(plugin.Tracker.All, settings, plugin.PluginVersion, plugin.GameVersion, clock.UtcNow));
            var what = settings != null ? "and the settings" : "without the settings, because this PC's settings file could not be read or is from a newer build,";
            _lines = [$"Exported {plugin.Tracker.All.Count} character(s) {what} to {Path.GetFileName(path)}."];
            Plugin.Log.Information("export written: {Count} record(s)", plugin.Tracker.All.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lines = [$"Export failed: {FileNames.Hide(ex.Message, Path.GetDirectoryName(path), "the chosen folder")}"];
            Plugin.Log.Error(ex, "export failed");
        }
    }

    private void OnImportPicked(bool ok, string? path)
    {
        if (!ok || string.IsNullOrEmpty(path)) return;
        Clear();
        try
        {
            _importText = fs.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _lines = [$"Import failed: {FileNames.Hide(ex.Message, Path.GetDirectoryName(path), "the chosen folder")}"];
            return;
        }
        _plan = Plan();
    }

    private ImportPlan Plan() => ImportPlanner.Plan(_importText!,
        new ImportContext(plugin.Tracker.All, plugin.Store.IsReadOnly, plugin.Tracker.Current?.ContentId, plugin.Settings, plugin.Store.SettingsReadOnly),
        _replaceSettings);

    /// <summary>Plans again with the state of this moment, so a character switch since the file was picked is honoured, then applies.</summary>
    private void Apply()
    {
        var plan = Plan();
        Clear();
        if (plan.Refusal != null) { _lines = plan.Summary(); return; }
        var applied = plugin.Tracker.Import(plan.ToApply);
        if (plan.Settings is { } settings) plugin.ReplaceSettings(settings);
        var settingsWord = plan.Settings != null ? "replaced" : "kept";
        _lines = [$"Import applied: {applied} character(s) added or replaced; settings {settingsWord}."];
        Plugin.Log.Information("import applied: {Applied} record(s) added or replaced; settings {Settings}", applied, settingsWord);
    }

    private void Clear()
    {
        _overwrite = null;
        _importText = null;
        _plan = null;
        _replaceSettings = false;
        _lines = [];
    }
}
