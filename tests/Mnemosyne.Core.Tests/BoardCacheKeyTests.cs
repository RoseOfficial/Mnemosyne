using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The key All tasks caches its last board on, so the board is not rebuilt every frame the page is open. Equal keys mean nothing
/// that could change the board has moved: the same characters' Checklist references in the same order (the cache hands back the same
/// object until that record, the settings version or the minute moves), the same tasks version (moved by an adjustment,
/// Plugin.UpdateTasks), and the same UTC minute (which also covers a snooze passing, since Board.Build resolves every expired one within
/// the minute it ends). Pure: it reads nothing but what it is given.</summary>
public class BoardCacheKeyTests
{
    [Fact]
    public void The_same_inputs_give_an_equal_key_and_a_changed_checklist_a_moved_counter_or_a_new_minute_each_differ()
    {
        var aria = Items.Record(1);
        var checklist = Assembler.Assemble(Items.Catalog(), aria, new Settings(), Items.Now);
        var characters = new (CharacterRecord Record, Mnemosyne.Core.Checklist.Checklist Checklist)[] { (aria, checklist) };
        var key = BoardCacheKey.For(characters, tasksVersion: 3, Items.Now);

        Assert.Equal(key, BoardCacheKey.For(characters, tasksVersion: 3, Items.Now));                 // the same inputs
        Assert.Equal(key, BoardCacheKey.For(characters, tasksVersion: 3, Items.Now.AddSeconds(30)));  // still the same minute

        var otherChecklist = Assembler.Assemble(Items.Catalog(), aria, new Settings(), Items.Now);
        var changed = new (CharacterRecord Record, Mnemosyne.Core.Checklist.Checklist Checklist)[] { (aria, otherChecklist) };
        Assert.NotEqual(key, BoardCacheKey.For(changed, tasksVersion: 3, Items.Now));                 // a different Checklist reference
        Assert.NotEqual(key, BoardCacheKey.For(characters, tasksVersion: 4, Items.Now));              // the counter moved
        Assert.NotEqual(key, BoardCacheKey.For(characters, tasksVersion: 3, Items.Now.AddMinutes(1))); // a new minute
    }
}
