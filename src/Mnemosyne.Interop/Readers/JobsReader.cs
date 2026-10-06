using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Time;

namespace Mnemosyne.Interop.Readers;

/// <summary>One level per experience index, Blue Mage spells, the Grand Company rank per company, and the current Grand
/// Company (gc:current), which the All characters table's Company seals column reads. The company seals are read in
/// Money: all three are read there, one per company.
/// A limited job's cap comes from the hand file's limitedJobCaps; the global cap is still checked once against the
/// client's own value.</summary>
public sealed class JobsReader(Catalog catalog, CatalogRows rows, CatalogExtras extras, IPlayerState player, IUnlockState unlocks, IDataManager data, IClock clock, Action<string> log) : IProgressReader
{
    private readonly Dictionary<int, ClassJobRow> _jobByIndex = rows.ClassJobs
        .Where(j => j.ExpArrayIndex >= 0)
        .GroupBy(j => j.ExpArrayIndex)
        .ToDictionary(g => g.Key, g => g.Where(j => j.Parent != j.RowId).OrderBy(j => j.RowId).FirstOrDefault() ?? g.OrderBy(j => j.RowId).First());
    private readonly Dictionary<uint, GrandCompanyRow> _companies = rows.GrandCompanies.ToDictionary(c => c.RowId);
    private bool _capChecked;
    private readonly HashSet<uint> _capWarned = [];

    public string Name => "JobsReader";
    public Category Category => Category.Jobs;
    public ReaderTrigger Triggers => ReaderTrigger.Login | ReaderTrigger.LevelOrJob;

    public IReadOnlyList<Reading>? Read()
    {
        // Every reader checks that the player state is loaded before reading anything. Every IUnlockState
        // member returns false rather than failing while it is not, so a read then would store zeros, and a
        // null return says the category was not read at all, so its stamp does not move.
        if (!player.IsLoaded) return null;
        var jobs = data.GetExcelSheet<ClassJob>();
        var companies = data.GetExcelSheet<GrandCompany>();
        var aoz = data.GetExcelSheet<AozAction>();
        var growth = data.GetExcelSheet<ParamGrow>();
        var now = clock.UtcNow;
        CheckCap();
        var readings = new List<Reading>();
        foreach (var item in catalog.InCategory(Category))
        {
            var parts = item.Id.Split(':');
            switch (parts[0])
            {
                case "job":
                    // Every id is parsed with TryParse: a malformed id skips its own item, where a throw disables this
                    // reader for the session (the category shows as unavailable meanwhile).
                    if (parts.Length > 1 && int.TryParse(parts[1], out var index) && _jobByIndex.TryGetValue(index, out var job) && jobs.GetRowOrDefault(job.RowId) is { } row)
                    {
                        var cap = extras.CapFor(job.RowId);
                        var level = player.GetClassJobLevel(row);
                        // Experience earned over experience needed to leave this level. ParamGrow's row id is the
                        // level. ExpToNext stays non-zero even at the shipped cap, so the "> 0" test only
                        // guards a level with no growth row; the assembler's own Current < Max check is what hides the
                        // second line at a cap.
                        var needed = level > 0 && growth.GetRowOrDefault((uint)level) is { } step && step.ExpToNext > 0 ? step.ExpToNext : (int?)null;
                        var earned = needed == null ? null : (int?)player.GetClassJobExperience(row);
                        WarnIfAboveCap(job, level, cap);
                        readings.Add(new Reading(item.Id, new LevelValue(level, cap, earned, needed)));
                    }
                    break;
                case "blu":
                {
                    var learned = 0;
                    foreach (var action in aoz)
                    {
                        if (action.RowId == 0) continue;
                        if (unlocks.IsAozActionUnlocked(action)) learned++;
                    }
                    readings.Add(new Reading(item.Id, new CounterValue(learned, rows.AozActionCount, now)));
                    break;
                }
                case "gc" when item.Id.StartsWith("gc:rank:", StringComparison.Ordinal):
                    if (parts.Length > 2 && uint.TryParse(parts[2], out var companyId) && _companies.TryGetValue(companyId, out var company) && companies.GetRowOrDefault(company.RowId) is { } companyRow)
                        readings.Add(new Reading(item.Id, new LevelValue(player.GetGrandCompanyRank(companyRow), company.RankCount)));
                    break;
                case "gc" when item.Id == CatalogBuilder.CurrentCompanyId:
                {
                    // The current Grand Company, row 0 for a character in none, read behind the loaded guard above; the value's shape is
                    // Readings.CurrentCompany's. Dalamud's own IPlayerState: RowRef<GrandCompany> GrandCompany { get; }; PlayerState.GrandCompany:
                    // the default row while not IsLoaded, else LuminaUtils.CreateRef<GrandCompany> over
                    // FFXIVClientStructs' PlayerState.Instance()->GrandCompany, the byte at offset 720.
                    // A non-zero row the sheets do not carry is unreadable, so it sends no reading at
                    // all, as the gc:rank arm above does when its own lookup fails; only row 0, a real "no company", clears the stored value.
                    var companyRowId = player.GrandCompany.RowId;
                    if (companyRowId == 0) readings.Add(Readings.CurrentCompany(item, null));
                    else if (_companies.TryGetValue(companyRowId, out var currentCompany)) readings.Add(Readings.CurrentCompany(item, currentCompany));
                    break;
                }
            }
        }
        return readings;
    }

    /// <summary>A limited job whose level is above its hand-file cap proves the cap has gone stale — the one self-check
    /// available, since nothing in the client or the sheets states a per-job maximum. Said once per job.</summary>
    private void WarnIfAboveCap(ClassJobRow job, int level, int cap)
    {
        if (level <= cap || !_capWarned.Add(job.RowId)) return;
        log($"{job.Abbreviation} is level {level}, above the hand file's cap of {cap}; its bar is wrong until the cap is corrected");
    }

    private void CheckCap()
    {
        if (_capChecked) return;
        unsafe
        {
            var state = PlayerState.Instance();
            if (state == null) return; // leave _capChecked false so the next read tries again
            _capChecked = true;
            var clientCap = state->MaxLevel;
            if (clientCap != extras.LevelCap) log($"the client says the level cap is {clientCap}; the hand file says {extras.LevelCap}; job bars use the hand file until it is corrected");
        }
    }
}
