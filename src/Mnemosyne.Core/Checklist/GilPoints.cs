using System.Globalization;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>The rules of a record's gil history. A point is one UTC calendar day, keyed "yyyy-MM-dd"
/// in the invariant culture, and the character's own gil plus its retainers' gil that day, added as the Totals page adds them (Holdings), so
/// moving gil to a retainer never reads as a loss. A day with no point carries the latest point before it, and a record has no amount before
/// its first point, though the total counts it from the start. A key this build cannot read, from a hand edit, is skipped, never a throw.
/// Every amount is a 64-bit number. Pure: every instant is passed in.</summary>
public static class GilPoints
{
    public const string KeyFormat = "yyyy-MM-dd";

    /// <summary>The key of a UTC calendar day.</summary>
    public static string Key(DateOnly day) => day.ToString(KeyFormat, CultureInfo.InvariantCulture);

    /// <summary>The UTC calendar day an instant falls on, whatever its offset, never the PC's local date, so a date means the
    /// same on any PC.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);

    /// <summary>A key this build can read, or false for anything else a hand edit left.</summary>
    public static bool TryDay(string key, out DateOnly day) =>
        DateOnly.TryParseExact(key, KeyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>Own gil plus every stored retainer gil row, the Totals page's "All gil" for one character. Null with no
    /// own gil stored, so a record Money has never read has no amount, even with a retainer row.</summary>
    public static long? Amount(CharacterRecord record) =>
        Holdings.OwnGil(record) is { } own ? own + (Holdings.RetainerGil(record) ?? 0) : null;

    /// <summary>Today's point set to the record's amount now, when it differs from today's point or today
    /// has none, so a day's point is always the last amount seen that day and the first write after midnight UTC starts the next day's. Returns
    /// whether it wrote. The Tracker calls it after a read that changed the record, in the same change, so it is saved with it.</summary>
    public static bool Note(CharacterRecord record, DateTimeOffset now)
    {
        if (Amount(record) is not { } amount) return false;
        var key = Key(DayOf(now));
        if (record.GilHistory is { } history && history.TryGetValue(key, out var held) && held == amount) return false;
        (record.GilHistory ??= new Dictionary<string, long>(StringComparer.Ordinal))[key] = amount;
        return true;
    }

    /// <summary>A record with own gil stored and no history gets one point, that amount, dated its last-seen day, so every
    /// character is on the graph from the first day. A record with any point it can read is never seeded, so a seed never overwrites one (a
    /// history holding only keys this build cannot read counts as none, and they are kept). A record with no last-seen instant
    /// has no day to date it on. Returns whether it seeded.</summary>
    public static bool Seed(CharacterRecord record)
    {
        if (Of(record).Count > 0 || record.LastSeen == default || Amount(record) is not { } amount) return false;
        (record.GilHistory ??= new Dictionary<string, long>(StringComparer.Ordinal))[Key(DayOf(record.LastSeen))] = amount;
        return true;
    }

    /// <summary>The points a record holds, oldest first, each key read as its day; a key this build cannot read is skipped, and
    /// the file's own order does not matter.</summary>
    public static IReadOnlyList<(DateOnly Day, long Amount)> Of(CharacterRecord record)
    {
        if (record.GilHistory is not { Count: > 0 } history) return [];
        var points = new List<(DateOnly Day, long Amount)>(history.Count);
        foreach (var (key, amount) in history)
        {
            if (TryDay(key, out var day)) points.Add((day, amount));
        }
        points.Sort((a, b) => a.Day.CompareTo(b.Day));
        return points;
    }

    /// <summary>The record's amount on a day, its latest point on or before it, or null before its first point.
    /// A point after the day (a PC whose clock ran ahead) is not read before its own day.</summary>
    public static long? On(CharacterRecord record, DateOnly day) => Carried(Of(record), day);

    /// <summary>The total on a day, the start of the
    /// total or later, every visible character with a point added up. A character counts its amount carried forward, and on a day before its own
    /// first point the amount of that first point, so a seed dated at an old last-seen day, or a character first seen later, never reads as gain.
    /// A hidden character is left out. Null before the start of the total: the first day any visible character has a point.</summary>
    public static long? TotalOn(IEnumerable<CharacterRecord> records, DateOnly day)
    {
        var total = 0L;
        var begun = false;
        foreach (var record in records)
        {
            if (record.Hidden) continue;
            var points = Of(record);
            if (points.Count == 0) continue;
            begun |= points[0].Day <= day;
            total += Carried(points, day) ?? points[0].Amount;
        }
        return begun ? total : null;
    }

    /// <summary>The latest of these points, oldest first, on or before the day, or null before the first.</summary>
    private static long? Carried(IReadOnlyList<(DateOnly Day, long Amount)> points, DateOnly day)
    {
        long? amount = null;
        foreach (var point in points)
        {
            if (point.Day > day) break;
            amount = point.Amount;
        }
        return amount;
    }
}
