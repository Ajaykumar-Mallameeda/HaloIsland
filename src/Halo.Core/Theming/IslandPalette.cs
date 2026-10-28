namespace Halo.Core.Theming;

/// <summary>Which theme the island uses (I-F01).</summary>
public enum ThemeMode
{
    /// <summary>The original black island (default).</summary>
    Dark,
    Light,

    /// <summary>Follows Windows' app mode (Settings › Personalisation › Colours).</summary>
    System,
}

/// <summary>An sRGB colour with alpha (0–255 each). Kept in Core so palettes are testable without Windows types.</summary>
public readonly record struct Rgba(byte A, byte R, byte G, byte B)
{
    public static Rgba Opaque(byte r, byte g, byte b) => new(255, r, g, b);

    /// <summary>This colour composited over an opaque background.</summary>
    public Rgba Over(Rgba background)
    {
        var a = A / 255.0;
        byte Mix(byte f, byte b) => (byte)Math.Round((f * a) + (b * (1 - a)));
        return Opaque(Mix(R, background.R), Mix(G, background.G), Mix(B, background.B));
    }

    /// <summary>WCAG 2.x relative luminance of the opaque colour.</summary>
    public double Luminance
    {
        get
        {
            static double C(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * C(R)) + (0.7152 * C(G)) + (0.0722 * C(B));
        }
    }

    /// <summary>WCAG contrast ratio between two opaque colours (1 to 21).</summary>
    public static double Contrast(Rgba a, Rgba b)
    {
        var (l1, l2) = (Math.Max(a.Luminance, b.Luminance), Math.Min(a.Luminance, b.Luminance));
        return (l1 + 0.05) / (l2 + 0.05);
    }
}

/// <summary>System colours used in high-contrast mode (from GetSysColor), so the island follows the user's theme.</summary>
public sealed record HighContrastColors(Rgba Window, Rgba WindowText, Rgba Highlight, Rgba GrayText);

/// <summary>
/// Every colour the island draws with (I-F01). Text and glyph colours keep at least 4.5:1 contrast against the fill
/// (FR-A11Y-07), which <see cref="MeetsContrast"/> checks. Information never depends on colour alone.
/// </summary>
public sealed record IslandPalette(
    Rgba Fill,
    Rgba HoverFill,
    Rgba Edge,
    Rgba HoverEdge,
    Rgba Primary,
    Rgba Secondary,
    Rgba Tertiary,
    Rgba Track,
    Rgba Bar)
{
    public static IslandPalette Dark { get; } = new(
        Rgba.Opaque(0, 0, 0),
        Rgba.Opaque(22, 22, 24),
        new Rgba(40, 255, 255, 255),
        new Rgba(72, 255, 255, 255),
        Rgba.Opaque(255, 255, 255),
        new Rgba(184, 255, 255, 255),
        new Rgba(140, 255, 255, 255),
        new Rgba(46, 255, 255, 255),
        new Rgba(230, 255, 255, 255));

    public static IslandPalette Light { get; } = new(
        Rgba.Opaque(246, 246, 248),
        Rgba.Opaque(234, 234, 238),
        new Rgba(48, 0, 0, 0),
        new Rgba(80, 0, 0, 0),
        Rgba.Opaque(20, 20, 22),
        new Rgba(196, 0, 0, 0),
        new Rgba(150, 0, 0, 0),
        new Rgba(36, 0, 0, 0),
        new Rgba(220, 20, 20, 22));

    /// <summary>The palette for the settings and the system state.</summary>
    /// <param name="mode">The user's theme choice.</param>
    /// <param name="systemIsLight">Windows' app mode is light (only used for <see cref="ThemeMode.System"/>).</param>
    /// <param name="accent">The system accent colour when the user wants it for bars, else null.</param>
    /// <param name="highContrast">System colours while high contrast is on (overrides everything), else null.</param>
    public static IslandPalette For(ThemeMode mode, bool systemIsLight, Rgba? accent, HighContrastColors? highContrast)
    {
        if (highContrast is { } hc)
        {
            return new IslandPalette(hc.Window, hc.Window, hc.WindowText, hc.Highlight, hc.WindowText, hc.WindowText, hc.WindowText, hc.GrayText, hc.Highlight);
        }

        var light = mode == ThemeMode.Light || (mode == ThemeMode.System && systemIsLight);
        var basis = light ? Light : Dark;
        return accent is { } a ? basis with { Bar = ReadableAccent(a, basis.Fill) } : basis;
    }

    /// <summary>True when every text colour reaches 4.5:1 against the fill (also over the hover fill).</summary>
    public bool MeetsContrast(double minimum = 4.5) =>
        new[] { Primary, Secondary, Tertiary }.All(t => Rgba.Contrast(t.Over(Fill), Fill) >= minimum && Rgba.Contrast(t.Over(HoverFill), HoverFill) >= minimum);

    /// <summary>
    /// The accent for bars, darkened or lightened until it stands out from the fill by 3:1 (WCAG non-text contrast),
    /// so a pale accent on the light island or a dark one on the black island stays visible.
    /// </summary>
    public static Rgba ReadableAccent(Rgba accent, Rgba fill)
    {
        var c = Rgba.Opaque(accent.R, accent.G, accent.B);
        var towardsWhite = fill.Luminance < 0.5;
        for (var i = 0; i < 20 && Rgba.Contrast(c, fill) < 3; i++)
        {
            byte Step(byte v) => (byte)Math.Clamp(towardsWhite ? v + 16 : v - 16, 0, 255);
            c = Rgba.Opaque(Step(c.R), Step(c.G), Step(c.B));
        }

        return c;
    }
}
