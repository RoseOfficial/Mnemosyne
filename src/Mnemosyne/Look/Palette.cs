using System.Numerics;
using Dalamud.Bindings.ImGui;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Look;

/// <summary>The one theme's colours: the surfaces and text, the status colours and the category colours, and the few surfaces the window
/// style needs beyond them, each mixed from those. Tone and Category are Core's; this gives each its colour. U32 goes through
/// ImGui.GetColorU32, so a colour drawn on the window's draw list honours the style's alpha: a disabled button, a dimmed row and Dalamud's own
/// window fade all reach it.</summary>
internal static class Palette
{
    // The surfaces and the text.
    public static readonly Vector4 Window = Rgb(0x0E1220, 0.97f);
    public static readonly Vector4 Card = Rgb(0x151A2C, 0.92f);
    public static readonly Vector4 CardHover = Rgb(0x1C2238);
    public static readonly Vector4 Border = Rgb(0x2A3150);
    public static readonly Vector4 Hairline = new(1f, 1f, 1f, 0.07f);
    public static readonly Vector4 Text = Rgb(0xEEF0F8);
    public static readonly Vector4 TextSecondary = Rgb(0xB8BDD4);
    public static readonly Vector4 TextDim = Rgb(0x7C83A0);
    public static readonly Vector4 TextMuted = Rgb(0x555B74);
    public static readonly Vector4 Accent = Rgb(0x9B8CFF);
    public static readonly Vector4 Gold = Rgb(0xF2C45A);

    // The status colours, one meaning everywhere.
    public static readonly Vector4 Done = Rgb(0x5BD08A);
    public static readonly Vector4 Today = Rgb(0xF5B94A);
    public static readonly Vector4 EndingSoon = Rgb(0xF2667A);
    public static readonly Vector4 ThisWeek = Rgb(0x6AA8FF);
    public static readonly Vector4 Running = Rgb(0xC58CF2);
    public static readonly Vector4 Quiet = Rgb(0x6B7190);

    // The surfaces beyond the theme's own colours, mixed from them.
    /// <summary>The sidebar: a step from the window toward a card.</summary>
    public static readonly Vector4 Sidebar = Mix(Rgb(0x0E1220), Rgb(0x151A2C), 0.6f) with { W = 0.95f };
    /// <summary>An input field or an empty box, and its hover and press.</summary>
    public static readonly Vector4 Frame = CardHover;
    public static readonly Vector4 FrameHover = Mix(CardHover, Accent, 0.12f);
    public static readonly Vector4 FrameActive = Mix(CardHover, Accent, 0.22f);
    /// <summary>The window's colour, opaque: the check on a done box, the knob of a toggle that is on.</summary>
    public static readonly Vector4 Ink = Rgb(0x0E1220);

    /// <summary>A status tone's colour. Neutral is the text colour.</summary>
    public static Vector4 Of(Tone tone) => tone switch
    {
        Tone.Done => Done,
        Tone.Today => Today,
        Tone.EndingSoon => EndingSoon,
        Tone.ThisWeek => ThisWeek,
        Tone.Running => Running,
        Tone.Quiet => Quiet,
        _ => Text,
    };

    /// <summary>A bar's fill: the tone's colour, and the accent for a Neutral row, whose text colour would read as an empty bar.</summary>
    public static Vector4 BarOf(Tone tone) => tone == Tone.Neutral ? Accent : Of(tone);

    /// <summary>Each category's colour, for its header and icon.</summary>
    public static Vector4 Of(Category category) => category switch
    {
        Category.Story => Rgb(0xF2C45A),
        Category.Jobs => Rgb(0x6AA8FF),
        Category.Events => Rgb(0xF27BC0),
        Category.Resets => Rgb(0xF5B94A),
        Category.Content => Rgb(0x4FD1C5),
        Category.Collections => Rgb(0xB08CFF),
        Category.Money => Rgb(0x5BD08A),
        _ => Rgb(0x7FE3C4),
    };

    /// <summary>The character lines in Starlight colours that stay clear of gold: eight colours, none gold or amber, so no character's line
    /// reads as the gold All gil line: blue, pink, teal, violet, green, lilac, rose and mint, neighbours far apart in hue. Taken in turn by a
    /// character's place in the Tracker's list, hidden ones counted, so hiding one character moves no other's colour and a line and its
    /// checkbox always match.</summary>
    private static readonly Vector4[] Lines =
        [Rgb(0x6AA8FF), Rgb(0xF27BC0), Rgb(0x4FD1C5), Rgb(0x9B8CFF), Rgb(0x5BD08A), Rgb(0xC58CF2), Rgb(0xF2667A), Rgb(0x7FE3C4)];

    /// <summary>The colour of the character at this place in the Tracker's list, the eight taken in turn and started again after the last.</summary>
    public static Vector4 LineOf(int place) => Lines[place % Lines.Length];

    public static Vector4 Rgb(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 Fade(Vector4 colour, float alpha) => colour with { W = colour.W * alpha };

    public static Vector4 Mix(Vector4 from, Vector4 to, float amount) => Vector4.Lerp(from, to, amount);

    public static uint U32(Vector4 colour) => ImGui.GetColorU32(colour);
}
