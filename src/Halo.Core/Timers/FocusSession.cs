namespace Halo.Core.Timers;

/// <summary>Phase of a focus session (G-F02).</summary>
public enum FocusPhase
{
    Work,
    ShortBreak,
    LongBreak,
}

/// <summary>Focus-session durations. Bounds keep a mistyped settings file from creating a 0- or 1000-minute phase.</summary>
public sealed record FocusPlan(TimeSpan Work, TimeSpan ShortBreak, TimeSpan LongBreak, int CycleLength)
{
    public static FocusPlan Default { get; } = new(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 4);

    public static FocusPlan? TryCreate(double workMinutes, double shortMinutes, double longMinutes, int cycleLength) =>
        workMinutes is >= 1 and <= 180 && shortMinutes is >= 1 and <= 60 && longMinutes is >= 1 and <= 120 && cycleLength is >= 1 and <= 12
            ? new FocusPlan(TimeSpan.FromMinutes(workMinutes), TimeSpan.FromMinutes(shortMinutes), TimeSpan.FromMinutes(longMinutes), cycleLength)
            : null;

    public TimeSpan DurationOf(FocusPhase phase) => phase switch
    {
        FocusPhase.ShortBreak => ShortBreak,
        FocusPhase.LongBreak => LongBreak,
        _ => Work,
    };
}

/// <summary>A saved focus session.</summary>
public sealed record FocusRecord(FocusPhase Phase, int CompletedWork, FocusPlan Plan, CountdownRecord Countdown);

/// <summary>A phase ended: what finished and what starts next.</summary>
public readonly record struct FocusPhaseEnded(FocusPhase Finished, FocusPhase Next, int CompletedWork);

/// <summary>
/// A Pomodoro-style focus session built on <see cref="CountdownTimer"/> (G-F02): work, short break, and a long break
/// after every <see cref="FocusPlan.CycleLength"/> work phases. When a phase ends the next one starts by itself, so
/// a session runs until the user stops it; pause and skip work at any time. No scores, streaks or accounts.
/// </summary>
public sealed class FocusSession(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public CountdownTimer Countdown { get; private set; } = new(time);

    public FocusPlan Plan { get; private set; } = FocusPlan.Default;

    public FocusPhase Phase { get; private set; }

    /// <summary>Work phases finished in this session.</summary>
    public int CompletedWork { get; private set; }

    public bool IsActive => Countdown.State is CountdownState.Running or CountdownState.Paused;

    /// <summary>Position in the cycle for display: the current (or upcoming) work phase, from 1 to the cycle length.</summary>
    public int CycleIndex => (CompletedWork % Plan.CycleLength) + (Phase == FocusPhase.Work ? 1 : 0) is var i && i == 0 ? Plan.CycleLength : i;

    public void Start(FocusPlan plan)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        CompletedWork = 0;
        Phase = FocusPhase.Work;
        Countdown = new CountdownTimer(_time);
        Countdown.Start(plan.Work);
    }

    public bool Pause() => Countdown.Pause();

    public bool Resume() => Countdown.Resume();

    /// <summary>Ends the current phase now and starts the next one.</summary>
    public FocusPhaseEnded? Skip() => IsActive ? Advance() : null;

    public void Stop()
    {
        Countdown.Clear();
        CompletedWork = 0;
        Phase = FocusPhase.Work;
    }

    /// <summary>When the running phase has run out: starts the next phase and reports the change, exactly once.</summary>
    public FocusPhaseEnded? CheckCompleted() => Countdown.CheckCompleted() ? Advance() : null;

    public FocusRecord ToRecord() => new(Phase, CompletedWork, Plan, Countdown.ToRecord());

    /// <summary>
    /// Restores a session. A phase that ended while Halo was closed is not replayed phase by phase: the session
    /// resumes at the next phase, paused, so the user decides when to continue.
    /// </summary>
    public bool Restore(FocusRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var countdown = new CountdownTimer(_time);
        if (!countdown.Restore(record.Countdown))
        {
            return false;
        }

        (Plan, Phase, CompletedWork, Countdown) = (record.Plan, record.Phase, record.CompletedWork, countdown);
        if (countdown.State == CountdownState.Completed)
        {
            _ = Advance();
            _ = Countdown.Pause();
        }

        return true;
    }

    private FocusPhaseEnded Advance()
    {
        var finished = Phase;
        if (finished == FocusPhase.Work)
        {
            CompletedWork++;
            Phase = CompletedWork % Plan.CycleLength == 0 ? FocusPhase.LongBreak : FocusPhase.ShortBreak;
        }
        else
        {
            Phase = FocusPhase.Work;
        }

        Countdown = new CountdownTimer(_time);
        Countdown.Start(Plan.DurationOf(Phase));
        return new FocusPhaseEnded(finished, Phase, CompletedWork);
    }
}
