using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>The first-run notice, the permanent privacy line, the settings inventory, the ignore lists, hidden characters, storage
/// problems, and the tick and window-read costs, along with export and import, the Roles section and the My tasks section. Drawn in the
/// Starlight palette and window style: the page is cards, General, Attention, Categories, Roles, My tasks, Ignored and hidden, and Data,
/// after the first-run notice while it is shown; each setting is a row, its label, a one-line dim description under it and its control at
/// the card's right; toggle switches replace the checkboxes, each under the checkbox's own label, so its id; the eight category switches
/// carry their category's icon and colour. Every control behaves as before.</summary>
public sealed class SettingsWindow(Plugin plugin) : Window("Mnemosyne settings##settings")
{
    /// <summary>A number field's width at 100 %, as the My tasks section's.</summary>
    private const float NumberWidth = 110f;

    /// <summary>The first-run notice's last words: how a character gets into the list, and how to open it.</summary>
    internal const string FirstRunHint = "Log into each character once and it appears in the list. Type /mnemo to open it.";

    /// <summary>A number being typed or stepped, by its field's id, until the field lets go (NumberRow).</summary>
    private static readonly Dictionary<string, int> Drafts = new();

    /// <summary>Made on the first draw. A field initialiser that read <c>plugin</c> would raise CS9124, since the primary constructor's parameter is
    /// also captured, and TreatWarningsAsErrors makes that an error.</summary>
    private RolesSection? _roles;

    /// <summary>Made on the first draw, as the Roles section is.</summary>
    private MyTasksSection? _myTasks;

    /// <summary>As the main window, the palette pushed before the window begins and popped after it ends.</summary>
    public override void PreDraw() => Starlight.Push();

    public override void PostDraw() => Starlight.Pop();

    public override void Draw()
    {
        var settings = plugin.Settings;
        if (!settings.FirstRunNoticeDismissed)
        {
            using var notice = Blocks.Card(Palette.Accent);
            using (ImRaii.TextWrapPos(notice.WrapPos))
                ImGui.TextWrapped(Plugin.PrivacySentence + " " + Plugin.ReadOnlySentence + " " + FirstRunHint);
            if (ImGui.Button("Got it")) plugin.UpdateSettings(s => s.FirstRunNoticeDismissed = true);
        }
        DrawGeneral(settings);
        DrawAttention(settings);
        DrawCategories(settings);
        using (var card = Blocks.Card())
        using (ImRaii.TextWrapPos(card.WrapPos))
            (_roles ??= new RolesSection(plugin)).Draw();
        using (var card = Blocks.Card())
        using (ImRaii.TextWrapPos(card.WrapPos))
            (_myTasks ??= new MyTasksSection(plugin)).Draw();
        DrawIgnored(settings);
        using (var card = Blocks.Card())
        using (ImRaii.TextWrapPos(card.WrapPos))
            plugin.Transfer.Draw();
    }

    /// <summary>The privacy line, permanent and verbatim, in green; the build, the records and the costs, dim; any storage problem in rose;
    /// then the four general settings.</summary>
    private void DrawGeneral(Settings settings)
    {
        using var card = Blocks.Card();
        using var wrap = ImRaii.TextWrapPos(card.WrapPos);
        Blocks.SectionLabel(plugin.Fonts.Label, "General");
        Wrapped(Plugin.PrivacySentence, Palette.Done);
        Wrapped($"Mnemosyne {plugin.PluginVersion} on game {plugin.GameVersion} · {plugin.Tracker.All.Count} character record(s)", Palette.TextDim);
        foreach (var problem in plugin.Store.Problems) Wrapped(problem, Palette.EndingSoon);
        if (plugin.Store.SettingsReadOnly) Wrapped("Settings will not be saved this session: the file could not be read or is from a newer build.", Palette.EndingSoon);
        // What the reads cost, for troubleshooting: closed until opened, so the page leads with what a player needs.
        using (var diagnostics = ImRaii.TreeNode("Diagnostics"))
        {
            if (diagnostics)
            {
                Wrapped($"Live tick: last {plugin.Ticks.LastUs} us, max {plugin.Ticks.MaxUs} us, average {plugin.Ticks.AverageUs:0} us over {plugin.Ticks.Count} ticks", Palette.TextDim);
                Wrapped($"Window reads: last {plugin.WindowTicks.LastUs} us, max {plugin.WindowTicks.MaxUs} us, average {plugin.WindowTicks.AverageUs:0} us over {plugin.WindowTicks.Count} fires", Palette.TextDim);
            }
        }
        Blocks.Hairline();

        var right = card.InnerRight;
        var staleness = settings.StalenessDays;
        if (NumberRow("Dim a character not seen for this many days", "Its tile, its row and its holdings are dimmed, and marked as possibly out of date.", ref staleness, right))
            plugin.UpdateSettings(s => s.StalenessDays = Math.Clamp(staleness, 1, 365));
        var soon = settings.EndingSoonDays;
        if (NumberRow("Ending soon means within this many days", "An event or a deadline this close to its end is listed under Ending soon.", ref soon, right))
            plugin.UpdateSettings(s => s.EndingSoonDays = Math.Clamp(soon, 1, 60));
        var weeklies = settings.WeekliesCount;
        if (ToggleRow("Weeklies count toward the badge", "When off, the badge counts only what is ending soon and what is due today.", ref weeklies, right))
            plugin.UpdateSettings(s => s.WeekliesCount = weeklies);
        var interval = settings.LiveReadSeconds;
        if (NumberRow("Live read interval, seconds", "How often the logged-in character's values are read again while you play.", ref interval, right))
            plugin.UpdateSettings(s => s.LiveReadSeconds = Math.Clamp(interval, 1, 60));
    }

