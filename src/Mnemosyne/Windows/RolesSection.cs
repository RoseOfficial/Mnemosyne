using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>The settings page's Roles section. New role names a role, which starts with everything on and is selected. The list selects a
/// role. Rename and Delete act on the selected one. Delete asks first, naming how many characters have the role; they then track
/// everything, as with no role, while their records keep its id. For the selected role, a tree of the eight categories (My tasks included),
/// each with the groups the catalog has in it (Catalog.GroupsIn), has a tick box on every one. Every edit goes through Edit:
/// Plugin.UpdateSettings, so it is saved, every checklist is assembled again once, and the info bar follows on the next frame
/// (Attention.RefreshSoon, raised inside UpdateSettings itself). The rules live in Core (Role, RoleList), where they are tested; this class
/// only draws them. Drawn inside the settings page's Roles card, its title a section label, its introduction dim, its warning amber, its
/// separator a hairline inside the card, and its ticks toggle switches under the checkboxes' own labels, so their ids.</summary>
public sealed class RolesSection(Plugin plugin)
{
    private const string DeleteTitle = "Delete role##delete-role";

    private string _newName = "";
    private string _rename = "";
    private string? _selected;          // the selected role's id
    private string? _deleting;          // a role id waiting for the confirmation
    private bool _deleteOpened;

