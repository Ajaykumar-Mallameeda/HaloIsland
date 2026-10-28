using System.Globalization;
using Halo.Core.Island;

namespace Halo.Core.Audio;

/// <summary>The default output endpoint's master volume (0–1 scalar, as Windows' own slider) and mute state.</summary>
public readonly record struct VolumeReading(float Level, bool Muted)
{
    /// <summary>0–100, rounded like the Windows volume flyout; out-of-range or invalid levels are clamped.</summary>
    public int Percent => (int)Math.Round(Math.Clamp(float.IsFinite(Level) ? Level : 0, 0, 1) * 100, MidpointRounding.AwayFromZero);

    /// <summary>Speaker glyph (Segoe Fluent Icons): mute, or 0–3 waves by level.</summary>
    public string Glyph => Muted ? "" : Percent switch
    {
        0 => "",
        < 34 => "",
        < 67 => "",
        _ => "",
    };

    /// <summary>What a screen reader hears (FR-A11Y-02): the number in words, and the mute state.</summary>
    public string Spoken => Muted
        ? $"Volume muted, {Percent.ToString(CultureInfo.InvariantCulture)} percent"
        : $"Volume {Percent.ToString(CultureInfo.InvariantCulture)} percent";
}

/// <summary>The transient volume pill (FR-SYS-01) as an alert for the shared controller.</summary>
public static class VolumeAlert
{
    /// <summary>One key for every volume change, so a burst of changes updates one pill and restarts its time.</summary>
    public const string Key = "volume";

    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(1500);

    /// <summary>A volume pill that could not be shown within a second (island open, pressed) no longer matters.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(1);

    public static IslandAlert For(VolumeReading reading) => new(
        Key,
        AlertKind.Volume,
        AlertPriority.Normal,
        new AlertContent(reading.Glyph, reading.Muted ? "Muted" : "Volume", reading.Percent / 100.0, reading.Percent.ToString(CultureInfo.InvariantCulture)) { Accessible = reading.Spoken },
        Duration,
        StaleAfter);
}

/// <summary>Which part of the volume row a point is on.</summary>
public enum VolumePart
{
    Mute,
    Track,
}

/// <summary>
/// The volume row at the bottom of the expanded island (M3-F03): a mute button and a slider. All values are canvas
/// pixels. The visible track is thin, but its hit band is 28 DIP tall (FR-A11Y-07: ≥ 24 DIP targets).
/// </summary>
public sealed record VolumeRow(double MuteX, double MuteY, double MuteSize, double TrackLeft, double TrackWidth, double TrackY, double HitHalfHeight, double ValueLeft, double ValueWidth)
{
    /// <summary>Extra height the row adds to the expanded island.</summary>
    public const double HeightDip = 40;

    /// <summary>Row centre below the expanded shape's top edge (under the clock's progress text and the media buttons).</summary>
    public const double CenterDip = 180;

    public const double MuteSizeDip = 32;
    public const double MuteCenterXDip = 40;
    public const double TrackLeftDip = 68;
    public const double TrackWidthDip = 284;
    public const double HitHeightDip = 28;

    /// <summary>The row for a layout, or null when the layout has no volume row.</summary>
    public static VolumeRow? For(IslandLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!layout.VolumeRow)
        {
            return null;
        }

        var e = layout.Expanded;
        var y = e.Top + layout.Dip(CenterDip);
        return new VolumeRow(
            e.Left + layout.Dip(MuteCenterXDip),
            y,
            layout.Dip(MuteSizeDip),
            e.Left + layout.Dip(TrackLeftDip),
            layout.Dip(TrackWidthDip),
            y,
            layout.Dip(HitHeightDip / 2),
            e.Left + layout.Dip(TrackLeftDip + TrackWidthDip + 8),
            layout.Dip(36));
    }

    /// <summary>The part under a canvas point. The slider's band reaches a little past both ends of the track.</summary>
    public VolumePart? HitTest(double x, double y)
    {
        if (Math.Abs(x - MuteX) <= MuteSize / 2 && Math.Abs(y - MuteY) <= MuteSize / 2)
        {
            return VolumePart.Mute;
        }

        var slack = HitHalfHeight / 2;
        return Math.Abs(y - TrackY) <= HitHalfHeight && x >= TrackLeft - slack && x <= TrackLeft + TrackWidth + slack ? VolumePart.Track : null;
    }

    /// <summary>The level for a pointer x on the track, clamped to 0–1 and rounded to whole percent.</summary>
    public float LevelAt(double x) => (float)(Math.Round(Math.Clamp((x - TrackLeft) / TrackWidth, 0, 1) * 100) / 100);

    /// <summary>The x of a level on the track (for the thumb and for tests).</summary>
    public double XFor(double level) => TrackLeft + (Math.Clamp(level, 0, 1) * TrackWidth);
}
