using Halo.Core.Island;

namespace Halo.Core.Activities;

/// <summary>What produced an activity; also the diagnostics token.</summary>
public enum ActivityKind
{
    Timer,
    Focus,
    Reminder,
    Task,
    Notification,
    Calendar,
    System,

    /// <summary>A media session that is playing or paused (Live Activity Engine 2.0).</summary>
    Media,

    /// <summary>Charging (Live Activity Engine 2.0).</summary>
    Power,

    /// <summary>Connectivity lost (Live Activity Engine 2.0).</summary>
    Network,
}

/// <summary>Lifecycle of an activity that is still on the board.</summary>
public enum ActivityLifecycle
{
    Active,

    /// <summary>Suspended by its provider or the user (a paused timer or media); ranks below active ones of equal priority.</summary>
    Paused,
}

/// <summary>Why an activity left the board (bounded history, diagnostics only).</summary>
public enum ActivityEnd
{
    /// <summary>The provider ended it (the task finished, the session closed).</summary>
    Ended,

    /// <summary>Its expiry passed.</summary>
    Expired,

    /// <summary>The user dismissed it.</summary>
    Dismissed,

    /// <summary>The board was full and it was the least important.</summary>
    Evicted,

    /// <summary>Its provider was turned off.</summary>
    ProviderStopped,
}

/// <summary>Whether the activity's text may appear in the compact pill and to anyone looking at the screen.</summary>
public enum ActivityPrivacy
{
    /// <summary>The text can be shown (a timer, a media title the user already sees elsewhere).</summary>
    Public,

    /// <summary>The text is personal (a task, a message preview). Unless previews are allowed, only the redacted text shows.</summary>
    Private,
}

/// <summary>A command an activity offers, routed back to its source by <see cref="Id"/>. Never executes text from content.</summary>
/// <param name="Id">Fixed token, for example "pause", "dismiss", "snooze".</param>
/// <param name="Label">Button text and accessible name.</param>
/// <param name="Glyph">Icon-font glyph for the button.</param>
public sealed record ActivityAction(string Id, string Label, string Glyph);

/// <summary>
/// One live activity (ADR-001 §6.2): something with a lifetime that the island can show compact and expanded — a
/// timer, a focus session, a due reminder, a notification. Sources describe what to show; the board decides which one
/// is primary; the window draws it. Records compare by value, so an update that changes nothing is detected.
/// </summary>
/// <param name="Id">Stable identity: an update with the same ID replaces the activity in place (coalescing).</param>
/// <param name="Kind">Source.</param>
/// <param name="Priority">Arbitration priority.</param>
public sealed record IslandActivity(string Id, ActivityKind Kind, AlertPriority Priority)
{
    /// <summary>Icon-font glyph.</summary>
    public string Glyph { get; init; } = string.Empty;

    /// <summary>What the activity means for colour; neutral = the usual tone of its kind (see HaloDesign.ToneOf).</summary>
    public Theming.Tone Tone { get; init; }

    /// <summary>Short compact text ("04:59", "3 tasks"). Never logged.</summary>
    public string CompactText { get; init; } = string.Empty;

    /// <summary>Compact text used instead of <see cref="CompactText"/> while the content is private and previews are hidden.</summary>
    public string RedactedText { get; init; } = string.Empty;

    /// <summary>Expanded headline ("04:59").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Expanded secondary line ("Timer · 5 min").</summary>
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>Progress from 0 to 1 (bar in the pill and the card), or null.</summary>
    public double? Level { get; init; }

    /// <summary>Ongoing activities last until their source ends them; transient ones need <see cref="ExpiresAt"/>.</summary>
    public bool Ongoing { get; init; } = true;

    /// <summary>When the activity removes itself (a finished timer after a while, a notification), or null.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public IReadOnlyList<ActivityAction> Actions { get; init; } = [];

    public ActivityPrivacy Privacy { get; init; } = ActivityPrivacy.Public;

    /// <summary>What a screen reader hears for this activity. Never logged.</summary>
    public string Accessible { get; init; } = string.Empty;

    /// <summary>May be primary while a full-screen app hides the island (shown with the island's override only otherwise).</summary>
    public bool AllowedWhenSuppressed { get; init; }

    /// <summary>Stays eligible as primary in quiet mode (user-started timers and focus sessions).</summary>
    public bool AllowedInQuietMode { get; init; }

    /// <summary>Restrained emphasis (a countdown's last seconds, a completion waiting to be dismissed).</summary>
    public bool Emphasis { get; init; }

    /// <summary>
    /// Which feature publishes the activity (Live Activity Engine 2.0): "timer", "media", "power"… Used to remove every
    /// activity of a provider that was turned off, and as a diagnostics token. Empty = the kind's token.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Running or paused. Ended activities leave the board (see <see cref="ActivityBoard.End"/>).</summary>
    public ActivityLifecycle Lifecycle { get; init; } = ActivityLifecycle.Active;

    /// <summary>
    /// Apple's relevance score: among activities of the same priority the higher score is shown; equal scores keep the
    /// one that started first. Relative values only (0, 10, 20…).
    /// </summary>
    public int Relevance { get; init; }

    /// <summary>
    /// The activity asks for the compact pill (and keeps the island out of the thin-line state). False = background
    /// only: it stays on the board and in the open island but never brings the pill up by itself (paused media).
    /// </summary>
    public bool ShowsCompact { get; init; } = true;

    /// <summary>
    /// Operational urgency (network lost): shown ahead of the user's own selection, though below critical activities.
    /// </summary>
    public bool Urgent { get; init; }

    /// <summary>The user may dismiss it; a dismissed activity stays hidden until its provider ends it.</summary>
    public bool Dismissible { get; init; } = true;

    /// <summary>Set by the board when the activity first appears.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set by the board on every real change.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The compact text to draw given the user's preview setting.</summary>
    public string CompactFor(bool showPrivate) => Privacy == ActivityPrivacy.Private && !showPrivate ? RedactedText : CompactText;

    /// <summary>The activity with the board's timestamps cleared, for "did anything change" comparisons.</summary>
    internal IslandActivity Content => this with { CreatedAt = default, UpdatedAt = default };
}

