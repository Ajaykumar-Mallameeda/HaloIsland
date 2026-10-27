namespace Halo.Spikes.Measurement;

/// <summary>
/// Result of checking one acceptance criterion (PROTOTYPE_S1_S2_REPORT, outcome vocabulary).
/// Only <see cref="Pass"/> counts as passing.
/// </summary>
public enum Outcome
{
    /// <summary>No valid attempt was made.</summary>
    NotRun,

    /// <summary>Required tooling, hardware, permission or condition was unavailable.</summary>
    Blocked,

    /// <summary>The method or the run failed its validity requirements, or there was too little valid data.</summary>
    Invalid,

    /// <summary>Measured and within the budget.</summary>
    Pass,

    /// <summary>A valid measurement was completed and the criterion was not met.</summary>
    Fail,
}

public static class OutcomeExtensions
{
    /// <summary>Upper-case label used in reports.</summary>
    public static string Label(this Outcome outcome) => outcome switch
    {
        Outcome.NotRun => "NOT RUN",
        Outcome.Blocked => "BLOCKED",
        Outcome.Invalid => "INVALID",
        Outcome.Pass => "PASS",
        Outcome.Fail => "FAIL",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    /// <summary>
    /// Combines outcomes of the same criterion across runs: any FAIL fails; otherwise anything that is not
    /// PASS (INVALID, BLOCKED, NOT RUN) prevents a pass; an empty set is NOT RUN.
    /// </summary>
    public static Outcome Combine(IEnumerable<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var list = outcomes.ToList();
        if (list.Count == 0)
        {
            return Outcome.NotRun;
        }

        if (list.Contains(Outcome.Fail))
        {
            return Outcome.Fail;
        }

        foreach (var weaker in new[] { Outcome.Invalid, Outcome.Blocked, Outcome.NotRun })
        {
            if (list.Contains(weaker))
            {
                return weaker;
            }
        }

        return Outcome.Pass;
    }
}
