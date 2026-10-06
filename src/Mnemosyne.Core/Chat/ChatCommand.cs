namespace Mnemosyne.Core.Chat;

/// <summary>What /mnemo was asked to do.</summary>
public enum ChatAction { Toggle, Settings, Help, Dump, DumpSheets, Open }

/// <summary>The /mnemo command's arguments and its help. A command word is read whole and in any case; anything else is the start of a
/// character's name, so "helpful" opens a character and "help" prints the help.</summary>
public static class ChatCommand
{
    public const string Name = "/mnemo";

    /// <summary>The one line Dalamud shows beside the command in its own lists.</summary>
    public const string HelpMessage = "Open or close Mnemosyne. /mnemo settings, /mnemo <character name>, /mnemo dump, /mnemo help";

    /// <summary>What /mnemo help prints, one chat line each. The sheet dump, a tool for rebuilding the test data after a game patch, is left
    /// out: it still works, and a player has no use for it.</summary>
    public static readonly IReadOnlyList<string> Help =
    [
        "/mnemo — open or close the window",
        "/mnemo settings — open the settings",
        "/mnemo <name> — open the window on the character whose name starts with <name>",
        "/mnemo dump — write a file for troubleshooting to the plugin configuration folder; it holds your characters' names and content ids",
        "/mnemo help — show this list",
    ];

    public static (ChatAction Action, string Argument) Parse(string arguments)
    {
        var text = arguments.Trim();
        var words = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return words switch
        {
            "" => (ChatAction.Toggle, ""),
            "settings" => (ChatAction.Settings, ""),
            "help" or "?" => (ChatAction.Help, ""),
            "dump" => (ChatAction.Dump, ""),
            "dump sheets" => (ChatAction.DumpSheets, ""),
            _ => (ChatAction.Open, text),
        };
    }

    /// <summary>The answer when no visible character's name starts with what was typed.</summary>
    public static string NoMatch(string name) => $"no character starts with \"{name}\"; {Name} help lists the commands";
}
