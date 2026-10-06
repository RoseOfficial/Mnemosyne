using System.Globalization;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>Why a holder may be out of date, so the page's words match
/// the cause: past the staleness threshold, or its line read at a window or place visit, however recently the character was seen
/// ("as of last visit").</summary>
public enum StaleReason { Threshold, Visit }

/// <summary>One character's share of a line, and why it may be out of date, or null while it is current.</summary>
public sealed record TotalHolder(CharacterRecord Record, long Amount, StaleReason? Stale);

/// <summary>One line of the Totals page: its sum over the visible characters, and every character that holds more than zero,
/// largest first. Id is the currency's item id, or one of the three gil lines' own.</summary>
public sealed record TotalLine(string Id, string Name, long Total, IReadOnlyList<TotalHolder> Holders);

/// <summary>What a record holds, read from its stored values alone. The Totals page and the All characters
/// table both read it, so the two never disagree. Every amount is a 64-bit number: fourteen characters' gil passes int.MaxValue.</summary>
public static class Holdings
{
    /// <summary>A held Counter's count, or null when nothing is stored.</summary>
    public static long? Held(CharacterRecord record, string id) => record.Values.GetValueOrDefault(id) is CounterValue counter ? counter.Current : null;

    /// <summary>The character's own gil, currency:1.</summary>
    public static long? OwnGil(CharacterRecord record) => Held(record, MoneyIds.CurrencyId(MoneyIds.Gil));

    /// <summary>The character's retainers' gil: every retainer:&lt;slot&gt;:gil row added up, or null when none is stored. A dismissed
    /// retainer's row is summed as stored until the roster clears it.</summary>
    public static long? RetainerGil(CharacterRecord record)
    {
        long? sum = null;
        foreach (var id in MoneyIds.RetainerGilIds)
        {
            if (Held(record, id) is { } gil) sum = (sum ?? 0) + gil;
        }
        return sum;
    }

    /// <summary>The free slots in the four bags. storage:bags stores used over their size.</summary>
    public static int? BagsFree(CharacterRecord record) =>
        record.Values.GetValueOrDefault(MoneyIds.Bags) is CounterValue bags ? Math.Max(0, bags.Cap - bags.Current) : null;

    /// <summary>The four bags' size, the cap storage:bags is stored over, so bag space can be drawn as a bar; 0
    /// with nothing stored.</summary>
    public static int BagSlots(CharacterRecord record) =>
        record.Values.GetValueOrDefault(MoneyIds.Bags) is CounterValue bags ? bags.Cap : 0;

    /// <summary>The current Grand Company's row, 1 to 3, as
    /// Readings.CurrentCompany stores it in gc:current. Null when nothing is stored, as on a record not logged into since this build, or when the
    /// stored row names no company.</summary>
    public static uint? CurrentCompany(CharacterRecord record) =>
        record.Values.GetValueOrDefault(CatalogBuilder.CurrentCompanyId) is DoneValue { PeriodKey: { } key }
        && uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var row) && MoneyIds.SealItem(row) != null ? row : null;

    /// <summary>The Company seals column: the current company's seal row, its count over its
    /// stored cap. A former company keeps its rank, so its row is stored over a real cap too, and is never the column. A read zero clears the
    /// row, its cap with it, so none of the company's seals is (0, 0) once Money has been read: a count with no cap. Null, a blank, with no
    /// company known, or Money not read yet.</summary>
    public static (int Seals, int Cap)? CompanySeals(CharacterRecord record)
    {
        if (CurrentCompany(record) is not { } company || MoneyIds.SealItem(company) is not { } seal) return null;
        if (record.Values.GetValueOrDefault(MoneyIds.CurrencyId(seal)) is CounterValue row) return (row.Current, row.Cap);
        return record.CategoryReadAt.ContainsKey(nameof(Category.Money)) ? (0, 0) : null;
    }
}

