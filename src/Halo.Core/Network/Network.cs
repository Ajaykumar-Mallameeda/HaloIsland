using Halo.Core.Island;

namespace Halo.Core.Network;

/// <summary>Internet reachability of the connection Windows uses for the internet.</summary>
public enum NetworkState
{
    /// <summary>The API failed; nothing is shown or alerted.</summary>
    Unknown,
    Disconnected,

    /// <summary>Connected, but only local or constrained (captive portal) access.</summary>
    Limited,
    Connected,
}

/// <summary>Connection type, from the adapter only (never the network's name).</summary>
public enum NetworkKind
{
    None,
    WiFi,
    Ethernet,
    Cellular,
    Other,
}

/// <summary>
/// One connectivity reading (FR-SYS-04, M3-F05). It holds a state and a connection type, nothing else: no network
/// name, address, traffic or history (SECURITY_AND_PRIVACY_DESIGN, "As built, network").
/// </summary>
public readonly record struct NetworkReading(NetworkState State, NetworkKind Kind)
{
    public static NetworkReading Unknown => new(NetworkState.Unknown, NetworkKind.None);

    public static NetworkReading Offline => new(NetworkState.Disconnected, NetworkKind.None);

    /// <summary>Status glyph (Segoe Fluent Icons), or empty when unknown.</summary>
    public string Glyph => (State, Kind) switch
    {
        (NetworkState.Unknown, _) => string.Empty,
        (NetworkState.Disconnected, _) => "", // globe with a "no" sign
        (NetworkState.Limited, NetworkKind.WiFi) => "", // Wi-Fi with a warning
        (NetworkState.Limited, NetworkKind.Ethernet) => "", // Ethernet with an error mark
        (NetworkState.Limited, _) => "",
        (_, NetworkKind.WiFi) => "",
        (_, NetworkKind.Ethernet) => "",
        _ => "", // globe: cellular or another link
    };

    public string KindName => Kind switch
    {
        NetworkKind.WiFi => "Wi-Fi",
        NetworkKind.Ethernet => "Ethernet",
        NetworkKind.Cellular => "Cellular",
        _ => "network",
    };

    /// <summary>What a screen reader hears, for example "Connected to Wi-Fi" or "Offline".</summary>
    public string Spoken => State switch
    {
        NetworkState.Connected => $"Connected to {KindName}",
        NetworkState.Limited => $"{KindName}, limited connectivity",
        NetworkState.Disconnected => "Offline",
        _ => string.Empty,
    };
}

/// <summary>
/// Decides which network alerts to raise (FR-SYS-04). Bursts are debounced by the caller (2 s of quiet before a
/// reading); this class only compares readings.
/// <list type="bullet">
/// <item>The first reading (start-up, even when offline) never alerts.</item>
/// <item>Lost → "Offline"; restored or switched → "Connected to …"; degraded → "Limited connectivity".</item>
/// <item>An unchanged reading or an unknown one (API failure) never alerts. After an unknown reading the next real
/// one is compared with the last known one, so a failed read does not produce a false "Connected".</item>
/// </list>
/// </summary>
public sealed class NetworkMonitor
{
    public const string Key = "network";

    /// <summary>The last reading taken, including unknown ones.</summary>
    public NetworkReading? Current { get; private set; }

    private NetworkReading? _lastKnown;

    public IslandAlert? Update(NetworkReading next)
    {
        Current = next;
        if (next.State == NetworkState.Unknown)
        {
            return null;
        }

        var previous = _lastKnown;
        _lastKnown = next;
        if (previous is not { } before || before == next)
        {
            return null;
        }

        var text = next.State switch
        {
            NetworkState.Disconnected => "Offline",
            NetworkState.Limited => "Limited connectivity",
            _ => $"Connected to {next.KindName}",
        };
        var duration = next.State == NetworkState.Connected ? TimeSpan.FromMilliseconds(2500) : TimeSpan.FromSeconds(3);
        return new IslandAlert(Key, AlertKind.Network, AlertPriority.Normal, new AlertContent(next.Glyph, text) { Accessible = next.Spoken }, duration, TimeSpan.FromSeconds(10));
    }
}
