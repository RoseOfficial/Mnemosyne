using System.Globalization;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Checklist;

/// <summary>The four ranges the graph offers. The window opens on ThreeMonths.</summary>
public enum GilRange { OneMonth, ThreeMonths, OneYear, All }

/// <summary>One line of the graph, its first day and its amount on every day from there to the range's last day,
/// each day carrying the latest point on or before it. A line starts at the range's first day, or at its own first point when that
/// is later, since before it the character has no amount. Never empty.</summary>
public sealed record GilSeries(DateOnly First, IReadOnlyList<long> Amounts)
{
    public DateOnly Last => First.AddDays(Amounts.Count - 1);
    public long Min { get; } = Amounts.Min();
    public long Max { get; } = Amounts.Max();

    /// <summary>The amount on a day of the line, or null outside it.</summary>
    public long? On(DateOnly day) => day < First || day > Last ? null : Amounts[day.DayNumber - First.DayNumber];
}

/// <summary>One axis of the graph, set by the plugin rather than by ImPlot's own fitting and
/// formatting, so no formatter callback crosses into native code: its limits, and each tick's place and label.</summary>
public sealed record GilTicks(double Low, double High, double[] Values, string[] Labels);

/// <summary>The Gil over time graph for one range, from the visible characters' histories alone. From is the
/// range's first day and To today, both UTC days. Started is the first point of any visible character, or null with none. Total is the All gil
/// line, from Started or From, whichever is later: each day, every visible character with a point counted from the start, as GilPoints.TotalOn
/// counts it. Characters holds each visible character's own line by content id,
/// from its own first point, for a character with any amount in the range. A hidden character is in neither. Pure: every instant
/// is passed in.</summary>
public sealed record GilGraph(GilRange Range, DateOnly From, DateOnly To, DateOnly? Started, GilSeries? Total, IReadOnlyDictionary<ulong, GilSeries> Characters)
{
    /// <summary>The history holds fewer than two days, with no point at all or its first point today,
    /// so the section says so instead of drawing.</summary>
    public bool EarlyDays => Started is not { } started || started >= To;

    public static GilGraph Build(IEnumerable<CharacterRecord> records, GilRange range, DateTimeOffset now)
    {
        var today = GilPoints.DayOf(now);
        var histories = records.Where(r => !r.Hidden).Select(r => (r.ContentId, Points: GilPoints.Of(r))).Where(h => h.Points.Count > 0).ToList();
        DateOnly? started = histories.Count == 0 ? null : histories.Min(h => h.Points[0].Day);
        var from = Start(range, today, started);
        var lines = new Dictionary<ulong, GilSeries>();
        foreach (var (contentId, points) in histories)
        {
            if (Series(points, from, today) is { } series) lines[contentId] = series;
        }
        return new GilGraph(range, from, today, started, AllGil(histories.Select(h => h.Points).ToList(), started, from, today), lines);
    }

    /// <summary>A calendar month, three months or a year before today, or, for All, the first point
    /// anywhere, never later than today.</summary>
    public static DateOnly Start(GilRange range, DateOnly today, DateOnly? started) => range switch
    {
        GilRange.OneMonth => today.AddMonths(-1),
        GilRange.ThreeMonths => today.AddMonths(-3),
        GilRange.OneYear => today.AddYears(-1),
        _ => started is { } first && first < today ? first : today,
    };

    /// <summary>One character's line over from..to, or null when it has no amount on any day of it.</summary>
    internal static GilSeries? Series(IReadOnlyList<(DateOnly Day, long Amount)> points, DateOnly from, DateOnly to)
    {
        long? amount = null;
        var next = 0;
        while (next < points.Count && points[next].Day <= from) amount = points[next++].Amount;
        var first = from;
        if (amount is null)
        {
            if (next >= points.Count || points[next].Day > to) return null;
            first = points[next].Day;
        }
        var amounts = new List<long>(to.DayNumber - first.DayNumber + 1);
        for (var day = first; day <= to; day = day.AddDays(1))
        {
            while (next < points.Count && points[next].Day <= day) amount = points[next++].Amount;
            amounts.Add(amount.GetValueOrDefault());
        }
        return new GilSeries(first, amounts);
    }

    /// <summary>The All gil line, from the start of the total or the
    /// range's first day, whichever is later, to today. Each day every character counts its amount carried forward, and before its own first
    /// point the amount of that first point, so nine characters seeded at nine last-seen days read flat, not as gil gained on each seed's day.
    /// Null when no visible character has a point on or before today.</summary>
    private static GilSeries? AllGil(IReadOnlyList<IReadOnlyList<(DateOnly Day, long Amount)>> histories, DateOnly? started, DateOnly from, DateOnly to)
    {
        if (started is not { } start || start > to) return null;
        var first = start > from ? start : from;
        var amounts = new long[to.DayNumber - first.DayNumber + 1];
        foreach (var points in histories)
        {
            var amount = points[0].Amount;
            var next = 0;
            for (var k = 0; k < amounts.Length; k++)
            {
                var day = first.AddDays(k);
                while (next < points.Count && points[next].Day <= day) amount = points[next++].Amount;
                amounts[k] += amount;
            }
        }
        return new GilSeries(first, amounts);
    }

