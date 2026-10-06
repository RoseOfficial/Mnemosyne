using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;
using Mnemosyne.Core.Time;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>One character's page, moved out of MainWindow and drawn with the building blocks. A header card (the name in the game's Axis font,
/// world and data centre, the role, when the character was seen, the summary pills, the switch), the search box with its magnifier, Ending
/// soon, Today, This week and Running events as cards, urgency first, then each category as a collapsible card of its colour. Every click,
/// menu, tooltip and id is the one MainWindow always had: the search box "##search", the "checklist" child, each top group under its title's
/// id, each row under its item's id with its "##done" box, the "ignore", "count" and "count-text" menus, each category's header label and id,
/// "Show done", and each group's "###" id. The tones, the bars and the tallies are Core's (Tones, ChecklistLook), read from the checklist the
/// cache holds; this class only draws them.</summary>
public sealed class CharacterPage(Plugin plugin)
{
    private const string ShowDone = "Show done";
    private const string Locked = "not unlocked yet";
    private const string ObservedWords = "Counted only while Mnemosyne was running; anything done with it off is not seen.";
    /// <summary>The room a row's name keeps before its marker is cut, and the least a cut marker keeps, at 100 %.</summary>
    private const float NameFloor = 60f, MarkerFloor = 40f;

    private readonly Dictionary<Category, bool> _showDone = new();
    private string _query = "";
    /// <summary>The last search: the checklist it filtered, the words, and what it kept. A checklist is rebuilt only when its record, the
    /// settings or the minute moved, so while the words stay the same the search is not run again on every frame.</summary>
    private (Checklist Source, string Query, Checklist Shown)? _searched;

