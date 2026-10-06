using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Switch;

/// <summary>The optional switch, provided by Interop over Lifestream alone. IsAvailable is Lifestream
/// loaded at its floor with its command registered (SwitchRules.LifestreamReady); SwitchTo dispatches SwitchRules.Command and says whether a
/// handler took it. Its implementation is the one caller of ICommandManager.ProcessCommand in the plugin.</summary>
public interface IRelogService
{
    bool IsAvailable { get; }
    bool SwitchTo(CharacterRecord record);
}
