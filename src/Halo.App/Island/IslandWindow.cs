using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Halo.App.Interop;
using Halo.Core.Clock;
using Halo.Core.Diagnostics;
using Halo.Core.Island;
using Halo.Core.Settings;
using Halo.Core.Shell;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// The island window (ADR-001 §4): a topmost, non-activating tool window (no taskbar button, not in Alt+Tab) with
/// no redirection bitmap, rendered by Windows.UI.Composition. Input is limited to the visible shape with a window
/// region (union of the current and target shapes during a transition). One instance per process, on the primary
/// monitor's work area.
/// </summary>
internal sealed unsafe class IslandWindow : IIslandAutomationSource, IDisposable
{
    public const string ClassName = "ProjectHalo.Island";
    public const string Title = "Halo";

    /// <summary>Message from a second launch: wParam 1 = expand, 2 = toggle.</summary>
    public const uint WmCommandFromInstance = WmApp + 1;

    private const uint WmTransitionDone = WmApp + 100;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const nuint ClockTimerId = 1;
    private const nuint IntentTimerId = 2;
    private const nuint FullscreenTimerId = 3;
    private const nuint AlertTimerId = 4;
    private const nuint SaveTimerId = 5;

    /// <summary>Settings are written at most once a second (ADR-001 §8 debounce).</summary>
    private const uint SaveDebounceMs = 1000;

    /// <summary>Diagnostics-only test hook: raise a synthetic alert (wParam = priority 0–3, lParam bit 0 = allowed
    /// over full screen, bits 8+ = duration in ms). Ignored without --diagnostics.</summary>
    private const uint WmTestAlert = WmApp + 50;

    // Island right-click menu.
    private const int MenuExpand = 1;
    private const int MenuExit = 2;
    private const int MenuCollapse = 3;

    // Tray menu (FR-APP-01).
    private const int TrayOpen = 11;
    private const int TrayCollapse = 12;
    private const int TrayExit = 13;
    private const int TrayShowNow = 14;
    private const int TraySettings = 15;

    private static readonly TimeSpan ClockSlack = TimeSpan.FromMilliseconds(40);

    private static IslandWindow? s_current;

    private readonly DiagnosticsLog? _log;
    private readonly IslandFault _fault;
    private readonly SettingsStore _store;
    private readonly LogFile? _fileLog;
    private HaloSettings _settings = HaloSettings.Defaults;
    private bool _savePending;
    private readonly IslandController _controller;
    private nint _hwnd;
    private uint _taskbarCreated;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private TextRenderer? _text;
    private IslandVisuals? _visuals;
    private IslandAutomationProvider? _automation;
    private TrayIcon? _tray;
    private FullscreenWatcher? _fullscreen;
    private SettingsWindow? _settingsWindow;
    private IslandLayout _layout = null!;
    private ClockReading _clock = null!;
    private bool _tracking;
    private bool _menuOpen;
    private bool _clockDirty;
    private bool _sessionLocked;
    private bool _displayOff;
    private nint _displayNotification;
    private bool _reducedMotion;
    private int _transitionId;

    public IslandWindow(DiagnosticsLog? log, SettingsStore store, LogFile? fileLog = null, IslandFault fault = IslandFault.None)
    {
        _log = log;
        _fileLog = fileLog;
        _store = store;
        _fault = fault;
        _controller = new IslandController(new WindowTimer(this), IslandTiming.Default);
    }

    /// <summary>Folder of the local diagnostic log (Settings › Open log folder), or null without a file log.</summary>
    public string? LogDirectory => _fileLog?.Directory;

    /// <summary>The settings in effect.</summary>
    public HaloSettings Settings => _settings;

    /// <summary>Applies changed settings immediately and saves them shortly after (debounced).</summary>
    public void UpdateSettings(HaloSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings == _settings)
        {
            return;
        }