    public void Draw(CharacterRecord record, Checklist checklist, DateTimeOffset now, bool reduced)
    {
        // The list always shows its scrollbar, and the header card and the search box stop where the list's cards stop, left of
        // it, so their right edges line up whether the list scrolls or not.
        var width = ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ScrollbarSize;
        DrawHeader(record, checklist, width, now, reduced);
        DrawSearch(width);
        // The look of the checklist as assembled, kept with it; a search keeps its entries, so their tones are found in it, and
        // the bars read its tallies, never the search's.
        var look = ChecklistLook.For(checklist);
        var shown = Searched(checklist);
        var searching = _query.Trim().Length > 0;
        using var body = ImRaii.Child("checklist", Vector2.Zero, false, ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (!body) return;

        // The cards follow urgency. The text form, the dump and the goldens keep their own order.
        DrawTopGroup("Ending soon", Tone.EndingSoon, "Nothing ending soon", shown.EndingSoon, checklist.EndingSoon.Count, searching, record, look, now, reduced);
        DrawTopGroup("Today", Tone.Today, "Nothing due today", shown.Today, checklist.Today.Count, searching, record, look, now, reduced);
        DrawTopGroup("This week", Tone.ThisWeek, "Nothing due this week", shown.ThisWeek, checklist.ThisWeek.Count, searching, record, look, now, reduced);
        DrawTopGroup("Running events", Tone.Running, "No events running", shown.Running, checklist.Running.Count, searching, record, look, now, reduced);
        ImGui.Spacing();
        foreach (var section in shown.Sections) DrawSection(section, record, look, now, reduced);
    }

    private Checklist Searched(Checklist checklist)
    {
        if (_searched is { } kept && ReferenceEquals(kept.Source, checklist) && kept.Query == _query) return kept.Shown;
        var shown = Search.Filter(checklist, _query);
        _searched = (checklist, _query, shown);
        return shown;
    }

    /// <summary>A header card: the name in the large font, the role pill and the switch at the top right; then world · data centre, "seen 5
    /// min ago" after a green dot, or a grey one and "· stale" when stale (the words say it too), and the summary pills on that line when
    /// they fit, else on their own. A name too long for the card is cut with "…" and shown whole in a tooltip.</summary>
    private void DrawHeader(CharacterRecord record, Checklist checklist, float width, DateTimeOffset now, bool reduced)
    {
        using var card = Blocks.Card(Palette.Accent, width);
        var state = SwitchRules.State(plugin.Relog.IsAvailable, record, plugin.Tracker.Current?.ContentId, plugin.Attention.Suppressed);
        var switchSide = ImGui.GetFrameHeight();
        var role = plugin.Settings.RoleOf(record)?.Name ?? (string.IsNullOrEmpty(record.Role) ? null : Summariser.UnknownRole);
        // A role name can run to 31 characters, so the chip is cut to at most half the header's line (Blocks.Fit) rather than
        // reserving its whole width, which would leave the name unreadable or run the chip past the switch and the card's edge; the whole
        // name is shown in a tooltip on the chip itself.
        string? roleText = role;
        var roleCut = false;
        if (role != null) (roleText, roleCut) = Blocks.Fit(role, width * 0.5f);
        var roleWidth = role != null ? Blocks.PillSize(roleText!).X + Blocks.S(10f) : 0f;
        var nameRight = card.InnerRight - (state != SwitchState.Hidden ? switchSide + Blocks.S(8f) : 0f) - roleWidth;
        bool cut, hovered;
        Vector2 nameMin, nameMax;
        using (plugin.Fonts.Title.Push())
        {
            (var name, cut) = Blocks.Fit(record.Label, nameRight - ImGui.GetCursorScreenPos().X);
            ImGui.TextColored(Palette.Text, name);
            hovered = ImGui.IsItemHovered();
            nameMin = ImGui.GetItemRectMin();
            nameMax = ImGui.GetItemRectMax();
        }
        if (cut && hovered) ImGui.SetTooltip(record.Label);
        var after = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        if (role != null)
        {
            var pillMin = new Vector2(nameMax.X + Blocks.S(10f), nameMin.Y + (nameMax.Y - nameMin.Y - Blocks.PillSize(roleText!).Y) * 0.5f);
            var pillSize = Blocks.PillSize(roleText!);
            Blocks.PillAt(drawList, pillMin, roleText!, role == Summariser.UnknownRole ? Palette.Quiet : Palette.TextSecondary);
            if (roleCut && ImGui.IsMouseHoveringRect(pillMin, pillMin + pillSize)) ImGui.SetTooltip(role);
        }
        if (state != SwitchState.Hidden)
        {
            SwitchIcon.Draw(plugin, record, state, new Vector2(card.InnerRight - switchSide, nameMin.Y + (nameMax.Y - nameMin.Y - switchSide) * 0.5f), switchSide, now, reduced);
            ImGui.SetCursorScreenPos(after);
        }

        var place = record.DataCenter.Length > 0 ? $"{record.HomeWorld} · {record.DataCenter}" : record.HomeWorld;
        var seen = "seen " + Relative.Since(record.LastSeen, now) + (checklist.Stale ? " · stale" : "");
        var dotRoom = Blocks.S(22f);
        var pills = Summary(checklist);
        var pillsWidth = pills.Sum(p => Blocks.PillSize(p.Text).X) + Blocks.S(6f) * (pills.Count - 1);
        var lineWidth = ImGui.CalcTextSize(place).X + dotRoom + ImGui.CalcTextSize(seen).X;
        var together = lineWidth + Blocks.S(16f) + pillsWidth <= card.InnerRight - ImGui.GetCursorScreenPos().X;
        // One line for all of it when it fits, so the list below keeps its room; the text then sits at the pills' height.
        if (together) ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Palette.TextSecondary, place);
        ImGui.SameLine(0f, dotRoom);
        ImGui.TextColored(Palette.TextDim, seen);
        var seenMin = ImGui.GetItemRectMin();
        var seenMax = ImGui.GetItemRectMax();
        Blocks.DotAt(drawList, new Vector2(seenMin.X - Blocks.S(11f), (seenMin.Y + seenMax.Y) * 0.5f), checklist.Stale ? Palette.Quiet : Palette.Done);
        for (var i = 0; i < pills.Count; i++)
        {
            if (i > 0 || together) ImGui.SameLine(0f, i == 0 ? Blocks.S(16f) : Blocks.S(6f));
            Blocks.Pill(pills[i].Text, pills[i].Tone);
        }
    }

