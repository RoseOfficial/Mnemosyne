namespace Mnemosyne;

/// <summary>The frame cost of the live tick, for the settings page.</summary>
public sealed class TickStats
{
    private double _totalUs;

    public long LastUs { get; private set; }
    public long MaxUs { get; private set; }
    public long Count { get; private set; }
    public double AverageUs => Count == 0 ? 0 : _totalUs / Count;

    public void Record(TimeSpan elapsed)
    {
        var us = (long)(elapsed.Ticks / 10);
        LastUs = us;
        if (us > MaxUs) MaxUs = us;
        Count++;
        _totalUs += us;
    }
}
