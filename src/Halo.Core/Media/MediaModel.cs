using System.Globalization;
using System.Text;

namespace Halo.Core.Media;

/// <summary>Playback state as reported by the session.</summary>
public enum MediaPlayback
{
    Unknown,
    Stopped,
    Paused,
    Playing,
}

/// <summary>One media session as seen by the selector (FR-MED-03).</summary>
/// <param name="Id">Source app id (opaque; never shown or logged).</param>
/// <param name="Playback">Its playback state.</param>
public readonly record struct MediaSessionInfo(string Id, MediaPlayback Playback);

/// <summary>
/// What the island shows about the selected session (FR-MED-01). Memory only: never persisted or logged
/// (SECURITY_AND_PRIVACY_DESIGN §2). Text is sanitised by <see cref="MediaText.Clean"/> before it gets here.
/// </summary>
public sealed record MediaSnapshot
{
    public required string Title { get; init; }

    public required string Artist { get; init; }

    public MediaPlayback Playback { get; init; }

    public bool CanPlayPause { get; init; }

    public bool CanNext { get; init; }

    public bool CanPrevious { get; init; }

    /// <summary>Timeline, or null when the player reports none or an invalid one (FR-MED-05: hide progress).</summary>
    public MediaTimeline? Timeline { get; init; }

    /// <summary>Album art as premultiplied BGRA, <see cref="ArtSize"/> square, or null.</summary>
    public IReadOnlyList<byte>? Art { get; init; }

    public int ArtSize { get; init; }

    /// <summary>A changing number for the art, so the surface is redrawn only when the picture changes.</summary>
    public int ArtVersion { get; init; }

    /// <summary>How many sessions Windows lists (diagnostics only; says nothing about their content).</summary>
    public int SessionCount { get; init; }

    /// <summary>The session reports a seekable position (J-F02): only then is the progress bar a seek control.</summary>
    public bool CanSeek { get; init; }

    /// <summary>A short name for the session's app, shown only when several sessions exist (J-F01). Never logged.</summary>
    public string SourceName { get; init; } = string.Empty;

    /// <summary>1-based position of this session among <see cref="SessionCount"/>.</summary>
    public int SessionIndex { get; init; } = 1;

    public bool IsPlaying => Playback == MediaPlayback.Playing;

    /// <summary>"Title — Artist", or whichever exists; "Unknown title" when neither does.</summary>
    public string Headline => (Title.Length, Artist.Length) switch
    {
        (> 0, > 0) => $"{Title} — {Artist}",
        (> 0, _) => Title,
        (_, > 0) => Artist,
        _ => "Unknown title",
    };

    /// <summary>UIA summary for the compact island (FR-A11Y-01): "Playing: Title by Artist".</summary>
    public string AccessibleSummary
    {
        get
        {
            var verb = Playback switch
            {
                MediaPlayback.Playing => "Playing",
                MediaPlayback.Paused => "Paused",
                _ => "Media",
            };
            var title = Title.Length > 0 ? Title : "unknown title";
            return Artist.Length > 0 ? $"{verb}: {title} by {Artist}" : $"{verb}: {title}";
        }
    }
}

