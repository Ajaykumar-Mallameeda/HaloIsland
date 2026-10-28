using Halo.Core.Activities;
using Halo.Core.Diagnostics;
using Halo.Core.Island;
using Halo.Core.Motion;
using Halo.Core.Storage;
using Halo.Core.Timers;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Activities in the island (B-F03, C-F02, G-F01–G-F03): the countdown timer, the focus session and quiet mode.
/// Everything runs on the UI thread with one-shot Win32 timers:
/// <list type="bullet">
/// <item>the tick timer fires at the next whole second of a running countdown while the island can be seen, and only at
/// completion while it is hidden, locked or the display is off (no per-second wake-ups nobody sees);</item>
/// <item>the expiry timer fires when the next activity expires;</item>
/// <item>the quiet timer fires when quiet mode's end time arrives.</item>
/// </list>
/// State is saved to <c>state.json</c> when it changes, before sleep and on exit.
/// </summary>
internal sealed partial class IslandWindow
{
    private const nuint ActivityTickTimerId = 10;
    private const nuint ActivityExpiryTimerId = 11;
    private const nuint QuietEndTimerId = 12;

    /// <summary>Diagnostics-only: start a countdown of wParam seconds.</summary>
    private const uint WmTestTimer = WmApp + 53;

    /// <summary>Diagnostics-only: set quiet mode (wParam = <see cref="QuietKind"/>).</summary>
    private const uint WmTestQuiet = WmApp + 54;

    /// <summary>Diagnostics-only: start a focus session with wParam-second phases.</summary>
    private const uint WmTestFocus = WmApp + 55;

    /// <summary>Timer presets offered in the tray menu, in minutes.</summary>
    private static readonly int[] TimerPresets = [1, 5, 10, 15, 25, 45];

    private readonly ActivityBoard _board = new();
    private CountdownTimer _countdown = new();
    private FocusSession _focus = new();
    private StateStore? _stateStore;
    private DateTimeOffset? _timerDoneAt;
    private HaloState? _suspended;
    private QuietMode _quietMode = QuietMode.Off;
    private IslandPage? _page;

    /// <summary>The countdown, for tests and the settings window.</summary>
    internal CountdownTimer Countdown => _countdown;

    /// <summary>Private activity text (tasks, previews) is shown only when the user allows previews (D-F03).</summary>
    private bool ShowPrivate => _settings.ShowPreviews;

    private IReadOnlyList<IslandPage> Pages => IslandPages.Available(_board.Primary is not null, _mediaSnapshot is not null);

    private IslandPage CurrentPage => IslandPages.Resolve(_page, Pages);

    // Tray menu commands for timers, focus sessions and quiet mode.
    private const int MenuTimerPreset = 100; // + index into TimerPresets
    private const int MenuTimerPause = 110;
    private const int MenuTimerResume = 111;
    private const int MenuTimerCancel = 112;
    private const int MenuFocusStart = 120;
    private const int MenuFocusPause = 121;
    private const int MenuFocusResume = 122;
    private const int MenuFocusSkip = 123;
    private const int MenuFocusStop = 124;
    private const int MenuQuietHour = 130;
    private const int MenuQuietTomorrow = 131;
    private const int MenuQuietManual = 132;
    private const int MenuQuietOff = 133;

    private MenuEntry[] TimerMenu()
    {
        var items = new List<MenuEntry>();
        switch (_countdown.State)
        {
            case CountdownState.Running:
                items.Add(new MenuEntry(MenuTimerPause, "Pause timer"));
                items.Add(new MenuEntry(MenuTimerCancel, "Cancel timer"));
                items.Add(new MenuEntry(0, null));
                break;
            case CountdownState.Paused:
                items.Add(new MenuEntry(MenuTimerResume, "Resume timer"));
                items.Add(new MenuEntry(MenuTimerCancel, "Cancel timer"));
                items.Add(new MenuEntry(0, null));
                break;
            case CountdownState.Completed:
                items.Add(new MenuEntry(MenuTimerCancel, "Dismiss timer"));
                items.Add(new MenuEntry(0, null));
                break;
        }

        for (var i = 0; i < TimerPresets.Length; i++)
        {
            var m = TimerPresets[i];
            items.Add(new MenuEntry(MenuTimerPreset + i, m == 1 ? "Start 1 minute" : $"Start {m} minutes"));
        }

        return [.. items];
    }

