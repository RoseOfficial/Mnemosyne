namespace Mnemosyne.Core.Storage;

/// <summary>Core never touches the file system directly. The plugin provides the real one; tests an in-memory one.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string text);
    /// <summary>Replaces the destination if it exists.</summary>
    void Move(string from, string to);
    void Delete(string path);
    /// <summary>Files directly inside the directory whose name matches the pattern, such as "*.json".</summary>
    IReadOnlyList<string> ListFiles(string directory, string pattern);
    void CreateDirectory(string directory);
}
