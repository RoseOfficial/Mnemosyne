namespace Mnemosyne.Core.Storage;

/// <summary>The text goes to "&lt;path&gt;.tmp" beside the target and is then moved over it, so a write that fails part way
/// leaves the older file whole. The record store's files and the export both write this way.</summary>
public static class AtomicFile
{
    public static void Write(IFileSystem fs, string path, string text)
    {
        var tmp = path + ".tmp";
        fs.WriteAllText(tmp, text);
        fs.Move(tmp, path);
    }
}
