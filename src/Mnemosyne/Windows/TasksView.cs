using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;
using Mnemosyne.Core.Time;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>One list across every character, drawn from the board Core builds over the sidebar's characters. Each group is drawn while it
/// holds a line. A line is a tree node; opened, it lists every character that owes the task, in sidebar order, with its own label, value and
/// marker, dimmed when stale, the logged-in one marked, and the sidebar's switch. A right-click adjusts the task for all characters at once,
/// and every adjustment is saved through Plugin.UpdateTasks, which leaves the checklist cache's version where it is. The Hidden (n) toggle at
/// the bottom shows the hidden and the snoozed lines, dimmed, with Unhide and Wake. Nothing here changes the badge, the login line, a
/// reminder or a character's own view. A character's name under a line opens that character's view through <paramref name="open"/>. Under a
/// My task's line each character has a real checkbox, as in its own list. Each group has a section label with its tone's dot and its count;
/// each line is a card with a stripe of its own group's tone, its name, its first characters as chips, its time as a pill and its pin and
/// priority as gold or muted marks; an open line lists its characters as entry rows with the switch as an icon. Every click, menu, tooltip
/// and id is the one it had: the "tasks" child, each line under its id with its "###line" node and "adjust" menu, each character under its
/// content id with its "##done" box, "count" menu, "##open" name and "Switch", and the "###hidden" toggle.</summary>
public sealed class TasksView(Plugin plugin, Action<ulong> open)
{
    private static readonly (string Label, TaskPriority Value)[] Priorities = [("High", TaskPriority.High), ("Normal", TaskPriority.Normal), ("Low", TaskPriority.Low)];
    private const string HiddenKey = "Hidden";     // not a BoardGroup, so it can never collide with one's own ToString()
    private const string Hint = "right-click a task to pin, prioritise, snooze or hide it";
    /// <summary>A line's tree node, its id "###line", as always (only the part from "###" makes the id), its text drawn over it, and its hit
    /// box the width of the card.</summary>
    private const ImGuiTreeNodeFlags LineFlags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap;
    /// <summary>The room a line's name keeps before its chips go, and the least a character's name under it keeps, at 100 %.</summary>
    private const float NameFloor = 80f, CharacterFloor = 60f;

    private bool _showHidden;
    // The order this page last drew, per section (a BoardGroup's name, or HiddenKey for the Hidden (n) lines), kept while
    // the pointer stays on the page and the window keeps focus, so a click under the cursor never resorts the lines under it. Board.Build's own
    // order (what decides membership and each line's own group) is untouched; only the order this page draws a section in is frozen. Null while
    // not frozen, so the board's own order is drawn. BoardOrder.Keep (Core) does the pure part; this class only decides when to start and stop.
    private Dictionary<string, List<string>>? _kept;

