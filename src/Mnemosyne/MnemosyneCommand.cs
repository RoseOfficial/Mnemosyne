using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using Mnemosyne.Core.Chat;

namespace Mnemosyne;

/// <summary>The one chat command, /mnemo, registered once; ChatCommand reads its arguments.</summary>
public sealed class MnemosyneCommand : IDisposable
{
    private readonly ICommandManager _commands;
    private readonly Plugin _plugin;
    private readonly bool _registered;

    public MnemosyneCommand(ICommandManager commands, Plugin plugin)
    {
        _commands = commands;
        _plugin = plugin;
        _registered = commands.AddHandler(Plugin.Command, new CommandInfo(Handle) { HelpMessage = ChatCommand.HelpMessage });
        if (!_registered) Plugin.Log.Warning("{Command} is already registered by another plugin", Plugin.Command);
    }

    private void Handle(string command, string arguments)
    {
        var (action, name) = ChatCommand.Parse(arguments);
        switch (action)
        {
            case ChatAction.Toggle: _plugin.MainWindow.Toggle(); break;
            case ChatAction.Settings: _plugin.SettingsWindow.IsOpen = true; break;
            case ChatAction.Help: foreach (var line in ChatCommand.Help) _plugin.Say(line); break;
            case ChatAction.Dump: _plugin.Dump(); break;
            case ChatAction.DumpSheets: _plugin.DumpSheets(); break;
            default: if (!_plugin.MainWindow.SelectByName(name)) _plugin.Say(ChatCommand.NoMatch(name)); break;
        }
    }

    public void Dispose() { if (_registered) _commands.RemoveHandler(Plugin.Command); }
}
