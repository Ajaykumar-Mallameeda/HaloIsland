using System.Globalization;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Network;
using Halo.Core.Power;
using Halo.Core.Theming;

namespace Halo.Core.Activities;

/// <summary>
/// Provider tokens and relevance scores (Live Activity Engine 2.0). Relevance only orders activities of the same
/// priority (Apple's relevanceScore): the camera indicator first, then countdowns the user started, then media.
/// </summary>
public static class LiveProviders
{
    public const string Timer = "timer";
    public const string Focus = "focus";
    public const string Media = "media";
    public const string Power = "power";
    public const string Network = "network";
    public const string Camera = "camera";
    public const string Reminders = "reminders";

    public const int CameraRelevance = 30;
    public const int CountdownRelevance = 20;
    public const int MediaRelevance = 10;
}

/// <summary>The media session as a live activity: one stable ID across tracks of the selected session.</summary>
public static class MediaActivities
{
    public const string ActivityId = "media";
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    /// <summary>
    /// Playing: an active activity that asks for the compact island (when <paramref name="compact"/>, the user's "show
    /// media in the pill" choice). Paused: kept in the background (it never brings the island up by itself). No
    /// session: null, and the caller ends the activity. Progress is not part of the activity (the media page reads the
    /// timeline itself), so per-second progress never touches the board.
    /// </summary>
    public static IslandActivity? For(MediaSnapshot? snapshot, bool compact = true)
    {
        if (snapshot is null || snapshot.Playback is not (MediaPlayback.Playing or MediaPlayback.Paused))
        {
            return null;
        }

        var playing = snapshot.IsPlaying;
        var title = snapshot.Title.Length > 0 ? snapshot.Title : snapshot.Headline;
        return new IslandActivity(ActivityId, ActivityKind.Media, AlertPriority.Normal)
        {
            Provider = LiveProviders.Media,
            Glyph = playing ? PauseGlyph : PlayGlyph,
            Tone = Tone.Media,
            CompactText = title,
            Title = title,
            Subtitle = snapshot.Artist,
            Lifecycle = playing ? ActivityLifecycle.Active : ActivityLifecycle.Paused,
            Relevance = LiveProviders.MediaRelevance,
            ShowsCompact = playing && compact,
            Accessible = snapshot.AccessibleSummary,
            AllowedInQuietMode = true,
        };
    }
}

/// <summary>Charging as a live activity (Apple shows charging briefly, never permanently).</summary>
public static class PowerActivities
{
    public const string ChargingId = "power/charging";

    /// <summary>How long the charging activity stays in the compact island after the charger is connected.</summary>
    public static readonly TimeSpan ChargingLingers = TimeSpan.FromSeconds(45);

    private const string ChargingGlyph = ""; // battery charging (Segoe Fluent Icons)

    /// <summary>
    /// The charging activity, from the moment the charger was connected (<paramref name="pluggedAt"/>, null when Halo
    /// started on the charger). It is transient: low priority, it expires after <see cref="ChargingLingers"/> and its
    /// ID then stays retired until the charger is removed, so a laptop that is always plugged in still rests as a line.
    /// </summary>
    public static IslandActivity? Charging(PowerReading reading, DateTimeOffset? pluggedAt)
    {
        if (!reading.HasBattery || !reading.Charging || pluggedAt is not { } since || reading.Percent is not { } p)
        {
            return null;
        }

        var text = string.Create(CultureInfo.CurrentCulture, $"{p}%");
        return new IslandActivity(ChargingId, ActivityKind.Power, AlertPriority.Low)
        {
            Provider = LiveProviders.Power,
            Glyph = ChargingGlyph,
            Tone = Tone.Charging,
            CompactText = text,
            Title = text,
            Subtitle = "Charging",
            Level = p / 100.0,
            Ongoing = false,
            ExpiresAt = since + ChargingLingers,
            Accessible = string.Create(CultureInfo.CurrentCulture, $"Charging, {p} percent"),
        };
    }
}

/// <summary>Lost connectivity as a live activity; normal connectivity stays passive (header icon only).</summary>
public static class NetworkActivities
{
    public const string OfflineId = "network/offline";

    /// <summary>An urgent, dismissible activity while offline; null otherwise (unknown readings never raise it).</summary>
    public static IslandActivity? For(NetworkReading reading) => reading.State == NetworkState.Disconnected
        ? new IslandActivity(OfflineId, ActivityKind.Network, AlertPriority.High)
        {
            Provider = LiveProviders.Network,
            Glyph = reading.Glyph,
            Tone = Tone.Offline,
            CompactText = "Offline",
            Title = "Offline",
            Subtitle = "No internet connection",
            Urgent = true,
            Accessible = "Offline",
        }
        : null;
}
