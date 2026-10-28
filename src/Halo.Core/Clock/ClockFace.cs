using System.Globalization;

namespace Halo.Core.Clock;

/// <summary>Text and values shown by the clock, computed for one local instant.</summary>
public sealed record ClockReading(string Time, string Designator, string ShortDate, string LongDate, double DayFraction, int DayOfYear)
{
    /// <summary>Time with its AM/PM designator when the culture uses one ("4:05 PM" or "16:05").</summary>
    public string TimeWithDesignator => Designator.Length == 0 ? Time : $"{Time} {Designator}";

    public string DayProgressText => string.Create(CultureInfo.CurrentCulture, $"Day {DayOfYear} · {Math.Floor(DayFraction * 100):0}% of today has passed");
}

/// <summary>
/// Minute-resolution clock (no seconds, so the settled island redraws at most once a minute). Formatting follows
/// the given culture's short time pattern; the AM/PM designator is split out so it can be drawn smaller.
/// </summary>
public static class ClockFace
{
    public static ClockReading Read(DateTimeOffset localNow, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var format = culture.DateTimeFormat;
        var pattern = format.ShortTimePattern;
        var usesDesignator = pattern.Contains('t', StringComparison.Ordinal);
        var corePattern = usesDesignator ? pattern.Replace("tt", string.Empty, StringComparison.Ordinal).Replace("t", string.Empty, StringComparison.Ordinal).Trim() : pattern;
        var time = localNow.ToString(corePattern, culture);
        var designator = usesDesignator ? localNow.ToString("tt", culture) : string.Empty;
        var shortDate = localNow.ToString("ddd d", culture);
        var longDate = localNow.ToString(MonthDayWithWeekday(format), culture);
        var fraction = localNow.TimeOfDay.TotalSeconds / TimeSpan.FromDays(1).TotalSeconds;
        return new ClockReading(time, designator, shortDate, longDate, fraction, localNow.DayOfYear);
    }

    /// <summary>
    /// Delay until just after the next minute boundary, so a one-shot timer re-armed on each tick keeps the display
    /// exact without a periodic high-frequency timer.
    /// </summary>
    public static TimeSpan DelayToNextMinute(DateTimeOffset now, TimeSpan slack)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slack, TimeSpan.Zero);
        var intoMinute = TimeSpan.FromTicks(now.TimeOfDay.Ticks % TimeSpan.TicksPerMinute);
        return TimeSpan.FromMinutes(1) - intoMinute + slack;
    }

    /// <summary>"dddd, MMMM d" style without the year, in the culture's own month/day order.</summary>
    private static string MonthDayWithWeekday(DateTimeFormatInfo format) => "dddd, " + format.MonthDayPattern;
}
