using System.Runtime.InteropServices;
using Halo.Core.Media;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>Whether the media integration works on this machine (FR-APP-08 module state).</summary>
internal enum MediaStatus
{
    Starting,
    Running,
    Unavailable,
}

/// <summary>
/// Media sessions through the Windows media session manager (GSMTC; FR-MED-01/03/04). Event-driven: session,
/// metadata, playback and timeline events only post <see cref="DirtyMessage"/>; the island debounces them and then
/// calls <see cref="Refresh"/>, which builds a <see cref="MediaSnapshot"/> off the UI thread and posts
/// <see cref="SnapshotMessage"/>. Timeline-only events reuse the cached metadata. Album art comes only from the
/// session's own thumbnail (no network), is size-capped and decoded off the UI thread. Nothing is stored or logged.
/// </summary>
internal sealed class MediaWatcher : IDisposable
{
    public const uint DirtyMessage = WmApp + 4;
    public const uint SnapshotMessage = WmApp + 5;
    public const int ArtPixels = 96;
    private const long MaxArtBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan ArtTimeout = TimeSpan.FromSeconds(2);

    private readonly nint _hwnd;
    private readonly Lock _gate = new();
    private readonly List<GlobalSystemMediaTransportControlsSession> _subscribed = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _selected;
    private string? _selectedId;
    private MediaSnapshot? _latest;
    private string? _metaKey;
    private (string Title, string Artist, byte[]? Art, int Version) _meta;
    private volatile bool _metadataStale = true;
    private volatile bool _disposed;
    private int _refreshing;
    private volatile bool _again;
    private int _artVersion;

    private readonly DiagnosticsLog? _log;

    public MediaWatcher(nint hwnd, DiagnosticsLog? log = null)
    {
        _hwnd = hwnd;
        _log = log;
    }

    public MediaStatus Status { get; private set; } = MediaStatus.Starting;

    /// <summary>HRESULT of the start failure, for diagnostics.</summary>
    public int FailureHResult { get; private set; }

    /// <summary>The newest snapshot (null = no session). Read on the UI thread after <see cref="SnapshotMessage"/>.</summary>
    public MediaSnapshot? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <summary>The selected session, for playback commands (M3-F02); with its id, so a command cannot hit another one.</summary>
    public (GlobalSystemMediaTransportControlsSession? Session, string? Id) Selected
    {
        get
        {
            lock (_gate)
            {
                return (_selected, _selectedId);
            }
        }
    }

    public async Task StartAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
            {
                return;
            }

