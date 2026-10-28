using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Halo.App.Interop;
using Halo.Core.Accessibility;
using Halo.Core.Activities;
using Halo.Core.Audio;
using Halo.Core.Clock;
using Halo.Core.Diagnostics;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Motion;
using Halo.Core.Network;
using Halo.Core.Power;
using Halo.Core.Settings;
using Halo.Core.Shell;
using Halo.Core.Timers;
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
internal sealed unsafe partial class IslandWindow : IDisposable
{
    public const string ClassName = "ProjectHalo.Island";
    public const string Title = "Halo";

    /// <summary>Message from a second launch: wParam 1 = expand, 2 = toggle.</summary>
    public const uint WmCommandFromInstance = WmApp + 1;

    private const uint WmTransitionDone = WmApp + 100;

    /// <summary>A UI Automation request (wParam = <see cref="AutomationRequest"/>, lParam = value) posted from a UIA thread.</summary>
    private const uint WmAutomationRequest = WmApp + 101;
    private const uint SpiSetClientAreaAnimation = 0x1043;
    private const nuint ClockTimerId = 1;
    private const nuint IntentTimerId = 2;
    private const nuint FullscreenTimerId = 3;
    private const nuint AlertTimerId = 4;
    private const nuint SaveTimerId = 5;
    private const nuint MediaDebounceTimerId = 6;
    private const nuint MediaTickTimerId = 7;
    private const nuint VolumeDeviceTimerId = 8;
    private const nuint NetworkDebounceTimerId = 9;

    /// <summary>FR-SYS-04: network changes are read after 2 s without another change (flapping links give one reading).</summary>
    private const uint NetworkDebounceMs = 2000;

    /// <summary>Device notifications arrive in bursts (unplug: state, removed, default changed): one re-bind per burst.</summary>
    private const uint VolumeDeviceDebounceMs = 200;

    /// <summary>Media events closer together than this become one refresh (no flicker on rapid metadata changes).</summary>
    private const uint MediaDebounceMs = 150;

    /// <summary>Settings are written at most once a second (ADR-001 §8 debounce).</summary>
    private const uint SaveDebounceMs = 1000;

    /// <summary>Diagnostics-only test hook: raise a synthetic alert (wParam = priority 0–3, lParam bit 0 = allowed
    /// over full screen, bits 8+ = duration in ms). Ignored without --diagnostics.</summary>
    private const uint WmTestAlert = WmApp + 50;

    /// <summary>Diagnostics-only test hook: a synthetic power reading through the real path (wParam = ACLineStatus |
    /// BatteryFlag &lt;&lt; 8 | BatteryLifePercent &lt;&lt; 16). Ignored without --diagnostics or while battery is off.</summary>
    private const uint WmTestPower = WmApp + 51;

    /// <summary>Diagnostics-only test hook: a synthetic network reading (wParam = NetworkState | NetworkKind &lt;&lt; 8).</summary>
    private const uint WmTestNetwork = WmApp + 52;

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
    private const int TrayTasks = 16;

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
    private MediaWatcher? _media;
    private MediaSnapshot? _mediaSnapshot;
    private bool _mediaUnavailableLogged;
    private MediaCommand? _pressedMedia;
    private SeekBand? _seekBand;
    private int? _pressedAction;
    private int? _pressedDot;
    private string? _pressedMediaSession;
    private VolumeWatcher? _volume;
    private bool _volumeUnavailableLogged;
    private bool _volumeDragging;
    private bool _volumeMutePressed;
    private int _volumeSetPercent = -1;
    private readonly BatteryMonitor _battery = new(BatteryThresholds.Default);
    private bool _batteryWatching;
    private nint _acdcNotification;
    private nint _batteryNotification;
    private NetworkWatcher? _network;
    private readonly NetworkMonitor _networkMonitor = new();
    private bool _networkUnavailableLogged;
    private IslandLayout _layout = null!;
    private ClockReading _clock = null!;
    private bool _tracking;
    private bool _menuOpen;
    private bool _clockDirty;
    private bool _sessionLocked;
    private bool _displayOff;
    private nint _displayNotification;
    private bool _reducedMotion;
    private readonly MotionTracker _motion = new();

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
        _automation = new IslandAutomationProvider(_hwnd, WmAutomationRequest);
        PublishAutomation();
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

        InitActivities();
        InitReminders();
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
            if (_settingsWindow?.PreTranslate(&msg) == true || _tasksWindow?.PreTranslate(&msg) == true)
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
        if ((HandleActivityMessage(msg, wParam) ?? HandleReminderMessage(msg, wParam)) is { } handled)
        {
            return handled;
        }

