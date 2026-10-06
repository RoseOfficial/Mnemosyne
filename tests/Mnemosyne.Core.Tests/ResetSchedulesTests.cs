using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class ResetSchedulesTests
{
    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, TimeSpan.Zero);

    [Fact]
    public void Daily_period_changes_at_fifteen_hundred_utc()
    {
        // 2026-09-13 is a Sunday.
        Assert.Equal("daily:2026-09-12", ResetSchedules.PeriodKey(ResetSchedules.Daily, Utc(2026, 9, 13, 14, 59)));
        Assert.Equal("daily:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.Daily, Utc(2026, 9, 13, 15, 0)));
        Assert.Equal(Utc(2026, 9, 13, 15), ResetSchedules.NextReset(ResetSchedules.Daily, Utc(2026, 9, 13, 14, 59)));
        Assert.Equal(Utc(2026, 9, 14, 15), ResetSchedules.NextReset(ResetSchedules.Daily, Utc(2026, 9, 13, 15, 0)));
    }

    [Fact]
    public void Weekly_period_changes_on_tuesday_at_eight_utc()
    {
        // 2026-09-08 and 2026-09-15 are Tuesdays.
        Assert.Equal("weekly:2026-09-08", ResetSchedules.PeriodKey(ResetSchedules.Weekly, Utc(2026, 9, 15, 7, 59)));
        Assert.Equal("weekly:2026-09-15", ResetSchedules.PeriodKey(ResetSchedules.Weekly, Utc(2026, 9, 15, 8, 0)));
        Assert.Equal("weekly:2026-09-08", ResetSchedules.PeriodKey(ResetSchedules.Weekly, Utc(2026, 9, 8, 8, 0)));
        Assert.Equal(Utc(2026, 9, 15, 8), ResetSchedules.NextReset(ResetSchedules.Weekly, Utc(2026, 9, 13, 12)));
    }

    [Fact]
    public void Month_and_year_rollovers_are_plain_dates()
    {
        Assert.Equal("daily:2026-12-31", ResetSchedules.PeriodKey(ResetSchedules.Daily, Utc(2027, 1, 1, 14, 59)));
        Assert.Equal("daily:2027-01-01", ResetSchedules.PeriodKey(ResetSchedules.Daily, Utc(2027, 1, 1, 15, 0)));
    }

    [Fact]
    public void Grand_company_and_cosmic_have_their_own_hours()
    {
        Assert.Equal("grand-company:2026-09-12", ResetSchedules.PeriodKey(ResetSchedules.GrandCompany, Utc(2026, 9, 13, 19, 59)));
        Assert.Equal("grand-company:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.GrandCompany, Utc(2026, 9, 13, 20, 0)));
        Assert.Equal("cosmic:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.Cosmic, Utc(2026, 9, 13, 9, 0)));
    }

    [Fact]
    public void Leve_grants_fall_at_midnight_and_noon_utc()
    {
        Assert.Equal(0, ResetSchedules.GrantsBetween(ResetSchedules.Leve, Utc(2026, 9, 13, 1), Utc(2026, 9, 13, 11, 59)));
        Assert.Equal(1, ResetSchedules.GrantsBetween(ResetSchedules.Leve, Utc(2026, 9, 13, 1), Utc(2026, 9, 13, 12, 0)));
        Assert.Equal(2, ResetSchedules.GrantsBetween(ResetSchedules.Leve, Utc(2026, 9, 13, 1), Utc(2026, 9, 14, 0, 0)));
        Assert.Equal(60, ResetSchedules.GrantsBetween(ResetSchedules.Leve, Utc(2026, 9, 1, 0, 30), Utc(2026, 10, 1, 0, 30)));
    }

    [Fact]
    public void Boundary_between_is_a_period_key_comparison()
    {
        Assert.False(ResetSchedules.BoundaryBetween(ResetSchedules.Daily, Utc(2026, 9, 13, 15), Utc(2026, 9, 14, 14, 59)));
        Assert.True(ResetSchedules.BoundaryBetween(ResetSchedules.Daily, Utc(2026, 9, 13, 15), Utc(2026, 9, 14, 15)));
    }

    [Fact]
    public void An_unknown_schedule_throws()
    {
        Assert.Throws<ArgumentException>(() => ResetSchedules.PeriodKey("nope", Utc(2026, 9, 13, 0)));
    }

    [Fact]
    public void A_non_utc_offset_gives_the_same_key_as_its_utc_instant()
    {
        var eveningWest = new DateTimeOffset(2026, 9, 13, 23, 59, 0, TimeSpan.FromHours(-5));   // 2026-09-14 04:59 UTC
        var morningEast = new DateTimeOffset(2026, 9, 14, 1, 0, 0, TimeSpan.FromHours(10));     // 2026-09-13 15:00 UTC
        Assert.Equal("daily:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.Daily, eveningWest));
        Assert.Equal(ResetSchedules.PeriodKey(ResetSchedules.Daily, eveningWest.ToUniversalTime()), ResetSchedules.PeriodKey(ResetSchedules.Daily, eveningWest));
        Assert.Equal("daily:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.Daily, morningEast));
        Assert.Equal(Utc(2026, 9, 14, 15), ResetSchedules.NextReset(ResetSchedules.Daily, morningEast));

        var mondayWest = new DateTimeOffset(2026, 9, 14, 23, 0, 0, TimeSpan.FromHours(-10)); // Tuesday 2026-09-15 09:00 UTC
        Assert.Equal("weekly:2026-09-15", ResetSchedules.PeriodKey(ResetSchedules.Weekly, mondayWest));
    }

    [Fact]
    public void A_default_start_is_clamped_to_a_year()
    {
        Assert.Equal(732, ResetSchedules.GrantsBetween(ResetSchedules.Leve, default, Utc(2026, 9, 13, 0)));
    }

    [Fact]
    public void A_schedule_with_two_boundaries_a_day_keys_each_period_by_its_hour()
    {
        Assert.Equal("leve:2026-09-13T00", ResetSchedules.PeriodKey(ResetSchedules.Leve, Utc(2026, 9, 13, 11, 59)));
        Assert.Equal("leve:2026-09-13T12", ResetSchedules.PeriodKey(ResetSchedules.Leve, Utc(2026, 9, 13, 12, 0)));
        Assert.True(ResetSchedules.BoundaryBetween(ResetSchedules.Leve, Utc(2026, 9, 13, 11), Utc(2026, 9, 13, 13)));
        Assert.Equal("daily:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.Daily, Utc(2026, 9, 13, 15)));
    }

    [Fact]
    public void The_jumbo_cactpot_draw_is_per_data_centre_and_unknown_ones_have_none()
    {
        // 2026-09-13 is a Sunday; the North American draw is Sunday 02:00 UTC, the other three are Saturday.
        Assert.Equal(ResetSchedules.JumboNorthAmerica, ResetSchedules.JumboDraw("Crystal"));
        Assert.Equal(ResetSchedules.JumboEurope, ResetSchedules.JumboDraw("Light"));
        Assert.Equal(ResetSchedules.JumboJapan, ResetSchedules.JumboDraw("Mana"));
        Assert.Equal(ResetSchedules.JumboOceania, ResetSchedules.JumboDraw("Materia"));
        Assert.Equal(ResetSchedules.JumboNorthAmerica, ResetSchedules.JumboDraw("dynamis"));
        Assert.Null(ResetSchedules.JumboDraw("Shirogane"));
        Assert.Null(ResetSchedules.JumboDraw(""));

        Assert.Equal(Utc(2026, 9, 13, 2), ResetSchedules.PeriodStart(ResetSchedules.JumboNorthAmerica, Utc(2026, 9, 13, 12)));
        Assert.Equal(Utc(2026, 9, 12, 19), ResetSchedules.PeriodStart(ResetSchedules.JumboEurope, Utc(2026, 9, 13, 12)));
        Assert.Equal(Utc(2026, 9, 12, 12), ResetSchedules.PeriodStart(ResetSchedules.JumboJapan, Utc(2026, 9, 13, 12)));
        Assert.Equal(Utc(2026, 9, 12, 9), ResetSchedules.PeriodStart(ResetSchedules.JumboOceania, Utc(2026, 9, 13, 12)));
        Assert.Equal("jumbo-na:2026-09-13", ResetSchedules.PeriodKey(ResetSchedules.JumboNorthAmerica, Utc(2026, 9, 13, 12)));
        Assert.Equal(Utc(2026, 9, 20, 2), ResetSchedules.NextReset(ResetSchedules.JumboNorthAmerica, Utc(2026, 9, 13, 12)));
    }

    [Fact]
    public void Resolve_turns_the_jumbo_sentinel_into_the_data_centres_draw()
    {
        Assert.Equal(ResetSchedules.JumboNorthAmerica, ResetSchedules.Resolve(ResetSchedules.Jumbo, "Crystal"));
        Assert.Equal(ResetSchedules.JumboOceania, ResetSchedules.Resolve(ResetSchedules.Jumbo, "Materia"));
        Assert.Null(ResetSchedules.Resolve(ResetSchedules.Jumbo, "Shirogane"));
        Assert.Equal(ResetSchedules.Weekly, ResetSchedules.Resolve(ResetSchedules.Weekly, "Shirogane"));   // every other name is its own schedule
        Assert.Throws<ArgumentException>(() => ResetSchedules.PeriodKey(ResetSchedules.Jumbo, Utc(2026, 9, 13, 12)));   // the sentinel is never a schedule itself
    }
}
