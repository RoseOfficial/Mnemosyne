using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>Each Money currency summed over the sidebar's characters, from what each record already holds: the three gil lines, then every
/// currency some character holds, in the order Totals.Build gives. Each line opens on every character that holds more than zero, largest
/// first, with its amount, and a holder whose value may be out of date is dimmed, "as of last visit" for a window- or place-read line, "last
/// seen ..." otherwise. Asked of Plugin.TotalsFor every frame the page is drawn, which keeps its last build while nothing has moved, from
/// the characters the sidebar lists, so a hidden one is never here. The Gil over time section follows the lines (GilHistorySection), and the
/// page scrolls as a whole. Each line is a card row, its currency's game icon, its name, its total in the Axis font and a "held by n" chip;
/// an open line lists its holders, each with a bar sized against the largest; the All gil line is gold. Each line's node keeps its own id.
/// Nothing here reads the game, stores anything or changes a checklist.</summary>
public sealed class TotalsView(Plugin plugin)
{
    private const string Hint = "every character in the list, as each last showed it; open a line to see who holds it";
    /// <summary>A line's node, its id "###" + the line's id (only the part from "###" makes the id), its text drawn over it, its hit box the
    /// width of the card.</summary>
    private const ImGuiTreeNodeFlags LineFlags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.AllowItemOverlap;
    /// <summary>At 100 %: a currency's icon; the widest a holder's bar is, and the least it is drawn at; the least room a holder's name keeps,
    /// and the least a cut words pill keeps.</summary>
    private const float IconSide = 24f, HolderBarWidth = 160f, BarFloor = 24f, NameFloor = 60f, PillFloor = 40f;

    // Two initialisers take the parameter and no member captures it, so the compiler's CS9124 (captured and also used to initialise) cannot
    // fire under TreatWarningsAsErrors.
    private readonly Plugin _plugin = plugin;
    private readonly GilHistorySection _history = new(plugin);

    public void Draw(IReadOnlyList<(CharacterRecord Record, Checklist Checklist)> characters, DateTimeOffset now)
    {
        Blocks.PageTitle(_plugin.Fonts.Title, Glyphs.Totals, "Totals", Hint);
        if (characters.Count == 0)
        {
            ImGui.TextColored(Palette.Quiet, "Log into a character and it appears here.");
            return;
        }
        DrawLines(_plugin.TotalsFor(characters.Select(c => c.Record), now), now);
        _history.Draw(now);
    }

    /// <summary>The lines, each a card, drawn under the id "totals", so each line's node keeps the id, and so the open state, it always
    /// had.</summary>
    private void DrawLines(IReadOnlyList<TotalLine> lines, DateTimeOffset now)
    {
        using var id = ImRaii.PushId("totals");
        foreach (var line in lines) DrawLine(line, now);
    }

    /// <summary>Each line is a card row: the chevron, the currency's game icon (PageLook.IconOf; a blank square with none, GameIcons), the
    /// name, and at the right a "held by n" chip and the total in the Axis font, gold for All gil. The row is as tall as the total's font or
    /// the icon; a name too long is cut with "…" and shown whole in a tooltip.</summary>
    private void DrawLine(TotalLine line, DateTimeOffset now)
    {
        var allGil = line.Id == Totals.AllGilId;
        using var card = Blocks.Card(allGil ? Palette.Gold : null);
        var total = Compact.Format(line.Total);
        float totalWidth, totalHeight;
        using (_plugin.Fonts.Title.Push())
        {
            totalWidth = ImGui.CalcTextSize(total).X;
            totalHeight = ImGui.GetTextLineHeight();
        }
        var icon = Blocks.S(IconSide);
        var text = ImGui.GetTextLineHeight();
        var padding = ImGui.GetStyle().FramePadding;
        var height = MathF.Max(totalHeight, icon) + Blocks.S(4f);
        var invisible = Blocks.Invisible();
        var tall = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(padding.X, MathF.Max(padding.Y, (height - text) * 0.5f)));
        using var node = ImRaii.TreeNode("###" + line.Id, LineFlags);
        tall.Dispose();
        invisible.Dispose();
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var after = ImGui.GetCursorScreenPos();
        var right = card.InnerRight;
        var drawList = ImGui.GetWindowDrawList();
        var middle = (min.Y + max.Y) * 0.5f;
        if (hovered) drawList.AddRectFilled(new Vector2(min.X - Blocks.S(4f), min.Y), new Vector2(right + Blocks.S(4f), max.Y), Palette.U32(Palette.CardHover), Blocks.S(6f));
        Blocks.GlyphAt(drawList, new Vector2(min.X, middle - text * 0.5f), node ? Glyphs.Open : Glyphs.Closed, Palette.TextDim);
        ImGui.SetCursorScreenPos(new Vector2(min.X + Blocks.S(20f), middle - icon * 0.5f));
        _plugin.Icons.Draw(PageLook.IconOf(line, _plugin.Catalog), icon);
        ImGui.SetCursorScreenPos(after);

