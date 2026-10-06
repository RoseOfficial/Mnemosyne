using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Records;

namespace Mnemosyne.Core.Tests;

/// <summary>The Gil over time graph's lines, its ranges, the change over a range, the early days, its two axes and its cache key. The
/// rules of one record's points are GilPointsTests'.</summary>
public class GilGraphTests
{
    private static DateOnly Day(int year, int month, int day) => new(year, month, day);

    /// <summary>Items.Now's UTC day, Sunday 13 September 2026.</summary>
    private static readonly DateOnly Today = Day(2026, 9, 13);

    /// <summary>Nine characters with 5M each and gil never touched, last seen 80, 70, ... 10 and 0 days before Items.Now, each seeded at
    /// its last-seen day as a load seeds it, content ids 1 to 9. GilHistoryTextTests reuses it.</summary>
    internal static List<CharacterRecord> NineSeeded()
    {
        string[] names = ["Aria Whitewind", "Kestrel Vane", "Momo Lulu"];
        var records = new List<CharacterRecord>();
        for (var i = 0; i < 9; i++)
        {
            var record = Items.Record((ulong)(i + 1), names[i % 3]);
            record.LastSeen = Items.Now - TimeSpan.FromDays(80 - 10 * i);
            TotalsTests.Gil(record, 5_000_000);
            GilPoints.Seed(record);
            records.Add(record);
        }
        return records;
    }

    [Fact]
    public void Seeds_on_nine_last_seen_days_never_read_as_gain_under_any_range()
    {
        // The total counts every character from its start at its first amount, so it is flat at 45M under every range: no seed's day
        // adds a character to the total on the day it was seeded, since it already counted from the beginning. Each character's own
        // line still starts at its own first point.
        var records = NineSeeded();

        Assert.Equal("All gil: 45M, no change over 1 month", Change(GilRange.OneMonth));
        Assert.Equal("All gil: 45M, no change since 25 Jun 2026", Change(GilRange.ThreeMonths));
        Assert.Equal("All gil: 45M, no change since 25 Jun 2026", Change(GilRange.OneYear));
        Assert.Equal("All gil: 45M, no change since 25 Jun 2026", Change(GilRange.All));
        var all = GilGraph.Build(records, GilRange.All, Items.Now);
        Assert.All(all.Total!.Amounts, amount => Assert.Equal(45_000_000L, amount));
        Assert.Equal("Kestrel Vane: 5M, no change since 3 Sep 2026", all.ChangeLine("Kestrel Vane", all.Characters[8]));

        string Change(GilRange range)
        {
            var graph = GilGraph.Build(records, range, Items.Now);
            return graph.ChangeLine(Totals.AllGilName, graph.Total!);
        }
    }

    [Fact]
    public void A_character_first_seen_later_counts_its_first_amount_back_to_the_start_of_the_total()
    {
        // Momo, a new alt, is first seen on the 10th with 1,000 gil and has 51,000 two days later. The total counts his 1,000 from its
        // start, so only the 50,000 he made reads as gain; a new alt's starting gil is small, so it barely shows. His own line starts
        // at his first point and says since when; his own amount has none before it.
        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-06-01", 10_000_000));
        var momo = GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2026-09-10", 1_000), ("2026-09-12", 51_000));

        var graph = GilGraph.Build([aria, momo], GilRange.ThreeMonths, Items.Now);

        Assert.Equal((Day(2026, 6, 13), 10_001_000L), (graph.Total!.First, graph.Total.Amounts[0]));
        Assert.Equal("All gil: 10M, up 50k over 3 months", graph.ChangeLine(Totals.AllGilName, graph.Total));
        Assert.Equal("Momo Lulu: 51k, up 50k since 10 Sep 2026", graph.ChangeLine("Momo Lulu", graph.Characters[3]));
        Assert.Equal(10_001_000L, GilPoints.TotalOn([aria, momo], Day(2026, 6, 1)));
        Assert.Null(GilPoints.On(momo, Day(2026, 9, 9)));
    }

