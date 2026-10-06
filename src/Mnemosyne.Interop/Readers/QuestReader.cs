using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Readers;

/// <summary>Quest completion, feature unlocks, aether currents and allied society ranks for Story, and
/// occurrence completion for Events. One instance per category. Everything here is a read of a bit array or a rank field
/// already in memory at login; the unlock event marks the category dirty.</summary>
public sealed class QuestReader(Category category, Catalog catalog, CatalogRows rows, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock) : IProgressReader
{
    private readonly Dictionary<uint, AetherCurrentZoneRow> _zones = rows.AetherCurrentZones.ToDictionary(z => z.CompFlgSetId);
    private readonly Dictionary<uint, BeastTribeRow> _tribes = rows.BeastTribes.ToDictionary(t => t.RowId);
    // CatalogBuilder.EventOccurrences is the one place that defines which quests form an occurrence; this
    // reader only looks the grouping up, it never restates it.
    private readonly Dictionary<(uint Genre, uint Festival), IReadOnlyList<uint>> _occurrences = CatalogBuilder.EventOccurrences(rows)
        .ToDictionary(o => (o.Genre, o.Festival), o => o.QuestIds);

    public string Name => $"QuestReader({Category})";
    public Category Category { get; } = category;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Unlock;

    /// <summary>Every id is parsed with TryParse: a malformed id skips its own item, where a throw disables this reader for
    /// the session (the category shows as unavailable meanwhile). The aether members are FFXIVClientStructs' own.</summary>
    public IReadOnlyList<Reading>? Read()
    {
        // Every reader checks that the player state is loaded before reading anything. Every IUnlockState
        // member returns false rather than failing while it is not, so a read then would store zeros, and a
        // null return says the category was not read at all, so its stamp does not move.
        if (!player.IsLoaded) return null;
        var quests = data.GetExcelSheet<Quest>();
        var currents = data.GetExcelSheet<AetherCurrent>();
        var flagSets = data.GetExcelSheet<AetherCurrentCompFlgSet>();
        var now = clock.UtcNow;
        var readings = new List<Reading>();
        foreach (var item in catalog.InCategory(Category))
        {
            var parts = item.Id.Split(':');
            switch (parts[0])
            {
                case "quest" or "unlock":
                    if (Number(parts, 1, out var questId) && quests.GetRowOrDefault(questId) is { } quest)
                        readings.Add(new Reading(item.Id, new DoneValue(unlocks.IsQuestCompleted(quest))));
                    break;
                case "aether":
                    if (Number(parts, 1, out var flagSetId) && _zones.TryGetValue(flagSetId, out var zone))
                    {
                        // The client's own complete flag is the truth for done; the attuned count is display
                        // only and is held one below the total until the flag is set, so the two can never disagree on done.
                        var complete = flagSets.GetRowOrDefault(flagSetId) is { } flagSet && unlocks.IsAetherCurrentCompFlgSetUnlocked(flagSet);
                        var attuned = zone.AetherCurrentIds.Count(id => currents.GetRowOrDefault(id) is { } current && unlocks.IsAetherCurrentUnlocked(current));
                        var total = zone.AetherCurrentIds.Count;
                        readings.Add(new Reading(item.Id, new CounterValue(complete ? total : Math.Min(attuned, Math.Max(0, total - 1)), total, now)));
                    }
                    break;
                case "tribe":
                    if (Number(parts, 1, out var tribeId) && _tribes.TryGetValue(tribeId, out var tribe))
                    {
                        if (tribe.RowId > byte.MaxValue) break;             // a row above 255 would wrap onto another society's reputation
                        if (BeastTribeRank((byte)tribe.RowId) is { } rank) // beast tribe rows are far below 256; the client indexes reputation by this byte
                            readings.Add(new Reading(item.Id, new LevelValue(rank.Rank, tribe.MaxRank, rank.Reputation, rank.Needed)));
                    }
                    break;
                case "event":
                    // A reward occurrence is a Counter owned by RewardReader; this reader only reads Done occurrences.
                    if (item.Kind != ItemKind.Done) break;
                    if (Number(parts, 1, out var genre) && Number(parts, 2, out var festival) && _occurrences.TryGetValue((genre, festival), out var questIds))
                    {
                        var done = questIds.All(id => quests.GetRowOrDefault(id) is { } q && unlocks.IsQuestCompleted(q));
                        readings.Add(new Reading(item.Id, new DoneValue(done)));
                    }
                    break;
            }
        }
        return readings;
    }

    /// <summary>One numeric part of an item id, or false when the part is absent or not a number.</summary>
    private static bool Number(string[] parts, int index, out uint value)
    {
        value = 0;
        return index < parts.Length && uint.TryParse(parts[index], out value);
    }

    /// <summary>The character's rank with one allied society, and the reputation earned toward the next rank over the
    /// reputation it takes, or null when the client's player state is not available. Members verified against the
    /// installed FFXIVClientStructs.dll.</summary>
    private static unsafe (int Rank, int? Reputation, int? Needed)? BeastTribeRank(byte tribeId)
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        var needed = (int)state->GetBeastTribeNeededReputation(tribeId);
        return (state->GetBeastTribeRank(tribeId), needed > 0 ? state->GetBeastTribeCurrentReputation(tribeId) : null, needed > 0 ? needed : null);
    }
}
