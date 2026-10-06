using System.Text.Json;
using System.Text.Json.Serialization;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Records;

/// <summary>One task the player adds by hand. Id is "mine:" and 12 random
/// hex characters (MyTaskList.NewId), generated when the task is made and never shown, so tasks from two PCs never share one. Name is the
/// player's own: not blank, and holding no "#", where ImGui starts a hidden id suffix; names need not be unique. Kind is "daily", "weekly", "once"
/// or "every", stored as a word rather than an enum, so a kind a later build adds loads here as a task this build cannot use instead of making
/// the settings file malformed. Hours is an "every" task's cooldown, 1 to 720, and null for the other kinds. Target is a daily,
/// weekly or one-off task's number of clicks, 2 to 99, and null for a plain task and for "every". For is "all", "role:&lt;role
/// id&gt;" or "picked"; Picked holds the picked characters' content ids, and is kept while For says otherwise, so picking them again is one
/// click. A null in a hand-edited file reads as "" or as nobody picked, and a missing "for" as every character; the import planner refuses
/// what IsWhole rejects instead.</summary>
public sealed class MyTask
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Once = "once";
    public const string Every = "every";
    public const string ForAll = "all";
    public const string ForPicked = "picked";
    public const string RolePrefix = "role:";
    public const int MinHours = 1;
    public const int MaxHours = 720;
    public const int MinTarget = 2;
    public const int MaxTarget = 99;

    private string _id = "";
    private string _name = "";
    private string _kind = "";
    private string _for = ForAll;
    private List<ulong> _picked = [];

    public string Id { get => _id; set => _id = value ?? ""; }
    public string Name { get => _name; set => _name = value ?? ""; }
    public string Kind { get => _kind; set => _kind = value ?? ""; }
    public int? Hours { get; set; }
    /// <summary>How many clicks a daily, weekly or one-off task is done at, 2 to 99; null for a plain one-tick task. Ignored on an "every"
    /// task, as Hours is on the other kinds.</summary>
    public int? Target { get; set; }
    public string For { get => _for; set => _for = value ?? ""; }
    public List<ulong> Picked { get => _picked; set => _picked = value ?? []; }

    /// <summary>Keys this build does not know, carried through a load and a save untouched, as the settings' own are.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>The role a task "for a role" follows; null for every other form, and for "role:" with no id.</summary>
    [JsonIgnore] public string? RoleId => For.StartsWith(RolePrefix, StringComparison.Ordinal) && For.Length > RolePrefix.Length ? For[RolePrefix.Length..] : null;

    /// <summary>A daily, weekly or one-off task with a number.</summary>
    [JsonIgnore] public bool IsCounted => Kind != Every && Target is not null;

    /// <summary>Everything but the id: a usable name, a known kind, an "every" length of 1 to 720 hours, a number of 2 to 99 or none on
    /// the other kinds, and a known "for". The settings page checks a draft with this before it adds or saves one. Nobody picked is
    /// allowed, and is for nobody.</summary>
    [JsonIgnore] public bool IsValid =>
        IsNameValid(Name)
        && Kind is Daily or Weekly or Once or Every
        && (Kind != Every || Hours is >= MinHours and <= MaxHours)
        && (Kind == Every || Target is null || Target is >= MinTarget and <= MaxTarget)
        && (For is ForAll or ForPicked || RoleId != null);

    public static string ForRole(string roleId) => RolePrefix + roleId;

    /// <summary>Not blank once trimmed, and no "#" anywhere, where ImGui starts a hidden id suffix.</summary>
    public static bool IsNameValid(string name) => name.Trim().Length > 0 && !name.Contains('#');

    /// <summary>A task the import accepts and the export keeps: not null, an id of the form MyTaskList.IsId accepts, and valid. The
    /// id's form keeps every My tasks item clear of the sheet-built catalog's ids.</summary>
    public static bool IsWhole(MyTask? task) => task is not null && MyTaskList.IsId(task.Id) && task.IsValid;

    /// <summary>Whether the character has this task at all. A task for a role follows the character's current role, so it
    /// moves with every assignment. A role deleted or unknown on this PC matches nobody, since RoleOf never returns one. A picked id whose
    /// character was forgotten matches nobody until a record with that id comes back.</summary>
    public bool IsFor(CharacterRecord record, Settings settings) => For switch
    {
        ForAll => true,
        ForPicked => Picked.Contains(record.ContentId),
        _ => RoleId is { } roleId && settings.RoleOf(record)?.Id == roleId,
    };

    /// <summary>What a tick at now stores. A daily or weekly tick stores the period it was made in, so it reads done until
    /// that period ends, a tick at 14:59 UTC included. A one-off stores no period; an "every" tick stores the end of its
    /// cooldown. A counted task's tick fills it to its number. Null for a task that is not valid.</summary>
    public ItemValue? TickValue(DateTimeOffset now)
    {
        if (!IsValid) return null;
        if (IsCounted) return new CounterValue(Target.GetValueOrDefault(), Target.GetValueOrDefault(), now);
        return Kind switch
        {
            Daily => new DoneValue(true, ResetSchedules.PeriodKey(ResetSchedules.Daily, now), now),
            Weekly => new DoneValue(true, ResetSchedules.PeriodKey(ResetSchedules.Weekly, now), now),
            Once => new DoneValue(true, null, now),
            _ => new TimerValue(now + TimeSpan.FromHours(Hours.GetValueOrDefault())),
        };
    }

    /// <summary>The count a counted task holds at now, for the value stored under it: the stored count while it is this period's, 0 from
    /// the task's own daily or weekly reset on (a one-off never resets), and never below 0 or above the number, so a number lowered below the
    /// clicks holds the number. The assembler applies the same rule to the row, so a click always adds to the count the row shows. 0 for a value
    /// of another shape, a plain tick included, and for a task that is not valid or not counted.</summary>
    public int CountAt(ItemValue? stored, DateTimeOffset now)
    {
        if (!IsValid || !IsCounted || stored is not CounterValue counter) return 0;
        var schedule = Kind switch { Daily => ResetSchedules.Daily, Weekly => ResetSchedules.Weekly, _ => null };
        if (schedule != null && ResetSchedules.BoundaryBetween(schedule, counter.ReadAt, now)) return 0;
        return Math.Clamp(counter.Current, 0, Target.GetValueOrDefault());
    }

    /// <summary>What a click on a counted task stores: the count held now (CountAt) plus step, 1 for a click that adds one and -1 for
    /// Take one back, kept between 0 and the number, read at now so the task's reset counts from this click. Null for a task that is not valid
    /// or not counted.</summary>
    public ItemValue? CountValue(ItemValue? stored, int step, DateTimeOffset now)
    {
        if (!IsValid || !IsCounted) return null;
        var target = Target.GetValueOrDefault();
        return new CounterValue(Math.Clamp(CountAt(stored, now) + step, 0, target), target, now);
    }

    /// <summary>The catalog item this task becomes: in the My tasks category's one group, at its place in the player's order, Manual.
    /// A daily or weekly task is a Done item on that reset; a one-off a Done item with none; an "every" task a Timer with none that reads "ready"
    /// once its deadline passes. A counted task is a Counter, done at its number, which the item carries.</summary>
    public ItemDefinition ToItem(int order) => new(
        Id, Category.MyTasks, MyTaskList.GroupName, Name.Trim(),
        Kind == Every ? ItemKind.Timer : IsCounted ? ItemKind.Counter : ItemKind.Done,
        Kind switch { Daily => ResetRule.Daily, Weekly => ResetRule.Weekly, _ => ResetRule.None },
        order, Freshness.Manual, ReadyWhenPassed: Kind == Every, Target: IsCounted ? Target : null);

    /// <summary>A copy of the definition without its id, for the settings page to edit, so the stored task changes only
    /// through MyTaskList.Edit inside a settings update.</summary>
    public MyTask Draft() => new() { Name = Name, Kind = Kind, Hours = Hours, Target = Target, For = For, Picked = [.. Picked] };

    /// <summary>Takes a valid draft's definition: the name trimmed, the length only for "every", the number only for the other kinds, and a list of
    /// its own. The id and any unknown key stay.</summary>
    internal void CopyFrom(MyTask draft)
    {
        Name = draft.Name.Trim();
        Kind = draft.Kind;
        Hours = draft.Kind == Every ? draft.Hours : null;
        Target = draft.Kind == Every ? null : draft.Target;
        For = draft.For;
        Picked = [.. draft.Picked];
    }
}

