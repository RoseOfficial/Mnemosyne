using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Mnemosyne.Look;

/// <summary>The building blocks, each written once and used on every page: the card (CardScope), the section label, the pill, the progress bar,
/// the icon button, the toggle switch, the segmented buttons, the status dot, the done box of an entry row, the glyphs and the pulse, along
/// with the page title, the hairline, and a toggle that can span a row and show a label's part before "##". Sizes are given at 100 % and
/// scaled by Dalamud's global UI scale when drawn. Text is measured with the font in use, so a size follows the font too. Every block that is
/// clicked is an ImGui item under the id its caller gives, so a caller keeps the id it always had; what the block shows is drawn on the
/// window's draw list over that item, using Dalamud's own ImGui bindings.</summary>
internal static class Blocks
{
    public const float CardPadding = 10f, CardRounding = 8f, EdgeWidth = 3f, PillPadding = 7f, RowBarWidth = 84f, RowBarHeight = 5f,
        HeaderBarWidth = 120f, GroupBarWidth = 64f, PulseSeconds = 1.8f;
    /// <summary>A row off for the character's role is dimmed whatever its state; its box and its icon are drawn at this alpha, its words in
    /// the muted text colour.</summary>
    public const float OffForAlpha = 0.55f;
    /// <summary>A stale character's tile is dimmed to this alpha.</summary>
    public const float StaleAlpha = 0.55f;
    /// <summary>What ends a cut name or value.</summary>
    public const string Ellipsis = "…";

    /// <summary>A size at 100 %, at Dalamud's global UI scale.</summary>
    public static float S(float size) => size * ImGuiHelpers.GlobalScale;

    /// <summary>A card; the width of the region it starts in unless a width is given (a card above a list that scrolls stops where the list's
    /// cards stop).</summary>
    public static CardScope Card(Vector4? edge = null, float width = 0f) => new(edge, width);

    /// <summary>Pushes an item's own text and highlight transparent, so a Selectable or a CollapsingHeader keeps its label, and so its id, while
    /// the block draws what shows over it.</summary>
    public static ImRaii.ColorDisposable Invisible() =>
        ImRaii.PushColor(ImGuiCol.Text, Vector4.Zero).Push(ImGuiCol.Header, Vector4.Zero).Push(ImGuiCol.HeaderHovered, Vector4.Zero).Push(ImGuiCol.HeaderActive, Vector4.Zero);

