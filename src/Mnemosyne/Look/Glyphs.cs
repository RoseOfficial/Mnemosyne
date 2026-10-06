using Dalamud.Interface;
using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Look;

/// <summary>The icon font's glyph for each use. Dalamud's icon font has no plain Exchange glyph, so the switch icon uses ExchangeAlt
/// instead.</summary>
internal static class Glyphs
{
    public const FontAwesomeIcon AllTasks = FontAwesomeIcon.ListUl;
    public const FontAwesomeIcon AllCharacters = FontAwesomeIcon.Users;
    public const FontAwesomeIcon Totals = FontAwesomeIcon.ChartBar;
    public const FontAwesomeIcon Settings = FontAwesomeIcon.Cog;
    public const FontAwesomeIcon Switch = FontAwesomeIcon.ExchangeAlt;
    public const FontAwesomeIcon Search = FontAwesomeIcon.Search;
    public const FontAwesomeIcon Check = FontAwesomeIcon.Check;
    public const FontAwesomeIcon Open = FontAwesomeIcon.ChevronDown;
    public const FontAwesomeIcon Closed = FontAwesomeIcon.ChevronRight;

    /// <summary>An All tasks line's pin, gold; its High priority, a gold ▲, and its Low, a muted ▼; and a table header's sort arrow.</summary>
    public const FontAwesomeIcon Pin = FontAwesomeIcon.Thumbtack;
    public const FontAwesomeIcon Up = FontAwesomeIcon.CaretUp;
    public const FontAwesomeIcon Down = FontAwesomeIcon.CaretDown;

    /// <summary>Story a book, jobs a briefcase, events a star, resets sync, content a dragon, collections a gem, money coins, and My tasks a
    /// clipboard.</summary>
    public static FontAwesomeIcon Of(Category category) => category switch
    {
        Category.Story => FontAwesomeIcon.Book,
        Category.Jobs => FontAwesomeIcon.Briefcase,
        Category.Events => FontAwesomeIcon.Star,
        Category.Resets => FontAwesomeIcon.SyncAlt,
        Category.Content => FontAwesomeIcon.Dragon,
        Category.Collections => FontAwesomeIcon.Gem,
        Category.Money => FontAwesomeIcon.Coins,
        _ => FontAwesomeIcon.ClipboardCheck,
    };
}