    [Fact]
    public void Each_range_starts_a_month_three_months_or_a_year_before_today_and_all_at_the_first_point_anywhere()
    {
        // Calendar months and a calendar year back from today, the shorter month's last day where the day does not exist; All from the
        // first point of any visible character, which a hidden one's earlier point does not move, and today alone with no point at all.
        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-02-01", 1_000));
        var momo = GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2025-01-01", 5_000));
        momo.Hidden = true;

        Assert.Equal(Day(2026, 8, 13), GilGraph.Build([aria, momo], GilRange.OneMonth, Items.Now).From);
        Assert.Equal(Day(2026, 6, 13), GilGraph.Build([aria, momo], GilRange.ThreeMonths, Items.Now).From);
        Assert.Equal(Day(2025, 9, 13), GilGraph.Build([aria, momo], GilRange.OneYear, Items.Now).From);
        var all = GilGraph.Build([aria, momo], GilRange.All, Items.Now);
        Assert.Equal((Day(2026, 2, 1), Today, Day(2026, 2, 1)), (all.From, all.To, all.Started!.Value));
        Assert.Equal(Today, GilGraph.Build([Items.Record()], GilRange.All, Items.Now).From);
        Assert.Equal(Day(2026, 2, 28), GilGraph.Start(GilRange.OneMonth, Day(2026, 3, 31), null));
        Assert.Equal(Day(2023, 2, 28), GilGraph.Start(GilRange.OneYear, Day(2024, 2, 29), null));
    }

    [Fact]
    public void A_line_runs_from_the_ranges_first_day_or_its_own_first_point_carried_forward_to_today()
    {
        // Over 1 month, 13 August to 13 September, Aria's line starts on the range's first day with the point Aria had before it and
        // steps where a point changed it. Kestrel's starts at his first point, the 10th, and has no amount before it. Momo, with no
        // point, has no line.
        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-07-01", 1_000_000), ("2026-09-01", 1_200_000), ("2026-09-12", 1_150_000));
        var kestrel = GilPointsTests.WithPoints(Items.Record(2, "Kestrel Vane"), ("2026-09-10", 8_500));
        var momo = Items.Record(3, "Momo Lulu");

        var graph = GilGraph.Build([aria, kestrel, momo], GilRange.OneMonth, Items.Now);

        var line = graph.Characters[1];
        Assert.Equal((Day(2026, 8, 13), Today, 32), (line.First, line.Last, line.Amounts.Count));
        Assert.Equal([1_000_000L, 1_200_000L, 1_200_000L, 1_150_000L, 1_150_000L],
            new[] { Day(2026, 8, 31), Day(2026, 9, 1), Day(2026, 9, 11), Day(2026, 9, 12), Today }.Select(d => line.On(d)!.Value));
        Assert.Equal((1_000_000L, 1_200_000L), (line.Min, line.Max));
        Assert.Equal((Day(2026, 9, 10), 4), (graph.Characters[2].First, graph.Characters[2].Amounts.Count));
        Assert.Null(graph.Characters[2].On(Day(2026, 9, 9)));
        Assert.False(graph.Characters.ContainsKey(3));
    }

    [Fact]
    public void The_all_gil_line_adds_the_visible_characters_day_by_day_and_a_hidden_one_comes_back_whole_when_unhidden()
    {
        // The total starts on the first day any visible character has a point, not at 0 before it, and counts Kestrel from there at his
        // first amount, though his own line starts at his own first point. Momo, hidden, is in neither the total nor the lines; unhidden,
        // his whole line is back, the point from while he was hidden included.
        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-09-08", 1_000_000), ("2026-09-12", 1_100_000));
        var kestrel = GilPointsTests.WithPoints(Items.Record(2, "Kestrel Vane"), ("2026-09-10", 8_500));
        var momo = GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2026-09-01", 5_000_000), ("2026-09-11", 5_500_000));
        momo.Hidden = true;

        var graph = GilGraph.Build([aria, kestrel, momo], GilRange.OneMonth, Items.Now);

        Assert.Equal(Day(2026, 9, 8), graph.Total!.First);
        Assert.Equal([1_008_500L, 1_008_500L, 1_008_500L, 1_008_500L, 1_108_500L, 1_108_500L], graph.Total.Amounts);
        Assert.Equal(Day(2026, 9, 10), graph.Characters[2].First);
        Assert.False(graph.Characters.ContainsKey(3));

        momo.Hidden = false;
        var unhidden = GilGraph.Build([aria, kestrel, momo], GilRange.OneMonth, Items.Now);
        Assert.Equal((Day(2026, 9, 1), 13), (unhidden.Characters[3].First, unhidden.Characters[3].Amounts.Count));
        Assert.Equal(5_500_000L, unhidden.Characters[3].On(Day(2026, 9, 11)));
        Assert.Equal(6_608_500L, unhidden.Total!.On(Today));
        Assert.Equal(GilPoints.TotalOn([aria, kestrel, momo], Day(2026, 9, 11)), unhidden.Total.On(Day(2026, 9, 11)));
    }

