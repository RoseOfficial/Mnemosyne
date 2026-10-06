namespace Mnemosyne.Core.Time;

/// <summary>Relative times as a person would say them. Two units at most, minutes dropped past a day.</summary>
public static class Relative
{
    public static string Until(DateTimeOffset deadline, DateTimeOffset now)
    {
        var span = deadline - now;
        if (span.Duration() < TimeSpan.FromMinutes(1)) return "now";
        return span < TimeSpan.Zero ? "overdue by " + Coarse(span.Duration()) : "in " + Coarse(span);
    }

    public static string Since(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes} min ago";
        if (span < TimeSpan.FromHours(24)) return $"{(int)span.TotalHours} h ago";
        if (span < TimeSpan.FromHours(48)) return "yesterday";
        return $"{(int)span.TotalDays} days ago";
    }

    private static string Coarse(TimeSpan span)
    {
        var days = (int)span.TotalDays;
        var hours = span.Hours;
        var minutes = span.Minutes;
        if (days > 0) return hours > 0 ? $"{days} d {hours} h" : $"{days} d";
        if (hours > 0) return minutes > 0 ? $"{hours} h {minutes} min" : $"{hours} h";
        return $"{Math.Max(1, minutes)} min";
    }
}
