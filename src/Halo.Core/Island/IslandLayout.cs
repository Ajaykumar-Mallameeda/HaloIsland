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
    /// Hit test for the flush-top island (Visual Experience 2.0): the top corners are square because the shape meets the
    /// top edge of the work area, so the band from the top to the start of the lower corners counts as inside.
    /// </summary>
    public bool ContainsSquareTop(double x, double y)
    {
        var r = Math.Min(Radius, Math.Min(Width / 2, Height / 2));
        return (x >= Left && x <= Right && y >= Top && y <= Top + r) || Contains(x, y);
    }

    /// <summary>
    /// The rounded rectangle actually drawn: the visible box extended upwards by its radius, so its top corners lie above
    /// the window and only the lower corners show. The top edge of the work area clips it.
    /// </summary>
    public RoundedBox Drawn() => this with { Top = Top - Math.Min(Radius, Height / 2), Height = Height + Math.Min(Radius, Height / 2) };
}

/// <summary>
/// Island geometry for one monitor (ADR-001 §4.3): a fixed canvas sized once for the largest presentation and
/// centred under the top edge of the work area; the visible shape animates inside it. All values are physical
/// pixels; design values are in DIP and scaled by <c>dpi / 96</c>.
/// </summary>
/// <param name="VolumeRow">The expanded shape has the volume row at the bottom (M3-F03), which makes it taller.</param>
public sealed record IslandLayout(PixelRect Canvas, RoundedBox Compact, RoundedBox Expanded, RoundedBox Alert, double Scale, bool VolumeRow = false)
{
    /// <summary>
    /// Visual Experience 2.0: the island is flush with the top edge of the work area (no gap), joined to it by concave
    /// shoulders, like a notch that grows out of the screen edge.
    /// </summary>
    public const double TopGapDip = 0;
    public const double CompactWidthDip = 236;
    public const double CompactHeightDip = 36;
    public const double ExpandedWidthDip = 460;
    public const double ExpandedHeightDip = 196;
    public const double ExpandedRadiusDip = 34;

    /// <summary>Transient alert pill: wider than compact, far smaller than expanded, inside the expanded shape.</summary>
    public const double AlertWidthDip = 320;
    public const double AlertHeightDip = 42;

    /// <summary>Spare room around the expanded shape for its shoulders and so the pressed-state scale never clips.</summary>
    public const double CanvasMarginDip = 14;

    /// <summary>Width (and height) of each concave shoulder in pixels at this layout's scale.</summary>
    public double Shoulder => Math.Round(Theming.HaloDesign.ShoulderDip * Scale, MidpointRounding.AwayFromZero);

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
        return new IslandLayout(
            canvas,
            Shape(width, CompactWidthDip, CompactHeightDip, CompactHeightDip, scale),
            Shape(width, ExpandedWidthDip, expandedHeight, ExpandedRadiusDip, scale),
            Shape(width, AlertWidthDip, AlertHeightDip, AlertHeightDip, scale),
            scale,
            volumeRow);
    }

    public RoundedBox ShapeFor(IslandPresentation presentation) => presentation switch
    {
        IslandPresentation.Expanded => Expanded,
        IslandPresentation.Alert => Alert,
        _ => Compact,
    };

    /// <summary>Converts a design value in DIP to pixels at this layout's scale.</summary>
    public double Dip(double dip) => dip * Scale;

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Shapes are anchored top-centre in the canvas; sizes are whole pixels so edges stay crisp. The radius is capped
    /// at half the height, so the compact shape (radius = height) is an exact pill.
    /// </summary>
    private static RoundedBox Shape(int canvasWidth, double widthDip, double heightDip, double radiusDip, double scale)
    {
        var w = Px(widthDip, scale);
        var h = Px(heightDip, scale);
        return new RoundedBox((canvasWidth - w) / 2, Px(TopGapDip, scale), w, h, Math.Min(radiusDip * scale, h / 2.0));
    }
}
