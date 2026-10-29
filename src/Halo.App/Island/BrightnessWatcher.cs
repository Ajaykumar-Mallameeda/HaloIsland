using System.Management;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Built-in panel brightness through WMI (J-F03): <c>WmiMonitorBrightness</c> for the start-up level and
/// <c>WmiMonitorBrightnessEvent</c> for changes (event-driven; no polling). External monitors expose neither, so the
/// feature reports "not supported" there and shows nothing. Read-only: <c>WmiSetBrightness</c> is never called.
/// Events arrive on a WMI thread; the watcher stores the level and posts <see cref="ChangedMessage"/>.
/// The first query and the subscription take tens of milliseconds, so <see cref="StartInBackground"/> runs them off the
/// UI thread (they would otherwise delay the first frame) and posts <see cref="ChangedMessage"/> with
/// <see cref="StartedFlag"/> when done.
/// </summary>
internal sealed class BrightnessWatcher(nint hwnd) : IDisposable
{
    public const uint ChangedMessage = WmApp + 11;

    /// <summary>wParam of <see cref="ChangedMessage"/> once <see cref="StartInBackground"/> has finished.</summary>
    public const nint StartedFlag = 1;

    private readonly Lock _gate = new();
    private ManagementEventWatcher? _events;
    private bool _disposed;
    private volatile int _latest = -1;

    /// <summary>The latest level (0–100), or -1 if unknown.</summary>
    public int Latest => _latest;

    public bool Supported { get; private set; }

    public int LastHResult { get; private set; }

    /// <summary>Runs <see cref="Start"/> on a pool thread, then posts <see cref="ChangedMessage"/> with <see cref="StartedFlag"/>.</summary>
    public void StartInBackground() => _ = Task.Run(() =>
    {
        _ = Start();
        _startFinished = true;
        _ = PostMessageW(hwnd, ChangedMessage, StartedFlag, 0);
    });

    private volatile bool _startFinished;

    /// <summary>
    /// UI thread: true once per watcher, after its background start has finished. A late message from an earlier
    /// watcher (the setting was turned off and on quickly) finds the new one unfinished or already reported.
    /// </summary>
    public bool TakeStartReport()
    {
        if (!_startFinished || _startReported)
        {
            return false;
        }

        _startReported = true;
        return true;
    }

    private bool _startReported;

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

            var events = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            events.EventArrived += OnEvent;
            events.Start();
            lock (_gate)
            {
                if (_disposed)
                {
                    // Turned off (or the island closed) while starting.
                    events.EventArrived -= OnEvent;
                    events.Stop();
                    events.Dispose();
                    return false;
                }

                _events = events;
                Supported = true;
            }

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
        ManagementEventWatcher? toStop;
        lock (_gate)
        {
            _disposed = true;
            toStop = _events;
            _events = null;
            Supported = false;
        }

        if (toStop is { } events)
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
        }
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
