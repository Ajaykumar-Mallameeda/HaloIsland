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
}

/// <summary>
/// Island geometry for one monitor (ADR-001 §4.3): a fixed canvas sized once for the largest presentation and
/// centred under the top edge of the work area; the visible shape animates inside it. All values are physical
/// pixels; design values are in DIP and scaled by <c>dpi / 96</c>.
/// </summary>
public sealed record IslandLayout(PixelRect Canvas, RoundedBox Compact, RoundedBox Expanded, double Scale)
{
    public const double TopGapDip = 6;
    public const double CompactWidthDip = 236;
    public const double CompactHeightDip = 38;
    public const double ExpandedWidthDip = 420;
    public const double ExpandedHeightDip = 172;
    public const double ExpandedRadiusDip = 30;

    /// <summary>Spare room around the expanded shape so the pressed-state scale never clips.</summary>
    public const double CanvasMarginDip = 8;

    public static IslandLayout Compute(PixelRect workArea, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 48);
        var scale = dpi / 96.0;
        var width = Px(ExpandedWidthDip + (2 * CanvasMarginDip), scale);
        var height = Px(TopGapDip + ExpandedHeightDip + CanvasMarginDip, scale);
        if (width > workArea.Width || height > workArea.Height)
        {
            throw new ArgumentException("The work area is smaller than the island canvas.", nameof(workArea));
        }

        var canvas = new PixelRect(workArea.Left + ((workArea.Width - width) / 2), workArea.Top, width, height);
        return new IslandLayout(
            canvas,
            Shape(width, CompactWidthDip, CompactHeightDip, CompactHeightDip, scale),
            Shape(width, ExpandedWidthDip, ExpandedHeightDip, ExpandedRadiusDip, scale),
            scale);
    }

    public RoundedBox ShapeFor(IslandPresentation presentation) => presentation == IslandPresentation.Expanded ? Expanded : Compact;

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
