using Dalamud.Plugin.Services;

namespace Mnemosyne.Interop;

/// <summary>The game version string, from the ffxiv repository the data manager exposes. Used in reader-disabled messages.</summary>
public static class GameVersion
{
    public static string Read(IDataManager data, IPluginLog log)
    {
        try
        {
            return data.GameData.Repositories.TryGetValue("ffxiv", out var repo) ? repo.Version : "unknown";
        }
        catch (Exception ex)
        {
            log.Warning(ex, "game version read failed");
            return "unknown";
        }
    }
}
