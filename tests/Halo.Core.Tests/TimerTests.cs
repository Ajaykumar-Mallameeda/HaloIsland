using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Storage;
using Halo.Core.Timers;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>G-F01 countdown, G-F02 focus session, G-F03 quiet mode and their persistence, on a manual clock.</summary>
public class TimerTests
{
    private readonly ManualClock _clock = new();

    [Fact]
    public void ACountdownRunsPausesResumesAndCompletesOnce()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(5));
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(3), t.Remaining);
        Assert.Equal(0.6, t.RemainingFraction, 3);

        Assert.True(t.Pause());
        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(3), t.Remaining); // paused time does not count
        Assert.False(t.Pause());
        Assert.True(t.Resume());
        _clock.Advance(TimeSpan.FromMinutes(3) - TimeSpan.FromMilliseconds(1));
        Assert.False(t.CheckCompleted());
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(t.CheckCompleted());
        Assert.False(t.CheckCompleted()); // exactly once
        Assert.Equal(CountdownState.Completed, t.State);
        Assert.Equal(TimeSpan.Zero, t.Remaining);
    }

    [Fact]
    public void MovingTheWallClockDoesNotChangeTheCountdown()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(10));
        _clock.JumpWallClock(TimeSpan.FromHours(5));
        Assert.Equal(TimeSpan.FromMinutes(10), t.Remaining);
        _clock.JumpWallClock(TimeSpan.FromHours(-30));
        _clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(6), t.Remaining);
        Assert.False(t.CheckCompleted());
    }

    [Fact]
    public void AddingTimeAndSnoozingAndResetting()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(1));
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.True(t.Add(TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.FromSeconds(90), t.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(2), t.Duration);

        Assert.True(t.Reset());
        Assert.Equal(CountdownState.Idle, t.State);
        Assert.Equal(TimeSpan.FromMinutes(2), t.Remaining);

        t.Start(TimeSpan.FromSeconds(5));
        _clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(t.CheckCompleted());
        Assert.True(t.Add(TimeSpan.FromMinutes(1))); // snooze
        Assert.Equal(CountdownState.Running, t.State);
        Assert.Equal(TimeSpan.FromMinutes(1), t.Remaining);
        Assert.False(t.Add(TimeSpan.Zero));
    }

    [Fact]
    public void DurationsOutsideOneSecondToADayAreRejected()
    {
        var t = new CountdownTimer(_clock);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Start(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Start(TimeSpan.FromHours(25)));
    }

    [Fact]
    public void NextChangeIsTheNextWholeSecondSoTheDisplayUpdatesAtMostOncePerSecond()
    {
        var t = new CountdownTimer(_clock);
        Assert.Null(t.NextChange());
        t.Start(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(1), t.NextChange());
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(TimeSpan.FromMilliseconds(700), t.NextChange());
        t.Pause();
        Assert.Null(t.NextChange()); // nothing changes while paused: no timer
    }

    [Theory]
    [InlineData(299.2, "5:00")]
    [InlineData(300, "5:00")]
    [InlineData(59.01, "1:00")]
    [InlineData(0.4, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(3_899, "1:04:59")]
    public void FormatRoundsUpSoZeroMeansDone(double seconds, string expected) =>
        Assert.Equal(expected, CountdownTimer.Format(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(299, "5 minutes left")]
    [InlineData(61, "2 minutes left")]
    [InlineData(60, "60 seconds left")]
    [InlineData(43, "50 seconds left")]
    [InlineData(10, "10 seconds left")]
    [InlineData(1, "1 second left")]
    [InlineData(3_700, "1 hour 2 minutes left")]
    public void SpokenTimeIsCoarseSoScreenReadersAreNotFloodedEverySecond(int seconds, string expected) =>
        Assert.Equal(expected, CountdownTimer.Spoken(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void ARunningCountdownSurvivesARestartUsingTheWallClockEnd()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(10));
        _clock.Advance(TimeSpan.FromMinutes(3));
        var record = t.ToRecord();
        Assert.Equal(_clock.GetUtcNow().AddMinutes(7), record.EndUtc);

        _clock.Advance(TimeSpan.FromMinutes(2)); // Halo was closed for 2 minutes
        var restored = new CountdownTimer(_clock);
        Assert.True(restored.Restore(record));
        Assert.Equal(CountdownState.Running, restored.State);
        Assert.Equal(TimeSpan.FromMinutes(5), restored.Remaining);
        Assert.False(restored.Missed);
    }

    [Fact]
    public void ACountdownThatEndedWhileClosedRestoresAsMissed()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(1));
        var record = t.ToRecord();
        _clock.Advance(TimeSpan.FromMinutes(5));

        var restored = new CountdownTimer(_clock);
        Assert.True(restored.Restore(record));
        Assert.Equal(CountdownState.Completed, restored.State);
        Assert.True(restored.Missed);
        Assert.Equal("Timer ended while Halo was closed", TimerActivities.ForCountdown(restored, _clock.GetUtcNow())!.Accessible);
    }

    [Fact]
    public void RestoringCapsTimeAfterTheClockWasMovedBackAndRejectsNonsense()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(5));
        var record = t.ToRecord();
        _clock.JumpWallClock(TimeSpan.FromHours(-2));
        var restored = new CountdownTimer(_clock);
        Assert.True(restored.Restore(record));
        Assert.Equal(TimeSpan.FromMinutes(5), restored.Remaining); // not 2 h 5 min

        Assert.False(new CountdownTimer(_clock).Restore(new CountdownRecord(CountdownState.Running, TimeSpan.FromDays(3), _clock.GetUtcNow(), null)));
        Assert.False(new CountdownTimer(_clock).Restore(new CountdownRecord(CountdownState.Paused, TimeSpan.FromMinutes(1), null, TimeSpan.FromMinutes(9))));
        Assert.False(new CountdownTimer(_clock).Restore(new CountdownRecord(CountdownState.Running, TimeSpan.FromMinutes(1), null, null)));
    }

    [Fact]
    public void TimerActivityFollowsTheCountdown()
    {
        var t = new CountdownTimer(_clock);
        Assert.Null(TimerActivities.ForCountdown(t, null));
        t.Start(TimeSpan.FromMinutes(5));
        var running = TimerActivities.ForCountdown(t, null)!;
        Assert.Equal(("5:00", 1.0, false, true), (running.CompactText, running.Level!.Value, running.Emphasis, running.AllowedInQuietMode));
        Assert.Equal([TimerActions.Pause, TimerActions.AddMinute, TimerActions.Cancel], running.Actions.Select(a => a.Id));
        Assert.False(running.AllowedWhenSuppressed);

        _clock.Advance(TimeSpan.FromSeconds(291));
        Assert.True(TimerActivities.ForCountdown(t, null)!.Emphasis); // last 10 seconds
        t.Pause();
        Assert.Equal([TimerActions.Resume, TimerActions.Reset, TimerActions.Cancel], TimerActivities.ForCountdown(t, null)!.Actions.Select(a => a.Id));
        Assert.False(TimerActivities.ForCountdown(t, null)!.Emphasis);

        t.Resume();
        _clock.Advance(TimeSpan.FromSeconds(9));
        t.CheckCompleted();
        var done = TimerActivities.ForCountdown(t, _clock.GetUtcNow())!;
        Assert.Equal((AlertPriority.High, true, true, false), (done.Priority, done.AllowedWhenSuppressed, done.Emphasis, done.Ongoing));
        Assert.Equal(_clock.GetUtcNow() + TimerActivities.DoneLingers, done.ExpiresAt);
        Assert.Equal([TimerActions.Dismiss, TimerActions.Snooze], done.Actions.Select(a => a.Id));
    }

    [Fact]
    public void AFocusSessionCyclesWorkAndBreaksWithALongBreakAfterTheCycle()
    {
        var plan = new FocusPlan(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), 2);
        var s = new FocusSession(_clock);
        s.Start(plan);
        Assert.Equal((FocusPhase.Work, 1), (s.Phase, s.CycleIndex));

        var phases = new List<FocusPhase>();
        for (var i = 0; i < 4; i++)
        {
            _clock.Advance(plan.DurationOf(s.Phase));
            var ended = s.CheckCompleted();
            Assert.NotNull(ended);
            phases.Add(ended!.Value.Next);
            Assert.Null(s.CheckCompleted()); // once per phase
        }

        Assert.Equal([FocusPhase.ShortBreak, FocusPhase.Work, FocusPhase.LongBreak, FocusPhase.Work], phases);
        Assert.Equal(2, s.CompletedWork);
        Assert.True(s.IsActive);
    }

    [Fact]
    public void FocusSkipPauseStopAndActivity()
    {
        var s = new FocusSession(_clock);
        s.Start(FocusPlan.Default);
        Assert.Equal(FocusPhase.ShortBreak, s.Skip()!.Value.Next);
        Assert.True(s.Pause());
        var paused = TimerActivities.ForFocus(s)!;
        Assert.Contains("paused", paused.Subtitle, StringComparison.Ordinal);
        Assert.Equal([TimerActions.Resume, TimerActions.Skip, TimerActions.Stop], paused.Actions.Select(a => a.Id));
        Assert.StartsWith("Short break", paused.Accessible, StringComparison.Ordinal);
        s.Stop();
        Assert.False(s.IsActive);
        Assert.Null(TimerActivities.ForFocus(s));
        Assert.Null(s.Skip());
    }

    [Fact]
    public void AFocusPhaseThatEndedWhileClosedResumesAtTheNextPhasePaused()
    {
        var s = new FocusSession(_clock);
        s.Start(FocusPlan.Default);
        var record = s.ToRecord();
        _clock.Advance(TimeSpan.FromHours(2));
        var restored = new FocusSession(_clock);
        Assert.True(restored.Restore(record));
        Assert.Equal(FocusPhase.ShortBreak, restored.Phase);
        Assert.Equal(CountdownState.Paused, restored.Countdown.State);
        Assert.Equal(1, restored.CompletedWork);
    }

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(60, "1 min")]
    [InlineData(95, "1 min 35 s")]
    [InlineData(300, "5 min")]
    [InlineData(5400, "1 h 30 min")]
    [InlineData(3600, "1 h")]
    public void DurationLabels(int seconds, string expected) =>
        Assert.Equal(expected, TimerActivities.DurationLabel(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void ARunningTimerSaysWhenItEndsAndADoneTimerHasNoBar()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(5));
        var local = new DateTimeOffset(2026, 10, 29, 14, 0, 0, TimeSpan.Zero);
        Assert.Contains(" · ends ", TimerActivities.ForCountdown(t, null, local)!.Subtitle, StringComparison.Ordinal);
        t.Pause();
        Assert.DoesNotContain("ends", TimerActivities.ForCountdown(t, null, local)!.Subtitle, StringComparison.Ordinal);
        t.Resume();
        _clock.Advance(TimeSpan.FromMinutes(5));
        t.CheckCompleted();
        Assert.Null(TimerActivities.ForCountdown(t, _clock.GetUtcNow())!.Level);
    }

    [Fact]
    public void FocusPlanBounds()
    {
        Assert.NotNull(FocusPlan.TryCreate(25, 5, 15, 4));
        Assert.Null(FocusPlan.TryCreate(0, 5, 15, 4));
        Assert.Null(FocusPlan.TryCreate(25, 5, 15, 0));
        Assert.Null(FocusPlan.TryCreate(500, 5, 15, 4));
    }

    [Fact]
    public void QuietModeDurations()
    {
        var now = new DateTimeOffset(2026, 10, 29, 9, 30, 0, TimeSpan.Zero);
        var hour = QuietMode.Start(QuietKind.OneHour, now, TimeZoneInfo.Utc);
        Assert.True(hour.IsActive(now.AddMinutes(59)));
        Assert.False(hour.IsActive(now.AddMinutes(60)));
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero), QuietMode.Start(QuietKind.UntilTomorrow, now, TimeZoneInfo.Utc).UntilUtc);
        var manual = QuietMode.Start(QuietKind.UntilTurnedOff, now, TimeZoneInfo.Utc);
        Assert.Null(manual.UntilUtc);
        Assert.True(manual.IsActive(now.AddYears(1)));
        Assert.False(QuietMode.Off.IsActive(now));
    }

    [Fact]
    public void UntilTomorrowUsesLocalMidnightAcrossADaylightSavingChange()
    {
        // A zone with a one-hour DST change at 02:00 on the last Sunday of October (like Central Europe).
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/CE", TimeSpan.FromHours(1), "Test", "Test", "Test DST", [rule]);

        // Saturday 24 October 2026, 23:00 local (summer time, UTC+2) → midnight is in summer time.
        var saturday = new DateTimeOffset(2026, 10, 24, 21, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), QuietMode.NextLocalMidnight(saturday, zone));

        // Sunday 25 October 2026 (the change day), 12:00 local → the next midnight is in winter time (UTC+1).
        var sunday = new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), QuietMode.NextLocalMidnight(sunday, zone));
    }

    [Fact]
    public void TheQuietGateAdmitsAllowedItemsAndCountsTheRest()
    {
        var gate = new QuietGate();
        var now = _clock.GetUtcNow();
        Assert.True(gate.Admit(now, false)); // off: everything
        gate.Set(QuietMode.Start(QuietKind.OneHour, now, TimeZoneInfo.Utc));
        Assert.False(gate.Admit(now, false));
        Assert.False(gate.Admit(now, false));
        Assert.True(gate.Admit(now, allowedInQuietMode: true));
        Assert.Equal(2, gate.Held);
        Assert.True(gate.Admit(now.AddHours(1), false)); // expired by time
        Assert.Equal(2, gate.End());
        Assert.Equal(0, gate.Held);
    }

    [Fact]
    public void TheControllerHoldsBackAlertsInQuietModeButNotCriticalOrUserRequestedOnes()
    {
        var timer = new VirtualTimer();
        var island = new IslandController(timer, IslandTiming.Default, timer);
        timer.Attach(island);
        _ = island.Raise(A("network", AlertPriority.Normal)); // shown
        _ = island.Raise(A("power", AlertPriority.Low)); // queued behind it
        Assert.Single(island.QueuedAlerts);

        island.SetQuiet(true);
        Assert.Empty(island.QueuedAlerts); // what waited would have arrived after quiet mode ended
        Assert.False(island.Raise(A("volume", AlertPriority.Normal)).Changed);
        Assert.False(island.Raise(A("battery", AlertPriority.High)).Changed);
        Assert.Equal(2, island.HeldWhileQuiet);
        Assert.Empty(island.QueuedAlerts);

        timer.Advance(TimeSpan.FromSeconds(10)); // the network alert ends
        Assert.True(island.Raise(A("battery", AlertPriority.Critical)).Changed);
        timer.Advance(TimeSpan.FromSeconds(10));
        Assert.True(island.Raise(TimerActivities.CompletedAlert(false)).Changed);
        Assert.Equal(2, island.SetQuiet(false));
        Assert.Equal(0, island.HeldWhileQuiet);
    }

    [Fact]
    public void StateRoundTripsAndInvalidPartsAreDropped()
    {
        var t = new CountdownTimer(_clock);
        t.Start(TimeSpan.FromMinutes(5));
        var focus = new FocusSession(_clock);
        focus.Start(FocusPlan.Default);
        var state = new HaloState { Timer = t.ToRecord(), Focus = focus.ToRecord(), Quiet = QuietMode.Start(QuietKind.OneHour, _clock.GetUtcNow(), TimeZoneInfo.Utc) };

        var (ok, parsed) = HaloState.Parse(state.ToJson());
        Assert.True(ok);
        Assert.Equal(state.Timer, parsed.Timer);
        Assert.Equal(state.Quiet, parsed.Quiet);
        Assert.Equal(state.Focus!.Plan, parsed.Focus!.Plan);
        Assert.Equal(state.Focus.Countdown, parsed.Focus.Countdown);

        var (ok2, junk) = HaloState.Parse("{ \"schemaVersion\": 1, \"timer\": { \"state\": \"Exploded\" }, \"quiet\": { \"kind\": \"OneHour\" }, \"focus\": { \"phase\": \"Work\", \"workMinutes\": 0 } }");
        Assert.True(ok2);
        Assert.Null(junk.Timer);
        Assert.Null(junk.Focus);
        Assert.Equal(QuietKind.Off, junk.Quiet.Kind); // one hour without an end time is not trusted
        Assert.False(HaloState.Parse("[]").Ok);
    }

    [Fact]
    public void TheStateStoreRecoversFromACorruptFileUsingTheBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "halo-state-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StateStore(dir);
            Assert.Equal(HaloState.Empty, store.Load(out var missing));
            Assert.Equal(DataLoadStatus.Missing, missing);

            var first = new HaloState { Quiet = new QuietMode(QuietKind.UntilTurnedOff, null) };
            Assert.True(store.Save(first));
            Assert.False(store.Save(first)); // unchanged: not rewritten
            Assert.True(store.Save(first with { Quiet = QuietMode.Off })); // first version is now the .bak
            File.WriteAllText(store.FilePath, "{ not json");

            var reopened = new StateStore(dir);
            var recovered = reopened.Load(out var status);
            Assert.Equal(DataLoadStatus.RecoveredFromBackup, status);
            Assert.Equal(QuietKind.UntilTurnedOff, recovered.Quiet.Kind);
            Assert.Single(Directory.GetFiles(dir, "state.json.corrupt-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static IslandAlert A(string key, AlertPriority priority) =>
        new(key, AlertKind.Test, priority, new AlertContent("x", key), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
}
