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

    public TimeSpan DelayFor(IslandTimer timer) => timer switch
    {
        IslandTimer.HoverDwell => HoverDwell,
        IslandTimer.LeaveGrace => LeaveGrace,
        _ => throw new ArgumentOutOfRangeException(nameof(timer), timer, null),
    };
}

/// <summary>
/// The one one-shot timer the island owns. The window implements it with a Win32 timer on the UI thread (no extra
/// thread, nothing scheduled while nothing is pending); tests implement it on virtual time. <see cref="Start"/>
/// replaces any running timer.
/// </summary>
public interface IIslandTimer
{
    void Start(TimeSpan delay);

    void Cancel();
}

/// <summary>
/// Applies inputs to <see cref="IslandStateMachine"/> and keeps the timer in step with the phase: every input, from
/// the pointer, a click, a menu, the tray, UI Automation or a second launch, goes through <see cref="Apply"/>, so
/// there is one interaction path. Single-threaded (the UI thread).
/// </summary>
public sealed class IslandController(IIslandTimer timer, IslandTiming timing)
{
    private IslandTimer _armed;

    public IslandState State { get; private set; } = IslandState.Initial;

    public IslandTiming Timing { get; } = timing;

    public bool IsShutDown { get; private set; }

    public IslandTransition Apply(IslandInput input)
    {
        if (IsShutDown)
        {
            return new IslandTransition(State, State);
        }

        var transition = IslandStateMachine.Apply(State, input);
        State = transition.Next;
        if (transition.CancelsTimer)
        {
            timer.Cancel();
            _armed = IslandTimer.None;
        }

        if (transition.StartsTimer is var start and not IslandTimer.None)
        {
            _armed = start;
            timer.Start(Timing.DelayFor(start));
        }

        return transition;
    }

    /// <summary>The timer fired. Ignored when nothing is armed (a late callback after a cancel or shutdown).</summary>
    public IslandTransition TimerElapsed()
    {
        var armed = _armed;
        _armed = IslandTimer.None;
        return armed switch
        {
            IslandTimer.HoverDwell => Apply(IslandInput.HoverDwellElapsed),
            IslandTimer.LeaveGrace => Apply(IslandInput.LeaveGraceElapsed),
            _ => new IslandTransition(State, State),
        };
    }

    /// <summary>Stops the timer and ignores every later input or callback (application exit).</summary>
    public void Shutdown()
    {
        if (IsShutDown)
        {
            return;
        }

        IsShutDown = true;
        _armed = IslandTimer.None;
        timer.Cancel();
    }
}
