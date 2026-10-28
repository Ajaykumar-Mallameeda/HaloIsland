using System.Globalization;
using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Notifications;

namespace Halo.Core.Accounts;

/// <summary>When to ask a provider again (E-F03 resource rules: bounded, backed off, stopped when not connected).</summary>
public static class PollSchedule
{
    public static readonly TimeSpan Normal = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

    /// <summary>The delay before the next poll, or null to stop until the user reconnects.</summary>
    public static TimeSpan? Next(ProviderStatus status, int consecutiveFailures, TimeSpan? retryAfter) => status switch
    {
        ProviderStatus.Connected => Normal,
        ProviderStatus.Throttled => retryAfter is { } r && r > TimeSpan.Zero ? (r < MaxBackoff ? r : MaxBackoff) : TimeSpan.FromMinutes(1),
        ProviderStatus.Unavailable => TimeSpan.FromMinutes(Math.Min(MaxBackoff.TotalMinutes, Math.Pow(2, Math.Clamp(consecutiveFailures, 0, 10)))),
        _ => null, // not connected, sign-in required, permission denied: never retried in a loop
    };
}

/// <summary>
/// New-mail notifications (E-F03). The first successful read after connecting sets a baseline: messages already
/// unread are counted, not announced, so connecting never floods the island. After that, each new unread message is
/// submitted once (the center deduplicates by message ID, so a later poll of the same message shows nothing).
/// </summary>
public sealed class MailWatcher(NotificationCenter center, string providerId = GraphClient.MailProviderId)
{
    public const string UnreadActivityId = "mail.unread";
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private bool _baseline;

    public ProviderStatus Status { get; private set; } = ProviderStatus.NotConnected;

    public int UnreadCount { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    /// <summary>Applies one provider read. Returns the notifications to show (activities and alerts).</summary>
    public IReadOnlyList<NotificationOutcome> Apply(ProviderResult<MailSnapshot> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Status = result.Status;
        if (!result.Ok)
        {
            ConsecutiveFailures = result.Status == ProviderStatus.Unavailable ? ConsecutiveFailures + 1 : 0;
            return [];
        }

        ConsecutiveFailures = 0;
        var snapshot = result.Value!;
        UnreadCount = snapshot.UnreadCount;
        var outcomes = new List<NotificationOutcome>();
        foreach (var m in snapshot.Unread.OrderBy(m => m.ReceivedUtc))
        {
            if (!_known.Add(m.Id) || !_baseline)
            {
                continue;
            }

            outcomes.Add(center.Submit(new HaloNotification(providerId, m.Id, AlertPriority.Normal)
            {
                Title = m.SenderName,
                Preview = m.Subject,
                Redacted = "New email",
                Glyph = "",
                OccurredAt = m.ReceivedUtc,
                Actions = m.WebLink is null ? [] : [new ActivityAction("open", "Open in Outlook", "")],
            }));
        }

        _baseline = true;
        if (_known.Count > 2000)
        {
            // Bounded: keep only what the provider still reports.
            _known.IntersectWith(snapshot.Unread.Select(m => m.Id));
        }

        return outcomes;
    }

    /// <summary>The unread-count activity ("3 unread"), or null when there is nothing unread or no connection.</summary>
    public IslandActivity? UnreadActivity() => Status == ProviderStatus.Connected && UnreadCount > 0
        ? new IslandActivity(UnreadActivityId, ActivityKind.Notification, AlertPriority.Low)
        {
            Glyph = "",
            CompactText = UnreadCount == 1 ? "1 unread" : string.Create(CultureInfo.CurrentCulture, $"{UnreadCount} unread"),
            Title = UnreadCount == 1 ? "1 unread email" : string.Create(CultureInfo.CurrentCulture, $"{UnreadCount} unread emails"),
            Accessible = UnreadCount == 1 ? "1 unread email" : string.Create(CultureInfo.CurrentCulture, $"{UnreadCount} unread emails"),
        }
        : null;

    /// <summary>Disconnect: forget everything (the center's live items are dropped by the caller).</summary>
    public void Reset()
    {
        _known.Clear();
        _baseline = false;
        UnreadCount = 0;
        Status = ProviderStatus.NotConnected;
        ConsecutiveFailures = 0;
    }
}

/// <summary>What the calendar glance shows (F-F02).</summary>
public enum GlanceState
{
    InProgress,
    Upcoming,
}

public sealed record CalendarGlance(CalendarEvent Event, GlanceState State, TimeSpan Until);

/// <summary>
/// Calendar glance and meeting reminders (F-F02/F-F03). Read-only: nothing is ever accepted, declined or changed.
/// <list type="bullet">
/// <item>Glance: the meeting in progress, else the next one within 12 hours; cancelled and all-day events are skipped.</item>
/// <item>Reminder: <see cref="Lead"/> before the start, once per event start time — a moved event reminds again at
/// its new time, a cancelled one is withdrawn.</item>
/// <item>Private events never show their subject, whatever the preview setting.</item>
/// </list>
/// </summary>
public sealed class CalendarWatcher(NotificationCenter center, string providerId = GraphClient.CalendarProviderId)
{
    public const string GlanceActivityId = "calendar.next";
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(12);
    private readonly Dictionary<string, string> _reminded = new(StringComparer.Ordinal); // event ID → source ID shown
    private IReadOnlyList<CalendarEvent> _events = [];

