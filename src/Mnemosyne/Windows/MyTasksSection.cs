using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Records;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>The settings page's My tasks section. The list shows every task in the player's own order, one line each with its kind and who
/// it is for. Selecting one loads it into the form and shows Up, Down and Delete beside the list; Delete asks first. One form serves both:
/// with no task selected it adds one, and with one selected it saves changes to it, or Cancel goes back to adding. It holds a name, a kind
/// (daily, weekly, one-off, or every so many hours or days), how many times a daily, weekly or one-off task is done (1 is a plain tick), and
/// who the task is for: every character, one role, or characters ticked from the list, where a picked id this PC no longer has reads "a
/// character not on this PC" until it is unticked. Every edit is one Plugin.UpdateSettings: saved, the catalog's My tasks part rebuilt, every
/// checklist assembled again once, and the info bar asked to follow on the next frame. The rules live in Core (MyTask, MyTaskList,
/// Settings.AddMyTask), where they are tested; this class only draws them. The form edits a copy (MyTask.Draft), and AddMyTask and
/// MyTaskList.Edit copy it again, so nothing typed here reaches the settings outside UpdateSettings. An import that replaces the settings
/// replaces the settings object, and the form is then cleared. Drawn inside the settings page's My tasks card, its title a section label,
/// its introduction dim, the form's heading secondary, its warning amber, its separator a hairline inside the card, and the picked
/// characters' ticks toggle switches under the checkboxes' own labels, so their ids.</summary>
public sealed class MyTasksSection(Plugin plugin)
{
    private const string DeleteTitle = "Delete task##delete-task";
    private const string RoleNotHere = "a role not on this PC (nobody)";
    /// <summary>An InputInt's "-"/"+" buttons are square at GetFrameHeight (font size plus FramePadding.Y twice), and its own field keeps
    /// FramePadding.X on each side of the digits, with ItemInnerSpacing.X between the field and each button. Starlight's FramePadding (8, 4)
    /// and ItemInnerSpacing (6, 4) are each 4 and 2 px wider than ImGui's own defaults (4, 3) and (4, 4), which a narrower 90 px was sized
    /// for, so it costs the field about 16 px (2 buttons taller by 2 px each, 2 gaps wider by 2 px each, 2 sides of the field's own padding
    /// wider by 4 px each) it does not have back, and "168" (the "every" length's three digits, hours) reads clipped at that width. 110 px,
    /// scaled by Blocks.S so it keeps its room at every UI scale, gives that room back with a little to spare.</summary>
    private const float NumberBoxWidth = 110f;
    private static readonly (string Label, string Kind)[] Kinds =
        [("Daily", MyTask.Daily), ("Weekly", MyTask.Weekly), ("One-off", MyTask.Once), ("Every", MyTask.Every)];

    private MyTask _draft = NewDraft();
    private int _length = 1;            // the "every" length as typed: hours, or days while _inDays
    private bool _inDays;
    private int _times = 1;             // a daily, weekly or one-off task's number as typed; 1 is a plain tick
    private string? _selected;          // the task the form edits; null while it adds one
    private string? _deleting;          // a task id waiting for the confirmation
    private bool _deleteOpened;
    private Settings? _settingsSeen;    // the settings object the form was last drawn over

