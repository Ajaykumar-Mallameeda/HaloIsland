namespace Halo.Spikes.Measurement;

/// <summary>Axis-aligned rectangle in physical pixels (right and bottom exclusive).</summary>
public readonly record struct PxRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public double CenterX => Left + (Width / 2.0);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Intersects(PxRect other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public static PxRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);
}

/// <summary>Rounded rectangle in pixels, relative to whatever origin the caller uses.</summary>
public readonly record struct RoundedRect(double Left, double Top, double Width, double Height, double Radius)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    /// <summary>
    /// Signed distance from a point to the shape outline: negative inside, positive outside (standard rounded-box
    /// distance function). Used to pick test points and pixels a safe distance away from anti-aliased edges.
    /// </summary>
    public double SignedDistance(double x, double y)
    {
        var halfW = Width / 2;
        var halfH = Height / 2;
        var r = Math.Min(Radius, Math.Min(halfW, halfH));
        var qx = Math.Abs(x - (Left + halfW)) - (halfW - r);
        var qy = Math.Abs(y - (Top + halfH)) - (halfH - r);
        var outside = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2));
        var inside = Math.Min(Math.Max(qx, qy), 0);
        return outside + inside - r;
    }

    public RoundedRect Offset(double dx, double dy) => this with { Left = Left + dx, Top = Top + dy };
}

/// <summary>
/// Test-surface geometry from PROTOTYPE_S1_S2_REPORT §8.2 (test parameters, not product design): a fixed canvas
/// centred under the top edge of the work area, with Compact and Expanded shapes anchored top-centre inside it.
/// Shape rectangles are relative to the canvas origin.
/// </summary>
public sealed record IslandGeometry(PxRect Canvas, RoundedRect Compact, RoundedRect Expanded, double Scale)
{
    public const double CanvasWidthDip = 468;
    public const double CanvasHeightDip = 208;
    public const double CompactWidthDip = 220;
    public const double CompactHeightDip = 40;
    public const double CompactRadiusDip = 20;
    public const double ExpandedWidthDip = 420;
    public const double ExpandedHeightDip = 160;
    public const double ExpandedRadiusDip = 32;
    public const double GlyphDiameterDip = 24;
    public static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(350);

    /// <param name="workArea">Work area of the monitor in physical pixels.</param>
    /// <param name="dpi">Effective DPI of that monitor (96 = 100 %).</param>
    public static IslandGeometry Compute(PxRect workArea, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 48);
        var scale = dpi / 96.0;
        var width = (int)Math.Round(CanvasWidthDip * scale, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(CanvasHeightDip * scale, MidpointRounding.AwayFromZero);
        if (width > workArea.Width || height > workArea.Height)
        {
            throw new ArgumentException("The work area is smaller than the test canvas.", nameof(workArea));
        }

        var left = workArea.Left + ((workArea.Width - width) / 2);
        var canvas = new PxRect(left, workArea.Top, width, height);
        return new IslandGeometry(canvas, Shape(width, CompactWidthDip, CompactHeightDip, CompactRadiusDip, scale), Shape(width, ExpandedWidthDip, ExpandedHeightDip, ExpandedRadiusDip, scale), scale);
    }

    /// <summary>The shape for a presentation, in screen pixels.</summary>
    public RoundedRect ScreenShape(bool expanded) => (expanded ? Expanded : Compact).Offset(Canvas.Left, Canvas.Top);

    private static RoundedRect Shape(int canvasWidthPx, double widthDip, double heightDip, double radiusDip, double scale)
    {
        var w = widthDip * scale;
        return new RoundedRect((canvasWidthPx - w) / 2, 0, w, heightDip * scale, radiusDip * scale);
    }
}