        switch (msg)
        {
            case WmMouseMove when _volumeDragging:
                SetVolumeFromPointer(LowWord(lParam));
                return 0;
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
            case WmLButtonDown when VolumePartAt(LowWord(lParam), HighWord(lParam)) is { } part:
                // The volume row acts on the endpoint and, like the media buttons, never collapses the island.
                _ = SetCapture(hwnd);
                _volumeSetPercent = -1;
                if (part == VolumePart.Mute)
                {
                    _volumeMutePressed = true;
                }
                else
                {
                    _volumeDragging = true;
                    SetVolumeFromPointer(LowWord(lParam));
                }

                return 0;
            case WmLButtonUp when _volumeDragging || _volumeMutePressed:
                var toggleMute = _volumeMutePressed && VolumePartAt(LowWord(lParam), HighWord(lParam)) == VolumePart.Mute;
                (_volumeDragging, _volumeMutePressed) = (false, false);
                _ = ReleaseCapture();
                if (toggleMute && _volume?.Current is { } now)
                {
                    var muted = _volume.SetMuted(!now.Muted);
                    _log?.Write("volume.command", ("command", now.Muted ? "unmute" : "mute"), ("result", muted ? "accepted" : "failed"));
                }

                SyncPointer(); // the drag may have ended outside the island
                return 0;
            case WmCaptureChanged when _volumeDragging || _volumeMutePressed:
                (_volumeDragging, _volumeMutePressed) = (false, false);
                return 0;
            case WmLButtonDown when ActivityButtonAt(LowWord(lParam), HighWord(lParam)) is { } action:
                // An activity action (pause, dismiss…): like the media buttons, it never toggles the island.
                _pressedAction = action;
                _ = SetCapture(hwnd);
                return 0;
            case WmLButtonUp when _pressedAction is { } pressedAction:
                _pressedAction = null;
                _ = ReleaseCapture();
                if (ActivityButtonAt(LowWord(lParam), HighWord(lParam)) == pressedAction)
                {
                    RunPrimaryAction(pressedAction, "pointer");
                }

                return 0;
            case WmLButtonDown when PageDotAt(LowWord(lParam), HighWord(lParam)) is { } dot:
                _pressedDot = dot;
                _ = SetCapture(hwnd);
                return 0;
            case WmLButtonUp when _pressedDot is { } pressedDot:
                _pressedDot = null;
                _ = ReleaseCapture();
                if (PageDotAt(LowWord(lParam), HighWord(lParam)) == pressedDot)
                {
                    ShowPageAt(pressedDot, "pointer");
                }

                return 0;
            case WmCaptureChanged when _pressedAction is not null || _pressedDot is not null:
                (_pressedAction, _pressedDot) = (null, null);
                return 0;
            case WmMouseWheel when State.IsExpanded && State.Visible:
                // FR-ISL-08: the wheel steps through the pages of the open island.
                StepPage((short)HighWord(wParam) > 0 ? -1 : 1, "wheel");
                return 0;
            case WmLButtonDown when SeekBandAt(LowWord(lParam), HighWord(lParam)) is { } band:
                // J-F02: a click on the progress bar of a seekable session seeks there (on release, so a slip off cancels).
                _seekBand = band;
                _pressedMediaSession = _media?.Selected.Id;
                _ = SetCapture(hwnd);
                return 0;
            case WmLButtonUp when _seekBand is { } seek:
                _seekBand = null;
                _ = ReleaseCapture();
                if (seek.Contains(LowWord(lParam), HighWord(lParam)) && _media?.Selected is { Session: { } seekTarget, Id: var seekId } && string.Equals(seekId, _pressedMediaSession, StringComparison.Ordinal))
                {
                    MediaWatcher.Seek(seekTarget, seek.FractionAt(LowWord(lParam)), _log);
                }

                return 0;
            case WmLButtonDown when MediaButtonAt(LowWord(lParam), HighWord(lParam)) is { } command:
                // A transport button: it acts on the session and does not collapse or toggle the island.
                _pressedMedia = command;
                _pressedMediaSession = _media?.Selected.Id;
                _ = SetCapture(hwnd);
                return 0;
            case WmLButtonDown:
                _ = SetCapture(hwnd);
                Apply(IslandInput.PointerPressed, "pointer");
                return 0;
            case WmLButtonUp when _pressedMedia is { } pressed:
                _pressedMedia = null;
                _ = ReleaseCapture();
                if (MediaButtonAt(LowWord(lParam), HighWord(lParam)) == pressed)
                {
                    RunMediaCommand(pressed, _pressedMediaSession);
                }

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
            case WmAutomationRequest:
                OnAutomationRequest((AutomationRequest)(int)wParam, (int)lParam);
                return 0;
            case WmTimer when (nuint)wParam == ClockTimerId:
                RefreshClock("minute");
                return 0;
            case WmTimer when (nuint)wParam == IntentTimerId:
                // Win32 timers repeat; the island's timers are one-shot.
                _ = KillTimer(hwnd, IntentTimerId);
                Present(_controller.TimerElapsed(IslandTimerSlot.Intent), "hover");
                return 0;
            case MediaWatcher.DirtyMessage:
                _ = SetTimer(hwnd, MediaDebounceTimerId, MediaDebounceMs, 0);
                return 0;
            case WmTimer when (nuint)wParam == MediaDebounceTimerId:
                _ = KillTimer(hwnd, MediaDebounceTimerId);
                OnMediaDirty();
                return 0;
            case MediaWatcher.SnapshotMessage:
                ApplyMedia(_media?.Latest);
                return 0;
            case VolumeWatcher.ChangedMessage:
                OnVolumeChanged();
                return 0;
            case VolumeWatcher.DeviceMessage:
                _ = SetTimer(hwnd, VolumeDeviceTimerId, VolumeDeviceDebounceMs, 0);
                return 0;
            case WmTimer when (nuint)wParam == VolumeDeviceTimerId:
                _ = KillTimer(hwnd, VolumeDeviceTimerId);
                if (_volume is not null)
                {
                    _volume.Rebind();
                    ShowVolumeDevice("device_changed");
                }

                return 0;
            case WmTimer when (nuint)wParam == MediaTickTimerId:
                _visuals?.UpdateMediaProgress(DateTimeOffset.Now);
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
                // Countdowns run on monotonic time and are unaffected; quiet mode's end is a wall-clock time.
                SetQuiet(_quietMode.IsActive(DateTimeOffset.UtcNow) ? _quietMode : QuietMode.Off, "time_change");
                CheckReminders("time_change");
                return 0;
            case WmPowerBroadcast when wParam == PbtApmSuspend:
                OnSuspend();
                return 1;
            case WmSettingChange when IsColorSetChange(lParam) || wParam == (nint)SpiSetHighContrast:
                // Windows' light/dark mode, accent colour or high contrast changed (I-F01).
                ApplyTheme("system");
                return null;
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
            case WmPowerBroadcast when wParam == PbtPowerSettingChange && IsBatterySetting(lParam):
            case WmPowerBroadcast when wParam == PbtApmPowerStatusChange:
                // FR-SYS-02/03: charger and charge changes; the reading itself always comes from GetSystemPowerStatus.
                if (_batteryWatching)
                {
                    ReadPower("system");
                }

                return 1;
            case NetworkWatcher.ChangedMessage:
                // Restarting the timer on every change is the debounce: one read after 2 s of quiet.
                _ = SetTimer(hwnd, NetworkDebounceTimerId, NetworkDebounceMs, 0);
                _log?.Write("network.event");
                return 0;
            case WmTimer when (nuint)wParam == NetworkDebounceTimerId:
                _ = KillTimer(hwnd, NetworkDebounceTimerId);
                _network?.Read();
                return 0;
            case NetworkWatcher.ReadingMessage:
                _log?.Write("network.read", ("hresult", _network?.LastHResult ?? 0));
                if (_network?.Latest is { } networkReading)
                {
                    OnNetworkReading(networkReading, "system");
                }

                return 0;
            case WmTestNetwork when _log is not null && _network is not null:
                OnNetworkReading(new NetworkReading((NetworkState)(wParam & 0xFF), (NetworkKind)((wParam >> 8) & 0xFF)), "test");
                return 0;
            case WmTestPower when _log is not null && _batteryWatching:
                OnPowerReading(PowerReading.FromSystem((byte)(wParam & 0xFF), (byte)((wParam >> 8) & 0xFF), (byte)((wParam >> 16) & 0xFF)), "test");
                return 0;
            case WmPowerBroadcast when wParam == PbtApmResumeAutomatic:
                // Displays may have changed while asleep (FR-ISL-11): re-place, re-check full screen, refresh the clock.
                Place("resume");
                _fullscreen?.Schedule();
                RefreshClock("resume");
                OnResume();
                CheckReminders("resume");
                return null;
            case WmTransitionDone when _motion.Complete((int)wParam):
                ApplyRegion(union: false);
                _log?.Write("transition.end", ("dir", State.IsExpanded ? "expand" : "collapse"));
                return 0;
            case WmHotkey when (int)wParam == HotkeyId:
                // I-F05: the user's shortcut opens or closes the island, like the tray icon. It never takes focus.
                Apply(IslandInput.Toggle, "hotkey");
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
                // hooks, tray, UIA, and completion callbacks of animations still running.
                StopActivityTimers();
                _ = KillTimer(hwnd, ReminderTimerId);
                ReleaseHotkey();
                SaveState(); // a running countdown survives the exit (FR-TIM-02)
                SaveTasks();
                _tasksWindow?.Dispose();
                _tasksWindow = null;
                _controller.Shutdown();
                _motion.Shutdown();
                _ = KillTimer(hwnd, SaveTimerId);
                SaveSettings(); // a change made less than a second before exit is not lost
                _fullscreen?.Dispose();
                _fullscreen = null;
                _settingsWindow?.Dispose();
                _ = KillTimer(hwnd, MediaDebounceTimerId);
                _ = KillTimer(hwnd, MediaTickTimerId);
                _media?.Dispose();
                _media = null;
                _ = KillTimer(hwnd, VolumeDeviceTimerId);
                _volume?.Dispose();
                _volume = null;
                StopBattery();
                _ = KillTimer(hwnd, NetworkDebounceTimerId);
                _network?.Dispose();
                _network = null;
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
            _visuals?.ShowAlert(state.Alert?.Content, _reducedMotion);
            if (transition.Previous.Alert is { } before && state.Alert is { } after && !transition.PresentationChanged
                && !string.Equals(before.Key, after.Key, StringComparison.Ordinal))
            {
                _visuals?.SwapAlertContent(MotionPlanner.Swap(_reducedMotion)); // a different alert replaced this one
            }

            LogAlert(transition, source);
            if (state.Alert is { } shown && _settings.AnnounceAlerts)
            {
                var hr = _automation?.Announce(shown.Content.Spoken, $"Halo.Alert.{AlertToken(shown.Kind)}", shown.Priority == AlertPriority.Critical);
                _log?.Write("announce", ("listening", hr is null ? 0 : 1), ("hresult", hr ?? 0));
            }
        }

