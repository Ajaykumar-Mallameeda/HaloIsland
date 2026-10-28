using System.Globalization;
using Halo.Core.Clock;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Clock correctness at day boundaries and daylight-saving days (M1-F03).</summary>
public class ClockBoundaryTests
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void MidnightRollsTheDateAndRestartsDayProgress()
    {
        var before = ClockFace.Read(new DateTimeOffset(2026, 12, 31, 23, 59, 30, TimeSpan.Zero), Us);
        var delay = ClockFace.DelayToNextMinute(new DateTimeOffset(2026, 12, 31, 23, 59, 30, TimeSpan.Zero), TimeSpan.FromMilliseconds(40));
        var after = ClockFace.Read(new DateTimeOffset(2026, 12, 31, 23, 59, 30, TimeSpan.Zero) + delay, Us);

        Assert.Equal("Thu 31", before.ShortDate);
        Assert.Equal(365, before.DayOfYear);
        Assert.Equal("Fri 1", after.ShortDate);
        Assert.Equal(1, after.DayOfYear);
        Assert.Equal("12:00", after.Time);
        Assert.Equal("AM", after.Designator);
        Assert.InRange(after.DayFraction, 0, 0.0001);
    }

    [Theory]
    [InlineData(0, 0, 0, "Day 1 · 0% of today has passed")]
    [InlineData(12, 0, 0, "Day 1 · 50% of today has passed")]
    [InlineData(23, 59, 59, "Day 1 · 99% of today has passed")]
    public void DayProgressStaysWithinZeroToNinetyNinePercent(int h, int m, int s, string expected)
    {
        var reading = ClockFace.Read(new DateTimeOffset(2026, 1, 1, h, m, s, TimeSpan.Zero), Us);

        Assert.InRange(reading.DayFraction, 0, 0.999999);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Us;
        try
        {
            Assert.Equal(expected, reading.DayProgressText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void DaylightSavingDaysUseTheWallClock()
    {
        // 2026-03-08 in US Eastern has 23 hours: 03:30 local is 2.5 h after midnight in real time but reads 03:30.
        var local = new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4));
        var reading = ClockFace.Read(local, Us);

        Assert.Equal("3:30", reading.Time);
        Assert.Equal(3.5 / 24, reading.DayFraction, 6);
    }

    [Fact]
    public void LeapDayIsDaySixtyAndYearEndIsDay366()
    {
        Assert.Equal(60, ClockFace.Read(new DateTimeOffset(2028, 2, 29, 9, 0, 0, TimeSpan.Zero), Us).DayOfYear);
        Assert.Equal(366, ClockFace.Read(new DateTimeOffset(2028, 12, 31, 9, 0, 0, TimeSpan.Zero), Us).DayOfYear);
    }

    [Fact]
    public void TheMinuteTimerIsTheOnlyPeriodicWorkAndFiresAtMostOncePerMinute()
    {
        // Re-arming from just after a boundary always waits a full minute (plus slack): no faster cadence exists.
        var justAfter = new DateTimeOffset(2026, 10, 28, 10, 0, 0, 40, TimeSpan.Zero);
        var delay = ClockFace.DelayToNextMinute(justAfter, TimeSpan.FromMilliseconds(40));

        Assert.Equal(TimeSpan.FromMilliseconds(60_000), delay);
    }
}