    /// <summary>"All gil: 45.2M, up 3.1M over 3 months". The amount on the line's last
    /// day less the amount on its first, "up", "down" or "no change", in the compact form. A line that starts after the range's first day, and
    /// every line of All, whose range starts at the first point anywhere, counts from its own first day and says "since" it.</summary>
    public string ChangeLine(string name, GilSeries series)
    {
        var last = series.Amounts[^1];
        var by = last - series.Amounts[0];
        var change = by > 0 ? "up " + Compact.Format(by) : by < 0 ? "down " + Compact.Format(-by) : "no change";
        var over = Range == GilRange.All || series.First > From ? "since " + DayWords(series.First) : Range switch
        {
            GilRange.OneMonth => "over 1 month",
            GilRange.ThreeMonths => "over 3 months",
            _ => "over 1 year",
        };
        return $"{name}: {Compact.Format(last)}, {change} {over}";
    }

    /// <summary>A day as the section and the hover say it: "2 Sep 2026".</summary>
    public static string DayWords(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The day axis. x is days from From, and day k's step runs from k to k + 1, so the axis ends one day after To.
    /// Five ticks spread over the range, every day while it is five days or fewer, each "d MMM", or "d MMM yy" when the range crosses a year.</summary>
    public GilTicks DayTicks()
    {
        var days = To.DayNumber - From.DayNumber;
        var offsets = days <= 4 ? Enumerable.Range(0, days + 1).ToArray() : Enumerable.Range(0, 5).Select(k => (int)Math.Round(k * days / 4.0)).ToArray();
        var format = From.Year == To.Year ? "d MMM" : "d MMM yy";
        return new GilTicks(0, days + 1, offsets.Select(o => (double)o).ToArray(),
            offsets.Select(o => From.AddDays(o).ToString(format, CultureInfo.InvariantCulture)).ToArray());
    }

    /// <summary>The amount axis over the lowest and highest amount drawn, in steps of 1, 2 or 5 times a power of ten, about
    /// four of them, each labelled in the Totals page's compact form. Amounts closer together than a room of
    /// a fifth of the highest, and at least 1,000, get that room each side, never below 0 for an amount that is not, so a flat line has an axis
    /// with height and a vendor purchase on 45M is not drawn as a cliff the axis's full height. Should two compact labels still read the same,
    /// every label is the exact amount instead, grouped by thousands.</summary>
    public static GilTicks AmountTicks(long min, long max)
    {
        var room = Math.Max(1_000, Math.Abs(max) / 5);
        if (max - min < room)
        {
            min = min >= 0 ? Math.Max(0, min - room) : min - room;
            max += room;
        }
        var step = NiceStep((max - min) / 4);
        var low = FloorTo(min, step);
        var high = CeilTo(max, step);
        var ticks = new List<long>();
        for (var tick = low; tick <= high; tick += step) ticks.Add(tick);
        var labels = ticks.Select(Compact.Format).ToArray();
        if (labels.Distinct(StringComparer.Ordinal).Count() < labels.Length)
            labels = ticks.Select(t => t.ToString("N0", CultureInfo.InvariantCulture)).ToArray();
        return new GilTicks(low, high, ticks.Select(t => (double)t).ToArray(), labels);
    }

    /// <summary>The day a place on the day axis falls on: the day whose step it is on, kept inside the range.</summary>
    public DateOnly DayAt(double x) => From.AddDays(Math.Clamp((int)Math.Floor(x), 0, To.DayNumber - From.DayNumber));

    /// <summary>1, 2 or 5 times a power of ten, the first at or above raw; 1 at the least.</summary>
    internal static long NiceStep(long raw)
    {
        if (raw <= 1) return 1;
        var magnitude = 1L;
        while (magnitude <= raw / 10) magnitude *= 10;
        return raw <= magnitude ? magnitude : raw <= 2 * magnitude ? 2 * magnitude : raw <= 5 * magnitude ? 5 * magnitude : 10 * magnitude;
    }

    private static long FloorTo(long value, long step) => value >= 0 ? value / step * step : -CeilTo(-value, step);

    private static long CeilTo(long value, long step) => value >= 0 ? (value + step - 1) / step * step : -FloorTo(-value, step);
}

/// <summary>The key Plugin.GilGraphFor keeps its last graph on, as TotalsCacheKey keeps the
/// Totals lines. Equal keys mean nothing that could move the graph has moved: the same tracker Version (every record change bumps it, a hide
/// and an import included), the same range, and the same UTC day. Pure: it reads nothing but what it is given.</summary>
public sealed record GilGraphCacheKey(int TrackerVersion, GilRange Range, DateOnly Day)
{
    public static GilGraphCacheKey For(int trackerVersion, GilRange range, DateTimeOffset now) => new(trackerVersion, range, GilPoints.DayOf(now));
}
