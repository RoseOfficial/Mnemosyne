using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;

namespace Mnemosyne.Core.Tests;

/// <summary>The switch's pure rules. The relog service in Interop reads Dalamud and asks these; the sidebar draws what they say.</summary>
public class SwitchRulesTests
{
    [Fact]
    public void The_one_command_is_li_with_the_name_at_the_home_world_and_the_chat_line_says_only_that_it_asked()
    {
        // Lifestream splits its arguments at the @, takes the whole name before it, spaces, apostrophes and hyphens included, and the
        // first word after it as a public world.
        var aria = Items.Record();
        Assert.Equal("/li Aria Whitewind@Balmung", SwitchRules.Command(aria));
        Assert.Equal("/li Z'ara Nhul-Tia@Balmung", SwitchRules.Command(Items.Record(2, "Z'ara Nhul-Tia")));
        // There is no success signal: the line says it asked, or that no handler took the command.
        Assert.Equal("Asked Lifestream to switch to Aria Whitewind@Balmung.", SwitchRules.Asked(aria, true));
        Assert.Equal("Lifestream's /li command is not registered, so nothing was asked.", SwitchRules.Asked(aria, false));
    }

    [Fact]
    public void Lifestream_counts_only_when_a_loaded_copy_is_at_2_5_3_0_or_later_and_li_is_registered()
    {
        // The Name@World branch is absent at 2.5.2.10 and present from 2.5.3.0, the floor AutoRetainer also demands.
        Assert.Equal(new Version(2, 5, 3, 0), SwitchRules.LifestreamFloor);
        Assert.False(SwitchRules.LifestreamReady([], true));                                                   // not loaded
        Assert.False(SwitchRules.LifestreamReady([new Version(2, 5, 2, 10)], true));                          // too old for Name@World
        Assert.True(SwitchRules.LifestreamReady([new Version(2, 5, 3, 0)], true));                            // the floor itself
        Assert.True(SwitchRules.LifestreamReady([new Version(2, 5, 4, 23)], true));                           // a later build than the floor, also ready
        Assert.True(SwitchRules.LifestreamReady([new Version(2, 5, 2, 0), new Version(2, 5, 4, 23)], true));  // any loaded copy at the floor
        Assert.False(SwitchRules.LifestreamReady([new Version(2, 5, 4, 23)], false));                         // loaded, /li not (yet) registered
    }

    [Fact]
    public void The_button_is_hidden_without_lifestream_or_a_home_world_and_disabled_logged_out_on_the_logged_in_row_and_while_silenced()
    {
        var aria = Items.Record(1);
        Assert.Equal(SwitchState.Hidden, SwitchRules.State(false, aria, 2, false));
        Assert.Equal(SwitchState.Hidden, SwitchRules.State(true, new CharacterRecord { ContentId = 3, Name = "Kestrel Vane" }, 2, false));   // no home world
        Assert.Equal(SwitchState.LogInFirst, SwitchRules.State(true, aria, null, false));
        Assert.Equal(SwitchState.LogInFirst, SwitchRules.State(true, aria, null, true));
        Assert.Equal(SwitchState.ThisCharacter, SwitchRules.State(true, aria, 1, false));
        Assert.Equal(SwitchState.ThisCharacter, SwitchRules.State(true, aria, 1, true));
        Assert.Equal(SwitchState.Silenced, SwitchRules.State(true, aria, 2, true));   // combat, a duty, a cutscene: the must-stay-silent set
        Assert.Equal(SwitchState.Ready, SwitchRules.State(true, aria, 2, false));

        // An imported name or home world can hold anything, and Lifestream's @ branch reads a second word after the world as a travel
        // destination and everything before the argument's first @ as the name. Each of the four probe strings below must hide the
        // button rather than reach the command.
        Assert.Equal(SwitchState.Hidden, SwitchRules.State(true, new CharacterRecord { ContentId = 4, Name = "Aria Whitewind", HomeWorld = "Balmung Ultros" }, 2, false));
        Assert.Equal(SwitchState.Hidden, SwitchRules.State(true, new CharacterRecord { ContentId = 4, Name = "Other Person@Cactuar", HomeWorld = "Balmung" }, 2, false));
        Assert.Equal(SwitchState.Hidden, SwitchRules.State(true, new CharacterRecord { ContentId = 4, Name = "Aria\nWhitewind", HomeWorld = "Balmung" }, 2, false));
        Assert.Equal(SwitchState.Hidden, SwitchRules.State(true, new CharacterRecord { ContentId = 4, Name = "Aria Whitewind", HomeWorld = "Balmung,gc" }, 2, false));

        // Every drawn state's tooltip names Lifestream, except Silenced, which names the whole must-stay-silent set instead; the ready
        // one says what Lifestream does while the character is busy.
        foreach (var state in new[] { SwitchState.LogInFirst, SwitchState.ThisCharacter, SwitchState.Ready })
            Assert.Contains("Lifestream", SwitchRules.Tooltip(state, aria));
        Assert.Contains("up to a minute", SwitchRules.Tooltip(SwitchState.Ready, aria));
        Assert.Contains("Aria Whitewind", SwitchRules.Tooltip(SwitchState.Ready, aria));
        // A disabled button says why.
        Assert.StartsWith("Log in first", SwitchRules.Tooltip(SwitchState.LogInFirst, aria));
        Assert.Contains("logged in as this character", SwitchRules.Tooltip(SwitchState.ThisCharacter, aria));
        Assert.Contains("combat", SwitchRules.Tooltip(SwitchState.Silenced, aria));
    }
}
