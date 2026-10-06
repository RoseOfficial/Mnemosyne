using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>The table's columns, in the order it draws them. The four holdings columns come after To do.</summary>
public enum MatrixColumn { Name, Role, Msq, Jobs, Events, Dailies, Weeklies, EndingSoon, LastSeen, Badge, Gil, Ventures, Bags, Seals }

/// <summary>One character as one line of the "All characters" view. Role is what the table's Role column shows:
/// the role's name, "unknown role" for an id this PC does not have, or "" for none. Four holdings, each
/// read from stored values: the character's own gil (null: blank) and its retainers' sum. The retainers with a venture timer stored, and how
/// many of those have ended (none stored: blank). The free bag slots (null: blank). The current company's seals over their cap (null:
/// blank; a cap of 0 when unknown). The four bags' size (0 with nothing stored), so bag space can be drawn as a bar
/// (PageLook.Bags).</summary>
public sealed record MatrixRow(
    ulong ContentId, string Label, string World, string MsqPosition,
    int JobsAtCap, int JobsUnlocked, int EventsDone, int EventsAvailable,
    int DailiesLeft, int WeekliesLeft, int EndingSoon,
    DateTimeOffset LastSeen, bool Stale, int Badge, string Role = "",
    long? Gil = null, long RetainerGil = 0, int VenturesReady = 0, int Ventures = 0, int? BagsFree = null, int? Seals = null, int SealCap = 0,
    int BagSlots = 0)
{
    /// <summary>Bag space is drawn red at this many free slots or fewer.</summary>
    public const int LowBagSpace = 5;

    /// <summary>"1.2M (+160k)", the bracket left out while the retainers hold no gil.</summary>
    public string GilText => Gil is { } gil ? Compact.Format(gil) + (RetainerGil > 0 ? $" (+{Compact.Format(RetainerGil)})" : "") : "";

    /// <summary>"n of m".</summary>
    public string VenturesText => Ventures > 0 ? $"{VenturesReady} of {Ventures}" : "";

    /// <summary>"n free".</summary>
    public string BagsText => BagsFree is { } free ? $"{free} free" : "";

    public bool BagsLow => BagsFree <= LowBagSpace;

    /// <summary>"50k / 50k"; the count alone, "0", when the cap went with a cleared row.</summary>
    public string SealsText => Seals is { } seals ? (SealCap > 0 ? $"{Compact.Format(seals)} / {Compact.Format(SealCap)}" : Compact.Format(seals)) : "";
}

public static class Summariser
{
    /// <summary>What the Role column and the sidebar's Role menu show for a record whose role id this PC does not
    /// have. RoleList owns the word, so no role can take it as a name.</summary>
    public const string UnknownRole = RoleList.UnknownName;

    public static MatrixRow Summarise(Catalog catalog, CharacterRecord record, Settings settings, DateTimeOffset now) =>
        Summarise(catalog, record, settings, now, Assembler.Assemble(catalog, record, settings, now));

    /// <summary>The same reduction over a checklist the caller already assembled, so a window can reuse its cache. An item off for
    /// the character's role is in no matrix count, as an ignored one is not. The dailies, weeklies, ending soon
    /// and badge come from the checklist, which never places one; the jobs and events counts read the catalog, so they skip it here. The MSQ
    /// position is a position, not a count, and still reads.</summary>
    public static MatrixRow Summarise(Catalog catalog, CharacterRecord record, Settings settings, DateTimeOffset now, Checklist checklist)
    {
        var role = settings.RoleOf(record);
        ItemDefinition? best = null;
        foreach (var item in catalog.InCategory(Category.Story))
        {
            if (!item.IsMsq || Assembler.IsIgnored(item, record, settings)) continue;
            if (!Assembler.Evaluate(item, record, now).IsDone) continue;
            if (best == null || item.GroupOrder > best.GroupOrder || (item.GroupOrder == best.GroupOrder && item.Order > best.Order)) best = item;
        }
        var msq = best == null ? "" : best.Group + ": " + best.Name;

        int jobsAtCap = 0, jobsUnlocked = 0;
        foreach (var item in catalog.InCategory(Category.Jobs))
        {
            if (item.Kind != ItemKind.Level || Assembler.IsIgnored(item, record, settings) || role?.IsOff(item) == true) continue;
            if (record.Values.GetValueOrDefault(item.Id) is not LevelValue level || level.Current <= 0) continue;
            jobsUnlocked++;
            if (level.Current >= level.Max) jobsAtCap++;
        }

        // Counts this calendar year's events. An occurrence is named for the year it runs in, but its window can
        // open in the year before (Heavensturn opens on 31 December), so select by overlap with the year, not by start year.
        // The undated sentinels (year 2000 and year 2100) fail the overlap test on their own and never count.
        int eventsDone = 0, eventsAvailable = 0;
        var year = now.ToUniversalTime().Year;
        var from = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var item in catalog.InCategory(Category.Events))
        {
            // A reward event is a Counter and counts like a Done occurrence; a held counter (the trove) is neither
            // done nor missable and is not an event to count.
            if (item.Kind is not (ItemKind.Done or ItemKind.Counter) || (item.Kind == ItemKind.Counter && !item.DoneAtCap)
                || item.Availability is not { } a || a.Start >= to || a.End <= from) continue;
            if (Assembler.IsIgnored(item, record, settings) || role?.IsOff(item) == true) continue;
            eventsAvailable++;
            if (Assembler.Evaluate(item, record, now).IsDone) eventsDone++;
        }

