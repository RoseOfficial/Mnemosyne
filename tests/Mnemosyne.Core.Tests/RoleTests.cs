using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The role, the settings' list of them, the tick rule, and the catalog's groups the tick tree reads.</summary>
public class RoleTests
{
    [Fact]
    public void A_role_switches_off_a_whole_category_or_one_group_by_the_keys_the_ignore_lists_use()
    {
        // An entry is "category:<Category>" or the group's own key, ItemDefinition.GroupKey; an item is off when either is in the set.
        var role = new Role { Id = "r1", Name = "Mule" };
        Assert.False(role.IsOff(Items.Roulette));                                   // a new role has everything on

        role.Off.Add(Role.CategoryKey(Category.Events));
        role.Off.Add(Items.Roulette.GroupKey);
        Assert.Equal("category:Events", Role.CategoryKey(Category.Events));
        Assert.Equal("group:Resets/Daily", Items.Roulette.GroupKey);

        Assert.True(role.IsOff(Items.Moonfire));                                    // the category
        Assert.True(role.IsOff(Items.Starlight));
        Assert.True(role.IsOff(Items.Roulette));                                    // the group
        Assert.True(role.IsOff(Items.Cactpot));
        Assert.False(role.IsOff(Items.Stickers));                                   // Resets "Weekly": another group of the same category
        Assert.False(role.IsOff(Items.Venture));
        Assert.False(role.IsCategoryOn(Category.Events));
        Assert.True(role.IsCategoryOn(Category.Resets));
        Assert.False(role.IsGroupOn(Category.Resets, Items.Roulette.GroupKey));
        Assert.True(role.IsGroupOn(Category.Resets, Items.Stickers.GroupKey));
        Assert.False(role.IsGroupOn(Category.Events, Items.Moonfire.GroupKey));     // a group inside a category that is off
    }

    [Fact]
    public void A_roles_name_is_unique_ignoring_case_and_the_spaces_around_it_at_creation_and_at_rename()
    {
        // Refused at creation and at rename, ignoring case. A name is trimmed, and a blank one is refused. Each role gets its own
        // generated id, never shown, and the list keeps the order the roles were made in, not the names' order.
        var roles = new RoleList();
        var mule = roles.Create("Mule");
        Assert.NotNull(mule);
        Assert.Null(roles.Create("mule"));
        Assert.Null(roles.Create("  MULE "));
        Assert.Null(roles.Create(""));
        Assert.Null(roles.Create("   "));
        var alt = roles.Create(" Alt ")!;
        Assert.Equal("Alt", alt.Name);
        Assert.NotEmpty(mule.Id);
        Assert.NotEqual(mule.Id, alt.Id);
        Assert.Equal(["Mule", "Alt"], roles.Usable.Select(r => r.Name));

        Assert.False(roles.Rename(mule.Id, "ALT"));                                  // another role's name
        Assert.False(roles.Rename(mule.Id, " "));
        Assert.False(roles.Rename("gone", "Anything"));                              // no such role
        Assert.Equal("Mule", mule.Name);
        Assert.True(roles.Rename(mule.Id, "MULE"));                                  // its own name, recased
        Assert.Equal("MULE", mule.Name);
        Assert.True(roles.Rename(mule.Id, " Retainer mule "));
        Assert.Equal("Retainer mule", mule.Name);
        Assert.True(roles.IsNameFree("mule"));                                       // the old name is free again
        Assert.False(roles.IsNameFree("retainer MULE"));
        Assert.True(roles.IsNameFree("retainer MULE", mule.Id));                     // except for the role being renamed
    }

    [Fact]
    public void A_name_containing_a_hash_is_refused_since_ImGui_would_hide_everything_after_it()
    {
        // ImGui reads "##" as the start of a hidden id suffix on a MenuItem or Selectable label, so "Mule##a" and "Mule##b" would both
        // display as "Mule" in the sidebar's Role submenu and the settings page's list, though IsNameFree treated them as distinct.
        var roles = new RoleList();
        var mule = roles.Create("Mule")!;
        Assert.False(roles.IsNameFree("Mule#a"));
        Assert.False(roles.IsNameFree("Mu#le"));
        Assert.False(roles.IsNameFree("#Mule"));
        Assert.Null(roles.Create("Mule##b"));
        Assert.False(roles.Rename(mule.Id, "Other#Name"));
        Assert.Equal("Mule", mule.Name);
    }

    [Fact]
    public void None_and_unknown_role_are_refused_as_names_ignoring_case_at_creation_and_at_rename()
    {
        // "None" is the sidebar's Role menu's own line, and "unknown role" is what the table and that menu show for an id this PC does
        // not have (Summariser.UnknownRole). A role under either name would be mistaken for them, so neither may name one.
        var roles = new RoleList();
        foreach (var reserved in new[] { RoleList.NoneName, "none", " NONE ", RoleList.UnknownName, "Unknown Role", "  UNKNOWN ROLE" })
            Assert.Null(roles.Create(reserved));
        Assert.Empty(roles);
        var mule = roles.Create("Mule")!;
        Assert.False(roles.Rename(mule.Id, "none"));
        Assert.False(roles.Rename(mule.Id, "UNKNOWN role"));
        Assert.Equal("Mule", mule.Name);
        Assert.NotNull(roles.Create("None of these"));                              // only the whole word is refused
        Assert.Equal(("None", "unknown role"), (RoleList.NoneName, RoleList.UnknownName));
    }