    /// <summary>Everything drawn inside at a fraction of the style's alpha, or as it is at 1: a stale tile, the box and icon of a row off for the
    /// role. Keep a menu or a tooltip out of it, or it fades too.</summary>
    public static ImRaii.StyleDisposable Faded(float fade) => ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * fade, fade < 1f);

    /// <summary>A Selectable's drawn body: ImGui widens its hit box by half the item spacing on each side so a column of them has no gap, and
    /// GetItemRectMin and GetItemRectMax report the widened box.</summary>
    public static (Vector2 Min, Vector2 Max) Visible(Vector2 min, Vector2 max)
    {
        var spacing = ImGui.GetStyle().ItemSpacing;
        var left = MathF.Floor(spacing.X * 0.5f);
        var up = MathF.Floor(spacing.Y * 0.5f);
        return (min + new Vector2(left, up), max - new Vector2(spacing.X - left, spacing.Y - up));
    }

    /// <summary>The coloured edge on the left of a rounded panel: the panel's own shape, filled in the colour and clipped to a strip, so the
    /// strip follows the panel's corners.</summary>
    public static void EdgeAt(ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 colour, float rounding)
    {
        drawList.PushClipRect(min, new Vector2(min.X + S(EdgeWidth), max.Y), true);
        drawList.AddRectFilled(min, max, Palette.U32(colour), rounding);
        drawList.PopClipRect();
    }

    /// <summary>Small dim capitals, then a count in secondary text, e.g. "TODAY 3", in the small Axis font (LookFonts.Label), or the normal
    /// font until it is built.</summary>
    public static void SectionLabel(IFontHandle font, string text, int? count = null)
    {
        using (font.Push())
        {
            ImGui.TextColored(Palette.TextDim, text.ToUpperInvariant());
            if (count is not { } n) return;
            ImGui.SameLine(0f, S(6f));
            ImGui.TextColored(Palette.TextSecondary, n.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A page's title in the game's Axis font (LookFonts.Title, the normal font until it is built), the page's glyph before it in the
    /// accent, as on its tab, and a dim hint under it, wrapped at the region's right.</summary>
    public static void PageTitle(IFontHandle font, FontAwesomeIcon icon, string title, string hint)
    {
        var start = ImGui.GetCursorScreenPos();
        var glyph = GlyphSize(icon);
        ImGui.SetCursorScreenPos(start + new Vector2(glyph.X + S(10f), 0f));
        Vector2 min, max;
        using (font.Push())
        {
            ImGui.TextColored(Palette.Text, title);
            min = ImGui.GetItemRectMin();
            max = ImGui.GetItemRectMax();
        }
        GlyphAt(ImGui.GetWindowDrawList(), new Vector2(start.X, (min.Y + max.Y - glyph.Y) * 0.5f), icon, Palette.Accent);
        if (hint.Length == 0) return;
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.TextDim)) ImGui.TextWrapped(hint);
    }

    /// <summary>A separator inside a card that spans its region. ImGui's own Separator spans the whole window and would cross the card's
    /// edges, so this hairline stops the card's padding short of the region's right, where a full-width card's content ends.</summary>
    public static void Hairline()
    {
        var min = ImGui.GetCursorScreenPos();
        // ContentRegionAvail is the card's own width only while the card is drawn full width, as every caller does today.
        var size = new Vector2(MathF.Max(1f, ImGui.GetContentRegionAvail().X - S(CardPadding)), MathF.Max(1f, S(1f)));
        ImGui.Dummy(size);
        ImGui.GetWindowDrawList().AddRectFilled(min, min + size, Palette.U32(Palette.Hairline), 0f);
    }

    public static Vector2 PillSize(string text) => new(ImGui.CalcTextSize(text).X + S(PillPadding) * 2f, ImGui.GetTextLineHeight() + S(2f));

    /// <summary>A small rounded tag with a tinted fill and coloured text, as an item the height of a framed row, so it sits on one line with a
    /// row's box and text, and a tooltip can ask IsItemHovered about it.</summary>
    public static void Pill(string text, Vector4 tone)
    {
        var size = PillSize(text);
        var height = MathF.Max(size.Y, ImGui.GetFrameHeight());
        ImGui.Dummy(new Vector2(size.X, height));
        var min = ImGui.GetItemRectMin();
        PillAt(ImGui.GetWindowDrawList(), new Vector2(min.X, min.Y + (height - size.Y) * 0.5f), text, tone);
    }

    /// <summary>A pill drawn where it is told, with no item: a badge, a role chip.</summary>
    public static void PillAt(ImDrawListPtr drawList, Vector2 min, string text, Vector4 tone)
    {
        var size = PillSize(text);
        drawList.AddRectFilled(min, min + size, Palette.U32(Palette.Fade(tone, 0.16f)), size.Y * 0.5f);
        drawList.AddText(min + new Vector2(S(PillPadding), S(1f)), Palette.U32(tone), text);
    }

    /// <summary>A rounded track filled in a tone, with its label, "12 / 20" or "2 of 5", drawn inside it when given (the fill is then lighter,
    /// so the label reads). A thin bar has no label: its row's value is beside it. Anything above none shows at least two pixels, so 3 of 102
    /// is a sliver, not a tenth of the bar; none shows no fill.</summary>
    public static void Bar(ImDrawListPtr drawList, Vector2 min, Vector2 size, float fraction, Vector4 tone, string? label = null)
    {
        var max = min + size;
        var rounding = size.Y * 0.5f;
        drawList.AddRectFilled(min, max, Palette.U32(Palette.Border), rounding);
        var filled = size.X * Math.Clamp(fraction, 0f, 1f);
        if (filled > 0f)
            drawList.AddRectFilled(min, new Vector2(min.X + MathF.Max(filled, S(2f)), max.Y), Palette.U32(Palette.Fade(tone, label == null ? 1f : 0.55f)), rounding);
        if (label == null) return;
        drawList.AddText(min + (size - ImGui.CalcTextSize(label)) * 0.5f, Palette.U32(Palette.Text), label);
    }

    /// <summary>A glyph from Dalamud's icon font on an invisible button of the given side, under the id the caller gives, with a hover and a
    /// press fill. The caller adds the tooltip. A pulse from 0 to 1 draws a ring that breathes (a switch in progress); below 0, none. Under
    /// ImRaii.Disabled it is not clicked and its glyph fades with the style's alpha.</summary>
    public static bool IconButton(string id, FontAwesomeIcon icon, Vector4 colour, float side, float pulse = -1f)
    {
        var clicked = ImGui.InvisibleButton(id, new Vector2(side, side));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var rounding = S(6f);
        if (ImGui.IsItemActive()) drawList.AddRectFilled(min, max, Palette.U32(Palette.Fade(Palette.Accent, 0.35f)), rounding);
        else if (ImGui.IsItemHovered()) drawList.AddRectFilled(min, max, Palette.U32(Palette.FrameHover), rounding);
        if (pulse >= 0f) drawList.AddRect(min, max, Palette.U32(Palette.Fade(Palette.Accent, 0.25f + 0.6f * pulse)), rounding, ImDrawFlags.None, S(1.5f));
        GlyphIn(drawList, min, max, icon, colour);
        return clicked;
    }

    /// <summary>The part of a label that shows, before "##", as ImGui shows a checkbox's. A toggle keeps a checkbox's whole label as its id,
    /// "##" and "###" suffixes included, so it shows only this part.</summary>
    public static string Shown(string label)
    {
        var cut = label.IndexOf("##", StringComparison.Ordinal);
        return cut < 0 ? label : label[..cut];
    }

    /// <summary>The width a toggle needs: its label's shown part, then its track.</summary>
    public static float ToggleWidth(string label)
    {
        var shown = Shown(label);
        return (shown.Length > 0 ? ImGui.CalcTextSize(shown).X + ImGui.GetStyle().ItemInnerSpacing.X : 0f) + ImGui.GetTextLineHeight() * 1.9f;
    }

    /// <summary>A toggle switch in place of a checkbox: the accent when on, grey when off. The label is its id, as a checkbox's is, and its part
    /// before "##" shows and is part of what is clicked. Given a width, the toggle spans it, its words at the left and its track at the right end
    /// (a settings row), its words cut with "…" to what the track leaves and shown whole in a tooltip, and its words can take a colour of their
    /// own (a category's). Such a toggle takes a mouse click on its words or its track only, not on the gap between them, so a stray click in a
    /// row's empty space changes nothing; it is still one item under its one id, so a keyboard press works as before. A toggle with no words of
    /// its own (the Hidden (n) label drawn over it) keeps its whole width. Given no width, it is as wide as its words and track. Returns whether
    /// it was clicked, having flipped <paramref name="on"/>.</summary>
    public static bool Toggle(string label, ref bool on, float width = 0f, Vector4? colour = null)
    {
        var height = ImGui.GetFrameHeight();
        var line = ImGui.GetTextLineHeight();
        var clicked = ImGui.InvisibleButton(label, new Vector2(width > 0f ? MathF.Max(width, line * 1.9f) : ToggleWidth(label), height));
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var shown = Shown(label);
        var (words, cut) = width > 0f && shown.Length > 0 ? Fit(shown, max.X - min.X - line * 1.9f - ImGui.GetStyle().ItemInnerSpacing.X) : (shown, false);
        var wordsMax = new Vector2(min.X + (words.Length > 0 ? ImGui.CalcTextSize(words).X + ImGui.GetStyle().ItemInnerSpacing.X : 0f), max.Y);
        var gap = width > 0f && shown.Length > 0 && !ImGui.IsMouseHoveringRect(min, wordsMax) && !ImGui.IsMouseHoveringRect(new Vector2(max.X - line * 1.9f, min.Y), max);
        if (clicked && gap && ImGui.IsMouseReleased(ImGuiMouseButton.Left)) clicked = false;
        if (clicked) on = !on;
        var hovered = ImGui.IsItemHovered() && !gap;
        var drawList = ImGui.GetWindowDrawList();
        if (words.Length > 0)
        {
            drawList.AddText(new Vector2(min.X, min.Y + (height - line) * 0.5f), Palette.U32(colour ?? (hovered ? Palette.Text : Palette.TextSecondary)), words);
            if (cut && hovered) ImGui.SetTooltip(shown);
        }
        var trackHeight = line * 0.95f;
        var trackMin = new Vector2(max.X - line * 1.9f, min.Y + (height - trackHeight) * 0.5f);
        var trackMax = new Vector2(max.X, trackMin.Y + trackHeight);
        var track = on ? Palette.Accent : hovered ? Palette.Mix(Palette.Border, Palette.Accent, 0.25f) : Palette.Border;
        drawList.AddRectFilled(trackMin, trackMax, Palette.U32(track), trackHeight * 0.5f);
        var knob = on ? trackMax.X - trackHeight * 0.5f : trackMin.X + trackHeight * 0.5f;
        drawList.AddCircleFilled(new Vector2(knob, trackMin.Y + trackHeight * 0.5f), trackHeight * 0.5f - S(2f), Palette.U32(on ? Palette.Ink : Palette.TextSecondary));
        return clicked;
    }

    /// <summary>The width Segmented takes for these choices, so a row can place it at its right end.</summary>
    public static float SegmentedWidth<T>(IReadOnlyList<(string Label, T Value)> choices)
    {
        var width = 0f;
        foreach (var (label, _) in choices) width += ImGui.CalcTextSize(label).X + S(12f) * 2f;
        return width;
    }

    /// <summary>One choice from a few, side by side, the chosen one filled in the accent. Each segment is an invisible button under its own
    /// label's id. Returns whether the choice changed. The graph's range is drawn with it.</summary>
    public static bool Segmented<T>(IReadOnlyList<(string Label, T Value)> choices, ref T current)
    {
        var changed = false;
        var height = ImGui.GetFrameHeight();
        var drawList = ImGui.GetWindowDrawList();
        using var tight = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0f, ImGui.GetStyle().ItemSpacing.Y));
        for (var i = 0; i < choices.Count; i++)
        {
            var (label, value) = choices[i];
            if (i > 0) ImGui.SameLine();
            var text = ImGui.CalcTextSize(label);
            var chosen = EqualityComparer<T>.Default.Equals(current, value);
            if (ImGui.InvisibleButton(label, new Vector2(text.X + S(12f) * 2f, height)) && !chosen)
            {
                current = value;
                changed = chosen = true;
            }
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var corners = choices.Count == 1 ? ImDrawFlags.RoundCornersAll
                : i == 0 ? ImDrawFlags.RoundCornersLeft : i == choices.Count - 1 ? ImDrawFlags.RoundCornersRight : ImDrawFlags.RoundCornersNone;
            var fill = chosen ? Palette.Fade(Palette.Accent, 0.35f) : ImGui.IsItemHovered() ? Palette.FrameHover : Palette.Frame;
            drawList.AddRectFilled(min, max, Palette.U32(fill), S(6f), corners);
            drawList.AddText(min + (max - min - text) * 0.5f, Palette.U32(chosen ? Palette.Text : Palette.TextSecondary), label);
        }
        return changed;
    }

    /// <summary>A small filled circle, with a soft halo when it glows (the logged-in character).</summary>
    public static void DotAt(ImDrawListPtr drawList, Vector2 centre, Vector4 colour, bool glow = false)
    {
        var radius = S(3.5f);
        if (glow)
        {
            drawList.AddCircleFilled(centre, radius * 2.6f, Palette.U32(Palette.Fade(colour, 0.12f)));
            drawList.AddCircleFilled(centre, radius * 1.8f, Palette.U32(Palette.Fade(colour, 0.25f)));
        }
        drawList.AddCircleFilled(centre, radius, Palette.U32(colour));
    }

    /// <summary>A glyph of Dalamud's icon font as a text item. UiBuilder.IconFontFixedWidth is Dalamud's default font until the icon font is
    /// built, so this and the other glyph helpers never fail on an early frame.</summary>
    public static void Glyph(FontAwesomeIcon icon, Vector4 colour)
    {
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth)) ImGui.TextColored(colour, icon.ToIconString());
    }

    public static Vector2 GlyphSize(FontAwesomeIcon icon)
    {
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth)) return ImGui.CalcTextSize(icon.ToIconString());
    }

    /// <summary>A glyph drawn with its top left where it is told, with no item.</summary>
    public static void GlyphAt(ImDrawListPtr drawList, Vector2 min, FontAwesomeIcon icon, Vector4 colour)
    {
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth)) drawList.AddText(min, Palette.U32(colour), icon.ToIconString());
    }

    /// <summary>A glyph centred in a box, with no item.</summary>
    public static void GlyphIn(ImDrawListPtr drawList, Vector2 min, Vector2 max, FontAwesomeIcon icon, Vector4 colour)
    {
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
        {
            var text = icon.ToIconString();
            drawList.AddText(min + (max - min - ImGui.CalcTextSize(text)) * 0.5f, Palette.U32(colour), text);
        }
    }

    /// <summary>The text as it fits the width, cut with "…" when it does not, and whether it was cut, so the caller shows it whole in a
    /// tooltip. A binary search over its length, so a long name costs a handful of measurements.</summary>
    public static (string Text, bool Cut) Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return (text, false);
        int low = 0, high = text.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid] + Ellipsis).X <= width) low = mid;
            else high = mid - 1;
        }
        return (text[..low].TrimEnd() + Ellipsis, true);
    }

    /// <summary>A gentle pulse, from 0 to 1 and back every PulseSeconds; held at 0.5 when Dalamud's reduce-motion setting is on.</summary>
    public static float Pulse(bool reduced) =>
        reduced ? 0.5f : 0.5f + 0.5f * MathF.Sin((float)(ImGui.GetTime() * (2d * Math.PI / PulseSeconds)));

    /// <summary>The rounded box, filled green with a check when done. A real ImGui checkbox under the id "##done", as always: clickable on a
    /// My tasks row, disabled on every other, where it keeps its full colour (DisabledAlpha 1) and has no hover. Returns whether a click
    /// changed <paramref name="done"/>.</summary>
    public static bool DoneBox(ref bool done, bool clickable)
    {
        using var colours = ImRaii.PushColor(ImGuiCol.FrameBg, done ? Palette.Done : Palette.Frame)
            .Push(ImGuiCol.FrameBgHovered, done ? Palette.Mix(Palette.Done, Palette.Text, 0.2f) : Palette.FrameHover)
            .Push(ImGuiCol.FrameBgActive, done ? Palette.Mix(Palette.Done, Palette.Text, 0.35f) : Palette.FrameActive)
            .Push(ImGuiCol.CheckMark, Palette.Ink)
            .Push(ImGuiCol.Border, done ? Palette.Done : Palette.Border);
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, S(5f)).Push(ImGuiStyleVar.FrameBorderSize, S(1f));
        if (clickable) return ImGui.Checkbox("##done", ref done);
        using (ImRaii.PushStyle(ImGuiStyleVar.DisabledAlpha, 1f))
        using (ImRaii.Disabled())
            ImGui.Checkbox("##done", ref done);
        return false;
    }
}
