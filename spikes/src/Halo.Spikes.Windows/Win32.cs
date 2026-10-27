using System.Runtime.InteropServices;

namespace Halo.Spikes.Windows;

[StructLayout(LayoutKind.Sequential)]
public struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
public struct Point
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct MonitorInfo
{
    public uint Size;
    public Rect Monitor;
    public Rect Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
public struct Margins
{
    public int Left;
    public int Right;
    public int Top;
    public int Bottom;
}

/// <summary>
/// Win32 declarations used by the spike tools (source-generated P/Invoke, no package). Throw-away code; production
/// code uses CsWin32 (ADR §5). Deliberately contains no display-mode setters (protocol G5).
/// </summary>
internal static partial class Win32
{
    public const int GwlStyle = -16;
    public const int GwlExStyle = -20;
    public const long WsPopup = 0x80000000;
    public const long WsVisible = 0x10000000;
    public const long WsExTopmost = 0x00000008;
    public const long WsExTransparent = 0x00000020;
    public const long WsExToolWindow = 0x00000080;
    public const long WsExAppWindow = 0x00040000;
    public const long WsExLayered = 0x00080000;
    public const long WsExNoActivate = 0x08000000;
    public const long WsExNoRedirectionBitmap = 0x00200000;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpFrameChanged = 0x0020;
    public const uint SwpShowWindow = 0x0040;
    public static readonly nint HwndTopmost = -1;

    public const uint GwHwndNext = 2;
    public const uint GaRoot = 2;
    public const uint MonitorDefaultToPrimary = 1;

    public const uint WmNull = 0x0000;
    public const uint WmSettingChange = 0x001A;
    public const uint WmDisplayChange = 0x007E;
    public const uint SpiSetWorkArea = 0x002F;
    public const uint SmtoAbortIfHung = 0x0002;

    public const uint EsContinuous = 0x80000000;
    public const uint EsSystemRequired = 0x00000001;
    public const uint EsDisplayRequired = 0x00000002;

    public const uint DwmwaCloaked = 14;
    public const uint DwmwaWindowCornerPreference = 33;
    public const uint DwmwaBorderColor = 34;
    public const int DwmwcpDoNotRound = 1;
    public const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    public const int RgnOr = 2;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

    [LibraryImport("gdi32.dll")]
    public static partial int CombineRgn(nint destination, nint source1, nint source2, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetTopWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hwnd, uint command);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hwnd, uint message, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LastInputInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport("kernel32.dll")]
    public static partial uint SetThreadExecutionState(uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryProcessCycleTime(nint process, out ulong cycles);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmExtendFrameIntoClientArea(nint hwnd, in Margins margins);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint attribute, in int value, uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, uint size);

    [StructLayout(LayoutKind.Sequential)]
    public struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    public static long ExStyle(nint hwnd) => GetWindowLongPtr(hwnd, GwlExStyle);

    public static Rect WindowRect(nint hwnd) => GetWindowRect(hwnd, out var r) ? r : default;

    /// <summary>Tick count (ms) of the last user or injected input in this session.</summary>
    public static uint LastInputTick()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.Time : 0;
    }

    public static bool IsCloaked(nint hwnd) => DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static bool IsResponsive(nint hwnd, uint timeoutMs) =>
        SendMessageTimeout(hwnd, WmNull, 0, 0, SmtoAbortIfHung, timeoutMs, out _) != 0;

    /// <summary>Work area and DPI of the primary monitor.</summary>
    public static (Rect Work, uint Dpi) PrimaryWorkArea()
    {
        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, 0, out var dx, out _) == 0 ? dx : 96;
        return (info.Work, dpi);
    }

    /// <summary>Sets a window region that is the union of rounded rectangles (window-relative pixels).</summary>
    public static void SetRoundedRegion(nint hwnd, params ReadOnlySpan<(int Left, int Top, int Right, int Bottom, int Radius)> shapes)
    {
        nint combined = 0;
        foreach (var s in shapes)
        {
            var diameter = s.Radius * 2;
            var rgn = CreateRoundRectRgn(s.Left, s.Top, s.Right + 1, s.Bottom + 1, diameter, diameter);
            if (combined == 0)
            {
                combined = rgn;
            }
            else
            {
                _ = CombineRgn(combined, combined, rgn, RgnOr);
                DeleteObject(rgn);
            }
        }

        // The system owns the region after a successful SetWindowRgn.
        if (SetWindowRgn(hwnd, combined, true) == 0)
        {
            DeleteObject(combined);
        }
    }
}