/// <summary>A timeline reported by the player at <see cref="UpdatedAt"/>.</summary>
public sealed record MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Validates a player-reported timeline (end after start, duration under 24 h, position not wildly outside).
    /// Returns null when it should not be shown.
    /// </summary>
    public static MediaTimeline? FromPlayer(TimeSpan start, TimeSpan end, TimeSpan position, DateTimeOffset updatedAt)
    {
        var duration = end - start;
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24))
        {
            return null;
        }

        var p = position - start;
        if (p < -TimeSpan.FromSeconds(1) || p > duration + TimeSpan.FromSeconds(5))
        {
            return null;
        }

        return new MediaTimeline(p < TimeSpan.Zero ? TimeSpan.Zero : p, duration, updatedAt);
    }

    /// <summary>Position now: interpolated while playing (no timer needed when paused), clamped to the duration.</summary>
    public TimeSpan PositionAt(DateTimeOffset now, bool playing)
    {
        var p = playing ? Position + (now - UpdatedAt) : Position;
        return p < TimeSpan.Zero ? TimeSpan.Zero : p > Duration ? Duration : p;
    }

    public double FractionAt(DateTimeOffset now, bool playing) => PositionAt(now, playing).TotalSeconds / Duration.TotalSeconds;

    /// <summary>Elapsed time, "1:23" (the left label of the media card).</summary>
    public string ElapsedAt(DateTimeOffset now, bool playing) => Format(PositionAt(now, playing));

    /// <summary>Remaining time, "-2:22" (the right label of the media card).</summary>
    public string RemainingAt(DateTimeOffset now, bool playing) => "-" + Format(Duration - PositionAt(now, playing));

    /// <summary>"1:23 / 3:45" (hours when needed).</summary>
    public string LabelAt(DateTimeOffset now, bool playing) => $"{Format(PositionAt(now, playing))} / {Format(Duration)}";

    private static string Format(TimeSpan t) => t.TotalHours >= 1
        ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
        : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
}

/// <summary>Media text hygiene (SECURITY_AND_PRIVACY_DESIGN §3: other apps' strings are untrusted).</summary>
public static class MediaText
{
    public const int MaxLength = 512;

    /// <summary>Drops control and format characters, collapses whitespace, trims and caps the length.</summary>
    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var b = new StringBuilder(Math.Min(value.Length, MaxLength));
        var space = false;
        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                space = b.Length > 0;
                continue;
            }

            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned)
            {
                continue;
            }

            if (space)
            {
                b.Append(' ');
                space = false;
            }

            b.Append(c);
            if (b.Length >= MaxLength)
            {
                break;
            }
        }

        return b.ToString();
    }
}

/// <summary>Which session the island follows (FR-MED-03, J-F01).</summary>
public static class MediaSessionSelector
{
    /// <summary>
    /// The user's choice while that session exists (J-F01), else Windows' current session if it is still listed, else
    /// the first playing one, else the first; null if none.
    /// </summary>
    public static string? Select(IReadOnlyList<MediaSessionInfo> sessions, string? currentId, string? preferredId = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (preferredId is not null && sessions.Any(s => string.Equals(s.Id, preferredId, StringComparison.Ordinal)))
        {
            return preferredId;
        }

        if (currentId is not null && sessions.Any(s => string.Equals(s.Id, currentId, StringComparison.Ordinal)))
        {
            return currentId;
        }

        foreach (var s in sessions)
        {
            if (s.Playback == MediaPlayback.Playing)
            {
                return s.Id;
            }
        }

        return sessions.Count > 0 ? sessions[0].Id : null;
    }

    /// <summary>The session after <paramref name="selectedId"/> in list order, wrapping; null with fewer than two.</summary>
    public static string? Next(IReadOnlyList<MediaSessionInfo> sessions, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (sessions.Count < 2)
        {
            return null;
        }

        var i = sessions.ToList().FindIndex(s => string.Equals(s.Id, selectedId, StringComparison.Ordinal));
        return sessions[(i + 1) % sessions.Count].Id;
    }

    /// <summary>
    /// A short app name from a session's AppUserModelID: "Spotify.exe" → "Spotify",
    /// "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "ZuneMusic". Cleaned and capped; "Media" when nothing
    /// readable remains.
    /// </summary>
    public static string SourceName(string? appId)
    {
        var s = appId ?? string.Empty;
        var slash = s.LastIndexOfAny(['\\', '/']);
        if (slash >= 0)
        {
            s = s[(slash + 1)..];
        }

        var bang = s.LastIndexOf('!');
        if (bang >= 0)
        {
            s = s[(bang + 1)..];
        }

        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^4];
        }

        var dot = s.LastIndexOf('.');
        if (dot >= 0 && dot < s.Length - 1)
        {
            s = s[(dot + 1)..];
        }

        var underscore = s.IndexOf('_', StringComparison.Ordinal);
        if (underscore > 0)
        {
            s = s[..underscore];
        }

        s = MediaText.Clean(s);
        return s.Length is 0 or > 32 ? "Media" : s;
    }
}
