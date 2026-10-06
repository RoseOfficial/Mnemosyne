using Mnemosyne.Core.Definitions;
using Mnemosyne.Core.Readers;

namespace Mnemosyne.Core.Tests;

public class FashionReportTests
{
    private static readonly FashionReportRules Rules = new(4, 80);

    [Fact]
    public void Each_done_rule_reads_the_remaining_attempts_and_the_high_score()
    {
        Assert.False(FashionReport.IsDone(FashionReportRule.AttemptMade, 4, 0, Rules));
        Assert.True(FashionReport.IsDone(FashionReportRule.AttemptMade, 3, 0, Rules));
        Assert.False(FashionReport.IsDone(FashionReportRule.AllAttemptsUsed, 1, 95, Rules));
        Assert.True(FashionReport.IsDone(FashionReportRule.AllAttemptsUsed, 0, 0, Rules));
        Assert.False(FashionReport.IsDone(FashionReportRule.RewardScore, 3, 79, Rules));
        Assert.True(FashionReport.IsDone(FashionReportRule.RewardScore, 3, 80, Rules));
        Assert.Equal(FashionReportRule.RewardScore, FashionReport.Rule);   // the active rule; one line to change
    }
}