            _manager = manager;
            manager.SessionsChanged += OnSessionsChanged;
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            Status = MediaStatus.Running;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // FR-MED-01: manager fails → Unavailable; the island carries on without media.
            Status = MediaStatus.Unavailable;
            FailureHResult = ex.HResult;
        }

        Dirty(metadata: true);
    }

    /// <summary>Builds a new snapshot off the UI thread; a refresh requested while one runs is run once afterwards.</summary>
    public void Refresh()
    {
        if (_disposed || _manager is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            _again = true;
            return;
        }

        _ = Task.Run(RefreshAsync);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_manager is { } manager)
        {
            manager.SessionsChanged -= OnSessionsChanged;
            manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        lock (_gate)
        {
            foreach (var s in _subscribed)
            {
                Unsubscribe(s);
            }

            _subscribed.Clear();
            _selected = null;
            _latest = null;
        }
    }

    /// <summary>Sends a transport command off the UI thread and logs its result token (never the session's content).</summary>
    internal static void Send(GlobalSystemMediaTransportControlsSession session, MediaCommand command, string token, DiagnosticsLog? log, Halo.Core.Diagnostics.LogFile? fileLog) =>
        _ = Task.Run(async () =>
        {
            var (result, hresult) = await SendAsync(session, command);
            if (result == "failed")
            {
                fileLog?.Write(Halo.Core.Diagnostics.LogLevel.Warning, "media.command_failed", ("command", token), ("hresult", hresult));
            }

            log?.Write("media.command", ("command", token), ("result", result));
        });

    /// <summary>Sends one transport command; returns "accepted", "refused" (the session declined) or "failed".</summary>
    internal static async Task<(string Result, int HResult)> SendAsync(GlobalSystemMediaTransportControlsSession session, MediaCommand command)
    {
        try
        {
            var accepted = command switch
            {
                MediaCommand.Previous => await session.TrySkipPreviousAsync(),
                MediaCommand.Next => await session.TrySkipNextAsync(),
                _ => await session.TryTogglePlayPauseAsync(),
            };
            return (accepted ? "accepted" : "refused", 0);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The session vanished while the command was in flight.
            return ("failed", ex.HResult);
        }
    }

    internal static MediaPlayback Map(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    {
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlayback.Playing,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlayback.Paused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaPlayback.Stopped,
        _ => MediaPlayback.Unknown,
    };

    private static async Task<byte[]?> DecodeArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbnail)
    {
        using var stream = await thumbnail.OpenReadAsync();
        if (stream.Size == 0 || stream.Size > MaxArtBytes)
        {
            return null; // FR-MED-04: huge or empty → placeholder
        }

        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform { ScaledWidth = ArtPixels, ScaledHeight = ArtPixels, InterpolationMode = BitmapInterpolationMode.Fant };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        return data.DetachPixelData();
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Event("sessions", metadata: true);

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Event("current", metadata: true);

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Event("properties", metadata: true);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Event("playback", metadata: false);

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => Event("timeline", metadata: false);

    private void Event(string kind, bool metadata)
    {
        _log?.Write("media.event", ("kind", kind));
        Dirty(metadata);
    }

    private void Dirty(bool metadata)
    {
        if (_disposed)
        {
            return;
        }

        if (metadata)
        {
            _metadataStale = true;
        }

        _ = PostMessageW(_hwnd, DirtyMessage, 0, 0);
    }

    private void Subscribe(GlobalSystemMediaTransportControlsSession s)
    {
        s.MediaPropertiesChanged += OnMediaPropertiesChanged;
        s.PlaybackInfoChanged += OnPlaybackInfoChanged;
        s.TimelinePropertiesChanged += OnTimelineChanged;
    }

    private void Unsubscribe(GlobalSystemMediaTransportControlsSession s)
    {
        s.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        s.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        s.TimelinePropertiesChanged -= OnTimelineChanged;
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snapshot = await BuildAsync();
            if (!_disposed)
            {
                lock (_gate)
                {
                    _latest = snapshot;
                }

                _ = PostMessageW(_hwnd, SnapshotMessage, 0, 0);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or TimeoutException)
        {
            // A session that vanishes mid-read throws; the next event retries (FR-MED-03 fallback).
            _metadataStale = true;
            _log?.Write("media.refresh_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
        }
        finally
        {
            _ = Interlocked.Exchange(ref _refreshing, 0);
            if (_again)
            {
                _again = false;
                Dirty(metadata: false);
            }
        }
    }

    private async Task<MediaSnapshot?> BuildAsync()
    {
        var manager = _manager!;
        var sessions = manager.GetSessions().ToList();
        var current = manager.GetCurrentSession();
        lock (_gate)
        {
            foreach (var gone in _subscribed.Where(s => !sessions.Contains(s)).ToList())
            {
                Unsubscribe(gone);
                _ = _subscribed.Remove(gone);
            }

            foreach (var added in sessions.Where(s => !_subscribed.Contains(s)))
            {
                Subscribe(added);
                _subscribed.Add(added);
            }
        }

        var infos = sessions.Select(s => new MediaSessionInfo(s.SourceAppUserModelId ?? string.Empty, Map(s.GetPlaybackInfo().PlaybackStatus))).ToList();
        var id = MediaSessionSelector.Select(infos, current?.SourceAppUserModelId);
        var session = id is null ? null : sessions.FirstOrDefault(s => string.Equals(s.SourceAppUserModelId, id, StringComparison.Ordinal));
        lock (_gate)
        {
            if (!string.Equals(_selectedId, id, StringComparison.Ordinal))
            {
                _metadataStale = true;
            }

            _selected = session;
            _selectedId = id;
        }

        if (session is null)
        {
            _metaKey = null;
            return null;
        }

        if (_metadataStale)
        {
            _metadataStale = false;
            var props = await session.TryGetMediaPropertiesAsync();
            var title = MediaText.Clean(props?.Title);
            var artist = MediaText.Clean(string.IsNullOrEmpty(props?.Artist) ? props?.AlbumArtist : props?.Artist);
            var key = $"{id}\u0001{title}\u0001{artist}\u0001{MediaText.Clean(props?.AlbumTitle)}";
            if (!string.Equals(key, _metaKey, StringComparison.Ordinal))
            {
                byte[]? art = null;
                if (props?.Thumbnail is { } thumbnail)
                {
                    var decode = DecodeArtAsync(thumbnail);
                    art = await Task.WhenAny(decode, Task.Delay(ArtTimeout)) == decode && decode.IsCompletedSuccessfully ? decode.Result : null;
                }

                _meta = (title, artist, art, ++_artVersion);
                _metaKey = key;
            }
        }

        var playback = session.GetPlaybackInfo();
        var controls = playback.Controls;
        var t = session.GetTimelineProperties();
        var timeline = MediaTimeline.FromPlayer(t.StartTime, t.EndTime, t.Position, t.LastUpdatedTime);
        return new MediaSnapshot
        {
            Title = _meta.Title,
            Artist = _meta.Artist,
            Playback = Map(playback.PlaybackStatus),
            CanPlayPause = controls.IsPlayPauseToggleEnabled,
            CanNext = controls.IsNextEnabled,
            CanPrevious = controls.IsPreviousEnabled,
            Timeline = timeline,
            Art = _meta.Art,
            ArtSize = _meta.Art is null ? 0 : ArtPixels,
            ArtVersion = _meta.Version,
            SessionCount = sessions.Count,
        };
    }
}
