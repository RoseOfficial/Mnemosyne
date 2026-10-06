using System.Text.Json.Serialization;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Records;

/// <summary>A task's priority, which orders it within its group. Normal is the default and the zero value, so a
/// missing field reads as Normal. Stored lowercase: "high", "normal", "low".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))]
public enum TaskPriority
{
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("low")] Low,
}

/// <summary>One task's adjustments, for all characters at once. The defaults are where every task starts, and an entry back
/// at all four leaves the map.</summary>
public sealed record TaskPreference(bool Pinned = false, TaskPriority Priority = TaskPriority.Normal, bool Hidden = false, DateTimeOffset? SnoozedUntil = null)
{
    public static readonly TaskPreference Default = new();

    [JsonIgnore] public bool IsDefault => this == Default;

    /// <summary>Whether the snooze still holds at now. It ends exactly at its instant.</summary>
    public bool SnoozedAt(DateTimeOffset now) => SnoozedUntil is { } until && now < until;
}

/// <summary>The settings' "tasks" map, item id to that task's adjustments, stored as a plain JSON object keyed by
/// item id. It holds only what the player changed: each operation writes its own field and removes an entry left at all four defaults. An id the catalog
/// no longer has is kept and does nothing, so a returning event keeps its pin. A null entry in a hand-edited file, loaded or imported, reads as
/// the defaults.</summary>
public sealed class TaskPreferences : Dictionary<string, TaskPreference>
{
    public TaskPreferences() : base(StringComparer.Ordinal) { }

    /// <summary>The task's adjustments, or the defaults when it has none.</summary>
    public TaskPreference Get(string id) => TryGetValue(id, out var preference) && preference is not null ? preference : TaskPreference.Default;

    public void Pin(string id) => Set(id, Get(id) with { Pinned = true });

    public void Unpin(string id) => Set(id, Get(id) with { Pinned = false });

    public void SetPriority(string id, TaskPriority priority) => Set(id, Get(id) with { Priority = priority });

    public void Hide(string id) => Set(id, Get(id) with { Hidden = true });

    public void Unhide(string id) => Set(id, Get(id) with { Hidden = false });

    /// <summary>Until the next reset of the task's own schedule after now, which the board's line carries (TaskLine.NextReset). A task
    /// with no schedule, an ending event or a timer, snoozes until the next daily reset.</summary>
    public void Snooze(string id, DateTimeOffset? nextReset, DateTimeOffset now) =>
        Set(id, Get(id) with { SnoozedUntil = nextReset ?? ResetSchedules.NextReset(ResetSchedules.Daily, now) });

    /// <summary>Ends a snooze early (Wake, under the Hidden (n) toggle), and clears one that has passed (Board.Build's Expired).</summary>
    public void Wake(string id) => Set(id, Get(id) with { SnoozedUntil = null });

    private void Set(string id, TaskPreference preference)
    {
        if (preference.IsDefault) Remove(id);
        else this[id] = preference;
    }
}
