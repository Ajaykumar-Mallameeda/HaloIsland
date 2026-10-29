using System.Runtime.InteropServices;
using Halo.Core.Privacy;

namespace Halo.App.Interop;

// Media Foundation sensor activity interfaces (mfidl.h, Windows SDK 10.0.26100). Method order is the vtable order.
#pragma warning disable SA1600, CA1711

[ComImport]
[Guid("39DC7F4A-B141-4719-813C-A7F46162A2B8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorProcessActivity
{
    [PreserveSig]
    int GetProcessId(out uint pid);

    [PreserveSig]
    int GetStreamingState([MarshalAs(UnmanagedType.Bool)] out bool streaming);
}

[ComImport]
[Guid("3E8C4BE1-A8C2-4528-90DE-2851BDE5FEAD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivityReport
{
    [PreserveSig]
    int GetFriendlyName(nint name, uint length, out uint written);

    [PreserveSig]
    int GetSymbolicLink(nint link, uint length, out uint written);

    [PreserveSig]
    int GetProcessCount(out uint count);

    [PreserveSig]
    int GetProcessActivity(uint index, out IMFSensorProcessActivity? activity);
}

[ComImport]
[Guid("683F7A5E-4A19-43CD-B1A9-DBF4AB3F7777")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivitiesReport
{
    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetActivityReport(uint index, out IMFSensorActivityReport? report);
}

[ComImport]
[Guid("DE5072EE-DBE3-46DC-8A87-B6F631194751")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivitiesReportCallback
{
    [PreserveSig]
    int OnActivitiesReport(IMFSensorActivitiesReport report);
}

[ComImport]
[Guid("D0CEF145-B3F4-4340-A2E5-7A5080CA05CB")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSensorActivityMonitor
{
    [PreserveSig]
    int Start();

    [PreserveSig]
    int Stop();
}

#pragma warning restore SA1600, CA1711

/// <summary>
/// Passive camera activity reports from Media Foundation (K / C-F08). Starting the monitor never opens or starts a
/// camera; Windows only reports which processes stream. Reports arrive on a Media Foundation thread: the watcher
/// reduces each to streaming flags plus process IDs (kept only to leave Halo out), stores it and posts a message.
/// Friendly names and symbolic links are never read.
/// </summary>
internal sealed class CameraWatcher(nint hwnd) : IDisposable
{
    public const uint ReportMessage = Native.WmApp + 10;

    private readonly Lock _gate = new();
    private IMFSensorActivityMonitor? _monitor;
    private IReadOnlyList<CameraUse> _latest = [];
    private volatile bool _disposed;
    private bool _mfStarted;

    public int LastHResult { get; private set; }

    public bool Running => _monitor is not null;

    public IReadOnlyList<CameraUse> Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public bool Start()
    {
        // Media Foundation must be started first (otherwise MF_E_SHUTDOWN). Lite: no sources or sinks are loaded.
        var hr = MFStartup(0x00020070 /* MF_VERSION */, 1 /* MFSTARTUP_LITE */);
        if (hr < 0)
        {
            LastHResult = hr;
            return false;
        }

        _mfStarted = true;
        hr = MFCreateSensorActivityMonitor(new Callback(this), out var monitor);
        if (hr < 0 || monitor is null)
        {
            LastHResult = hr;
            return false;
        }

        hr = monitor.Start();
        LastHResult = hr;
        if (hr < 0)
        {
            _ = Marshal.ReleaseComObject(monitor);
            return false;
        }

        _monitor = monitor;
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_monitor is { } monitor)
        {
            _ = monitor.Stop();
            _ = Marshal.ReleaseComObject(monitor);
            _monitor = null;
        }

        if (_mfStarted)
        {
            _ = MFShutdown();
            _mfStarted = false;
        }
    }

    private void OnReport(IMFSensorActivitiesReport report)
    {
        var uses = new List<CameraUse>();
        if (report.GetCount(out var count) >= 0)
        {
            for (uint i = 0; i < count && i < 32; i++)
            {
                if (report.GetActivityReport(i, out var device) < 0 || device is null)
                {
                    continue;
                }

                if (device.GetProcessCount(out var processes) >= 0)
                {
                    for (uint j = 0; j < processes && j < 64; j++)
                    {
                        if (device.GetProcessActivity(j, out var p) >= 0 && p is not null && p.GetProcessId(out var pid) >= 0 && p.GetStreamingState(out var streaming) >= 0)
                        {
                            uses.Add(new CameraUse(pid, streaming));
                        }
                    }
                }
            }
        }

        lock (_gate)
        {
            _latest = uses;
        }

        if (!_disposed)
        {
            _ = Native.PostMessageW(hwnd, ReportMessage, 0, 0);
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("mfplat.dll")]
    private static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfsensorgroup.dll")]
    private static extern int MFCreateSensorActivityMonitor(IMFSensorActivitiesReportCallback callback, out IMFSensorActivityMonitor? monitor);
#pragma warning restore SYSLIB1054

    private sealed class Callback(CameraWatcher owner) : IMFSensorActivitiesReportCallback
    {
        public int OnActivitiesReport(IMFSensorActivitiesReport report)
        {
            try
            {
                owner.OnReport(report);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // A report that cannot be read is skipped; the next one replaces it.
            }

            return 0;
        }
    }
}
