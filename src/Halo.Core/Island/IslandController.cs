namespace Halo.Core.Island;

/// <summary>
/// Hover-intent timing (FR-ISL-03), kept in one place. Defaults follow FR-ISL-03 and ADR-001 §6.1:
/// a 120 ms dwell is long enough that moving the pointer across the pill to reach something else does not open it,
/// and short next to the 350 ms shape transition, so opening still feels immediate. The 400 ms leave grace is longer
/// than that transition, so brushing past the edge or overshooting it does not start a collapse that is then
/// reversed mid-animation.
/// </summary>
public sealed record IslandTiming
{
    public static readonly TimeSpan MaxHoverDwell = TimeSpan.FromMilliseconds(500);

    public IslandTiming(TimeSpan hoverDwell, TimeSpan leaveGrace)
    {
        // FR-ISL-03: the dwell is configurable from 0 to 500 ms.
        ArgumentOutOfRangeException.ThrowIfLessThan(hoverDwell, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hoverDwell, MaxHoverDwell);
        ArgumentOutOfRangeException.ThrowIfLessThan(leaveGrace, TimeSpan.Zero);
        HoverDwell = hoverDwell;
        LeaveGrace = leaveGrace;
    }

    public static IslandTiming Default { get; } = new(TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(400));

    public TimeSpan HoverDwell { get; }

    public TimeSpan LeaveGrace { get; }

    /// <summary>False = click-only mode (FR-ISL-04): the pointer never opens the island by itself.</summary>
    public bool HoverEnabled { get; init; } = true;

    public TimeSpan DelayFor(IslandTimer timer) => timer switch
    {
        IslandTimer.HoverDwell => HoverDwell,
        IslandTimer.LeaveGrace => LeaveGrace,
        _ => throw new ArgumentOutOfRangeException(nameof(timer), timer, null),
    };
}

/// <summary>The island's independent one-shot timers.</summary>
public enum IslandTimerSlot
{
    /// <summary>Hover dwell or leave grace (only one can be pending at a time).</summary>
    Intent,

    /// <summary>Lifetime of the alert on screen.</summary>
    Alert,
}

/// <summary>
/// The island's one-shot timers. The window implements them with Win32 timers on the UI thread (no extra thread,
/// nothing scheduled while nothing is pending); tests implement them on virtual time. <see cref="Start"/> replaces a
/// running timer in the same slot.
/// </summary>
public interface IIslandTimer
{
    void Start(IslandTimerSlot slot, TimeSpan delay);

    void Cancel(IslandTimerSlot slot);
}

/// <summary>
/// Applies inputs to <see cref="IslandStateMachine"/>, keeps the timers in step with the state and arbitrates
/// transient alerts (ADR-001 §6.2). Every input, from the pointer, a click, a menu, the tray, UI Automation, a second
/// launch, full-screen detection or a system event, goes through this class, so there is one interaction path.
/// Single-threaded (the UI thread).
/// <para>Alert rules:</para>
/// <list type="bullet">
/// <item>Same key as the alert on screen: the content is replaced and its time restarts (coalescing).</item>
/// <item>An alert is shown only while the island is collapsed and not being pressed, and, while full screen hides the
/// island, only if it is allowed over full screen. Otherwise it waits in a queue of at most four (same key replaces).</item>
/// <item>A higher- or equal-priority alert replaces the one on screen (which is dropped, not re-queued); a lower one waits.</item>
/// <item>When the alert on screen ends, or the user collapses the island, the highest-priority queued alert that is not
/// stale is shown; stale ones are dropped.</item>
/// <item>Expanding the island (hover, click, tray, UIA) removes the alert; alerts never pin the island.</item>
/// <item>A timer expiry is acted on only if that slot is still armed, so a late callback cannot change a newer state.</item>
/// </list>
/// </summary>
public sealed class IslandController
{
    public const int MaxQueuedAlerts = 4;

