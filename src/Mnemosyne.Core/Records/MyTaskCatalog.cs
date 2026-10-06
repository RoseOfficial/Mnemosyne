using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Records;

/// <summary>The catalog the checklists read. The sheet-built catalog is built once at start
/// and never holds a My task. This adds the My tasks part, one item per usable task, once per settings version: every definitions edit moves
/// that version (Plugin.UpdateSettings, ReplaceSettings), so it is read on the next frame, and a frame that changed nothing reuses the catalog
/// it has. With no usable task it is the sheet-built catalog itself. The readers and the cactpot observer keep the sheet-built one: none of
/// them ever touches a My task.</summary>
public sealed class MyTaskCatalog(Catalog sheets)
{
    private Catalog? _built;
    private int _version;

    public Catalog For(Settings settings, int version)
    {
        if (_built != null && _version == version) return _built;
        _built = settings.MyTasks.Usable.Any() ? new Catalog([.. sheets.Items, .. settings.MyTasks.Items()]) : sheets;
        _version = version;
        return _built;
    }
}
