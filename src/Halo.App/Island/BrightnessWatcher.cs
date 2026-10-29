using System.Management;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Built-in panel brightness through WMI (J-F03): <c>WmiMonitorBrightness</c> for the start-up level and
/// <c>WmiMonitorBrightnessEvent</c> for changes (event-driven; no polling). External monitors expose neither, so the
/// feature reports "not supported" there and shows nothing. Read-only: <c>WmiSetBrightness</c> is never called.
/// Events arrive on a WMI thread; the watcher stores the level and posts <see cref="ChangedMessage"/>.
/// </summary>
internal sealed class BrightnessWatcher(nint hwnd) : IDisposable
{
    public const uint ChangedMessage = WmApp + 11;

    private ManagementEventWatcher? _events;
    private volatile int _latest = -1;

    /// <summary>The latest level (0–100), or -1 if unknown.</summary>
    public int Latest => _latest;

    public bool Supported { get; private set; }

    public int LastHResult { get; private set; }

    /// <summary>Reads the level and subscribes. Returns false when there is no WMI-controllable panel.</summary>
    public bool Start()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    _latest = Convert.ToInt32(item["CurrentBrightness"], System.Globalization.CultureInfo.InvariantCulture);
                }

                break;
            }

            if (_latest < 0)
            {
                return false;
            }

            _events = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _events.EventArrived += OnEvent;
            _events.Start();
            Supported = true;
            return true;
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidCastException or FormatException)
        {
            LastHResult = ex.HResult;
            Dispose();
            return false;
        }
    }

    public void Dispose()
    {
        if (_events is { } events)
        {
            events.EventArrived -= OnEvent;
            try
            {
                events.Stop();
            }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException)
            {
                // Already stopped.
            }

            events.Dispose();
            _events = null;
        }

        Supported = false;
    }

    private void OnEvent(object sender, EventArrivedEventArgs e)
    {
        try
        {
            _latest = Convert.ToInt32(e.NewEvent["Brightness"], System.Globalization.CultureInfo.InvariantCulture);
            _ = PostMessageW(hwnd, ChangedMessage, 0, 0);
        }
        catch (Exception ex) when (ex is ManagementException or InvalidCastException or FormatException)
        {
            // An unreadable event is skipped.
        }
    }
}
