using Mnemosyne.Core.Storage;

namespace Mnemosyne;

/// <summary>The real file system. Move replaces the destination, which is what makes the tmp-then-move write atomic.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string text) => File.WriteAllText(path, text);
    public void Move(string from, string to) => File.Move(from, to, overwrite: true);
    public void Delete(string path) => File.Delete(path);
    public IReadOnlyList<string> ListFiles(string directory, string pattern) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly) : [];
    public void CreateDirectory(string directory) => Directory.CreateDirectory(directory);
}