    public TimeSpan Lead { get; set; } = TimeSpan.FromMinutes(5);

    public ProviderStatus Status { get; private set; } = ProviderStatus.NotConnected;

    public int ConsecutiveFailures { get; private set; }

    public IReadOnlyList<CalendarEvent> Events => _events;

    /// <summary>Applies a provider read. Returns the source IDs of reminders to withdraw (cancelled or moved events).</summary>
    public IReadOnlyList<string> Apply(ProviderResult<IReadOnlyList<CalendarEvent>> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Status = result.Status;
        if (!result.Ok)
        {
            ConsecutiveFailures = result.Status == ProviderStatus.Unavailable ? ConsecutiveFailures + 1 : 0;
            return [];
        }

        ConsecutiveFailures = 0;
        _events = result.Value!;
        var withdraw = new List<string>();
        foreach (var (eventId, sourceId) in _reminded.ToList())
        {
            var e = _events.FirstOrDefault(x => x.Id == eventId);
            if (e is null || e.IsCancelled || SourceId(e) != sourceId)
            {
                withdraw.Add(sourceId);
                _ = _reminded.Remove(eventId);
            }
        }

        return withdraw;
    }

    public CalendarGlance? Glance(DateTimeOffset nowUtc)
    {
        var candidates = _events.Where(e => !e.IsCancelled && !e.IsAllDay && e.EndUtc > nowUtc).OrderBy(e => e.StartUtc).ToList();
        if (candidates.FirstOrDefault(e => e.StartUtc <= nowUtc) is { } current)
        {
            return new CalendarGlance(current, GlanceState.InProgress, current.EndUtc - nowUtc);
        }

        return candidates.FirstOrDefault(e => e.StartUtc - nowUtc <= Horizon) is { } next ? new CalendarGlance(next, GlanceState.Upcoming, next.StartUtc - nowUtc) : null;
    }

    /// <summary>The glance as an island activity (low priority; private unless previews are allowed).</summary>
    public IslandActivity? GlanceActivity(DateTimeOffset nowUtc, TimeZoneInfo zone, bool showPreview)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (Glance(nowUtc) is not { } g)
        {
            return null;
        }

