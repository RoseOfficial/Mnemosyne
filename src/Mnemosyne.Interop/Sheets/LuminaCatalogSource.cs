using Dalamud.Plugin.Services;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Sheets;

/// <summary>The plugin's catalog source, over the data manager's Excel module.</summary>
public sealed class LuminaCatalogSource(IDataManager data, CatalogExtras extras, string gameVersion, IClock clock) : ICatalogSource
{
    public CatalogRows Load() => SheetReducer.Reduce(data.Excel, extras, gameVersion, clock.UtcNow);
}
