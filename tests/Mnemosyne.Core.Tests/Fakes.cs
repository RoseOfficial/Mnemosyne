using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

internal sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start;
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A small catalog that touches every kind, reset rule, and freshness. Sunday 2026-09-13 12:00 UTC is "Now" throughout.</summary>
internal static class Items
{
    public static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    public static readonly ItemDefinition Roulette = new("reset:roulette:1", Category.Resets, "Daily", "Duty Roulette: Leveling", ItemKind.Done, ResetRule.Daily, 1, Freshness.AtLogin);
    public static readonly ItemDefinition Allowances = new("reset:tribal", Category.Resets, "Daily", "Tribal allowances", ItemKind.Counter, ResetRule.Daily, 2, Freshness.AtLogin);
    public static readonly ItemDefinition Stickers = new("reset:tails", Category.Resets, "Weekly", "Wondrous Tails stickers", ItemKind.Counter, ResetRule.Weekly, 1, Freshness.AtLogin);
    public static readonly ItemDefinition Leves = new("reset:leves", Category.Resets, "Daily", "Levequest allowances", ItemKind.Counter, ResetRule.Accrual(ResetSchedules.Leve, 3), 3, Freshness.AtLogin);
    public static readonly ItemDefinition Map = new("reset:map", Category.Resets, "Daily", "Treasure map", ItemKind.Timer, ResetRule.Accrual(ResetSchedules.Leve, 0), 4, Freshness.AtLogin);
    public static readonly ItemDefinition Cactpot = new("reset:cactpot", Category.Resets, "Daily", "Mini cactpot tickets", ItemKind.Counter, ResetRule.Daily, 5, Freshness.Observed);
    public static readonly ItemDefinition Venture = new("timer:venture:1", Category.Money, "Retainers", "Venture: Retainer 1", ItemKind.Timer, ResetRule.None, 1, Freshness.AtLogin);
    public static readonly ItemDefinition Paladin = new("job:1", Category.Jobs, "Tanks", "Paladin", ItemKind.Level, ResetRule.None, 1, Freshness.AtLogin);
    public static readonly ItemDefinition Msq1 = new("quest:1", Category.Story, "MSQ: Dawntrail", "The First Step", ItemKind.Done, ResetRule.None, 1, Freshness.AtLogin, IsMsq: true);
    public static readonly ItemDefinition Msq2 = new("quest:2", Category.Story, "MSQ: Dawntrail", "The Second Step", ItemKind.Done, ResetRule.None, 2, Freshness.AtLogin, IsMsq: true);
    public static readonly ItemDefinition Moonfire = new("event:moonfire:2026", Category.Events, "Moonfire Faire", "Moonfire Faire 2026", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
        new Availability(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero)));
    /// <summary>A reward event: a counter of rewards, gated on the Achievements window, running from 4 August to 5 October 2026.</summary>
    public static readonly ItemDefinition Yokai = new("event:252:39", Category.Events, "Collaboration Quests", "Yo-kai Watch collaboration 2026", ItemKind.Counter, ResetRule.Window, 39, Freshness.Window("Achievements"),
        new Availability(new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 5, 14, 59, 0, TimeSpan.Zero)), ReusedQuest: true);
    public static readonly ItemDefinition Rising = new("event:rising:2026", Category.Events, "The Rising", "The Rising 2026", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
        new Availability(new DateTimeOffset(2026, 8, 27, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero)));
    public static readonly ItemDefinition Starlight = new("event:starlight:2026", Category.Events, "Starlight Celebration", "Starlight 2026", ItemKind.Done, ResetRule.Window, 1, Freshness.AtLogin,
        new Availability(new DateTimeOffset(2026, 12, 15, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 1, 8, 0, 0, TimeSpan.Zero)));
    public static readonly ItemDefinition Achievement = new("achievement:1", Category.Collections, "Achievements", "Mapping the Realm", ItemKind.Done, ResetRule.None, 1, Freshness.Window("Achievements"));
    public static readonly ItemDefinition Eureka = new("level:eureka", Category.Content, "Field operations", "Eureka elemental level", ItemKind.Level, ResetRule.None, 1, Freshness.Place("Eureka"));

    public static Catalog Catalog() => new([Roulette, Allowances, Stickers, Leves, Map, Cactpot, Venture, Paladin, Msq1, Msq2, Moonfire, Rising, Starlight, Achievement, Eureka]);

    /// <summary>A record whose every category has been read at Now, so a missing value means "not done", never "not read yet".</summary>
    public static CharacterRecord Record(ulong id = 1, string name = "Aria Whitewind")
    {
        var r = new CharacterRecord { ContentId = id, Name = name, HomeWorld = "Balmung", DataCenter = "Crystal", FirstSeen = Now, LastSeen = Now };
        foreach (var c in Enum.GetValues<Category>()) r.CategoryReadAt[c.ToString()] = Now;
        return r;
    }
}

