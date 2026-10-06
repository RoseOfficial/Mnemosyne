using System.Globalization;

namespace Mnemosyne.Core.Checklist;

/// <summary>The one compact form the Totals page and the All characters table's
/// new columns use. Under 10,000 the plain number, grouped by thousands ("9,999"). Then thousands, millions and billions with one decimal:
/// "12.3k", "1.2M", "3.4B", a trailing ".0" dropped ("160k"). The decimal is cut, never rounded up, so 999,999 reads "999.9k" and a sum never
/// reads more than is held.</summary>
public static class Compact
{
    public static string Format(long value)
    {
        if (value < 10_000) return value.ToString("N0", CultureInfo.InvariantCulture);
        var (unit, suffix) = value switch
        {
            >= 1_000_000_000 => (1_000_000_000L, "B"),
            >= 1_000_000 => (1_000_000L, "M"),
            _ => (1_000L, "k"),
        };
        var tenths = value / (unit / 10);
        var whole = (tenths / 10).ToString(CultureInfo.InvariantCulture);
        return (tenths % 10 == 0 ? whole : whole + "." + (tenths % 10).ToString(CultureInfo.InvariantCulture)) + suffix;
    }
}
