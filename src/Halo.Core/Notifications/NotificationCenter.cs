using Halo.Core.Activities;
using Halo.Core.Island;

namespace Halo.Core.Notifications;

/// <summary>
/// One notification from a provider (D-F01): Halo's own reminders, or an approved integration such as Outlook mail.
/// Holds only what the island needs. Content stays in memory and is never logged or persisted by the center.
/// </summary>
/// <param name="ProviderId">Fixed provider token, for example "halo.reminders" or "outlook.mail".</param>
/// <param name="SourceId">The provider's own identifier (deduplication key). Never shown.</param>
/// <param name="Priority">Arbitration priority.</param>
public sealed record HaloNotification(string ProviderId, string SourceId, AlertPriority Priority)
{
    /// <summary>Headline (sender, reminder text). Private: shown only when previews are allowed for the provider.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Second line (subject). Private as above.</summary>
    public string Preview { get; init; } = string.Empty;

    /// <summary>What shows instead of private text ("New email", "Reminder").</summary>
    public required string Redacted { get; init; }

    public string Glyph { get; init; } = "";

    /// <summary>When the event happened at the source (received time, due time).</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Actions the provider supports for this notification, beyond dismiss.</summary>
    public IReadOnlyList<ActivityAction> Actions { get; init; } = [];
}

/// <summary>Per-provider settings (D-F03). The defaults are conservative: previews off, normal priority and up.</summary>
public sealed record ProviderPolicy
{
    public static ProviderPolicy Default { get; } = new();

    public bool Enabled { get; init; } = true;

    /// <summary>Show titles and previews. Off by default (private content, SECURITY_AND_PRIVACY_DESIGN §2).</summary>
    public bool ShowPreview { get; init; }

    /// <summary>Notifications below this priority are not shown.</summary>
    public AlertPriority MinPriority { get; init; } = AlertPriority.Low;

    /// <summary>How long the arrival pill stays.</summary>
    public TimeSpan AlertDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long the notification stays in the island (then it expires).</summary>
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(15);

    public bool AllowedOverFullscreen { get; init; }

    public bool AllowedInQuietMode { get; init; }

    /// <summary>At most this many arrivals per minute; more are summarised (a burst never floods the island).</summary>
    public int MaxPerMinute { get; init; } = 6;
}

/// <summary>What happened to a submitted notification.</summary>
public enum SubmitResult
{
    /// <summary>Shown: an activity and an arrival alert were produced.</summary>
    Shown,

    /// <summary>Accepted into the island without an arrival alert (rate-limited burst).</summary>
    Summarised,

    /// <summary>Already seen (same provider and source ID).</summary>
    Duplicate,

    /// <summary>The provider is turned off or unknown.</summary>
    Disabled,

    /// <summary>Below the provider's minimum priority.</summary>
    Filtered,
}

/// <summary>What the island should do for one submission.</summary>
public readonly record struct NotificationOutcome(SubmitResult Result, IslandActivity? Activity, IslandAlert? Alert);

/// <summary>Result of an action, as reported by the provider. A rejected action is never shown as done.</summary>
public enum ActionOutcome
{
    Done,
    Rejected,
    Unsupported,
    Gone,
}

