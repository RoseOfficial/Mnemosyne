namespace Mnemosyne.Core.Storage;

/// <summary>Takes a folder's path out of a message, so a file inside it is named alone and the folder itself by a few words. A folder under the
/// Windows user profile holds the user name, and a message shown on the settings page or in chat is easily shared in a screenshot. Either
/// slash and any case are matched, since the system's own messages may use either.</summary>
public static class FileNames
{
    public static string Hide(string text, string? folder, string folderWords)
    {
        var trimmed = (folder ?? "").TrimEnd('\\', '/');
        if (trimmed.Length == 0) return text;
        return text.Replace(trimmed + '\\', "", StringComparison.OrdinalIgnoreCase)
            .Replace(trimmed + '/', "", StringComparison.OrdinalIgnoreCase)
            .Replace(trimmed, folderWords, StringComparison.OrdinalIgnoreCase);
    }
}