        _settings = settings;
        ApplySettings("changed");
        _savePending = true;
        _ = SetTimer(_hwnd, SaveTimerId, SaveDebounceMs, 0);
    }

    public bool IsExpanded => State.IsExpanded;

    public string AccessibleName => $"Halo, {_clock.TimeWithDesignator}, {_clock.LongDate}";

    public string? AccessibleDescription => State.Alert is { } alert ? alert.Content.Text : State.IsExpanded ? _clock.DayProgressText : null;

    private IslandState State => _controller.State;

    /// <summary>Creates the window and runs its message loop until the window is closed. Returns the exit code.</summary>
    public int Run()
    {
        // Windows.UI.Composition needs a DispatcherQueue on the UI thread of a Win32 app (thread type 2 = current
        // thread, apartment 2 = STA).
        var options = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = 2, ApartmentType = 2 };
        Check(CreateDispatcherQueueController(options, out _), "CreateDispatcherQueueController");
        _settings = _store.Load(out var settingsStatus);
        _log?.Write("settings", ("status", SettingsToken(settingsStatus)), ("invalid_fields", _store.LastIssues.Count));
        _fileLog?.Write(
            settingsStatus is SettingsLoadStatus.RecoveredDefaults or SettingsLoadStatus.RecoveredFromBackup || _store.LastIssues.Count > 0 ? LogLevel.Warning : LogLevel.Information,
            "app.start",
            ("settings", SettingsToken(settingsStatus)),
            ("invalid_fields", _store.LastIssues.Count),
            ("dpi", (int)PrimaryWorkArea().Dpi));
        _reducedMotion = !AnimationsEnabled() || _settings.ReduceMotion;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _clock = ClockFace.Read(DateTimeOffset.Now, CultureInfo.CurrentCulture);

        CreateWindow();
        Place("initial");
        _compositor = new Compositor();
        _target = CreateTarget(_compositor, _hwnd);
        _text = new TextRenderer(_compositor);
        _visuals = new IslandVisuals(_compositor, _target, _text, _layout);
        _visuals.ShowClock(_clock);
        _automation = new IslandAutomationProvider(_hwnd, this);
        ArmClock();
        _tray = new TrayIcon(_hwnd);
        AddTray("initial");
        // FR-ISL-12: the clock pauses while the session is locked or the display is off.
        _ = WTSRegisterSessionNotification(_hwnd, 0);
        var displayStatus = GuidSessionDisplayStatus;
        _displayNotification = RegisterPowerSettingNotification(_hwnd, &displayStatus, 0);
        _fullscreen = new FullscreenWatcher(_hwnd, FullscreenTimerId);
        ApplySettings("startup");
        if (_fault == IslandFault.Startup)
        {
            // Test hook (--diagnostics only): fail after everything is created, to prove nothing is left behind.
            throw new InvalidOperationException("Injected startup fault.");
        }

        _ = ShowWindow(_hwnd, SwShowNoActivate);

        // Completes once the first frame of the visual tree has been committed to DWM.
        var ready = _compositor.GetCommitBatch(CompositionBatchTypes.Animation);
        ready.Completed += (_, _) => _log?.Write("ready", ("left", _layout.Canvas.Left), ("top", _layout.Canvas.Top), ("width", _layout.Canvas.Width), ("height", _layout.Canvas.Height), ("dpi", (int)Math.Round(_layout.Scale * 96)));
        if (_settings.StartExpanded)
        {
            Apply(IslandInput.Expand, "startup");
        }

        Msg msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            if (_settingsWindow?.PreTranslate(&msg) == true)
            {
                continue;
            }

            _ = TranslateMessage(&msg);
            _ = DispatchMessageW(&msg);
        }

        _log?.Write("exit", ("code", 0));
        _fileLog?.Write(LogLevel.Information, "app.exit", ("code", 0));
        return 0;
    }

    public void RequestExpanded(bool expanded) => Apply(expanded ? IslandInput.Expand : IslandInput.Collapse, "automation");

    public void RequestToggle() => Apply(IslandInput.Toggle, "automation");

    /// <summary>
    /// Releases everything the island owns. After a normal exit WM_DESTROY has already stopped the timers and removed
    /// the tray icon; after a startup failure, destroying the window here runs that same clean-up.
    /// </summary>
    public void Dispose()
    {
        _controller.Shutdown();
        if (_hwnd != 0 && IsWindow(_hwnd))
        {
            _ = DestroyWindow(_hwnd);
        }

        _tray?.Dispose();
        _tray = null;
        _visuals?.Dispose();
        _visuals = null;
        _text?.Dispose();
        _text = null;
        _target?.Dispose();
        _target = null;
        _compositor?.Dispose();
        _compositor = null;
        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    private static DesktopWindowTarget CreateTarget(Compositor compositor, nint hwnd)
    {
        var unknown = ((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr;
        var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
        Check(Marshal.QueryInterface(unknown, in iid, out var interop), "QueryInterface(ICompositorDesktopInterop)");
        try
        {
            nint target;
            // ICompositorDesktopInterop::CreateDesktopWindowTarget(HWND, BOOL isTopmost, IDesktopWindowTarget**) is slot 3.
            Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)(*(nint**)interop)[3])(interop, hwnd, 1, &target), "CreateDesktopWindowTarget");
            return DesktopWindowTarget.FromAbi(target);
        }
        finally
        {
            _ = Marshal.Release(interop);
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{what} failed: 0x{hr:X8}") { HResult = hr };
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (s_current is { } window && window.Handle(hwnd, msg, wParam, lParam) is { } result)
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            // A failed redraw or placement must not take the island down (NFR-05); the next event retries.
            s_current?._log?.Write("error", ("hresult", ex.HResult));
            s_current?._fileLog?.Write(LogLevel.Error, "window.error", ("type", ex.GetType().Name), ("hresult", ex.HResult), ("message_id", (long)msg));
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void CreateWindow()
    {
        s_current = this;
        var instance = GetModuleHandleW(null);
        fixed (char* className = ClassName)
        fixed (char* title = Title)
        {
            var wc = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                Cursor = LoadCursorW(0, 32649), // IDC_HAND: the island is clickable.
                ClassName = className,
            };
            if (RegisterClassExW(&wc) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed.");
            }

            const uint exStyle = WsExNoRedirectionBitmap | WsExTopmost | WsExToolWindow | WsExNoActivate;
            _hwnd = CreateWindowExW(exStyle, className, title, unchecked((uint)WsPopup), 0, 0, 1, 1, 0, 0, instance, 0);
        }

        if (_hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed.");
        }
    }

    private nint? Handle(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmMouseMove:
                if (!_tracking)
                {
                    _tracking = true;
                    var tme = new TrackMouseEventInfo { Size = (uint)sizeof(TrackMouseEventInfo), Flags = TmeLeave, Hwnd = hwnd };
                    _ = TrackMouseEvent(&tme);
                }

                // Hover is "inside the target shape", not "inside the window region": during a collapse the region
                // still covers the expanded shape, and the pointer there must not count as resting on the pill.
                if (!_menuOpen && GetCapture() != hwnd)
                {
                    SetHovered(InsideTarget(LowWord(lParam), HighWord(lParam)));
                }

                return 0;
            case WmMouseLeave:
                _tracking = false;
                if (!_menuOpen && GetCapture() != hwnd)
                {
                    // WM_MOUSELEAVE can also arrive when the window region is replaced while the pointer stays put
                    // (end of a transition). Trust the real pointer position, and keep tracking if it is still inside.
                    if (PointerInside())
                    {
                        var again = new TrackMouseEventInfo { Size = (uint)sizeof(TrackMouseEventInfo), Flags = TmeLeave, Hwnd = hwnd };
                        _tracking = TrackMouseEvent(&again);
                        _log?.Write("pointer.leave_ignored");
                    }
                    else
                    {
                        SetHovered(false);
                    }
                }

                return 0;
            case WmLButtonDown:
                _ = SetCapture(hwnd);
                Apply(IslandInput.PointerPressed, "pointer");
                return 0;
            case WmRButtonDown:
                // Holding the right button must not let the dwell open the island under the menu.
                Apply(IslandInput.MenuRequested, "pointer");
                return 0;
            case WmLButtonUp:
                var inside = InsideTarget(LowWord(lParam), HighWord(lParam));

                // Handle the release first: ReleaseCapture sends WM_CAPTURECHANGED synchronously, which would
                // otherwise cancel the press before this click is counted.
                Apply(inside ? IslandInput.PointerReleasedInside : IslandInput.PointerReleasedOutside, "pointer");
                _ = ReleaseCapture();
                if (!inside)
                {
                    Apply(IslandInput.PointerLeft, "pointer");
                }

                return 0;
            case WmCaptureChanged when State.Pressed:
                Apply(IslandInput.PointerReleasedOutside, "pointer");
                return 0;
            case WmRButtonUp:
                ShowIslandMenu(_layout.Canvas.Left + LowWord(lParam), _layout.Canvas.Top + HighWord(lParam));
                return 0;
            case WmGetObject:
                return _automation?.HandleGetObject(wParam, lParam);
            case WmTimer when (nuint)wParam == ClockTimerId:
                RefreshClock("minute");
                return 0;
            case WmTimer when (nuint)wParam == IntentTimerId:
                // Win32 timers repeat; the island's timers are one-shot.
                _ = KillTimer(hwnd, IntentTimerId);
                Present(_controller.TimerElapsed(IslandTimerSlot.Intent), "hover");
                return 0;
            case WmTimer when (nuint)wParam == SaveTimerId:
                _ = KillTimer(hwnd, SaveTimerId);
                SaveSettings();
                return 0;
            case WmTimer when (nuint)wParam == AlertTimerId:
                _ = KillTimer(hwnd, AlertTimerId);
                Present(_controller.TimerElapsed(IslandTimerSlot.Alert), "alert");
                return 0;
            case WmTestAlert when _log is not null:
                RaiseAlert(TestAlert((int)wParam, (long)lParam), "test");
                return 0;
            case WmTimer when (nuint)wParam == FullscreenTimerId:
                OnFullscreenTimer();
                return 0;
            case FullscreenWatcher.AppBarMessage:
                _ = _fullscreen?.OnAppBarMessage(wParam, lParam);
                return 0;
            case TrayIcon.CallbackMessage:
                OnTrayEvent((uint)(lParam & 0xFFFF), LowWord(wParam), HighWord(wParam));
                return 0;
            case WmTimeChange:
                // The time or the time zone changed; .NET caches the local time zone until told otherwise.
                TimeZoneInfo.ClearCachedData();
                RefreshClock("time_change");
                return 0;
            case WmSettingChange when IsIntlChange(lParam):
                // Regional format changed (12/24 h, date order): drop the cached culture data.
                CultureInfo.CurrentCulture.ClearCachedData();
                RefreshClock("intl");
                return null;
            case WmWtsSessionChange when wParam == WtsSessionLock || wParam == WtsSessionUnlock:
                SetClockPaused(locked: wParam == WtsSessionLock, displayOff: _displayOff, "session");
                return 0;
            case WmPowerBroadcast when wParam == PbtPowerSettingChange && DisplayState(lParam) is { } on:
                SetClockPaused(_sessionLocked, displayOff: !on, "display");
                return 1;
            case WmPowerBroadcast when wParam == PbtApmResumeAutomatic:
                // Displays may have changed while asleep (FR-ISL-11): re-place, re-check full screen, refresh the clock.
                Place("resume");
                _fullscreen?.Schedule();
                RefreshClock("resume");
                return null;
            case WmTransitionDone when (int)wParam == _transitionId:
                ApplyRegion(union: false);
                _log?.Write("transition.end", ("dir", State.IsExpanded ? "expand" : "collapse"));
                return 0;
            case WmCommandFromInstance:
                Apply(wParam == 2 ? IslandInput.Toggle : IslandInput.Expand, "instance");
                return 0;
            case WmDpiChanged:
                Place("dpi_changed");
                _tray?.UpdateDpi(PrimaryWorkArea().Dpi);
                _fullscreen?.Schedule();
                return 0;
            case WmDisplayChange:
                Place("display_changed");
                _tray?.UpdateDpi(PrimaryWorkArea().Dpi);
                _fullscreen?.Schedule();
                return null;
            case WmSettingChange when wParam == (nint)SpiSetWorkArea:
                Place("work_area_changed");
                _fullscreen?.Schedule();
                return null;
            case WmSettingChange when wParam == (nint)SpiSetClientAreaAnimation:
                _reducedMotion = !AnimationsEnabled() || _settings.ReduceMotion;
                return null;
            case WmClose:
                _ = DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                // Stop everything that could call back into a closing island: hover timers, clock, full-screen
                // hooks, tray, UIA.
                _controller.Shutdown();
                _ = KillTimer(hwnd, SaveTimerId);
                SaveSettings(); // a change made less than a second before exit is not lost
                _fullscreen?.Dispose();
                _fullscreen = null;
                _settingsWindow?.Dispose();
                _settingsWindow = null;
                _ = KillTimer(hwnd, IntentTimerId);
                _ = KillTimer(hwnd, ClockTimerId);
                _ = WTSUnRegisterSessionNotification(hwnd);
                if (_displayNotification != 0)
                {
                    _ = UnregisterPowerSettingNotification(_displayNotification);
                    _displayNotification = 0;
                }

                if (_tray?.Remove() is { } removed)
                {
                    // The shell's own answer: true only if it still had the icon.
                    _log?.Write("tray", ("status", removed ? "removed" : "remove_failed"), ("reason", "exit"), ("hwnd", (long)_hwnd));
                }

                _automation?.Disconnect();
                _automation = null;
                PostQuitMessage(0);
                return 0;
            default:
                // Explorer restarted: the work area may have changed (FR-ISL-11) and the tray icon is gone.
                if (msg == _taskbarCreated && _taskbarCreated != 0)
                {
                    Place("taskbar_created");
                    AddTray("taskbar_created");
                    _fullscreen?.OnTaskbarCreated();
                }

                return null;
        }
    }

    private void Apply(IslandInput input, string source) => Present(_controller.Apply(input), source);

    /// <summary>Shows the result of one input: feedback, a shape transition, the input region and UIA events.</summary>
    private void Present(IslandTransition transition, string source)
    {
        if (!transition.Changed)
        {
            return;
        }

        var state = transition.Next;
        if (transition.PhaseChanged)
        {
            _log?.Write("phase", ("phase", PhaseToken(state.Phase)), ("source", source));
        }

        if (transition.AlertChanged)
        {
            _visuals?.ShowAlert(state.Alert?.Content);
            LogAlert(transition, source);
            if (state.Alert is { } shown && _settings.AnnounceAlerts)
            {
                var hr = _automation?.Announce(shown.Content.Text, $"Halo.Alert.{AlertToken(shown.Kind)}", shown.Priority == AlertPriority.Critical);
                _log?.Write("announce", ("listening", hr is null ? 0 : 1), ("hresult", hr ?? 0));
            }
        }

        if (transition.VisibilityChanged)
        {
            ApplyVisibility(source);
        }

        var nameBefore = AccessibleName;
        if (transition.PresentationChanged)
        {
            ApplyRegion(union: true);
            var id = ++_transitionId;
            _log?.Write("transition.start", ("dir", state.IsExpanded ? "expand" : "collapse"), ("source", source));
            var batch = _visuals?.Animate(transition, _reducedMotion);
            if (batch is not null)
            {
                batch.Completed += (_, _) => PostMessageW(_hwnd, WmTransitionDone, id, 0);
            }
            else
            {
                _ = PostMessageW(_hwnd, WmTransitionDone, id, 0);
            }
        }
        else
        {
            _ = _visuals?.Animate(transition, _reducedMotion);
        }

        _automation?.RaiseChanged(transition.Previous.IsExpanded, state.IsExpanded, nameBefore, AccessibleName);
    }

    private void RefreshClock(string reason)
    {
        var nameBefore = AccessibleName;
        _clock = ClockFace.Read(DateTimeOffset.Now, CultureInfo.CurrentCulture);
        if (State.Visible)
        {
            _visuals?.ShowClock(_clock);
        }
        else
        {
            // FR-ISL-12: nothing is drawn while hidden; the text is redrawn when the island is shown again.
            _clockDirty = true;
        }

        _automation?.RaiseChanged(State.IsExpanded, State.IsExpanded, nameBefore, AccessibleName);
        _log?.Write("clock", ("reason", reason));
        ArmClock();
    }

    private static bool IsIntlChange(nint lParam) =>
        lParam != 0 && new string((char*)lParam).Equals("intl", StringComparison.Ordinal);

    /// <summary>GUID_SESSION_DISPLAY_STATUS from a POWERBROADCAST_SETTING: true = on or dimmed, false = off.</summary>
    private static bool? DisplayState(nint lParam)
    {
        if (lParam == 0)
        {
            return null;
        }

        var setting = (byte*)lParam;
        var guid = *(Guid*)setting;
        var length = *(uint*)(setting + 16);
        return guid == GuidSessionDisplayStatus && length >= 1 ? setting[20] != 0 : null;
    }

    /// <summary>
    /// Stops the minute clock (no timer, no wake-ups) while the session is locked or the display is off; refreshes it
    /// immediately when both end, so the time is right as soon as the user can see it.
    /// </summary>
    private void SetClockPaused(bool locked, bool displayOff, string reason)
    {
        var wasPaused = _sessionLocked || _displayOff;
        (_sessionLocked, _displayOff) = (locked, displayOff);
        var paused = locked || displayOff;
        if (paused && !wasPaused)
        {
            _ = KillTimer(_hwnd, ClockTimerId);
            _log?.Write("clock", ("reason", "paused"), ("by", reason));
        }
        else if (!paused && wasPaused)
        {
            RefreshClock("resumed");
        }
    }

    /// <summary>One-shot timer to just after the next minute boundary (no periodic timer while idle).</summary>
    private void ArmClock()
    {
        if (_sessionLocked || _displayOff)
        {
            return;
        }

        var delay = ClockFace.DelayToNextMinute(DateTimeOffset.Now, ClockSlack);
        _ = SetTimer(_hwnd, ClockTimerId, (uint)Math.Ceiling(delay.TotalMilliseconds), 0);
    }

    /// <summary>
    /// Places the island from the live work area and DPI (nothing cached). When the result equals the current layout
    /// (frequent: every work-area or display notification), nothing is moved, rebuilt or redrawn.
    /// </summary>
    private void Place(string reason)
    {
        var (work, dpi) = PrimaryWorkArea();
        var layout = IslandLayout.Compute(new PixelRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), (int)dpi);
        if (layout == _layout)
        {
            _log?.Write("placement.unchanged", ("reason", reason));
            return;
        }

        _layout = layout;
        var c = _layout.Canvas;
        _ = SetWindowPos(_hwnd, HwndTopmost, c.Left, c.Top, c.Width, c.Height, SwpNoActivate);
        ApplyRegion(union: false);
        _visuals?.ApplyLayout(_layout, State);
        _log?.Write("placement", ("reason", reason), ("left", c.Left), ("top", c.Top), ("width", c.Width), ("height", c.Height), ("dpi", (int)dpi));
    }

    private void ApplyRegion(bool union)
    {
        static (int, int, int, int, int) Shape(RoundedBox r) =>
            ((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom), (int)Math.Round(r.Radius));
        if (union)
        {
            SetRoundedRegion(_hwnd, Shape(_layout.Compact), Shape(_layout.Expanded));
        }
        else
        {
            SetRoundedRegion(_hwnd, Shape(_layout.ShapeFor(State.Presentation)));
        }
    }

    /// <summary>Puts the current settings into effect: hover timing, motion, full-screen suppression.</summary>
    private void ApplySettings(string reason)
    {
        var s = _settings;
        _controller.SetTiming(new IslandTiming(TimeSpan.FromMilliseconds(s.HoverDwellMs), TimeSpan.FromMilliseconds(s.LeaveGraceMs)) { HoverEnabled = s.HoverToExpand });
        _reducedMotion = !AnimationsEnabled() || s.ReduceMotion;
        if (_fullscreen is not null)
        {
            if (s.HideInFullscreen && !_fullscreen.IsRunning)
            {
                var started = _fullscreen.Start();
                _log?.Write("fullscreen.watch", ("status", started ? "running" : "failed"));
                if (!started)
                {
                    _fileLog?.Write(LogLevel.Warning, "fullscreen.unavailable");
                }
            }
            else if (!s.HideInFullscreen && _fullscreen.IsRunning)
            {
                _fullscreen.Stop();
                _log?.Write("fullscreen.watch", ("status", "off"));
                if (State.Suppressed)
                {
                    Apply(IslandInput.FullscreenEnded, "settings");
                }
            }
        }

        _log?.Write("settings.applied", ("reason", reason), ("hover", s.HoverToExpand ? 1 : 0), ("dwell_ms", s.HoverDwellMs), ("grace_ms", s.LeaveGraceMs), ("reduce_motion", s.ReduceMotion ? 1 : 0), ("fullscreen", s.HideInFullscreen ? 1 : 0));
    }

    private void SaveSettings()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        try
        {
            _log?.Write("settings.saved", ("written", _store.Save(_settings) ? 1 : 0));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // NFR-05: a failed save never takes the island down; the settings stay in effect for this session.
            _log?.Write("settings.save_failed", ("hresult", ex.HResult));
            _fileLog?.Write(LogLevel.Warning, "settings.save_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
        }
    }

    private static string SettingsToken(SettingsLoadStatus status) => status switch
    {
        SettingsLoadStatus.Missing => "missing",
        SettingsLoadStatus.Loaded => "loaded",
        SettingsLoadStatus.Migrated => "migrated",
        SettingsLoadStatus.RecoveredFromBackup => "recovered_backup",
        _ => "recovered_defaults",
    };

    /// <summary>Requests a transient alert through the controller (the single arbitration path).</summary>
    internal void RaiseAlert(IslandAlert alert, string source)
    {
        var transition = _controller.Raise(alert);
        if (!transition.Changed)
        {
            _log?.Write("alert", ("state", "queued"), ("kind", AlertToken(alert.Kind)), ("priority", (int)alert.Priority), ("source", source));
        }

        Present(transition, source);
    }

    private static IslandAlert TestAlert(int priority, long flags)
    {
        var p = (AlertPriority)Math.Clamp(priority, 0, 3);
        var ms = Math.Clamp(flags >> 8, 100, 10_000);
        return new IslandAlert(
            $"test.{(int)p}",
            AlertKind.Test,
            p,
            new AlertContent("\uE767", $"Test alert {(int)p}", 0.5, "50"),
            TimeSpan.FromMilliseconds(ms),
            TimeSpan.FromSeconds(5),
            (flags & 1) != 0);
    }

    private void LogAlert(IslandTransition transition, string source)
    {
        var (before, after) = (transition.Previous.Alert, transition.Next.Alert);
        var stateToken = after is null ? "ended" : before is null ? "shown" : string.Equals(before.Key, after.Key, StringComparison.Ordinal) ? "updated" : "replaced";
        var kind = (after ?? before)!.Kind;
        _log?.Write("alert", ("state", stateToken), ("kind", AlertToken(kind)), ("priority", (int)(after ?? before)!.Priority), ("source", source));
    }

    private static string AlertToken(AlertKind kind) => kind switch
    {
        AlertKind.Volume => "volume",
        AlertKind.Battery => "battery",
        AlertKind.Power => "power",
        AlertKind.Network => "network",
        AlertKind.Media => "media",
        _ => "test",
    };

    /// <summary>Shows or hides the window for full-screen suppression, without activating it.</summary>
    private void ApplyVisibility(string source)
    {
        if (State.Visible)
        {
            if (_clockDirty)
            {
                _clockDirty = false;
                _visuals?.ShowClock(_clock);
            }

            // Back on top of a topmost full-screen window when the user asked to see the island.
            _ = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoActivate | SwpNoMove | SwpNoSize);
            _ = ShowWindow(_hwnd, SwShowNoActivate);
        }
        else
        {
            _ = ShowWindow(_hwnd, SwHide);
        }

        _log?.Write("visibility", ("visible", State.Visible ? 1 : 0), ("suppressed", State.Suppressed ? 1 : 0), ("show_now", State.ShowNow ? 1 : 0), ("source", source));
    }

    /// <summary>Debounced full-screen evaluation (FR-ISL-09).</summary>
    private void OnFullscreenTimer()
    {
        if (_fullscreen is null)
        {
            return;
        }

        var verdict = _fullscreen.Evaluate(out var kind);
        if (verdict == FullscreenVerdict.Fullscreen && !State.Suppressed)
        {
            _log?.Write("fullscreen", ("state", "started"), ("foreground", KindToken(kind)));
            Apply(IslandInput.FullscreenStarted, "fullscreen");
        }
        else if (verdict == FullscreenVerdict.NotFullscreen && State.Suppressed)
        {
            _log?.Write("fullscreen", ("state", "ended"), ("foreground", KindToken(kind)));
            Apply(IslandInput.FullscreenEnded, "fullscreen");
        }
    }

    private static string KindToken(ForegroundKind kind) => kind switch
    {
        ForegroundKind.None => "none",
        ForegroundKind.Desktop => "desktop",
        ForegroundKind.TransientShell => "shell",
        ForegroundKind.OwnProcess => "own",
        _ => "application",
    };

    private static string PhaseToken(IslandPhase phase) => phase switch
    {
        IslandPhase.Collapsed => "collapsed",
        IslandPhase.ExpandPending => "expand_pending",
        IslandPhase.ExpandedByHover => "expanded_hover",
        IslandPhase.CollapsePending => "collapse_pending",
        IslandPhase.ExpandedPinned => "expanded_pinned",
        _ => "unknown",
    };

    /// <summary>True when a client-area point lies inside the shape of the current (target) presentation.</summary>
    private bool InsideTarget(int clientX, int clientY) => _layout.ShapeFor(State.Presentation).Contains(clientX, clientY);

    private void SetHovered(bool hovered)
    {
        if (hovered != State.Hovered)
        {
            Apply(hovered ? IslandInput.PointerEntered : IslandInput.PointerLeft, "pointer");
        }
    }

    /// <summary>Re-reads the pointer after a menu closes, because pointer messages were ignored while it was open.</summary>
    private void SyncPointer() => SetHovered(PointerInside());

    /// <summary>True when the pointer is over this window and inside the current target shape.</summary>
    private bool PointerInside() =>
        GetCursorPos(out var p)
        && GetAncestor(WindowFromPoint(p), 2) == _hwnd
        && InsideTarget(p.X - _layout.Canvas.Left, p.Y - _layout.Canvas.Top);

    private void AddTray(string reason)
    {
        if (_tray is null)
        {
            return;
        }

        var ok = _fault != IslandFault.Tray && _tray.Add(PrimaryWorkArea().Dpi);

        // The island stays fully usable without the icon (right-click menu, --exit), and TaskbarCreated retries.
        _log?.Write("tray", ("status", ok ? "added" : "failed"), ("reason", reason), ("hwnd", (long)_hwnd));
        if (!ok)
        {
            _fileLog?.Write(LogLevel.Warning, "tray.failed", ("reason", reason));
        }
    }

    /// <summary>Tray icon events (NOTIFYICON_VERSION_4): left click or Space/Enter toggles, the context menu offers the rest.</summary>
    private void OnTrayEvent(uint trayEvent, int x, int y)
    {
        switch (trayEvent)
        {
            case NinSelect:
                Apply(IslandInput.Toggle, "tray");
                break;
            case NinKeySelect:
                // Enter can deliver NIN_KEYSELECT twice, so the keyboard opens rather than toggles.
                Apply(IslandInput.Expand, "tray");
                break;
            case WmContextMenu:
                ShowTrayMenu(x, y);
                break;
        }
    }

    private void ShowTrayMenu(int x, int y)
    {
        var align = (GetSystemMetrics(SmMenuDropAlignment) != 0 ? TpmRightAlign : 0) | TpmBottomAlign;
        // "Show island now" exists only while a full-screen app suppresses the island; checked while it is shown anyway.
        (int, string?, bool)[] items = State.Suppressed
            ? [(TrayOpen, "Open Halo", false), (TrayCollapse, "Collapse Halo", false), (TrayShowNow, "Show island now", State.ShowNow), (TraySettings, "Settings\u2026", false), (0, null, false), (TrayExit, "Exit Halo", false)]
            : [(TrayOpen, "Open Halo", false), (TrayCollapse, "Collapse Halo", false), (TraySettings, "Settings\u2026", false), (0, null, false), (TrayExit, "Exit Halo", false)];
        var command = RunMenu(x, y, align, items, TrayOpen);
        switch (command)
        {
            case TrayOpen:
                Apply(IslandInput.Expand, "tray");
                break;
            case TrayCollapse:
                Apply(IslandInput.Collapse, "tray");
                break;
            case TraySettings:
                _settingsWindow = SettingsWindow.Open(this, _settingsWindow);
                _log?.Write("settings.window", ("state", "open"));
                break;
            case TrayShowNow:
                Apply(State.ShowNow ? IslandInput.ShowNowCleared : IslandInput.ShowNowRequested, "tray");
                break;
            case TrayExit:
                _log?.Write("exit.request", ("source", "tray"));
                _ = PostMessageW(_hwnd, WmClose, 0, 0);
                break;
        }
    }

    private void ShowIslandMenu(int x, int y)
    {
        // The item reflects the state when the menu opens; it maps to Expand or Collapse, never Toggle, so it does
        // what its label says even if the state changes while the menu is open.
        var expanded = State.IsExpanded;
        var command = RunMenu(x, y, 0, [(expanded ? MenuCollapse : MenuExpand, expanded ? "Collapse" : "Expand", false), (0, null, false), (MenuExit, "Exit Halo", false)], 0);
        switch (command)
        {
            case MenuExpand:
                Apply(IslandInput.Expand, "menu");
                break;
            case MenuCollapse:
                Apply(IslandInput.Collapse, "menu");
                break;
            case MenuExit:
                _log?.Write("exit.request", ("source", "menu"));
                _ = PostMessageW(_hwnd, WmClose, 0, 0);
                break;
        }
    }

    /// <summary>
    /// Shows a popup menu at a screen point and returns the chosen id (0 if dismissed). Menus need their owner in the
    /// foreground, so the island activates only for this explicit user action and hands the foreground back to the
    /// previous window afterwards (FR-ISL-06). Pointer messages are ignored while the menu is open and re-read after.
    /// </summary>
    private int RunMenu(int x, int y, uint align, ReadOnlySpan<(int Id, string? Text, bool Checked)> items, int defaultId)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return 0;
        }

        Apply(IslandInput.MenuRequested, "menu");
        _menuOpen = true;
        try
        {
            foreach (var (id, text, isChecked) in items)
            {
                _ = text is null ? AppendMenuW(menu, MfSeparator, 0, null) : AppendMenuW(menu, MfString | (isChecked ? MfChecked : 0), (nuint)id, text);
            }

            if (defaultId != 0)
            {
                _ = SetMenuDefaultItem(menu, (uint)defaultId, 0);
            }

            var previous = GetForegroundWindow();
            _ = SetForegroundWindow(_hwnd);
            var command = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton | align, x, y, _hwnd, 0);
            _ = PostMessageW(_hwnd, 0, 0, 0);
            if (previous != 0 && previous != _hwnd && IsWindow(previous))
            {
                _ = SetForegroundWindow(previous);
            }

            return command;
        }
        finally
        {
            _ = DestroyMenu(menu);
            _menuOpen = false;
            if (!_controller.IsShutDown)
            {
                SyncPointer();
            }
        }
    }

    /// <summary>The controller's one-shot timer as a Win32 timer on the UI thread (no extra thread).</summary>
    private sealed class WindowTimer(IslandWindow owner) : IIslandTimer
    {
        public void Start(IslandTimerSlot slot, TimeSpan delay)
        {
            // USER_TIMER_MINIMUM is 10 ms; a zero dwell therefore fires on the next timer tick.
            var ms = (uint)Math.Max(10, Math.Ceiling(delay.TotalMilliseconds));
            if (owner._hwnd != 0)
            {
                _ = SetTimer(owner._hwnd, Id(slot), ms, 0);
            }
        }

        public void Cancel(IslandTimerSlot slot)
        {
            if (owner._hwnd != 0)
            {
                _ = KillTimer(owner._hwnd, Id(slot));
            }
        }

        private static nuint Id(IslandTimerSlot slot) => slot == IslandTimerSlot.Alert ? AlertTimerId : IntentTimerId;
    }
}

/// <summary>Fault injection for desktop tests; honoured only together with <c>--diagnostics</c>.</summary>
internal enum IslandFault
{
    None,

    /// <summary>Behave as if the shell refused the tray icon.</summary>
    Tray,

    /// <summary>Fail after the window, visuals and tray icon exist, before the message loop.</summary>
    Startup,
}

/// <summary>
/// Optional diagnostics on standard output (<c>--diagnostics</c>), used by tests and the measurement harness.
/// JSON lines with an event name, a QPC timestamp, numbers and fixed tokens only — never user content.
/// </summary>
internal sealed class DiagnosticsLog(TextWriter writer)
{
    public void Write(string name, params ReadOnlySpan<(string Key, object Value)> fields)
    {
        var line = new System.Text.StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"{{\"event\":\"{name}\",\"qpc\":{System.Diagnostics.Stopwatch.GetTimestamp()}");
        foreach (var (key, value) in fields)
        {
            line.Append(CultureInfo.InvariantCulture, $",\"{key}\":");
            if (value is string s)
            {
                line.Append(CultureInfo.InvariantCulture, $"\"{s}\"");
            }
            else
            {
                line.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        line.Append('}');
        writer.WriteLine(line.ToString());
        writer.Flush();
    }
}
