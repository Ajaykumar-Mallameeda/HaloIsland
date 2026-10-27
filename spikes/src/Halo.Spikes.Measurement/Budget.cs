namespace Halo.Spikes.Measurement;

/// <summary>An upper-bound performance budget from ADR-001 §9 (a measured value passes when it is at most <see cref="Limit"/>).</summary>
public sealed record Budget(string Id, string Metric, string Unit, double Limit)
{
    /// <summary>Compares a measurement with the limit. A missing or non-finite measurement is <see cref="Outcome.NotRun"/>.</summary>
    public Outcome Evaluate(double? measured)
    {
        if (measured is not { } value || !double.IsFinite(value))
        {
            return Outcome.NotRun;
        }

        return value <= Limit ? Outcome.Pass : Outcome.Fail;
    }
}

/// <summary>
/// Budgets copied from ADR-001 §9. Values must only change through an approved ADR update,
/// never to make a spike pass. PB-05 (frame pacing) has two conditions and is evaluated by <see cref="FramePacing"/>.
/// </summary>
public static class Budgets
{
    public static readonly Budget Pb01SettledIdleCpu = new("PB-01", "Process CPU time, settled idle", "s per 10 min", 0.5);
    public static readonly Budget Pb02SettledWakeups = new("PB-02", "Context switches into process threads, settled idle", "per s (average)", 2);
    public static readonly Budget Pb03SettledFrames = new("PB-03", "Frames presented, settled", "per s", 0);
    public static readonly Budget Pb06HoverToFirstFrame = new("PB-06", "Hover intent to first animation frame", "ms", 50);
    public static readonly Budget Pb09WarmStartup = new("PB-09", "Startup to island visible, warm (p50 of 10)", "s", 1.0);
    public static readonly Budget Pb10PrivateBytes = new("PB-10", "Private bytes after 10 min idle", "MB", 120);
    public static readonly Budget Pb12SettledGpu = new("PB-12", "GPU 3D engine utilisation, settled", "%", 0.5);
}