    private MenuEntry[] FocusMenu() => _focus.IsActive
        ? [
            _focus.Countdown.State == CountdownState.Paused ? new MenuEntry(MenuFocusResume, "Resume") : new MenuEntry(MenuFocusPause, "Pause"),
            new MenuEntry(MenuFocusSkip, "Skip to next phase"),
            new MenuEntry(MenuFocusStop, "Stop session"),
        ]
        : [new MenuEntry(MenuFocusStart, "Start (25 min focus, 5 min break)")];

    private MenuEntry[] QuietMenu()
    {
        var active = _quietMode.IsActive(DateTimeOffset.UtcNow);
        return
        [
            new MenuEntry(MenuQuietHour, "For 1 hour", active && _quietMode.Kind == QuietKind.OneHour),
            new MenuEntry(MenuQuietTomorrow, "Until tomorrow", active && _quietMode.Kind == QuietKind.UntilTomorrow),
            new MenuEntry(MenuQuietManual, "Until I turn it off", active && _quietMode.Kind == QuietKind.UntilTurnedOff),
            new MenuEntry(0, null),
            new MenuEntry(MenuQuietOff, "Turn off", Disabled: !active),
        ];
    }

    /// <summary>Runs a timer, focus or quiet-mode menu command. False when the command is not one of them.</summary>
    private bool RunFeatureMenuCommand(int command)
    {
        switch (command)
        {
            case >= MenuTimerPreset and < MenuTimerPreset + 6:
                StartTimer(TimeSpan.FromMinutes(TimerPresets[command - MenuTimerPreset]), "tray");
                return true;
            case MenuTimerPause:
                RunActivityAction(TimerActivities.TimerId, TimerActions.Pause, "tray");
                return true;
            case MenuTimerResume:
                RunActivityAction(TimerActivities.TimerId, TimerActions.Resume, "tray");
                return true;
            case MenuTimerCancel:
                RunActivityAction(TimerActivities.TimerId, TimerActions.Cancel, "tray");
                return true;
            case MenuFocusStart:
                StartFocus(FocusPlan.Default, "tray");
                return true;
            case MenuFocusPause:
                RunActivityAction(TimerActivities.FocusId, TimerActions.Pause, "tray");
                return true;
            case MenuFocusResume:
                RunActivityAction(TimerActivities.FocusId, TimerActions.Resume, "tray");
                return true;
            case MenuFocusSkip:
                RunActivityAction(TimerActivities.FocusId, TimerActions.Skip, "tray");
                return true;
            case MenuFocusStop:
                RunActivityAction(TimerActivities.FocusId, TimerActions.Stop, "tray");
                return true;
            case MenuQuietHour or MenuQuietTomorrow or MenuQuietManual:
                var kind = command switch { MenuQuietHour => QuietKind.OneHour, MenuQuietTomorrow => QuietKind.UntilTomorrow, _ => QuietKind.UntilTurnedOff };
                SetQuiet(QuietMode.Start(kind, DateTimeOffset.UtcNow, TimeZoneInfo.Local), "tray");
                return true;
            case MenuQuietOff:
                SetQuiet(QuietMode.Off, "tray");
                return true;
            default:
                return false;
        }
    }

