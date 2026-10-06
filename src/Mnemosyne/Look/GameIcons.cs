using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Plugin.Services;

namespace Mnemosyne.Look;

/// <summary>A row's game icon, through Dalamud's texture provider, which loads it lazily and caches it. TryGetFromGameIcon returns false rather
/// than throwing for an icon the game data lacks, and GetWrapOrEmpty gives an empty texture while the icon loads or when its load failed. An
/// icon the game data lacks is remembered here, so its path is not searched for again every frame. No icon, a missing one and one still
/// loading all draw a blank square of the same size, never an error.</summary>
internal sealed class GameIcons(ITextureProvider textures)
{
    private readonly HashSet<uint> _missing = [];

    /// <summary>The icon as an item, a square of the given side, or a blank square of that side.</summary>
    public void Draw(uint? iconId, float side)
    {
        var size = new Vector2(side, side);
        if (iconId is { } id && !_missing.Contains(id))
        {
            if (textures.TryGetFromGameIcon(new GameIconLookup(id), out var texture))
            {
                ImGui.Image(texture.GetWrapOrEmpty().Handle, size);
                return;
            }
            _missing.Add(id);
        }
        ImGui.Dummy(size);
    }
}
