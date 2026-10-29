using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Network;
using Halo.Core.Notifications;
using Halo.Core.Power;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>
/// Live Activity Engine 2.0: lifecycle, the deterministic scheduler, the line/pill presentation state and the
/// provider adapters (docs/LIVE_ACTIVITY_ENGINE_2_DESIGN.md).
/// </summary>
public class LiveActivityEngineTests
{
    private readonly ManualClock _time = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));

    // ---- Lifecycle ----

    [Fact]
    public void FourActivitiesCoexistAndEachKeepsItsOwnState()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("04:59"));
        board.Upsert(Media(playing: true));
        board.Upsert(Charging(64));
        board.Upsert(Camera());
        Assert.Equal(4, board.Count);

        board.Upsert(Timer("04:58"));
        Assert.Equal("04:58", board.Find("timer")!.CompactText);
        Assert.Equal("Song", board.Find("media")!.CompactText); // updating one never resets another
        Assert.Equal("64%", board.Find(PowerActivities.ChargingId)!.CompactText);
        Assert.NotNull(board.Find("camera"));
    }

    [Fact]
    public void RepeatedEventsUpdateTheSameActivityInsteadOfDuplicating()
    {
        var board = new ActivityBoard(_time);
        for (var i = 0; i < 100; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            board.Upsert(Timer($"{i}"));
        }

        Assert.Equal(1, board.Count);
        Assert.Equal(_time.GetUtcNow().AddSeconds(-99), board.Find("timer")!.CreatedAt); // identity and start kept
    }

    [Fact]
    public void ADismissedActivityStaysHiddenUntilItsProviderEndsIt()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Offline());
        Assert.True(board.Dismiss(NetworkActivities.OfflineId).PrimaryChanged);
        Assert.Null(board.Primary);

        // The provider keeps reporting "offline": nothing comes back.
        Assert.False(board.Upsert(Offline()).Changed);
        Assert.True(board.IsRetired(NetworkActivities.OfflineId));

        // Connectivity returns (the provider ends it) and is lost again: a new activity.
        board.End(NetworkActivities.OfflineId);
        Assert.True(board.Upsert(Offline()).PrimaryChanged);
        Assert.Contains(board.History, h => h.Reason == ActivityEnd.Dismissed && h.Provider == "network");
    }

    [Fact]
    public void ThePrivacyIndicatorCannotBeDismissed()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Camera());
        Assert.False(board.Dismiss("camera").Changed);
        Assert.Equal("camera", board.Primary!.Id);
    }

    [Fact]
    public void AnExpiredActivityDoesNotReappearFromARepeatedPublication()
    {
        var board = new ActivityBoard(_time);
        var plugged = _time.GetUtcNow();
        board.Upsert(PowerActivities.Charging(new PowerReading(true, 50, PowerSource.Ac, true), plugged)!);
        _time.Advance(PowerActivities.ChargingLingers);
        Assert.True(board.RemoveExpired().PrimaryChanged);

        // Still charging: the provider publishes the same activity again, and it stays gone.
        Assert.False(board.Upsert(PowerActivities.Charging(new PowerReading(true, 51, PowerSource.Ac, true), plugged)!).Changed);
        Assert.Null(board.Primary);
        Assert.Contains(board.History, h => h.Reason == ActivityEnd.Expired);
    }

    [Fact]
    public void HistoryAndRetiredIdsAreBounded()
    {
        var board = new ActivityBoard(_time);
        for (var i = 0; i < 200; i++)
        {
            board.Upsert(Timer("x") with { Id = $"t{i}" });
            board.Dismiss($"t{i}");
        }

        Assert.Equal(ActivityBoard.MaxHistory, board.History.Count);
        Assert.Equal(0, board.Count);
        Assert.False(board.IsRetired("t0")); // the oldest memory went first
        Assert.True(board.IsRetired("t199"));
    }

    [Fact]
    public void StoppingAProviderRemovesOnlyItsActivities()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("04:59"));
        board.Upsert(Offline());
        board.StopProvider(LiveProviders.Network);
        Assert.Null(board.Find(NetworkActivities.OfflineId));
        Assert.NotNull(board.Find("timer"));
    }

    [Fact]
    public void InvalidInputIsRejected()
    {
        var board = new ActivityBoard(_time);
        Assert.Throws<ArgumentNullException>(() => board.Upsert(null!));
        Assert.Throws<ArgumentException>(() => board.Upsert(Timer("x") with { Id = string.Empty }));
        Assert.False(board.End("missing").Changed);
        Assert.False(board.Dismiss("missing").Changed);
    }

    // ---- Scheduler ----

    [Fact]
    public void EqualPriorityActivitiesKeepTheFirstStartedAndTheOtherWaitsBesideIt()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("x") with { Id = "a", Relevance = 0 });
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Upsert(Timer("x") with { Id = "b", Relevance = 0 });
        Assert.Equal(("a", "b"), (board.Primary!.Id, board.Secondary!.Id));

        // Relevance decides before start time (Apple's relevanceScore).
        board.Upsert(Timer("x") with { Id = "b", Relevance = 50 });
        Assert.Equal(("b", "a"), (board.Primary!.Id, board.Secondary!.Id));
    }

    [Fact]
    public void AnUrgentEventDuringMediaTakesTheIslandAndMediaReturnsAfter()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Media(playing: true));
        board.Upsert(Offline());
        Assert.Equal(NetworkActivities.OfflineId, board.Primary!.Id);
        Assert.Equal("media", board.Secondary!.Id); // media keeps playing in the bubble
        board.End(NetworkActivities.OfflineId);
        Assert.Equal("media", board.Primary!.Id);
    }

    [Fact]
    public void ChargingWhileAFocusTimerRunsNeverDisplacesTheTimer()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Focus());
        board.Upsert(Charging(30));
        Assert.Equal("focus", board.Primary!.Id);
        Assert.Equal(PowerActivities.ChargingId, board.Secondary!.Id);
    }

    [Fact]
    public void AReminderDoesNotTakeTheIslandFromTheUsersSelection()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("04:59"));
        board.Upsert(Media(playing: true));
        board.Select("media");
        board.Upsert(Reminder());
        Assert.Equal("media", board.Primary!.Id); // the user's choice outranks a high-priority reminder
        board.Upsert(Offline());
        Assert.Equal(NetworkActivities.OfflineId, board.Primary!.Id); // operational urgency outranks the choice
        board.End(NetworkActivities.OfflineId);
        Assert.Equal("media", board.Primary!.Id); // and the choice returns
        board.End("media");
        Assert.Null(board.SelectedId); // the selection ends with its activity
        Assert.Equal("reminder", board.Primary!.Id);
    }

    [Fact]
    public void QuietModeDefersNonessentialActivitiesButNeverCriticalOnes()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Reminder());
        board.Upsert(Timer("x") with { Id = "critical", Priority = AlertPriority.Critical, AllowedInQuietMode = false });
        board.SetQuiet(true);
        Assert.Equal("critical", board.Primary!.Id);
        Assert.Null(board.Secondary); // the reminder is held, not dropped
        board.SetQuiet(false);
        Assert.Equal("reminder", board.Secondary!.Id);
    }

    [Fact]
    public void FullScreenSuppressionAndRecoveryRestoreTheSameChoice()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Media(playing: true));
        board.Upsert(Timer("04:59"));
        var before = (board.Primary!.Id, board.Secondary!.Id);
        board.SetSuppressed(true);
        Assert.Null(board.Primary);
        board.SetSuppressed(false);
        Assert.Equal(before, (board.Primary!.Id, board.Secondary!.Id));
    }

    [Fact]
    public void PausedMediaStaysOnTheBoardButLetsTheIslandRest()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Media(playing: false));
        Assert.Null(board.Primary); // background only: the island can rest as a line
        Assert.Equal("media", board.First(_ => true)!.Id);
        board.Upsert(Media(playing: true));
        Assert.Equal("media", board.Primary!.Id); // same activity, resumed
    }

    [Fact]
    public void APausedTimerRanksBelowAnActiveOneOfTheSamePriority()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("04:59") with { Lifecycle = ActivityLifecycle.Paused });
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Upsert(Focus());
        Assert.Equal("focus", board.Primary!.Id);
    }

    [Fact]
    public void CompletionSelectsTheNextEligibleActivityPredictably()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("x") with { Id = "a" });
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Upsert(Timer("x") with { Id = "b" });
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Upsert(Timer("x") with { Id = "c", Ongoing = false, ExpiresAt = _time.GetUtcNow().AddSeconds(5), Priority = AlertPriority.High });
        Assert.Equal("c", board.Primary!.Id);
        _time.Advance(TimeSpan.FromSeconds(5));
        board.RemoveExpired();
        Assert.Equal(("a", "b"), (board.Primary!.Id, board.Secondary!.Id));
    }

    [Fact]
    public void RotationIsOffByDefaultAndStepsOnlyThroughTiedActivities()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(Timer("x") with { Id = "a" });
        board.Upsert(Timer("x") with { Id = "b" });
        board.Upsert(Charging(50)); // lower priority: never rotated in
        Assert.False(board.Rotate().Changed);

        board.RotationEnabled = true;
        Assert.True(board.CanRotate);
        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            seen.Add(board.Primary!.Id);
            board.Rotate();
        }

        Assert.Equal(["a", "b", "a", "b"], seen);
        board.Select("a");
        Assert.False(board.CanRotate); // the user's choice stops rotation
    }

    [Fact]
    public void EquivalentEventSequencesGiveTheSameDecisionWhateverTheOrder()
    {
        var events = new Func<IslandActivity>[] { () => Timer("x"), () => Media(true), () => Charging(40), Camera, Reminder, Focus };
        string? expected = null;
        foreach (var order in Permutations(Enumerable.Range(0, events.Length).ToList()))
        {
            var board = new ActivityBoard(new ManualClock()); // all at the same instant
            foreach (var i in order)
            {
                board.Upsert(events[i]());
            }

            var decision = string.Join(",", board.All.Select(a => a.Id)) + "|" + board.Primary!.Id + "|" + board.Secondary!.Id;
            expected ??= decision;
            Assert.Equal(expected, decision);
        }
    }

    // ---- Presentation state ----

    [Fact]
    public void TheIslandRestsAsALineAndGrowsIntoThePillWhenNeeded()
    {
        var timer = new VirtualTimer();
        var island = new IslandController(timer, IslandTiming.Default);
        Assert.Equal(IslandPresentation.Compact, island.State.Presentation); // thin-line mode starts off
        Assert.Equal(IslandPresentation.Line, island.SetLineIdle(true).Next.Presentation);

        // Pointer → pill (feedback) → expanded after the dwell → pill on leave after the grace → line.
        Assert.Equal(IslandPresentation.Compact, island.Apply(IslandInput.PointerEntered).Next.Presentation);
        Assert.Equal(IslandPresentation.Expanded, island.TimerElapsed(IslandTimerSlot.Intent).Next.Presentation);
        island.Apply(IslandInput.PointerLeft);
        Assert.Equal(IslandPresentation.Expanded, island.State.Presentation);
        Assert.Equal(IslandPresentation.Line, island.TimerElapsed(IslandTimerSlot.Intent).Next.Presentation);

        // A live activity → pill; it ends → line.
        Assert.Equal(IslandPresentation.Compact, island.SetLive(true).Next.Presentation);
        Assert.False(island.SetLive(true).Changed);
        Assert.Equal(IslandPresentation.Line, island.SetLive(false).Next.Presentation);

        // An alert shows as the alert pill from the line and returns to it.
        island.Raise(new IslandAlert("v", AlertKind.Volume, AlertPriority.Normal, new AlertContent("", "Volume"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
        Assert.Equal(IslandPresentation.Alert, island.State.Presentation);
        Assert.Equal(IslandPresentation.Line, island.TimerElapsed(IslandTimerSlot.Alert).Next.Presentation);
    }

    [Fact]
    public void PinningKeepsTheIslandOpenAndFullScreenRestoresTheRightPresentation()
    {
        var island = new IslandController(new VirtualTimer(), IslandTiming.Default);
        island.SetLineIdle(true);
        island.Apply(IslandInput.Expand);
        island.Apply(IslandInput.PointerLeft);
        Assert.Equal(IslandPresentation.Expanded, island.State.Presentation); // pinned: leaving does not close

        island.Apply(IslandInput.FullscreenStarted);
        Assert.False(island.State.Visible);
        island.SetLive(true); // an activity starts while hidden
        island.Apply(IslandInput.FullscreenEnded);
        Assert.True(island.State.Visible);
        Assert.Equal(IslandPresentation.Compact, island.State.Presentation); // suppression collapsed it; live → pill
    }

    [Fact]
    public void ClickOnlyModeStillRevealsThePillOnHoverWithoutOpening()
    {
        var island = new IslandController(new VirtualTimer(), IslandTiming.Default with { HoverEnabled = false });
        island.SetLineIdle(true);
        Assert.Equal(IslandPresentation.Compact, island.Apply(IslandInput.PointerEntered).Next.Presentation);
        Assert.Equal(IslandPhase.Collapsed, island.State.Phase);
        Assert.Equal(IslandPresentation.Line, island.Apply(IslandInput.PointerLeft).Next.Presentation);
    }

    [Fact]
    public void ShutdownIgnoresLateLiveAndLineChanges()
    {
        var island = new IslandController(new VirtualTimer(), IslandTiming.Default);
        island.Shutdown();
        Assert.False(island.SetLive(true).Changed);
        Assert.False(island.SetLineIdle(true).Changed);
    }

    [Fact]
    public void LineTransitionsAreShortAndReducedMotionIsInstant()
    {
        foreach (var (from, to) in new[] { (IslandPresentation.Line, IslandPresentation.Compact), (IslandPresentation.Compact, IslandPresentation.Line), (IslandPresentation.Line, IslandPresentation.Alert), (IslandPresentation.Line, IslandPresentation.Expanded) })
        {
            var plan = new Motion.MotionPlan(1, Motion.MotionPlanner.Transition(from, to, reducedMotion: false));
            Assert.Equal(TimeSpan.FromMilliseconds(220), plan.Spec(Motion.MotionKind.Morph).Duration);
            Assert.InRange(plan.Duration.TotalMilliseconds, 160, 300); // spec: 160–240 ms morph; content done by 300 ms
            var reduced = new Motion.MotionPlan(2, Motion.MotionPlanner.Transition(from, to, reducedMotion: true));
            Assert.True(reduced.Spec(Motion.MotionKind.Morph).IsInstant);
            Assert.False(reduced.Has(Motion.MotionKind.Settle));
        }

        var open = new Motion.MotionPlan(3, Motion.MotionPlanner.Transition(IslandPresentation.Compact, IslandPresentation.Expanded, false));
        Assert.InRange(open.Spec(Motion.MotionKind.Morph).Duration.TotalMilliseconds, 180, 300);
    }

    // ---- Providers ----

    [Fact]
    public void MediaIsOneActivityAcrossTracksAndPausedMediaIsBackgroundOnly()
    {
        var a = MediaActivities.For(new MediaSnapshot { Title = "One", Artist = "X", Playback = MediaPlayback.Playing })!;
        var b = MediaActivities.For(new MediaSnapshot { Title = "Two", Artist = "X", Playback = MediaPlayback.Playing })!;
        Assert.Equal(a.Id, b.Id);
        Assert.True(a.ShowsCompact);
        var paused = MediaActivities.For(new MediaSnapshot { Title = "Two", Artist = "X", Playback = MediaPlayback.Paused })!;
        Assert.Equal(ActivityLifecycle.Paused, paused.Lifecycle);
        Assert.False(paused.ShowsCompact);
        Assert.False(MediaActivities.For(new MediaSnapshot { Title = "Two", Artist = "X", Playback = MediaPlayback.Playing }, compact: false)!.ShowsCompact);
        Assert.Null(MediaActivities.For(null));
        Assert.Null(MediaActivities.For(new MediaSnapshot { Title = "Two", Artist = "X", Playback = MediaPlayback.Stopped }));
    }

    [Fact]
    public void ChargingIsBriefAndOnlyFromAConnection()
    {
        var now = _time.GetUtcNow();
        Assert.Null(PowerActivities.Charging(new PowerReading(true, 50, PowerSource.Ac, true), pluggedAt: null)); // started on the charger
        Assert.Null(PowerActivities.Charging(new PowerReading(true, 50, PowerSource.Battery, false), now));
        Assert.Null(PowerActivities.Charging(new PowerReading(false, null, PowerSource.Ac, false), now)); // desktop PC
        var c = PowerActivities.Charging(new PowerReading(true, 50, PowerSource.Ac, true), now)!;
        Assert.Equal(now + PowerActivities.ChargingLingers, c.ExpiresAt);
        Assert.Equal(AlertPriority.Low, c.Priority);
        Assert.Equal(0.5, c.Level);
    }

    [Fact]
    public void OnlyLostConnectivityIsAnActivity()
    {
        Assert.True(NetworkActivities.For(NetworkReading.Offline)!.Urgent);
        Assert.Null(NetworkActivities.For(new NetworkReading(NetworkState.Connected, NetworkKind.WiFi)));
        Assert.Null(NetworkActivities.For(NetworkReading.Unknown)); // a failed read never claims "offline"
    }

    [Theory]
    [InlineData(false, true, true, NotificationAccess.Allowed, NotificationAccess.Off)]
    [InlineData(true, false, true, NotificationAccess.Allowed, NotificationAccess.NoPackageIdentity)]
    [InlineData(true, true, false, NotificationAccess.Allowed, NotificationAccess.NotDeclared)]
    [InlineData(true, true, true, null, NotificationAccess.NotRequested)]
    [InlineData(true, true, true, NotificationAccess.Denied, NotificationAccess.Denied)]
    [InlineData(true, true, true, NotificationAccess.Allowed, NotificationAccess.Allowed)]
    public void WindowsNotificationAccessIsDecidedWithoutSideEffects(bool optedIn, bool identity, bool declared, NotificationAccess? windows, NotificationAccess expected)
    {
        var access = WindowsNotifications.Evaluate(optedIn, identity, declared, windows);
        Assert.Equal(expected, access);
        Assert.Equal(expected == NotificationAccess.Allowed, WindowsNotifications.CanRead(access));
    }

    [Fact]
    public void WindowsNotificationsArePrivateByDefaultAndCleaned()
    {
        var n = WindowsNotifications.From(7, "Mail", ["New message\u0007 from Sam", "Body"], _time.GetUtcNow())!;
        Assert.Equal(WindowsNotifications.ProviderId, n.ProviderId);
        Assert.Equal("7", n.SourceId);
        Assert.Equal("New message from Sam", n.Preview); // control characters removed
        Assert.Equal(WindowsNotifications.Redacted, n.Redacted);
        Assert.False(WindowsNotifications.DefaultPolicy.ShowPreview);
        Assert.Null(WindowsNotifications.From(8, "Halo", ["mine"], _time.GetUtcNow())); // never echo Halo's own
        Assert.Null(WindowsNotifications.From(9, "Mail", ["", "  "], _time.GetUtcNow()));
        Assert.Equal(120, WindowsNotifications.From(10, "Mail", [new string('a', 500)], _time.GetUtcNow())!.Preview.Length);

        // Through the existing center: with previews off only the generic text is visible.
        var center = new NotificationCenter(_time);
        center.Register(WindowsNotifications.ProviderId, WindowsNotifications.DefaultPolicy);
        var outcome = center.Submit(n);
        Assert.Equal(SubmitResult.Shown, outcome.Result);
        Assert.Equal(WindowsNotifications.Redacted, outcome.Activity!.CompactFor(showPrivate: false));
        Assert.Equal(SubmitResult.Duplicate, center.Submit(n).Result);
    }

    private static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, j) => j != i).ToList();
            foreach (var p in Permutations(rest))
            {
                yield return [items[i], .. p];
            }
        }
    }

    private static IslandActivity Timer(string text) => new("timer", ActivityKind.Timer, AlertPriority.Normal)
    {
        Provider = LiveProviders.Timer,
        Relevance = LiveProviders.CountdownRelevance,
        CompactText = text,
        Level = 0.5,
        AllowedInQuietMode = true,
    };

    private static IslandActivity Focus() => new("focus", ActivityKind.Focus, AlertPriority.Normal)
    {
        Provider = LiveProviders.Focus,
        Relevance = LiveProviders.CountdownRelevance,
        CompactText = "24:00",
        AllowedInQuietMode = true,
    };

    private static IslandActivity Media(bool playing) =>
        MediaActivities.For(new MediaSnapshot { Title = "Song", Artist = "Band", Playback = playing ? MediaPlayback.Playing : MediaPlayback.Paused })!;

    private IslandActivity Charging(int percent) =>
        PowerActivities.Charging(new PowerReading(true, percent, PowerSource.Ac, true), _time.GetUtcNow())!;

    private static IslandActivity Offline() => NetworkActivities.For(NetworkReading.Offline)!;

    private static IslandActivity Camera() => new("camera", ActivityKind.System, AlertPriority.Normal)
    {
        Provider = LiveProviders.Camera,
        Relevance = LiveProviders.CameraRelevance,
        Dismissible = false,
        CompactText = "Camera on",
    };

    private static IslandActivity Reminder() => new("reminder", ActivityKind.Reminder, AlertPriority.High) { CompactText = "Due" };
}
