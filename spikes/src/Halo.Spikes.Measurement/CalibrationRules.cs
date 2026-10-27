namespace Halo.Spikes.Measurement;

/// <summary>
/// Predeclared calibration rules (PROTOTYPE_S1_S2_REPORT §8.7). They were fixed before any calibration run and must
/// not be tuned to results. The method is valid at a refresh rate only if BASE, POS, one NEG/NEGCTL pair and every
/// TICK run pass.
/// </summary>
public static class CalibrationRules
{
    public const double StallGapMs = 40;
    public const int NegativeControlMaxGapTransitions = 2;
    public const double PositiveMinRateFraction = 0.9;
    public const double PositiveMedianLow = 0.9;
    public const double PositiveMedianHigh = 1.1;
    public const double PositiveMaxCoalescedFraction = 0.01;
    public const double TickMatchWindowMs = 250;

    /// <summary>BASE: the attribution region must be quiet with no overlay.</summary>
    public static Outcome Baseline(int attributedFrames) => attributedFrames == 0 ? Outcome.Pass : Outcome.Fail;

    /// <summary>POS: continuous animation is seen at the refresh cadence.</summary>
    public static Outcome Positive(IReadOnlyList<FrameRecord> frames, double windowSeconds, double refreshHz, long qpcFrequency)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count < 2 || windowSeconds <= 0)
        {
            return Outcome.Fail;
        }

        var refreshMs = 1000.0 / refreshHz;
        var intervals = new List<double>();
        for (var i = 1; i < frames.Count; i++)
        {
            intervals.Add(FrameTimeline.ToMs(frames[i].Qpc - frames[i - 1].Qpc, qpcFrequency));
        }

        var rate = frames.Count / windowSeconds;
        var median = Statistics.Median(intervals);
        var coalescedFraction = frames.Count(f => f.Coalesced) / (double)frames.Count;
        var pass = rate >= PositiveMinRateFraction * refreshHz
            && median >= PositiveMedianLow * refreshMs
            && median <= PositiveMedianHigh * refreshMs
            && coalescedFraction <= PositiveMaxCoalescedFraction;
        return pass ? Outcome.Pass : Outcome.Fail;
    }

    /// <summary>NEG: every stalled transition must show a gap of at least <see cref="StallGapMs"/>.
    /// Any invalid transition makes the run INVALID (rerun rule §8.9).</summary>
    public static Outcome Negative(IReadOnlyCollection<TransitionFrames> stalled, int expectedTransitions)
    {
        ArgumentNullException.ThrowIfNull(stalled);
        if (stalled.Count != expectedTransitions || stalled.Any(t => !t.Valid))
        {
            return Outcome.Invalid;
        }

        return stalled.All(t => t.MaxIntervalMs >= StallGapMs) ? Outcome.Pass : Outcome.Fail;
    }

    /// <summary>NEGCTL: without the stall, at most <see cref="NegativeControlMaxGapTransitions"/> transitions show such a gap.</summary>
    public static Outcome NegativeControl(IReadOnlyCollection<TransitionFrames> control, int expectedTransitions)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (control.Count != expectedTransitions || control.Any(t => !t.Valid))
        {
            return Outcome.Invalid;
        }

        return control.Count(t => t.MaxIntervalMs >= StallGapMs) <= NegativeControlMaxGapTransitions ? Outcome.Pass : Outcome.Fail;
    }

    /// <summary>TICK: every logged tick has an attributed frame within the match window after it, and no attributed
    /// frame lies outside such a window (i.e. stray frames are detected one by one).</summary>
    public static Outcome Tick(IReadOnlyList<long> tickQpcs, IReadOnlyList<FrameRecord> frames, long qpcFrequency)
    {
        ArgumentNullException.ThrowIfNull(tickQpcs);
        ArgumentNullException.ThrowIfNull(frames);
        if (tickQpcs.Count == 0)
        {
            return Outcome.Invalid;
        }

        var window = (long)(TickMatchWindowMs * qpcFrequency / 1000.0);
        var everyTickSeen = tickQpcs.All(t => frames.Any(f => f.Qpc >= t && f.Qpc <= t + window));
        var noStrayFrame = frames.All(f => tickQpcs.Any(t => f.Qpc >= t && f.Qpc <= t + window));
        return everyTickSeen && noStrayFrame ? Outcome.Pass : Outcome.Fail;
    }
}
