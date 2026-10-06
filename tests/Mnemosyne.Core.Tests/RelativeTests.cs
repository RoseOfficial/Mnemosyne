using Mnemosyne.Core.Time;

namespace Mnemosyne.Core.Tests;

public class RelativeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 0, 45, "in 45 min")]
    [InlineData(0, 3, 0, "in 3 h")]
    [InlineData(0, 3, 30, "in 3 h 30 min")]
    [InlineData(2, 3, 0, "in 2 d 3 h")]
    [InlineData(9, 0, 0, "in 9 d")]
    public void Until_is_short_and_coarse(int d, int h, int m, string expected) =>
        Assert.Equal(expected, Relative.Until(Now + new TimeSpan(d, h, m, 0), Now));

    [Fact]
    public void A_passed_deadline_is_overdue()
    {
        Assert.Equal("overdue by 1 d", Relative.Until(Now - TimeSpan.FromDays(1), Now));
        Assert.Equal("overdue by 5 min", Relative.Until(Now - TimeSpan.FromMinutes(5), Now));
        Assert.Equal("now", Relative.Until(Now, Now));
    }

    [Theory]
    [InlineData(0, 0, "just now")]
    [InlineData(0, 5, "5 min ago")]
    [InlineData(3, 0, "3 h ago")]
    [InlineData(30, 0, "yesterday")]
    [InlineData(72, 0, "3 days ago")]
    public void Since_reads_like_a_person_wrote_it(int h, int m, string expected) =>
        Assert.Equal(expected, Relative.Since(Now - new TimeSpan(h, m, 0), Now));
}
