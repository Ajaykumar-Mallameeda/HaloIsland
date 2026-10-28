using System.Globalization;

namespace Halo.Core.Timers;

/// <summary>Countdown state (FR-TIM-01).</summary>
public enum CountdownState
{
    Idle,
    Running,
    Paused,
    Completed,
}

/// <summary>
/// What is persisted so a countdown survives a restart (FR-TIM-02): the wall-clock end time while running (a remaining
/// time would be wrong after the app was closed), the remaining time while paused.
/// </summary>
/// <param name="State">State when saved.</param>
/// <param name="Duration">The duration the user chose (plus additions).</param>
/// <param name="EndUtc">Expected end while running.</param>
/// <param name="Remaining">Remaining time while paused.</param>
public sealed record CountdownRecord(CountdownState State, TimeSpan Duration, DateTimeOffset? EndUtc, TimeSpan? Remaining);

/// <summary>
/// A countdown on monotonic time (G-F01). While the process runs, the remaining time comes from
/// <see cref="TimeProvider.GetTimestamp"/>, so moving the wall clock (by hand, DST, network time) never shortens or
/// lengthens it. Across a restart the persisted wall-clock end time is used, the only reference that survives; a
/// countdown whose end passed while the app was closed restores as completed ("missed"). Single-threaded.
/// </summary>
public sealed class CountdownTimer(TimeProvider? time = null)
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _deadline;
    private TimeSpan _remaining;

    public CountdownState State { get; private set; }

    /// <summary>The chosen duration, including time added while running.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>True when the countdown completed while Halo was not running (restored after its end).</summary>
    public bool Missed { get; private set; }

    /// <summary>Remaining time, never negative.</summary>
    public TimeSpan Remaining => State switch
    {
        CountdownState.Running => Max(TimeSpan.Zero, TimeSpan.FromSeconds((_deadline - _time.GetTimestamp()) / (double)_time.TimestampFrequency)),
        CountdownState.Paused => _remaining,
        CountdownState.Completed => TimeSpan.Zero,
        _ => Duration,
    };

    /// <summary>Remaining as a fraction of the duration, from 1 (just started) to 0.</summary>
    public double RemainingFraction => Duration <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining / Duration, 0, 1);

    public void Start(TimeSpan duration)
    {
        if (duration < MinDuration || duration > MaxDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "A countdown lasts from 1 second to 24 hours.");
        }

        Duration = duration;
        Missed = false;
        Run(duration);
    }

    /// <summary>Pauses a running countdown. Returns false when it was not running.</summary>
    public bool Pause()
    {
        if (State != CountdownState.Running)
        {
            return false;
        }

        _remaining = Remaining;
        State = CountdownState.Paused;
        return true;
    }

    public bool Resume()
    {
        if (State != CountdownState.Paused)
        {
            return false;
        }

        Run(_remaining);
        return true;
    }

    /// <summary>Back to the full duration, not running.</summary>
    public bool Reset()
    {
        if (State == CountdownState.Idle && _remaining == Duration)
        {
            return false;
        }

        State = CountdownState.Idle;
        _remaining = Duration;
        Missed = false;
        return true;
    }

    /// <summary>Adds time while running or paused; after completion it restarts with that time (snooze).</summary>
    public bool Add(TimeSpan extra)
    {
        if (extra <= TimeSpan.Zero)
        {
            return false;
        }

        switch (State)
        {
            case CountdownState.Running:
                var remaining = Remaining + extra;
                Duration = Min(MaxDuration, Duration + extra);
                Run(Min(MaxDuration, remaining));
                return true;
            case CountdownState.Paused:
                _remaining = Min(MaxDuration, _remaining + extra);
                Duration = Min(MaxDuration, Duration + extra);
                return true;
            case CountdownState.Completed:
                Duration = extra;
                Missed = false;
                Run(extra);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Ends the countdown (cancel or dismiss).</summary>
    public void Clear()
    {
        State = CountdownState.Idle;
        Duration = TimeSpan.Zero;
        _remaining = TimeSpan.Zero;
        Missed = false;
    }

    /// <summary>Moves a running countdown to Completed when its time is up. True exactly once, on that transition.</summary>
    public bool CheckCompleted()
    {
        if (State != CountdownState.Running || Remaining > TimeSpan.Zero)
        {
            return false;
        }

        State = CountdownState.Completed;
        return true;
    }

    /// <summary>
    /// When the display next needs to change: the next whole-second boundary of the remaining time, or the completion.
    /// Null when nothing will change by itself (idle, paused, completed). The owner arms one one-shot timer for it.
    /// </summary>
    public TimeSpan? NextChange()
    {
        if (State != CountdownState.Running)
        {
            return null;
        }

        var remaining = Remaining;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var fraction = TimeSpan.FromTicks(remaining.Ticks % TimeSpan.TicksPerSecond);
        return fraction == TimeSpan.Zero ? TimeSpan.FromSeconds(1) : fraction;
    }

    /// <summary>What to save now. Running countdowns store "now + remaining" on the wall clock.</summary>
    public CountdownRecord ToRecord() => State switch
    {
        CountdownState.Running => new CountdownRecord(State, Duration, _time.GetUtcNow() + Remaining, null),
        CountdownState.Paused => new CountdownRecord(State, Duration, null, _remaining),
        _ => new CountdownRecord(State, Duration, null, null),
    };

    /// <summary>Restores a saved countdown. Invalid records are ignored (returns false, the timer stays idle).</summary>
    public bool Restore(CountdownRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Duration < MinDuration || record.Duration > MaxDuration)
        {
            return false;
        }

        switch (record.State)
        {
            case CountdownState.Running when record.EndUtc is { } end:
                Duration = record.Duration;
                var left = end - _time.GetUtcNow();
                if (left <= TimeSpan.Zero)
                {
                    State = CountdownState.Completed;
                    Missed = true;
                }
                else
                {
                    // A clock moved back while Halo was closed could make "left" exceed the duration; cap it.
                    Run(Min(left, record.Duration));
                }

                return true;
            case CountdownState.Paused when record.Remaining is { } r && r > TimeSpan.Zero && r <= record.Duration:
                Duration = record.Duration;
                _remaining = r;
                State = CountdownState.Paused;
                return true;
            case CountdownState.Completed:
                Duration = record.Duration;
                State = CountdownState.Completed;
                return true;
            default:
                return false;
        }
    }

    /// <summary>"4:59", "59:59" or "1:04:59" (rounded up, so the display reaches 0:00 exactly at completion).</summary>
    public static string Format(TimeSpan remaining)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds) - 1e-9);
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }

    /// <summary>
    /// Spoken remaining time with coarse granularity so screen readers are not given a new value every second:
    /// whole minutes above one minute, then tens of seconds, then single seconds in the last ten.
    /// </summary>
    public static string Spoken(TimeSpan remaining)
    {
        var seconds = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds) - 1e-9);
        if (seconds > 60)
        {
            var minutes = (int)Math.Ceiling(seconds / 60.0);
            return minutes >= 60
                ? string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} hour{(minutes / 60 == 1 ? string.Empty : "s")} {minutes % 60} minutes left")
                : string.Create(CultureInfo.CurrentCulture, $"{minutes} minutes left");
        }

        if (seconds > 10)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{(int)Math.Ceiling(seconds / 10.0) * 10} seconds left");
        }

        return seconds == 1 ? "1 second left" : string.Create(CultureInfo.CurrentCulture, $"{seconds} seconds left");
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private void Run(TimeSpan remaining)
    {
        _deadline = _time.GetTimestamp() + (long)(remaining.TotalSeconds * _time.TimestampFrequency);
        _remaining = remaining;
        State = CountdownState.Running;
    }
}
