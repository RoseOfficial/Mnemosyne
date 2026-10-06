using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Switch;

namespace Mnemosyne.Interop;

/// <summary>The switch over Lifestream alone. SwitchTo is the one method in the plugin that calls ICommandManager.ProcessCommand,
/// with /li and the record's name at its home world; Only_the_switch_dispatches_a_command holds it there. ProcessCommand runs
/// Lifestream's handler on this thread and sends nothing to the game itself; Lifestream then logs out and in. Nothing here makes
/// an IPC call or reads AutoRetainer.
///
/// InstalledPlugins wraps every installed plugin anew on each read, so it is read again only after ActivePluginsChanged has set the
/// stale flag; the handler does nothing else, because the event is raised from asynchronous load paths. The /li registration is
/// checked on every read of IsAvailable, a dictionary lookup, so a command registered after the event still shows the button.
/// Dalamud's own code: CommandManager.ProcessCommand splits at the first space, returns false when no handler is registered,
/// otherwise dispatches synchronously and returns true.</summary>
public sealed class RelogService : IRelogService, IDisposable
{
    private readonly IDalamudPluginInterface _plugins;
    private readonly ICommandManager _commands;
    // 1 until the installed list has been read, and again whenever Dalamud says the active plugin list changed.
    private int _stale = 1;
    private IReadOnlyList<Version> _lifestream = [];

    public RelogService(IDalamudPluginInterface plugins, ICommandManager commands)
    {
        _plugins = plugins;
        _commands = commands;
        _plugins.ActivePluginsChanged += OnActivePluginsChanged;
    }

    public bool IsAvailable
    {
        get
        {
            if (Interlocked.Exchange(ref _stale, 0) == 1)
            {
                _lifestream = _plugins.InstalledPlugins
                    .Where(plugin => plugin.InternalName == SwitchRules.LifestreamInternalName && plugin.IsLoaded)
                    .Select(plugin => plugin.Version)
                    .ToList();
            }
            return SwitchRules.LifestreamReady(_lifestream, _commands.Commands.ContainsKey(SwitchRules.LifestreamCommand));
        }
    }

    public bool SwitchTo(CharacterRecord record) => _commands.ProcessCommand(SwitchRules.Command(record));

    public void Dispose() => _plugins.ActivePluginsChanged -= OnActivePluginsChanged;

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args) => Interlocked.Exchange(ref _stale, 1);
}
