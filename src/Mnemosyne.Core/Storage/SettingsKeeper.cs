using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Storage;

/// <summary>The settings in memory, their save, and the version the checklist cache keys on, moved out of Plugin so
/// a test can hold the rule. Update is every edit but an All tasks adjustment: it saves and moves Version, so every cached checklist is
/// assembled again. UpdateTasks is an All tasks adjustment, which no checklist reads: it saves the same file and leaves Version where it is, so
/// a pin on a fourteen-character account re-assembles nothing. Replace is an import's settings, and moves Version as an edit does. A save that
/// fails is reported once through the store and handed to the log, and the edit stays in memory.</summary>
public sealed class SettingsKeeper
{
    private readonly RecordStore _store;
    private readonly Action<Exception, string> _logError;

    public SettingsKeeper(RecordStore store, Action<Exception, string> logError)
    {
        _store = store;
        _logError = logError;
        Settings = store.LoadSettings();
    }

    public Settings Settings { get; private set; }

    /// <summary>What ChecklistCache keys on for the settings: moved by Update and Replace, never by UpdateTasks.</summary>
    public int Version { get; private set; }

    /// <summary>Moved by UpdateTasks alone, so a caller of Plugin.BoardFor can tell an adjustment from
    /// every other reason a frame might redraw, without touching Version and re-assembling a checklist no adjustment reads.</summary>
    public int TasksVersion { get; private set; }

    public void Update(Action<Settings> change)
    {
        change(Settings);
        Save();
        Version++;
    }

    public void UpdateTasks(Action<TaskPreferences> change)
    {
        change(Settings.Tasks);
        Save();
        TasksVersion++;
    }

    /// <summary>An import that replaces the settings. The planner has already kept this PC's two machine-local fields.</summary>
    public void Replace(Settings settings)
    {
        Settings = settings;
        Update(_ => { });
    }

    private void Save()
    {
        try
        {
            _store.SaveSettings(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The log gets the words the settings page shows, the folder taken out.
            _logError(ex, _store.Report($"settings: not saved ({ex.Message})"));
        }
    }
}
