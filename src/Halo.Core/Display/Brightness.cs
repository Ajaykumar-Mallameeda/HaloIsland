using System.Globalization;
using Halo.Core.Island;

namespace Halo.Core.Display;

/// <summary>
/// The brightness pill (J-F03): read-only. When the built-in panel's brightness changes (keys, Action Center, power
/// plan), a pill shows the new level, coalesced like the volume pill. The first reading only records the level; equal
/// readings show nothing. Halo never changes brightness.
/// </summary>
public sealed class BrightnessMonitor
{
    public const string Key = "brightness";
    private const string Glyph = "";

    /// <summary>Last level, 0–100, or null before the first reading.</summary>
    public int? Current { get; private set; }

    public IslandAlert? Update(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        var previous = Current;
        Current = percent;
        return previous is null || previous == percent ? null : Alert(percent);
    }

    public void Reset() => Current = null;

    public static IslandAlert Alert(int percent) => new(
        Key,
        AlertKind.Brightness,
        AlertPriority.Normal,
        new AlertContent(Glyph, "Brightness", percent / 100.0, percent.ToString(CultureInfo.CurrentCulture))
        {
            Accessible = string.Create(CultureInfo.CurrentCulture, $"Brightness {percent} percent"),
        },
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromSeconds(1));
}
