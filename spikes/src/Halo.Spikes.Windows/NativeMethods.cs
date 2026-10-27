using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Halo.Spikes.Windows;

/// <summary>Source-generated P/Invoke declarations (Win32 documentation names kept for traceability).</summary>
internal static partial class NativeMethods
{
    internal const int EnumCurrentSettings = -1;
    internal const uint MonitorDefaultToPrimary = 1;
    internal const int MdtEffectiveDpi = 0;
    internal static readonly nint DpiAwarenessContextPerMonitorAwareV2 = -4;

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevModeW devMode);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(Point pt, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial nint SetThreadDpiAwarenessContext(nint dpiContext);

    [LibraryImport("shcore.dll")]
    internal static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    [LibraryImport("powrprof.dll")]
    internal static partial uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    /// <summary>DEVMODEW, display variant of the union. 220 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DevModeW
    {
        public WcharBuffer32 DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        public WcharBuffer32 FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    /// <summary>WCHAR[32] kept as UTF-16 code units so the struct stays blittable.</summary>
    [InlineArray(32)]
    internal struct WcharBuffer32
    {
        private ushort _element;
    }
}
