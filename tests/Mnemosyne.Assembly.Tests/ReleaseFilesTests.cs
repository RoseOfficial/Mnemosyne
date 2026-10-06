using System.Text.RegularExpressions;

namespace Mnemosyne.Assembly.Tests;

/// <summary>The files a release reads: the changelog's one latest block, headed by the project's version, which the release workflow checks
/// against the tag; the licence; and the two workflows, which build, test and publish Mnemosyne and nothing else.</summary>
public class ReleaseFilesTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(AssemblyTests.RepoRoot(), relative));

    [Fact]
    public void The_changelog_has_one_latest_block_headed_by_the_project_version()
    {
        var changelog = Read("CHANGELOG.md");
        Assert.Single(Regex.Matches(changelog, "<!-- LATEST-START -->"));
        Assert.Single(Regex.Matches(changelog, "<!-- LATEST-END -->"));
        var start = changelog.IndexOf("<!-- LATEST-START -->", StringComparison.Ordinal);
        var end = changelog.IndexOf("<!-- LATEST-END -->", StringComparison.Ordinal);
        Assert.True(start < end, "LATEST-START must come before LATEST-END");
        var latest = changelog[start..end];
        var heading = latest.Split('\n').Select(line => line.TrimEnd('\r')).First(line => line.StartsWith("## ", StringComparison.Ordinal));
        var version = Regex.Match(Read("src/Mnemosyne/Mnemosyne.csproj"), "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Matches($"^## v{Regex.Escape(version)}( |$)", heading);
        Assert.Contains("\n- ", latest);
    }

    [Fact]
    public void The_licence_is_mit_under_roseofficial()
    {
        var licence = Read("LICENSE");
        Assert.StartsWith("MIT License", licence);
        Assert.Contains("Copyright (c) 2026 RoseOfficial", licence);
    }

    [Fact]
    public void The_workflows_build_test_and_publish_mnemosyne_alone()
    {
        foreach (var name in new[] { "ci.yml", "release.yml" })
        {
            var workflow = Read(Path.Combine(".github", "workflows", name));
            Assert.DoesNotContain("Komos", workflow, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dotnet test tests/Mnemosyne.Core.Tests/Mnemosyne.Core.Tests.csproj -c Release", workflow);
            Assert.Contains("dotnet test tests/Mnemosyne.Assembly.Tests/Mnemosyne.Assembly.Tests.csproj -c Release -p:DalamudLibPath=$HOME/dalamud/", workflow);
            // The solution holds a sheet tool that needs a local Dalamud folder, so no workflow builds or tests it.
            Assert.DoesNotContain("Mnemosyne.sln", workflow);
        }
        // The built-file tests run on a Linux runner, so their own project targets no Windows framework; only the plugin it builds does.
        Assert.DoesNotContain("-windows", Read(Path.Combine("tests", "Mnemosyne.Assembly.Tests", "Mnemosyne.Assembly.Tests.csproj")));
        var release = Read(Path.Combine(".github", "workflows", "release.yml"));
        Assert.Contains("PLUGIN_REPO: RoseOfficial/Mnemosyne", release);
        Assert.Contains("map(select(.InternalName != \"Mnemosyne\")) + $entry", release);
        Assert.Contains("/main/src/Mnemosyne/images/icon.png", release);
    }
}
