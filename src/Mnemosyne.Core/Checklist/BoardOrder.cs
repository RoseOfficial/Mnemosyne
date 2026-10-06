namespace Mnemosyne.Core.Checklist;

/// <summary>The order a page keeps drawing a section in while the pointer stays on it, so a click under
/// the cursor never resorts the lines under it. Board.Build's own order (Board.InOrder) is unchanged; this only lets a caller freeze the order
/// it draws in for a while. A pure function: a line still on the board keeps its kept position; a line new to the board goes at the end; a
/// line that left the board is dropped. No kept order (<paramref name="previous"/> is null) returns the board's own order untouched.</summary>
public static class BoardOrder
{
    public static IReadOnlyList<string> Keep(IReadOnlyList<string>? previous, IReadOnlyList<string> board)
    {
        if (previous == null) return board;
        var onBoard = new HashSet<string>(board, StringComparer.Ordinal);
        var kept = previous.Where(onBoard.Contains).ToList();
        var already = new HashSet<string>(kept, StringComparer.Ordinal);
        kept.AddRange(board.Where(id => !already.Contains(id)));
        return kept;
    }
}