    private readonly IIslandTimer _timer;
    private readonly TimeProvider _time;
    private readonly List<IslandAlert> _queue = [];
    private IslandTimer _armedIntent;
    private bool _alertArmed;

    public IslandController(IIslandTimer timer, IslandTiming timing, TimeProvider? time = null)
    {
        _timer = timer;
        Timing = timing;
        _time = time ?? TimeProvider.System;
    }

    public IslandState State { get; private set; } = IslandState.Initial;

    public IslandTiming Timing { get; private set; }

    public bool IsShutDown { get; private set; }

    /// <summary>
    /// Quiet mode (G-F03). While on, an alert that is neither critical nor allowed in quiet mode is not shown or queued;
    /// it is only counted in <see cref="HeldWhileQuiet"/>, so nothing floods in when quiet mode ends.
    /// </summary>
    public bool Quiet { get; private set; }

    /// <summary>Alerts held back since quiet mode started.</summary>
    public int HeldWhileQuiet { get; private set; }

    /// <summary>Alerts waiting to be shown, in arrival order.</summary>
    public IReadOnlyList<IslandAlert> QueuedAlerts => _queue;

    /// <summary>Replaces the hover timing (settings); takes effect from the next pending action.</summary>
    public void SetTiming(IslandTiming timing) => Timing = timing ?? throw new ArgumentNullException(nameof(timing));

    public IslandTransition Apply(IslandInput input) =>
        IsShutDown ? Unchanged() : Commit(IslandStateMachine.Apply(State, input, Timing.HoverEnabled));

    /// <summary>A timer fired. Ignored when the slot is not armed (a late callback after a cancel or shutdown).</summary>
    public IslandTransition TimerElapsed(IslandTimerSlot slot)
    {
        if (IsShutDown)
        {
            return Unchanged();
        }

        if (slot == IslandTimerSlot.Intent)
        {
            var armed = _armedIntent;
            _armedIntent = IslandTimer.None;
            return armed switch
            {
                IslandTimer.HoverDwell => Apply(IslandInput.HoverDwellElapsed),
                IslandTimer.LeaveGrace => Apply(IslandInput.LeaveGraceElapsed),
                _ => Unchanged(),
            };
        }

        if (!_alertArmed || State.Alert is null)
        {
            _alertArmed = false;
            return Unchanged();
        }

        _alertArmed = false;
        var previous = State;
        State = State with { Alert = null };
        ShowNextQueued();
        return new IslandTransition(previous, State);
    }

    /// <summary>The hover timer fired (kept for callers that only use hover timing).</summary>
    public IslandTransition TimerElapsed() => TimerElapsed(IslandTimerSlot.Intent);

    /// <summary>
    /// Removes every alert of the given kinds, queued or on screen (a feature was turned off, so what it raised no
    /// longer applies). An alert on screen ends at once and the next queued one, if any, takes its place.
    /// </summary>
    public IslandTransition DropAlerts(params ReadOnlySpan<AlertKind> kinds)
    {
        if (IsShutDown)
        {
            return Unchanged();
        }

        var set = kinds.ToArray();
        _ = _queue.RemoveAll(a => set.Contains(a.Kind));
        if (State.Alert is not { } shown || !set.Contains(shown.Kind))
        {
            return Unchanged();
        }

        var previous = State;
        _timer.Cancel(IslandTimerSlot.Alert);
        _alertArmed = false;
        State = State with { Alert = null };
        ShowNextQueued();
        return new IslandTransition(previous, State);
    }

    /// <summary>
    /// Turns quiet mode on or off. Turning it on drops what is queued and not allowed (it would otherwise appear when
    /// quiet mode ends); the alert on screen finishes normally. Returns the number held back when turned off.
    /// </summary>
    public int SetQuiet(bool quiet)
    {
        if (quiet == Quiet)
        {
            return 0;
        }

        Quiet = quiet;
        if (quiet)
        {
            HeldWhileQuiet = 0;
            _ = _queue.RemoveAll(a => !AllowedWhileQuiet(a));
            return 0;
        }

        var held = HeldWhileQuiet;
        HeldWhileQuiet = 0;
        return held;
    }

