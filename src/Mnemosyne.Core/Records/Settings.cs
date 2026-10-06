using System.Text.Json;
using System.Text.Json.Serialization;
using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Records;

/// <summary>Every setting with its default. Every default is safe for a stranger.</summary>
public sealed class Settings
{
    /// <summary>Neither the "tasks" map of All tasks, the "roles" list, nor the "myTasks" list needs a schema step. A missing one reads as empty, and a
    /// build that does not know the field carries it through a load and a save whole, in <see cref="Extra"/>. A bump would have made an older
    /// build run every setting on defaults on a rollback, and refuse a newer export whole, for a field a missing-reads-as-empty rule already
    /// handles.</summary>
    public const int CurrentSchemaVersion = 1;

    private TaskPreferences _tasks = new();
    private RoleList _roles = new();
    private MyTaskList _myTasks = new();

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public HashSet<string> CategoriesOff { get; set; } = new(StringComparer.Ordinal);
    public int StalenessDays { get; set; } = 7;
    public int EndingSoonDays { get; set; } = 3;
    public bool WeekliesCount { get; set; } = true;
    public bool LoginLine { get; set; } = true;
    public bool InfoBar { get; set; } = true;
    public bool RemindDailyReset { get; set; }
    public bool RemindEventEnding { get; set; }
    public bool RemindWondrousTails { get; set; }
    public bool RemindHouseVisit { get; set; }

    /// <summary>An hour before the daily and the weekly reset, one chat line naming the logged-in character's undone daily or weekly My
    /// tasks. Off by default; an older build carries it through its extension data, so there is no schema bump.</summary>
    public bool RemindMyTasksBeforeReset { get; set; }
    public int LiveReadSeconds { get; set; } = 5;
    public HashSet<string> IgnoredEverywhere { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Pin, priority, hide and snooze, per item id. Only the All tasks board reads it, never Assemble, so
    /// an adjustment is saved through Plugin.UpdateTasks and leaves the checklist cache's version where it is. A null in a hand-edited file reads
    /// as no adjustments; the import planner's stricter options refuse one instead.</summary>
    public TaskPreferences Tasks { get => _tasks; set => _tasks = value ?? new TaskPreferences(); }

    /// <summary>The player's roles, in the order they were made. Assemble reads them through RoleOf, so every edit
    /// goes through Plugin.UpdateSettings and moves the version the checklist cache keys on. A null in a hand-edited file reads as no roles; the
    /// import planner's stricter options refuse one instead, and the planner refuses a role with no id or no name.</summary>
    public RoleList Roles { get => _roles; set => _roles = value ?? new RoleList(); }

    /// <summary>The player's tasks, in the order made and moved. They are catalog input: every edit goes through
    /// Plugin.UpdateSettings, which moves the version MyTaskCatalog and the checklist cache key on, so the catalog's My tasks part is rebuilt and
    /// every checklist assembled again once. A null in a hand-edited file reads as no tasks; the import planner's stricter options
    /// refuse one instead, and the planner refuses a task MyTask.IsWhole rejects.</summary>
    public MyTaskList MyTasks { get => _myTasks; set => _myTasks = value ?? new MyTaskList(); }

    /// <summary>The role a character gets the first time Mnemosyne sees it: a role id, or null for none. A character already known
    /// is never changed by it. An older build carries it through its extension data, so there is no schema bump.</summary>
    public string? DefaultRole { get; set; }

    /// <summary>The default role's id when this PC has that role, else null: a deleted or unknown role means none, as a record's own role does.</summary>
    [JsonIgnore] public string? RoleForNewCharacters => Roles.Find(DefaultRole)?.Id;

    public bool FirstRunNoticeDismissed { get; set; }
    public bool MainWindowOpen { get; set; }

    public bool IsCategoryOn(Category category) => !CategoriesOff.Contains(category.ToString());

    /// <summary>The character's role, by the id its record stores. Null for no role and for an id this PC does not
    /// have: both track everything.</summary>
    public Role? RoleOf(CharacterRecord record) => Roles.Find(record.Role);

    /// <summary>A copy of the draft, added last, with a new random id (MyTaskList.NewId). Null, and nothing changes, when the draft is not valid.
    /// The draft itself is never stored, so the settings page can keep editing it.</summary>
    public MyTask? AddMyTask(MyTask draft)
    {
        if (!draft.IsValid) return null;
        var task = new MyTask { Id = MyTaskList.NewId() };
        task.CopyFrom(draft);
        MyTasks.Add(task);
        return task;
    }

    /// <summary>Keys this build does not know, carried through a load and a save untouched.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}