    public void Draw(IReadOnlyList<(CharacterRecord Record, Checklist Checklist)> characters, DateTimeOffset now, bool reduced)
    {
        var board = plugin.BoardFor(characters, now);
        Blocks.PageTitle(plugin.Fonts.Title, Glyphs.AllTasks, "All tasks", Hint);
        using var body = ImRaii.Child("tasks", Vector2.Zero, false);
        if (!body) return;

        // Read once a frame, as the sidebar reads them.
        var current = plugin.Tracker.Current?.ContentId;
        var switchable = plugin.Relog.IsAvailable;
        var silenced = plugin.Attention.Suppressed;
        var any = false;
        var changed = false;
        var drawn = new Dictionary<string, List<string>>();
        foreach (var section in board.Groups)
        {
            if (section.Lines.Count == 0) continue;
            any = true;
            // Pinned's dot is gold, a place rather than a status (Tones.OfGroup reads it Neutral); every other group's is its tone.
            GroupLabel(Board.Title(section.Group), section.Group == BoardGroup.Pinned ? Palette.Gold : Palette.Of(Tones.OfGroup(section.Group)), section.Lines.Count);
            foreach (var line in Ordered(section.Group.ToString(), section.Lines, drawn)) changed |= DrawLine(line, now, false, current, switchable, silenced, reduced);
            ImGui.Spacing();
        }
        // Every owed task can be hidden or snoozed at once, most often with the dailies, so "nothing to do" would be untrue.
        if (!any)
        {
            using var quiet = Blocks.Card(Palette.Quiet);
            Blocks.Glyph(Glyphs.Check, Palette.Quiet);
            ImGui.SameLine();
            ImGui.TextColored(Palette.Quiet, board.Hidden.Count > 0 ? $"Nothing to show; {board.Hidden.Count} hidden or snoozed below." : "Nothing to do on any character.");
        }

        // The "Hidden (n)" toggle: its label, quiet, and the switch that shows its lines, under the id the checkbox had, "###hidden" (only the
        // part from "###" makes the id, so the count in the label never moves it). The switch spans its label, drawn over its left part, so
        // a click on the words toggles it, as a click on the checkbox's words did.
        float words;
        using (plugin.Fonts.Label.Push())
            words = ImGui.CalcTextSize("HIDDEN").X + Blocks.S(6f) + ImGui.CalcTextSize(board.Hidden.Count.ToString(CultureInfo.InvariantCulture)).X;
        var start = ImGui.GetCursorScreenPos();
        Blocks.Toggle("###hidden", ref _showHidden, Blocks.S(14f) + words + Blocks.S(10f) + Blocks.ToggleWidth("###hidden"));
        var after = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(start);
        ImGui.AlignTextToFramePadding();
        GroupLabel("Hidden", Palette.Quiet, board.Hidden.Count);
        ImGui.SetCursorScreenPos(after);
        if (_showHidden)
        {
            foreach (var line in Ordered(HiddenKey, board.Hidden, drawn)) changed |= DrawLine(line, now, true, current, switchable, silenced, reduced);
        }

        // Freeze the order drawn above for the next frame while the pointer stays over this child and Mnemosyne's own window keeps focus,
        // starting the freeze the frame a checkbox or a line's menu is clicked (changed) and holding it while already frozen (_kept != null),
        // even with no further click. AllowWhenBlockedByPopup so the line's own right-click menu, itself a popup over this child, does not read
        // as the pointer having left it.
        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByPopup) && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootWindow);
        _kept = hovered && (changed || _kept != null) ? drawn : null;
    }

    /// <summary>A group's section label, its tone's dot and its count, e.g. "● TODAY 3", the dot centred on the label's text.</summary>
    private void GroupLabel(string title, Vector4 colour, int count)
    {
        var at = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(at + new Vector2(Blocks.S(14f), 0f));
        Blocks.SectionLabel(plugin.Fonts.Label, title, count);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        Blocks.DotAt(ImGui.GetWindowDrawList(), new Vector2(at.X + Blocks.S(5f), (min.Y + max.Y) * 0.5f), colour);
    }

    /// <summary>This section's lines in the kept order (BoardOrder.Keep), falling back to the board's own while not frozen, and this frame's
    /// drawn order recorded into <paramref name="drawn"/> so Draw can decide whether to keep it for the next frame.</summary>
    private IReadOnlyList<TaskLine> Ordered(string key, IReadOnlyList<TaskLine> lines, Dictionary<string, List<string>> drawn)
    {
        var ids = lines.Select(l => l.Id).ToList();
        var order = _kept != null && _kept.TryGetValue(key, out var previous) ? BoardOrder.Keep(previous, ids) : ids;
        drawn[key] = [.. order];
        var byId = lines.ToDictionary(l => l.Id);
        return order.Select(id => byId[id]).ToList();
    }

    /// <summary>One task line as a card and, while it is open, its characters. The card's stripe is the tone of the line's own group (a
    /// pinned line keeps it, Tones.OfLine), quiet for a hidden or snoozed line, whose header is also drawn faded; its menu and its characters
    /// never are. The tree node is ImGui's own under "###line", as before, its own text and fill drawn transparent and the header drawn over
    /// it, so the time in its words can change without closing it. Returns whether a checkbox or a menu choice under this line fired this
    /// frame, for Draw to decide whether to start or keep the order frozen.</summary>
    private bool DrawLine(TaskLine line, DateTimeOffset now, bool dimmed, ulong? current, bool switchable, bool silenced, bool reduced)
    {
        using var id = ImRaii.PushId(line.Id);
        using var card = Blocks.Card(dimmed ? Palette.Quiet : Palette.Of(Tones.OfLine(line)));
        var invisible = Blocks.Invisible();
        using var node = ImRaii.TreeNode("###line", LineFlags);
        invisible.Dispose();
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var changed = false;
        using (var menu = ImRaii.ContextPopupItem("adjust"))
        {
            if (menu) changed = DrawMenu(line, now);
        }
        DrawHeader(line, now, dimmed, node, hovered, min, max, card.InnerRight, current);
        if (!node) return changed;
        var tone = Tones.OfLine(line);
        foreach (var character in line.Characters) changed |= DrawCharacter(character, tone, current, switchable, silenced, card.InnerRight, now, reduced);
        return changed;
    }

    /// <summary>Each line is a card, its header: the chevron, the name, the first characters as chips (the logged-in one gold), and at the
    /// right the time as a pill in its tone (PageLook.OfTime), a snooze and "hidden" as quiet pills, a gold ▲ for High or a muted ▼ for Low,
    /// and a gold pin when pinned. When the line is short the chips give way first, one name at a time, the "+n" chip counting those not
    /// named, then the "+n" chip itself; the name is cut last, and left out when less room is left than a short word. The whole words
    /// (BoardText.Heading, the text form's own: the name, how many characters, the time and the adjustments) are its tooltip when the name is
    /// cut or left out or no chip is left, and a mark's tooltip says what it means, so no status is colour alone.</summary>
    private void DrawHeader(TaskLine line, DateTimeOffset now, bool dimmed, bool open, bool hovered, Vector2 min, Vector2 max, float right, ulong? current)
    {
        var drawList = ImGui.GetWindowDrawList();
        var text = ImGui.GetTextLineHeight();
        var y = min.Y + (max.Y - min.Y - text) * 0.5f;
        var gap = Blocks.S(6f);
        if (hovered) drawList.AddRectFilled(new Vector2(min.X - Blocks.S(4f), min.Y), new Vector2(right + Blocks.S(4f), max.Y), Palette.U32(Palette.CardHover), Blocks.S(6f));
        string? markTip = null;
        bool cut, chipless;
        using (Blocks.Faded(dimmed ? Blocks.StaleAlpha : 1f))
        {
            Blocks.GlyphAt(drawList, new Vector2(min.X, y), open ? Glyphs.Open : Glyphs.Closed, Palette.TextDim);
            var x = min.X + Blocks.S(20f);

            // The right end, from the right: the time, the quiet words, then the marks.
            var end = right;
            if (line.When.Length > 0) end = PillFromRight(drawList, end, min.Y, max.Y, line.When, Palette.Of(PageLook.OfTime(line))) - gap;
            if (line.Preference.Hidden) end = PillFromRight(drawList, end, min.Y, max.Y, "hidden", Palette.Quiet) - gap;
            if (PageLook.Snoozed(line.Preference, now) is { } snoozed) end = PillFromRight(drawList, end, min.Y, max.Y, snoozed, Palette.Quiet) - gap;
            if (line.Preference.Priority == TaskPriority.Low) end = MarkFromRight(drawList, end, y, Glyphs.Down, Palette.TextMuted, "low priority", ref markTip) - gap;
            else if (line.Preference.Priority == TaskPriority.High) end = MarkFromRight(drawList, end, y, Glyphs.Up, Palette.Gold, "high priority", ref markTip) - gap;
            if (line.Preference.Pinned) end = MarkFromRight(drawList, end, y, Glyphs.Pin, Palette.Gold, "pinned", ref markTip) - gap;

            // The name and the chips in what is left.
            var room = end - x;
            var (chipNames, _) = PageLook.Chips(line);
            // Colour is never the only signal; the logged-in character's chip carries the "●" dot
            // too, as its other appearances do, and its width is folded in here so the chips still give way in the right order.
            var names = chipNames.Select((chipName, i) => line.Characters[i].Record.ContentId == current ? "● " + chipName : chipName).ToList();
            var nameWidth = ImGui.CalcTextSize(line.Name).X;
            var shown = names.Count;
            while (shown > 0 && nameWidth + ChipsWidth(names, shown, line.Count) > room) shown--;
            var chips = ChipsWidth(names, shown, line.Count);
            if (room - chips < MathF.Min(nameWidth, Blocks.S(NameFloor))) chips = -1f;         // no room for even the "+n" chip
            // With less room than a short word, the name is left out rather than drawn over the pills; its tooltip carries the whole words.
            var nameRoom = room - MathF.Max(chips, 0f);
            if (nameRoom < Blocks.S(24f)) cut = true;
            else
            {
                (var name, cut) = Blocks.Fit(line.Name, nameRoom);
                drawList.AddText(new Vector2(x, y), Palette.U32(Palette.Text), name);
                x += ImGui.CalcTextSize(name).X;
            }
            chipless = chips < 0f;
            if (!chipless)
            {
                for (var i = 0; i < shown; i++)
                {
                    var gold = line.Characters[i].Record.ContentId == current;
                    x = PillFromLeft(drawList, x + gap, min.Y, max.Y, names[i], gold ? Palette.Gold : Palette.TextSecondary);
                }
                if (shown < line.Count) PillFromLeft(drawList, x + gap, min.Y, max.Y, "+" + (line.Count - shown).ToString(CultureInfo.InvariantCulture), Palette.TextSecondary);
            }
        }
        // IsMouseHoveringRect alone does not check occlusion, so a mark's tooltip could pop up over an open right-click menu
        // or the header's own popup; the line's own `hovered` (IsItemHovered on the node) is required too.
        if (markTip != null && hovered) ImGui.SetTooltip(markTip);
        else if (hovered && (cut || chipless)) ImGui.SetTooltip(BoardText.Heading(line, now));
    }

    /// <summary>The width the chips take after the name: the first <paramref name="shown"/> names, then "+n" for the rest, each after a gap.</summary>
    private static float ChipsWidth(IReadOnlyList<string> names, int shown, int count)
    {
        var gap = Blocks.S(6f);
        var width = 0f;
        for (var i = 0; i < shown; i++) width += gap + Blocks.PillSize(names[i]).X;
        if (shown < count) width += gap + Blocks.PillSize("+" + (count - shown).ToString(CultureInfo.InvariantCulture)).X;
        return width;
    }

    /// <summary>A pill ending at <paramref name="right"/>, centred on the header; returns its left edge.</summary>
    private static float PillFromRight(ImDrawListPtr drawList, float right, float top, float bottom, string text, Vector4 tone)
    {
        var size = Blocks.PillSize(text);
        Blocks.PillAt(drawList, new Vector2(right - size.X, top + (bottom - top - size.Y) * 0.5f), text, tone);
        return right - size.X;
    }

    /// <summary>A pill starting at <paramref name="left"/>, centred on the header; returns its right edge.</summary>
    private static float PillFromLeft(ImDrawListPtr drawList, float left, float top, float bottom, string text, Vector4 tone)
    {
        var size = Blocks.PillSize(text);
        Blocks.PillAt(drawList, new Vector2(left, top + (bottom - top - size.Y) * 0.5f), text, tone);
        return left + size.X;
    }

    /// <summary>A mark (the pin, ▲ or ▼) ending at <paramref name="right"/>; its words become the tooltip while the mouse is on it. Returns its
    /// left edge.</summary>
    private static float MarkFromRight(ImDrawListPtr drawList, float right, float y, FontAwesomeIcon glyph, Vector4 colour, string words, ref string? tip)
    {
        var size = Blocks.GlyphSize(glyph);
        var at = new Vector2(right - size.X, y);
        Blocks.GlyphAt(drawList, at, glyph, colour);
        if (ImGui.IsMouseHoveringRect(at, at + size)) tip = words;
        return at.X;
    }

    /// <summary>Pin or Unpin; Priority, with High, Normal and Low, the current one ticked; Snooze until next reset, or Wake while snoozed;
    /// Hide, or Unhide while hidden. Each choice is one save. Returns whether one fired.</summary>
    private bool DrawMenu(TaskLine line, DateTimeOffset now)
    {
        var id = line.Id;
        var preference = line.Preference;
        var changed = false;
        if (preference.Pinned) { if (ImGui.MenuItem("Unpin")) { plugin.UpdateTasks(tasks => tasks.Unpin(id)); changed = true; } }
        else if (ImGui.MenuItem("Pin")) { plugin.UpdateTasks(tasks => tasks.Pin(id)); changed = true; }
        using (var priority = ImRaii.Menu("Priority"))
        {
            if (priority)
            {
                foreach (var (label, value) in Priorities)
                {
                    if (ImGui.MenuItem(label, preference.Priority == value)) { plugin.UpdateTasks(tasks => tasks.SetPriority(id, value)); changed = true; }
                }
            }
        }
        if (preference.SnoozedAt(now)) { if (ImGui.MenuItem("Wake")) { plugin.UpdateTasks(tasks => tasks.Wake(id)); changed = true; } }
        else
        {
            // The menu names the time it snoozes to, from the line's own next reset, or the daily reset for a
            // line with none (TaskPreferences.Snooze takes the same fallback), with the existing relative-time helper.
            var until = line.NextReset ?? ResetSchedules.NextReset(ResetSchedules.Daily, now);
            if (ImGui.MenuItem($"Snooze until next reset ({Relative.Until(until, now)})")) { plugin.UpdateTasks(tasks => tasks.Snooze(id, line.NextReset, now)); changed = true; }
        }
        if (preference.Hidden) { if (ImGui.MenuItem("Unhide")) { plugin.UpdateTasks(tasks => tasks.Unhide(id)); changed = true; } }
        else if (ImGui.MenuItem("Hide")) { plugin.UpdateTasks(tasks => tasks.Hide(id)); changed = true; }
        return changed;
    }

    /// <summary>One character under a line as an entry row: a My task's box, the gold dot of the logged-in character and its name, which
    /// opens its own list, its own label for the task, its marker as a pill, a thin bar for a count with a cap and its value at the right,
    /// and the switch as an icon at the row's end. The switch is always there, under its id, with its click, tooltip and keyboard path as
    /// before; it is faint until the row is under the mouse. A stale character's words are faded, never its box, its menus, its tooltips or
    /// its switch. Returns whether its checkbox fired.</summary>
    private bool DrawCharacter(OwingCharacter character, Tone tone, ulong? current, bool switchable, bool silenced, float right, DateTimeOffset now, bool reduced)
    {
        var record = character.Record;
        var entry = character.Entry;
        using var id = ImRaii.PushId(record.ContentId.ToString());
        var state = SwitchRules.State(switchable, record, current, silenced);
        var rowMin = ImGui.GetCursorScreenPos();
        var rowHeight = ImGui.GetFrameHeight();
        var rowHovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(rowMin, new Vector2(right, rowMin.Y + rowHeight));
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var end = right - (state != SwitchState.Hidden ? rowHeight + spacing : 0f);
        var changed = false;
        ImGui.AlignTextToFramePadding();
        // Beside each character under a My task's line, the same real checkbox as in its own
        // list. A character under a line owes the task, so the box reads unticked, and a tick takes the character off the line.
        if (entry.Item.Freshness.Kind == FreshnessKind.Manual)
        {
            var done = entry.IsDone;
            if (Blocks.DoneBox(ref done, clickable: true)) { plugin.SetTaskDone(record, entry.Item.Id, done); changed = true; }
            // A counted task's box adds one per click, and its row shows the character's own count; a right-click on the box takes one
            // back or resets it, as in the character's own list.
            if (entry.Item.Kind == ItemKind.Counter)
            {
                using var popup = ImRaii.ContextPopupItem("count");
                if (popup)
                {
                    if (ImGui.MenuItem("Take one back")) { plugin.SetTaskDone(record, entry.Item.Id, false); changed = true; }
                    if (ImGui.MenuItem("Reset to 0")) { plugin.ResetTask(record, entry.Item.Id); changed = true; }
                }
            }
            ImGui.SameLine();
        }

        // The room: the value keeps the right end, at most a third of the row; the character's name its whole width, or at most a third; the
        // bar only when the rest fits; then the marker is cut to its floor, and the task's own label last.
        var start = ImGui.GetCursorScreenPos().X;
        var avail = end - start;
        var (value, valueCut) = Blocks.Fit(entry.Display, avail / 3f);
        var valueRoom = value.Length > 0 ? ImGui.CalcTextSize(value).X + spacing : 0f;
        var loggedIn = current == record.ContentId;
        var dotRoom = loggedIn ? Blocks.S(14f) : 0f;
        var (name, nameCut) = Blocks.Fit(record.Label, MathF.Max(Blocks.S(CharacterFloor), avail / 3f - dotRoom));
        var nameRoom = dotRoom + ImGui.CalcTextSize(name).X + spacing;
        var marker = entry.Marker;
        var markerWidth = marker.Length > 0 ? Blocks.PillSize(marker).X + spacing : 0f;
        var meter = Tones.MeterOf(entry);
        var barWidth = Blocks.S(Blocks.RowBarWidth);
        var labelWidth = ImGui.CalcTextSize(entry.Name).X + spacing;
        var showBar = meter != null && nameRoom + labelWidth + markerWidth + barWidth + spacing + valueRoom <= avail;
        var room = avail - valueRoom - (showBar ? barWidth + spacing : 0f) - nameRoom;
        var markerCut = false;
        if (markerWidth > 0f && Blocks.S(CharacterFloor) + markerWidth > room)
        {
            var pill = MathF.Max(Blocks.S(40f), room - Blocks.S(CharacterFloor) - spacing);
            (marker, markerCut) = Blocks.Fit(entry.Marker, pill - Blocks.S(Blocks.PillPadding) * 2f);
            markerWidth = Blocks.PillSize(marker).X + spacing;
        }
        var (label, labelCut) = Blocks.Fit(entry.Name, MathF.Max(Blocks.S(24f), room - markerWidth));

        // The name: its Selectable keeps the label it always had ("● " before the logged-in character's), so its id; its own text and highlight
        // are drawn transparent and the gold dot and the name drawn over it, in the accent under the mouse, as a link.
        var shownLabel = (loggedIn ? "● " : "") + record.Label;
        bool clicked;
        using (Blocks.Invisible())
            clicked = ImGui.Selectable(shownLabel + "##open", false, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(nameRoom - spacing, 0f));
        if (clicked) open(record.ContentId);
        var linkHovered = ImGui.IsItemHovered();
        if (linkHovered) ImGui.SetTooltip(nameCut ? $"Open {record.Label}'s own list\n{record.Label}" : $"Open {record.Label}'s own list");
        var (linkMin, linkMax) = Blocks.Visible(ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        var drawList = ImGui.GetWindowDrawList();
        // A tooltip is found inside the fade and shown after it, so it is never dimmed (as the sidebar's tile does).
        string? tip = null;
        using (Blocks.Faded(character.Stale ? Blocks.StaleAlpha : 1f))
        {
            var textY = linkMin.Y + (linkMax.Y - linkMin.Y - ImGui.GetTextLineHeight()) * 0.5f;
            if (loggedIn) Blocks.DotAt(drawList, new Vector2(linkMin.X + Blocks.S(4f), textY + ImGui.GetTextLineHeight() * 0.5f), Palette.Gold, glow: true);
            drawList.AddText(new Vector2(linkMin.X + dotRoom, textY), Palette.U32(linkHovered ? Palette.Accent : Palette.Text), name);

            ImGui.SameLine();
            ImGui.TextColored(Palette.TextSecondary, label);
            if (labelCut && ImGui.IsItemHovered()) tip = entry.Name;
            if (marker.Length > 0)
            {
                ImGui.SameLine();
                Blocks.Pill(marker, Palette.Of(Tones.OfMarker(entry)));
                if (markerCut && ImGui.IsItemHovered()) tip = entry.Marker;
            }
            if (showBar && meter is { } bar)
            {
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(end - valueRoom - barWidth, rowMin.Y));
                ImGui.Dummy(new Vector2(barWidth, rowHeight));
                var barHeight = Blocks.S(Blocks.RowBarHeight);
                Blocks.Bar(drawList, new Vector2(end - valueRoom - barWidth, rowMin.Y + (rowHeight - barHeight) * 0.5f), new Vector2(barWidth, barHeight), bar.Fraction,
                    Palette.BarOf(tone));
            }
            if (value.Length > 0)
            {
                ImGui.SameLine();
                ImGui.SetCursorScreenPos(new Vector2(end - valueRoom + spacing, rowMin.Y));
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(Palette.TextSecondary, value);
                if (valueCut && ImGui.IsItemHovered()) tip = entry.Display;
            }
        }
        if (tip != null) ImGui.SetTooltip(tip);
        if (state != SwitchState.Hidden)
            SwitchIcon.Draw(plugin, record, state, new Vector2(right - rowHeight, rowMin.Y), rowHeight, now, reduced, rowHovered ? null : Palette.Fade(Palette.Quiet, 0.7f));
        return changed;
    }
}
