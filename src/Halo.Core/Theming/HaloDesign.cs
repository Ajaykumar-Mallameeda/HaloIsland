using Halo.Core.Activities;
using Halo.Core.Island;

namespace Halo.Core.Theming;

/// <summary>
/// What an alert or activity means, for colour (Visual Experience 2.0). Sources say what happened; the palette decides
/// the colour, so every activity uses one accent consistently across its glyph, bar, ring and chip. The meaning is
/// always also in the text and the glyph: colour never carries information alone (FR-A11Y-07).
/// </summary>
public enum Tone
{
    /// <summary>No particular meaning: the theme's bar colour.</summary>
    Neutral,
    Media,
    Timer,
    Focus,
    ShortBreak,
    LongBreak,
    Charging,
    Warning,
    Critical,
    Connected,
    Offline,
    Bluetooth,
    Camera,
    Reminder,
    Notification,
    Quiet,
    Brightness,
    Tasks,
    Shelf,
}

/// <summary>
/// Shared design tokens (Visual Experience 2.0). One place for sizes, type, radii and accents, used by drawing,
/// hit-testing and UI Automation alike. Sizes are DIP; the layout scales them by the monitor DPI.
/// </summary>
public static class HaloDesign
{
    // Typography (Segoe UI Variable). Display sizes are for big readouts, text sizes for everything else.
    public const double ReadoutSize = 40;
    public const double ClockSize = 50;
    public const double HeadlineSize = 20;
    public const double TitleSize = 15.5;
    public const double BodySize = 13;
    public const double CaptionSize = 11.5;
    public const double HeaderSize = 13;
    public const double ChipSize = 12;

    // Shape.
    public const double ChipHeight = 30;
    public const double ChipRadius = 15;
    public const double PanelRadius = 14;
    public const double ArtRadius = 14;
    public const double RingDiameter = 78;
    public const double RingStroke = 6;
    public const double CompactRingDiameter = 15;
    public const double CompactRingStroke = 2.25;
    public const double BarThickness = 5;

    /// <summary>The concave fillet that joins the flush-top island to the top edge of the screen.</summary>
    public const double ShoulderDip = 10;

    // Spacing.
    public const double Inset = 40;
    public const double Gap = 8;

    /// <summary>Accent of a tone on the black island (Apple-like system hues, restrained: one per activity).</summary>
    public static Rgba BaseAccent(Tone tone) => tone switch
    {
        Tone.Media => Rgba.Opaque(255, 55, 95),
        Tone.Timer => Rgba.Opaque(255, 159, 10),
        Tone.Focus => Rgba.Opaque(255, 69, 58),
        Tone.ShortBreak => Rgba.Opaque(48, 209, 88),
        Tone.LongBreak => Rgba.Opaque(100, 210, 255),
        Tone.Charging => Rgba.Opaque(48, 209, 88),
        Tone.Warning => Rgba.Opaque(255, 214, 10),
        Tone.Critical => Rgba.Opaque(255, 69, 58),
        Tone.Connected => Rgba.Opaque(10, 132, 255),
        Tone.Offline => Rgba.Opaque(255, 159, 10),
        Tone.Bluetooth => Rgba.Opaque(10, 132, 255),
        Tone.Camera => Rgba.Opaque(48, 209, 88),
        Tone.Reminder => Rgba.Opaque(255, 214, 10),
        Tone.Notification => Rgba.Opaque(10, 132, 255),
        Tone.Quiet => Rgba.Opaque(191, 90, 242),
        Tone.Brightness => Rgba.Opaque(255, 214, 10),
        Tone.Tasks => Rgba.Opaque(10, 132, 255),
        Tone.Shelf => Rgba.Opaque(100, 210, 255),
        _ => Rgba.Opaque(255, 255, 255),
    };

    /// <summary>
    /// The accent for a tone in a palette: neutral uses the palette's bar colour (which follows the user's accent
    /// setting), high contrast uses the system highlight for everything, and other themes adjust the hue until it stands
    /// out from the fill by 3:1 (WCAG non-text contrast).
    /// </summary>
    public static Rgba Accent(Tone tone, IslandPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.HighContrast || tone == Tone.Neutral)
        {
            return palette.Bar;
        }

        return IslandPalette.ReadableAccent(BaseAccent(tone), palette.Fill);
    }

    /// <summary>The tone an alert shows: what its source said, else the usual meaning of its kind.</summary>
    public static Tone ToneOf(IslandAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (alert.Content.Tone != Tone.Neutral)
        {
            return alert.Content.Tone;
        }

        return alert.Kind switch
        {
            AlertKind.Timer => Tone.Timer,
            AlertKind.Reminder => Tone.Reminder,
            AlertKind.Notification => Tone.Notification,
            AlertKind.Brightness => Tone.Brightness,
            AlertKind.Media => Tone.Media,
            _ => Tone.Neutral,
        };
    }

    /// <summary>The tone an activity shows: what its source said, else the usual meaning of its kind.</summary>
    public static Tone ToneOf(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Tone != Tone.Neutral)
        {
            return activity.Tone;
        }

        return activity.Kind switch
        {
            ActivityKind.Timer => Tone.Timer,
            ActivityKind.Focus => Tone.Focus,
            ActivityKind.Reminder => Tone.Reminder,
            ActivityKind.Task => Tone.Tasks,
            ActivityKind.Notification or ActivityKind.Calendar => Tone.Notification,
            ActivityKind.Media => Tone.Media,
            ActivityKind.Power => Tone.Charging,
            ActivityKind.Network => Tone.Offline,
            _ => Tone.Neutral,
        };
    }

    /// <summary>
    /// A restrained accent from album art (premultiplied BGRA, as Windows delivers thumbnails): the average of the
    /// colourful pixels, so a mostly grey cover falls back to <paramref name="fallback"/>. Samples a grid of at most
    /// 32 × 32 pixels, so the cost is fixed whatever the art size, and runs once per new picture.
    /// </summary>
    public static Rgba ArtAccent(ReadOnlySpan<byte> bgra, int size, Rgba fallback)
    {
        if (size <= 0 || bgra.Length < size * size * 4)
        {
            return fallback;
        }

        var step = Math.Max(1, size / 32);
        double r = 0, g = 0, b = 0, weight = 0;
        for (var y = 0; y < size; y += step)
        {
            for (var x = 0; x < size; x += step)
            {
                var i = ((y * size) + x) * 4;
                var a = bgra[i + 3];
                if (a < 128)
                {
                    continue;
                }

                // Un-premultiply, then weight by saturation so greys, blacks and whites do not wash the colour out.
                double pb = bgra[i] * 255.0 / a, pg = bgra[i + 1] * 255.0 / a, pr = bgra[i + 2] * 255.0 / a;
                var max = Math.Max(pr, Math.Max(pg, pb));
                var min = Math.Min(pr, Math.Min(pg, pb));
                var saturation = max <= 0 ? 0 : (max - min) / max;
                var w = saturation * saturation * (max / 255.0);
                r += pr * w;
                g += pg * w;
                b += pb * w;
                weight += w;
            }
        }

        if (weight < 0.5)
        {
            return fallback;
        }

        static byte C(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        var colour = Rgba.Opaque(C(r / weight), C(g / weight), C(b / weight));

        // Lift very dark averages so the accent reads as a colour on the black island.
        var peak = Math.Max(colour.R, Math.Max(colour.G, colour.B));
        if (peak is > 0 and < 170)
        {
            var k = 170.0 / peak;
            colour = Rgba.Opaque(C(colour.R * k), C(colour.G * k), C(colour.B * k));
        }

        return colour;
    }
}