    /// <summary>Loads saved activity state and shows it (start-up).</summary>
    private void InitActivities()
    {
        _stateStore = new StateStore(_store.Directory);
        HaloState state;
        DataLoadStatus status;
        try
        {
            state = _stateStore.Load(out status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            state = HaloState.Empty;
            status = DataLoadStatus.Unreadable;
        }

        _log?.Write("state", ("status", status.ToString().ToLowerInvariant()), ("timer", state.Timer is null ? 0 : 1), ("focus", state.Focus is null ? 0 : 1), ("quiet", state.Quiet.Kind != QuietKind.Off ? 1 : 0));
        if (status is DataLoadStatus.RecoveredFromBackup or DataLoadStatus.Unreadable)
        {
            _fileLog?.Write(LogLevel.Warning, "state.recovered", ("status", status.ToString().ToLowerInvariant()));
        }

        Restore(state, "startup");
    }

    private void Restore(HaloState state, string reason)
    {
        _countdown = new CountdownTimer();
        if (state.Timer is { } t && _countdown.Restore(t) && _countdown.State == CountdownState.Completed)
        {
            _timerDoneAt = DateTimeOffset.UtcNow;
            if (_countdown.Missed)
            {
                RaiseAlert(TimerActivities.CompletedAlert(missed: true), "timer");
            }
        }

        _focus = new FocusSession();
        if (state.Focus is { } f)
        {
            _ = _focus.Restore(f);
        }

        SetQuiet(state.Quiet.IsActive(DateTimeOffset.UtcNow) ? state.Quiet : QuietMode.Off, reason, summarise: false);
        SyncActivities(reason);
    }

    /// <summary>
    /// The single place where timer state becomes island state: completions, the board, the visuals, the next tick,
    /// persistence and UIA. Called after every change and on every tick.
    /// </summary>
    private void SyncActivities(string reason)
    {
        if (_controller.IsShutDown)
        {
            return;
        }

        if (_countdown.CheckCompleted())
        {
            _timerDoneAt = DateTimeOffset.UtcNow;
            _log?.Write("timer", ("state", "completed"), ("reason", reason));
            RaiseAlert(TimerActivities.CompletedAlert(missed: false), "timer");
            _visuals?.Arrive(MotionPlanner.Arrival(_reducedMotion));
        }

        if (_focus.CheckCompleted() is { } ended)
        {
            _log?.Write("focus", ("state", "phase_ended"), ("next", ended.Next.ToString().ToLowerInvariant()), ("completed_work", ended.CompletedWork));
            RaiseAlert(TimerActivities.PhaseAlert(ended), "focus");
            _visuals?.Arrive(MotionPlanner.Arrival(_reducedMotion));
        }

        var before = _board.Primary;
        Put(TimerActivities.TimerId, TimerActivities.ForCountdown(_countdown, _timerDoneAt, DateTimeOffset.Now));
        Put(TimerActivities.FocusId, TimerActivities.ForFocus(_focus));
        if (_board.RemoveExpired().Changed && _board.Find(TimerActivities.TimerId) is null && _countdown.State == CountdownState.Completed)
        {
            // The finished timer waited long enough without being dismissed.
            _countdown.Clear();
            _timerDoneAt = null;
        }

        var primary = _board.Primary;
        if (!string.Equals(before?.Id, primary?.Id, StringComparison.Ordinal) || before?.Emphasis != primary?.Emphasis)
        {
            _log?.Write("activity", ("primary", primary is null ? "none" : primary.Id), ("count", _board.Count), ("emphasis", primary?.Emphasis == true ? 1 : 0), ("reason", reason));
        }

        _visuals?.ShowActivity(primary, _board.OthersCount, ShowPrivate, _reducedMotion);
        if (primary?.Emphasis == true && (primary.Kind is ActivityKind.Timer or ActivityKind.Focus) && _countdown.State != CountdownState.Completed && State.Visible)
        {
            _visuals?.Emphasise(MotionPlanner.Emphasis(primary.Kind == ActivityKind.Focus ? _focus.Countdown.Remaining : _countdown.Remaining, _reducedMotion));
        }

        ApplyPage(animate: false);
        ArmActivityTimers();
        SaveState();
        PublishAutomation();
    }

    private void Put(string id, IslandActivity? activity) => _ = activity is null ? _board.Remove(id) : _board.Upsert(activity);

    /// <summary>One tick timer and one expiry timer, each armed only while something will change.</summary>
    private void ArmActivityTimers()
    {
        var seen = State.Visible && !_sessionLocked && !_displayOff;
        var waits = new List<TimeSpan>(2);
        foreach (var countdown in new[] { _countdown, _focus.Countdown })
        {
            if (countdown.State != CountdownState.Running)
            {
                continue;
            }

            // Visible: redraw each second. Not visible: wake only for the completion.
            waits.Add(seen ? countdown.NextChange() ?? TimeSpan.Zero : countdown.Remaining);
        }

        if (waits.Count > 0)
        {
            _ = SetTimer(_hwnd, ActivityTickTimerId, (uint)Math.Clamp(Math.Ceiling(waits.Min().TotalMilliseconds) + 5, 10, int.MaxValue), 0);
        }
        else
        {
            _ = KillTimer(_hwnd, ActivityTickTimerId);
        }

        if (_board.NextExpiry is { } expiry)
        {
            var ms = Math.Clamp(Math.Ceiling((expiry - DateTimeOffset.UtcNow).TotalMilliseconds) + 5, 10, int.MaxValue);
            _ = SetTimer(_hwnd, ActivityExpiryTimerId, (uint)ms, 0);
        }
        else
        {
            _ = KillTimer(_hwnd, ActivityExpiryTimerId);
        }
    }

    private void SaveState()
    {
        if (_stateStore is null)
        {
            return;
        }

        var state = _suspended ?? CurrentState();
        try
        {
            _ = _stateStore.Save(state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // NFR-05: the timer keeps running; the next change tries again.
            _fileLog?.Write(LogLevel.Warning, "state.save_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
        }
    }

    private HaloState CurrentState() => new()
    {
        Timer = _countdown.State == CountdownState.Idle ? null : _countdown.ToRecord(),
        Focus = _focus.IsActive ? _focus.ToRecord() : null,
        Quiet = _quietMode,
    };

    /// <summary>Before sleep: keep the wall-clock end times, so the countdown is right after resume however long the sleep was.</summary>
    private void OnSuspend()
    {
        _suspended = CurrentState();
        SaveState();
        _log?.Write("state", ("status", "suspended"));
    }

    private void OnResume()
    {
        if (_suspended is { } state)
        {
            _suspended = null;
            Restore(state, "resume");
        }
        else
        {
            SyncActivities("resume");
        }
    }

    /// <summary>Starts a countdown (tray, test hook). Replaces a finished or running one.</summary>
    internal void StartTimer(TimeSpan duration, string source)
    {
        _countdown = new CountdownTimer();
        _countdown.Start(duration);
        _timerDoneAt = null;
        _page = IslandPage.Activity;
        _log?.Write("timer", ("state", "started"), ("seconds", (int)duration.TotalSeconds), ("source", source));
        SyncActivities(source);
    }

    internal void StartFocus(FocusPlan plan, string source)
    {
        _focus = new FocusSession();
        _focus.Start(plan);
        _page = IslandPage.Activity;
        _log?.Write("focus", ("state", "started"), ("work_s", (int)plan.Work.TotalSeconds), ("cycle", plan.CycleLength), ("source", source));
        SyncActivities(source);
    }

    /// <summary>Runs an action of the primary activity by its index (island button or UIA). Stale indexes are ignored.</summary>
    private void RunPrimaryAction(int index, string source)
    {
        if (_board.Primary is not { } primary || index < 0 || index >= primary.Actions.Count)
        {
            _log?.Write("activity.action", ("action", "none"), ("result", "stale"), ("source", source));
            return;
        }

        RunActivityAction(primary.Id, primary.Actions[index].Id, source);
    }

    private void RunActivityAction(string activityId, string action, string source)
    {
        var ok = (activityId, action) switch
        {
            (TimerActivities.TimerId, TimerActions.Pause) => _countdown.Pause(),
            (TimerActivities.TimerId, TimerActions.Resume) => _countdown.Resume(),
            (TimerActivities.TimerId, TimerActions.Reset) => _countdown.Reset(),
            (TimerActivities.TimerId, TimerActions.AddMinute or TimerActions.Snooze) => _countdown.Add(TimeSpan.FromMinutes(1)),
            (TimerActivities.TimerId, TimerActions.Cancel or TimerActions.Dismiss) => ClearTimer(),
            (TimerActivities.FocusId, TimerActions.Pause) => _focus.Pause(),
            (TimerActivities.FocusId, TimerActions.Resume) => _focus.Resume(),
            (TimerActivities.FocusId, TimerActions.Skip) => _focus.Skip() is not null,
            (TimerActivities.FocusId, TimerActions.Stop) => StopFocus(),
            _ => false,
        };
        if (action is TimerActions.Snooze && ok)
        {
            _timerDoneAt = null;
        }

        _log?.Write("activity.action", ("activity", activityId), ("action", action), ("result", ok ? "done" : "ignored"), ("source", source));
        if (ok && action is TimerActions.Cancel or TimerActions.Dismiss && State.Alert is { Kind: AlertKind.Timer })
        {
            Present(_controller.DropAlerts(AlertKind.Timer), source);
        }

        SyncActivities(source);
    }

    private bool ClearTimer()
    {
        var had = _countdown.State != CountdownState.Idle;
        _countdown.Clear();
        _timerDoneAt = null;
        return had;
    }

    private bool StopFocus()
    {
        var had = _focus.IsActive;
        _focus.Stop();
        return had;
    }

    /// <summary>Quiet mode (G-F03). Never touches Windows' own Do Not Disturb or Focus settings.</summary>
    internal void SetQuiet(QuietMode mode, string source, bool summarise = true)
    {
        _quietMode = mode;
        var active = mode.IsActive(DateTimeOffset.UtcNow);
        var held = _controller.SetQuiet(active);
        _ = _board.SetQuiet(active);
        if (mode.UntilUtc is { } until && active)
        {
            var ms = Math.Clamp(Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMilliseconds) + 5, 10, int.MaxValue);
            _ = SetTimer(_hwnd, QuietEndTimerId, (uint)ms, 0);
        }
        else
        {
            _ = KillTimer(_hwnd, QuietEndTimerId);
        }

        _log?.Write("quiet", ("kind", mode.Kind.ToString().ToLowerInvariant()), ("active", active ? 1 : 0), ("held", held), ("source", source));
        _tray?.SetQuiet(active);
        if (!active && held > 0 && summarise)
        {
            // One summary instead of everything at once (G-F03).
            var text = held == 1 ? "1 alert while Do Not Disturb was on" : $"{held} alerts while Do Not Disturb was on";
            RaiseAlert(new IslandAlert("quiet.summary", AlertKind.Halo, AlertPriority.Normal, new AlertContent("", text), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(30)), source);
        }

        if (_stateStore is not null)
        {
            SyncActivities(source);
        }
    }

    private void OnQuietEndTimer()
    {
        _ = KillTimer(_hwnd, QuietEndTimerId);
        if (!_quietMode.IsActive(DateTimeOffset.UtcNow))
        {
            SetQuiet(QuietMode.Off, "expired");
        }
        else
        {
            SetQuiet(_quietMode, "rearm"); // the clock moved: re-arm for the remaining time
        }
    }

    private void ApplyPage(bool animate)
    {
        var pages = Pages;
        _visuals?.ShowPage(IslandPages.Resolve(_page, pages), pages, animate && State.IsExpanded, _reducedMotion);
    }

    /// <summary>Opening the island shows the default page: the primary activity, else what is playing, else the clock.</summary>
    private void ResetPage() => _page = null;

    private void StepPage(int direction, string source)
    {
        var pages = Pages;
        if (pages.Count < 2)
        {
            return;
        }

        _page = IslandPages.Step(CurrentPage, pages, direction);
        _log?.Write("page", ("page", _page.Value.ToString().ToLowerInvariant()), ("source", source));
        ApplyPage(animate: true);
        UpdateMediaTick();
        PublishAutomation();
    }

    private void ShowPageAt(int index, string source)
    {
        var pages = Pages;
        if (index < 0 || index >= pages.Count || !State.IsExpanded)
        {
            return;
        }

        _page = pages[index];
        _log?.Write("page", ("page", _page.Value.ToString().ToLowerInvariant()), ("source", source));
        ApplyPage(animate: true);
        UpdateMediaTick();
        PublishAutomation();
    }

    /// <summary>The activity action button under a client point while the activity page is on screen.</summary>
    private int? ActivityButtonAt(int x, int y)
    {
        if (!State.IsExpanded || !State.Visible || CurrentPage != IslandPage.Activity || _board.Primary is not { } primary)
        {
            return null;
        }

        foreach (var square in ActivityLayout.Actions(_layout, primary.Actions.Count))
        {
            if (square.Contains(x, y))
            {
                return square.Index;
            }
        }

        return null;
    }

    private int? PageDotAt(int x, int y)
    {
        if (!State.IsExpanded || !State.Visible)
        {
            return null;
        }

        foreach (var dot in ActivityLayout.Dots(_layout, Pages.Count))
        {
            if (dot.Contains(x, y))
            {
                return dot.Index;
            }
        }

        return null;
    }

    /// <summary>The primary activity as UIA sees it: private text redacted unless previews are allowed.</summary>
    private IslandActivity? AccessibleActivity() => _board.Primary is { } a && a.Privacy == ActivityPrivacy.Private && !ShowPrivate
        ? a with { Accessible = a.RedactedText, Title = a.RedactedText }
        : _board.Primary;

    private void StopActivityTimers()
    {
        _ = KillTimer(_hwnd, ActivityTickTimerId);
        _ = KillTimer(_hwnd, ActivityExpiryTimerId);
        _ = KillTimer(_hwnd, QuietEndTimerId);
    }

    /// <summary>Activity messages; null when the message is not one of them.</summary>
    private nint? HandleActivityMessage(uint msg, nint wParam)
    {
        switch (msg)
        {
            case WmTimer when (nuint)wParam == ActivityTickTimerId:
                _ = KillTimer(_hwnd, ActivityTickTimerId);
                SyncActivities("tick");
                return 0;
            case WmTimer when (nuint)wParam == ActivityExpiryTimerId:
                _ = KillTimer(_hwnd, ActivityExpiryTimerId);
                SyncActivities("expiry");
                return 0;
            case WmTimer when (nuint)wParam == QuietEndTimerId:
                OnQuietEndTimer();
                return 0;
            case WmTestTimer when _log is not null:
                StartTimer(TimeSpan.FromSeconds(Math.Clamp((int)wParam, 1, 86_400)), "test");
                return 0;
            case WmTestQuiet when _log is not null:
                SetQuiet(QuietMode.Start((QuietKind)Math.Clamp((int)wParam, 0, 3), DateTimeOffset.UtcNow, TimeZoneInfo.Local), "test");
                return 0;
            case WmTestFocus when _log is not null:
                var phase = TimeSpan.FromSeconds(Math.Clamp((int)wParam, 1, 3600));
                StartFocus(new FocusPlan(phase, phase, phase, 2), "test");
                return 0;
            default:
                return null;
        }
    }
}