internal sealed class InMemoryFileSystem : Mnemosyne.Core.Storage.IFileSystem
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Unreadable { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Writes { get; private set; }
    public bool FailWrites { get; set; }
    /// <summary>What a failed write says, as the system's own message does, often with the path in it.</summary>
    public string WriteFailure { get; set; } = "disk full";
    /// <summary>The folder cannot be listed, as when the system refuses access to it.</summary>
    public bool FailList { get; set; }
    /// <summary>No file can be moved, as when another program holds it open.</summary>
    public bool FailMoves { get; set; }

    public bool FileExists(string path) => Files.ContainsKey(path);
    public string ReadAllText(string path) => Unreadable.Contains(path) ? throw new IOException("unreadable: " + path) : Files[path];
    public void WriteAllText(string path, string text)
    {
        if (FailWrites) throw new IOException(WriteFailure);
        Files[path] = text;
        Writes++;
    }
    public void Move(string from, string to)
    {
        if (FailMoves) throw new IOException("in use: " + from);
        Files[to] = Files[from];
        Files.Remove(from);
    }
    public void Delete(string path) => Files.Remove(path);
    public IReadOnlyList<string> ListFiles(string directory, string pattern)
    {
        if (FailList) throw new UnauthorizedAccessException("access denied: " + directory);
        var suffix = pattern.TrimStart('*');
        return Files.Keys
            .Where(p => string.Equals(Path.GetDirectoryName(p), directory, StringComparison.OrdinalIgnoreCase) && p.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
    public void CreateDirectory(string directory) => Directories.Add(directory);
}

internal sealed class FakeIdentity : Mnemosyne.Core.Readers.ICharacterIdentity
{
    public bool IsLoaded { get; set; } = true;
    public ulong ContentId { get; set; } = 1;
    public string Name { get; set; } = "Aria Whitewind";
    public string HomeWorld { get; set; } = "Balmung";
    public string DataCenter { get; set; } = "Crystal";
}

internal sealed class FakeReader(Category category, Mnemosyne.Core.Readers.ReaderTrigger triggers) : Mnemosyne.Core.Readers.IProgressReader
{
    public string Name { get; set; } = "fake";
    public Category Category { get; } = category;
    public Mnemosyne.Core.Readers.ReaderTrigger Triggers { get; } = triggers;
    public List<Mnemosyne.Core.Readers.Reading> Next { get; } = new();
    public int Calls { get; private set; }
    public bool Throw { get; set; }
    public bool ReturnsNull { get; set; }
    public Func<DateTimeOffset>? Stamp { get; set; }

    /// <summary>What each Tracker call said fired, in order: the Tracker tells a reader which of its triggers fired.</summary>
    public List<Mnemosyne.Core.Readers.ReaderTrigger> Fired { get; } = new();

    public IReadOnlyList<Mnemosyne.Core.Readers.Reading>? Read(Mnemosyne.Core.Readers.ReaderTrigger fired)
    {
        Fired.Add(fired);
        return Read();
    }

    public IReadOnlyList<Mnemosyne.Core.Readers.Reading>? Read()
    {
        Calls++;
        if (Throw) throw new InvalidOperationException("layout changed");
        if (ReturnsNull) return null;
        if (Stamp == null) return Next.ToList();
        return Next.Select(r => r.Value is CounterValue counter ? r with { Value = counter with { ReadAt = Stamp() } } : r).ToList();
    }
}

/// <summary>The checked-in rows dumped from the game; the Core tests build the real catalog from them.</summary>
internal sealed class FixtureCatalogSource : Mnemosyne.Core.Definitions.ICatalogSource
{
    public static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog-rows.json");

    public Mnemosyne.Core.Definitions.CatalogRows Load() =>
        System.Text.Json.JsonSerializer.Deserialize<Mnemosyne.Core.Definitions.CatalogRows>(File.ReadAllText(Path), Mnemosyne.Core.Storage.Json.Options)
        ?? throw new InvalidOperationException("the fixture is empty");
}

/// <summary>A My task draft as the settings page makes one, and Items' catalog with a settings' My tasks part added, as MyTaskCatalog
/// builds it over the sheet-built catalog.</summary>
internal static class Tasks
{
    public static MyTask Draft(string name, string kind, int? hours = null, string forWhom = MyTask.ForAll, params ulong[] picked) =>
        new() { Name = name, Kind = kind, Hours = hours, For = forWhom, Picked = [.. picked] };

    /// <summary>A counted task's draft, for every character.</summary>
    public static MyTask Counted(string name, string kind, int target) => new() { Name = name, Kind = kind, Target = target, For = MyTask.ForAll };

    public static Catalog Catalog(Settings settings) => new([.. Items.Catalog().Items, .. settings.MyTasks.Items()]);
}
