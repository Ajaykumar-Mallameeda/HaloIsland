namespace Halo.Spikes.Measurement;

/// <summary>Normalises cumulative process CPU time to the PB-01/PB-04 unit (seconds per 10 minutes).</summary>
public static class CpuRate
{
    public static readonly TimeSpan ReferenceWindow = TimeSpan.FromMinutes(10);

    /// <param name="cpuTime">Increase in total process CPU time (user + kernel) over the interval.</param>
    /// <param name="wallTime">Length of the interval.</param>
    public static double SecondsPerTenMinutes(TimeSpan cpuTime, TimeSpan wallTime)
    {
        if (wallTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wallTime), wallTime, "The interval must be positive.");
        }

        if (cpuTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cpuTime), cpuTime, "CPU time cannot decrease.");
        }

        return cpuTime.TotalSeconds * (ReferenceWindow.TotalSeconds / wallTime.TotalSeconds);
    }
}
