using System.Globalization;
using System.Text.RegularExpressions;

namespace Mnemosyne.Core.Storage;

/// <summary>The /mnemo dump files: their names, the chat line that says one was written, and which older ones to remove. Each dump holds the
/// current record and every visible character's name and content id, so only the newest few are kept.</summary>
public static partial class Dumps
{
    /// <summary>How many dump files the configuration folder keeps; each new dump removes the older ones beyond this.</summary>
    public const int Kept = 5;

    /// <summary>"dump-&lt;content id&gt;-&lt;yyyyMMdd-HHmmss&gt;.txt", stamped to the second in UTC. Formatted with the invariant culture: a
    /// Windows culture with another calendar would otherwise write another year.</summary>
    public static string FileName(ulong contentId, DateTimeOffset now) =>
        "dump-" + contentId.ToString(CultureInfo.InvariantCulture) + "-" + now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt";

    /// <summary>The chat line after a dump: where it went, and what it holds.</summary>
    public static string Written(string fileName) =>
        $"dump written: {fileName} in the plugin configuration folder. It holds your characters' names and content ids; share it only with someone you trust.";

    /// <summary>The dump files to remove so that only the newest <paramref name="kept"/> remain, newest first. Only names this plugin writes
    /// count, and newest means the stamp in the name, never the file's own time.</summary>
    public static IReadOnlyList<string> Older(IEnumerable<string> fileNames, int kept = Kept) =>
        fileNames.Select(name => (Name: name, Match: DumpName().Match(name)))
            .Where(d => d.Match.Success)
            .OrderByDescending(d => d.Match.Groups["stamp"].Value, StringComparer.Ordinal)
            .ThenByDescending(d => d.Name, StringComparer.Ordinal)
            .Skip(kept)
            .Select(d => d.Name)
            .ToList();

    [GeneratedRegex(@"^dump-\d+-(?<stamp>\d{8}-\d{6})\.txt$", RegexOptions.CultureInvariant)]
    private static partial Regex DumpName();
}
