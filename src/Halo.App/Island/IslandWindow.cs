using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Halo.App.Interop;
using Halo.Core.Clock;
using Halo.Core.Island;
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

    // Island right-click menu.
    private const int MenuExpand = 1;
    private const int MenuExit = 2;
    private const int MenuCollapse = 3;

    private static readonly TimeSpan ClockSlack = TimeSpan.FromMilliseconds(40);

    private static IslandWindow? s_current;

    private readonly DiagnosticsLog? _log;
    private readonly IslandController _controller;
    private nint _hwnd;
    private uint _taskbarCreated;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private TextRenderer? _text;
    private IslandVisuals? _visuals;
    private IslandAutomationProvider? _automation;
    private IslandLayout _layout = null!;
    private ClockReading _clock = null!;
    private bool _tracking;
    private bool _menuOpen;
    private bool _reducedMotion;
    private int _transitionId;

    public IslandWindow(DiagnosticsLog? log)
    {
        _log = log;
        _controller = new IslandController(new WindowTimer(this), IslandTiming.Default);
    }

    public bool IsExpanded => State.IsExpanded;

    public string AccessibleName => $"Halo, {_clock.TimeWithDesignator}, {_clock.LongDate}";

    public string? AccessibleDescription => State.IsExpanded ? _clock.DayProgressText : null;

    private IslandState State => _controller.State;

    /// <summary>Creates the window and runs its message loop until the window is closed. Returns the exit code.</summary>
    public int Run()
    {
        // Windows.UI.Composition needs a DispatcherQueue on the UI thread of a Win32 app (thread type 2 = current
        // thread, apartment 2 = STA).
        var options = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = 2, ApartmentType = 2 };
        Check(CreateDispatcherQueueController(options, out _), "CreateDispatcherQueueController");
        _reducedMotion = !AnimationsEnabled();
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
        _ = ShowWindow(_hwnd, SwShowNoActivate);

        // Completes once the first frame of the visual tree has been committed to DWM.
        var ready = _compositor.GetCommitBatch(CompositionBatchTypes.Animation);
        ready.Completed += (_, _) => _log?.Write("ready", ("left", _layout.Canvas.Left), ("top", _layout.Canvas.Top), ("width", _layout.Canvas.Width), ("height", _layout.Canvas.Height), ("dpi", (int)Math.Round(_layout.Scale * 96)));

        Msg msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            _ = TranslateMessage(&msg);
            _ = DispatchMessageW(&msg);
        }

        _log?.Write("exit", ("code", 0));
        return 0;
    }

    public void RequestExpanded(bool expanded) => Apply(expanded ? IslandInput.Expand : IslandInput.Collapse, "automation");

    public void RequestToggle() => Apply(IslandInput.Toggle, "automation");

    public void Dispose()
    {
        _controller.Shutdown();
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
                    SetHovered(false);
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
                // Win32 timers repeat; the hover timers are one-shot.
                _ = KillTimer(hwnd, IntentTimerId);
                Present(_controller.TimerElapsed(), "hover");
                return 0;
            case WmTimeChange:
                RefreshClock("time_change");
                return 0;
            case WmPowerBroadcast when wParam == PbtApmResumeAutomatic:
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
                return 0;
            case WmDisplayChange:
                Place("display_changed");
                return null;
            case WmSettingChange when wParam == (nint)SpiSetWorkArea:
                Place("work_area_changed");
                return null;
            case WmSettingChange when wParam == (nint)SpiSetClientAreaAnimation:
                _reducedMotion = !AnimationsEnabled();
                return null;
            case WmClose:
                _ = DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                // Stop everything that could call back into a closing island: hover timers, clock, UIA.
                _controller.Shutdown();
                _ = KillTimer(hwnd, IntentTimerId);
                _ = KillTimer(hwnd, ClockTimerId);
                _automation?.Disconnect();
                _automation = null;
                PostQuitMessage(0);
                return 0;
            default:
                // Explorer restarted: the work area may have changed (FR-ISL-11).
                if (msg == _taskbarCreated && _taskbarCreated != 0)
                {
                    Place("taskbar_created");
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
        _visuals?.ShowClock(_clock);
        _automation?.RaiseChanged(State.IsExpanded, State.IsExpanded, nameBefore, AccessibleName);
        _log?.Write("clock", ("reason", reason));
        ArmClock();
    }

    /// <summary>One-shot timer to just after the next minute boundary (no periodic timer while idle).</summary>
    private void ArmClock()
    {
        var delay = ClockFace.DelayToNextMinute(DateTimeOffset.Now, ClockSlack);
        _ = SetTimer(_hwnd, ClockTimerId, (uint)Math.Ceiling(delay.TotalMilliseconds), 0);
    }

    private void Place(string reason)
    {
        var (work, dpi) = PrimaryWorkArea();
        _layout = IslandLayout.Compute(new PixelRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top), (int)dpi);
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
    private void SyncPointer()
    {
        var inside = GetCursorPos(out var p)
            && GetAncestor(WindowFromPoint(p), 2) == _hwnd
            && InsideTarget(p.X - _layout.Canvas.Left, p.Y - _layout.Canvas.Top);
        SetHovered(inside);
    }

    private void ShowIslandMenu(int x, int y)
    {
        // The item reflects the state when the menu opens; it maps to Expand or Collapse, never Toggle, so it does
        // what its label says even if the state changes while the menu is open.
        var expanded = State.IsExpanded;
        var command = RunMenu(x, y, 0, [(expanded ? MenuCollapse : MenuExpand, expanded ? "Collapse" : "Expand"), (0, null), (MenuExit, "Exit Halo")], 0);
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
    private int RunMenu(int x, int y, uint align, ReadOnlySpan<(int Id, string? Text)> items, int defaultId)
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
            foreach (var (id, text) in items)
            {
                _ = text is null ? AppendMenuW(menu, MfSeparator, 0, null) : AppendMenuW(menu, MfString, (nuint)id, text);
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
        public void Start(TimeSpan delay)
        {
            // USER_TIMER_MINIMUM is 10 ms; a zero dwell therefore fires on the next timer tick.
            var ms = (uint)Math.Max(10, Math.Ceiling(delay.TotalMilliseconds));
            if (owner._hwnd != 0)
            {
                _ = SetTimer(owner._hwnd, IntentTimerId, ms, 0);
            }
        }

        public void Cancel()
        {
            if (owner._hwnd != 0)
            {
                _ = KillTimer(owner._hwnd, IntentTimerId);
            }
        }
    }
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
