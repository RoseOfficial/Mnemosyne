using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;
using Mnemosyne.Look;

namespace Mnemosyne.Windows;

/// <summary>The switch as an icon button, on a sidebar tile, a character's header card and beside each character under an All tasks line,
/// where it replaces the text button MainWindow drew and takes a colour of its own: faint until its row is under the mouse. It behaves as
/// that button did: disabled whenever it cannot ask, its tooltip shown on a disabled button too, so the reason is never hidden, and its
/// click asking Plugin.SwitchTo. Its id is "Switch", the text button's own. It pulses while the switch it asked for is in progress. It moves
/// the cursor; the caller puts it back.</summary>
internal static class SwitchIcon
{
    public static void Draw(Plugin plugin, CharacterRecord record, SwitchState state, Vector2 at, float side, DateTimeOffset now, bool reduced, Vector4? colour = null)
    {
        ImGui.SetCursorScreenPos(at);
        var pulse = plugin.SwitchInProgress(record.ContentId, now) ? Blocks.Pulse(reduced) : -1f;
        using (ImRaii.Disabled(state != SwitchState.Ready))
        {
            if (Blocks.IconButton("Switch", Glyphs.Switch, colour ?? Palette.Accent, side, pulse)) plugin.SwitchTo(record);
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(SwitchRules.Tooltip(state, record));
    }
}