        // The right end: the total, then the chip before it.
        var colour = allGil ? Palette.Gold : Palette.Text;
        using (_plugin.Fonts.Title.Push()) drawList.AddText(new Vector2(right - totalWidth, middle - totalHeight * 0.5f), Palette.U32(colour), total);
        // "held by n", or, on a gil line no one holds, "nothing held yet", quiet: the line opens onto nothing, as before.
        var held = PageLook.HeldBy(line);
        var chip = Blocks.PillSize(held);
        var chipX = right - totalWidth - Blocks.S(12f) - chip.X;
        Blocks.PillAt(drawList, new Vector2(chipX, middle - chip.Y * 0.5f), held, line.Holders.Count > 0 ? Palette.TextSecondary : Palette.Quiet);
        var nameX = min.X + Blocks.S(28f) + icon;
        var (name, cut) = Blocks.Fit(line.Name, MathF.Max(Blocks.S(24f), chipX - Blocks.S(8f) - nameX));
        drawList.AddText(new Vector2(nameX, middle - text * 0.5f), Palette.U32(colour), name);
        if (cut && hovered) ImGui.SetTooltip(line.Name);

        if (!node) return;
        foreach (var holder in line.Holders) DrawHolder(holder, line, right, allGil, now);
    }

    /// <summary>An open line lists its holders, each with a bar sized against the largest holder (PageLook.Share). A stale holder is dimmed
    /// and keeps its "as of last visit" or "last seen" words, as a quiet pill after the name: the name, the pill, the bar and the amount at
    /// the right, the whole row faded, its tooltip shown after the fade at full strength. The amount keeps the right end and the name its
    /// floor; the bar is sized last, from what the name's floor, the words and the amount leave, and is dropped below its own floor; then the
    /// words' pill is cut to its floor, its whole words in a tooltip; the name is cut last.</summary>
    private static void DrawHolder(TotalHolder holder, TotalLine line, float right, bool allGil, DateTimeOffset now)
    {
        var rowMin = ImGui.GetCursorScreenPos();
        var rowHeight = ImGui.GetFrameHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var amount = Compact.Format(holder.Amount);
        var amountRoom = ImGui.CalcTextSize(amount).X + spacing;
        var words = holder.Stale switch
        {
            StaleReason.Visit => "as of last visit",
            StaleReason.Threshold => "last seen " + Relative.Since(holder.Record.LastSeen, now),
            _ => null,
        };
        var avail = right - rowMin.X;
        var nameWidth = ImGui.CalcTextSize(holder.Record.Label).X;
        var nameKeep = MathF.Min(nameWidth, Blocks.S(NameFloor));
        var wordsRoom = words != null ? spacing + Blocks.PillSize(words).X : 0f;
        // Sized from the whole name, not its floor, so the bar gives way first instead of eating room the name
        // still needs; only once the bar is at its own floor does the words' pill get cut, then the name, below.
        var barWidth = MathF.Min(Blocks.S(HolderBarWidth), avail - nameWidth - wordsRoom - amountRoom - spacing);
        var showBar = barWidth >= Blocks.S(BarFloor);
        var room = avail - amountRoom - (showBar ? barWidth + spacing : 0f);
        var shownWords = words;
        var wordsCut = false;
        if (words != null && nameKeep + wordsRoom > room)
        {
            var pill = MathF.Max(Blocks.S(PillFloor), room - nameKeep - spacing);
            (shownWords, wordsCut) = Blocks.Fit(words, pill - Blocks.S(Blocks.PillPadding) * 2f);
            wordsRoom = spacing + Blocks.PillSize(shownWords).X;
        }
        var (name, cut) = Blocks.Fit(holder.Record.Label, MathF.Max(Blocks.S(24f), room - wordsRoom));
        string? tip = null;
        using (Blocks.Faded(holder.Stale != null ? Blocks.StaleAlpha : 1f))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Palette.Text, name);
            if (cut && ImGui.IsItemHovered()) tip = holder.Record.Label;
            if (shownWords != null)
            {
                ImGui.SameLine();
                Blocks.Pill(shownWords, Palette.Quiet);
                if (wordsCut && ImGui.IsItemHovered()) tip = words;
            }
            if (showBar)
            {
                var barHeight = Blocks.S(Blocks.RowBarHeight);
                Blocks.Bar(ImGui.GetWindowDrawList(), new Vector2(right - amountRoom - barWidth, rowMin.Y + (rowHeight - barHeight) * 0.5f),
                    new Vector2(barWidth, barHeight), PageLook.Share(holder, line).Fraction, allGil ? Palette.Gold : Palette.Accent);
            }
            ImGui.SameLine();
            ImGui.SetCursorScreenPos(new Vector2(right - amountRoom + spacing, rowMin.Y));
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Palette.TextSecondary, amount);
        }
        if (tip != null) ImGui.SetTooltip(tip);
    }
}