    public void Draw()
    {
        var roles = plugin.Settings.Roles;
        Blocks.SectionLabel(plugin.Fonts.Label, "Roles");
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextDim))
            ImGui.TextWrapped("A role switches categories and groups off for the characters that have it: their rows stay in their own list, dimmed, and are never counted. Give a character its role from its right-click menu in the sidebar, or tick several in All characters and use Give role.");

        // The default role: what a character gets the first time Mnemosyne sees it. A deleted role reads as None.
        var forNew = roles.Find(plugin.Settings.DefaultRole);
        ImGui.SetNextItemWidth(180f);
        using (var combo = ImRaii.Combo("New characters get", forNew?.Name ?? RoleList.NoneName))
        {
            if (combo)
            {
                if (ImGui.Selectable(RoleList.NoneName, forNew == null)) plugin.UpdateSettings(s => s.DefaultRole = null);
                foreach (var role in roles.Usable)
                {
                    using var id = ImRaii.PushId(role.Id);
                    if (ImGui.Selectable(role.Name, forNew?.Id == role.Id)) plugin.UpdateSettings(s => s.DefaultRole = role.Id);
                }
            }
        }

        ImGui.SetNextItemWidth(180f);
        // Enter creates the role, the same as clicking New role, once the name is free.
        var enterPressed = ImGui.InputTextWithHint("##new-role", "a new role's name", ref _newName, 32, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        var free = roles.IsNameFree(_newName);
        if (enterPressed && free) CreateNewRole();
        using (ImRaii.Disabled(!free))
        {
            if (ImGui.Button("New role")) CreateNewRole();
        }
        // Past the card's wrap edge this warning would wrap one character per line; disabling wrap keeps it on its one line.
        if (!free && _newName.Trim().Length > 0) { ImGui.SameLine(); using (ImRaii.TextWrapPos(-1f)) ImGui.TextColored(Palette.Today, "that name is taken or reserved"); }

        void CreateNewRole()
        {
            var name = _newName;
            Role? created = null;
            Edit(s => created = s.Roles.Create(name));
            if (created != null) { _selected = created.Id; _rename = created.Name; _newName = ""; }
        }

        foreach (var role in roles.Usable)
        {
            using var id = ImRaii.PushId(role.Id);
            // The highlight stops inside the card; ImGui widens a Selectable past the region's right by half the item spacing.
            // This width assumes the card is drawn full width, as it is here.
            if (ImGui.Selectable(role.Name, _selected == role.Id, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X - Blocks.S(Blocks.CardPadding), 0f)))
            {
                _selected = role.Id;
                _rename = role.Name;
            }
        }
        if (roles.Find(_selected) is { } selected) DrawSelected(selected);
        else _selected = null;
        DrawDeleteModal();
    }

    /// <summary>The selected role: its rename box, Rename, Delete, and the tick tree. Unticking a category switches its groups off; ticking one
    /// group inside an unticked category ticks the category again with only that group on (Role.SetCategory, Role.SetGroup).</summary>
    private void DrawSelected(Role role)
    {
        var roleId = role.Id;
        using var id = ImRaii.PushId(roleId);
        Blocks.Hairline();
        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##rename", role.Name, ref _rename, 32);
        ImGui.SameLine();
        var name = _rename;
        using (ImRaii.Disabled(name.Trim() == role.Name || !plugin.Settings.Roles.IsNameFree(name, roleId)))
        {
            if (ImGui.Button("Rename")) Edit(s => s.Roles.Rename(roleId, name));
        }
        ImGui.SameLine();
        if (ImGui.Button("Delete...")) _deleting = roleId;

        ImGui.TextDisabled("Switch off what this role leaves out. Switching on one group inside a category that is off switches the category back on, with only that group on.");
        foreach (var category in Enum.GetValues<Category>())
        {
            using var categoryId = ImRaii.PushId(category.ToString());
            var on = role.IsCategoryOn(category);
            // The toggle is an item of the frame's height with no text, so the line is aligned to the frame first, as the checkbox did itself,
            // and the category's tree node after it sits level with its track.
            ImGui.AlignTextToFramePadding();
            if (Blocks.Toggle("##category", ref on)) Edit(s => s.Roles.Find(roleId)?.SetCategory(category, on, plugin.Catalog));
            ImGui.SameLine();
            using var node = ImRaii.TreeNode(category.Label());
            if (!node) continue;
            foreach (var group in plugin.Catalog.GroupsIn(category))
            {
                using var groupId = ImRaii.PushId(group.Key);
                var groupOn = role.IsGroupOn(category, group.Key);
                // As wide as the card's content, so every group's track lines up at the card's right. This
                // width assumes the card is drawn full width, as it is here.
                if (Blocks.Toggle(group.Name, ref groupOn, ImGui.GetContentRegionAvail().X - Blocks.S(Blocks.CardPadding)))
                    Edit(s => s.Roles.Find(roleId)?.SetGroup(category, group.Key, groupOn, plugin.Catalog));
            }
        }
    }

    /// <summary>Delete asks first, naming how many characters have the role, hidden ones included. No record is written: the characters keep
    /// its id and behave as no role. Drawn at the section's own level, outside any pushed id, as MainWindow's forget modal is.</summary>
    private void DrawDeleteModal()
    {
        if (_deleting is not { } roleId) return;
        if (plugin.Settings.Roles.Find(roleId) is not { } role) { _deleting = null; _deleteOpened = false; return; }
        if (_deleteOpened && !ImGui.IsPopupOpen(DeleteTitle)) { _deleting = null; _deleteOpened = false; return; }
        if (!ImGui.IsPopupOpen(DeleteTitle)) { ImGui.OpenPopup(DeleteTitle); _deleteOpened = true; }
        using var modal = ImRaii.PopupModal(DeleteTitle);
        if (!modal) return;
        var count = plugin.Tracker.All.Count(r => r.Role == roleId);
        // A task "for a role" follows the role's id, not a saved character list, so deleting the role leaves it for nobody
        // until it is given another role (MyTask.IsFor). The settings page already shows this as "for a role not on this PC (nobody)"; this
        // names it here too, where the delete actually happens.
        var tasks = plugin.Settings.MyTasks.Usable.Count(t => t.RoleId == roleId);
        ImGui.TextWrapped($"Delete the role \"{role.Name}\"? {count} character{(count == 1 ? " has" : "s have")} it, and will track everything again, as with no role, and show as 'unknown role' until given another role or None" +
            (tasks > 0 ? $", and {tasks} My task{(tasks == 1 ? "" : "s")} for it will be for nobody until given another role." : "."));
        if (ImGui.Button("Delete"))
        {
            Edit(s => s.Roles.Delete(roleId));
            if (_selected == roleId) _selected = null;
            _deleting = null;
            _deleteOpened = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) { _deleting = null; _deleteOpened = false; ImGui.CloseCurrentPopup(); }
    }

    /// <summary>Every role edit: saved through Plugin.UpdateSettings, which moves the version the checklist cache keys on and itself asks the
    /// info bar to follow on the next frame — so this method does not raise it a second time.</summary>
    private void Edit(Action<Settings> change) => plugin.UpdateSettings(change);
}
