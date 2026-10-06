using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace Mnemosyne.Look;

/// <summary>The palette and the window style, pushed when a Mnemosyne window starts drawing and popped when it ends, so a user's own Dalamud
/// style is untouched outside Mnemosyne and Mnemosyne's windows look the same whatever it is. Every one of ImGui's 55 colour slots is pushed,
/// so no slot of the user's style shows through. Sizes are given at 100 % and scaled by Dalamud's global UI scale when pushed. Dalamud's own
/// window host calls Window.PreDraw() before ImGui.Begin and Window.PostDraw() after ImGui.End, both only when the window drew without an
/// error at the start of the frame, so a push in PreDraw and a pop in PostDraw always pair; the host's own title bar background, its title bar
/// buttons and its context-actions menu fall between them.</summary>
internal static class Starlight
{
    private static readonly (ImGuiCol Slot, Vector4 Colour)[] Colours =
    [
        (ImGuiCol.Text, Palette.Text),
        (ImGuiCol.TextDisabled, Palette.TextDim),
        (ImGuiCol.WindowBg, Palette.Window),
        (ImGuiCol.ChildBg, Vector4.Zero),
        (ImGuiCol.PopupBg, Palette.Card with { W = 0.98f }),
        (ImGuiCol.Border, Palette.Border),
        (ImGuiCol.BorderShadow, Vector4.Zero),
        (ImGuiCol.FrameBg, Palette.Frame),
        (ImGuiCol.FrameBgHovered, Palette.FrameHover),
        (ImGuiCol.FrameBgActive, Palette.FrameActive),
        (ImGuiCol.TitleBg, Palette.Ink),
        (ImGuiCol.TitleBgActive, Palette.Card with { W = 1f }),
        (ImGuiCol.TitleBgCollapsed, Palette.Fade(Palette.Ink, 0.9f)),
        (ImGuiCol.MenuBarBg, Palette.Card),
        (ImGuiCol.ScrollbarBg, Vector4.Zero),
        (ImGuiCol.ScrollbarGrab, Palette.Border),
        (ImGuiCol.ScrollbarGrabHovered, Palette.Mix(Palette.Border, Palette.Accent, 0.3f)),
        (ImGuiCol.ScrollbarGrabActive, Palette.Accent),
        (ImGuiCol.CheckMark, Palette.Accent),
        (ImGuiCol.SliderGrab, Palette.Accent),
        (ImGuiCol.SliderGrabActive, Palette.Mix(Palette.Accent, Palette.Text, 0.3f)),
        (ImGuiCol.Button, Palette.Frame),
        (ImGuiCol.ButtonHovered, Palette.FrameHover),
        (ImGuiCol.ButtonActive, Palette.Fade(Palette.Accent, 0.4f)),
        (ImGuiCol.Header, Palette.Fade(Palette.Accent, 0.22f)),
        (ImGuiCol.HeaderHovered, Palette.FrameHover),
        (ImGuiCol.HeaderActive, Palette.Fade(Palette.Accent, 0.3f)),
        (ImGuiCol.Separator, Palette.Hairline),
        (ImGuiCol.SeparatorHovered, Palette.Fade(Palette.Accent, 0.5f)),
        (ImGuiCol.SeparatorActive, Palette.Accent),
        (ImGuiCol.ResizeGrip, Palette.Fade(Palette.Accent, 0.2f)),
        (ImGuiCol.ResizeGripHovered, Palette.Fade(Palette.Accent, 0.5f)),
        (ImGuiCol.ResizeGripActive, Palette.Fade(Palette.Accent, 0.8f)),
        (ImGuiCol.Tab, Palette.Card),
        (ImGuiCol.TabHovered, Palette.FrameHover),
        (ImGuiCol.TabActive, Palette.Fade(Palette.Accent, 0.35f)),
        (ImGuiCol.TabUnfocused, Palette.Card),
        (ImGuiCol.TabUnfocusedActive, Palette.Frame),
        (ImGuiCol.DockingPreview, Palette.Fade(Palette.Accent, 0.5f)),
        (ImGuiCol.DockingEmptyBg, Palette.Window),
        (ImGuiCol.PlotLines, Palette.Accent),
        (ImGuiCol.PlotLinesHovered, Palette.Gold),
        (ImGuiCol.PlotHistogram, Palette.Accent),
        (ImGuiCol.PlotHistogramHovered, Palette.Gold),
        (ImGuiCol.TableHeaderBg, Palette.Card),
        (ImGuiCol.TableBorderStrong, Palette.Border),
        (ImGuiCol.TableBorderLight, Palette.Hairline),
        (ImGuiCol.TableRowBg, Vector4.Zero),
        (ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.025f)),
        (ImGuiCol.TextSelectedBg, Palette.Fade(Palette.Accent, 0.35f)),
        (ImGuiCol.DragDropTarget, Palette.Gold),
        (ImGuiCol.NavHighlight, Palette.Accent),
        (ImGuiCol.NavWindowingHighlight, Palette.Fade(Palette.Accent, 0.7f)),
        (ImGuiCol.NavWindowingDimBg, Palette.Fade(Palette.Ink, 0.5f)),
        (ImGuiCol.ModalWindowDimBg, Palette.Fade(Palette.Ink, 0.6f)),
    ];

    /// <summary>Rounded windows, frames and cards, and more room between rows, at 100 %.</summary>
    private static readonly (ImGuiStyleVar Slot, float Size)[] Sizes =
    [
        (ImGuiStyleVar.WindowRounding, 10f), (ImGuiStyleVar.ChildRounding, 8f), (ImGuiStyleVar.FrameRounding, 6f),
        (ImGuiStyleVar.PopupRounding, 8f), (ImGuiStyleVar.ScrollbarRounding, 8f), (ImGuiStyleVar.GrabRounding, 6f),
        (ImGuiStyleVar.TabRounding, 6f), (ImGuiStyleVar.WindowBorderSize, 1f), (ImGuiStyleVar.ChildBorderSize, 1f),
        (ImGuiStyleVar.PopupBorderSize, 1f), (ImGuiStyleVar.FrameBorderSize, 0f), (ImGuiStyleVar.IndentSpacing, 18f),
        (ImGuiStyleVar.ScrollbarSize, 12f), (ImGuiStyleVar.GrabMinSize, 10f),
    ];

    private static readonly (ImGuiStyleVar Slot, Vector2 Size)[] Pairs =
    [
        (ImGuiStyleVar.WindowPadding, new Vector2(12f, 12f)), (ImGuiStyleVar.FramePadding, new Vector2(8f, 4f)),
        (ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f)), (ImGuiStyleVar.ItemInnerSpacing, new Vector2(6f, 4f)),
        (ImGuiStyleVar.CellPadding, new Vector2(6f, 4f)),
    ];

    /// <summary>From a window's PreDraw: before ImGui.Begin, so the window's own background, border, rounding and title bar take it.</summary>
    public static void Push()
    {
        foreach (var (slot, colour) in Colours) ImGui.PushStyleColor(slot, colour);
        var scale = ImGuiHelpers.GlobalScale;
        foreach (var (slot, size) in Sizes) ImGui.PushStyleVar(slot, size * scale);
        foreach (var (slot, size) in Pairs) ImGui.PushStyleVar(slot, size * scale);
    }

    /// <summary>From the same window's PostDraw: exactly what Push pushed.</summary>
    public static void Pop()
    {
        ImGui.PopStyleVar(Sizes.Length + Pairs.Length);
        ImGui.PopStyleColor(Colours.Length);
    }
}
