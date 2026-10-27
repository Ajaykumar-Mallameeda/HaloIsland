using Halo.Spikes.Measurement;
using Xunit;

namespace Halo.Spikes.Tests;

public class StatisticsTests
{
    [Fact]
    public void UsesNearestRank()
    {
        var values = Enumerable.Range(1, 20).Select(i => (double)i).ToArray();

        Assert.Equal(19, Statistics.Percentile(values, 95));
        Assert.Equal(10, Statistics.Median(values));
        Assert.Equal(20, Statistics.Percentile(values, 100));
        Assert.Equal(1, Statistics.Percentile(values, 0.1));
    }

    [Fact]
    public void IgnoresInputOrder()
    {
        Assert.Equal(3, Statistics.Percentile([5, 1, 3, 2, 4], 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100.5)]
    [InlineData(double.NaN)]
    public void RejectsOutOfRangePercentile(double percentile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Statistics.Percentile([1.0], percentile));
    }

    [Fact]
    public void RejectsEmptyAndNonFinite()
    {
        Assert.Throws<ArgumentException>(() => Statistics.Percentile(Array.Empty<double>(), 50));
        Assert.Throws<ArgumentException>(() => Statistics.Percentile([1.0, double.NaN], 50));
    }
}

public class CpuRateTests
{
    [Fact]
    public void NormalisesToTenMinutes()
    {
        Assert.Equal(0.5, CpuRate.SecondsPerTenMinutes(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(20)), 12);
        Assert.Equal(0.3, CpuRate.SecondsPerTenMinutes(TimeSpan.FromSeconds(0.3), TimeSpan.FromMinutes(10)), 12);
    }

    [Fact]
    public void RejectsInvalidIntervals()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuRate.SecondsPerTenMinutes(TimeSpan.Zero, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => CpuRate.SecondsPerTenMinutes(TimeSpan.FromSeconds(-1), TimeSpan.FromMinutes(1)));
    }
}

public class BudgetTests
{
    [Fact]
    public void LimitIsInclusive()
    {
        Assert.Equal(Outcome.Pass, Budgets.Pb01SettledIdleCpu.Evaluate(0.5));
        Assert.Equal(Outcome.Fail, Budgets.Pb01SettledIdleCpu.Evaluate(0.5001));
        Assert.Equal(Outcome.Pass, Budgets.Pb03SettledFrames.Evaluate(0));
        Assert.Equal(Outcome.Fail, Budgets.Pb03SettledFrames.Evaluate(0.1));
    }

    [Fact]
    public void MissingMeasurementIsNotRun()
    {
        Assert.Equal(Outcome.NotRun, Budgets.Pb10PrivateBytes.Evaluate(null));
        Assert.Equal(Outcome.NotRun, Budgets.Pb10PrivateBytes.Evaluate(double.NaN));
    }

    [Fact]
    public void BudgetsMatchAdr()
    {
        Assert.Equal(0.5, Budgets.Pb01SettledIdleCpu.Limit);
        Assert.Equal(2, Budgets.Pb02SettledWakeups.Limit);
        Assert.Equal(0, Budgets.Pb03SettledFrames.Limit);
        Assert.Equal(50, Budgets.Pb06HoverToFirstFrame.Limit);
        Assert.Equal(1.0, Budgets.Pb09WarmStartup.Limit);
        Assert.Equal(120, Budgets.Pb10PrivateBytes.Limit);
        Assert.Equal(0.5, Budgets.Pb12SettledGpu.Limit);
    }
}

public class FramePacingTests
{
    private const double Hz120 = 1000.0 / 120;

    private static double[] Frames(params double[] intervals)
    {
        var times = new double[intervals.Length + 1];
        for (var i = 0; i < intervals.Length; i++)
        {
            times[i + 1] = times[i] + intervals[i];
        }

        return times;
    }

    [Fact]
    public void CountsIntervalsAboveOnePointFiveRefresh()
    {
        var pacing = FramePacing.AnalyzeTransition(Frames(Hz120, Hz120, 2 * Hz120, 1.5 * Hz120), Hz120);

        Assert.Equal(4, pacing.IntervalsMs.Count);
        Assert.Equal(1, pacing.LongFrames); // exactly 1.5x is not "over"
        Assert.Equal(2 * Hz120, pacing.MaxIntervalMs, 9);
    }

    [Fact]
    public void RejectsNonAscendingTimestamps()
    {
        Assert.Throws<ArgumentException>(() => FramePacing.AnalyzeTransition([0, 5, 5], Hz120));
        Assert.Throws<ArgumentException>(() => FramePacing.AnalyzeTransition([0], Hz120));
    }

    [Fact]
    public void PassesSmoothRun()
    {
        var transitions = Enumerable.Range(0, 50)
            .Select(_ => FramePacing.AnalyzeTransition(Frames(Enumerable.Repeat(Hz120, 40).ToArray()), Hz120))
            .ToArray();

        var verdict = FramePacing.Evaluate(transitions, Hz120);

        Assert.Equal(Outcome.Pass, verdict.Outcome);
        Assert.Equal(0, verdict.WorstLongFrames);
    }

    [Fact]
    public void FailsWhenOneTransitionHasTwoLongFrames()
    {
        var smooth = FramePacing.AnalyzeTransition(Frames(Enumerable.Repeat(Hz120, 40).ToArray()), Hz120);
        var hitch = FramePacing.AnalyzeTransition(Frames([.. Enumerable.Repeat(Hz120, 38), 3 * Hz120, 3 * Hz120]), Hz120);
        var transitions = Enumerable.Repeat(smooth, 49).Append(hitch).ToArray();

        var verdict = FramePacing.Evaluate(transitions, Hz120);

        Assert.Equal(Outcome.Fail, verdict.Outcome);
        Assert.Equal(2, verdict.WorstLongFrames);
    }

    [Fact]
    public void FailsWhenP95IsTooHigh()
    {
        // Every frame takes two refresh intervals (i.e. the animation runs at half rate).
        var halfRate = FramePacing.AnalyzeTransition(Frames(Enumerable.Repeat(2 * Hz120, 20).ToArray()), Hz120);

        var verdict = FramePacing.Evaluate(Enumerable.Repeat(halfRate, 50).ToArray(), Hz120);

        Assert.Equal(Outcome.Fail, verdict.Outcome);
    }

    [Fact]
    public void TooFewTransitionsIsInconclusiveEvenIfSmooth()
    {
        var smooth = FramePacing.AnalyzeTransition(Frames(Enumerable.Repeat(Hz120, 40).ToArray()), Hz120);

        Assert.Equal(Outcome.Inconclusive, FramePacing.Evaluate(Enumerable.Repeat(smooth, 49).ToArray(), Hz120).Outcome);
        Assert.Equal(Outcome.NotRun, FramePacing.Evaluate(Array.Empty<TransitionPacing>(), Hz120).Outcome);
    }
}
