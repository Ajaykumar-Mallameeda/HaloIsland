namespace Halo.Spikes.Measurement;

/// <summary>Cumulative context-switch counts per thread ID of one process at one sample time.</summary>
public sealed record ThreadSample(double TimeSeconds, IReadOnlyDictionary<int, long> CountsByThreadId);

/// <param name="NewThreadStarted">True when a thread ID absent from the first sample appears later, or a known ID's
/// count goes down (ID reuse). A new thread could also start and end between samples unseen, so PB-02 is INVALID
/// (protocol v1.3, PROTOTYPE_S1_S2_REPORT §8.6).</param>
/// <param name="ThreadsEnded">Thread IDs from the first sample that are gone by the last one. Their switches after
/// their last sample are not counted; this is reported as a limitation and does not invalidate PB-02.</param>
public sealed record ContextSwitchResult(long TotalSwitches, double WindowSeconds, double PerSecond, bool NewThreadStarted, int ThreadsEnded);

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
        var newThread = false;
        long total = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            if (samples[i].TimeSeconds <= samples[i - 1].TimeSeconds)
            {
                throw new ArgumentException("Sample times must increase.", nameof(samples));
            }

            var previous = samples[i - 1].CountsByThreadId;
            var current = samples[i].CountsByThreadId;
            foreach (var (threadId, count) in current)
            {
                newThread |= !firstIds.Contains(threadId) || (previous.TryGetValue(threadId, out var earlier) && count < earlier);
                // A thread first seen in this sample started inside the interval, so all its switches happened here.
                total += previous.TryGetValue(threadId, out var before) ? Math.Max(0, count - before) : Math.Max(0, count);
            }
        }

        var ended = firstIds.Count(id => !samples[^1].CountsByThreadId.ContainsKey(id));
        return new ContextSwitchResult(total, window, total / window, newThread, ended);
    }
}
