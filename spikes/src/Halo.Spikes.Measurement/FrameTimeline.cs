namespace Halo.Spikes.Measurement;

/// <summary>One desktop update attributed to the overlay region (QPC ticks). <paramref name="Coalesced"/> means the
/// capture accumulated more than one desktop frame, so at least one intermediate timestamp is unknown.</summary>
public readonly record struct FrameRecord(long Qpc, bool Coalesced);

/// <summary>A transition as logged by the overlay (QPC ticks).</summary>
public readonly record struct TransitionMark(long StartQpc, long EndQpc);

public sealed record TransitionFrames(bool Valid, string? InvalidReason, IReadOnlyList<double> IntervalsMs, double? FirstFrameLatencyMs, int FrameCount)
{
    public double MaxIntervalMs => IntervalsMs.Count == 0 ? 0 : IntervalsMs.Max();
}

/// <summary>
/// PB-03/PB-05 windowing over attributed desktop updates (PROTOTYPE_S1_S2_REPORT §8.6). Pure functions on
/// timestamps so the rules are unit-tested independently of the capture.
/// </summary>
public static class FrameTimeline
{
    /// <summary>Frames up to this many refresh periods after the logged end still belong to the transition.</summary>
    public const double EndGraceRefreshPeriods = 3;

    public static double ToMs(long qpcTicks, long qpcFrequency) => qpcTicks * 1000.0 / qpcFrequency;

    /// <param name="frames">Attributed frames sorted by <see cref="FrameRecord.Qpc"/>.</param>
    public static TransitionFrames ForTransition(IReadOnlyList<FrameRecord> frames, TransitionMark mark, long qpcFrequency, double refreshMs)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qpcFrequency);
        if (!double.IsFinite(refreshMs) || refreshMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshMs));
        }

        if (mark.EndQpc < mark.StartQpc)
        {
            throw new ArgumentException("Transition end precedes start.", nameof(mark));
        }

        var graceTicks = (long)Math.Ceiling(EndGraceRefreshPeriods * refreshMs * qpcFrequency / 1000.0);
        var inWindow = frames.Where(f => f.Qpc > mark.StartQpc && f.Qpc <= mark.EndQpc + graceTicks).ToList();
        double? latency = inWindow.Count > 0 ? ToMs(inWindow[0].Qpc - mark.StartQpc, qpcFrequency) : null;

        if (inWindow.Any(f => f.Coalesced))
        {
            return new TransitionFrames(false, "coalesced", [], latency, inWindow.Count);
        }

        if (inWindow.Count < 2)
        {
            return new TransitionFrames(false, "too_few_frames", [], latency, inWindow.Count);
        }

        var times = inWindow.Select(f => ToMs(f.Qpc, qpcFrequency)).ToArray();
        var pacing = FramePacing.AnalyzeTransition(times, refreshMs);
        return new TransitionFrames(true, null, pacing.IntervalsMs, latency, inWindow.Count);
    }

    /// <summary>PB-05: evaluates the first <paramref name="required"/> valid transitions; fewer is INVALID.</summary>
    public static PacingVerdict EvaluatePb05(IEnumerable<TransitionFrames> transitions, double refreshMs, int required = FramePacing.RequiredTransitions)
    {
        ArgumentNullException.ThrowIfNull(transitions);
        var valid = transitions
            .Where(t => t.Valid)
            .Take(required)
            .Select(t => new TransitionPacing(t.IntervalsMs, t.IntervalsMs.Count(v => v > FramePacing.LongFrameFactor * refreshMs), t.MaxIntervalMs))
            .ToList();
        return FramePacing.Evaluate(valid, refreshMs, required);
    }

    /// <summary>Number of frames with <paramref name="startQpc"/> &lt; Qpc ≤ <paramref name="endQpc"/>.</summary>
    public static int CountInWindow(IEnumerable<FrameRecord> frames, long startQpc, long endQpc)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return frames.Count(f => f.Qpc > startQpc && f.Qpc <= endQpc);
    }
}
