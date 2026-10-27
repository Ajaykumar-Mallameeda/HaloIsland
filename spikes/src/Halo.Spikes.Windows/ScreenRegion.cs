using System.Runtime.InteropServices;
using Halo.Spikes.Measurement;

namespace Halo.Spikes.Windows;

/// <summary>32-bit BGRA pixels of one screen rectangle (top-down rows).</summary>
public sealed record RegionPixels(PxRect Rect, byte[] Bgra)
{
    public (byte R, byte G, byte B) At(int x, int y)
    {
        var i = ((y * Rect.Width) + x) * 4;
        return (Bgra[i + 2], Bgra[i + 1], Bgra[i]);
    }
}

/// <summary>
/// Captures one screen rectangle with GDI <c>BitBlt</c> + <c>CAPTUREBLT</c> (includes layered windows). Used only for
/// S2 checks of the overlay canvas over a probe window, so the region holds only test colours and test content.
/// </summary>
public static partial class ScreenRegion
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    public static RegionPixels Capture(PxRect rect)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, rect.Width, rect.Height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, rect.Width, rect.Height, screen, rect.Left, rect.Top, SrcCopy | CaptureBlt))
            {
                throw new InvalidOperationException("BitBlt failed.");
            }

            SelectObject(memory, old);
            var info = new BitmapInfoHeader
            {
                Size = 40, // sizeof(BITMAPINFOHEADER); the trailing field is room for bmiColors
                Width = rect.Width,
                Height = -rect.Height,
                Planes = 1,
                BitCount = 32,
            };
            var pixels = new byte[rect.Width * rect.Height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)rect.Height, pixels, ref info, 0) == 0)
            {
                throw new InvalidOperationException("GetDIBits failed.");
            }

            return new RegionPixels(rect, pixels);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(0, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
        public uint Colors;
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hwnd, nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleBitmap(nint dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(nint dc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
}
