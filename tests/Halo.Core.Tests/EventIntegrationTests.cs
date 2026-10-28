using Halo.Core.Audio;
using Halo.Core.Island;
using Halo.Core.Network;
using Halo.Core.Power;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>
/// M3-F06: the real volume, battery and network alerts together through one <see cref="IslandController"/>, on
/// virtual time. Priorities: critical battery 3 &gt; low battery 2 &gt; volume, charger, network 1.
/// </summary>
public class EventIntegrationTests
{
    private static readonly NetworkReading WiFi = new(NetworkState.Connected, NetworkKind.WiFi);

    [Fact]
    public void SimultaneousEventsShowTheMostImportantAndKeepWhatStillMatters()
    {
        var (island, clock) = Create();
        island.Raise(VolumeAlert.For(new VolumeReading(0.4f, false)));
        island.Raise(LowBattery());
        island.Raise(Offline());

        Assert.Equal(AlertKind.Battery, island.State.Alert!.Kind); // High replaced the Normal volume pill
        Assert.Single(island.QueuedAlerts); // network waits; the replaced volume pill is gone

        clock.Advance(TimeSpan.FromSeconds(5)); // battery low ends
        Assert.Equal("Offline", island.State.Alert!.Content.Text); // still relevant after 5 s
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Null(island.State.Alert);
        Assert.Equal(IslandPresentation.Compact, island.State.Presentation);
    }

    [Fact]
    public void AnEqualPriorityEventReplacesTheOneOnScreen()
    {
        var (island, clock) = Create();
        island.Raise(VolumeAlert.For(new VolumeReading(0.4f, false)));
        clock.Advance(TimeSpan.FromMilliseconds(500));

        island.Raise(Offline());

        Assert.Equal(AlertKind.Network, island.State.Alert!.Kind);
        Assert.Empty(island.QueuedAlerts);
    }

    [Fact]
    public void OverAFullScreenAppOnlyCriticalBatteryAppears()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.FullscreenStarted);

        island.Raise(VolumeAlert.For(new VolumeReading(0.4f, false)));
        island.Raise(Offline());
        island.Raise(LowBattery());
        Assert.Null(island.State.Alert);

        island.Raise(CriticalBattery());
        Assert.Equal(AlertPriority.Critical, island.State.Alert!.Priority);
        Assert.True(island.State.Visible);

        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.Null(island.State.Alert);
        Assert.False(island.State.Visible); // hidden again: the game is still in front
    }

    [Fact]
    public void AnEventWhileTheUserHasTheIslandOpenNeverInterruptsAndExpiresUnseen()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.Expand); // pinned by the user
        island.Raise(VolumeAlert.For(new VolumeReading(0.4f, false)));
        island.Raise(Offline());
        Assert.Equal(IslandPresentation.Expanded, island.State.Presentation);
        Assert.Null(island.State.Alert);

        clock.Advance(TimeSpan.FromSeconds(11)); // both stale (1 s and 10 s)
        island.Apply(IslandInput.Collapse);
        Assert.Null(island.State.Alert);
        Assert.Empty(island.QueuedAlerts);
    }

    [Fact]
    public void TurningAFeatureOffDropsItsAlertsOnScreenAndQueued()
    {
        var (island, clock) = Create();
        island.Raise(CriticalBattery());
        island.Raise(Offline()); // queued behind critical

        var t = island.DropAlerts(AlertKind.Battery, AlertKind.Power);

        Assert.True(t.AlertChanged);
        Assert.Equal(AlertKind.Network, island.State.Alert!.Kind); // the next queued alert takes its place
        Assert.True(clock.IsAlertArmed);

        island.DropAlerts(AlertKind.Network);
        Assert.Null(island.State.Alert);
        Assert.False(clock.IsAlertArmed); // no stale alert timer left behind
        Assert.False(island.DropAlerts(AlertKind.Volume).Changed);
    }

    [Fact]
    public void ShutdownIgnoresLaterEventsAndDrops()
    {
        var (island, clock) = Create();
        island.Raise(Offline());
        island.Shutdown();

        Assert.False(island.Raise(CriticalBattery()).Changed);
        Assert.False(island.DropAlerts(AlertKind.Network).Changed);
        Assert.False(clock.IsAlertArmed);
    }

    private static IslandAlert LowBattery() =>
        new BatteryMonitor(BatteryThresholds.Default).Update(new PowerReading(true, 15, PowerSource.Battery, false))!;

    private static IslandAlert CriticalBattery() =>
        new BatteryMonitor(BatteryThresholds.Default).Update(new PowerReading(true, 5, PowerSource.Battery, false))!;

    private static IslandAlert Offline()
    {
        var monitor = new NetworkMonitor();
        monitor.Update(WiFi);
        return monitor.Update(NetworkReading.Offline)!;
    }

    private static (IslandController Island, VirtualTimer Clock) Create()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default, clock);
        clock.Attach(island);
        return (island, clock);
    }
}
