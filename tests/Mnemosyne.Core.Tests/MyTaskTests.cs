using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The task, the settings' list of them, the item each becomes, the value a tick stores, and the catalog's My tasks part.
/// What the assembler does with a Manual item beyond the game's own rules is AssemblerTests'.</summary>
public class MyTaskTests
{
    [Fact]
    public void Each_kind_becomes_a_manual_item_in_the_my_tasks_category_after_money_and_a_tick_stores_its_kinds_value()
    {
        // Daily and weekly are Done items on the game's two resets, a one-off a Done item with none, and "every" a Timer with none that
        // reads ready once passed; all Manual, in the one group "My tasks", in the order made, the name trimmed. A tick at Sunday 12:00
        // stores the daily period that began Saturday 15:00, the weekly one that began Tuesday 08:00, no period, or the end of the cooldown.
        var settings = new Settings();
        settings.AddMyTask(Tasks.Draft(" FC chest ", MyTask.Daily));
        settings.AddMyTask(Tasks.Draft("Island granary", MyTask.Weekly));
        settings.AddMyTask(Tasks.Draft("Buy the mount", MyTask.Once));
        settings.AddMyTask(Tasks.Draft("Three-day craft", MyTask.Every, 72));

        var items = settings.MyTasks.Items().ToList();

        Assert.Equal(settings.MyTasks.Select(t => t.Id), items.Select(i => i.Id));            // each item carries its task's id
        Assert.All(items, i => Assert.Matches("^mine:[0-9a-f]{12}$", i.Id));
        Assert.Equal(["FC chest", "Island granary", "Buy the mount", "Three-day craft"], items.Select(i => i.Name));
        Assert.Equal([ItemKind.Done, ItemKind.Done, ItemKind.Done, ItemKind.Timer], items.Select(i => i.Kind));
        Assert.Equal([ResetRule.Daily, ResetRule.Weekly, ResetRule.None, ResetRule.None], items.Select(i => i.Reset));
        Assert.Equal([false, false, false, true], items.Select(i => i.ReadyWhenPassed));
        Assert.Equal([0, 1, 2, 3], items.Select(i => i.Order));
        Assert.All(items, i => Assert.Equal((Category.MyTasks, "My tasks", Freshness.Manual, "group:MyTasks/My tasks"), (i.Category, i.Group, i.Freshness, i.GroupKey)));
        Assert.Equal(FreshnessKind.Manual, Freshness.Manual.Kind);
        Assert.Equal(Category.MyTasks, Enum.GetValues<Category>()[^1]);                // after Money, last
        Assert.Equal(Category.Money + 1, Category.MyTasks);
        Assert.Equal("My tasks", Category.MyTasks.Label());
        Assert.Equal(["Story", "Jobs", "Events", "Resets", "Content", "Collections", "Money"],
            Enum.GetValues<Category>().Where(c => c != Category.MyTasks).Select(c => c.Label()));   // the seven read as before

        Assert.Equal<ItemValue?>(
            [new DoneValue(true, "daily:2026-09-12", Items.Now), new DoneValue(true, "weekly:2026-09-08", Items.Now), new DoneValue(true, null, Items.Now),
             new TimerValue(Items.Now + TimeSpan.FromHours(72))],
            settings.MyTasks.Usable.Select(t => t.TickValue(Items.Now)));
        Assert.Null(new MyTask { Id = "mine:aaaaaaaaaaaa", Name = "Monthly", Kind = "monthly" }.TickValue(Items.Now));   // not valid: nothing to store
    }

