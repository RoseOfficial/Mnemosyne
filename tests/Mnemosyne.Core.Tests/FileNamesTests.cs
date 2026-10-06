using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

/// <summary>A folder's path taken out of a message the settings page or the chat shows, so a file is named alone.</summary>
public class FileNamesTests
{
    [Fact]
    public void A_folder_is_taken_out_of_a_message_in_either_slash_and_any_case_leaving_the_file_names()
    {
        const string folder = @"D:\Games\Data";
        Assert.Equal("Access to the path 'mnemosyne-20260926-120000.json.tmp' is denied.",
            FileNames.Hide(@"Access to the path 'D:\Games\Data\mnemosyne-20260926-120000.json.tmp' is denied.", folder, "the chosen folder"));
        Assert.Equal("Could not find a part of the path 'the chosen folder'.",
            FileNames.Hide(@"Could not find a part of the path 'd:\games\data'.", folder + @"\", "the chosen folder"));
        Assert.Equal("sheets-1.json is in use", FileNames.Hide(@"D:\Games\Data/sheets-1.json is in use", folder, "the chosen folder"));
        Assert.Equal("disk full", FileNames.Hide("disk full", null, "the chosen folder"));
        Assert.Equal(@"D:\Games\Data\x.json", FileNames.Hide(@"D:\Games\Data\x.json", "", "the chosen folder"));
    }
}