/// <summary>The settings' "myTasks" list, in the order the player made and moved them. A null entry, or a task
/// IsWhole rejects, which only a hand edit or a later build leaves, is kept in the file and never used: Usable skips it. Of two tasks with one
/// id the first wins. So the catalog's My tasks part never meets a duplicate.</summary>
public sealed class MyTaskList : List<MyTask>
{
    public const string IdPrefix = "mine:";

    /// <summary>The My tasks category's one group.</summary>
    public const string GroupName = "My tasks";

    public IEnumerable<MyTask> Usable
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var task in this)
            {
                if (MyTask.IsWhole(task) && seen.Add(task.Id)) yield return task;
            }
        }
    }

    /// <summary>How many hex characters follow the prefix.</summary>
    public const int IdLength = 12;

    /// <summary>A new id, "mine:" and the first 12 lower-case hex characters of a new GUID, so tasks made
    /// on two PCs never share one. Guid.NewGuid is a BCL call, as RoleList.Create's is; Core still reads no clock and no file.</summary>
    public static string NewId() => IdPrefix + Guid.NewGuid().ToString("N")[..IdLength];

    /// <summary>Whether an id has the form NewId gives: "mine:" and exactly 12 lower-case hex characters. No other id is a task's, a sheet row's
    /// included.</summary>
    public static bool IsId(string? id) =>
        id is not null && id.Length == IdPrefix.Length + IdLength && id.StartsWith(IdPrefix, StringComparison.Ordinal)
        && id[IdPrefix.Length..].All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    /// <summary>The usable task with this id; null for none, for "", and for an id this list does not use.</summary>
    public MyTask? Find(string? id) => string.IsNullOrEmpty(id) ? null : Usable.FirstOrDefault(task => task.Id == id);

    /// <summary>The catalog's My tasks part, one item per usable task, in the player's order.</summary>
    public IEnumerable<ItemDefinition> Items() => Usable.Select((task, order) => task.ToItem(order));

    /// <summary>Renames, changes the kind, the length or who the task is for, and keeps the id, so every tick stays with it.
    /// False, and nothing changes, when there is no such task or the draft is not valid.</summary>
    public bool Edit(string id, MyTask draft)
    {
        if (Find(id) is not { } task || !draft.IsValid) return false;
        task.CopyFrom(draft);
        return true;
    }

    /// <summary>One place up (-1) or down (1) among the usable tasks, the order every character's list shows. False at either end and
    /// for no such task.</summary>
    public bool Move(string id, int step)
    {
        var usable = Usable.ToList();
        var at = usable.FindIndex(task => task.Id == id);
        var to = at + step;
        if (at < 0 || to < 0 || to >= usable.Count) return false;
        var i = IndexOf(usable[at]);
        var j = IndexOf(usable[to]);
        (this[i], this[j]) = (this[j], this[i]);
        return true;
    }

    /// <summary>The definition goes, with every entry that shares its id. The ticks stay in the records, unread, like a
    /// retired catalog item's value.</summary>
    public void Delete(string id) => RemoveAll(task => task is not null && task.Id == id);
}
