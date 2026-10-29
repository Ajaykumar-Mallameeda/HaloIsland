namespace Halo.Core.Island;

/// <summary>Axis-aligned rectangle in physical pixels (right and bottom exclusive).</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>Rounded rectangle in pixels relative to the island canvas origin.</summary>
public readonly record struct RoundedBox(double Left, double Top, double Width, double Height, double Radius)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double CenterX => Left + (Width / 2);

    /// <summary>True when the point lies inside the rounded outline (edges count as inside).</summary>
    public bool Contains(double x, double y)
    {
        var halfW = Width / 2;
        var halfH = Height / 2;
        var r = Math.Min(Radius, Math.Min(halfW, halfH));
        var qx = Math.Abs(x - (Left + halfW)) - (halfW - r);
        var qy = Math.Abs(y - (Top + halfH)) - (halfH - r);
        if (qx <= 0 || qy <= 0)
        {
            return qx <= r && qy <= r;
        }

        return (qx * qx) + (qy * qy) <= r * r;
    }

    /// <summary>
    /// Hit test for the floating island (Live Activity Engine 2.0): the shape plus the gap above it, up to the top edge
    /// of the canvas. A pointer pushed against the top of the screen therefore always lands on the island, and moving
    /// between the gap and the shape never counts as leaving it.
    /// </summary>
    public bool ContainsWithGap(double x, double y)
    {
        var r = Math.Min(Radius, Math.Min(Width / 2, Height / 2));
        return (x >= Left && x <= Right && y >= 0 && y <= Top + r) || Contains(x, y);
    }

    /// <summary>
    /// The input region of the shape: the shape extended up to the canvas top (its top corners lie above the window, so
    /// the region's top edge is square and covers the gap; the lower corners stay round).
    /// </summary>
    public RoundedBox Reach()
    {
        var r = Math.Min(Radius, Height / 2);
        return this with { Top = -r, Height = Bottom + r };
    }
}

/// <summary>
/// Island geometry for one monitor (ADR-001 §4.3, Live Activity Engine 2.0): a fixed canvas sized once for the largest
/// presentation and centred under the top edge of the work area; the visible shape animates inside it. The island
/// floats: every shape keeps a gap below the top edge, and the idle line sits inside that gap. All values are physical
/// pixels; design values are in DIP and scaled by <c>dpi / 96</c>.
/// </summary>
/// <param name="VolumeRow">The expanded shape has the volume row at the bottom (M3-F03), which makes it taller.</param>
public sealed record IslandLayout(PixelRect Canvas, RoundedBox Compact, RoundedBox Expanded, RoundedBox Alert, double Scale, bool VolumeRow = false)
{
    /// <summary>Gap between the top edge of the work area and the floating island.</summary>
    public const double TopGapDip = 8;

    /// <summary>Compact pill: Apple's compact island is 36.67 pt tall; Halo uses 38 DIP so desktop text fits.</summary>
    public const double CompactWidthDip = 236;
    public const double CompactHeightDip = 38;
    public const double ExpandedWidthDip = 460;
    public const double ExpandedHeightDip = 196;
    public const double ExpandedRadiusDip = 40;

    /// <summary>Transient alert pill: wider than compact, far smaller than expanded, inside the expanded shape.</summary>
    public const double AlertWidthDip = 320;
    public const double AlertHeightDip = 44;

    /// <summary>Idle line: its length, and the top of the line inside the gap.</summary>
    public const double LineWidthDip = 76;
    public const double LineTopDip = 4;

    /// <summary>Line thickness in DIP, rounded to whole pixels and kept within 2–4 physical pixels.</summary>
    public const double LineThicknessDip = 2;

    /// <summary>
    /// Pointer zone of the idle line: wider and taller than the line so it is easy to reach, but only a small band at
    /// the very top centre, so it never takes clicks meant for the windows below.
    /// </summary>
    public const double LineHitWidthDip = 168;
    public const double LineHitHeightDip = 12;

    /// <summary>
    /// Apple's minimal presentation: a second activity shown detached beside the compact island, a circle as tall as
    /// the compact pill, <see cref="MinimalGapDip"/> away from it.
    /// </summary>
    public const double MinimalGapDip = 6;

    /// <summary>Spare room around the expanded shape, so the expand overshoot and the press scale never clip.</summary>
    public const double CanvasMarginDip = 14;