    [Fact]
    public void The_change_reads_up_down_or_no_change_over_the_range_or_since_a_lines_first_day()
    {
        // The amount on the range's last day less the amount on its first, in the compact form, the decimal cut. A character's line with
        // no amount on the range's first day counts from its first point and says "since" it; so does every line of All, whose range
        // starts at the first point anywhere. The total counts every character from the start: over 3 months it is Aria's 3.2M gained
        // less Kestrel's 500 lost, and under All it is the same, never the 45M the characters hold read as gain.
        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-06-01", 42_000_000), ("2026-09-12", 45_200_000));
        var kestrel = GilPointsTests.WithPoints(Items.Record(2, "Kestrel Vane"), ("2026-09-10", 9_000), ("2026-09-12", 8_500));
        var momo = GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2026-05-01", 700_000));

        var graph = GilGraph.Build([aria, kestrel, momo], GilRange.ThreeMonths, Items.Now);

        Assert.Equal("All gil: 45.9M, up 3.1M over 3 months", graph.ChangeLine(Totals.AllGilName, graph.Total!));
        Assert.Equal("Aria Whitewind: 45.2M, up 3.2M over 3 months", graph.ChangeLine("Aria Whitewind", graph.Characters[1]));
        Assert.Equal("Kestrel Vane: 8,500, down 500 since 10 Sep 2026", graph.ChangeLine("Kestrel Vane", graph.Characters[2]));
        Assert.Equal("Momo Lulu: 700k, no change over 3 months", graph.ChangeLine("Momo Lulu", graph.Characters[3]));

        var all = GilGraph.Build([aria, kestrel, momo], GilRange.All, Items.Now);
        Assert.Equal("All gil: 45.9M, up 3.1M since 1 May 2026", all.ChangeLine(Totals.AllGilName, all.Total!));
        Assert.Equal("Aria Whitewind: 45.2M, up 3.2M since 1 Jun 2026", all.ChangeLine("Aria Whitewind", all.Characters[1]));

        var month = GilGraph.Build([momo], GilRange.OneMonth, Items.Now);
        Assert.Equal("Momo Lulu: 700k, no change over 1 month", month.ChangeLine("Momo Lulu", month.Characters[3]));
        var year = GilGraph.Build([GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2025-01-01", 1_000))], GilRange.OneYear, Items.Now);
        Assert.Equal("Momo Lulu: 1,000, no change over 1 year", year.ChangeLine("Momo Lulu", year.Characters[3]));
    }

    [Fact]
    public void The_graph_waits_for_a_second_day_of_history()
    {
        // With no point at all, or with the first point today, the history holds fewer than two days and the section says so instead of
        // drawing. A single point yesterday is two days, so it draws: the rule counts days since the first point, not points. A hidden
        // character's older point does not count.
        var none = GilGraph.Build([Items.Record()], GilRange.ThreeMonths, Items.Now);
        Assert.True(none.EarlyDays);
        Assert.Null(none.Started);

        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-09-13", 1_000));
        var momo = GilPointsTests.WithPoints(Items.Record(3, "Momo Lulu"), ("2026-09-01", 5_000));
        momo.Hidden = true;
        var first = GilGraph.Build([aria, momo], GilRange.ThreeMonths, Items.Now);
        Assert.True(first.EarlyDays);
        Assert.Equal(Today, first.Started);

        var yesterday = GilPointsTests.WithPoints(Items.Record(2, "Kestrel Vane"), ("2026-09-12", 900));
        Assert.False(GilGraph.Build([yesterday, momo], GilRange.ThreeMonths, Items.Now).EarlyDays);
    }

    [Fact]
    public void The_day_axis_has_five_dated_ticks_and_ends_a_day_after_today()
    {
        // Day k's step runs from k to k + 1, so the axis ends one day after today. Five ticks spread over the range, "d MMM", or "d MMM yy"
        // once it crosses a year; every day while the range is five days or fewer.
        var aria = GilPointsTests.WithPoints(Items.Record(), ("2026-09-10", 1_000));

        var months = GilGraph.Build([aria], GilRange.ThreeMonths, Items.Now).DayTicks();
        Assert.Equal((0d, 93d), (months.Low, months.High));
        Assert.Equal([0d, 23d, 46d, 69d, 92d], months.Values);
        Assert.Equal(["13 Jun", "6 Jul", "29 Jul", "21 Aug", "13 Sep"], months.Labels);

        var year = GilGraph.Build([aria], GilRange.OneYear, Items.Now).DayTicks();
        Assert.Equal(["13 Sep 25", "13 Dec 25", "14 Mar 26", "14 Jun 26", "13 Sep 26"], year.Labels);

        var all = GilGraph.Build([aria], GilRange.All, Items.Now).DayTicks();
        Assert.Equal((0d, 4d), (all.Low, all.High));
        Assert.Equal(["10 Sep", "11 Sep", "12 Sep", "13 Sep"], all.Labels);
    }

    [Fact]
    public void The_amount_axis_steps_by_1_2_or_5_and_has_room_for_a_flat_line_and_tens_of_billions()
    {
        // About four steps of 1, 2 or 5 times a power of ten over the amounts drawn, each tick in the Totals page's compact form. A flat
        // line gets room around it instead of an axis of no height; 0 gil stays at the bottom, never below it; tens of billions read "B".
        var span = GilGraph.AmountTicks(30_000_000, 45_908_500);
        Assert.Equal((30_000_000d, 50_000_000d), (span.Low, span.High));
        Assert.Equal(["30M", "35M", "40M", "45M", "50M"], span.Labels);
        Assert.Equal(span.Labels.Length, span.Values.Length);

        var flat = GilGraph.AmountTicks(1_000_000, 1_000_000);
        Assert.Equal((800_000d, 1_200_000d), (flat.Low, flat.High));
        Assert.Equal(["800k", "900k", "1M", "1.1M", "1.2M"], flat.Labels);

        Assert.Equal(["0", "500", "1,000"], GilGraph.AmountTicks(0, 0).Labels);
        Assert.Equal(["25B", "30B", "35B", "40B"], GilGraph.AmountTicks(32_999_999_967, 32_999_999_967).Labels);
    }

    [Fact]
    public void A_small_change_on_a_large_amount_gets_room_and_every_tick_label_is_distinct()
    {
        // A vendor purchase of 2,000 on 45.2M, 8,500 on 1.4M, or 2,000 on 1B is a change too small to draw as a cliff the axis's full
        // height. Amounts closer together than the flat line's room get that room, so the ticks step by millions and each reads once.
        // Should two compact labels still come out the same, as 1,000,000 and 1,050,000 both read "1M", every label is the exact amount.
        Assert.Equal(["35M", "40M", "45M", "50M", "55M"], GilGraph.AmountTicks(45_198_000, 45_200_000).Labels);
        Assert.Equal(["1M", "1.2M", "1.4M", "1.6M", "1.8M"], GilGraph.AmountTicks(1_394_567, 1_403_067).Labels);
        Assert.Equal(["600M", "800M", "1B", "1.2B", "1.4B"], GilGraph.AmountTicks(999_999_000, 1_000_001_000).Labels);

        var close = GilGraph.AmountTicks(800_008, 1_000_010);
        Assert.Equal(["800,000", "850,000", "900,000", "950,000", "1,000,000", "1,050,000"], close.Labels);
        foreach (var (min, max) in new (long, long)[] { (45_198_000, 45_200_000), (1_394_567, 1_403_067), (999_999_000, 1_000_001_000), (800_008, 1_000_010) })
        {
            var labels = GilGraph.AmountTicks(min, max).Labels;
            Assert.Equal(labels.Length, labels.Distinct().Count());
        }
    }

    [Fact]
    public void The_day_under_the_mouse_is_the_day_whose_step_it_is_on()
    {
        // Day k's step runs from k to k + 1; a place left of the first day or right of today reads the nearer end.
        var graph = GilGraph.Build([GilPointsTests.WithPoints(Items.Record(), ("2026-09-10", 1_000))], GilRange.OneMonth, Items.Now);

        Assert.Equal(Day(2026, 8, 13), graph.DayAt(0.9));
        Assert.Equal(Day(2026, 8, 14), graph.DayAt(1.0));
        Assert.Equal(Today, graph.DayAt(31.99));
        Assert.Equal(Today, graph.DayAt(40));
        Assert.Equal(Day(2026, 8, 13), graph.DayAt(-3));
    }

    [Fact]
    public void The_graph_cache_key_moves_with_the_records_the_range_and_the_utc_day()
    {
        // Plugin.GilGraphFor keeps its last graph while nothing that could move it has moved.
        var key = GilGraphCacheKey.For(7, GilRange.ThreeMonths, Items.Now);

        Assert.Equal(key, GilGraphCacheKey.For(7, GilRange.ThreeMonths, Items.Now + TimeSpan.FromHours(11)));         // 23:00 the same UTC day
        Assert.NotEqual(key, GilGraphCacheKey.For(7, GilRange.ThreeMonths, Items.Now + TimeSpan.FromHours(12)));      // midnight UTC
        Assert.NotEqual(key, GilGraphCacheKey.For(8, GilRange.ThreeMonths, Items.Now));
        Assert.NotEqual(key, GilGraphCacheKey.For(7, GilRange.OneYear, Items.Now));
    }
}