    /// <summary>The summary pills: in urgency order, "1 ending soon", "3 today", "2 this week", those above zero, counted over the whole
    /// checklist whatever the search; "all clear" when none is.</summary>
    private static List<(string Text, Vector4 Tone)> Summary(Checklist checklist)
    {
        var pills = new List<(string Text, Vector4 Tone)>(3);
        if (checklist.EndingSoon.Count > 0) pills.Add(($"{checklist.EndingSoon.Count} ending soon", Palette.EndingSoon));
        if (checklist.Today.Count > 0) pills.Add(($"{checklist.Today.Count} today", Palette.Today));
        if (checklist.ThisWeek.Count > 0) pills.Add(($"{checklist.ThisWeek.Count} this week", Palette.ThisWeek));
        if (pills.Count == 0) pills.Add(("all clear", Palette.Done));
        return pills;
    }

    /// <summary>The search box below the header, with a magnifier: the box under its id "##search", as wide as the header card, its text
    /// moved right to make room for the magnifier drawn inside it.</summary>
    private void DrawSearch(float width)
    {
        var padding = ImGui.GetStyle().FramePadding;
        var glyph = Blocks.GlyphSize(Glyphs.Search).X;
        ImGui.SetNextItemWidth(width);
        using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(padding.X * 2f + glyph, padding.Y)))
            ImGui.InputTextWithHint("##search", "search everything", ref _query, 64);
        Blocks.GlyphAt(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin() + padding, Glyphs.Search, Palette.TextDim);
        ImGui.Spacing();
    }

    /// <summary>A top group as a card with its tone's edge and a section label with its count, or, empty, one quiet line with a check
    /// ("Nothing due today"). While a search is typed, a group that holds rows but none that match says "No match" with the magnifier instead,
    /// so it never contradicts the header's pills. Under its title's id, as always.</summary>
    private void DrawTopGroup(string title, Tone tone, string empty, IReadOnlyList<Entry> entries, int all, bool searching, CharacterRecord record, ChecklistLook look,
        DateTimeOffset now, bool reduced)
    {
        using var id = ImRaii.PushId(title);
        if (entries.Count == 0)
        {
            var unmatched = searching && all > 0;
            using var quiet = Blocks.Card(Palette.Quiet);
            Blocks.Glyph(unmatched ? Glyphs.Search : Glyphs.Check, Palette.Quiet);
            ImGui.SameLine();
            ImGui.TextColored(Palette.Quiet, unmatched ? "No match" : empty);
            return;
        }
        using var card = Blocks.Card(Palette.Of(tone));
        Blocks.SectionLabel(plugin.Fonts.Label, title, entries.Count);
        foreach (var entry in entries) DrawEntry(entry, record, look, card.InnerRight, entry.Item.IconId != null, now, reduced);
    }

    /// <summary>Each category is a collapsible card: its icon and colour, its name, and a done/total bar; "read 5 min ago" on hover as before;
    /// "Show done" as a toggle in the header while it is open; each group with a small done/total bar; "(unavailable)" and its reason in rose.
    /// The header is ImGui's own CollapsingHeader under the label it always had (so its id and open state are the ones it had), its own text
    /// and fill drawn transparent and the card's header drawn over it. The bars count the rows that can be finished, over the whole category
    /// and group whatever the search; a card or group with none has no bar.</summary>
    private void DrawSection(Section section, CharacterRecord record, ChecklistLook look, DateTimeOffset now, bool reduced)
    {
        var category = section.Category;
        var disabledReason = plugin.Tracker.DisabledReason(category);
        var title = category.Label();
        if (disabledReason != null) title += " (unavailable)";
        var colour = Palette.Of(category);
        using var card = Blocks.Card(colour);
        bool open;
        using (Blocks.Invisible()) open = ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.AllowItemOverlap);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var left = ImGui.GetCursorScreenPos().X;
        var right = card.InnerRight;
        var line = ImGui.GetTextLineHeight();
        var toggleWidth = Blocks.ToggleWidth(ShowDone);
        var tally = look.Of(category);
        var barSize = new Vector2(Blocks.S(Blocks.HeaderBarWidth), line);
        var barMin = new Vector2(right - toggleWidth - Blocks.S(12f) - barSize.X, min.Y + (max.Y - min.Y - barSize.Y) * 0.5f);
        var nameEnd = (tally.Total > 0 ? barMin.X : right - toggleWidth) - Blocks.S(8f);
        // The header's hover, its fill and its tooltip, is the part left of the bar; the bar and the toggle are not the header's.
        var hovered = ImGui.IsItemHovered() && ImGui.IsMouseHoveringRect(new Vector2(left - Blocks.S(4f), min.Y), new Vector2(nameEnd, max.Y));
        // Each category's own read time, on hover. The Resets category is the first where a value can be
        // hours old while the record itself was seen a minute ago.
        if (hovered)
        {
            // My tasks are ticked by hand, so no reader stamps the category.
            ImGui.SetTooltip(category == Category.MyTasks ? "ticked by hand, per character"
                : record.CategoryReadAt.TryGetValue(category.ToString(), out var readAt) ? "read " + Relative.Since(readAt, now)
                : "not read yet");
        }
        var drawList = ImGui.GetWindowDrawList();
        if (hovered) drawList.AddRectFilled(new Vector2(left - Blocks.S(4f), min.Y), new Vector2(right + Blocks.S(4f), max.Y), Palette.U32(Palette.CardHover), Blocks.S(6f));
        var y = min.Y + (max.Y - min.Y - line) * 0.5f;
        Blocks.GlyphAt(drawList, new Vector2(left, y), open ? Glyphs.Open : Glyphs.Closed, Palette.TextDim);
        Blocks.GlyphAt(drawList, new Vector2(left + Blocks.S(20f), y), Glyphs.Of(category), colour);
        // The name, and "(unavailable)" in rose, are cut to the room left of the bar.
        var nameX = left + Blocks.S(44f);
        var label = category.Label();
        var (shownName, _) = Blocks.Fit(disabledReason != null ? label + " (unavailable)" : label, nameEnd - nameX);
        var labelPart = shownName.StartsWith(label, StringComparison.Ordinal) ? label : shownName;
        drawList.AddText(new Vector2(nameX, y), Palette.U32(colour), labelPart);
        if (labelPart.Length < shownName.Length)
            drawList.AddText(new Vector2(nameX + ImGui.CalcTextSize(labelPart).X, y), Palette.U32(Palette.EndingSoon), shownName[labelPart.Length..]);
        if (tally.Total > 0) Blocks.Bar(drawList, barMin, barSize, tally.Fraction, colour, $"{tally.Done} / {tally.Total}");
        if (open)
        {
            // The same id as the checkbox it replaces: "Show done" under the category's id. Its place is kept while the card is
            // closed, so the bar never moves.
            using var toggleId = ImRaii.PushId(category.ToString());
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(right - toggleWidth, min.Y + (max.Y - min.Y - ImGui.GetFrameHeight()) * 0.5f));
            var showDoneNow = _showDone.GetValueOrDefault(category);
            if (Blocks.Toggle(ShowDone, ref showDoneNow)) _showDone[category] = showDoneNow;
        }
        if (!open) return;
        using var id = ImRaii.PushId(category.ToString());
        if (disabledReason != null)
        {
            using (ImRaii.TextWrapPos(card.WrapPos)) ImGui.TextColored(Palette.EndingSoon, disabledReason);
        }
        var showDone = _showDone.GetValueOrDefault(category);
        if (section.Groups.Count == 0)
        {
            ImGui.TextColored(Palette.Quiet, "nothing here yet");
            return;
        }
        var drewAny = false;
        foreach (var group in section.Groups)
        {
            var entries = showDone ? group.Entries : group.Entries.Where(e => !e.IsDone).ToList();
            if (entries.Count == 0) continue;
            drewAny = true;
            using var node = ImRaii.TreeNode($"{group.Name}###{group.Name}");
            DrawGroupTally(look.Of(category, group.Name), colour, right);
            if (!node) continue;
            var slot = look.HasIcons(category, group.Name);
            foreach (var entry in entries) DrawEntry(entry, record, look, right, slot, now, reduced);
        }
        if (!drewAny) ImGui.TextColored(Palette.Quiet, showDone ? "nothing here yet" : "everything here is done");
    }

    /// <summary>Each group inside shows a small done/total bar, at the right of the group's line, with its numbers beside it; none for a
    /// group with no row that can be finished.</summary>
    private static void DrawGroupTally(Tally tally, Vector4 colour, float right)
    {
        if (tally.Total == 0) return;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var size = new Vector2(Blocks.S(Blocks.GroupBarWidth), Blocks.S(Blocks.RowBarHeight));
        Blocks.Bar(drawList, new Vector2(right - size.X, (min.Y + max.Y - size.Y) * 0.5f), size, tally.Fraction, colour);
        var text = $"{tally.Done} / {tally.Total}";
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddText(new Vector2(right - size.X - Blocks.S(8f) - textSize.X, (min.Y + max.Y - textSize.Y) * 0.5f), Palette.U32(Palette.TextDim), text);
    }

    /// <summary>The entry row: the box, the game icon's square when the row's group has icons, the name in its tone, the pills (not unlocked
    /// yet, the marker, off for the role), and on the right a thin bar and the value. The box is read-only but on a My tasks row, which on a
    /// counted task adds one per click and has Take one back and Reset to 0 on a right-click; the ignore menu is on the name; the count menu
    /// is also on a counted task's value; the marker's tooltip is on its pill. The value keeps its place at the right end; on a short line the
    /// bar goes first, then the marker is cut, then the "off for" pill, and a cut name, pill or value is shown whole in a tooltip. A row off
    /// for the role is dimmed whatever its state, and a row in Ending soon that ends within the hour pulses. A row out of view is only a
    /// blank of its height. Ids as always.</summary>
    private void DrawEntry(Entry entry, CharacterRecord record, ChecklistLook look, float right, bool slot, DateTimeOffset now, bool reduced)
    {
        using var id = ImRaii.PushId(entry.Item.Id);
        var rowMin = ImGui.GetCursorScreenPos();
        var rowHeight = ImGui.GetFrameHeight();
        // A row the list's view does not reach is a blank of its height, measured and drawn by nothing, so a long
        // open category with Show done on costs what the rows on screen cost.
        var height = rowHeight + (entry.Detail.Length > 0 ? ImGui.GetStyle().ItemSpacing.Y + ImGui.GetTextLineHeight() : 0f);
        var extent = new Vector2(MathF.Max(1f, right - rowMin.X), height);
        if (!ImGui.IsRectVisible(extent))
        {
            ImGui.Dummy(extent);
            return;
        }
        var tone = look.ToneOf(entry);
        var meter = Tones.MeterOf(entry);
        var drawList = ImGui.GetWindowDrawList();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        if (Tones.Pulses(entry, tone, record, now))
        {
            var glow = Palette.Fade(Palette.EndingSoon, 0.05f + 0.13f * Blocks.Pulse(reduced));
            drawList.AddRectFilled(rowMin - new Vector2(Blocks.S(4f), 0f), new Vector2(right + Blocks.S(4f), rowMin.Y + rowHeight), Palette.U32(glow), Blocks.S(6f));
        }
        // A row off for the character's role is dimmed whatever its state: its words in the
        // muted text colour, its pills quiet, its box and icon at a lower alpha. Its menus and tooltips, drawn after, stay as clear as any row's.
        var off = entry.OffFor != null;

        // A My tasks row's box is a real checkbox, for any character, logged in or not. A click
        // ticks the task for this character, and a click on a ticked box removes the tick. Every other box stays read-only.
        var done = entry.IsDone;
        var manual = entry.Item.Freshness.Kind == FreshnessKind.Manual;
        bool clicked;
        using (Blocks.Faded(off ? Blocks.OffForAlpha : 1f)) clicked = Blocks.DoneBox(ref done, clickable: manual);
        if (clicked) plugin.SetTaskDone(record, entry.Item.Id, done);
        // A counted task's box adds one per click; a right-click on it offers Take one back and Reset to 0, as its name's menu does.
        if (manual && entry.Item.Kind == ItemKind.Counter)
        {
            using var popup = ImRaii.ContextPopupItem("count");
            if (popup) DrawCountItems(entry, record);
        }
        if (slot)
        {
            ImGui.SameLine();
            using (Blocks.Faded(off ? Blocks.OffForAlpha : 1f)) plugin.Icons.Draw(entry.Item.IconId, rowHeight);
        }
        ImGui.SameLine();
        var nameX = ImGui.GetCursorScreenPos().X;

        // The room first. The value keeps the right end (at most half the line; longer, it is cut). The bar goes
        // when the name's floor, the pills, the bar and the value do not all fit; then the marker is cut, down to its own floor, and then the
        // "off for" pill the same way, for a long role name.
        var avail = right - nameX;
        var (value, valueCut) = Blocks.Fit(entry.Display, avail * 0.5f);
        var valueWidth = value.Length > 0 ? ImGui.CalcTextSize(value).X : 0f;
        var valueRoom = value.Length > 0 ? valueWidth + spacing : 0f;
        var offForWords = entry.OffFor is { } role ? "off for " + role : null;
        var offFor = offForWords;
        var lockedWidth = entry.Locked ? Blocks.PillSize(Locked).X + spacing : 0f;
        var offForWidth = offFor != null ? Blocks.PillSize(offFor).X + spacing : 0f;
        var marker = entry.Marker;
        var markerWidth = marker.Length > 0 ? Blocks.PillSize(marker).X + spacing : 0f;
        var barWidth = Blocks.S(Blocks.RowBarWidth);
        var nameFloor = Blocks.S(NameFloor);
        var showBar = meter != null && nameFloor + lockedWidth + offForWidth + markerWidth + barWidth + spacing + valueRoom <= avail;
        var room = avail - valueRoom - (showBar ? barWidth + spacing : 0f);
        var markerCut = false;
        if (markerWidth > 0f && nameFloor + lockedWidth + offForWidth + markerWidth > room)
        {
            var pill = MathF.Max(Blocks.S(MarkerFloor), room - nameFloor - lockedWidth - offForWidth - spacing);
            (marker, markerCut) = Blocks.Fit(entry.Marker, pill - Blocks.S(Blocks.PillPadding) * 2f);
            markerWidth = Blocks.PillSize(marker).X + spacing;
        }
        var offForCut = false;
        if (offForWords != null && nameFloor + lockedWidth + offForWidth + markerWidth > room)
        {
            var pill = MathF.Max(Blocks.S(MarkerFloor), room - nameFloor - lockedWidth - markerWidth - spacing);
            (offFor, offForCut) = Blocks.Fit(offForWords, pill - Blocks.S(Blocks.PillPadding) * 2f);
            offForWidth = Blocks.PillSize(offFor).X + spacing;
        }
        var (name, cut) = Blocks.Fit(entry.Name, MathF.Max(Blocks.S(24f), room - lockedWidth - offForWidth - markerWidth));
        ImGui.TextColored(off ? Palette.TextMuted : Palette.Of(tone), name);
        DrawIgnoreMenu(entry, record);
        if (cut && ImGui.IsItemHovered()) ImGui.SetTooltip(entry.Name);

        // A stored lock: the character has not unlocked this, so it is not unfinished work.
        if (entry.Locked)
        {
            ImGui.SameLine();
            Blocks.Pill(Locked, Palette.Quiet);
        }
        if (marker.Length > 0)
        {
            ImGui.SameLine();
            Blocks.Pill(marker, Palette.Of(Tones.OfMarker(entry)));
            // An observed count is a floor: a ticket bought while the plugin was off was never seen. A cut marker is shown whole.
            var observed = entry.Item.Freshness.Kind == FreshnessKind.ObservedOnly;
            // A "needs:" marker also says which window to open (Tones.HintOf); a cut marker shows its whole words first.
            var hint = Tones.HintOf(entry);
            if ((markerCut || observed || hint != null) && ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join("\n", new[] { markerCut ? entry.Marker : null, observed ? ObservedWords : null, hint }.OfType<string>()));
        }
        // Switched off by the character's role, so listed but never counted.
        if (offFor != null)
        {
            ImGui.SameLine();
            Blocks.Pill(offFor, Palette.Quiet);
            if (offForCut && offForWords != null && ImGui.IsItemHovered()) ImGui.SetTooltip(offForWords);
        }
        if (showBar || value.Length > 0)
        {
            ImGui.SameLine();
            var y = ImGui.GetCursorScreenPos().Y;
            if (showBar && meter is { } bar)
            {
                ImGui.SetCursorScreenPos(new Vector2(right - valueRoom - barWidth, y));
                ImGui.Dummy(new Vector2(barWidth, rowHeight));
                var barHeight = Blocks.S(Blocks.RowBarHeight);
                var barMin = ImGui.GetItemRectMin();
                Blocks.Bar(drawList, new Vector2(barMin.X, barMin.Y + (rowHeight - barHeight) * 0.5f), new Vector2(barWidth, barHeight), bar.Fraction, Palette.BarOf(tone));
                if (value.Length > 0) ImGui.SameLine();
            }
            if (value.Length > 0)
            {
                ImGui.SetCursorScreenPos(new Vector2(right - valueWidth, y));
                ImGui.TextColored(off ? Palette.TextMuted : Palette.TextSecondary, value);
                // A counted task can be right-clicked anywhere on the row, so the count text carries the same menu as the box, under its own
                // popup id so no two popups in one row share one.
                if (manual && entry.Item.Kind == ItemKind.Counter)
                {
                    using var popup = ImRaii.ContextPopupItem("count-text");
                    if (popup) DrawCountItems(entry, record);
                }
                if (valueCut && ImGui.IsItemHovered()) ImGui.SetTooltip(entry.Display);
            }
        }
        // The step in progress reads under the bar, not beside it, so a row with a long job name does not wrap.
        if (entry.Detail.Length > 0)
        {
            using var indent = ImRaii.PushIndent(nameX - rowMin.X, scaled: false);
            ImGui.TextColored(off ? Palette.TextMuted : Palette.TextDim, entry.Detail);
        }
    }

    private void DrawIgnoreMenu(Entry entry, CharacterRecord record)
    {
        using var popup = ImRaii.ContextPopupItem("ignore");
        if (!popup) return;
        // A counted My task's own two items first, then the ignores every row has.
        if (entry.Item.Freshness.Kind == FreshnessKind.Manual && entry.Item.Kind == ItemKind.Counter)
        {
            DrawCountItems(entry, record);
            ImGui.Separator();
        }
        if (ImGui.MenuItem("Ignore on this character")) plugin.Tracker.Ignore(record.ContentId, entry.Item.Id, true);
        if (ImGui.MenuItem($"Ignore the group \"{entry.Item.Group}\" on this character")) plugin.Tracker.Ignore(record.ContentId, entry.Item.GroupKey, true);
        if (ImGui.MenuItem("Ignore everywhere")) plugin.UpdateSettings(s => s.IgnoredEverywhere.Add(entry.Item.Id));
        if (ImGui.MenuItem($"Ignore the group \"{entry.Item.Group}\" everywhere")) plugin.UpdateSettings(s => s.IgnoredEverywhere.Add(entry.Item.GroupKey));
    }

    /// <summary>A counted My task's two menu items. Take one back is a click on a ticked box; Reset to 0 removes the count. Both are harmless
    /// at 0.</summary>
    private void DrawCountItems(Entry entry, CharacterRecord record)
    {
        if (ImGui.MenuItem("Take one back")) plugin.SetTaskDone(record, entry.Item.Id, false);
        if (ImGui.MenuItem("Reset to 0")) plugin.ResetTask(record, entry.Item.Id);
    }
}
