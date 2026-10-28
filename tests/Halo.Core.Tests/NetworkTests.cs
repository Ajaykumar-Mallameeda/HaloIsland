using Halo.Core.Island;
using Halo.Core.Network;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Connectivity readings and network alert rules (M3-F05, FR-SYS-04), on synthetic readings.</summary>
public class NetworkTests
{
    private static readonly NetworkReading WiFi = new(NetworkState.Connected, NetworkKind.WiFi);
    private static readonly NetworkReading Ethernet = new(NetworkState.Connected, NetworkKind.Ethernet);
    private static readonly NetworkReading LimitedWiFi = new(NetworkState.Limited, NetworkKind.WiFi);

    [Theory]
    [InlineData(NetworkState.Connected, NetworkKind.WiFi, "")]
    [InlineData(NetworkState.Connected, NetworkKind.Ethernet, "")]
    [InlineData(NetworkState.Connected, NetworkKind.Cellular, "")]
    [InlineData(NetworkState.Limited, NetworkKind.WiFi, "")]
    [InlineData(NetworkState.Limited, NetworkKind.Ethernet, "")]
    [InlineData(NetworkState.Disconnected, NetworkKind.None, "")]
    [InlineData(NetworkState.Unknown, NetworkKind.None, "")]
    public void GlyphShowsStateAndKind(NetworkState state, NetworkKind kind, string glyph) =>
        Assert.Equal(glyph, new NetworkReading(state, kind).Glyph);

    [Fact]
    public void SpokenTextIsShortAndHasNoNetworkName()
    {
        Assert.Equal("Connected to Wi-Fi", WiFi.Spoken);
        Assert.Equal("Wi-Fi, limited connectivity", LimitedWiFi.Spoken);
        Assert.Equal("Offline", NetworkReading.Offline.Spoken);
        Assert.Equal(string.Empty, NetworkReading.Unknown.Spoken);
    }

    [Fact]
    public void StartUpNeverAlertsEvenWhenOffline()
    {
        Assert.Null(new NetworkMonitor().Update(WiFi));
        var offline = new NetworkMonitor();
        Assert.Null(offline.Update(NetworkReading.Offline));
        Assert.Equal(NetworkReading.Offline, offline.Current); // still shown as offline
    }

    [Fact]
    public void LostLimitedRestoredAndSwitchedEachAlertOnce()
    {
        var monitor = new NetworkMonitor();
        monitor.Update(WiFi);

        var lost = monitor.Update(NetworkReading.Offline)!;
        Assert.Equal((NetworkMonitor.Key, AlertKind.Network, AlertPriority.Normal, "Offline"), (lost.Key, lost.Kind, lost.Priority, lost.Content.Text));
        Assert.False(lost.AllowedWhenSuppressed);
        Assert.Null(monitor.Update(NetworkReading.Offline)); // unchanged

        Assert.Equal("Connected to Wi-Fi", monitor.Update(WiFi)!.Content.Text); // reconnect
        Assert.Equal("Limited connectivity", monitor.Update(LimitedWiFi)!.Content.Text);
        Assert.Equal("Connected to Wi-Fi", monitor.Update(WiFi)!.Content.Text);
        Assert.Equal("Connected to Ethernet", monitor.Update(Ethernet)!.Content.Text); // Wi-Fi → Ethernet
    }

    [Fact]
    public void AFailedReadIsIgnoredAndDoesNotFakeAReconnect()
    {
        var monitor = new NetworkMonitor();
        monitor.Update(WiFi);

        Assert.Null(monitor.Update(NetworkReading.Unknown));
        Assert.Equal(NetworkReading.Unknown, monitor.Current);
        Assert.Null(monitor.Update(WiFi)); // same as the last known state: no "Connected" pill
        Assert.NotNull(monitor.Update(NetworkReading.Offline));
    }

    [Fact]
    public void AnUnknownStartIsFollowedByASilentFirstRealReading()
    {
        var monitor = new NetworkMonitor();
        Assert.Null(monitor.Update(NetworkReading.Unknown));
        Assert.Null(monitor.Update(NetworkReading.Offline)); // first known state: treated as start-up
        Assert.NotNull(monitor.Update(WiFi));
    }

    [Fact]
    public void ABurstOfNetworkChangesIsOnePill()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default, clock);
        clock.Attach(island);
        var monitor = new NetworkMonitor();
        monitor.Update(WiFi);

        foreach (var r in new[] { NetworkReading.Offline, WiFi, NetworkReading.Offline, LimitedWiFi, WiFi })
        {
            if (monitor.Update(r) is { } alert)
            {
                island.Raise(alert);
            }

            clock.Advance(TimeSpan.FromMilliseconds(200));
        }

        Assert.Equal("Connected to Wi-Fi", island.State.Alert!.Content.Text); // the newest, one pill
        Assert.Empty(island.QueuedAlerts);
    }
}
