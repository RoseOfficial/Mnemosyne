using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mnemosyne.Assembly.Tests;

/// <summary>The manifest lives in Mnemosyne.json alone and says what the plugin does, in the plugin's own words. The sentences are read
/// from the built Mnemosyne.dll's constant table, so the JSON and the code cannot drift apart.</summary>
public class ManifestTests
{
    /// <summary>Verbatim in the manifest's description, on the settings page and in the first-run notice.</summary>
    private const string PrivacySentence = "Mnemosyne stores character data only on this PC. It is never uploaded anywhere.";

    private static string Read(string relative) => File.ReadAllText(Path.Combine(AssemblyTests.RepoRoot(), relative));

    /// <summary>A string constant as the newest built Mnemosyne.dll holds it: the field's row in the Constant table, read as UTF-16.</summary>
    private static string Constant(string typeName, string fieldName)
    {
        using var stream = File.OpenRead(AssemblyTests.Newest("Mnemosyne.dll"));
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            if (md.GetString(type.Namespace) + "." + md.GetString(type.Name) != typeName) continue;
            foreach (var fieldHandle in type.GetFields())
            {
                var field = md.GetFieldDefinition(fieldHandle);
                if (md.GetString(field.Name) != fieldName) continue;
                var value = md.GetBlobReader(md.GetConstant(field.GetDefaultValue()).Value);
                return value.ReadUTF16(value.Length);
            }
        }
        throw new InvalidOperationException($"{typeName}.{fieldName} is not a constant of the built Mnemosyne.dll");
    }

    [Fact]
    public void The_manifest_lives_in_its_json_alone_and_keeps_its_promises()
    {
        // The packager never reads the csproj's copy of these fields, so a stale value there (LoadSync once said true in the csproj
        // while the shipped manifest said false) would drift unnoticed. The csproj's <Version> is not a manifest field.
        var csproj = Read("src/Mnemosyne/Mnemosyne.csproj");
        foreach (var field in new[] { "LoadSync", "Author", "Name", "Punchline", "Description", "RepoUrl", "Tags" })
            Assert.DoesNotContain($"<{field}>", csproj);
        using var manifest = JsonDocument.Parse(Read("src/Mnemosyne/Mnemosyne.json"));
        var root = manifest.RootElement;
        var description = root.GetProperty("Description").GetString()!;
        Assert.Equal("RoseOfficial", root.GetProperty("Author").GetString());
        Assert.Equal(PrivacySentence, Constant("Mnemosyne.Plugin", "PrivacySentence"));   // the settings page's and the notice's own words
        Assert.Contains(PrivacySentence, description);
        // The description ends with the privacy sentence, then the switch's clause, in Plugin.ReadOnlySentence's words, which
        // the first-run notice also shows.
        var readOnly = Constant("Mnemosyne.Plugin", "ReadOnlySentence");
        Assert.Contains("Lifestream", readOnly);
        Assert.EndsWith(PrivacySentence + " " + readOnly, description);
        Assert.Equal("any", root.GetProperty("ApplicableVersion").GetString());           // no game-version pin
        Assert.False(root.TryGetProperty("LoadSync", out _));                              // false: the catch-up is marshalled instead
    }

    [Fact]
    public void The_first_run_notice_says_how_a_character_appears_and_how_to_open_the_list()
    {
        var hint = Constant("Mnemosyne.Windows.SettingsWindow", "FirstRunHint");
        Assert.Contains("Log into each character once", hint);
        Assert.EndsWith("Type /mnemo to open it.", hint);
    }

    [Fact]
    public void The_description_says_what_a_stranger_needs_first()
    {
        using var manifest = JsonDocument.Parse(Read("src/Mnemosyne/Mnemosyne.json"));
        var description = manifest.RootElement.GetProperty("Description").GetString()!;
        Assert.StartsWith("A progress checklist for all your characters.", description);
        Assert.Contains("after you log into it once with the plugin running", description);
        foreach (var feature in new[] { "one list", "a role", "gil over time", "your own tasks" }) Assert.Contains(feature, description);
    }

    [Fact]
    public void The_version_has_three_parts_and_lives_in_the_plugin_project_alone()
    {
        // The release workflow compares the tag, less its "v", with this string, and the changelog's latest heading with the tag.
        var csproj = Read("src/Mnemosyne/Mnemosyne.csproj");
        var version = Regex.Match(csproj, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        Assert.Contains($"<InformationalVersion>{version}+", csproj);
        Assert.DoesNotContain("<Version>", Read("src/Mnemosyne.Interop/Mnemosyne.Interop.csproj"));
        Assert.DoesNotContain("<Version>", Read("src/Mnemosyne.Core/Mnemosyne.Core.csproj"));
    }
}
