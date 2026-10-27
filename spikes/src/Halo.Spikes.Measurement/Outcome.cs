namespace Halo.Spikes.Measurement;

/// <summary>Result of checking one acceptance criterion. Only <see cref="Pass"/> counts as passing.</summary>
public enum Outcome
{
    /// <summary>The measurement was not taken (tool, hardware or condition unavailable).</summary>
    NotRun,

    /// <summary>Measured, but too little or too unreliable data to decide.</summary>
    Inconclusive,

    /// <summary>Measured and within the budget.</summary>
    Pass,

    /// <summary>Measured and outside the budget.</summary>
    Fail,
}
