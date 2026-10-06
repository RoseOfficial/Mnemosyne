using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Mnemosyne.Core.Checklist;
using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Records;
using Mnemosyne.Core.Storage;

namespace Mnemosyne.Core.Tests;

/// <summary>The record's gil history and its rules: the stored map, today's point, carrying forward, the total, seeding, and a
/// hand-edited history. The Tracker's part is TrackerTests'; the graph's is GilGraphTests'.</summary>
public class GilPointsTests
{
    private static DateOnly Day(int year, int month, int day) => new(year, month, day);

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>A record holding these points, keyed as the store writes them. GilGraphTests and the golden reuse it.</summary>
    internal static CharacterRecord WithPoints(CharacterRecord record, params (string Key, long Amount)[] points)
    {
        record.GilHistory = points.ToDictionary(p => p.Key, p => p.Amount);
        return record;
    }

    /// <summary>A record with no gil history, as Json.Options writes it: the pin that such a record saves exactly as it always did.</summary>
    private const string Before = """
        {
          "schemaVersion": 2,
          "contentId": 1,
          "name": "Aria Whitewind",
          "homeWorld": "",
          "dataCenter": "",
          "hidden": false,
          "firstSeen": "2026-09-13T12:00:00+00:00",
          "lastSeen": "2026-09-13T12:00:00+00:00",
          "categoryReadAt": {},
          "itemReadAt": {},
          "itemNames": {},
          "reminded": {},
          "values": {},
          "observed": {},
          "ignored": []
        }
        """;

    [Fact]
    public void A_record_with_no_history_saves_exactly_as_before_and_one_with_points_writes_a_map_of_days()
    {
        // The field is null until the first point, and Json.Options leaves a null out, so the file of a record with no history is byte for
        // byte unchanged. With points it is "gilHistory", day to amount, in the order they came.
        var record = new CharacterRecord { ContentId = 1, Name = "Aria Whitewind", FirstSeen = Items.Now, LastSeen = Items.Now };
        Assert.Equal(Before.ReplaceLineEndings("\n"), JsonSerializer.Serialize(record, Json.Options).ReplaceLineEndings("\n"));

        WithPoints(record, ("2026-09-12", 1_300_000), ("2026-09-13", 32_999_999_967));
        var saved = JsonNode.Parse(JsonSerializer.Serialize(record, Json.Options))!["gilHistory"]!.AsObject();
        Assert.Equal(["2026-09-12", "2026-09-13"], saved.Select(p => p.Key));
        Assert.Equal(32_999_999_967L, saved["2026-09-13"]!.GetValue<long>());
    }

    /// <summary>A record as an older build declares it, reduced to what the test needs: it does not know "gilHistory", so the field lands
    /// in its extension data.</summary>
    private sealed class OlderRecord
    {
        public int SchemaVersion { get; set; }
        public ulong ContentId { get; set; }
        public string Name { get; set; } = "";
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }

