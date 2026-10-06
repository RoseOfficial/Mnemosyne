using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Mnemosyne.Assembly.Tests;

/// <summary>Each built DLL carries its symbols inside it, and a build on a GitHub runner, the only build ever released, names every source file
/// by a mapped path ("/_/..."), so the released zip holds no machine's folder names. Read from the built files, without loading them.</summary>
public class SymbolTests
{
    [Theory]
    [InlineData("Mnemosyne.Core.dll")]
    [InlineData("Mnemosyne.Interop.dll")]
    [InlineData("Mnemosyne.dll")]
    public void The_symbols_are_embedded_and_a_runner_build_maps_every_source_path(string fileName)
    {
        using var stream = File.OpenRead(AssemblyTests.Newest(fileName));
        using var pe = new PEReader(stream);
        var entry = pe.ReadDebugDirectory().SingleOrDefault(e => e.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
        Assert.True(entry.DataSize > 0, $"{fileName} has no embedded symbols");
        using var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
        var pdb = provider.GetMetadataReader();
        var documents = pdb.Documents.Select(handle => pdb.GetString(pdb.GetDocument(handle).Name)).ToList();
        Assert.NotEmpty(documents);
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            Assert.All(documents, name => Assert.StartsWith("/_/", name));
    }
}
