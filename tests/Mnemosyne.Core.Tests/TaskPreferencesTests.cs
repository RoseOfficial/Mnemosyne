using System.Text.Json;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

/// <summary>The adjustments map, its operations, and its place in the settings file.</summary>
public class TaskPreferencesTests
{
    private const string Id = "reset:roulette:1";

    [Fact]
    public void Each_operation_sets_its_own_field_and_an_entry_back_at_its_defaults_is_removed()
    {
        var tasks = new TaskPreferences();
        Assert.Equal(TaskPreference.Default, tasks.Get(Id));   // every task starts unpinned, at Normal, shown and awake

        tasks.Pin(Id);
        tasks.SetPriority(Id, TaskPriority.High);
        tasks.Hide(Id);
        tasks.Snooze(Id, Items.Now + TimeSpan.FromHours(3), Items.Now);
        Assert.Equal(new TaskPreference(true, TaskPriority.High, true, Items.Now + TimeSpan.FromHours(3)), tasks.Get(Id));

        tasks.Unpin(Id);
        tasks.SetPriority(Id, TaskPriority.Normal);
        tasks.Unhide(Id);
        Assert.Single(tasks);                                   // the snooze still holds the entry
        tasks.Wake(Id);
        Assert.Empty(tasks);                                    // the map holds only what was changed

        tasks.SetPriority(Id, TaskPriority.Low);
        tasks.Unpin(Id);                                        // unpinning an unpinned task leaves its priority alone
        Assert.Equal(TaskPriority.Low, Assert.Single(tasks).Value.Priority);
    }

    [Fact]
    public void A_snooze_is_saved_as_the_next_reset_it_is_given_or_the_next_daily_reset_when_the_task_has_none()
    {
        // Until the task's own next reset after the click, which the board's line carries (TaskLine.NextReset); an ending event or a
        // timer has no schedule and snoozes until the next daily reset, 15:00 UTC. Now is Sunday 12:00 UTC.
        var tasks = new TaskPreferences();
        var tuesday = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);
        var fifteen = new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero);
        tasks.Snooze("reset:tails", tuesday, Items.Now);
        tasks.Snooze("event:moonfire:2026", null, Items.Now);
        Assert.Equal(tuesday, tasks.Get("reset:tails").SnoozedUntil);
        Assert.Equal(fifteen, tasks.Get("event:moonfire:2026").SnoozedUntil);

        // One minute before the daily reset, a snooze lasts that minute, and ends exactly at its instant.
        tasks.Snooze("timer:venture:1", null, fifteen - TimeSpan.FromMinutes(1));
        Assert.Equal(fifteen, tasks.Get("timer:venture:1").SnoozedUntil);
        Assert.True(tasks.Get("timer:venture:1").SnoozedAt(fifteen - TimeSpan.FromTicks(1)));
        Assert.False(tasks.Get("timer:venture:1").SnoozedAt(fifteen));
    }

    [Fact]
    public void The_map_is_stored_as_tasks_keyed_by_item_id_with_lowercase_priorities_and_reads_back()
    {
        // "tasks" maps an item id to pinned, priority (high, normal or low), hidden and snoozedUntil, an instant or null.
        var settings = new Settings();
        settings.Tasks.Pin("reset:roulette:1");
        settings.Tasks.SetPriority("reset:tails", TaskPriority.High);
        settings.Tasks.Snooze("event:moonfire:2026", null, Items.Now);
        var text = JsonSerializer.Serialize(settings, Json.Options);

        using var json = JsonDocument.Parse(text);
        var tasks = json.RootElement.GetProperty("tasks");
        Assert.Equal(3, tasks.EnumerateObject().Count());
        Assert.True(tasks.GetProperty("reset:roulette:1").GetProperty("pinned").GetBoolean());
        Assert.Equal("normal", tasks.GetProperty("reset:roulette:1").GetProperty("priority").GetString());
        Assert.Equal("high", tasks.GetProperty("reset:tails").GetProperty("priority").GetString());
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero), tasks.GetProperty("event:moonfire:2026").GetProperty("snoozedUntil").GetDateTimeOffset());
        Assert.False(tasks.GetProperty("reset:roulette:1").TryGetProperty("snoozedUntil", out _));   // a null is not written (Json.Options)
        Assert.False(tasks.GetProperty("reset:roulette:1").TryGetProperty("isDefault", out _));      // nor the computed flag

        var back = JsonSerializer.Deserialize<Settings>(text, Json.Options)!;
        Assert.Equal(3, back.Tasks.Count);
        foreach (var (id, preference) in settings.Tasks) Assert.Equal(preference, back.Tasks.Get(id));
        Assert.Equal(TaskPriority.Low, JsonSerializer.Deserialize<TaskPreference>("""{"priority":"low"}""", Json.Options)!.Priority);
    }
}
