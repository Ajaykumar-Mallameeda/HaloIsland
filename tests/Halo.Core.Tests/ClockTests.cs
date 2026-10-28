using System.Globalization;
using Halo.Core.Clock;
using Xunit;

namespace Halo.Core.Tests;

public class ClockFaceTests
{
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 28, 16, 5, 42, TimeSpan.FromHours(5.5));

    [Fact]
    public void SplitsTheDesignatorForTwelveHourCultures()
    {
        var r = ClockFace.Read(Afternoon, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("4:05", r.Time);
        Assert.Equal("PM", r.Designator);
        Assert.Equal("4:05 PM", r.TimeWithDesignator);
        Assert.Equal("Wed 28", r.ShortDate);
        Assert.Equal("Wednesday, October 28", r.LongDate);
    }

    [Fact]
    public void UsesTwentyFourHourTimeWhereTheCultureDoes()
    {
        var r = ClockFace.Read(Afternoon, CultureInfo.GetCultureInfo("de-DE"));

        Assert.Equal("16:05", r.Time);
        Assert.Equal(string.Empty, r.Designator);
        Assert.Equal("16:05", r.TimeWithDesignator);
    }

    [Fact]
    public void ShowsNoSeconds()
    {
        var r = ClockFace.Read(Afternoon, CultureInfo.InvariantCulture);

        Assert.DoesNotContain("42", r.Time, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsDayProgress()
    {
        var r = ClockFace.Read(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(0.75, r.DayFraction, 6);
        Assert.Equal(301, r.DayOfYear);
        Assert.Contains("75%", r.DayProgressText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0, 60_000)]
    [InlineData(42, 0, 18_000)]
    [InlineData(59, 999, 1)]
    public void NextMinuteDelayLandsJustAfterTheBoundary(int second, int millisecond, int expectedMs)
    {
        var now = new DateTimeOffset(2026, 10, 28, 16, 5, second, millisecond, TimeSpan.Zero);

        var delay = ClockFace.DelayToNextMinute(now, TimeSpan.FromMilliseconds(40));

        Assert.Equal(expectedMs + 40, delay.TotalMilliseconds, 3);
        Assert.Equal(0, (now + delay).Second);
    }

    [Fact]
    public void NegativeSlackIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockFace.DelayToNextMinute(Afternoon, TimeSpan.FromMilliseconds(-1)));
}
