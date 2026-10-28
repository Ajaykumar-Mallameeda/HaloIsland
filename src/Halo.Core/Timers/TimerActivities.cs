using System.Globalization;
using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Motion;

namespace Halo.Core.Timers;

/// <summary>Action tokens shared by the timer and focus activities (routed by the window; never from content).</summary>
public static class TimerActions
{
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Reset = "reset";
    public const string AddMinute = "add1";
    public const string Cancel = "cancel";
    public const string Dismiss = "dismiss";
    public const string Snooze = "snooze";
    public const string Skip = "skip";
    public const string Stop = "stop";
}

/// <summary>Builds the island activities for the countdown timer and the focus session (C-F02 visual content).</summary>
public static class TimerActivities
{
    public const string TimerId = "timer";
    public const string FocusId = "focus";

    /// <summary>A finished timer waits this long for the user before it removes itself.</summary>
    public static readonly TimeSpan DoneLingers = TimeSpan.FromMinutes(10);

    // Segoe Fluent Icons.
    private const string TimerGlyph = "";
    private const string DoneGlyph = "";
    private const string FocusGlyph = "";
    private const string BreakGlyph = "";
    private static readonly ActivityAction PauseAction = new(TimerActions.Pause, "Pause", "");
    private static readonly ActivityAction ResumeAction = new(TimerActions.Resume, "Resume", "");
    private static readonly ActivityAction ResetAction = new(TimerActions.Reset, "Reset", "");
    private static readonly ActivityAction AddAction = new(TimerActions.AddMinute, "Add 1 minute", "");
    private static readonly ActivityAction CancelAction = new(TimerActions.Cancel, "Cancel timer", "");
    private static readonly ActivityAction DismissAction = new(TimerActions.Dismiss, "Dismiss", "");
    private static readonly ActivityAction SnoozeAction = new(TimerActions.Snooze, "Snooze 1 minute", "");
    private static readonly ActivityAction SkipAction = new(TimerActions.Skip, "Skip to next phase", "");
    private static readonly ActivityAction StopAction = new(TimerActions.Stop, "Stop focus session", "");

    /// <summary>
    /// The countdown's activity, or null when it is idle. <paramref name="completedAt"/> starts the linger time;
    /// <paramref name="localNow"/> (when given) adds the local end time to a running countdown's subtitle.
    /// </summary>
    public static IslandActivity? ForCountdown(CountdownTimer timer, DateTimeOffset? completedAt, DateTimeOffset? localNow = null)
    {
        ArgumentNullException.ThrowIfNull(timer);
        var remaining = timer.Remaining;
        var label = "Timer · " + DurationLabel(timer.Duration);
        var ends = timer.State == CountdownState.Running && localNow is { } now
            ? " · ends " + (now + remaining).ToString("t", CultureInfo.CurrentCulture)
            : string.Empty;
        return timer.State switch
        {
            CountdownState.Running or CountdownState.Paused => new IslandActivity(TimerId, ActivityKind.Timer, AlertPriority.Normal)
            {
                Glyph = TimerGlyph,
                CompactText = CountdownTimer.Format(remaining),
                Title = CountdownTimer.Format(remaining),
                Subtitle = timer.State == CountdownState.Paused ? label + " · paused" : label + ends,
                Level = timer.RemainingFraction,
                Actions = timer.State == CountdownState.Running ? [PauseAction, AddAction, CancelAction] : [ResumeAction, ResetAction, CancelAction],
                Accessible = timer.State == CountdownState.Paused ? "Timer paused, " + CountdownTimer.Spoken(remaining) : "Timer, " + CountdownTimer.Spoken(remaining),
                AllowedInQuietMode = true,
                Emphasis = timer.State == CountdownState.Running && remaining <= CountdownEmphasis.Window,
            },
            CountdownState.Completed => new IslandActivity(TimerId, ActivityKind.Timer, AlertPriority.High)
            {
                Glyph = DoneGlyph,
                CompactText = "Time's up",
                Title = "Time's up",
                Subtitle = timer.Missed ? label + " · ended while Halo was closed" : label,
                Level = null,
                Actions = [DismissAction, SnoozeAction],
                Accessible = timer.Missed ? "Timer ended while Halo was closed" : "Timer finished",
                Ongoing = false,
                ExpiresAt = completedAt + DoneLingers,
                AllowedInQuietMode = true,
                AllowedWhenSuppressed = true,
                Emphasis = true,
            },
            _ => null,
        };
    }

