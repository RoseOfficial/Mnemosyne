using System.Text;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

/// <summary>A fixture record at a fixed instant renders to text that is checked in. A mismatch writes the actual text beside the
/// golden as .actual.txt so the diff can be read and, when intended, accepted by copying it over the golden.</summary>
public class GoldenTests
{
    private static string GoldenPath(string name) => Path.Combine(AppContext.BaseDirectory, "Goldens", name);

    private static void AssertGolden(string name, string actual)
    {
        var path = GoldenPath(name);
        var expected = File.ReadAllText(path).Replace("\r\n", "\n");
        actual = actual.Replace("\r\n", "\n");
        if (expected != actual)
        {
            File.WriteAllText(Path.ChangeExtension(path, ".actual.txt"), actual);
            Assert.Fail($"golden {name} differs; see {Path.ChangeExtension(path, ".actual.txt")}\n{actual}");
        }
    }

    [Fact]
    public void Aria_renders_as_checked_in()
    {
        var record = SummariserTests.Aria();
        var checklist = Assembler.Assemble(Items.Catalog(), record, new Settings(), Items.Now);
        AssertGolden("aria.txt", ChecklistText.Render(checklist, record, Items.Now));
    }

    /// <summary>The real catalog's shape, so a builder change that silently renames, reorders or re-sizes a group of the
    /// live data fails a test instead of going unnoticed: one line per group, in the order the category lists them.</summary>
    [Fact]
    public void The_groups_built_from_the_fixture_render_as_checked_in()
    {
        var catalog = CatalogBuilder.Build(new FixtureCatalogSource().Load(), CatalogExtras.Load());
        var text = new StringBuilder();
        foreach (var category in new[] { Category.Story, Category.Jobs, Category.Events, Category.Resets, Category.Content, Category.Collections, Category.Money })
        {
            foreach (var group in catalog.InCategory(category).GroupBy(i => (i.GroupOrder, i.Group)))
                text.Append(category).Append(' ').Append(group.Key.GroupOrder).Append(' ').Append(group.Key.Group).Append(' ').Append(group.Count()).Append('\n');
        }
        AssertGolden("groups.txt", text.ToString());
    }

    [Fact]
    public void An_empty_catalog_renders_as_checked_in()
    {
        var record = Items.Record(2, "Kestrel Vane");
        record.LastSeen = Items.Now - TimeSpan.FromDays(3);
        var checklist = Assembler.Assemble(Catalog.Empty, record, new Settings(), Items.Now);
        AssertGolden("empty.txt", ChecklistText.Render(checklist, record, Items.Now));
    }

    /// <summary>The reward counter's count, its countdown and its "as of last visit" marker, rendered through ChecklistText beside an
    /// ordinary done occurrence. The main golden's catalog leaves Yo-kai out so it stays byte-identical, so this is the one golden
    /// that exercises a reward counter end to end.</summary>
    [Fact]
    public void A_reward_counter_renders_as_checked_in()
    {
        var record = Items.Record();
        record.Values[Items.Yokai.Id] = new CounterValue(12, 39, Items.Now);
        record.Values[Items.Moonfire.Id] = new DoneValue(true);
        var catalog = new Catalog([Items.Moonfire, Items.Yokai]);
        var checklist = Assembler.Assemble(catalog, record, new Settings(), Items.Now);
        AssertGolden("reward.txt", ChecklistText.Render(checklist, record, Items.Now));
    }

    /// <summary>A fixed board rendered through BoardText. Two characters in sidebar order, the second stale; every group's shape, one of
    /// them empty; a slot numbered per character; the earliest deadline naming a line; a Timer's deadline in Today; a pin, both priorities
    /// off Normal, a hide and a snooze.</summary>
    [Fact]
    public void A_board_renders_as_checked_in()
    {
        var catalog = new Catalog([.. Items.Catalog().Items, Items.Yokai]);
        var aria = SummariserTests.Aria();
        aria.ItemNames[Items.Venture.Id] = "Mira";
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.LastSeen = Items.Now - TimeSpan.FromDays(30);
        kestrel.Values[Items.Venture.Id] = new TimerValue(Items.Now + TimeSpan.FromHours(2));
        kestrel.ItemNames[Items.Venture.Id] = "Tomo";
        kestrel.Values[Items.Map.Id] = new TimerValue(Items.Now - TimeSpan.FromHours(2));
        kestrel.Values[Items.Moonfire.Id] = new DoneValue(false);
        var preferences = new TaskPreferences();
        preferences.Pin(Items.Stickers.Id);
        preferences.SetPriority(Items.Stickers.Id, TaskPriority.High);
        preferences.SetPriority(Items.Moonfire.Id, TaskPriority.Low);
        preferences.Hide(Items.Cactpot.Id);
        preferences.Snooze(Items.Allowances.Id, null, Items.Now);

        var board = Board.Build(
            [
                (Record: aria, Checklist: Assembler.Assemble(catalog, aria, new Settings(), Items.Now)),
                (Record: kestrel, Checklist: Assembler.Assemble(catalog, kestrel, new Settings(), Items.Now)),
            ],
            preferences, Items.Now);

        AssertGolden("board.txt", BoardText.Render(board, Items.Now));
    }

