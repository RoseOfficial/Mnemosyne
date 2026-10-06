using System.Text.Json;
using System.Text.Json.Serialization;
using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Records;

/// <summary>A named set of switches. Id is generated and never shown, so a record that stores it survives
/// a rename. Name is the player's own, unique among roles ignoring case (RoleList). Off holds what the role switches off: "category:&lt;Category&gt;"
/// for a whole category, or a group's own key, "group:&lt;Category&gt;/&lt;Group&gt;", the key the ignore lists use (ItemDefinition.GroupKey).
/// An item is off when its category or its group is in the set. A key for a group the catalog no longer has stays in the set and does
/// nothing. A group the catalog gains later is on until the player switches it off, unless its whole category is off. A null in a hand-edited
/// file reads as "" or as everything on; the import planner refuses one instead.</summary>
public sealed class Role
{
    private string _id = "";
    private string _name = "";
    private HashSet<string> _off = new(StringComparer.Ordinal);

    public string Id { get => _id; set => _id = value ?? ""; }
    public string Name { get => _name; set => _name = value ?? ""; }
    public HashSet<string> Off { get => _off; set => _off = value ?? new HashSet<string>(StringComparer.Ordinal); }

    /// <summary>Keys this build does not know, carried through a load and a save untouched, as the settings' and the record's own are.</summary>
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>The key a whole category is switched off under.</summary>
    public static string CategoryKey(Category category) => "category:" + category;

    public bool IsOff(ItemDefinition item) => _off.Contains(CategoryKey(item.Category)) || _off.Contains(item.GroupKey);

    public bool IsCategoryOn(Category category) => !_off.Contains(CategoryKey(category));

    /// <summary>A group is on while its category is on and its own key is not in the set.</summary>
    public bool IsGroupOn(Category category, string groupKey) => IsCategoryOn(category) && !_off.Contains(groupKey);

    /// <summary>Unticking a category switches it off whole, its groups with it. Ticking it switches it back on with every group the
    /// catalog has in it. Only the catalog's own groups' keys are touched, so a retired group's key stays.</summary>
    public void SetCategory(Category category, bool on, Catalog catalog)
    {
        if (!on)
        {
            _off.Add(CategoryKey(category));
            return;
        }
        _off.Remove(CategoryKey(category));
        foreach (var group in catalog.GroupsIn(category)) _off.Remove(group.Key);
    }

    /// <summary>A group inside a ticked category is switched on or off alone. Ticking one inside an unticked category ticks the
    /// category again with only that group on: every other group the catalog has in it is switched off by its own key.</summary>
    public void SetGroup(Category category, string groupKey, bool on, Catalog catalog)
    {
        if (!on)
        {
            _off.Add(groupKey);
            return;
        }
        if (!IsCategoryOn(category))
        {
            _off.Remove(CategoryKey(category));
            foreach (var group in catalog.GroupsIn(category))
            {
                if (group.Key != groupKey) _off.Add(group.Key);
            }
        }
        _off.Remove(groupKey);
    }
}

/// <summary>The settings' "roles" list, in the order the player made them. A null entry or a role with no id, which
/// only a hand edit leaves, is kept in the file and never used: Usable skips it, and every read of a role goes through Usable.</summary>
public sealed class RoleList : List<Role>
{
    /// <summary>The sidebar's Role menu's own line for no role.</summary>
    public const string NoneName = "None";

    /// <summary>What the table and the Role menu show for an id this PC does not have (Summariser.UnknownRole).</summary>
    public const string UnknownName = "unknown role";

    public IEnumerable<Role> Usable => this.Where(role => role is not null && role.Id.Length > 0);

    /// <summary>A role the import accepts and the export keeps: not null, with an id and a name that is not blank. Only a hand edit leaves one
    /// that is not.</summary>
    public static bool IsWhole(Role? role) => role is not null && role.Id.Length > 0 && role.Name.Trim().Length > 0;

    /// <summary>The role with this id; null for none, for "", and for an id this PC does not have.</summary>
    public Role? Find(string? id) => string.IsNullOrEmpty(id) ? null : Usable.FirstOrDefault(role => role.Id == id);

    /// <summary>A name is free when, trimmed, it is not blank, it is neither NoneName nor UnknownName, holds no "#"
    /// anywhere, and no other role has it, all ignoring case. The role being renamed does not count against itself. ImGui reads "#" as the
    /// start of a hidden id suffix on a MenuItem or Selectable label, so two differently-id'd names that
    /// agree up to their first "#" would display identically in the sidebar's Role submenu and the settings page's list.</summary>
    public bool IsNameFree(string name, string? exceptId = null)
    {
        var trimmed = name.Trim();
        return trimmed.Length > 0
            && !trimmed.Contains('#')
            && !string.Equals(trimmed, NoneName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(trimmed, UnknownName, StringComparison.OrdinalIgnoreCase)
            && !Usable.Any(role => role.Id != exceptId && string.Equals(role.Name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A role with everything on and a generated id, added last; null when the name is not free.</summary>
    public Role? Create(string name)
    {
        if (!IsNameFree(name)) return null;
        var role = new Role { Id = Guid.NewGuid().ToString("N"), Name = name.Trim() };
        Add(role);
        return role;
    }

    /// <summary>False, and nothing changes, when there is no such role or the name is not free.</summary>
    public bool Rename(string id, string name)
    {
        if (Find(id) is not { } role || !IsNameFree(name, id)) return false;
        role.Name = name.Trim();
        return true;
    }

    /// <summary>The records that name it keep the id and behave as no role, until a settings import brings it back.</summary>
    public void Delete(string id) => RemoveAll(role => role is not null && role.Id == id);
}
