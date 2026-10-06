using Mnemosyne.Core.Checklist;

namespace Mnemosyne.Core.Tests;

/// <summary>The pure part of freezing All tasks' drawn order while the pointer stays on the page.</summary>
public class BoardOrderTests
{
    [Fact]
    public void A_line_still_on_the_board_keeps_its_kept_position()
    {
        Assert.Equal(["c", "a", "b"], BoardOrder.Keep(["c", "a", "b"], ["a", "b", "c"]));
    }

    [Fact]
    public void A_line_new_to_the_board_goes_at_the_end_of_its_group()
    {
        Assert.Equal(["b", "a", "c"], BoardOrder.Keep(["b", "a"], ["a", "b", "c"]));
    }

    [Fact]
    public void A_line_that_left_the_board_is_dropped()
    {
        Assert.Equal(["c", "a"], BoardOrder.Keep(["c", "a", "b"], ["a", "c"]));
    }

    [Fact]
    public void No_kept_order_returns_the_boards_own_order()
    {
        Assert.Equal(["a", "b", "c"], BoardOrder.Keep(null, ["a", "b", "c"]));
    }
}
