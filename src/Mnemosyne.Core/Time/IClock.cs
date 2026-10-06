namespace Mnemosyne.Core.Time;

/// <summary>The only source of "now" inside Core. The plugin provides the machine's UTC clock; tests a fake.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