        // The four holdings columns, from stored values alone. Gil, bag space and the seals are holdings, which no
        // ignore, role or switch touches; ventures ready is a count.
        var (ventures, ready) = Ventures(catalog, record, settings, role, now);
        var seals = Holdings.CompanySeals(record);
        return new MatrixRow(record.ContentId, record.Label, record.HomeWorld, msq,
            jobsAtCap, jobsUnlocked, eventsDone, eventsAvailable,
            checklist.Today.Count, checklist.ThisWeek.Count, checklist.EndingSoon.Count,
            record.LastSeen, checklist.Stale, checklist.Badge,
            role?.Name ?? (string.IsNullOrEmpty(record.Role) ? "" : UnknownRole),
            Holdings.OwnGil(record), Holdings.RetainerGil(record) ?? 0, ready, ventures, Holdings.BagsFree(record), seals?.Seals, seals?.Cap ?? 0,
            Holdings.BagSlots(record));
    }

    /// <summary>The retainers with a venture timer stored, and how many of those have ended, a deadline of now
    /// included. A slot ignored on the character or everywhere, or off for its role, is in neither count, as in the jobs and events counts.
    /// So is every slot while the settings switch Resets off, as Dailies left, built from the checklist, is empty then. A slot the catalog does
    /// not carry is skipped.</summary>
    private static (int Ventures, int Ready) Ventures(Catalog catalog, CharacterRecord record, Settings settings, Role? role, DateTimeOffset now)
    {
        int ventures = 0, ready = 0;
        for (var slot = 0; slot < MoneyIds.RetainerSlots; slot++)
        {
            if (catalog.TryGet(MoneyIds.VentureId(slot)) is not { } item || !settings.IsCategoryOn(item.Category) || Assembler.IsIgnored(item, record, settings)
                || role?.IsOff(item) == true) continue;
            if (record.Values.GetValueOrDefault(item.Id) is not TimerValue timer) continue;
            ventures++;
            if (timer.Deadline <= now) ready++;
        }
        return (ventures, ready);
    }

    /// <summary>Every column sorts. The chosen column follows the chosen direction; ties always break by label ascending. A blank in
    /// one of the four holdings columns sorts last in both directions.</summary>
    public static IReadOnlyList<MatrixRow> Sort(IEnumerable<MatrixRow> rows, MatrixColumn column, bool descending)
    {
        var labels = StringComparer.OrdinalIgnoreCase;
        IOrderedEnumerable<MatrixRow> ordered = column switch
        {
            MatrixColumn.Name => Order(rows, r => r.Label, labels, descending),
            MatrixColumn.Role => Order(rows, r => r.Role, labels, descending),
            MatrixColumn.Msq => Order(rows, r => r.MsqPosition, labels, descending),
            MatrixColumn.Jobs => Then(Order(rows, r => r.JobsAtCap, null, descending), r => r.JobsUnlocked, descending),
            MatrixColumn.Events => Order(rows, r => r.EventsDone, null, descending),
            MatrixColumn.Dailies => Order(rows, r => r.DailiesLeft, null, descending),
            MatrixColumn.Weeklies => Order(rows, r => r.WeekliesLeft, null, descending),
            MatrixColumn.EndingSoon => Order(rows, r => r.EndingSoon, null, descending),
            MatrixColumn.LastSeen => Order(rows, r => r.LastSeen, null, descending),
            MatrixColumn.Badge => Order(rows, r => r.Badge, null, descending),
            // Gil by own plus retainers', ventures by the ready count, seals by the count; a blank
            // last in both directions. Bags by how full they are, the free slots negated, so
            // Descending (the table's default) lists the fullest bags first.
            MatrixColumn.Gil => BlanksLast(rows, r => r.Gil is { } gil ? gil + r.RetainerGil : null, descending),
            MatrixColumn.Ventures => BlanksLast(rows, r => r.Ventures > 0 ? r.VenturesReady : null, descending),
            MatrixColumn.Bags => BlanksLast(rows, r => r.BagsFree is { } free ? -(long)free : null, descending),
            MatrixColumn.Seals => BlanksLast(rows, r => r.Seals, descending),
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "unknown matrix column"),
        };
        return ordered.ThenBy(r => r.Label, labels).ToList();
    }

    private static IOrderedEnumerable<MatrixRow> Order<TKey>(IEnumerable<MatrixRow> rows, Func<MatrixRow, TKey> key, IComparer<TKey>? comparer, bool descending) =>
        descending ? rows.OrderByDescending(key, comparer) : rows.OrderBy(key, comparer);

    /// <summary>A blank, a null key, sorts last in both directions; the rest follow the chosen direction.</summary>
    private static IOrderedEnumerable<MatrixRow> BlanksLast(IEnumerable<MatrixRow> rows, Func<MatrixRow, long?> key, bool descending) =>
        Then(rows.OrderBy(r => key(r) is null), r => key(r) ?? 0, descending);

    private static IOrderedEnumerable<MatrixRow> Then<TKey>(IOrderedEnumerable<MatrixRow> rows, Func<MatrixRow, TKey> key, bool descending) =>
        descending ? rows.ThenByDescending(key) : rows.ThenBy(key);
}