    /// <summary>Aria with the Mule role, which switches the Events category and the Resets "Daily" group off. Each off row stays in its
    /// section, ending " · off for Mule", the words the window draws; none is in a top group, and the badge counts the stickers and the
    /// venture alone. No existing golden has a role, so every one of them is byte-identical.</summary>
    [Fact]
    public void An_off_row_renders_as_checked_in()
    {
        var record = SummariserTests.Aria();
        var settings = new Settings();
        var mule = settings.Roles.Create("Mule")!;
        mule.Off.Add(Role.CategoryKey(Category.Events));
        mule.Off.Add(Items.Roulette.GroupKey);   // the "Daily" group of Resets
        record.Role = mule.Id;
        var checklist = Assembler.Assemble(Items.Catalog(), record, settings, Items.Now);
        AssertGolden("role.txt", ChecklistText.Render(checklist, record, Items.Now));
    }

    /// <summary>A My tasks section, last, in the player's order: a daily ticked this period, a weekly and a one-off owed, an "every" task
    /// cooling down and one ready, and a task for another character that Aria does not have at all. The one-off and the ready task are in
    /// Today, the weekly in This week. No existing golden has a task, so every one of them is byte-identical.</summary>
    [Fact]
    public void A_my_tasks_section_renders_as_checked_in()
    {
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        settings.AddMyTask(Tasks.Draft("Island granary", MyTask.Weekly));
        settings.AddMyTask(Tasks.Draft("Buy the mount", MyTask.Once));
        var craft = settings.AddMyTask(Tasks.Draft("Three-day craft", MyTask.Every, 72))!;
        var hourly = settings.AddMyTask(Tasks.Draft("Hourly check", MyTask.Every, 1))!;
        settings.AddMyTask(Tasks.Draft("Alt errand", MyTask.Daily, null, MyTask.ForPicked, 2));
        var record = Items.Record();
        record.Values[chest.Id] = chest.TickValue(Items.Now)!;
        record.Values[craft.Id] = craft.TickValue(Items.Now - TimeSpan.FromHours(24))!;
        record.Values[hourly.Id] = hourly.TickValue(Items.Now - TimeSpan.FromHours(2))!;
        var checklist = Assembler.Assemble(new Catalog(settings.MyTasks.Items()), record, settings, Items.Now);
        AssertGolden("mytasks.txt", ChecklistText.Render(checklist, record, Items.Now));
    }

    /// <summary>A small Totals page. Aria is seen at Now, with two retainers. Kestrel was last seen 30 days ago, so each of its rows is
    /// marked stale. Momo is hidden, so nowhere. The page draws a table, so the test prints each line, then its holders indented, largest
    /// first, in the Money category's order with MGP last. No existing golden prints the totals or the matrix, so every one of them is
    /// byte-identical.</summary>
    [Fact]
    public void A_totals_page_renders_as_checked_in()
    {
        var aria = Items.Record(1);
        TotalsTests.Gil(aria, 1_234_567, 100_000, 60_000);
        aria.Values[MoneyIds.CurrencyId(20)] = TotalsTests.Held(50_000, 50_000);             // Storm Seals
        aria.Values[MoneyIds.CurrencyId(28)] = TotalsTests.Held(1_200, 2_000);               // Poetics
        aria.Values[MoneyIds.CurrencyId(29)] = TotalsTests.Held(250_000, 9_999_999);         // MGP
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.LastSeen = Items.Now - TimeSpan.FromDays(30);
        TotalsTests.Gil(kestrel, 8_500, 0);
        kestrel.Values[MoneyIds.CurrencyId(28)] = TotalsTests.Held(900, 2_000);
        kestrel.Values[MoneyIds.CurrencyId(21072)] = TotalsTests.Held(12, 65_000);           // Ventures
        var momo = Items.Record(3, "Momo Lulu");
        momo.Hidden = true;
        TotalsTests.Gil(momo, 5_000_000);
        momo.Values[MoneyIds.CurrencyId(25)] = TotalsTests.Held(20_000, 20_000);             // Wolf Marks, Momo's alone

        var lines = Totals.Build([aria, kestrel, momo], TotalsTests.Built, new Settings(), Items.Now);

        // The text form lives in Core since the dump prints it too (TotalsText); this golden is its byte-for-byte pin.
        AssertGolden("totals.txt", TotalsText.Render(lines, Items.Now));
    }

    /// <summary>The dump's gil history section. Aria's points reach back a year, so each of the four days carries Aria's latest point on
    /// or before it. Kestrel's first point is three days ago: today carries it, since a day with no change writes no point, and his row's
    /// three earlier days have none, while the total counts him on them at his first amount. Momo is hidden, so nowhere, the total
    /// included. No existing golden prints the gil history, so every one of them is byte-identical.</summary>
    [Fact]
    public void A_gil_history_section_renders_as_checked_in()
    {
        var aria = GilPointsTests.WithPoints(Items.Record(1), ("2025-09-01", 900_000), ("2026-08-01", 1_200_000), ("2026-09-06", 1_300_000), ("2026-09-13", 1_394_567));
        var kestrel = GilPointsTests.WithPoints(Items.Record(2, "Kestrel Vane"), ("2026-09-10", 8_500));
        kestrel.LastSeen = Items.Now - TimeSpan.FromDays(3);
        var momo = GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2026-09-01", 5_000_000));
        momo.Hidden = true;

        AssertGolden("gilhistory.txt", GilHistoryText.Render([aria, kestrel, momo], Items.Now));
    }
}
