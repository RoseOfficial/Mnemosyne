using System.Globalization;

namespace Mnemosyne.Core.Time;

/// <summary>Every hour below is a server-fixed UTC value: daily 15:00, weekly Tuesday 08:00, Grand Company 20:00,
/// Cosmic Exploration 09:00, levequest grants at 00:00 and 12:00. A period key names the period's
/// start date, so "daily:2026-09-13" is the period from 2026-09-13 15:00 UTC to the next day's 15:00 UTC.</summary>
public static class ResetSchedules
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string GrandCompany = "grand-company";
    public const string Cosmic = "cosmic";
    public const string Leve = "leve";
    /// <summary>The Jumbo Cactpot drawing refills the ticket allowance, and it falls at a different instant per data-centre
    /// group. The North American draw is Saturday evening Pacific, which is Sunday in UTC.</summary>
    public const string JumboNorthAmerica = "jumbo-na";
    public const string JumboEurope = "jumbo-eu";
    public const string JumboJapan = "jumbo-jp";
    public const string JumboOceania = "jumbo-oce";
    /// <summary>The Jumbo Cactpot's schedule name on a catalog item. It stands for whichever draw the record's own data centre
    /// uses, because a catalog is global and the draw is not: Resolve turns it
    /// into one of the four draws above, or into nothing for a data centre the table does not know, which then has no period
    /// at all rather than a guessed one. It is never a schedule itself, so Get rejects it.</summary>
    public const string Jumbo = "jumbo";

    private sealed record Schedule(int[] Hours, DayOfWeek? Day);

    private static readonly Dictionary<string, Schedule> Table = new(StringComparer.Ordinal)
    {
        [Daily] = new([15], null),
        [Weekly] = new([8], DayOfWeek.Tuesday),
        [GrandCompany] = new([20], null),
        [Cosmic] = new([9], null),
        [Leve] = new([0, 12], null),
        [JumboNorthAmerica] = new([2], DayOfWeek.Sunday),
        [JumboEurope] = new([19], DayOfWeek.Saturday),
        [JumboJapan] = new([12], DayOfWeek.Saturday),
        [JumboOceania] = new([9], DayOfWeek.Saturday),
    };

    /// <summary>Which data centres draw on which schedule. A data centre this table does not name has no known drawing time
    /// and shows no draw timer at all, rather than a guess.</summary>
    private static readonly Dictionary<string, string> JumboByDataCentre = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aether"] = JumboNorthAmerica, ["Crystal"] = JumboNorthAmerica, ["Dynamis"] = JumboNorthAmerica, ["Primal"] = JumboNorthAmerica,
        ["Chaos"] = JumboEurope, ["Light"] = JumboEurope,
        ["Elemental"] = JumboJapan, ["Gaia"] = JumboJapan, ["Mana"] = JumboJapan, ["Meteor"] = JumboJapan,
        ["Materia"] = JumboOceania,
    };

    public static string? JumboDraw(string dataCentre) => JumboByDataCentre.GetValueOrDefault(dataCentre);

    /// <summary>The schedule an item's periods are keyed on for one record: the per-record sentinel resolved by data centre,
    /// every other name itself. Null is "no known period".</summary>
    public static string? Resolve(string schedule, string dataCentre) => schedule == Jumbo ? JumboDraw(dataCentre) : schedule;

    /// <summary>For a schedule with more than one boundary a day the key also carries the boundary hour, "leve:2026-09-13T12".</summary>
    public static string PeriodKey(string schedule, DateTimeOffset now)
    {
        var s = Get(schedule);
        var start = PeriodStart(s, now);
        var key = schedule + ":" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return s.Hours.Length > 1 ? key + "T" + start.ToString("HH", CultureInfo.InvariantCulture) : key;
    }

    /// <summary>The most recent boundary at or before now.</summary>
    public static DateTimeOffset PeriodStart(string schedule, DateTimeOffset now) => PeriodStart(Get(schedule), now);

    private static DateTimeOffset PeriodStart(Schedule s, DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var best = DateTimeOffset.MinValue;
        for (var daysBack = 0; daysBack <= 7; daysBack++)
        {
            var date = utc.Date.AddDays(-daysBack);
            if (s.Day is { } day && date.DayOfWeek != day) continue;
            foreach (var hour in s.Hours)
            {
                var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);
                if (candidate <= utc && candidate > best) best = candidate;
            }
            if (best != DateTimeOffset.MinValue) break;
        }
        return best;
    }

    /// <summary>The first boundary strictly after now.</summary>
    public static DateTimeOffset NextReset(string schedule, DateTimeOffset now)
    {
        var s = Get(schedule);
        var utc = now.ToUniversalTime();
        for (var daysAhead = 0; daysAhead <= 7; daysAhead++)
        {
            var date = utc.Date.AddDays(daysAhead);
            if (s.Day is { } day && date.DayOfWeek != day) continue;
            foreach (var hour in s.Hours.OrderBy(h => h))
            {
                var candidate = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);
                if (candidate > utc) return candidate;
            }
        }
        throw new InvalidOperationException($"no boundary within a week for '{schedule}'");
    }

    public static bool BoundaryBetween(string schedule, DateTimeOffset from, DateTimeOffset to) =>
        PeriodKey(schedule, from) != PeriodKey(schedule, to);

    /// <summary>How many boundaries lie in (from, to]. Used for accrual.</summary>
    public static int GrantsBetween(string schedule, DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) return 0;
        // A default (unset) read instant must not stall a frame walking a boundary at a time; clamp the lookback to a year.
        if (from < to.AddDays(-366)) from = to.AddDays(-366);
        var count = 0;
        var t = NextReset(schedule, from);
        while (t <= to) { count++; t = NextReset(schedule, t); }
        return count;
    }

    private static Schedule Get(string schedule) =>
        Table.TryGetValue(schedule, out var s) ? s : throw new ArgumentException($"unknown reset schedule '{schedule}'");
}
