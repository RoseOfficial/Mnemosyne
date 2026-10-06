using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;

namespace Mnemosyne.Look;

/// <summary>The game's Axis font, larger, for a character's name and page titles, and smaller, for the small capitals of a section label.
/// Built once, on the plugin's own font atlas, when the plugin loads; Dalamud rebuilds that atlas when the global UI scale changes, and
/// Dispose frees both at unload. Until one is built, its Push keeps the normal font, as Dalamud's own font-handle API does when a font is not
/// yet available.</summary>
internal sealed class LookFonts(IUiBuilder ui) : IDisposable
{
    /// <summary>AXIS at 18 pt: 24 pixels at 100 %, against the normal font's 16.</summary>
    public IFontHandle Title { get; } = ui.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis18));

    /// <summary>AXIS at 9.6 pt: about 13 pixels at 100 %, the section labels' small capitals.</summary>
    public IFontHandle Label { get; } = ui.FontAtlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis96));

    public void Dispose()
    {
        Title.Dispose();
        Label.Dispose();
    }
}