    [Fact]
    public void Deleting_a_role_leaves_its_characters_ids_in_place_and_they_resolve_to_no_role_until_it_comes_back()
    {
        // A record whose role names an id this PC does not have behaves as no role, and keeps the id, so bringing the role back (a
        // settings import) brings the character back with it. A delete writes no record.
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        var aria = Items.Record();
        var kestrel = Items.Record(2, "Kestrel Vane");
        aria.Role = mule.Id;
        Assert.Same(mule, settings.RoleOf(aria));
        Assert.Null(settings.RoleOf(kestrel));                                       // no role

        settings.Roles.Delete(mule.Id);
        Assert.Empty(settings.Roles);
        Assert.Null(settings.RoleOf(aria));
        Assert.Equal(mule.Id, aria.Role);

        settings.Roles.Add(new Role { Id = mule.Id, Name = "Mule" });              // as a settings import would bring it back
        Assert.Equal("Mule", settings.RoleOf(aria)?.Name);
        settings.Roles.Delete("gone");                                             // deleting what is not there changes nothing
        Assert.Single(settings.Roles);
    }

    [Fact]
    public void Unticking_a_category_switches_its_groups_off_and_ticking_one_group_inside_it_ticks_the_category_with_only_that_group_on()
    {
        // The tick rule over the catalog's groups: a key for a group the catalog no longer has stays in the set through every tick
        // and does nothing. Items.Catalog's Resets holds two groups, "Daily" and "Weekly".
        var catalog = Items.Catalog();
        var role = new Role { Id = "r1", Name = "Mule" };
        role.Off.Add("group:Resets/Retired");

        role.SetCategory(Category.Resets, false, catalog);
        Assert.False(role.IsCategoryOn(Category.Resets));
        Assert.False(role.IsGroupOn(Category.Resets, Items.Roulette.GroupKey));
        Assert.False(role.IsGroupOn(Category.Resets, Items.Stickers.GroupKey));
        Assert.True(role.IsOff(Items.Stickers));

        role.SetGroup(Category.Resets, Items.Stickers.GroupKey, true, catalog);     // tick "Weekly" inside the unticked category
        Assert.True(role.IsCategoryOn(Category.Resets));
        Assert.True(role.IsGroupOn(Category.Resets, Items.Stickers.GroupKey));
        Assert.False(role.IsGroupOn(Category.Resets, Items.Roulette.GroupKey));     // only that group on
        Assert.False(role.IsOff(Items.Stickers));
        Assert.True(role.IsOff(Items.Roulette));
        Assert.Equal(["group:Resets/Daily", "group:Resets/Retired"], role.Off.Order(StringComparer.Ordinal));

        role.SetGroup(Category.Resets, Items.Stickers.GroupKey, false, catalog);    // a group inside a ticked category, alone
        Assert.True(role.IsCategoryOn(Category.Resets));
        Assert.True(role.IsOff(Items.Stickers));

        role.SetCategory(Category.Resets, true, catalog);                            // ticking the category turns it and its groups on
        Assert.False(role.IsOff(Items.Roulette));
        Assert.False(role.IsOff(Items.Stickers));
        Assert.Equal(["group:Resets/Retired"], role.Off.Order(StringComparer.Ordinal));   // the retired key is never touched
    }

    [Fact]
    public void The_catalogs_groups_come_in_the_order_a_characters_own_list_shows_them_each_with_its_ignore_key()
    {
        // The tick tree reads Catalog.GroupsIn, built once with the catalog. Over the real catalog, each category's groups are exactly
        // the groups a character with nothing read is listed under, in that order (Assembler groups Catalog.InCategory by name), and
        // each key is the one its items' ignores use.
        var catalog = CatalogBuilder.Build(new FixtureCatalogSource().Load(), CatalogExtras.Load());
        var unread = new CharacterRecord { ContentId = 1, Name = "Aria Whitewind" };
        var checklist = Assembler.Assemble(catalog, unread, new Settings(), Items.Now);

        Assert.Equal(7, checklist.Sections.Count);
        foreach (var section in checklist.Sections)
        {
            var groups = catalog.GroupsIn(section.Category);
            Assert.Equal(section.Groups.Select(g => g.Name), groups.Select(g => g.Name));
            foreach (var group in groups)
                Assert.All(catalog.InCategory(section.Category).Where(i => i.Group == group.Name), item => Assert.Equal(item.GroupKey, group.Key));
        }
        Assert.Empty(Catalog.Empty.GroupsIn(Category.Story));
    }

    [Fact]
    public void The_role_for_new_characters_is_the_chosen_role_or_none_when_it_is_unset_or_gone()
    {
        // The default role: a role id this PC has, or null; a deleted or unknown role means none.
        var settings = new Settings();
        Assert.Null(settings.RoleForNewCharacters);
        var mule = settings.Roles.Create("Mule")!;
        settings.DefaultRole = mule.Id;
        Assert.Equal(mule.Id, settings.RoleForNewCharacters);
        settings.Roles.Delete(mule.Id);
        Assert.Null(settings.RoleForNewCharacters);
        settings.DefaultRole = "no-such-role";
        Assert.Null(settings.RoleForNewCharacters);
    }
}
