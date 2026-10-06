using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The dump's shape, where the goldens hold no example. The fixture character has no running event, so the
/// "RUNNING EVENTS" group is checked here with one in it.</summary>
public class ChecklistTextTests
{
    [Fact]
    public void The_dump_prints_the_running_events_group_after_ending_soon()
    {
        var running = new ItemDefinition("event:252:39", Category.Events, "Yo-kai Watch", "Yo-kai Watch 2026", ItemKind.Done, ResetRule.Window, 39, Freshness.AtLogin,
            new Availability(Items.Now - TimeSpan.FromDays(2), Items.Now + TimeSpan.FromDays(30)));
        var record = Items.Record();
        record.Values[running.Id] = new DoneValue(false);
        var checklist = Assembler.Assemble(new Catalog([running]), record, new Settings { EndingSoonDays = 3 }, Items.Now);

        var text = ChecklistText.Render(checklist, record, Items.Now).Replace("\r\n", "\n");
        Assert.Contains("ENDING SOON\n  (nothing)\nRUNNING EVENTS\n  [ ] Yo-kai Watch 2026 — ends in 30 d\n", text);
    }
}
