using System.Globalization;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Bindings.ImPlot;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>The Totals page's Gil over time section, below the totals. The All gil line, always, and the line of each character ticked under
/// the graph, over 1 month, 3 months, 1 year or all, with the change over the range under the graph. The range and the ticks are window
/// state, kept until the plugin reloads and never saved, so changing them assembles nothing again; the section opens on 3 months with nothing
/// ticked. The characters are the Tracker's visible records in its own order, by name, not the sidebar's, so a checkbox never moves under the
/// cursor and a line keeps its colour. The graph comes from Plugin.GilGraphFor, and every rule it draws (the lines, the range, the change,
/// both axes, the day under the mouse) is Core's, where it is tested. Drawn with ImPlot on the context Dalamud creates, its axes locked and
/// set here from Core, with no formatter callback. The section is its own card, gold-edged for All gil; the range is segmented buttons; the
/// All gil line is gold and each character's line one of eight Starlight colours clear of gold, in the same name order; the plot's background
/// is the card's; each change line is a pill, green up, rose down, quiet for no change; the checkboxes are filled in their line's colour.
/// Nothing here reads the game or stores anything.</summary>
public sealed class GilHistorySection(Plugin plugin)
{
    private static readonly (string Label, GilRange Range)[] Ranges =
        [("1 month", GilRange.OneMonth), ("3 months", GilRange.ThreeMonths), ("1 year", GilRange.OneYear), ("All", GilRange.All)];
    private const float AllGilWeight = 2.5f, CharacterWeight = 1.5f, PlotHeight = 260f;

    private GilRange _range = GilRange.ThreeMonths;
    private readonly HashSet<ulong> _ticked = [];
    private GilGraph? _stepsOf;
    private double[] _totalSteps = [];
    private readonly Dictionary<ulong, double[]> _steps = new();

    /// <summary>One line drawn: its name, its ImPlot id, its series, its steps, its colour and its weight.</summary>
    private readonly record struct Line(string Name, string Id, GilSeries Series, double[] Steps, Vector4 Colour, float Weight);

    /// <summary>One visible character and its place in the Tracker's list, hidden ones counted, so hiding one character moves no other's
    /// colour.</summary>
    private readonly record struct Place(CharacterRecord Record, int Index);

    public void Draw(DateTimeOffset now)
    {
        using var id = ImRaii.PushId("gil-history");
        ImGui.Spacing();
        // The graph's card is a CardScope like any other. ImPlot's BeginPlot does not split the window's draw list: it opens a
        // child window of its own, which ImGui draws after this window's, so the card's panel stays behind the plot.
        using var card = Blocks.Card(Palette.Gold);
        using var wrap = ImRaii.TextWrapPos(card.WrapPos);
        var places = plugin.Tracker.All.Select((record, index) => new Place(record, index)).Where(p => !p.Record.Hidden).ToList();
        var records = places.Select(p => p.Record).ToList();
        var graph = plugin.GilGraphFor(records, _range, now);
        ImGui.AlignTextToFramePadding();
        Blocks.SectionLabel(plugin.Fonts.Label, "Gil over time");
        if (graph.EarlyDays)
        {
            // Fewer than two days of history, so no graph yet.
            using (ImRaii.PushColor(ImGuiCol.Text, Palette.Quiet))
                ImGui.TextWrapped(graph.Started is { } started
                    ? $"gil history started on {GilGraph.DayWords(started)}; the graph fills in as days pass"
                    : "no gil history yet; it starts when a character's gil is read, and the graph fills in as days pass");
            return;
        }
        // The range as segmented buttons, at the card's right, each under its own label's id, as each radio button was.
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(card.InnerRight - Blocks.SegmentedWidth(Ranges), ImGui.GetCursorScreenPos().Y));
        Blocks.Segmented(Ranges, ref _range);
        graph = plugin.GilGraphFor(records, _range, now);
        Steps(graph);

