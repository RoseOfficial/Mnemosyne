using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Readers;

/// <summary>A reward occurrence counts the rewards the character owns over the ones the hand
/// file lists. Every kind but achievements is in memory at login; achievements are read only while the client reports the
/// list loaded, which happens after the player opens the Achievements window, and never requested. A title
/// would need the Titles list loaded the same way, which is why title is not a <see cref="RewardKind"/>. Runs on
/// the interval tick so the first visit of a session is picked up within one live-read period.
/// Shares the Events category with the quest reader; a throw disables only this reader for the session, and the category
/// shows as unavailable while either of its readers is disabled. It trusts the list only once it has been seen
/// not loaded for the same character this session, and also reads on a window's PostShow, so the guard can see the
/// list the Achievements window has just requested.</summary>
public sealed class RewardReader(Catalog catalog, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    // The catalog and the hand file both only change with a rebuild, so the item ids and their occurrences are paired
    // once here: the interval tick walks this map instead of splitting every Events id every few seconds.
    private readonly Dictionary<string, EventOccurrence> _rewarded = Rewarded(catalog, extras);
    private readonly HashSet<RewardSignal> _reported = [];
    // The achievement list is trusted only once seen not loaded for this character this session, so a list loaded before
    // the plugin reloaded, or brought over from the previous character, is never counted for the new one, as AchievementReader does.
    private readonly ListLoadGuard _achievementGuard = new();

    public string Name => "RewardReader";
    public Category Category => Category.Events;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Unlock | ReaderTrigger.Interval | ReaderTrigger.Window;

    public IReadOnlyList<Reading>? Read()
    {
        var readings = new List<Reading>();
        // Every reader checks that the player state is loaded before reading anything. Every IUnlockState
        // member returns false rather than failing while it is not, so a read then would store zeros over
        // counts the plugin already has, and the gated occurrence's zero would stand until the next Achievements visit.
        // A null return says the category was not read at all, so its stamp does not move.
        if (!player.IsLoaded) return null;
        if (_rewarded.Count == 0) return readings;
        var achievementsLoaded = _achievementGuard.Trust(player.ContentId, unlocks.IsAchievementListLoaded);
        var sheets = Sheets.Resolve(data);
        var now = clock.UtcNow;
        foreach (var (id, occurrence) in _rewarded)
        {
            // A set with an achievement is read whole or not at all, so the count never mixes read and unread kinds.
            if (occurrence.HasAchievement && !achievementsLoaded) continue;
            var owned = 0;
            foreach (var reward in occurrence.RewardList)
            {
                if (Owned(reward, sheets)) owned++;
            }
            readings.Add(new Reading(id, new CounterValue(owned, occurrence.RewardList.Count, now)));
        }
        return readings;
    }

    /// <summary>The reward occurrences by item id. A Done occurrence belongs to QuestReader; the two readers split the
    /// Events items by kind, and an occurrence with no reward list has nothing for this reader to count.</summary>
    private static Dictionary<string, EventOccurrence> Rewarded(Catalog catalog, CatalogExtras extras)
    {
        var byRun = extras.EventOccurrences
            .Where(o => o.RewardList.Count > 0)
            .ToDictionary(o => (o.Genre, o.Festival));
        var byItem = new Dictionary<string, EventOccurrence>(StringComparer.Ordinal);
        foreach (var item in catalog.InCategory(Category.Events))
        {
            if (item.Kind != ItemKind.Counter) continue;
            var parts = item.Id.Split(':');
            if (parts.Length != 3 || parts[0] != "event" || !uint.TryParse(parts[1], out var genre) || !uint.TryParse(parts[2], out var festival)) continue;
            if (byRun.TryGetValue((genre, festival), out var occurrence)) byItem[item.Id] = occurrence;
        }
        return byItem;
    }

    private bool Owned(RewardSignal reward, in Sheets sheets)
    {
        var resolved = reward.Kind switch
        {
            RewardKind.Achievement => Check(sheets.Achievements.GetRowOrDefault(reward.Id), unlocks.IsAchievementComplete),
            RewardKind.Mount => Check(sheets.Mounts.GetRowOrDefault(reward.Id), unlocks.IsMountUnlocked),
            RewardKind.Minion => Check(sheets.Minions.GetRowOrDefault(reward.Id), unlocks.IsCompanionUnlocked),
            RewardKind.Emote => Check(sheets.Emotes.GetRowOrDefault(reward.Id), unlocks.IsEmoteUnlocked),
            RewardKind.Orchestrion => Check(sheets.Orchestrion.GetRowOrDefault(reward.Id), unlocks.IsOrchestrionUnlocked),
            RewardKind.Card => Check(sheets.Cards.GetRowOrDefault(reward.Id), unlocks.IsTripleTriadCardUnlocked),
            RewardKind.Item => Check(sheets.Items.GetRowOrDefault(reward.Id), unlocks.IsItemUnlocked),
            _ => Missing(reward),
        };
        if (resolved is { } owned) return owned;
        // A row that does not resolve is a hand-file error against this game version: say so once, count it as not owned.
        if (_reported.Add(reward)) log($"reward {reward.Kind.ToString().ToLowerInvariant()} {reward.Id} has no row in this game version; counted as not owned");
        return false;
    }

    /// <summary>A kind with no switch arm above is a defect in this reader, not a hand-file error against the row: say so
    /// distinctly (never the unresolved-row message below) and count it as not owned.</summary>
    private bool? Missing(RewardSignal reward)
    {
        if (_reported.Add(reward)) log($"reward kind {reward.Kind} has no reader; counted as not owned");
        return false;
    }

    private static bool? Check<T>(T? row, Func<T, bool> unlocked) where T : struct => row is { } r ? unlocked(r) : null;

    /// <summary>The seven sheets a reward kind can resolve against, fetched once per read rather than once per reward,
    /// the way the quest reader hoists its sheets to locals.</summary>
    private readonly record struct Sheets(
        ExcelSheet<Achievement> Achievements,
        ExcelSheet<Mount> Mounts,
        ExcelSheet<Companion> Minions,
        ExcelSheet<Emote> Emotes,
        ExcelSheet<Orchestrion> Orchestrion,
        ExcelSheet<TripleTriadCard> Cards,
        ExcelSheet<Item> Items)
    {
        public static Sheets Resolve(IDataManager data) => new(
            data.GetExcelSheet<Achievement>(),
            data.GetExcelSheet<Mount>(),
            data.GetExcelSheet<Companion>(),
            data.GetExcelSheet<Emote>(),
            data.GetExcelSheet<Orchestrion>(),
            data.GetExcelSheet<TripleTriadCard>(),
            data.GetExcelSheet<Item>());
    }
}
