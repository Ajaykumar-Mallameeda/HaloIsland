using Halo.Core.Island;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Transient alert arbitration (ADR-001 §6.2) through <see cref="IslandController"/> on virtual time.</summary>
public class AlertTests
{
    private static readonly TimeSpan Ms100 = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void AnAlertShowsAsItsOwnPresentationAndExpires()
    {
        var (island, clock) = Create();

        var t = island.Raise(Volume(0.4));
        Assert.True(t.PresentationChanged);
        Assert.Equal(IslandPresentation.Alert, island.State.Presentation);
        Assert.Equal(IslandPhase.Collapsed, island.State.Phase); // an alert is not an expansion

        clock.Advance(TimeSpan.FromMilliseconds(1499));
        Assert.NotNull(island.State.Alert);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Null(island.State.Alert);
        Assert.Equal(IslandPresentation.Compact, island.State.Presentation);
    }

    [Fact]
    public void RepeatedEventsWithTheSameKeyCoalesceAndRestartTheTime()
    {
        var (island, clock) = Create();
        island.Raise(Volume(0.4));
        clock.Advance(TimeSpan.FromSeconds(1));

        var t = island.Raise(Volume(0.5));

        Assert.True(t.AlertChanged);
        Assert.False(t.PresentationChanged); // same pill, new level: no shape animation
        Assert.Equal(0.5, island.State.Alert!.Content.Level);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(island.State.Alert); // 2 s after the first event, 1 s after the last
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Null(island.State.Alert);
        Assert.Equal(1, clock.AlertFired);
    }

    [Fact]
    public void HigherOrEqualPriorityReplacesLowerWaits()
    {
        var (island, clock) = Create();
        island.Raise(Network());
        island.Raise(Volume(0.3)); // High replaces Normal
        Assert.Equal(AlertKind.Volume, island.State.Alert!.Kind);

        island.Raise(Network()); // Normal waits behind High
        Assert.Equal(AlertKind.Volume, island.State.Alert!.Kind);
        Assert.Single(island.QueuedAlerts);

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(AlertKind.Network, island.State.Alert!.Kind); // shown when the volume alert ends
        Assert.Empty(island.QueuedAlerts);
    }

    [Fact]
    public void StaleQueuedAlertsAreDropped()
    {
        var (island, clock) = Create();
        island.Raise(BatteryLow(critical: false)); // High, 4 s
        island.Raise(Network()); // Normal, waits; stale after 3 s

        clock.Advance(TimeSpan.FromSeconds(4));

        Assert.Null(island.State.Alert);
        Assert.Empty(island.QueuedAlerts);
    }