    /// <summary>Requests a transient alert. Returns the change on screen (unchanged if it was queued or held by quiet mode).</summary>
    public IslandTransition Raise(IslandAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (IsShutDown)
        {
            return Unchanged();
        }

        if (Quiet && !AllowedWhileQuiet(alert))
        {
            HeldWhileQuiet++;
            return Unchanged();
        }

        alert = alert with { RaisedAt = _time.GetUtcNow() };
        var previous = State;
        if (State.Alert is { } current && string.Equals(current.Key, alert.Key, StringComparison.Ordinal))
        {
            Show(alert);
            return new IslandTransition(previous, State);
        }

        _ = _queue.RemoveAll(a => string.Equals(a.Key, alert.Key, StringComparison.Ordinal));
        if (!CanShow(State, alert) || (State.Alert is { } shown && alert.Priority < shown.Priority))
        {
            Enqueue(alert);
            return Unchanged();
        }

        Show(alert);
        return new IslandTransition(previous, State);
    }

    /// <summary>Stops every timer, drops queued alerts and ignores every later input or callback (application exit).</summary>
    public void Shutdown()
    {
        if (IsShutDown)
        {
            return;
        }

        IsShutDown = true;
        _armedIntent = IslandTimer.None;
        _alertArmed = false;
        _queue.Clear();
        _timer.Cancel(IslandTimerSlot.Intent);
        _timer.Cancel(IslandTimerSlot.Alert);
    }

    private static bool AllowedWhileQuiet(IslandAlert alert) => alert.AllowedInQuietMode || alert.Priority == AlertPriority.Critical;

    private static bool CanShow(IslandState state, IslandAlert alert) =>
        state.Phase == IslandPhase.Collapsed && !state.Pressed && (!state.Hidden || alert.AllowedWhenSuppressed);

    private IslandTransition Unchanged() => new(State, State);

    private IslandTransition Commit(IslandTransition transition)
    {
        State = transition.Next;
        if (transition.CancelsTimer)
        {
            _timer.Cancel(IslandTimerSlot.Intent);
            _armedIntent = IslandTimer.None;
        }

        if (transition.StartsTimer is var start and not IslandTimer.None)
        {
            _armedIntent = start;
            _timer.Start(IslandTimerSlot.Intent, Timing.DelayFor(start));
        }

        if (transition.Previous.Alert is not null && State.Alert is null && _alertArmed)
        {
            _timer.Cancel(IslandTimerSlot.Alert);
            _alertArmed = false;
        }

        if (State.Alert is null)
        {
            ShowNextQueued();
        }

        return new IslandTransition(transition.Previous, State);
    }

    private void Show(IslandAlert alert)
    {
        State = State with { Alert = alert };
        _alertArmed = true;
        _timer.Start(IslandTimerSlot.Alert, alert.Duration);
    }

    private void Enqueue(IslandAlert alert)
    {
        if (_queue.Count >= MaxQueuedAlerts)
        {
            // Make room by dropping the lowest-priority, oldest entry.
            var drop = _queue.OrderBy(a => a.Priority).ThenBy(a => a.RaisedAt).First();
            _ = _queue.Remove(drop);
        }

        _queue.Add(alert);
    }

    private void ShowNextQueued()
    {
        var now = _time.GetUtcNow();
        _ = _queue.RemoveAll(a => now - a.RaisedAt > a.StaleAfter);
        var next = _queue.Where(a => CanShow(State, a)).OrderByDescending(a => a.Priority).ThenByDescending(a => a.RaisedAt).FirstOrDefault();
        if (next is not null)
        {
            _ = _queue.Remove(next);
            Show(next);
        }
    }
}
