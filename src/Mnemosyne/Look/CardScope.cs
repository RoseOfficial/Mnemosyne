using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Mnemosyne.Look;

/// <summary>A rounded panel, the width it is given or of the region it starts in, with an optional coloured edge on the left. Its content is
/// drawn first, on the upper of two channels of the window's draw list, in a group inset by the card's padding; Dispose then draws the panel
/// on the lower channel, behind the content, at the height the content took, and merges the two. So a card is not a child window, and every id
/// inside it is the id the same item always had. Cards do not nest: a draw list splits its channels once, so nothing drawn inside one splits
/// channels again. The constructor throws when one is opened while another is still open, so a caller meets the mistake at once rather than an
/// ImGui assert deep in ChannelsSplit; the depth it checks is decremented in Dispose on every path, so a card that throws while open still lets
/// the next one open. Use it with using, so the channels merge, and the depth clears, even when the content throws, using Dalamud's own ImGui
/// draw-list bindings.</summary>
internal ref struct CardScope
{
    /// <summary>How many CardScopes are open right now (0 or 1; never more, once the guard below is in place). Static because ImGui draws on
    /// one thread and every card, whatever window it is in, shares the one rule: cards do not nest.</summary>
    private static int _depth;

    private ImDrawListPtr _drawList;
    private readonly Vector2 _min;
    private readonly float _right;
    private readonly Vector4? _edge;
    private bool _alive;

    public CardScope(Vector4? edge, float width)
    {
        if (_depth > 0) throw new InvalidOperationException("CardScope opened inside another CardScope: cards do not nest.");
        _depth++;
        _drawList = ImGui.GetWindowDrawList();
        _min = ImGui.GetCursorScreenPos();
        _right = _min.X + (width > 0f ? width : ImGui.GetContentRegionAvail().X);
        _edge = edge;
        _drawList.ChannelsSplit(2);
        _drawList.ChannelsSetCurrent(1);
        var pad = Blocks.S(Blocks.CardPadding);
        ImGui.SetCursorScreenPos(_min + new Vector2(pad + (edge != null ? Blocks.S(Blocks.EdgeWidth) : 0f), pad));
        ImGui.BeginGroup();
        _alive = true;
    }

    /// <summary>Where the card's content ends on the right, in screen space: right-aligned values and bars end here.</summary>
    public readonly float InnerRight => _right - Blocks.S(Blocks.CardPadding);

    /// <summary>The same edge as a text wrap position, in the window's local space, for ImRaii.TextWrapPos.</summary>
    public readonly float WrapPos => ImGui.GetCursorPosX() + (InnerRight - ImGui.GetCursorScreenPos().X);

    public void Dispose()
    {
        if (!_alive) return;
        _alive = false;
        _depth--;
        ImGui.EndGroup();
        var max = new Vector2(_right, ImGui.GetItemRectMax().Y + Blocks.S(Blocks.CardPadding));
        var rounding = Blocks.S(Blocks.CardRounding);
        _drawList.ChannelsSetCurrent(0);
        _drawList.AddRectFilled(_min, max, Palette.U32(Palette.Card), rounding);
        _drawList.AddRect(_min, max, Palette.U32(Palette.Border), rounding);
        if (_edge is { } edge) Blocks.EdgeAt(_drawList, _min, max, edge, rounding);
        _drawList.ChannelsMerge();
        ImGui.SetCursorScreenPos(new Vector2(_min.X, max.Y));
        ImGui.Dummy(new Vector2(_right - _min.X, 0f));
    }
}
