using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Switch;

/// <summary>What the switch button shows on one sidebar row.</summary>
public enum SwitchState
{
    /// <summary>No button: Lifestream is not loaded at its floor with /li registered, the record lacks a name or a home world, or either is
    /// not plain game-style text.</summary>
    Hidden,
    /// <summary>Disabled: no character is logged in, and Lifestream switches from a logged-in one.</summary>
    LogInFirst,
    /// <summary>Disabled: the row is the character logged in.</summary>
    ThisCharacter,
    /// <summary>Disabled: a must-stay-silent flag is on, since Lifestream checks neither combat nor duty state.</summary>
    Silenced,
    Ready,
}

/// <summary>The switch's pure rules, tested without a client. The relog service in Interop reads Dalamud
/// and asks these; the sidebar draws what they say. Mnemosyne dispatches one command and reads nothing back: Lifestream does the
/// logout and the login, and speaks for itself when it cannot.</summary>
public static class SwitchRules
{
    /// <summary>The internal name AutoRetainer's own Lifestream check matches, and the command Lifestream registers for
    /// the handler that /lifestream shares.</summary>
    public const string LifestreamInternalName = "Lifestream";
    public const string LifestreamCommand = "/li";

    /// <summary>The Name@World branch is absent at tag 2.5.2.10 and present at 2.5.3.0 and 2.5.5.0; AutoRetainer demands
    /// the same floor of Lifestream.</summary>
    public static readonly Version LifestreamFloor = new(2, 5, 3, 0);

    /// <summary>Whether Lifestream can take the switch: any loaded copy at the floor or later, and its command registered.</summary>
    public static bool LifestreamReady(IEnumerable<Version> loadedVersions, bool commandRegistered) =>
        commandRegistered && loadedVersions.Any(version => version >= LifestreamFloor);

    /// <summary>The one command the plugin dispatches: /li, a space, then the record's name at its home world.</summary>
    public static string Command(CharacterRecord record) => $"{LifestreamCommand} {record.NameAtWorld}";

    /// <summary>The button's state for one row. Hidden first: no Lifestream, or nothing to name. Then the reasons it cannot ask now, in the
    /// order that says the most: logged out, this very character, a must-stay-silent flag.</summary>
    public static SwitchState State(bool available, CharacterRecord record, ulong? current, bool silenced)
    {
        if (!available || !IsPlainName(record.Name) || !IsPlainWorld(record.HomeWorld)) return SwitchState.Hidden;
        if (current == null) return SwitchState.LogInFirst;
        if (current == record.ContentId) return SwitchState.ThisCharacter;
        return silenced ? SwitchState.Silenced : SwitchState.Ready;
    }

    /// <summary>An imported name or home world can hold anything, and Lifestream's `@` branch reads the argument's first `@`
    /// as the name's end and a second word after the world as a travel destination. So the button hides unless the name is
    /// only letters, apostrophes and hyphens with single spaces between words and no leading or trailing space, and the home world is a single
    /// word of letters: neither can then carry an `@`, a space, a comma or a control character into the command.</summary>
    private static bool IsPlainName(string name) =>
        name.Length > 0 && name.Split(' ').All(word => word.Length > 0 && word.All(c => char.IsLetter(c) || c is '\'' or '-'));

    private static bool IsPlainWorld(string world) => world.Length > 0 && world.All(char.IsLetter);

    /// <summary>The tooltip, on an enabled or a disabled button. It names Lifestream in every state it is drawn in except
    /// Silenced, which names the whole must-stay-silent set instead of Lifestream, and says what Lifestream does while the player
    /// is busy, which Mnemosyne does not check.</summary>
    public static string Tooltip(SwitchState state, CharacterRecord record) => state switch
    {
        SwitchState.Ready => $"Asks Lifestream to log out and log in as {record.Name} on {record.HomeWorld}. Lifestream does the switch: " +
                             "it waits up to a minute while you are busy (crafting, gathering, at a bell) and says so itself when it is busy.",
        SwitchState.LogInFirst => "Log in first: Lifestream switches from a logged-in character.",
        SwitchState.ThisCharacter => "You are logged in as this character; Lifestream can switch you to the others.",
        SwitchState.Silenced => "Not while you are busy: in combat, in a duty, in a cutscene, talking to someone, or between areas.",
        _ => "",
    };

    /// <summary>The chat line after a click: that it asked, and nothing more, since there is no success signal. A false return means
    /// no handler was registered for /li at that moment, so nothing was asked.</summary>
    public static string Asked(CharacterRecord record, bool taken) => taken
        ? $"Asked Lifestream to switch to {record.NameAtWorld}."
        : $"Lifestream's {LifestreamCommand} command is not registered, so nothing was asked.";

    /// <summary>How long a switch counts as in progress at most. Lifestream waits up to
    /// a minute while the player is busy, then logs out and in, and Mnemosyne reads nothing back from it, so the wait ends
    /// here.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    /// <summary>Whether the switch to <paramref name="contentId"/> is in progress, for the pulse on its button:
    /// from the click that asked for that character until that character is the one logged in, or Patience has passed.</summary>
    public static bool InProgress(ulong askedFor, DateTimeOffset askedAt, ulong? current, ulong contentId, DateTimeOffset now) =>
        askedFor == contentId && current != contentId && now >= askedAt && now - askedAt < Patience;
}