        var e = g.Event;
        var subject = e.IsPrivate ? "Private appointment" : e.Subject.Length > 0 ? e.Subject : "Untitled event";
        var when = $"{TimeZoneInfo.ConvertTime(e.StartUtc, zone).ToString("t", CultureInfo.CurrentCulture)}–{TimeZoneInfo.ConvertTime(e.EndUtc, zone).ToString("t", CultureInfo.CurrentCulture)}";
        var compact = g.State == GlanceState.InProgress ? "Meeting now" : "In " + Short(g.Until);
        var showSubject = showPreview && !e.IsPrivate;
        return new IslandActivity(GlanceActivityId, ActivityKind.Calendar, AlertPriority.Low)
        {
            Glyph = "",
            CompactText = compact,
            RedactedText = compact,
            Title = showSubject ? subject : (g.State == GlanceState.InProgress ? "Meeting in progress" : "Next meeting"),
            Subtitle = showSubject && e.Location.Length > 0 ? $"{when} · {e.Location}" : when,
            Privacy = showSubject ? ActivityPrivacy.Public : ActivityPrivacy.Private,
            Actions = Links(e),
            Accessible = (g.State == GlanceState.InProgress ? "Meeting in progress, ends " : "Next meeting in " + Short(g.Until) + ", ") + when + (showSubject ? ", " + subject : e.IsPrivate ? ", private appointment" : string.Empty),
        };
    }

    /// <summary>Reminders that are due now (each event start once). Submitted through the center.</summary>
    public IReadOnlyList<NotificationOutcome> DueReminders(DateTimeOffset nowUtc)
    {
        var outcomes = new List<NotificationOutcome>();
        foreach (var e in _events.Where(e => !e.IsCancelled && !e.IsAllDay && e.StartUtc - Lead <= nowUtc && nowUtc < e.StartUtc + TimeSpan.FromMinutes(5)))
        {
            var sourceId = SourceId(e);
            if (_reminded.TryGetValue(e.Id, out var shown) && shown == sourceId)
            {
                continue;
            }

            _reminded[e.Id] = sourceId;
            var minutes = Math.Max(0, (int)Math.Ceiling((e.StartUtc - nowUtc).TotalMinutes));
            outcomes.Add(center.Submit(new HaloNotification(providerId, sourceId, AlertPriority.High)
            {
                Title = e.IsPrivate ? string.Empty : e.Subject,
                Preview = minutes == 0 ? "Starting now" : string.Create(CultureInfo.CurrentCulture, $"Starts in {minutes} min"),
                Redacted = minutes == 0 ? "Meeting starting now" : string.Create(CultureInfo.CurrentCulture, $"Meeting in {minutes} min"),
                Glyph = "",
                OccurredAt = e.StartUtc,
                Actions = Links(e),
            }));
        }

        return outcomes;
    }

    /// <summary>The next time something changes: a reminder becomes due, or the glance moves on.</summary>
    public DateTimeOffset? NextChange(DateTimeOffset nowUtc)
    {
        var times = _events.Where(e => !e.IsCancelled && !e.IsAllDay).SelectMany(e => new[] { e.StartUtc - Lead, e.StartUtc, e.EndUtc }).Where(t => t > nowUtc);
        return times.Any() ? times.Min() : null;
    }

    public void Reset()
    {
        _events = [];
        _reminded.Clear();
        Status = ProviderStatus.NotConnected;
        ConsecutiveFailures = 0;
    }

    /// <summary>The event's link for an action ("join" or "open"), validated again at use.</summary>
    public Uri? LinkFor(string sourceId, string action)
    {
        var eventId = sourceId.Split('@')[0];
        var e = _events.FirstOrDefault(x => x.Id == eventId);
        return action switch
        {
            "join" => e?.JoinUrl,
            "open" => e?.WebLink,
            _ => null,
        };
    }

    private static string SourceId(CalendarEvent e) => $"{e.Id}@{e.StartUtc.ToUnixTimeSeconds()}";

    private static List<ActivityAction> Links(CalendarEvent e)
    {
        var actions = new List<ActivityAction>(2);
        if (e.JoinUrl is not null)
        {
            actions.Add(new ActivityAction("join", "Join meeting", ""));
        }

        if (e.WebLink is not null)
        {
            actions.Add(new ActivityAction("open", "Open in Outlook", ""));
        }

        return actions;
    }

    private static string Short(TimeSpan t) => t.TotalMinutes < 60
        ? string.Create(CultureInfo.CurrentCulture, $"{Math.Max(1, (int)Math.Ceiling(t.TotalMinutes))} min")
        : string.Create(CultureInfo.CurrentCulture, $"{(int)t.TotalHours} h {t.Minutes} min");
}