    /// <summary>The login line, the info bar and the five reminders, with the must-stay-silent rule said once.</summary>
    private void DrawAttention(Settings settings)
    {
        using var card = Blocks.Card();
        using var wrap = ImRaii.TextWrapPos(card.WrapPos);
        Blocks.SectionLabel(plugin.Fonts.Label, "Attention");
        Wrapped("Nothing is said in chat during a duty, a cutscene or combat.", Palette.TextDim);
        var right = card.InnerRight;
        var loginLine = settings.LoginLine;
        if (ToggleRow("One chat line on login", "After you log in, one line saying how many things are to do, with a link that opens Mnemosyne's main window.", ref loginLine, right))
            plugin.UpdateSettings(s => s.LoginLine = loginLine);
        var infoBar = settings.InfoBar;
        if (ToggleRow("Server info bar counter", "The same count in the server info bar at the top of the screen; a click on it opens Mnemosyne's main window.", ref infoBar, right))
            plugin.UpdateSettings(s => s.InfoBar = infoBar);
        var r1 = settings.RemindDailyReset;
        if (ToggleRow("Remind: daily reset passed", "One chat line after the daily reset while something is still to do today.", ref r1, right))
            plugin.UpdateSettings(s => s.RemindDailyReset = r1);
        var r2 = settings.RemindEventEnding;
        if (ToggleRow("Remind: event ending", "One chat line when an event not done yet moves into Ending soon.", ref r2, right))
            plugin.UpdateSettings(s => s.RemindEventEnding = r2);
        var r3 = settings.RemindWondrousTails;
        if (ToggleRow("Remind: Wondrous Tails expiring", "One chat line when your Wondrous Tails journal is close to expiring.", ref r3, right))
            plugin.UpdateSettings(s => s.RemindWondrousTails = r3);
        var r4 = settings.RemindHouseVisit;
        if (ToggleRow("Remind: house visit due", "One chat line when the game's own warning says a house needs a visit.", ref r4, right))
            plugin.UpdateSettings(s => s.RemindHouseVisit = r4);
        var r5 = settings.RemindMyTasksBeforeReset;
        if (ToggleRow("Remind: My tasks before reset", "An hour before each daily and weekly reset, one line naming the My tasks still to do.", ref r5, right,
                "An hour before the daily and the weekly reset, one chat line naming the daily or weekly My tasks this character still has to do."))
            plugin.UpdateSettings(s => s.RemindMyTasksBeforeReset = r5);
    }

    /// <summary>The eight category switches carry their category's icon and colour: each a row, its glyph and its name in its colour, its
    /// toggle under the checkbox's label, the category's name.</summary>
    private void DrawCategories(Settings settings)
    {
        using var card = Blocks.Card();
        using var wrap = ImRaii.TextWrapPos(card.WrapPos);
        Blocks.SectionLabel(plugin.Fonts.Label, "Categories");
        Wrapped("A category switched off is left out of every character's list, the badge and All tasks.", Palette.TextDim);
        var right = card.InnerRight;
        foreach (var category in Enum.GetValues<Category>())
        {
            var on = settings.IsCategoryOn(category);
            var colour = Palette.Of(category);
            var start = ImGui.GetCursorScreenPos().X;
            ImGui.AlignTextToFramePadding();
            Blocks.Glyph(Glyphs.Of(category), colour);
            ImGui.SameLine(0f, Blocks.S(8f));
            var indent = ImGui.GetCursorScreenPos().X - start;
            if (Blocks.Toggle(category.Label(), ref on, right - ImGui.GetCursorScreenPos().X, colour))
                plugin.UpdateSettings(s => { if (on) s.CategoriesOff.Remove(category.ToString()); else s.CategoriesOff.Add(category.ToString()); });
            using (ImRaii.PushIndent(indent, scaled: false)) Describe(CategoryWords(category));
        }
    }

