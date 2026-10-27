namespace Halo.Spikes.Measurement;

/// <summary>Cumulative context-switch counts per thread ID of one process at one sample time.</summary>
public sealed record ThreadSample(double TimeSeconds, IReadOnlyDictionary<int, long> CountsByThreadId);

/// <param name="ThreadChurn">True when the set of thread IDs changed between samples. PB-02 cannot be bounded then
/// (a thread that starts and ends between samples is invisible), so the run is INVALID for PB-02.</param>
public sealed record ContextSwitchResult(long TotalSwitches, double WindowSeconds, double PerSecond, bool ThreadChurn);

/// <summary>PB-02 aggregation (PROTOTYPE_S1_S2_REPORT §8.6).</summary>
public static class ContextSwitches
{
    public static ContextSwitchResult Aggregate(IReadOnlyList<ThreadSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count < 2)
        {
            throw new ArgumentException("At least two samples are required.", nameof(samples));
        }

        var window = samples[^1].TimeSeconds - samples[0].TimeSeconds;
        if (!double.IsFinite(window) || window <= 0)
        {
            throw new ArgumentException("Sample times must increase.", nameof(samples));
        }

        var firstIds = samples[0].CountsByThreadId.Keys.ToHashSet();
        var churn = false;
        long total = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i].TimeSeconds <= samples[i - 1].TimeSeconds)
            {
                throw new ArgumentException("Sample times must increase.", nameof(samples));
            }

            var previous = samples[i - 1].CountsByThreadId;
            var current = samples[i].CountsByThreadId;
            churn |= !firstIds.SetEquals(current.Keys);
            foreach (var (threadId, count) in current)
            {
                // A thread first seen in this sample started inside the interval, so all its switches happened here.
                total += previous.TryGetValue(threadId, out var before) ? Math.Max(0, count - before) : Math.Max(0, count);
            }
        }

        return new ContextSwitchResult(total, window, total / window, churn);
    }
}
