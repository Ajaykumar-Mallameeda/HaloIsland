namespace Halo.Spikes.Measurement;

/// <summary>Descriptive statistics for spike reports.</summary>
public static class Statistics
{
    /// <summary>
    /// Nearest-rank percentile: the smallest value such that at least <paramref name="percentile"/> % of the
    /// values are less than or equal to it. Never interpolates, so the result is always an observed value.
    /// </summary>
    public static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        if (double.IsNaN(percentile) || percentile <= 0 || percentile > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be in (0, 100].");
        }

        var sorted = values.ToArray();
        if (sorted.Any(v => !double.IsFinite(v)))
        {
            throw new ArgumentException("Values must be finite.", nameof(values));
        }

        Array.Sort(sorted);

        // Multiply before dividing so that whole-number ranks (e.g. 95 * 20 / 100 = 19) stay exact.
        var rank = (int)Math.Ceiling(percentile * sorted.Length / 100.0);
        return sorted[Math.Max(rank, 1) - 1];
    }

    public static double Median(IReadOnlyCollection<double> values) => Percentile(values, 50);
}
