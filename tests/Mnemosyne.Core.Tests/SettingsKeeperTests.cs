using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

/// <summary>An adjustment is saved, and the version the checklist cache keys on does not move. A save that fails is reported and
/// logged, as it was when Plugin held the save.</summary>
public class SettingsKeeperTests
{
    private const string Dir = @"C:\cfg\Mnemosyne";

    [Fact]
    public void An_adjustment_is_saved_without_moving_the_cache_version_and_changes_no_characters_checklist()
    {
        // No adjustment is an input to any checklist, so none may move the version ChecklistCache keys on; pinning a task on a
        // fourteen-character account must not re-assemble fourteen checklists. The badge and each character's own view are untouched.
        var fs = new InMemoryFileSystem();
        var keeper = new SettingsKeeper(new RecordStore(fs, Dir), (_, _) => { });
        var cache = new ChecklistCache();
        var aria = SummariserTests.Aria();
        var first = cache.Get(Items.Catalog(), aria, keeper.Settings, 1, keeper.Version, Items.Now);

        keeper.UpdateTasks(tasks => tasks.Pin(Items.Allowances.Id));
        keeper.UpdateTasks(tasks => tasks.Hide(Items.Cactpot.Id));
        keeper.UpdateTasks(tasks => tasks.Snooze(Items.Stickers.Id, null, Items.Now));

        Assert.Equal(0, keeper.Version);
        Assert.Equal(3, keeper.TasksVersion);   // All tasks keeps its own counter, so a caller can cache the board without touching Version
        Assert.Same(first, cache.Get(Items.Catalog(), aria, keeper.Settings, 1, keeper.Version, Items.Now));   // nothing re-assembled
        Assert.Equal(ChecklistText.Render(first, aria, Items.Now),
            ChecklistText.Render(Assembler.Assemble(Items.Catalog(), aria, keeper.Settings, Items.Now), aria, Items.Now));   // the same five to do
        var saved = new RecordStore(fs, Dir).LoadSettings();                                                      // and yet saved
        Assert.True(saved.Tasks.Get(Items.Allowances.Id).Pinned);
        Assert.True(saved.Tasks.Get(Items.Cactpot.Id).Hidden);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero), saved.Tasks.Get(Items.Stickers.Id).SnoozedUntil);

        // Every other edit is one Assemble may read: the version moves and the checklist is assembled again, as it always has; so does an
        // import's settings.
        keeper.Update(s => s.WeekliesCount = false);
        Assert.Equal(1, keeper.Version);
        Assert.Equal(3, keeper.TasksVersion);   // an ordinary edit does not move it
        Assert.NotSame(first, cache.Get(Items.Catalog(), aria, keeper.Settings, 1, keeper.Version, Items.Now));
        keeper.Replace(new Settings());
        Assert.Equal(2, keeper.Version);
        Assert.Equal(3, keeper.TasksVersion);   // nor does an import's settings
    }

    [Fact]
    public void A_failed_save_is_reported_once_and_logged_and_the_adjustment_stays_in_memory()
    {
        // The behaviour Plugin.UpdateSettings had, moved here with the save: a full disk or a read-only folder is reported on the settings page
        // and logged, the edit stays in memory for the next save, and an adjustment still leaves the version where it is.
        var fs = new InMemoryFileSystem();
        var store = new RecordStore(fs, Dir);
        var logged = new List<string>();
        var keeper = new SettingsKeeper(store, (_, message) => logged.Add(message));
        fs.FailWrites = true;

        keeper.UpdateTasks(tasks => tasks.Pin(Items.Roulette.Id));

        Assert.Equal("settings: not saved (disk full)", Assert.Single(store.Problems));
        Assert.Equal("settings: not saved (disk full)", Assert.Single(logged));
        Assert.Equal(0, keeper.Version);
        Assert.True(keeper.Settings.Tasks.Get(Items.Roulette.Id).Pinned);
    }

    [Fact]
    public void A_failed_save_logs_the_words_the_settings_page_shows_with_the_folder_taken_out()
    {
        var fs = new InMemoryFileSystem { FailWrites = true, WriteFailure = "cannot write " + Path.Combine(Dir, "settings.json.tmp") };
        var store = new RecordStore(fs, Dir);
        var logged = new List<string>();
        var keeper = new SettingsKeeper(store, (_, message) => logged.Add(message));

        keeper.Update(s => s.StalenessDays = 9);

        Assert.Equal("settings: not saved (cannot write settings.json.tmp)", Assert.Single(store.Problems));
        Assert.Equal(store.Problems, logged);
    }
}
