using System.Diagnostics;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;
using ClientAchievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;
using UIState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState;

namespace Mnemosyne.Interop.Readers;

/// <summary>The Collections rows behind a list the client loads only on request: one Counter per
/// achievement category, done over its named achievements, and the titles, owned over the named titles.
///
/// Achievements are read only while IUnlockState.IsAchievementListLoaded and the titles only while IsTitleListLoaded, never
/// requested. Each list is trusted only once this reader has seen it not loaded for the same character this session
/// (ListLoadGuard), so a list loaded before the plugin reloaded, or brought over from the previous character, is never
/// credited to the new one. Until a list is trusted its rows say nothing and keep what the last visit stored.
///
/// The reader runs at login, on the tick and on the Achievements, Character and title windows' PostShow; the window fire is what
/// lets the guard see a list just requested. Except at login it reads in full only when a list's trusted answer or its set bits
/// moved: the pair costs 4,486 checks, and a completion flips a bit the count sees within one tick.
///
/// The raw list states are logged per character whenever they change, and the title count beside the achievement-derived one
/// once both lists are trusted. Nothing here writes or requests anything: Achievement's request,
/// receive and set members, TitleList.RequestTitleList and SetTitleUnlocked, TitleController.SendTitleIdUpdate and
/// AgentAchievement.OpenById are never called.</summary>
public sealed class AchievementReader(Catalog catalog, CatalogRows rows, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    private readonly IReadOnlyList<(ItemDefinition Item, AchievementCategoryRow Category)> _categories = Categories(catalog, rows);
    private readonly (ItemDefinition Item, CollectionKindRow Kind)? _titles = Titles(catalog, rows);
    // Each list is trusted only once seen not loaded for the same character this session.
    private readonly ListLoadGuard _achievementGuard = new(), _titleGuard = new();
    private ListPrint? _lastPrint;
    // The characters the log lines were last written for, so a switch writes the new character's evidence again.
    private ulong _stateOwner, _comparedFor, _costFor;
    private string? _stateReported;

    public string Name => "AchievementReader";
    public Category Category => Category.Collections;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.Interval | ReaderTrigger.Window;

    /// <summary>A read that says nothing of its trigger is a full one.</summary>
    public IReadOnlyList<Reading>? Read() => Read(ReaderTrigger.Login);

    public IReadOnlyList<Reading>? Read(ReaderTrigger fired)
    {
        // Nothing is read while the player state is not loaded.
        if (!player.IsLoaded) return null;
        if (ListSnapshot() is not { } lists) return null;
        var owner = player.ContentId;
        var achievements = _achievementGuard.Trust(owner, unlocks.IsAchievementListLoaded);
        var titles = _titleGuard.Trust(owner, unlocks.IsTitleListLoaded);
        ReportLists(owner, lists, achievements, titles);
        var print = new ListPrint(owner, achievements, titles, lists.AchievementsDone, lists.TitlesOwned);
        if (!CollectionRules.ReadNow(fired, ReaderTrigger.Interval | ReaderTrigger.Window, _lastPrint, print)) return [];
        _lastPrint = print;
        var watch = Stopwatch.StartNew();
        var now = clock.UtcNow;
        var readings = new List<Reading>(_categories.Count + 1);
        if (achievements)
        {
            var sheet = data.GetExcelSheet<Achievement>();
            foreach (var (item, category) in _categories)
                readings.Add(new Reading(item.Id, new CounterValue(CollectionCounts.Owned(sheet, category.AchievementIds, unlocks.IsAchievementComplete), category.AchievementIds.Count, now)));
        }
        if (titles && _titles is { } title)
        {
            var owned = CollectionCounts.Owned(data.GetExcelSheet<Title>(), title.Kind.Ids, unlocks.IsTitleUnlocked);
            readings.Add(new Reading(title.Item.Id, new CounterValue(owned, title.Kind.Ids.Count, now)));
            if (achievements) CompareTitles(owner, owned, title.Kind.Ids);
        }
        if (achievements && _costFor != owner)
        {
            _costFor = owner;
            log($"achievements: {_categories.Sum(c => c.Category.AchievementIds.Count)} achievements in {_categories.Count} categories{(titles ? " and the titles" : "")} read in {watch.Elapsed.TotalMicroseconds:0} us");
        }
        return readings;
    }

    private static List<(ItemDefinition, AchievementCategoryRow)> Categories(Catalog catalog, CatalogRows rows)
    {
        var byId = rows.AchievementCategories.ToDictionary(c => CollectionIds.AchievementId(c.RowId), StringComparer.Ordinal);
        return catalog.InCategory(Category.Collections).Where(i => byId.ContainsKey(i.Id)).Select(i => (i, byId[i.Id])).ToList();
    }

    private static (ItemDefinition, CollectionKindRow)? Titles(Catalog catalog, CatalogRows rows) =>
        catalog.TryGet(CollectionIds.KindId(CollectionIds.Title)) is { } item && rows.Collections.FirstOrDefault(k => k.Kind == CollectionIds.Title) is { } kind
            ? (item, kind)
            : null;

    /// <summary>Logs the two lists' raw states and the guard's answers whenever they change for this character, showing whether a
    /// list survives a zone change, a relog or a character switch, and which window loads the titles.</summary>
    private void ReportLists(ulong owner, Lists lists, bool achievements, bool titles)
    {
        if (owner != _stateOwner)
        {
            _stateOwner = owner;
            _stateReported = null;
        }
        var said = $"achievement list {lists.Achievements} (trusted {achievements}); title list requested {lists.Requested}, pending {lists.Pending}, received {lists.Received} (trusted {titles})";
        if (said == _stateReported) return;
        _stateReported = said;
        log($"achievements: {said}");
    }

    /// <summary>Once per character with both lists trusted: the title count beside the titles a completed achievement grants. This
    /// comparison is needed before a title may be counted from its achievement instead of its own list.</summary>
    private void CompareTitles(ulong owner, int owned, IReadOnlyList<uint> titleIds)
    {
        if (_comparedFor == owner) return;
        _comparedFor = owner;
        var counted = titleIds.ToHashSet();
        var granted = new HashSet<uint>();
        foreach (var achievement in data.GetExcelSheet<Achievement>())
        {
            if (achievement.Title.RowId != 0 && counted.Contains(achievement.Title.RowId) && unlocks.IsAchievementComplete(achievement)) granted.Add(achievement.Title.RowId);
        }
        log($"titles: {owned} of {titleIds.Count} owned by the title list; {granted.Count} granted by a completed achievement");
    }

    private readonly record struct Lists(string Achievements, bool Requested, bool Pending, bool Received, int AchievementsDone, int TitlesOwned);

    private readonly record struct ListPrint(ulong Owner, bool Achievements, bool Titles, int AchievementsDone, int TitlesOwned);

    /// <summary>The achievement list's state and the title list's three handshake flags, for the log, and the set
    /// bits of both completion arrays, the fingerprint a tick compares. FFXIVClientStructs' Achievement (static Achievement*
    /// Instance(), AchievementState State, Span&lt;byte&gt; CompletedAchievements); UIState (static UIState* Instance(), TitleList
    /// TitleList); TitleList (bool DataPending, DataReceived, DataRequested, Span&lt;byte&gt; UnlockedTitles). RequestCompletedAchievements
    /// and RequestTitleList are never called.</summary>
    private static unsafe Lists? ListSnapshot()
    {
        var achievement = ClientAchievement.Instance();
        var ui = UIState.Instance();
        if (achievement == null || ui == null) return null;
        return new Lists(achievement->State.ToString(), ui->TitleList.DataRequested, ui->TitleList.DataPending, ui->TitleList.DataReceived,
            CollectionCounts.Population(achievement->CompletedAchievements), CollectionCounts.Population(ui->TitleList.UnlockedTitles));
    }
}