    public void Draw()
    {
        using var section = ImRaii.PushId("my-tasks");
        // Plugin.ReplaceSettings, an import's, swaps the settings object, while every other edit changes the same one. A form left
        // on the old list is cleared, so Save changes never writes a draft made from it onto the new list.
        if (!ReferenceEquals(plugin.Settings, _settingsSeen))
        {
            _settingsSeen = plugin.Settings;
            Clear();
        }
        var tasks = plugin.Settings.MyTasks;
        Blocks.SectionLabel(plugin.Fonts.Label, "My tasks");
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextDim))
            ImGui.TextWrapped("Things the game cannot tell Mnemosyne, ticked by hand per character: in the character's own list, or beside its name under the task's All tasks line.");

        var usable = tasks.Usable.ToList();
        foreach (var task in usable)
        {
            using var id = ImRaii.PushId(task.Id);
            // The highlight stops inside the card; ImGui widens a Selectable past the region's right by half the item spacing.
            // This width assumes the card is drawn full width, as it is here.
            if (ImGui.Selectable($"{task.Name} · {KindWords(task)} · {ForWords(task)}", _selected == task.Id, ImGuiSelectableFlags.None,
                    new Vector2(ImGui.GetContentRegionAvail().X - Blocks.S(Blocks.CardPadding), 0f)))
                Load(task);
        }
        if (usable.Count == 0) ImGui.TextDisabled("No tasks yet.");
        if (tasks.Find(_selected) is { } selected) DrawSelected(selected, usable);
        else if (_selected != null) Clear();
        DrawForm();
        DrawDeleteModal();
    }

    /// <summary>Up, Down and Delete for the selected task. Up and Down move it one place among the tasks in use (MyTaskList.Move), the order every
    /// character's list and All tasks show; each is greyed at its end.</summary>
    private void DrawSelected(MyTask task, List<MyTask> usable)
    {
        var taskId = task.Id;
        var at = usable.FindIndex(t => t.Id == taskId);
        using var id = ImRaii.PushId(taskId);
        using (ImRaii.Disabled(at <= 0))
        {
            if (ImGui.SmallButton("Up")) plugin.UpdateSettings(s => s.MyTasks.Move(taskId, -1));
        }
        ImGui.SameLine();
        using (ImRaii.Disabled(at >= usable.Count - 1))
        {
            if (ImGui.SmallButton("Down")) plugin.UpdateSettings(s => s.MyTasks.Move(taskId, 1));
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Delete...")) _deleting = taskId;
    }

    /// <summary>The one form for adding and editing. Add task and Save changes stay greyed while the draft breaks a rule (MyTask.IsValid),
    /// and a line says which.</summary>
    private void DrawForm()
    {
        Blocks.Hairline();
        ImGui.TextColored(Palette.TextSecondary, _selected == null ? "Add a task" : "Edit the selected task");
        var name = _draft.Name;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.InputTextWithHint("Name##task-name", "e.g. FC chest", ref name, 64)) _draft.Name = name;

        ImGui.TextUnformatted("Kind:");
        foreach (var (label, kind) in Kinds)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(label, _draft.Kind == kind)) _draft.Kind = kind;
        }
        if (_draft.Kind == MyTask.Every)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Blocks.S(NumberBoxWidth));
            ImGui.InputInt("##length", ref _length, 1, 5);
            ImGui.SameLine();
            if (ImGui.RadioButton("hours", !_inDays)) _inDays = false;
            ImGui.SameLine();
            if (ImGui.RadioButton("days", _inDays)) _inDays = true;
            // Worked out in long, so a huge number of days cannot wrap round into a length that looks valid.
            var hours = _inDays ? _length * 24L : _length;
            _draft.Hours = hours is >= MyTask.MinHours and <= MyTask.MaxHours ? (int)hours : null;
            _draft.Target = null;
        }
        else
        {
            // How many clicks the task is done at. 1 is a plain one-tick task, stored as no number; anything outside 1 to 99 keeps the
            // draft invalid, and the line beside the buttons says so.
            ImGui.SameLine();
            ImGui.SetNextItemWidth(Blocks.S(NumberBoxWidth));
            // In a settings window narrower than about 575 px this row runs past the card's wrap edge; without this, ImGui's
            // own "times" label (drawn to the field's right) would wrap one character per line instead of overflowing on its one line.
            using (ImRaii.TextWrapPos(-1f)) ImGui.InputInt("times##target", ref _times, 1, 5);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Each click on the box adds one, and the task is done at this number. 1 is a plain tick.");
            _draft.Target = _times == 1 ? null : _times;
        }

        DrawFor();

        var valid = _draft.IsValid;
        using (ImRaii.Disabled(!valid))
        {
            if (_selected is { } editing)
            {
                if (ImGui.Button("Save changes"))
                {
                    var draft = _draft;
                    plugin.UpdateSettings(s => s.MyTasks.Edit(editing, draft));
                }
            }
            else if (ImGui.Button("Add task"))
            {
                var draft = _draft;
                plugin.UpdateSettings(s => s.AddMyTask(draft));
                Clear();
            }
        }
        if (_selected != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) Clear();
        }
        // Past the card's wrap edge this warning would wrap one character per line; disabling wrap keeps it on its one line.
        if (!valid) { ImGui.SameLine(); using (ImRaii.TextWrapPos(-1f)) ImGui.TextColored(Palette.Today, WhatIsMissing()); }
    }

    /// <summary>Who the task is for: every character, one role, or picked characters. A role that is later deleted leaves the task for
    /// nobody, and the combo says so until another role is picked.</summary>
    private void DrawFor()
    {
        var roles = plugin.Settings.Roles.Usable.ToList();
        ImGui.TextUnformatted("For:");
        ImGui.SameLine();
        if (ImGui.RadioButton("Every character", _draft.For == MyTask.ForAll)) _draft.For = MyTask.ForAll;
        ImGui.SameLine();
        using (ImRaii.Disabled(roles.Count == 0 && _draft.RoleId == null))
        {
            if (ImGui.RadioButton("A role", _draft.RoleId != null) && _draft.RoleId == null) _draft.For = MyTask.ForRole(roles[0].Id);
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Picked characters", _draft.For == MyTask.ForPicked)) _draft.For = MyTask.ForPicked;
        if (roles.Count == 0) ImGui.TextDisabled("Make a role in the Roles section to give a task to one role.");

        if (_draft.RoleId is { } roleId)
        {
            ImGui.SetNextItemWidth(200f);
            using var combo = ImRaii.Combo("Role##task-role", plugin.Settings.Roles.Find(roleId)?.Name ?? RoleNotHere);
            if (combo)
            {
                foreach (var role in roles)
                {
                    using var id = ImRaii.PushId(role.Id);
                    if (ImGui.Selectable(role.Name, role.Id == roleId)) _draft.For = MyTask.ForRole(role.Id);
                }
            }
        }
        if (_draft.For == MyTask.ForPicked) DrawPicked();
    }

    /// <summary>A box for every character this PC has, hidden ones included, then one for each picked id it no longer has (forgotten, or
    /// picked on another PC): ticked, reading "a character not on this PC", and removed by unticking it.</summary>
    private void DrawPicked()
    {
        using var indent = ImRaii.PushIndent();
        var records = plugin.Tracker.All;
        foreach (var record in records)
        {
            using var id = ImRaii.PushId(record.ContentId.ToString());
            var on = _draft.Picked.Contains(record.ContentId);
            // As wide as the card's content, so every character's track lines up at the card's right. This
            // width assumes the card is drawn full width; the indent above shifts the left edge only, so the assumption still holds.
            if (Blocks.Toggle(record.Label + (record.Hidden ? " (hidden)" : "") + "##pick", ref on, ImGui.GetContentRegionAvail().X - Blocks.S(Blocks.CardPadding)))
                Pick(record.ContentId, on);
        }
        foreach (var gone in _draft.Picked.Where(contentId => records.All(r => r.ContentId != contentId)).ToList())
        {
            using var id = ImRaii.PushId(gone.ToString());
            var on = true;
            if (Blocks.Toggle("a character not on this PC##gone", ref on, ImGui.GetContentRegionAvail().X - Blocks.S(Blocks.CardPadding)) && !on) Pick(gone, false);
        }
        if (records.Count == 0) ImGui.TextDisabled("Log into a character and it appears here.");
        else if (_draft.Picked.Count == 0) ImGui.TextDisabled("Nobody is picked yet, so the task is for nobody.");
    }

    private void Pick(ulong contentId, bool on)
    {
        _draft.Picked.Remove(contentId);
        if (on) _draft.Picked.Add(contentId);
    }

    /// <summary>Delete asks first. The task leaves every character's list; its ticks stay in the records, unread. Drawn at the section's own
    /// level, outside a task's pushed id, as the Roles section's modal is.</summary>
    private void DrawDeleteModal()
    {
        if (_deleting is not { } taskId) return;
        if (plugin.Settings.MyTasks.Find(taskId) is not { } task) { _deleting = null; _deleteOpened = false; return; }
        if (_deleteOpened && !ImGui.IsPopupOpen(DeleteTitle)) { _deleting = null; _deleteOpened = false; return; }
        if (!ImGui.IsPopupOpen(DeleteTitle)) { ImGui.OpenPopup(DeleteTitle); _deleteOpened = true; }
        using var modal = ImRaii.PopupModal(DeleteTitle);
        if (!modal) return;
        ImGui.TextWrapped($"Delete the task \"{task.Name}\"? It leaves every character's list. The ticks already made stay in the records, unread.");
        if (ImGui.Button("Delete"))
        {
            plugin.UpdateSettings(s => s.MyTasks.Delete(taskId));
            if (_selected == taskId) Clear();
            _deleting = null;
            _deleteOpened = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) { _deleting = null; _deleteOpened = false; ImGui.CloseCurrentPopup(); }
    }

    /// <summary>Loads a task into the form: a copy (MyTask.Draft), its length shown in days when it is a whole number of days.</summary>
    private void Load(MyTask task)
    {
        _selected = task.Id;
        _draft = task.Draft();
        _inDays = task.Hours is { } hours && hours % 24 == 0;
        _length = task.Hours is { } length ? (_inDays ? length / 24 : length) : 1;
        _times = task.Target ?? 1;
    }

    /// <summary>Back to adding: an empty daily task for every character.</summary>
    private void Clear()
    {
        _selected = null;
        _draft = NewDraft();
        _length = 1;
        _inDays = false;
        _times = 1;
    }

    private static MyTask NewDraft() => new() { Kind = MyTask.Daily, For = MyTask.ForAll };

    /// <summary>The first rule the draft breaks, in MyTask.IsValid's order.</summary>
    private string WhatIsMissing() =>
        _draft.Name.Contains('#') ? "a name cannot hold \"#\""
        : !MyTask.IsNameValid(_draft.Name) ? "give the task a name"
        : _draft.Kind == MyTask.Every && _draft.Hours == null ? "every 1 hour to 30 days"
        : _draft.Kind != MyTask.Every && _draft.Target is not null and not (>= MyTask.MinTarget and <= MyTask.MaxTarget) ? "1 to 99 times"
        : "pick a role";

    private static string KindWords(MyTask task) => task.Kind switch
    {
        MyTask.Daily => "daily" + Times(task),
        MyTask.Weekly => "weekly" + Times(task),
        MyTask.Once => "one-off" + Times(task),
        _ => task.Hours is { } hours && hours % 24 == 0 ? $"every {hours / 24} d" : $"every {task.Hours} h",
    };

    /// <summary>", 5 times" after a counted task's kind in the list; nothing for a plain one.</summary>
    private static string Times(MyTask task) => task.IsCounted ? $", {task.Target} times" : "";

    private string ForWords(MyTask task)
    {
        if (task.RoleId is { } roleId) return "for " + (plugin.Settings.Roles.Find(roleId)?.Name ?? RoleNotHere);
        if (task.For == MyTask.ForAll) return "for every character";
        // A picked id this PC no longer has (forgotten, or picked on another PC) is for nobody, so the count that matters is
        // how many picked ids this PC still has a record for; the rest are named, as DrawPicked already counts them individually.
        var here = plugin.Tracker.All;
        var count = task.Picked.Count(id => here.Any(r => r.ContentId == id));
        var gone = task.Picked.Count - count;
        return $"for {count} picked character{(count == 1 ? "" : "s")}" + (gone > 0 ? $" ({gone} not on this PC)" : "");
    }
}
