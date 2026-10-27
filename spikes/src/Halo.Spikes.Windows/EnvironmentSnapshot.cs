using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Halo.Spikes.Windows;

public enum PowerSource
{
    Unknown,
    Battery,
    AC,
}

/// <summary>
/// Measurement conditions recorded with every spike run (TESTING_STRATEGY §4.8). Contains no user,
/// machine or file names.
/// </summary>
public sealed record EnvironmentSnapshot(
    string OsVersion,
    string OsDisplayVersion,
    string RuntimeVersion,
    int PrimaryWidthPx,
    int PrimaryHeightPx,
    int PrimaryRefreshHz,
    int PrimaryDpi,
    PowerSource PowerSource,
    int? BatteryPercent,
    Guid? PowerModeOverlay)
{
    public double ScalePercent => PrimaryDpi * 100.0 / 96.0;

    public static EnvironmentSnapshot Capture()
    {
        var (width, height, refresh) = ReadPrimaryDisplayMode();
        var (power, battery) = ReadPowerStatus();
        return new EnvironmentSnapshot(
            ReadOsVersion(),
            ReadRegistryString("DisplayVersion") ?? "unknown",
            RuntimeInformation.FrameworkDescription,
            width,
            height,
            refresh,
            ReadPrimaryDpi(),
            power,
            battery,
            ReadPowerModeOverlay());
    }

    private static string ReadOsVersion()
    {
        var v = Environment.OSVersion.Version;
        var ubr = Registry.GetValue(CurrentVersionKey, "UBR", null) is int revision ? revision : 0;
        return $"{v.Major}.{v.Minor}.{v.Build}.{ubr}";
    }

    private const string CurrentVersionKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private static string? ReadRegistryString(string name) => Registry.GetValue(CurrentVersionKey, name, null) as string;

    private static (int Width, int Height, int RefreshHz) ReadPrimaryDisplayMode()
    {
        var mode = new NativeMethods.DevModeW { Size = (ushort)Unsafe.SizeOf<NativeMethods.DevModeW>() };
        if (!NativeMethods.EnumDisplaySettings(null, NativeMethods.EnumCurrentSettings, ref mode))
        {
            return (0, 0, 0);
        }

        return ((int)mode.PelsWidth, (int)mode.PelsHeight, (int)mode.DisplayFrequency);
    }

    /// <summary>Effective DPI of the primary monitor, read under a temporary per-monitor-aware thread context.</summary>
    private static int ReadPrimaryDpi()
    {
        var previous = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessContextPerMonitorAwareV2);
        try
        {
            var monitor = NativeMethods.MonitorFromPoint(default, NativeMethods.MonitorDefaultToPrimary);
            var hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MdtEffectiveDpi, out var dpiX, out _);
            return hr == 0 ? (int)dpiX : 0;
        }
        finally
        {
            if (previous != 0)
            {
                NativeMethods.SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    private static (PowerSource Source, int? BatteryPercent) ReadPowerStatus()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status))
        {
            return (PowerSource.Unknown, null);
        }

        var source = status.ACLineStatus switch
        {
            0 => PowerSource.Battery,
            1 => PowerSource.AC,
            _ => PowerSource.Unknown,
        };
        int? battery = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : null;
        return (source, battery);
    }

    /// <summary>The active Windows power-mode overlay GUID, or null if the API is unavailable.</summary>
    private static Guid? ReadPowerModeOverlay()
    {
        try
        {
            return NativeMethods.PowerGetEffectiveOverlayScheme(out var overlay) == 0 ? overlay : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }
}