/// <summary>
/// Provider-neutral notification ingestion (D-F01): deduplication, per-provider enable/preview/priority policy, rate
/// limiting with a summary, expiry, dismissal and action routing. Produces island activities and alerts; drawing,
/// quiet mode and full screen stay with the activity board and the controller. Single-threaded (UI thread).
/// </summary>
public sealed class NotificationCenter(TimeProvider? time = null)
{
    public const string ActivityPrefix = "note:";
    public const string DismissAction = "dismiss";
    public const int MaxRemembered = 512;
    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, ProviderPolicy> _policies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, string, ActionOutcome>> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset At, long Order)> _seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HaloNotification> _live = new(StringComparer.Ordinal);
    private long _order;

    /// <summary>Registers (or re-registers) a provider with its policy and its action handler (sourceId, actionId).</summary>
    public void Register(string providerId, ProviderPolicy policy, Func<string, string, ActionOutcome>? actions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        _policies[providerId] = policy ?? throw new ArgumentNullException(nameof(policy));
        if (actions is not null)
        {
            _handlers[providerId] = actions;
        }
    }

    public ProviderPolicy? PolicyOf(string providerId) => _policies.GetValueOrDefault(providerId);

    /// <summary>Notifications currently held (not dismissed or expired).</summary>
    public int LiveCount => _live.Count;

    /// <summary>Activity IDs of the notifications currently held.</summary>
    public IReadOnlyList<string> LiveActivityIds => _live.Keys.Select(k => ActivityPrefix + k).ToList();

    /// <summary>
    /// Ingests a notification. The same provider and source ID is accepted once (a push and a later poll of the same
    /// email do not show twice). The activity is private unless the provider's previews are allowed.
    /// </summary>
    public NotificationOutcome Submit(HaloNotification n)
    {
        ArgumentNullException.ThrowIfNull(n);
        if (!_policies.TryGetValue(n.ProviderId, out var policy) || !policy.Enabled)
        {
            return new NotificationOutcome(SubmitResult.Disabled, null, null);
        }

        var now = _time.GetUtcNow();
        Prune(now);
        var key = Key(n);
        if (_seen.ContainsKey(key))
        {
            return new NotificationOutcome(SubmitResult.Duplicate, null, null);
        }

        if (n.Priority < policy.MinPriority)
        {
            _seen[key] = (now, ++_order); // filtered is still "seen": raising the threshold later does not replay it
            return new NotificationOutcome(SubmitResult.Filtered, null, null);
        }

        _seen[key] = (now, ++_order);
        _live[key] = n;
        var activity = ToActivity(n, policy, now);
        var recent = _recent.TryGetValue(n.ProviderId, out var q) ? q : _recent[n.ProviderId] = new Queue<DateTimeOffset>();
        while (recent.Count > 0 && now - recent.Peek() >= TimeSpan.FromMinutes(1))
        {
            _ = recent.Dequeue();
        }

        if (recent.Count >= policy.MaxPerMinute)
        {
            return new NotificationOutcome(SubmitResult.Summarised, activity, null);
        }

        recent.Enqueue(now);
        var text = policy.ShowPreview && n.Title.Length > 0 ? n.Title : n.Redacted;
        var alert = new IslandAlert(
            "note." + n.ProviderId,
            AlertKind.Notification,
            n.Priority,
            new AlertContent(n.Glyph, text) { Accessible = policy.ShowPreview ? Join(n.Title, n.Preview, n.Redacted) : n.Redacted },
            policy.AlertDuration,
            TimeSpan.FromSeconds(30),
            policy.AllowedOverFullscreen)
        {
            AllowedInQuietMode = policy.AllowedInQuietMode,
        };
        return new NotificationOutcome(SubmitResult.Shown, activity, alert);
    }

    /// <summary>Removes a notification (user dismissal or the source withdrew it). Returns its activity ID, or null.</summary>
    public string? Dismiss(string providerId, string sourceId)
    {
        var key = providerId + "/" + sourceId;
        return _live.Remove(key) ? ActivityPrefix + key : null;
    }

    /// <summary>
    /// Runs an action on a notification activity. Dismiss is handled here; anything else goes to the provider, whose
    /// answer is returned as is (no simulated success). Unknown activities report <see cref="ActionOutcome.Gone"/>.
    /// </summary>
    public ActionOutcome RunAction(string activityId, string actionId)
    {
        if (!activityId.StartsWith(ActivityPrefix, StringComparison.Ordinal) || !_live.TryGetValue(activityId[ActivityPrefix.Length..], out var n))
        {
            return ActionOutcome.Gone;
        }

        if (string.Equals(actionId, DismissAction, StringComparison.Ordinal))
        {
            _ = _live.Remove(Key(n));
            return ActionOutcome.Done;
        }

        if (!n.Actions.Any(a => string.Equals(a.Id, actionId, StringComparison.Ordinal)) || !_handlers.TryGetValue(n.ProviderId, out var handler))
        {
            return ActionOutcome.Unsupported;
        }

        var outcome = handler(n.SourceId, actionId);
        if (outcome is ActionOutcome.Done or ActionOutcome.Gone)
        {
            _ = _live.Remove(Key(n));
        }

        return outcome;
    }

    /// <summary>Forgets expired notifications (their activities expire on the board at the same time).</summary>
    public void Expire(IEnumerable<string> expiredActivityIds)
    {
        ArgumentNullException.ThrowIfNull(expiredActivityIds);
        foreach (var id in expiredActivityIds.Where(i => i.StartsWith(ActivityPrefix, StringComparison.Ordinal)))
        {
            _ = _live.Remove(id[ActivityPrefix.Length..]);
        }
    }

    /// <summary>Drops a provider's live notifications (provider turned off or disconnected). Returns their activity IDs.</summary>
    public IReadOnlyList<string> DropProvider(string providerId)
    {
        var keys = _live.Where(kv => string.Equals(kv.Value.ProviderId, providerId, StringComparison.Ordinal)).Select(kv => kv.Key).ToList();
        foreach (var k in keys)
        {
            _ = _live.Remove(k);
        }

        return keys.Select(k => ActivityPrefix + k).ToList();
    }

    private static string Key(HaloNotification n) => n.ProviderId + "/" + n.SourceId;

    private static string Join(params string[] parts) => string.Join(", ", parts.Where(p => p.Length > 0).Distinct(StringComparer.Ordinal));

    private static IslandActivity ToActivity(HaloNotification n, ProviderPolicy policy, DateTimeOffset now) =>
        new(ActivityPrefix + Key(n), ActivityKind.Notification, n.Priority == AlertPriority.Critical ? AlertPriority.High : AlertPriority.Low)
        {
            Glyph = n.Glyph,
            CompactText = n.Title.Length > 0 ? n.Title : n.Redacted,
            RedactedText = n.Redacted,
            Title = n.Title.Length > 0 ? n.Title : n.Redacted,
            Subtitle = policy.ShowPreview ? n.Preview : string.Empty,
            Privacy = policy.ShowPreview ? ActivityPrivacy.Public : ActivityPrivacy.Private,
            Ongoing = false,
            ExpiresAt = now + policy.Lifetime,
            Actions = [.. n.Actions.Take(ActivityLayout.MaxActions - 1), new ActivityAction(DismissAction, "Dismiss", "")],
            Accessible = policy.ShowPreview ? Join(n.Redacted, n.Title, n.Preview) : n.Redacted,
            AllowedInQuietMode = policy.AllowedInQuietMode,
            AllowedWhenSuppressed = policy.AllowedOverFullscreen,
        };

    private void Prune(DateTimeOffset now)
    {
        if (_seen.Count < MaxRemembered)
        {
            return;
        }

        foreach (var old in _seen.Where(kv => now - kv.Value.At > RememberFor).Select(kv => kv.Key).ToList())
        {
            _ = _seen.Remove(old);
        }

        // Still full: forget the oldest half. A forgotten source ID older than two days can only reappear if the
        // provider re-sends it, which providers do not do for items that old.
        if (_seen.Count >= MaxRemembered)
        {
            // Oldest by arrival order (timestamps can be equal), never a live notification.
            foreach (var old in _seen.Where(kv => !_live.ContainsKey(kv.Key)).OrderBy(kv => kv.Value.Order).Take(MaxRemembered / 2).Select(kv => kv.Key).ToList())
            {
                _ = _seen.Remove(old);
            }
        }
    }
}
