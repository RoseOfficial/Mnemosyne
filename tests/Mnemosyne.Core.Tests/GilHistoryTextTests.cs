using Mnemosyne.Core.Checklist;

namespace Mnemosyne.Core.Tests;

/// <summary>The dump's gil history section beyond its golden (GoldenTests).</summary>
public class GilHistoryTextTests
{
    [Fact]
    public void Seeds_on_nine_last_seen_days_read_the_same_total_on_every_day_of_the_dump()
    {
        // The total counts every character from the start: a character's own last-seen-days-ago point does not gate when its gil joins
        // the total, so the total is 45,000,000 (nine characters at 5,000,000 each) on every day from the first seed on. A year ago is
        // before any point at all, so it has no total yet, the start of the total being unchanged. Each character's own row still reads
        // "no history yet" before its own first point.
        var lines = GilHistoryText.Render(GilGraphTests.NineSeeded(), Items.Now).Split('\n');

        Assert.Equal("All gil · today 45,000,000 · 7 days ago 45,000,000 · 30 days ago 45,000,000 · a year ago no history yet", lines[0]);
        Assert.Equal("  Momo Lulu · today 5,000,000 · 7 days ago no history yet · 30 days ago no history yet · a year ago no history yet", lines[9]);
    }
}