    [Fact]
    public void An_id_is_mine_and_12_random_hex_characters_and_two_adds_never_share_one()
    {
        // The id is "mine:" and 12 random lower-case hex characters, generated when the task is made and never shown, so tasks made on
        // two PCs never share one. Only that form is a task's id. The list holds a copy of the draft, so a draft the settings page
        // keeps editing never reaches the settings behind their save.
        var settings = new Settings();
        var draft = Tasks.Draft("FC chest", MyTask.Daily);
        var first = settings.AddMyTask(draft)!;
        var second = settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly))!;
        Assert.Matches("^mine:[0-9a-f]{12}$", first.Id);
        Assert.Matches("^mine:[0-9a-f]{12}$", second.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(draft, first);
        Assert.Equal("", draft.Id);
        draft.Name = "Changed after the add";
        Assert.Equal("FC chest", first.Name);

        settings.MyTasks.Delete(second.Id);
        var third = settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly))!;
        Assert.DoesNotContain(third.Id, new[] { first.Id, second.Id });                   // a task made again is a new task
        Assert.NotEqual(MyTaskList.NewId(), MyTaskList.NewId());

        Assert.True(MyTaskList.IsId("mine:0123456789ab"));
        foreach (var notAnId in new[] { null, "", "mine:", "mine:1", "mine:0123456789a", "mine:0123456789abc", "mine:0123456789AB", "mine:0123456789ag", "quest:0123456789", "MINE:0123456789ab" })
            Assert.False(MyTaskList.IsId(notAnId), notAnId);
    }

    [Fact]
    public void A_name_is_not_blank_and_holds_no_hash_and_an_every_task_runs_from_1_to_720_hours()
    {
        // ImGui cuts a label at "##", so a name holds no "#"; an "every" task's cooldown is 1 to 720 hours (30 days); the kind and "for"
        // must be forms this build knows. Nobody picked is allowed: such a task is for nobody. A draft that breaks a rule is neither
        // added nor saved over a task.
        Assert.True(Tasks.Draft("FC chest", MyTask.Daily).IsValid);
        Assert.True(Tasks.Draft("Every hour", MyTask.Every, 1).IsValid);
        Assert.True(Tasks.Draft("Every month", MyTask.Every, 720).IsValid);
        Assert.True(Tasks.Draft("For Mule", MyTask.Daily, forWhom: MyTask.ForRole("r1")).IsValid);
        Assert.True(Tasks.Draft("Nobody picked yet", MyTask.Daily, forWhom: MyTask.ForPicked).IsValid);
        MyTask[] bad =
        [
            Tasks.Draft("", MyTask.Daily), Tasks.Draft("   ", MyTask.Daily), Tasks.Draft("Chest #1", MyTask.Daily),
            Tasks.Draft("Monthly", "monthly"), Tasks.Draft("No kind", ""),
            Tasks.Draft("Too short", MyTask.Every, 0), Tasks.Draft("Too long", MyTask.Every, 721), Tasks.Draft("No length", MyTask.Every),
            Tasks.Draft("Friends", MyTask.Daily, forWhom: "friends"), Tasks.Draft("No role", MyTask.Daily, forWhom: MyTask.RolePrefix),
        ];
        Assert.All(bad, task => Assert.False(task.IsValid, task.Name));
        Assert.Equal("r1", Tasks.Draft("For Mule", MyTask.Daily, forWhom: MyTask.ForRole("r1")).RoleId);
        Assert.Null(Tasks.Draft("FC chest", MyTask.Daily).RoleId);

        var settings = new Settings();
        Assert.Null(settings.AddMyTask(Tasks.Draft("Chest #1", MyTask.Daily)));
        Assert.Empty(settings.MyTasks);
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        Assert.False(settings.MyTasks.Edit(chest.Id, Tasks.Draft("FC chest", MyTask.Every, 0)));
        Assert.Equal(MyTask.Daily, chest.Kind);
    }

    [Fact]
    public void An_edit_keeps_the_id_and_moving_up_or_down_sets_the_order_every_character_sees()
    {
        // An edit renames, changes the kind, the length or who the task is for, and keeps the id, so every tick stays with the task. Up
        // and Down move a task one place among the tasks in use, past an entry only a hand edit leaves; the catalog's order follows.
        // The settings page edits a copy (Draft), never the stored task.
        var settings = new Settings();
        var a = settings.AddMyTask(Tasks.Draft("A", MyTask.Daily))!;
        var b = settings.AddMyTask(Tasks.Draft("B", MyTask.Daily))!;
        var c = settings.AddMyTask(Tasks.Draft("C", MyTask.Daily))!;
        settings.MyTasks.Insert(1, null!);                                                  // a hand-edited null between A and B, never used
        var bId = b.Id;

        Assert.True(settings.MyTasks.Edit(b.Id, Tasks.Draft(" Island granary ", MyTask.Every, 48, MyTask.ForPicked, 7)));
        Assert.Equal((bId, "Island granary", MyTask.Every, MyTask.ForPicked), (b.Id, b.Name, b.Kind, b.For));
        Assert.Equal(48, b.Hours);
        Assert.Equal([7UL], b.Picked);
        var copy = b.Draft();
        copy.Picked.Add(8);
        Assert.Equal("", copy.Id);
        Assert.Equal([7UL], b.Picked);                                                      // the copy has a list of its own
        Assert.True(settings.MyTasks.Edit(b.Id, Tasks.Draft("Island granary", MyTask.Weekly, 48)));
        Assert.Null(b.Hours);                                                               // a length is kept only for "every"
        Assert.False(settings.MyTasks.Edit("mine:ffffffffffff", Tasks.Draft("Nothing", MyTask.Daily)));

        Assert.True(settings.MyTasks.Move(c.Id, -1));                                      // C above B
        Assert.False(settings.MyTasks.Move(a.Id, -1));                                     // A is at the top
        Assert.True(settings.MyTasks.Move(a.Id, 1));                                       // A below C, past the null
        Assert.Equal(["C", "A", "Island granary"], settings.MyTasks.Items().Select(i => i.Name));
        Assert.Equal([0, 1, 2], settings.MyTasks.Items().Select(i => i.Order));
        Assert.False(settings.MyTasks.Move(b.Id, 1));                                      // B is at the bottom
        Assert.False(settings.MyTasks.Move("mine:ffffffffffff", 1));

        settings.MyTasks.Delete(a.Id);
        Assert.Equal(["C", "Island granary"], settings.MyTasks.Items().Select(i => i.Name));
        Assert.Null(settings.MyTasks.Find(a.Id));
    }

    [Fact]
    public void Only_whole_tasks_are_used_and_of_two_with_one_id_the_first_so_the_catalog_never_meets_a_duplicate()
    {
        // A hand edit or a later build can leave a null, an id that is not "mine:" and 12 lower-case hex characters (one could name a
        // sheet row), an unknown kind, a length out of range, an unknown "for", or two tasks with one id. Each is kept in the list, as
        // the file has it, and never used; the first of a repeated id wins. So the combined catalog's duplicate-id guard never throws.
        var list = new MyTaskList
        {
            null!,
            new() { Id = "quest:1", Name = "Looks like a quest", Kind = MyTask.Daily },
            new() { Id = "mine:1", Name = "An id too short", Kind = MyTask.Daily },
            new() { Id = "mine:aaaaaaaaaaaa", Name = "FC chest", Kind = MyTask.Daily },
            new() { Id = "mine:aaaaaaaaaaaa", Name = "A second with that id", Kind = MyTask.Weekly },
            new() { Id = "mine:bbbbbbbbbbbb", Name = "Monthly", Kind = "monthly" },
            new() { Id = "mine:cccccccccccc", Name = "Too long", Kind = MyTask.Every, Hours = 721 },
            new() { Id = "mine:dddddddddddd", Name = "Friends", Kind = MyTask.Daily, For = "friends" },
            new() { Id = "mine:eeeeeeeeeeee", Name = "Granary", Kind = MyTask.Weekly },
        };

        Assert.Equal(["mine:aaaaaaaaaaaa", "mine:eeeeeeeeeeee"], list.Usable.Select(t => t.Id));
        Assert.Equal("FC chest", list.Find("mine:aaaaaaaaaaaa")!.Name);
        Assert.Null(list.Find("quest:1"));
        Assert.Null(list.Find("mine:1"));
        Assert.Null(list.Find(null));
        Assert.Equal(9, list.Count);
        var catalog = new Catalog([.. Items.Catalog().Items, .. list.Items()]);            // no duplicate-id throw
        Assert.Equal("FC chest", catalog.TryGet("mine:aaaaaaaaaaaa")!.Name);
        Assert.Equal(Items.Catalog().Items.Count + 2, catalog.Items.Count);
        Assert.False(MyTask.IsWhole(null));
        Assert.False(MyTask.IsWhole(list[1]));
        Assert.False(MyTask.IsWhole(list[2]));
        Assert.True(MyTask.IsWhole(list[3]));
    }

    [Fact]
    public void The_catalogs_my_tasks_part_follows_the_settings_version_and_stays_out_of_the_sheet_built_catalog()
    {
        // The sheet-built catalog is built once, and holds no My task and no id a task could take. With no task the checklists read it
        // as it is. With tasks, one catalog adds them, built once per settings version: a frame that changed nothing reuses it, and an
        // edit, which moves the version, is read at once. GroupsIn names the one group, which the roles' tick tree lists.
        var sheets = CatalogBuilder.Build(new FixtureCatalogSource().Load(), CatalogExtras.Load());
        Assert.DoesNotContain(sheets.Items, i => i.Id.StartsWith(MyTaskList.IdPrefix, StringComparison.Ordinal) || i.Category == Category.MyTasks);
        var catalogs = new MyTaskCatalog(sheets);
        var settings = new Settings();
        Assert.Same(sheets, catalogs.For(settings, 0));

        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var first = catalogs.For(settings, 1);
        Assert.Equal(sheets.Items.Count + 1, first.Items.Count);
        Assert.Equal("FC chest", first.TryGet(chest.Id)!.Name);
        Assert.Null(sheets.TryGet(chest.Id));
        Assert.Same(first, catalogs.For(settings, 1));                                      // the same version: the same catalog
        Assert.Equal([new CatalogGroup("My tasks", "group:MyTasks/My tasks")], first.GroupsIn(Category.MyTasks));
        Assert.Empty(sheets.GroupsIn(Category.MyTasks));

        settings.MyTasks.Edit(chest.Id, Tasks.Draft("Company chest", MyTask.Daily));
        Assert.Equal("Company chest", catalogs.For(settings, 2).TryGet(chest.Id)!.Name);
        settings.MyTasks.Delete(chest.Id);
        Assert.Same(sheets, catalogs.For(settings, 3));
    }

    [Fact]
    public void With_no_task_no_checklist_has_a_my_tasks_section_and_with_one_the_section_comes_last()
    {
        // With no tasks the category holds nothing and is not drawn, so every existing golden is byte-identical. With a task, the
        // section comes after Money and prints as "MY TASKS".
        var aria = SummariserTests.Aria();
        var none = Assembler.Assemble(Items.Catalog(), aria, new Settings(), Items.Now);
        Assert.Equal(7, none.Sections.Count);
        Assert.DoesNotContain(none.Sections, s => s.Category == Category.MyTasks);
        Assert.DoesNotContain("MY TASKS", ChecklistText.Render(none, aria, Items.Now));

        var settings = new Settings();
        settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily));
        var one = Assembler.Assemble(Tasks.Catalog(settings), aria, settings, Items.Now);
        Assert.Equal(8, one.Sections.Count);
        Assert.Equal(Category.MyTasks, one.Sections[^1].Category);
        Assert.EndsWith("MONEY\n  Retainers\n    [ ] Venture: Retainer 1 — in 18 h\nMY TASKS\n  My tasks\n    [ ] FC chest\n", ChecklistText.Render(one, aria, Items.Now));
    }

    [Fact]
    public void A_daily_or_weekly_task_is_done_for_the_period_it_was_ticked_in_and_due_again_at_its_reset()
    {
        // A daily and a weekly map onto what the assembler already does for a game daily and weekly. Each tick stores its period, and
        // each task is due again at its own reset: the daily at 15:00, the weekly on Tuesday at 08:00.
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var granary = settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var r = Items.Record();
        r.Values[chest.Id] = chest.TickValue(Items.Now)!;
        r.Values[granary.Id] = granary.TickValue(Items.Now)!;

        var ticked = Assembler.Assemble(catalog, r, settings, Items.Now);
        Assert.Empty(ticked.Today.Concat(ticked.ThisWeek));
        Assert.Equal(0, ticked.Badge);
        Assert.All(ticked.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries,
            e => Assert.Equal((EntryState.Done, "done"), (e.State, e.Display)));

        var dailyReset = Assembler.Assemble(catalog, r, settings, new DateTimeOffset(2026, 9, 13, 15, 0, 0, TimeSpan.Zero));
        Assert.Equal([chest.Id], dailyReset.Today.Select(e => e.Item.Id));
        Assert.Empty(dailyReset.ThisWeek);
        var weeklyReset = Assembler.Assemble(catalog, r, settings, new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal([granary.Id], weeklyReset.ThisWeek.Select(e => e.Item.Id));
    }

    [Fact]
    public void An_ignore_a_role_and_the_category_switch_act_on_a_my_task_as_on_any_item()
    {
        // Everything built on the catalog applies unchanged. A role switching My tasks off lists its rows dimmed, "off for" the role,
        // and counts none. Ignored everywhere, a task is gone for every character. The category switched off in the settings is not
        // drawn at all.
        var settings = new Settings();
        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        var granary = settings.AddMyTask(Tasks.Draft("Granary", MyTask.Weekly))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var mule = settings.Roles.Create("Mule")!;
        mule.SetCategory(Category.MyTasks, false, catalog);
        var aria = Items.Record(1);
        var kestrel = Items.Record(2, "Kestrel Vane");
        kestrel.Role = mule.Id;

        Assert.Equal(2, Assembler.Assemble(catalog, aria, settings, Items.Now).Badge);
        var off = Assembler.Assemble(catalog, kestrel, settings, Items.Now);
        Assert.Equal(0, off.Badge);
        Assert.All(off.Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries, e => Assert.Equal("Mule", e.OffFor));

        settings.IgnoredEverywhere.Add(chest.Id);
        var ignored = Assembler.Assemble(catalog, aria, settings, Items.Now);
        Assert.Empty(ignored.Today);
        Assert.Equal([granary.Id], ignored.ThisWeek.Select(e => e.Item.Id));
        Assert.DoesNotContain(chest.Id, Assembler.Assemble(catalog, kestrel, settings, Items.Now).Sections.SelectMany(s => s.Groups).SelectMany(g => g.Entries).Select(e => e.Item.Id));

        settings.CategoriesOff.Add(nameof(Category.MyTasks));
        var hidden = Assembler.Assemble(catalog, aria, settings, Items.Now);
        Assert.DoesNotContain(hidden.Sections, s => s.Category == Category.MyTasks);
        Assert.Equal(0, hidden.Badge);
    }

    [Fact]
    public void A_counted_task_is_a_manual_counter_on_its_kinds_reset_with_its_number_on_the_item()
    {
        // A daily, weekly or one-off task with a number is a Counter done at that number, on the same reset as its plain form. The
        // number rides on the item, so an edit reaches every character at once. A plain task is the Done item it was, with no number.
        var settings = new Settings();
        settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5));
        settings.AddMyTask(Tasks.Counted("Granary", MyTask.Weekly, 3));
        settings.AddMyTask(Tasks.Counted("Two mounts", MyTask.Once, 2));
        settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily));

        var items = settings.MyTasks.Items().ToList();

        Assert.Equal([ItemKind.Counter, ItemKind.Counter, ItemKind.Counter, ItemKind.Done], items.Select(i => i.Kind));
        Assert.Equal([ResetRule.Daily, ResetRule.Weekly, ResetRule.None, ResetRule.Daily], items.Select(i => i.Reset));
        Assert.Equal<int?>([5, 3, 2, null], items.Select(i => i.Target));
        Assert.All(items, i => Assert.True(i.DoneAtCap));
        Assert.Equal([true, true, true, false], settings.MyTasks.Usable.Select(t => t.IsCounted));
    }

    [Fact]
    public void A_number_is_2_to_99_on_a_daily_weekly_or_one_off_and_ignored_on_an_every_task()
    {
        // 1 is a plain task, so it is stored as none; 0, 1, 100 or a negative is a task this build cannot use, which only a hand edit
        // or an import leaves, and IsWhole rejects it. On an "every" task a number is ignored, as Hours is on a daily: valid, not
        // counted, a Timer item with no number, and dropped when the task is saved.
        var daily = Tasks.Draft("Deliver", MyTask.Daily);
        foreach (var bad in new[] { -1, 0, 1, 100 }) { daily.Target = bad; Assert.False(daily.IsValid); }
        foreach (var good in new int?[] { null, 2, 99 }) { daily.Target = good; Assert.True(daily.IsValid); }

        var settings = new Settings();
        var every = Tasks.Draft("Craft", MyTask.Every, 72);
        every.Target = 4;
        Assert.True(every.IsValid);
        Assert.False(every.IsCounted);
        var stored = settings.AddMyTask(every)!;
        Assert.Null(stored.Target);
        Assert.Equal((ItemKind.Timer, (int?)null), (stored.ToItem(0).Kind, stored.ToItem(0).Target));

        var imported = new MyTask { Id = "mine:aaaaaaaaaaaa", Name = "Deliver", Kind = MyTask.Daily, Target = 100 };
        Assert.False(MyTask.IsWhole(imported));
    }

    [Fact]
    public void An_edit_changes_the_number_and_keeps_the_id_and_the_draft_carries_it()
    {
        // The settings page edits a copy (Draft) and saves it through MyTaskList.Edit, so the number travels both ways; turning
        // counting off is a draft with no number.
        var settings = new Settings();
        var task = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var draft = task.Draft();
        Assert.Equal(5, draft.Target);

        draft.Target = 3;
        Assert.True(settings.MyTasks.Edit(task.Id, draft));
        Assert.Equal(3, settings.MyTasks.Find(task.Id)!.Target);

        draft.Target = null;
        Assert.True(settings.MyTasks.Edit(task.Id, draft));
        Assert.False(settings.MyTasks.Find(task.Id)!.IsCounted);
        Assert.Equal(ItemKind.Done, settings.MyTasks.Find(task.Id)!.ToItem(0).Kind);
    }

    [Fact]
    public void A_click_adds_one_or_takes_one_back_from_the_count_held_now_within_0_and_the_number()
    {
        // CountValue stores the count held now plus the step, read now, so the reset counts from this click. It never goes past the
        // number or below 0. A count from before the task's own reset is 0, so the first click of a new day stores 1; a one-off's never resets.
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 3))!;
        var mounts = settings.AddMyTask(Tasks.Counted("Two mounts", MyTask.Once, 2))!;
        var now = Items.Now;

        Assert.Equal(new CounterValue(1, 3, now), deliver.CountValue(null, 1, now));
        Assert.Equal(new CounterValue(3, 3, now), deliver.CountValue(new CounterValue(2, 3, now), 1, now));
        Assert.Equal(new CounterValue(3, 3, now), deliver.CountValue(new CounterValue(3, 3, now), 1, now));      // at the number: stays
        Assert.Equal(new CounterValue(1, 3, now), deliver.CountValue(new CounterValue(2, 3, now), -1, now));
        Assert.Equal(new CounterValue(0, 3, now), deliver.CountValue(null, -1, now));                             // at 0: stays

        var yesterday = new CounterValue(3, 3, now - TimeSpan.FromDays(1));
        Assert.Equal(0, deliver.CountAt(yesterday, now));
        Assert.Equal(new CounterValue(1, 3, now), deliver.CountValue(yesterday, 1, now));
        Assert.Equal(1, mounts.CountAt(new CounterValue(1, 2, now - TimeSpan.FromDays(30)), now));                // a one-off never resets
        Assert.Equal(0, deliver.CountAt(new DoneValue(true, "daily:2026-09-12", now), now));                     // a plain tick is no count

        var chest = settings.AddMyTask(Tasks.Draft("FC chest", MyTask.Daily))!;
        Assert.Null(chest.CountValue(null, 1, now));                                                               // not counted: nothing
    }

    [Fact]
    public void A_lowered_number_counts_from_the_number_and_a_tick_marks_a_counted_task_done()
    {
        // Four clicks, then the number lowered to 3: the task holds 3, so Take one back leaves 2, not 3. TickValue, the "mark done"
        // store, fills a counted task to its number.
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 3))!;
        var four = new CounterValue(4, 5, Items.Now);
        Assert.Equal(3, deliver.CountAt(four, Items.Now));
        Assert.Equal(new CounterValue(2, 3, Items.Now), deliver.CountValue(four, -1, Items.Now));
        Assert.Equal(new CounterValue(3, 3, Items.Now), deliver.TickValue(Items.Now));
    }

    [Fact]
    public void The_count_a_click_starts_from_is_the_count_the_list_shows()
    {
        // MyTask.CountAt and the assembler apply one reset rule, so the number a click adds to is always the one on the row, across a
        // daily and a weekly boundary.
        var settings = new Settings();
        var deliver = settings.AddMyTask(Tasks.Counted("Deliver", MyTask.Daily, 5))!;
        var granary = settings.AddMyTask(Tasks.Counted("Granary", MyTask.Weekly, 5))!;
        var catalog = new Catalog(settings.MyTasks.Items());
        var record = Items.Record();
        var clicked = new DateTimeOffset(2026, 9, 12, 14, 59, 0, TimeSpan.Zero);
        record.Values[deliver.Id] = new CounterValue(2, 5, clicked);
        record.Values[granary.Id] = new CounterValue(2, 5, clicked);
        foreach (var hours in new[] { 0, 1, 24, 72, 96 })
        {
            var now = clicked + TimeSpan.FromHours(hours);
            var rows = Assembler.Assemble(catalog, record, settings, now).Sections.Single(s => s.Category == Category.MyTasks).Groups.Single().Entries;
            Assert.Equal($"{deliver.CountAt(record.Values[deliver.Id], now)} of 5", rows.Single(e => e.Item.Id == deliver.Id).Display);
            Assert.Equal($"{granary.CountAt(record.Values[granary.Id], now)} of 5", rows.Single(e => e.Item.Id == granary.Id).Display);
        }
    }
}