    /// <summary>The ignore lists with their clear buttons, and the hidden characters, each a button that shows it again.</summary>
    private void DrawIgnored(Settings settings)
    {
        using var card = Blocks.Card();
        using var wrap = ImRaii.TextWrapPos(card.WrapPos);
        Blocks.SectionLabel(plugin.Fonts.Label, "Ignored and hidden");
        var right = card.InnerRight;
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Palette.Text, $"Ignored everywhere: {settings.IgnoredEverywhere.Count}");
        if (settings.IgnoredEverywhere.Count > 0 && ButtonAtRight("Clear", right)) plugin.UpdateSettings(s => s.IgnoredEverywhere.Clear());
        Describe("Rows ignored on every character, from a row's right-click menu.");
        var hidden = plugin.Tracker.All.Where(r => r.Hidden).ToList();
        if (hidden.Count > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Palette.Text, "Hidden characters:");
            foreach (var record in hidden)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(record.Label + "##unhide" + record.ContentId)) plugin.Tracker.Hide(record.ContentId, false);
            }
            Describe("Click a name to show that character in the list again.");
        }
        var perCharacter = plugin.Tracker.All.Sum(r => r.Ignored.Count);
        if (perCharacter > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Palette.Text, $"Ignored on individual characters: {perCharacter}");
            if (ButtonAtRight("Clear all", right))
                foreach (var record in plugin.Tracker.All.ToList()) foreach (var key in record.Ignored.ToList()) plugin.Tracker.Ignore(record.ContentId, key, false);
            Describe("Rows ignored on one character only.");
        }
    }

    /// <summary>Each setting is a row: a label, a one-line dim description, and its control on the right: a toggle across the row, its label
    /// (the checkbox's own, so its id) at the left and its track at the card's right, and the description under it. A tooltip the setting
    /// had stays on the toggle; while the label is cut, the whole label comes first in it.</summary>
    private static bool ToggleRow(string label, string description, ref bool on, float right, string? tooltip = null)
    {
        var width = right - ImGui.GetCursorScreenPos().X;
        var clicked = Blocks.Toggle(label, ref on, width, Palette.Text);
        if (tooltip != null && ImGui.IsItemHovered()) ImGui.SetTooltip(Blocks.ToggleWidth(label) > width ? label + "\n" + tooltip : tooltip);
        Describe(description);
        return clicked;
    }

    /// <summary>A number setting's row, its words at the left, cut with "…" and whole in a tooltip when the card is narrow, its field at the
    /// card's right. The field's id is hidden ("##" + the label) rather than the label itself, since the row already draws the words at the
    /// left; ImGui then has no label of its own to lay out past the wrap edge, so no clip rect is needed and the window gains no width from
    /// it.</summary>
    private static bool NumberRow(string label, string description, ref int value, float right)
    {
        var width = Blocks.S(NumberWidth);
        var start = ImGui.GetCursorScreenPos();
        var (words, cut) = Blocks.Fit(label, MathF.Max(Blocks.S(24f), right - width - Blocks.S(12f) - start.X));
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Palette.Text, words);
        if (cut && ImGui.IsItemHovered()) ImGui.SetTooltip(label);
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(right - width, start.Y));
        ImGui.SetNextItemWidth(width);
        // While the field is active (a number being typed, or a step button held) the number is kept as a draft and nothing is saved; it is
        // applied once, when the field lets go: Enter, Tab, a click elsewhere, or a step button's click. Escape puts the old number back.
        var id = "##" + label;
        var shown = Drafts.GetValueOrDefault(id, value);
        var changed = ImGui.InputInt(id, ref shown, 1, 10);
        if (ImGui.IsItemActive())
        {
            if (changed) Drafts[id] = shown;
            changed = false;
        }
        else if (Drafts.Remove(id, out var draft))
        {
            if (!changed) shown = draft;
            changed = true;
        }
        if (changed) value = shown;
        Describe(description);
        return changed;
    }

    /// <summary>A small button at the card's right on the line just drawn, under its own label, so its id. SmallButton aligns itself to the
    /// line's text baseline by adding FramePadding.Y back once it is placed, so the cursor is set that much above the line's top to begin
    /// with; without it the button drew 4 px below its line.</summary>
    private static bool ButtonAtRight(string label, float right)
    {
        var top = ImGui.GetItemRectMin().Y - ImGui.GetStyle().FramePadding.Y;
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(right - ImGui.CalcTextSize(label).X - ImGui.GetStyle().FramePadding.X * 2f, top));
        return ImGui.SmallButton(label);
    }

    /// <summary>A setting's description: one dim line, wrapped at the card's edge when the window is narrow, then a little room.</summary>
    private static void Describe(string description)
    {
        Wrapped(description, Palette.TextDim);
        ImGui.Spacing();
    }

    private static void Wrapped(string text, Vector4 colour)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, colour)) ImGui.TextWrapped(text);
    }

    /// <summary>What each category holds, as a category switch's description (the groups the catalog builds under it).</summary>
    private static string CategoryWords(Category category) => category switch
    {
        Category.Story => "The main scenario, and the class, job, role and other quest lines.",
        Category.Jobs => "Each class and job's level, and your Grand Company.",
        Category.Events => "Seasonal events, collaboration quests and the Moogle Treasure Trove.",
        Category.Resets => "Dailies and weeklies: roulettes, hunts, Wondrous Tails, the challenge log and more.",
        Category.Content => "Housing, PvP, the Gold Saucer, field operations, deep dungeons and every duty.",
        Category.Collections => "Mounts, minions and more, the crafting, gathering and sightseeing logs, and achievements.",
        Category.Money => "Gil, tomestones and other currencies, retainers and storage.",
        _ => "The tasks you add by hand in the My tasks section below.",
    };
}
