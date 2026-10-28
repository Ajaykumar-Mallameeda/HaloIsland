using System.Runtime.InteropServices;
using Halo.Core.Island;
using Halo.Core.Shell;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Full-screen detection (FR-ISL-09, ADR-001 §4.9), event-driven with no polling:
/// <list type="bullet">
/// <item><c>EVENT_SYSTEM_FOREGROUND</c> (all processes, out of context) for foreground changes;</item>
/// <item><c>EVENT_OBJECT_LOCATIONCHANGE</c> scoped to the foreground application's process, so F11 or a game
/// switching to borderless full screen inside the same window is seen;</item>
/// <item>the shell's <c>ABN_FULLSCREENAPP</c> notification, received by registering the island as an app bar
/// without reserving any space.</item>
/// </list>
/// Each trigger only schedules one evaluation after <see cref="Debounce"/>, so a burst of location events during a
/// resize costs one probe, and the island does not flicker while a window changes size. The decision itself is
/// <see cref="FullscreenRule"/>. Hook callbacks arrive on the UI thread (out-of-context hooks are delivered through
/// the thread's message loop).
/// </summary>
internal sealed unsafe class FullscreenWatcher : IDisposable
{
    /// <summary>App bar callback message (wParam = ABN_* code).</summary>
    public const uint AppBarMessage = WmApp + 3;

    public static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);

    private static FullscreenWatcher? s_current;

    private readonly nint _hwnd;
    private readonly nuint _timerId;
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private nint _foregroundHook;
    private nint _locationHook;
    private uint _locationProcess;
    private nint _foreground;
    private bool _appBar;

    public FullscreenWatcher(nint hwnd, nuint timerId)
    {
        _hwnd = hwnd;
        _timerId = timerId;
    }

    /// <summary>True while the hooks are installed.</summary>
    public bool IsRunning => _foregroundHook != 0;

    /// <summary>The shell's last ABN_FULLSCREENAPP value (diagnostics only; the rule decides).</summary>
    public bool ShellReportsFullscreen { get; private set; }

    public bool Start()
    {
        if (IsRunning)
        {
            return true;
        }

        s_current = this;
        _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, 0, &OnWinEvent, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        RegisterAppBar();
        Schedule();
        return _foregroundHook != 0;
    }

    public void Stop()
    {
        _ = KillTimer(_hwnd, _timerId);
        UnhookLocation();
        if (_foregroundHook != 0)
        {
            _ = UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
        }

        if (_appBar)
        {
            var data = new AppBarData { Size = (uint)sizeof(AppBarData), Hwnd = _hwnd };
            _ = SHAppBarMessage(AbmRemove, &data);
            _appBar = false;
        }

        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    /// <summary>Explorer restarted: the app bar registration is gone with it.</summary>
    public void OnTaskbarCreated()
    {
        if (!IsRunning)
        {
            return;
        }

        _appBar = false;
        RegisterAppBar();
        Schedule();
    }

    /// <summary>Handles the app bar callback; returns true when the message was a full-screen notification.</summary>
    public bool OnAppBarMessage(nint wParam, nint lParam)
    {
        if (wParam != AbnFullscreenApp)
        {
            return false;
        }

        ShellReportsFullscreen = lParam != 0;
        Schedule();
        return true;
    }

    /// <summary>Re-evaluates soon (display, DPI or work-area change).</summary>
    public void Schedule()
    {
        if (IsRunning)
        {
            _ = SetTimer(_hwnd, _timerId, (uint)Debounce.TotalMilliseconds, 0);
        }
    }

    /// <summary>Called when the debounce timer fires: stops it, probes the foreground window and decides.</summary>
    public FullscreenVerdict Evaluate(out ForegroundKind kind)
    {
        _ = KillTimer(_hwnd, _timerId);
        var probe = Capture();
        kind = probe.Kind;
        if (probe.Kind == ForegroundKind.Application)
        {
            HookLocation(_foreground);
        }

        return FullscreenRule.Evaluate(probe);
    }

    public void Dispose() => Stop();

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (s_current is not { } watcher)
        {
            return;
        }

        // Location events: only the foreground window itself, not its caret or child objects.
        if (eventType == EventObjectLocationChange && (hwnd != watcher._foreground || idObject != ObjIdWindow || idChild != 0))
        {
            return;
        }

        watcher.Schedule();
    }

    private void RegisterAppBar()
    {
        var data = new AppBarData { Size = (uint)sizeof(AppBarData), Hwnd = _hwnd, CallbackMessage = AppBarMessage };
        _appBar = SHAppBarMessage(AbmNew, &data) != 0;
    }

    private FullscreenProbe Capture()
    {
        var fg = GetForegroundWindow();
        _foreground = fg;
        _ = GetWindowThreadProcessId(fg, out var pid);
        var kind = ForegroundClassifier.Classify(fg, fg != 0 && pid == _ownProcess, ClassName(fg));
        if (SHQueryUserNotificationState(out var quns) < 0)
        {
            quns = 0;
        }

        var (monitor, bounds) = PrimaryMonitor();
        if (kind != ForegroundKind.Application)
        {
            return new FullscreenProbe(quns, kind, default, ToPixel(bounds), false, false, false, false);
        }

        _ = GetWindowRect(fg, out var rect);
        int cloaked = 0;
        _ = DwmGetWindowAttribute(fg, DwmwaCloaked, &cloaked, sizeof(int));
        var hasCaption = (GetWindowLongPtr(fg, GwlStyle) & WsCaption) == WsCaption;
        return new FullscreenProbe(
            quns,
            kind,
            ToPixel(rect),
            ToPixel(bounds),
            MonitorFromWindow(fg, MonitorDefaultToNull) == monitor,
            IsIconic(fg),
            IsZoomed(fg) && hasCaption,
            cloaked != 0);
    }

    /// <summary>Scopes the location hook to the foreground application's process (re-hooked when it changes).</summary>
    private void HookLocation(nint fg)
    {
        _ = GetWindowThreadProcessId(fg, out var pid);
        if (pid == _locationProcess && _locationHook != 0)
        {
            return;
        }

        UnhookLocation();
        _locationHook = SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, 0, &OnWinEvent, pid, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        _locationProcess = _locationHook != 0 ? pid : 0;
    }

    private void UnhookLocation()
    {
        if (_locationHook != 0)
        {
            _ = UnhookWinEvent(_locationHook);
            _locationHook = 0;
            _locationProcess = 0;
        }
    }

    private static string ClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return string.Empty;
        }

        var buffer = stackalloc char[128];
        var length = GetClassNameW(hwnd, buffer, 128);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static PixelRect ToPixel(Rect r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