    /// <summary>The focus session's activity, or null when no session runs.</summary>
    public static IslandActivity? ForFocus(FocusSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsActive)
        {
            return null;
        }

        var countdown = session.Countdown;
        var remaining = countdown.Remaining;
        var paused = countdown.State == CountdownState.Paused;
        var phase = PhaseName(session.Phase);
        var position = string.Create(CultureInfo.CurrentCulture, $"{session.CycleIndex} of {session.Plan.CycleLength}");
        return new IslandActivity(FocusId, ActivityKind.Focus, AlertPriority.Normal)
        {
            Glyph = session.Phase == FocusPhase.Work ? FocusGlyph : BreakGlyph,
            CompactText = CountdownTimer.Format(remaining),
            Title = CountdownTimer.Format(remaining),
            Subtitle = $"{phase} · {position}{(paused ? " · paused" : string.Empty)}",
            Level = countdown.RemainingFraction,
            Actions = [paused ? ResumeAction : PauseAction, SkipAction, StopAction],
            Accessible = $"{phase}, {position}, {(paused ? "paused, " : string.Empty)}{CountdownTimer.Spoken(remaining)}",
            AllowedInQuietMode = true,
            Emphasis = !paused && remaining <= CountdownEmphasis.Window,
        };
    }

    /// <summary>The alert shown when a countdown finishes (completion arrival). Allowed over full screen and in quiet mode.</summary>
    public static IslandAlert CompletedAlert(bool missed) => new(
        "timer",
        AlertKind.Timer,
        AlertPriority.High,
        new AlertContent(DoneGlyph, missed ? "Timer ended earlier" : "Time's up") { Accessible = missed ? "Timer ended while Halo was closed" : "Timer finished" },
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(30),
        AllowedWhenSuppressed: true)
    {
        AllowedInQuietMode = true,
    };

    /// <summary>The alert when a focus phase ends.</summary>
    public static IslandAlert PhaseAlert(FocusPhaseEnded ended) => new(
        "focus",
        AlertKind.Timer,
        AlertPriority.High,
        new AlertContent(ended.Next == FocusPhase.Work ? FocusGlyph : BreakGlyph, ended.Next == FocusPhase.Work ? "Back to focus" : PhaseName(ended.Next) + " time")
        {
            Accessible = ended.Next == FocusPhase.Work ? "Break over, focus started" : PhaseName(ended.Next) + " started",
        },
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(30))
    {
        AllowedInQuietMode = true,
    };

    public static string PhaseName(FocusPhase phase) => phase switch
    {
        FocusPhase.ShortBreak => "Short break",
        FocusPhase.LongBreak => "Long break",
        _ => "Focus",
    };

    /// <summary>"5 min", "1 min 35 s", "1 h 30 min", "45 s".</summary>
    public static string DurationLabel(TimeSpan duration)
    {
        var totalSeconds = (int)Math.Round(duration.TotalSeconds);
        if (totalSeconds < 60)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{totalSeconds} s");
        }

        if (totalSeconds < 600 && totalSeconds % 60 != 0)
        {
            return string.Create(CultureInfo.CurrentCulture, $"{totalSeconds / 60} min {totalSeconds % 60} s");
        }

        var minutes = (int)Math.Round(duration.TotalMinutes);
        return minutes >= 60
            ? string.Create(CultureInfo.CurrentCulture, $"{minutes / 60} h{(minutes % 60 > 0 ? $" {minutes % 60} min" : string.Empty)}")
            : string.Create(CultureInfo.CurrentCulture, $"{minutes} min");
    }
}