    /// <summary>The idle line (a capsule).</summary>
    public RoundedBox Line { get; init; }

    /// <summary>The idle line's pointer zone, from the top edge of the canvas.</summary>
    public RoundedBox LineHit { get; init; }

    /// <summary>The detached minimal bubble at the right of the compact pill.</summary>
    public RoundedBox Minimal { get; init; }

    /// <param name="workArea">The chosen display's work area in physical pixels.</param>
    /// <param name="dpi">That display's DPI.</param>
    /// <param name="volumeRow">The expanded shape has the volume row.</param>
    /// <param name="offsetDip">
    /// I-F04: horizontal offset from the centre in DIP (negative = left). The canvas is clamped so the whole island
    /// always stays inside the work area, whatever the offset or the display size.
    /// </param>
    public static IslandLayout Compute(PixelRect workArea, int dpi, bool volumeRow = false, int offsetDip = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 48);
        var scale = dpi / 96.0;
        var expandedHeight = ExpandedHeightDip + (volumeRow ? Audio.VolumeRow.HeightDip : 0);
        var width = Px(ExpandedWidthDip + (2 * CanvasMarginDip), scale);
        var height = Px(TopGapDip + expandedHeight + CanvasMarginDip, scale);
        if (width > workArea.Width || height > workArea.Height)
        {
            throw new ArgumentException("The work area is smaller than the island canvas.", nameof(workArea));
        }

        var centred = workArea.Left + ((workArea.Width - width) / 2);
        var left = Math.Clamp(centred + Px(offsetDip, scale), workArea.Left, workArea.Right - width);
        var canvas = new PixelRect(left, workArea.Top, width, height);
        var compact = Shape(width, CompactWidthDip, CompactHeightDip, CompactHeightDip, scale);
        var thickness = Math.Clamp(Px(LineThicknessDip, scale), 2, 4);
        var lineWidth = Px(LineWidthDip, scale);
        var hitWidth = Px(LineHitWidthDip, scale);
        return new IslandLayout(
            canvas,
            compact,
            Shape(width, ExpandedWidthDip, expandedHeight, ExpandedRadiusDip, scale),
            Shape(width, AlertWidthDip, AlertHeightDip, AlertHeightDip, scale),
            scale,
            volumeRow)
        {
            Line = new RoundedBox((width - lineWidth) / 2, Px(LineTopDip, scale), lineWidth, thickness, thickness / 2.0),
            LineHit = new RoundedBox((width - hitWidth) / 2, 0, hitWidth, Px(LineHitHeightDip, scale), 0),
            Minimal = new RoundedBox(compact.Right + Px(MinimalGapDip, scale), compact.Top, compact.Height, compact.Height, compact.Height / 2.0),
        };
    }

    public RoundedBox ShapeFor(IslandPresentation presentation) => presentation switch
    {
        IslandPresentation.Expanded => Expanded,
        IslandPresentation.Alert => Alert,
        IslandPresentation.Line => Line,
        _ => Compact,
    };

    /// <summary>True when a canvas point is on the island in this presentation (the line uses its larger pointer zone).</summary>
    public bool Hit(IslandPresentation presentation, double x, double y) =>
        presentation == IslandPresentation.Line ? LineHit.Contains(x, y) : ShapeFor(presentation).ContainsWithGap(x, y);

    /// <summary>The input region box of a presentation (see <see cref="RoundedBox.Reach"/>).</summary>
    public RoundedBox RegionFor(IslandPresentation presentation) =>
        presentation == IslandPresentation.Line ? LineHit : ShapeFor(presentation).Reach();

    /// <summary>Converts a design value in DIP to pixels at this layout's scale.</summary>
    public double Dip(double dip) => dip * Scale;

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Shapes are anchored top-centre in the canvas below the gap; sizes are whole pixels so edges stay crisp. The radius
    /// is capped at half the height, so the compact shape (radius = height) is an exact pill.
    /// </summary>
    private static RoundedBox Shape(int canvasWidth, double widthDip, double heightDip, double radiusDip, double scale)
    {
        var w = Px(widthDip, scale);
        var h = Px(heightDip, scale);
        return new RoundedBox((canvasWidth - w) / 2, Px(TopGapDip, scale), w, h, Math.Min(radiusDip * scale, h / 2.0));
    }
}
