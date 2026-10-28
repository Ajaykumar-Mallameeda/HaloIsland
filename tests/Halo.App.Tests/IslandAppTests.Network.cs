using System.Diagnostics;
using System.IO;
using FlaUI.UIA3;
using Halo.Core.Network;
using Windows.Networking.Connectivity;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Network connectivity (M3-F05, FR-SYS-04). The start-up reading and the debounced re-read use the machine's real
/// connection; the owner's connection is never changed. Lost / limited / restored transitions are SYNTHETIC: a
/// diagnostics-only message feeds a reading through the same path a real one takes.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmNetworkChanged = 0x8000 + 8; // NetworkWatcher.ChangedMessage
    private const uint WmTestNetwork = 0x8000 + 52;

    [Fact]
    public void TheRealConnectionIsReadAtStartWithoutAPill()
    {
        var expected = ReadConnection();
        using var island = IslandProcess.Start();

        var first = WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "system");
        Assert.Equal(expected.State.ToString().ToLowerInvariant(), first["state"]!.GetValue<string>());
        Assert.Equal(expected.Kind.ToString().ToLowerInvariant(), first["kind"]!.GetValue<string>());
        Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() == "network");

        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        Assert.Contains(expected.Spoken, element.Properties.FullDescription.ValueOrDefault ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ABurstOfNetworkEventsIsReadOnceAfterTwoQuietSeconds()
    {
        using var island = IslandProcess.Start();
        WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "system");
        var reads = island.Events("network.read").Count;

        long lastPost = 0;
        for (var i = 0; i < 10; i++)
        {
            Assert.True(PostMessageW(island.Hwnd, WmNetworkChanged, 0, 0));
            lastPost = Stopwatch.GetTimestamp();
            Thread.Sleep(100);
        }

        Assert.True(WaitUntil(() => island.Events("network.read").Count > reads, 5000), "no read after the burst");
        Thread.Sleep(2500);
        var after = island.Events("network.read").Skip(reads).ToList();
        Assert.Single(after); // ten events, one read
        var quietMs = (after[0]["qpc"]!.GetValue<long>() - lastPost) * 1000.0 / Stopwatch.Frequency;
        Assert.InRange(quietMs, 1900, 3000); // read only after 2 s without another event
    }

    [Fact]
    public void SyntheticNetworkTransitionsFollowTheAlertRules()
    {
        using var island = IslandProcess.Start();
        WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "system");

        // Start the synthetic sequence from Wi-Fi, whatever the real connection is: an unknown reading first (ignored by
        // the alert rules), so the Wi-Fi reading is always processed.
        SendNetwork(island, NetworkState.Unknown, NetworkKind.None);
        WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "test" && e["state"]?.GetValue<string>() == "unknown");
        SendNetwork(island, NetworkState.Connected, NetworkKind.WiFi);
        WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "test" && e["kind"]?.GetValue<string>() == "wifi");
        Thread.Sleep(200); // a pill for a real Ethernet → test Wi-Fi switch is logged just after the reading
        var baseline = island.Events("alert").Count(e => e["kind"]?.GetValue<string>() == "network");

        SendNetwork(island, NetworkState.Disconnected, NetworkKind.None);
        WaitForNetwork(island, e => e["state"]?.GetValue<string>() == "disconnected");
        SendNetwork(island, NetworkState.Disconnected, NetworkKind.None); // unchanged: ignored
        SendNetwork(island, NetworkState.Limited, NetworkKind.WiFi);
        WaitForNetwork(island, e => e["state"]?.GetValue<string>() == "limited");
        SendNetwork(island, NetworkState.Connected, NetworkKind.Ethernet);
        WaitForNetwork(island, e => e["state"]?.GetValue<string>() == "connected" && e["kind"]?.GetValue<string>() == "ethernet");

        // Three changes, one key: one pill shown, then updated in place (never a queue of network pills).
        Assert.True(WaitUntil(() => island.Events("alert").Count(e => e["kind"]?.GetValue<string>() == "network") >= baseline + 3, 2000), Alerts(island));
        var states = island.Events("alert").Where(e => e["kind"]?.GetValue<string>() == "network").Skip(baseline).Select(e => e["state"]!.GetValue<string>()).ToList();
        Assert.DoesNotContain("queued", states);
        Assert.Equal(5, island.Events("network").Count(e => e["reason"]?.GetValue<string>() == "test")); // the repeated offline reading was dropped
    }

    [Fact]
    public void AnUnavailableNetworkApiIsLoggedAndTheIslandCarriesOn()
    {
        using (var island = IslandProcess.Start(fault: "network"))
        {
            var reading = WaitForNetwork(island, _ => true);
            Assert.Equal("unknown", reading["state"]!.GetValue<string>());
            Assert.NotEqual(0, island.Events("network.read").Last()["hresult"]!.GetValue<int>());
            Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() == "network");
            Assert.True(PostMessageW(island.Hwnd, WmNetworkChanged, 0, 0)); // a later event retries; still no crash
            Thread.Sleep(2500);
            island.Exit();
            var log = File.ReadAllText(Path.Combine(island.DataDir, "logs", "halo.log"));
            Assert.Single(log.Split('\n'), l => l.Contains("\"network.unavailable\"", StringComparison.Ordinal)); // once, not per event
        }

        using var off = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showNetwork\": false }");
        off.WaitFor("ready");
        Assert.True(PostMessageW(off.Hwnd, WmNetworkChanged, 0, 0));
        Thread.Sleep(600);
        Assert.Empty(off.Events("network"));
        Assert.Empty(off.Events("network.read"));
    }

    /// <summary>The same reading the island makes, taken by the test from the real API.</summary>
    private static NetworkReading ReadConnection()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile is null)
        {
            return NetworkReading.Offline;
        }

        var state = profile.GetNetworkConnectivityLevel() switch
        {
            NetworkConnectivityLevel.InternetAccess => NetworkState.Connected,
            NetworkConnectivityLevel.ConstrainedInternetAccess or NetworkConnectivityLevel.LocalAccess => NetworkState.Limited,
            _ => NetworkState.Disconnected,
        };
        var kind = profile.IsWlanConnectionProfile ? NetworkKind.WiFi
            : profile.IsWwanConnectionProfile ? NetworkKind.Cellular
            : profile.NetworkAdapter?.IanaInterfaceType == 6 ? NetworkKind.Ethernet
            : NetworkKind.Other;
        return new NetworkReading(state, state == NetworkState.Disconnected ? NetworkKind.None : kind);
    }

    private static void SendNetwork(IslandProcess island, NetworkState state, NetworkKind kind) =>
        Assert.True(PostMessageW(island.Hwnd, WmTestNetwork, (int)state | ((int)kind << 8), 0));

    private static System.Text.Json.Nodes.JsonObject WaitForNetwork(IslandProcess island, Func<System.Text.Json.Nodes.JsonObject, bool> match)
    {
        System.Text.Json.Nodes.JsonObject? found = null;
        Assert.True(
            WaitUntil(() => (found = island.Events("network").LastOrDefault(match)) is not null, 6000),
            $"no matching network event; seen: {string.Join(",", island.Events("network").Select(e => $"{e["state"]}/{e["kind"]}/{e["reason"]}"))}");
        return found!;
    }
}
