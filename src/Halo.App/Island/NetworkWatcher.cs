using System.Runtime.InteropServices;
using Halo.Core.Network;
using Windows.Networking.Connectivity;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Connectivity through <c>Windows.Networking.Connectivity.NetworkInformation</c> (FR-SYS-04, M3-F05). Event-driven:
/// <c>NetworkStatusChanged</c> only posts <see cref="ChangedMessage"/>; the island waits for 2 s of quiet and then calls
/// <see cref="Read"/>, which reads the internet profile off the UI thread and posts <see cref="ReadingMessage"/>.
/// Only the connectivity level and the adapter type are read: never the profile name (the Wi-Fi name), addresses,
/// traffic or usage.
/// </summary>
internal sealed class NetworkWatcher : IDisposable
{
    public const uint ChangedMessage = WmApp + 8;
    public const uint ReadingMessage = WmApp + 9;
    private const uint IanaEthernet = 6;

    private readonly nint _hwnd;
    private readonly bool _fail;
    private readonly Lock _gate = new();
    private NetworkReading? _latest;
    private bool _subscribed;
    private int _reading;
    private volatile bool _again;
    private volatile bool _disposed;

    /// <param name="hwnd">Window that receives the messages.</param>
    /// <param name="fail">Test fault: every read fails as if the API were unavailable.</param>
    public NetworkWatcher(nint hwnd, bool fail = false)
    {
        _hwnd = hwnd;
        _fail = fail;
    }

    /// <summary>HRESULT of the last failed read (0 after a good one), for diagnostics.</summary>
    public int LastHResult { get; private set; }

    /// <summary>The newest reading (read on the UI thread after <see cref="ReadingMessage"/>).</summary>
    public NetworkReading? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>Subscribes to status changes. False if the API is unavailable (reads are still attempted).</summary>
    public bool Start()
    {
        try
        {
            NetworkInformation.NetworkStatusChanged += OnStatusChanged;
            _subscribed = true;
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            LastHResult = ex.HResult;
            return false;
        }
    }

    /// <summary>Reads the current state off the UI thread; a read requested while one runs is run once afterwards.</summary>
    public void Read()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref _reading, 1) == 1)
        {
            _again = true;
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var reading = ReadNow();
                lock (_gate)
                {
                    _latest = reading;
                }

                if (!_disposed)
                {
                    _ = PostMessageW(_hwnd, ReadingMessage, 0, 0);
                }
            }
            finally
            {
                _ = Interlocked.Exchange(ref _reading, 0);
                if (_again && !_disposed)
                {
                    _again = false;
                    Read();
                }
            }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        if (_subscribed)
        {
            NetworkInformation.NetworkStatusChanged -= OnStatusChanged;
            _subscribed = false;
        }
    }

    private NetworkReading ReadNow()
    {
        try
        {
            if (_fail)
            {
                throw new InvalidOperationException("Injected network fault.") { HResult = unchecked((int)0x80004005) };
            }

            var profile = NetworkInformation.GetInternetConnectionProfile();
            LastHResult = 0;
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
                : profile.NetworkAdapter?.IanaInterfaceType == IanaEthernet ? NetworkKind.Ethernet
                : NetworkKind.Other;
            return new NetworkReading(state, state == NetworkState.Disconnected ? NetworkKind.None : kind);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // FR-SYS-04 "temporary API failures": report unknown; the next change event tries again.
            LastHResult = ex.HResult;
            return NetworkReading.Unknown;
        }
    }

    private void OnStatusChanged(object? sender)
    {
        if (!_disposed)
        {
            _ = PostMessageW(_hwnd, ChangedMessage, 0, 0);
        }
    }
}
