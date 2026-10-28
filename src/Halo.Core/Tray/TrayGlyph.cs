namespace Halo.Core.Tray;

/// <summary>
/// The tray icon image, drawn in code so no binary icon file is needed: a small black pill with a light outline,
/// readable on both dark and light taskbars. Output is premultiplied 32-bit BGRA, top-down, row-major, ready for a
/// DIB section.
/// </summary>
public static class TrayGlyph
{
    public static uint[] Render(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, 256);
        var pixels = new uint[size * size];
        var width = size * 0.92;
        var height = size * 0.5;
        var radius = height / 2;
        var cx = size / 2.0;
        var cy = size / 2.0;
        var stroke = Math.Max(1.0, size / 16.0);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var d = RoundedBoxDistance((x + 0.5) - cx, (y + 0.5) - cy, width / 2, height / 2, radius);
                var shape = Coverage(d);
                var inner = Coverage(d + stroke);
                var outline = shape - inner;
                var alpha = (uint)Math.Round(shape * 255);
                var light = (uint)Math.Round(outline * 235); // premultiplied: never above alpha
                pixels[(y * size) + x] = (alpha << 24) | (light << 16) | (light << 8) | light;
            }
        }

        return pixels;
    }

    /// <summary>Signed distance from a point (relative to the centre) to a rounded box; negative inside.</summary>
    private static double RoundedBoxDistance(double px, double py, double halfW, double halfH, double r)
    {
        var qx = Math.Abs(px) - (halfW - r);
        var qy = Math.Abs(py) - (halfH - r);
        var outside = Math.Sqrt((Math.Max(qx, 0) * Math.Max(qx, 0)) + (Math.Max(qy, 0) * Math.Max(qy, 0)));
        return outside + Math.Min(Math.Max(qx, qy), 0) - r;
    }

    /// <summary>Pixel coverage for a one-pixel anti-aliased edge.</summary>
    private static double Coverage(double distance) => Math.Clamp(0.5 - distance, 0, 1);
}