        if (transition.VisibilityChanged)
        {
            ApplyVisibility(source);
        }

        if (transition.PresentationChanged && state.IsExpanded && !transition.Previous.IsExpanded)
        {
            // Opening the island starts on the default page (activity, else media, else clock).
            ResetPage();
            ApplyPage(animate: false);
        }

        if (transition.PresentationChanged || transition.VisibilityChanged)
        {
            UpdateMediaTick();
        }

        if (transition.VisibilityChanged)
        {
            _ = _board.SetSuppressed(state.Hidden);
            ArmActivityTimers(); // per-second redraws only while the island can be seen
        }

        if (transition.PresentationChanged)
        {
            ApplyRegion(union: true);
            var plan = _motion.Begin(MotionPlanner.Transition(transition.Previous.Presentation, state.Presentation, _reducedMotion));
            var id = plan?.Id ?? 0;
            _log?.Write("transition.start", ("dir", state.IsExpanded ? "expand" : "collapse"), ("source", source));
            var batch = _visuals?.Animate(transition, plan, _reducedMotion);
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
            _ = _visuals?.Animate(transition, null, _reducedMotion);
        }

        PublishAutomation();
    }

    private void RefreshClock(string reason)
    {
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

        PublishAutomation();
        _log?.Write("clock", ("reason", reason));
        ArmClock();
    }

    private static bool IsColorSetChange(nint lParam) =>
        lParam != 0 && new string((char*)lParam).Equals("ImmersiveColorSet", StringComparison.Ordinal);

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
            ArmActivityTimers(); // countdowns now wake only for their completion
        }
        else if (!paused && wasPaused)
        {
            RefreshClock("resumed");
            SyncActivities("resumed");
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
        var layout = IslandLayout.Compute(new PixelRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), (int)dpi, _settings.ShowVolume);
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
        PublishAutomation();
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
        if (s.ShowMedia && _media is null && _hwnd != 0)
        {
            _media = new MediaWatcher(_hwnd, _log);
            _ = _media.StartAsync();
        }
        else if (!s.ShowMedia && _media is not null)
        {
            _media.Dispose();
            _media = null;
            ApplyMedia(null);
        }

        if (s.ShowVolume && _volume is null && _hwnd != 0)
        {
            _volume = new VolumeWatcher(_hwnd, noDevice: _fault == IslandFault.NoAudio);
            if (!_volume.Start() && !_volumeUnavailableLogged)
            {
                _volumeUnavailableLogged = true;
                _fileLog?.Write(LogLevel.Warning, "volume.unavailable", ("hresult", _volume.LastHResult));
            }

            ShowVolumeDevice(reason);
        }
        else if (!s.ShowVolume && _volume is not null)
        {
            _ = KillTimer(_hwnd, VolumeDeviceTimerId);
            _volume.Dispose();
            _volume = null;
            _visuals?.ShowVolume(null);
            _log?.Write("volume.device", ("status", "off"), ("reason", reason), ("hresult", 0));
            Present(_controller.DropAlerts(AlertKind.Volume), "settings"); // a turned-off feature leaves no pill behind
        }

        _battery.Thresholds = new BatteryThresholds(s.BatteryLowPercent, s.BatteryCriticalPercent);
        if (s.ShowBattery && !_batteryWatching && _hwnd != 0)
        {
            StartBattery(reason);
        }
        else if (!s.ShowBattery && _batteryWatching)
        {
            StopBattery();
            UpdateStatus();
            _log?.Write("power", ("battery", 0), ("percent", -1), ("source", "off"), ("charging", 0), ("reason", reason));
            Present(_controller.DropAlerts(AlertKind.Battery, AlertKind.Power), "settings");
        }

        if (s.ShowNetwork && _network is null && _hwnd != 0)
        {
            _network = new NetworkWatcher(_hwnd, fail: _fault == IslandFault.Network);
            if (!_network.Start())
            {
                LogNetworkUnavailable();
            }

            _network.Read(); // the start-up reading, without the debounce
        }
        else if (!s.ShowNetwork && _network is not null)
        {
            _ = KillTimer(_hwnd, NetworkDebounceTimerId);
            _network.Dispose();
            _network = null;
            UpdateStatus();
            _log?.Write("network", ("state", "off"), ("kind", "none"), ("reason", reason));
            Present(_controller.DropAlerts(AlertKind.Network), "settings");
        }

        if (_taskStore is not null)
        {
            // Reminders follow the settings at once: turned off, their notifications leave the island.
            RegisterReminderProvider();
            if (!s.ShowReminders)
            {
                foreach (var id in _notifications.DropProvider(RemindersProvider))
                {
                    _ = _board.Remove(id);
                }
            }

            CheckReminders("settings");
        }

        ApplyHotkey(reason);
        ApplyTheme(reason);
        UpdateStatus(); // pill content options (I-F02)

        if (_layout is not null && _layout.VolumeRow != s.ShowVolume)
        {
            // The volume row makes the expanded island taller.
            Place("settings");
        }

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

    private const int HotkeyId = 1;
    private HotkeyChoice _hotkeyRegistered;
    private bool _hotkeyApplied;

    /// <summary>
    /// The shortcut's state for the settings window: registered, off, or in use by another app (Windows refuses a
    /// shortcut that is already registered, which is how a conflict is detected).
    /// </summary>
    internal string HotkeyStatus { get; private set; } = "Off";

    /// <summary>Registers the chosen shortcut (only when it changed). RegisterHotKey: no keyboard hook, no keystrokes seen.</summary>
    private void ApplyHotkey(string reason)
    {
        var wanted = _settings.Hotkey;
        if (_hotkeyApplied && wanted == _hotkeyRegistered && (wanted == HotkeyChoice.Off || HotkeyStatus != "in_use"))
        {
            return;
        }

        _hotkeyApplied = true;
        ReleaseHotkey();
        if (Hotkeys.Keys(wanted) is not { } keys || _hwnd == 0)
        {
            HotkeyStatus = "Off";
            _log?.Write("hotkey", ("choice", Hotkeys.Token(wanted)), ("registered", 0), ("reason", reason));
            return;
        }

        var ok = RegisterHotKey(_hwnd, HotkeyId, keys.Modifiers, keys.Key);
        var error = ok ? 0 : Marshal.GetLastPInvokeError();
        _hotkeyRegistered = ok ? wanted : HotkeyChoice.Off;
        HotkeyStatus = ok ? Hotkeys.Label(wanted) : "in_use";
        _log?.Write("hotkey", ("choice", Hotkeys.Token(wanted)), ("registered", ok ? 1 : 0), ("error", error), ("reason", reason));
        if (!ok)
        {
            _fileLog?.Write(LogLevel.Warning, "hotkey.unavailable", ("choice", Hotkeys.Token(wanted)), ("error", error));
        }
    }

    private void ReleaseHotkey()
    {
        if (_hotkeyRegistered != HotkeyChoice.Off && _hwnd != 0)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyId);
        }

        _hotkeyRegistered = HotkeyChoice.Off;
    }

    private Halo.Core.Theming.IslandPalette? _palette;

    private Halo.Core.Theming.PillContent PillOptions() => new(_settings.PillShowsActivity, _settings.PillShowsMedia, _settings.PillShowsDate);

    /// <summary>
    /// Picks the palette from the settings and Windows' state (I-F01): high contrast first (system colours), then the
    /// chosen theme or Windows' app mode, then the accent for bars. Read through UISettings, the documented way for
    /// Win32 apps to follow the system theme.
    /// </summary>
    private void ApplyTheme(string reason)
    {
        if (_visuals is null)
        {
            return;
        }

        var highContrast = HighContrastColors();
        var (systemLight, accent) = SystemColors();
        var palette = Halo.Core.Theming.IslandPalette.For(_settings.Theme, systemLight, _settings.AccentBars ? accent : null, highContrast);
        if (palette == _palette)
        {
            return;
        }

        _palette = palette;
        _visuals.ApplyPalette(palette, State);
        ApplyPage(animate: false);
        UpdateStatus();
        _visuals.ShowActivity(_board.Primary, _board.OthersCount, ShowPrivate, reducedMotion: true);
        _log?.Write("theme", ("mode", _settings.Theme.ToString().ToLowerInvariant()), ("light", palette.Fill.Luminance > 0.5 ? 1 : 0), ("accent", _settings.AccentBars ? 1 : 0), ("high_contrast", highContrast is null ? 0 : 1), ("reason", reason));
    }

    private static (bool Light, Halo.Core.Theming.Rgba? Accent) SystemColors()
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            var fg = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
            var accent = ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent);
            // A dark foreground means Windows' app mode is light.
            var light = ((5 * fg.G) + (2 * fg.R) + fg.B) < (8 * 128);
            return (light, Halo.Core.Theming.Rgba.Opaque(accent.R, accent.G, accent.B));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return (false, null);
        }
    }

    private static Halo.Core.Theming.HighContrastColors? HighContrastColors()
    {
        var hc = new HighContrast { Size = (uint)sizeof(HighContrast) };
        if (!SystemParametersInfoHighContrast(SpiGetHighContrast, hc.Size, &hc, 0) || (hc.Flags & 0x1) == 0)
        {
            return null;
        }

        static Halo.Core.Theming.Rgba Sys(int index)
        {
            var c = GetSysColor(index); // 0x00BBGGRR
            return Halo.Core.Theming.Rgba.Opaque((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF));
        }

        return new Halo.Core.Theming.HighContrastColors(Sys(5), Sys(8), Sys(13), Sys(17)); // window, window text, highlight, gray text
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

    /// <summary>Debounced media event: reports a failed start once, then builds a new snapshot off the UI thread.</summary>
    private void OnMediaDirty()
    {
        if (_media is null)
        {
            return;
        }

        if (_media.Status == MediaStatus.Unavailable)
        {
            if (!_mediaUnavailableLogged)
            {
                _mediaUnavailableLogged = true;
                _log?.Write("media.status", ("status", "unavailable"), ("hresult", _media.FailureHResult));
                _fileLog?.Write(LogLevel.Warning, "media.unavailable", ("hresult", _media.FailureHResult));
            }

            return;
        }

        _media.Refresh();
    }

    /// <summary>Shows a new media snapshot (null = no session) and keeps the progress timer in step.</summary>
    private void ApplyMedia(MediaSnapshot? snapshot)
    {
        if (!_settings.ShowMedia)
        {
            snapshot = null;
        }

        if (snapshot == _mediaSnapshot)
        {
            return;
        }

        _mediaSnapshot = snapshot;
        _visuals?.ShowMedia(snapshot, DateTimeOffset.Now, _reducedMotion);
        ApplyPage(animate: false); // a session appearing or ending adds or removes the media page
        PublishAutomation();
        UpdateMediaTick();

        // Tokens only: never the title, artist or app (SECURITY_AND_PRIVACY_DESIGN §6).
        _log?.Write(
            "media",
            ("state", snapshot is null ? "none" : snapshot.Playback.ToString().ToLowerInvariant()),
            ("art", snapshot?.Art is null ? 0 : 1),
            ("timeline", snapshot?.Timeline is null ? 0 : 1),
            ("play_pause", snapshot?.CanPlayPause == true ? 1 : 0),
            ("next", snapshot?.CanNext == true ? 1 : 0),
            ("previous", snapshot?.CanPrevious == true ? 1 : 0),
            ("title_length", snapshot?.Title.Length ?? 0),
            ("sessions", snapshot?.SessionCount ?? 0),
            ("index", snapshot?.SessionIndex ?? 0),
            ("seek", snapshot?.CanSeek == true ? 1 : 0));
    }

    /// <summary>The transport button under a client point while the media card is on screen.</summary>
    private MediaCommand? MediaButtonAt(int x, int y) =>
        State.IsExpanded && CurrentPage == IslandPage.Media && _mediaSnapshot is { } m ? MediaControls.HitTest(MediaControls.Layout(_layout, m), x, y) : null;

    /// <summary>The seek band under a client point while a seekable session's card is on screen (J-F02).</summary>
    private SeekBand? SeekBandAt(int x, int y) =>
        State.IsExpanded && State.Visible && CurrentPage == IslandPage.Media && MediaControls.Seek(_layout, _mediaSnapshot) is { } band && band.Contains(x, y) ? band : null;

    /// <summary>
    /// Sends a transport command to the session that was selected when the button was pressed (a different session
    /// selected since then makes the command stale, and it is dropped). The island does not assume success: it shows
    /// whatever the session reports next. The result is logged as a token.
    /// </summary>
    private void RunMediaCommand(MediaCommand command, string? sessionId)
    {
        if (command == MediaCommand.SwitchSource)
        {
            // J-F01: not a session command; the watcher re-selects and the island redraws from the new snapshot.
            var switched = _media?.SelectNext() == true;
            _log?.Write("media.command", ("command", "switch"), ("result", switched ? "accepted" : "refused"));
            return;
        }

        var (session, currentId) = _media?.Selected ?? default;
        var token = command switch { MediaCommand.Previous => "previous", MediaCommand.Next => "next", _ => "play_pause" };
        if (session is null || !string.Equals(sessionId, currentId, StringComparison.Ordinal))
        {
            _log?.Write("media.command", ("command", token), ("result", "stale"));
            return;
        }

        MediaWatcher.Send(session, command, token, _log, _fileLog);
    }

    /// <summary>FR-MED-05: progress ticks at 1 Hz only while the media card is on screen and playing.</summary>
    private void UpdateMediaTick()
    {
        var needed = _mediaSnapshot is { IsPlaying: true, Timeline: not null } && State.IsExpanded && State.Visible && CurrentPage == IslandPage.Media;
        if (needed)
        {
            _ = SetTimer(_hwnd, MediaTickTimerId, 1000, 0);
        }
        else
        {
            _ = KillTimer(_hwnd, MediaTickTimerId);
        }
    }

    /// <summary>The open island's UIA description: day progress, then battery and volume when they are shown.</summary>
    private string ExpandedDescription()
    {
        var parts = new List<string>(3) { _clock.DayProgressText };
        if (_batteryWatching && _battery.Current is { HasBattery: true } power)
        {
            parts.Add(power.Spoken);
        }

        if (_network is not null && _networkMonitor.Current is { State: not NetworkState.Unknown } net)
        {
            parts.Add(net.Spoken);
        }

        if (_volume?.Current is { } v)
        {
            parts.Add(v.Spoken);
        }

        return string.Join(", ", parts);
    }

    /// <summary>Starts the event-driven battery feed: power-source and charge notifications, then a first reading.</summary>
    private void StartBattery(string reason)
    {
        var acdc = GuidAcDcPowerSource;
        var charge = GuidBatteryPercentageRemaining;
        _acdcNotification = RegisterPowerSettingNotification(_hwnd, &acdc, 0);
        _batteryNotification = RegisterPowerSettingNotification(_hwnd, &charge, 0);
        _batteryWatching = true;
        if (_acdcNotification == 0 || _batteryNotification == 0)
        {
            // WM_POWERBROADCAST/PBT_APMPOWERSTATUSCHANGE still arrives, so the feature keeps working.
            _fileLog?.Write(LogLevel.Warning, "power.register_failed", ("acdc", _acdcNotification != 0), ("charge", _batteryNotification != 0));
        }

        ReadPower(reason);
    }

    private void StopBattery()
    {
        if (_acdcNotification != 0)
        {
            _ = UnregisterPowerSettingNotification(_acdcNotification);
            _acdcNotification = 0;
        }

        if (_batteryNotification != 0)
        {
            _ = UnregisterPowerSettingNotification(_batteryNotification);
            _batteryNotification = 0;
        }

        _batteryWatching = false;
    }

    private void ReadPower(string reason)
    {
        if (!GetSystemPowerStatus(out var status))
        {
            _log?.Write("power.read_failed");
            return;
        }

        OnPowerReading(PowerReading.FromSystem(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent), reason);
    }

    /// <summary>A power reading from Windows (or a test): status cluster, diagnostics, and at most one alert.</summary>
    private void OnPowerReading(PowerReading reading, string reason)
    {
        if (_battery.Current == reading)
        {
            return; // several notifications for one change: nothing new
        }

        var alert = _battery.Update(reading);
        UpdateStatus();
        _log?.Write("power", ("battery", reading.HasBattery ? 1 : 0), ("percent", reading.Percent ?? -1), ("source", reading.Source.ToString().ToLowerInvariant()), ("charging", reading.Charging ? 1 : 0), ("reason", reason));
        if (alert is not null)
        {
            RaiseAlert(alert, "power");
        }

        PublishAutomation();
    }

    /// <summary>Status icons and value at the top right of the open island: network, then battery and its percent.</summary>
    private void UpdateStatus()
    {
        var power = _batteryWatching && _battery.Current is { HasBattery: true } p ? p : (PowerReading?)null;
        var network = _network is not null ? _networkMonitor.Current?.Glyph ?? string.Empty : string.Empty;
        var glyphs = string.Join(" ", new[] { network, power?.Glyph ?? string.Empty }.Where(g => g.Length > 0));
        _visuals?.ShowStatus(glyphs, power?.ShortText ?? string.Empty, _reducedMotion);
        _visuals?.ShowPillOptions(PillOptions(), _settings.PillShowsBattery ? power?.ShortText ?? string.Empty : string.Empty);
    }

    /// <summary>A connectivity reading (real or test): status icon, diagnostics, and at most one alert.</summary>
    private void OnNetworkReading(NetworkReading reading, string reason)
    {
        if (_networkMonitor.Current == reading)
        {
            return;
        }
        var alert = _networkMonitor.Update(reading);
        UpdateStatus();
        _log?.Write("network", ("state", reading.State.ToString().ToLowerInvariant()), ("kind", reading.Kind.ToString().ToLowerInvariant()), ("reason", reason));
        if (reading.State == NetworkState.Unknown)
        {
            LogNetworkUnavailable();
        }

        if (alert is not null)
        {
            RaiseAlert(alert, "network");
        }

        PublishAutomation();
    }

    private void LogNetworkUnavailable()
    {
        if (!_networkUnavailableLogged)
        {
            _networkUnavailableLogged = true;
            _fileLog?.Write(LogLevel.Warning, "network.unavailable", ("hresult", _network?.LastHResult ?? 0));
        }
    }

    private static bool IsBatterySetting(nint lParam)
    {
        if (lParam == 0)
        {
            return false;
        }

        var guid = *(Guid*)lParam;
        return guid == GuidAcDcPowerSource || guid == GuidBatteryPercentageRemaining;
    }

    /// <summary>Shows the bound device's volume (or "No audio output") and logs the binding, never the device's name or ID.</summary>
    private void ShowVolumeDevice(string reason)
    {
        if (_volume is null)
        {
            return;
        }

        _visuals?.ShowVolume(_volume.Current);
        _log?.Write("volume.device", ("status", _volume.HasDevice ? "bound" : "none"), ("reason", reason), ("hresult", _volume.LastHResult));
        if (_volume.Current is { } v)
        {
            _log?.Write("volume", ("percent", v.Percent), ("muted", v.Muted ? 1 : 0), ("source", "bind"));
        }

        PublishAutomation();
    }

    /// <summary>
    /// A volume change from any source (FR-SYS-01). The row always follows it. The pill appears only for changes made
    /// elsewhere (keys, flyout, mixer, apps) and only while the island is not open, because the open island already
    /// shows the slider. The controller decides when the pill may appear (full screen, pressed, other alerts).
    /// </summary>
    private void OnVolumeChanged()
    {
        if (_volume?.TakeChange() is not { } change)
        {
            return;
        }

        var (reading, fromHalo) = change;
        _visuals?.ShowVolume(reading, animate: !_volumeDragging, _reducedMotion);
        _log?.Write("volume", ("percent", reading.Percent), ("muted", reading.Muted ? 1 : 0), ("source", fromHalo ? "halo" : "external"));
        if (!fromHalo && !State.IsExpanded)
        {
            RaiseAlert(VolumeAlert.For(reading), "volume");
        }

        PublishAutomation();
    }

    /// <summary>The volume-row part under a client point while the open island shows a bound device.</summary>
    private VolumePart? VolumePartAt(int x, int y) =>
        State.IsExpanded && State.Visible && _volume?.HasDevice == true ? VolumeRow.For(_layout)?.HitTest(x, y) : null;

    /// <summary>Slider drag: sets the endpoint volume when the whole percent under the pointer changes.</summary>
    private void SetVolumeFromPointer(int x)
    {
        if (VolumeRow.For(_layout) is not { } row || _volume is null)
        {
            return;
        }

        var level = row.LevelAt(x);
        var percent = (int)Math.Round(level * 100);
        if (percent == _volumeSetPercent)
        {
            return;
        }

        _volumeSetPercent = percent;
        var ok = _volume.SetLevel(level);
        _log?.Write("volume.command", ("command", "set"), ("percent", percent), ("result", ok ? "accepted" : "failed"));
    }

    /// <summary>Requests a transient alert through the controller (the single arbitration path).</summary>
    internal void RaiseAlert(IslandAlert alert, string source)
    {
        var held = _controller.HeldWhileQuiet;
        var transition = _controller.Raise(alert);
        if (!transition.Changed)
        {
            var state = _controller.HeldWhileQuiet > held ? "held_quiet" : "queued";
            _log?.Write("alert", ("state", state), ("kind", AlertToken(alert.Kind)), ("priority", (int)alert.Priority), ("source", source));
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
        AlertKind.Timer => "timer",
        AlertKind.Reminder => "reminder",
        AlertKind.Notification => "notification",
        AlertKind.Halo => "halo",
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
        var items = new List<MenuEntry> { new(TrayOpen, "Open Halo"), new(TrayCollapse, "Collapse Halo") };
        if (State.Suppressed)
        {
            items.Add(new MenuEntry(TrayShowNow, "Show island now", State.ShowNow));
        }

        items.Add(new MenuEntry(TraySettings, "Settings\u2026"));
        items.Add(new MenuEntry(TrayTasks, "Tasks and reminders\u2026"));
        items.Add(new MenuEntry(0, "Timer", Children: TimerMenu()));
        items.Add(new MenuEntry(0, "Focus session", Children: FocusMenu()));
        items.Add(new MenuEntry(0, "Do Not Disturb", Children: QuietMenu()));
        items.Add(new MenuEntry(0, null));
        items.Add(new MenuEntry(TrayExit, "Exit Halo"));
        var command = RunMenu(x, y, align, items, TrayOpen);
        if (RunFeatureMenuCommand(command))
        {
            return;
        }

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
            case TrayTasks:
                OpenTasksWindow();
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
        var entries = new List<MenuEntry>(items.Length);
        foreach (var (id, text, isChecked) in items)
        {
            entries.Add(new MenuEntry(id, text, isChecked));
        }

        return RunMenu(x, y, align, entries, defaultId);
    }

    private static nint BuildMenu(IReadOnlyList<MenuEntry> items)
    {
        var menu = CreatePopupMenu();
        if (menu == 0)
        {
            return 0;
        }

        foreach (var e in items)
        {
            if (e.Text is null)
            {
                _ = AppendMenuW(menu, MfSeparator, 0, null);
            }
            else if (e.Children is { } children && BuildMenu(children) is var sub && sub != 0)
            {
                // The submenu belongs to its parent from now on; DestroyMenu on the top level destroys it.
                _ = AppendMenuW(menu, MfPopup, (nuint)sub, e.Text);
            }
            else
            {
                _ = AppendMenuW(menu, MfString | (e.Checked ? MfChecked : 0) | (e.Disabled ? MfGrayed : 0), (nuint)e.Id, e.Text);
            }
        }

        return menu;
    }

    private int RunMenu(int x, int y, uint align, IReadOnlyList<MenuEntry> items, int defaultId)
    {
        var menu = BuildMenu(items);
        if (menu == 0)
        {
            return 0;
        }

        Apply(IslandInput.MenuRequested, "menu");
        _menuOpen = true;
        try
        {

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

    /// <summary>Behave as if Windows reports no audio output device.</summary>
    NoAudio,

    /// <summary>Every network read fails as if the connectivity API were unavailable.</summary>
    Network,
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
