using Mnemosyne.Core.Definitions;

namespace Mnemosyne.Core.Readers;

/// <summary>What makes a week's Fashion Report done: one attempt made, every attempt
/// used, or a rating that earns the second reward.</summary>
public enum FashionReportRule { AttemptMade, AllAttemptsUsed, RewardScore }

/// <summary>The Fashion Report row, judged from the two numbers the Fashion agent carries: attempts remaining and the
/// week's high score. Pure, so the rule is tested without a client.</summary>
public static class FashionReport
{
    /// <summary>RewardScore collects both weekly rewards: 10,000
    /// MGP for taking part and 50,000 more at a rating of 80 or above. This is the one line that changes it.</summary>
    public const FashionReportRule Rule = FashionReportRule.RewardScore;

    public static bool IsDone(FashionReportRule rule, int remaining, int highScore, FashionReportRules rules) => rule switch
    {
        FashionReportRule.AttemptMade => remaining < rules.Attempts,
        FashionReportRule.AllAttemptsUsed => remaining <= 0,
        FashionReportRule.RewardScore => highScore >= rules.RewardScore,
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "unknown Fashion Report rule"),
    };
}