    [Fact]
    public void AnAlertDoesNotInterruptTheExpandedIslandAndAppearsAfterIfStillFresh()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.Expand);

        var t = island.Raise(BatteryLow(critical: false));
        Assert.False(t.Changed);
        Assert.Equal(IslandPresentation.Expanded, island.State.Presentation);

        clock.Advance(TimeSpan.FromSeconds(2));
        var collapse = island.Apply(IslandInput.Collapse);
        Assert.Equal(IslandPresentation.Alert, collapse.Next.Presentation); // the queued alert shows on collapse
    }

    [Fact]
    public void ExpiredEventsDoNotAppearAfterTheUserCollapses()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.Expand);
        island.Raise(Volume(0.2)); // stale after 1.5 s

        clock.Advance(TimeSpan.FromSeconds(5));
        island.Apply(IslandInput.Collapse);

        Assert.Equal(IslandPresentation.Compact, island.State.Presentation);
    }

    [Fact]
    public void AnAlertWaitsWhileThePointerIsAboutToExpandOrPressing()
    {
        var (island, _) = Create();
        island.Apply(IslandInput.PointerEntered); // dwell pending
        Assert.False(island.Raise(Volume(0.5)).Changed);

        var (pressed, _) = Create();
        pressed.Apply(IslandInput.PointerPressed);
        Assert.False(pressed.Raise(Volume(0.5)).Changed);
    }

    [Fact]
    public void HoveringAnAlertExpandsTheIslandAndRemovesTheAlert()
    {
        var (island, clock) = Create();
        island.Raise(Volume(0.5));

        island.Apply(IslandInput.PointerEntered);
        clock.Advance(IslandTiming.Default.HoverDwell);

        Assert.Equal(IslandPhase.ExpandedByHover, island.State.Phase);
        Assert.Null(island.State.Alert);
        Assert.False(clock.IsAlertArmed);
    }

    [Fact]
    public void ClickingAnAlertPinsTheIslandOpenAndTheAlertNeverPinsByItself()
    {
        var (island, clock) = Create();
        island.Raise(Volume(0.5));
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(IslandPhase.Collapsed, island.State.Phase); // expired, nothing pinned

        island.Raise(Volume(0.6));
        island.Apply(IslandInput.PointerEntered);
        island.Apply(IslandInput.PointerPressed);
        island.Apply(IslandInput.PointerReleasedInside);
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);
        Assert.Null(island.State.Alert);
    }

    [Fact]
    public void AStaleAlertTimerCannotChangeANewerUserState()
    {
        var (island, _) = Create();
        island.Raise(Volume(0.5));
        island.Apply(IslandInput.Expand); // clears the alert and its timer

        var late = island.TimerElapsed(IslandTimerSlot.Alert); // a callback already in flight

        Assert.False(late.Changed);
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);
    }

    [Fact]
    public void FullscreenSuppressionOnlyLetsAllowedAlertsThrough()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.FullscreenStarted);

        Assert.False(island.Raise(Volume(0.5)).Changed);
        Assert.False(island.State.Visible);

        var critical = island.Raise(BatteryLow(critical: true));
        Assert.True(critical.VisibilityChanged); // shown over the full-screen app for its duration
        Assert.True(island.State.Visible);

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(island.State.Visible); // hidden again afterwards
    }

    [Fact]
    public void FullscreenStartingRemovesAnOrdinaryAlert()
    {
        var (island, clock) = Create();
        island.Raise(Volume(0.5));

        island.Apply(IslandInput.FullscreenStarted);

        Assert.Null(island.State.Alert);
        Assert.False(clock.IsAlertArmed);
    }

    [Fact]
    public void TheQueueIsBoundedAndKeepsTheMostImportant()
    {
        var (island, _) = Create();
        island.Apply(IslandInput.Expand);
        for (var i = 0; i < 6; i++)
        {
            island.Raise(new IslandAlert($"n{i}", AlertKind.Test, AlertPriority.Low, new AlertContent(string.Empty, "x"), Ms100, TimeSpan.FromMinutes(1)));
        }

        island.Raise(BatteryLow(critical: true));

        Assert.Equal(IslandController.MaxQueuedAlerts, island.QueuedAlerts.Count);
        Assert.Contains(island.QueuedAlerts, a => a.Priority == AlertPriority.Critical);
    }

    [Fact]
    public void ShutdownCancelsAlertTimersAndDropsTheQueue()
    {
        var (island, clock) = Create();
        island.Raise(Volume(0.5));
        island.Raise(Network());

        island.Shutdown();

        Assert.False(clock.IsAlertArmed);
        Assert.Empty(island.QueuedAlerts);
        Assert.False(island.Raise(Volume(0.1)).Changed);
        Assert.False(island.TimerElapsed(IslandTimerSlot.Alert).Changed);
    }

    [Fact]
    public void LevelIsClampedForDisplay()
    {
        Assert.Equal(1, new AlertContent(string.Empty, "x", 1.7).ClampedLevel);
        Assert.Equal(0, new AlertContent(string.Empty, "x", -0.2).ClampedLevel);
        Assert.Equal(0, new AlertContent(string.Empty, "x", double.NaN).ClampedLevel);
        Assert.Null(new AlertContent(string.Empty, "x").ClampedLevel);
    }

    [Fact]
    public void AlertShapeIsAPillInsideTheExpandedShape()
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1020), 120);
        var a = layout.Alert;
        var e = layout.Expanded;

        Assert.Equal(400, a.Width); // 320 DIP × 1.25
        Assert.Equal(a.Height / 2, a.Radius);
        Assert.Equal(layout.Compact.Top, a.Top);
        Assert.True(a.Left >= e.Left && a.Right <= e.Right && a.Bottom <= e.Bottom);
        Assert.True(a.Width > layout.Compact.Width);
        Assert.Equal(a, layout.ShapeFor(IslandPresentation.Alert));
    }

    private static (IslandController Island, VirtualTimer Clock) Create()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default, clock);
        clock.Attach(island);
        return (island, clock);
    }

    private static IslandAlert Volume(double level) =>
        new("volume", AlertKind.Volume, AlertPriority.High, new AlertContent("v", "Volume", level, "x"), TimeSpan.FromMilliseconds(1500), TimeSpan.FromMilliseconds(1500));

    private static IslandAlert Network() =>
        new("network", AlertKind.Network, AlertPriority.Normal, new AlertContent("n", "Wi-Fi connected"), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));

    private static IslandAlert BatteryLow(bool critical) =>
        new("battery.low", AlertKind.Battery, critical ? AlertPriority.Critical : AlertPriority.High, new AlertContent("b", "Battery low", 0.1, "10%"), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(30), AllowedWhenSuppressed: critical);
}
