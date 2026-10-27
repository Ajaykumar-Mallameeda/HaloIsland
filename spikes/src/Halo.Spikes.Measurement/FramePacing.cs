namespace Halo.Spikes.Measurement;

/// <summary>Frame intervals observed during one expand or collapse transition.</summary>
public sealed record TransitionPacing(IReadOnlyList<double> IntervalsMs, int LongFrames, double MaxIntervalMs);

/// <summary>PB-05 verdict over a set of transitions at one refresh rate.</summary>
public sealed record PacingVerdict(int Transitions, double P95IntervalMs, int WorstLongFrames, Outcome Outcome);

/// <summary>
/// PB-05 (ADR-001 §9): at most one frame interval longer than 1.5 x the refresh interval per transition,
/// and a 95th-percentile frame interval of at most 1.5 x the refresh interval, over 50 transitions.
/// This class only evaluates timestamps; how they are captured is defined by the spike protocol.
/// </summary>
public static class FramePacing
{
    public const double LongFrameFactor = 1.5;
    public const int MaxLongFramesPerTransition = 1;
    public const int RequiredTransitions = 50;

    /// <param name="presentTimesMs">Present (or display) timestamps of consecutive frames, ascending, in milliseconds.</param>
    /// <param name="refreshIntervalMs">Display refresh interval, e.g. 8.333 ms at 120 Hz.</param>
    public static TransitionPacing AnalyzeTransition(IReadOnlyList<double> presentTimesMs, double refreshIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(presentTimesMs);
        ValidateRefreshInterval(refreshIntervalMs);
        if (presentTimesMs.Count < 2)
        {
            throw new ArgumentException("A transition needs at least two frames.", nameof(presentTimesMs));
        }

        var intervals = new double[presentTimesMs.Count - 1];
        for (var i = 1; i < presentTimesMs.Count; i++)
        {
            var interval = presentTimesMs[i] - presentTimesMs[i - 1];
            if (!double.IsFinite(interval) || interval <= 0)
            {
                throw new ArgumentException("Timestamps must be finite and strictly ascending.", nameof(presentTimesMs));
            }

            intervals[i - 1] = interval;
        }

        var threshold = LongFrameFactor * refreshIntervalMs;
        return new TransitionPacing(intervals, intervals.Count(v => v > threshold), intervals.Max());
    }

    /// <summary>Returns <see cref="Outcome.Invalid"/> when fewer than <paramref name="requiredTransitions"/> were measured.</summary>
    public static PacingVerdict Evaluate(
        IReadOnlyCollection<TransitionPacing> transitions,
        double refreshIntervalMs,
        int requiredTransitions = RequiredTransitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        ValidateRefreshInterval(refreshIntervalMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requiredTransitions);

        if (transitions.Count == 0)
        {
            return new PacingVerdict(0, double.NaN, 0, Outcome.NotRun);
        }

        var allIntervals = transitions.SelectMany(t => t.IntervalsMs).ToArray();
        var p95 = Statistics.Percentile(allIntervals, 95);
        var worst = transitions.Max(t => t.LongFrames);

        Outcome outcome;
        if (transitions.Count < requiredTransitions)
        {
            outcome = Outcome.Invalid;
        }
        else
        {
            var pass = p95 <= LongFrameFactor * refreshIntervalMs && worst <= MaxLongFramesPerTransition;
            outcome = pass ? Outcome.Pass : Outcome.Fail;
        }

        return new PacingVerdict(transitions.Count, p95, worst, outcome);
    }

    private static void ValidateRefreshInterval(double refreshIntervalMs)
    {
        if (!double.IsFinite(refreshIntervalMs) || refreshIntervalMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshIntervalMs), refreshIntervalMs, "The refresh interval must be positive.");
        }
    }
}