/// <summary>Each Money currency summed over the visible characters, from their stored values alone.
/// Three gil lines, always listed: gil on characters, gil on retainers, and both. Then one line per other currency row, in the Money category's
/// own order, then MGP, each listed only while some visible character holds more than zero. A cap is per character, so no
/// line sums one. A hidden character is left out. Roles, the ignores and the category switches change nothing: the totals report holdings,
/// which are information, not tasks.</summary>
public static class Totals
{
    public const string CharacterGilId = "gil:characters", RetainerGilId = "gil:retainers", AllGilId = "gil:all";
    public const string CharacterGilName = "Gil on characters", RetainerGilName = "Gil on retainers", AllGilName = "All gil";

    public static IReadOnlyList<TotalLine> Build(IEnumerable<CharacterRecord> records, Catalog catalog, Settings settings, DateTimeOffset now)
    {
        var visible = records.Where(r => !r.Hidden).ToList();
        var lines = new List<TotalLine>
        {
            Line(CharacterGilId, CharacterGilName, visible, Holdings.OwnGil, false, settings, now),
            Line(RetainerGilId, RetainerGilName, visible, Holdings.RetainerGil, false, settings, now),
            Line(AllGilId, AllGilName, visible, r => (Holdings.OwnGil(r) ?? 0) + (Holdings.RetainerGil(r) ?? 0), false, settings, now),
        };
        var gil = MoneyIds.CurrencyId(MoneyIds.Gil);
        var currencies = catalog.InCategory(Category.Money).Where(i => i.Id.StartsWith(MoneyIds.CurrencyPrefix, StringComparison.Ordinal) && i.Id != gil).ToList();
        // MGP, a currency row Content owns, after the Money category's own.
        if (catalog.TryGet(MoneyIds.CurrencyId(CatalogBuilder.MgpItemId)) is { Category: not Category.Money } mgp) currencies.Add(mgp);
        foreach (var item in currencies)
        {
            // A value read at a window visit may be out of date however recently the character was seen ("as of last visit").
            var windowed = item.Freshness.Kind is FreshnessKind.NeedsWindow or FreshnessKind.NeedsPlace;
            var line = Line(item.Id, item.Name, visible, r => Holdings.Held(r, item.Id), windowed, settings, now);
            if (line.Holders.Count > 0) lines.Add(line);
        }
        return lines;
    }

    /// <summary>One line: every character holding more than zero, largest first, ties by label and then content id, each marked when stale.
    /// The zero holders are filtered out before a TotalHolder is allocated for them.</summary>
    private static TotalLine Line(string id, string name, IReadOnlyList<CharacterRecord> records, Func<CharacterRecord, long?> amount, bool windowed,
        Settings settings, DateTimeOffset now)
    {
        var holders = records
            .Select(r => (Record: r, Amount: amount(r) ?? 0))
            .Where(h => h.Amount > 0)
            .Select(h => new TotalHolder(h.Record, h.Amount, windowed ? StaleReason.Visit : Assembler.IsStale(h.Record, settings, now) ? StaleReason.Threshold : null))
            .OrderByDescending(h => h.Amount).ThenBy(h => h.Record.Label, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.Record.ContentId)
            .ToList();
        return new TotalLine(id, name, holders.Sum(h => h.Amount), holders);
    }
}

/// <summary>The key Plugin.TotalsFor caches its last-built lines on, the way BoardCacheKey keeps the
/// All tasks board. Equal keys mean nothing that could move a line has moved: the same tracker Version (every record change bumps it), the same
/// settings Version, and the same UTC minute (which also covers a holder going stale). Pure: it reads nothing but what it is given.</summary>
public sealed record TotalsCacheKey(int TrackerVersion, int SettingsVersion, long Minute)
{
    public static TotalsCacheKey For(int trackerVersion, int settingsVersion, DateTimeOffset now) =>
        new(trackerVersion, settingsVersion, now.ToUnixTimeSeconds() / 60);
}