    [Fact]
    public void The_history_is_carried_through_a_load_and_a_save_by_a_record_type_that_does_not_know_it_and_through_the_store()
    {
        // No schema bump. An older build reads the field into its [JsonExtensionData] and writes it back whole, so a rollback and a return
        // keep every point; this build's store saves and loads it at schema 2.
        var record = WithPoints(Items.Record(), ("2026-09-12", 1_300_000), ("2026-09-13", 1_394_567));
        var written = JsonSerializer.Serialize(record, Json.Options);

        var older = JsonSerializer.Deserialize<OlderRecord>(written, Json.Options)!;
        Assert.True(older.Extra!.ContainsKey("gilHistory"));
        var rewritten = JsonNode.Parse(JsonSerializer.Serialize(older, Json.Options))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(written)!["gilHistory"], rewritten["gilHistory"]));

        var fs = new InMemoryFileSystem();
        new RecordStore(fs, @"C:\cfg\Mnemosyne").Save(record);
        var back = new RecordStore(fs, @"C:\cfg\Mnemosyne").LoadAll().Single();
        Assert.Equal(CharacterRecord.CurrentSchemaVersion, back.SchemaVersion);
        Assert.Equal(record.GilHistory, back.GilHistory);
    }

    [Fact]
    public void Todays_point_is_the_last_amount_seen_that_day_and_the_next_day_gets_its_own()
    {
        // Within a day the point is replaced; the first write after midnight UTC starts the next day's point and leaves the last day's as
        // it ended. The amount is own gil plus every retainer's (Totals' "All gil").
        var record = Items.Record();
        TotalsTests.Gil(record, 1_000_000);

        Assert.True(GilPoints.Note(record, At(13, 9)));
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 1_000_000 }, record.GilHistory);

        TotalsTests.Gil(record, 1_200_000, 100_000, 50_000);
        Assert.True(GilPoints.Note(record, At(13, 23, 59)));
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 1_350_000 }, record.GilHistory);

        TotalsTests.Gil(record, 1_100_000, 100_000, 50_000);
        Assert.True(GilPoints.Note(record, At(14, 0, 1)));
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 1_350_000, ["2026-09-14"] = 1_250_000 }, record.GilHistory);
    }

    [Fact]
    public void No_point_is_written_without_own_gil_stored_or_when_todays_amount_has_not_changed()
    {
        // With no own gil stored there is no amount, even with a retainer row, and no point: the field stays null, so the record still saves
        // as before. Today's point equal to the amount writes nothing, and Note says so, which is what keeps a tick with no change from saving.
        var record = Items.Record();
        record.Values[MoneyIds.RetainerId(0, MoneyIds.RetainerGil)] = TotalsTests.Held(50_000, 999_999_999);
        Assert.Null(GilPoints.Amount(record));
        Assert.False(GilPoints.Note(record, At(13, 12)));
        Assert.Null(record.GilHistory);

        TotalsTests.Gil(record, 0, 50_000);                                                  // gil at zero is still stored
        Assert.Equal(50_000L, GilPoints.Amount(record));
        Assert.True(GilPoints.Note(record, At(13, 12)));
        Assert.False(GilPoints.Note(record, At(13, 18)));
        Assert.Equal(new Dictionary<string, long> { ["2026-09-13"] = 50_000 }, record.GilHistory);
    }

    [Fact]
    public void A_day_is_the_utc_calendar_day_whatever_the_offset_of_the_instant()
    {
        // 20:00 on the 13th at UTC-5 is 01:00 UTC on the 14th, and 00:30 on the 14th at UTC+2 is 22:30 UTC on the 13th: each point is the
        // UTC day's, never the PC's local date.
        var record = Items.Record();
        TotalsTests.Gil(record, 1_000);
        GilPoints.Note(record, new DateTimeOffset(2026, 9, 13, 20, 0, 0, TimeSpan.FromHours(-5)));
        TotalsTests.Gil(record, 2_000);
        GilPoints.Note(record, new DateTimeOffset(2026, 9, 14, 0, 30, 0, TimeSpan.FromHours(2)));

        Assert.Equal(new Dictionary<string, long> { ["2026-09-14"] = 1_000, ["2026-09-13"] = 2_000 }, record.GilHistory);
        Assert.Equal(Day(2026, 9, 14), GilPoints.DayOf(new DateTimeOffset(2026, 9, 13, 20, 0, 0, TimeSpan.FromHours(-5))));
        Assert.Equal("2026-09-14", GilPoints.Key(Day(2026, 9, 14)));
    }

    [Fact]
    public void A_day_with_no_point_carries_the_latest_point_before_it_and_there_is_nothing_before_the_first()
    {
        var record = WithPoints(Items.Record(), ("2026-09-01", 900_000), ("2026-09-06", 1_300_000));

        Assert.Null(GilPoints.On(record, Day(2026, 8, 31)));
        Assert.Equal(900_000L, GilPoints.On(record, Day(2026, 9, 1)));
        Assert.Equal(900_000L, GilPoints.On(record, Day(2026, 9, 5)));
        Assert.Equal(1_300_000L, GilPoints.On(record, Day(2026, 9, 6)));
        Assert.Equal(1_300_000L, GilPoints.On(record, Day(2026, 9, 13)));
        Assert.Null(GilPoints.On(Items.Record(), Day(2026, 9, 13)));                           // no history at all
    }

    [Fact]
    public void The_total_on_a_day_counts_each_visible_character_from_the_start_at_its_first_amount_and_leaves_a_hidden_one_out()
    {
        // The total starts on the first day any visible character has a point, 1 September, and is null before it, not 0. Kestrel's first
        // point is the 10th, yet the total counts him from the 1st at that first amount, 8,500, and then carries his points as his own
        // amount does; his own amount has none before the 10th. Momo is hidden and in no total. Aria's one point is past int.MaxValue on
        // its own, so the total is a 64-bit number.
        var aria = WithPoints(Items.Record(), ("2026-09-01", 10_999_999_989));
        var kestrel = WithPoints(Items.Record(2, "Kestrel Vane"), ("2026-09-10", 8_500), ("2026-09-12", 9_500));
        var momo = WithPoints(Items.Record(3, "Momo Lulu"), ("2026-08-01", 5_000_000));
        momo.Hidden = true;
        CharacterRecord[] records = [aria, kestrel, momo];

        Assert.Null(GilPoints.TotalOn(records, Day(2026, 8, 31)));
        Assert.Equal(11_000_008_489L, GilPoints.TotalOn(records, Day(2026, 9, 1)));
        Assert.Equal(11_000_008_489L, GilPoints.TotalOn(records, Day(2026, 9, 11)));
        Assert.Equal(11_000_009_489L, GilPoints.TotalOn(records, Day(2026, 9, 13)));
        Assert.Null(GilPoints.On(kestrel, Day(2026, 9, 9)));
    }

    [Fact]
    public void Seeding_gives_a_record_with_own_gil_and_no_history_one_point_on_its_last_seen_day_and_never_overwrites()
    {
        // The seed is the amount now, own gil and retainers', dated the UTC day of the last-seen instant. A record with any readable point
        // is left alone, whatever its amount; so is one with no own gil stored, or never seen.
        var record = Items.Record();
        record.LastSeen = new DateTimeOffset(2026, 9, 10, 23, 30, 0, TimeSpan.FromHours(-2));  // 01:30 UTC on the 11th
        TotalsTests.Gil(record, 1_000_000, 200_000);
        Assert.True(GilPoints.Seed(record));
        Assert.Equal(new Dictionary<string, long> { ["2026-09-11"] = 1_200_000 }, record.GilHistory);

        TotalsTests.Gil(record, 5_000_000);
        record.LastSeen = Items.Now;
        Assert.False(GilPoints.Seed(record));                                                 // never overwrites, never adds a second
        Assert.Equal(new Dictionary<string, long> { ["2026-09-11"] = 1_200_000 }, record.GilHistory);

        var unread = Items.Record(2, "Kestrel Vane");                                          // Money never read
        Assert.False(GilPoints.Seed(unread));
        Assert.Null(unread.GilHistory);

        var unseen = new CharacterRecord { ContentId = 3, Name = "Momo Lulu" };                // no last-seen instant
        TotalsTests.Gil(unseen, 1_000);
        Assert.False(GilPoints.Seed(unseen));
        Assert.Null(unseen.GilHistory);
    }

    [Fact]
    public void A_hand_edited_history_skips_a_key_it_cannot_read_reads_the_rest_in_date_order_and_a_future_point_only_from_its_day()
    {
        // Keys out of order (a hand edit, or a PC clock set back) read in date order; a key this build cannot read is skipped and kept,
        // never a throw; a point after today, from a PC whose clock ran ahead, is not read before its own day. A history holding only
        // keys it cannot read counts as none for seeding, and the seed sits beside them.
        var record = WithPoints(Items.Record(), ("2026-09-20", 7_000), ("13/09/2026", 1), ("2026-09-06", 5_000), ("2026-02-30", 2), ("", 3), ("2026-09-01", 4_000));

        Assert.Equal([(Day(2026, 9, 1), 4_000L), (Day(2026, 9, 6), 5_000L), (Day(2026, 9, 20), 7_000L)], GilPoints.Of(record));
        Assert.Equal(5_000L, GilPoints.On(record, Day(2026, 9, 13)));
        Assert.Equal(7_000L, GilPoints.On(record, Day(2026, 9, 20)));

        var unreadable = WithPoints(Items.Record(2, "Kestrel Vane"), ("yesterday", 1));
        TotalsTests.Gil(unreadable, 8_500);
        Assert.True(GilPoints.Seed(unreadable));
        Assert.Equal(new Dictionary<string, long> { ["yesterday"] = 1, ["2026-09-13"] = 8_500 }, unreadable.GilHistory);
    }
}
