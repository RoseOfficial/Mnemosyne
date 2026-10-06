using Mnemosyne.Core.Chat;
using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

/// <summary>The /mnemo command's words, its help, and the dump files it writes.</summary>
public class ChatCommandTests
{
    [Fact]
    public void Each_word_is_read_whole_in_any_case_and_anything_else_starts_a_characters_name()
    {
        Assert.Equal((ChatAction.Toggle, ""), ChatCommand.Parse("   "));
        Assert.Equal((ChatAction.Settings, ""), ChatCommand.Parse(" SETTINGS "));
        Assert.Equal((ChatAction.Help, ""), ChatCommand.Parse("help"));
        Assert.Equal((ChatAction.Help, ""), ChatCommand.Parse("?"));
        Assert.Equal((ChatAction.Dump, ""), ChatCommand.Parse("Dump"));
        Assert.Equal((ChatAction.DumpSheets, ""), ChatCommand.Parse("dump  sheets"));
        Assert.Equal((ChatAction.Open, "Aria Whi"), ChatCommand.Parse(" Aria Whi "));
        Assert.Equal((ChatAction.Open, "helpful"), ChatCommand.Parse("helpful"));
    }

    [Fact]
    public void The_help_lists_every_command_a_player_uses_and_never_the_sheet_dump()
    {
        Assert.Equal(["/mnemo", "/mnemo settings", "/mnemo <name>", "/mnemo dump", "/mnemo help"],
            ChatCommand.Help.Select(line => line[..line.IndexOf(" — ", StringComparison.Ordinal)]));
        Assert.DoesNotContain(ChatCommand.Help, line => line.Contains("sheets", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("sheets", ChatCommand.HelpMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/mnemo help", ChatCommand.HelpMessage);
        Assert.Equal("no character starts with \"Zz\"; /mnemo help lists the commands", ChatCommand.NoMatch("Zz"));
    }

    [Fact]
    public void A_dump_is_named_by_content_id_and_utc_second_and_its_chat_line_warns_what_it_holds()
    {
        var at = new DateTimeOffset(2026, 9, 26, 22, 5, 9, TimeSpan.FromHours(2));
        Assert.Equal("dump-18014398509481984-20260926-200509.txt", Dumps.FileName(18014398509481984, at));
        var line = Dumps.Written("dump-1-20260926-200509.txt");
        Assert.StartsWith("dump written: dump-1-20260926-200509.txt", line);
        Assert.Contains("names and content ids", line);
    }

    [Fact]
    public void Only_the_five_newest_dumps_are_kept_by_the_stamp_in_their_names()
    {
        string[] names =
        [
            "dump-1-20260920-100000.txt", "dump-2-20260926-090000.txt", "dump-1-20260921-100000.txt", "dump-1-20260922-100000.txt",
            "dump-2-20260919-235959.txt", "dump-1-20260923-100000.txt", "dump-1-20260924-100000.txt",
            "settings.json", "dump-notes.txt", "dump-1-2026.txt", "sheets-2026.09.01.json",
        ];
        Assert.Equal(["dump-1-20260920-100000.txt", "dump-2-20260919-235959.txt"], Dumps.Older(names));
        Assert.Empty(Dumps.Older(names.Take(5)));
    }
}
