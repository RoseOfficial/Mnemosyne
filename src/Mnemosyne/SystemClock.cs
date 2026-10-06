using Mnemosyne.Core.Time;

namespace Mnemosyne;

/// <summary>The machine's UTC clock. Reset hours are UTC constants, so no server clock is needed.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