        // The All gil line first, then each ticked character's, in the Tracker's order and the colour its checkbox name is drawn in.
        var drawn = new List<Line>();
        if (graph.Total is { } total) drawn.Add(new Line(Totals.AllGilName, "all", total, _totalSteps, Palette.Gold, AllGilWeight));
        foreach (var (record, index) in places)
        {
            if (_ticked.Contains(record.ContentId) && graph.Characters.TryGetValue(record.ContentId, out var series))
                drawn.Add(new Line(record.Label, record.ContentId.ToString(CultureInfo.InvariantCulture), series, _steps[record.ContentId], Palette.LineOf(index), CharacterWeight));
        }
        if (drawn.Count == 0) return;
        DrawPlot(graph, drawn, card.InnerRight);

        // The change over the range: one for the total and one per ticked character, after a
        // dot of its line's colour, green up, rose down, quiet with no change (PageLook.OfChange); cut to the card and whole in a tooltip.
        foreach (var line in drawn) DrawChange(graph, line, card.InnerRight);
        DrawTicks(graph, places, card.InnerRight);
    }

    /// <summary>A stairs line per drawn line, over axes locked to the range and to the amounts drawn, each tick placed and labelled from Core:
    /// no legend (the checkboxes name the lines), no menus, no box select, no mouse text. Hovering shows the day under the mouse and each
    /// line's amount that day. The plot background matches the card: the frame and the plot area are transparent over it, the plot's border
    /// and its ticks the card's border colour, its grid the hairline and its words dim. The plot is as wide as the card's content and its
    /// height follows the UI scale.</summary>
    private static void DrawPlot(GilGraph graph, IReadOnlyList<Line> drawn, float right)
    {
        var days = graph.DayTicks();
        var amounts = GilGraph.AmountTicks(drawn.Min(l => l.Series.Min), drawn.Max(l => l.Series.Max));
        var size = new Vector2(right - ImGui.GetCursorScreenPos().X, Blocks.S(PlotHeight));
        using var colours = ImRaii.PushColor(ImPlotCol.FrameBg, Vector4.Zero).Push(ImPlotCol.Bg, Vector4.Zero).Push(ImPlotCol.Border, Palette.Border)
            .Push(ImPlotCol.AxisGrid, Palette.Hairline).Push(ImPlotCol.AxisTick, Palette.Border).Push(ImPlotCol.AxisText, Palette.TextDim);
        using var plot = ImRaii.Plot("##gil-over-time", size,
            ImPlotFlags.NoLegend | ImPlotFlags.NoMenus | ImPlotFlags.NoBoxSelect | ImPlotFlags.NoMouseText);
        if (!plot) return;
        ImPlot.SetupAxis(ImAxis.X1, ImPlotAxisFlags.Lock | ImPlotAxisFlags.NoMenus);
        ImPlot.SetupAxis(ImAxis.Y1, ImPlotAxisFlags.Lock | ImPlotAxisFlags.NoMenus);
        ImPlot.SetupAxisLimits(ImAxis.X1, days.Low, days.High, ImPlotCond.Always);
        ImPlot.SetupAxisLimits(ImAxis.Y1, amounts.Low, amounts.High, ImPlotCond.Always);
        ImPlot.SetupAxisTicks(ImAxis.X1, ref days.Values[0], days.Values.Length, days.Labels);
        ImPlot.SetupAxisTicks(ImAxis.Y1, ref amounts.Values[0], amounts.Values.Length, amounts.Labels);
        foreach (var line in drawn)
        {
            ImPlot.SetNextLineStyle(line.Colour, line.Weight);
            ImPlot.PlotStairs($"{line.Name}##{line.Id}", ref line.Steps[0], line.Steps.Length, 1, line.Series.First.DayNumber - graph.From.DayNumber);
        }
        if (ImPlot.IsPlotHovered()) ImGui.SetTooltip(Hover(graph.DayAt(ImPlot.GetPlotMousePos().X), drawn));
    }

    /// <summary>One change line as a pill in its tone, after a dot of its line's colour, so the pill is tied to its line; the words are
    /// GilGraph.ChangeLine's, as the dump prints them.</summary>
    private static void DrawChange(GilGraph graph, Line line, float right)
    {
        var words = graph.ChangeLine(line.Name, line.Series);
        var at = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(at + new Vector2(Blocks.S(14f), 0f));
        var (shown, cut) = Blocks.Fit(words, MathF.Max(Blocks.S(40f), right - at.X - Blocks.S(14f) - Blocks.S(Blocks.PillPadding) * 2f));
        Blocks.Pill(shown, Palette.Of(PageLook.OfChange(line.Series)));
        if (cut && ImGui.IsItemHovered()) ImGui.SetTooltip(words);
        Blocks.DotAt(ImGui.GetWindowDrawList(), new Vector2(at.X + Blocks.S(5f), at.Y + ImGui.GetFrameHeight() * 0.5f), line.Colour);
    }

    /// <summary>A checkbox per visible character, in the Tracker's order, three to a row, each name in its line's colour. None is ticked when
    /// the plugin loads. A tick is kept by content id while its character is hidden, so unhiding it brings its line back ticked. A character
    /// with no gil history has no line to add, so its box is greyed and says why. The checkboxes are styled: each box is rounded and, ticked,
    /// filled in its line's colour with the check in the window's ink, as a done box is; its label and id are the character's label, as
    /// before; the table is as wide as the card's content.</summary>
    private void DrawTicks(GilGraph graph, IReadOnlyList<Place> places, float right)
    {
        using var table = ImRaii.Table("gil-lines", 3, ImGuiTableFlags.None, new Vector2(right - ImGui.GetCursorScreenPos().X, 0f));
        if (!table) return;
        foreach (var (record, index) in places)
        {
            using var rowId = ImRaii.PushId(record.ContentId.ToString(CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            var hasLine = graph.Characters.ContainsKey(record.ContentId);
            var ticked = _ticked.Contains(record.ContentId);
            var colour = Palette.LineOf(index);
            using (ImRaii.Disabled(!hasLine))
            using (ImRaii.PushColor(ImGuiCol.Text, colour).Push(ImGuiCol.CheckMark, Palette.Ink)
                       .Push(ImGuiCol.FrameBg, ticked ? colour : Palette.Frame)
                       .Push(ImGuiCol.FrameBgHovered, ticked ? Palette.Mix(colour, Palette.Text, 0.2f) : Palette.FrameHover)
                       .Push(ImGuiCol.FrameBgActive, ticked ? Palette.Mix(colour, Palette.Text, 0.35f) : Palette.FrameActive))
            using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Blocks.S(5f)))
            {
                if (ImGui.Checkbox(record.Label, ref ticked))
                {
                    if (ticked) _ticked.Add(record.ContentId);
                    else _ticked.Remove(record.ContentId);
                }
            }
            if (!hasLine && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("no gil history yet");
        }
    }

    /// <summary>Each line's amounts as the doubles ImPlot draws, converted once per graph Plugin.GilGraphFor builds. The last amount is repeated
    /// one day later: a stairs line draws each point's step up to the next point, so without it today would have no step of its own.</summary>
    private void Steps(GilGraph graph)
    {
        if (ReferenceEquals(graph, _stepsOf)) return;
        _stepsOf = graph;
        _totalSteps = graph.Total is { } total ? ToSteps(total) : [];
        _steps.Clear();
        foreach (var (contentId, series) in graph.Characters) _steps[contentId] = ToSteps(series);
    }

    private static double[] ToSteps(GilSeries series)
    {
        var steps = new double[series.Amounts.Count + 1];
        for (var k = 0; k < series.Amounts.Count; k++) steps[k] = series.Amounts[k];
        steps[^1] = series.Amounts[^1];
        return steps;
    }

    /// <summary>Hovering shows the day and the amounts: the day, then each drawn line's amount that day, exact and grouped by thousands, so a
    /// small change shows. A line with no amount that day is left out.</summary>
    private static string Hover(DateOnly day, IReadOnlyList<Line> lines)
    {
        var text = new StringBuilder(GilGraph.DayWords(day));
        foreach (var line in lines)
        {
            if (line.Series.On(day) is { } amount) text.Append('\n').Append(line.Name).Append(": ").Append(amount.ToString("N0", CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }
}
