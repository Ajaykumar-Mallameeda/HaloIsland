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

    /// <summary>Set by the board when the activity first appears.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set by the board on every real change.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The compact text to draw given the user's preview setting.</summary>
    public string CompactFor(bool showPrivate) => Privacy == ActivityPrivacy.Private && !showPrivate ? RedactedText : CompactText;

    /// <summary>The activity with the board's timestamps cleared, for "did anything change" comparisons.</summary>
    internal IslandActivity Content => this with { CreatedAt = default, UpdatedAt = default };
}

/// <summary>What an operation on the board changed.</summary>
public readonly record struct ActivityUpdate(bool Changed, bool PrimaryChanged, IslandActivity? Primary)
{
    public static ActivityUpdate None(IslandActivity? primary) => new(false, false, primary);
}

/// <summary>
/// The set of live activities and the single primary one (B-F03). Rules:
/// <list type="bullet">
/// <item>Bounded: at most <see cref="MaxActivities"/>; a new one beyond that replaces the lowest-priority, oldest one.</item>
/// <item>Coalescing: an update equal to what is held changes nothing (no reordering, no redraw).</item>
/// <item>Primary: the user's explicit selection while it exists and is eligible; otherwise the highest priority, then the
/// most recently changed. Eligible means not filtered by quiet mode or full-screen suppression.</item>
/// <item>Expiry: activities past <see cref="IslandActivity.ExpiresAt"/> are removed by <see cref="RemoveExpired"/>; the owner
/// arms one timer for <see cref="NextExpiry"/>.</item>
/// </list>
/// Single-threaded (the UI thread). Time comes from the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class ActivityBoard(TimeProvider? time = null)
{
    public const int MaxActivities = 8;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<IslandActivity> _items = [];
    private string? _selectedId;
    private bool _quiet;
    private bool _suppressed;

    /// <summary>Every activity, in display order (primary first among eligible ones).</summary>
    public IReadOnlyList<IslandActivity> All => Ordered().ToList();

    public int Count => _items.Count;

    /// <summary>The activity the compact pill and the first expanded page show, or null.</summary>
    public IslandActivity? Primary => Ordered().FirstOrDefault(Eligible);

    /// <summary>Eligible activities that are not primary (shown as a count in the pill and as further pages).</summary>
    public int OthersCount => Math.Max(0, _items.Count(Eligible) - 1);

    /// <summary>Earliest expiry among held activities, or null.</summary>
    public DateTimeOffset? NextExpiry => _items.Where(a => a.ExpiresAt is not null).Select(a => a.ExpiresAt).Min();

    /// <summary>Quiet mode (Do Not Disturb): only activities allowed in quiet mode can be primary.</summary>
    public bool Quiet => _quiet;

    public IslandActivity? Find(string id) => _items.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>Adds or replaces an activity by ID.</summary>
    public ActivityUpdate Upsert(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var before = Primary;
        var now = _time.GetUtcNow();
        var index = _items.FindIndex(a => string.Equals(a.Id, activity.Id, StringComparison.Ordinal));
        if (index >= 0)
        {
            var held = _items[index];
            if (held.Content == activity.Content)
            {
                return ActivityUpdate.None(before);
            }

            _items[index] = activity with { CreatedAt = held.CreatedAt, UpdatedAt = now };
        }
        else
        {
            if (_items.Count >= MaxActivities)
            {
                var drop = _items.OrderBy(a => a.Priority).ThenBy(a => a.UpdatedAt).First();
                _ = _items.Remove(drop);
            }

            _items.Add(activity with { CreatedAt = now, UpdatedAt = now });
        }

        return Result(before);
    }

    public ActivityUpdate Remove(string id)
    {
        var before = Primary;
        var removed = _items.RemoveAll(a => string.Equals(a.Id, id, StringComparison.Ordinal)) > 0;
        if (string.Equals(_selectedId, id, StringComparison.Ordinal))
        {
            _selectedId = null;
        }

        return removed ? Result(before) : ActivityUpdate.None(before);
    }

    /// <summary>Removes every activity whose expiry has passed.</summary>
    public ActivityUpdate RemoveExpired()
    {
        var before = Primary;
        var now = _time.GetUtcNow();
        var removed = _items.RemoveAll(a => a.ExpiresAt is { } e && e <= now) > 0;
        if (_selectedId is not null && Find(_selectedId) is null)
        {
            _selectedId = null;
        }

        return removed ? Result(before) : ActivityUpdate.None(before);
    }

    /// <summary>The user chose an activity (explicit switching). Ignored for an unknown or ineligible ID.</summary>
    public ActivityUpdate Select(string id)
    {
        var before = Primary;
        if (Find(id) is not { } a || !Eligible(a))
        {
            return ActivityUpdate.None(before);
        }

        _selectedId = id;
        return Result(before);
    }

    /// <summary>Selects the next eligible activity after the primary one, wrapping around.</summary>
    public ActivityUpdate SelectNext()
    {
        var eligible = Ordered().Where(Eligible).ToList();
        if (eligible.Count < 2)
        {
            return ActivityUpdate.None(Primary);
        }

        // Order without the selection, so stepping visits every activity in a stable order.
        var stable = _items.Where(Eligible).OrderByDescending(a => a.Priority).ThenBy(a => a.CreatedAt).ToList();
        var current = stable.FindIndex(a => ReferenceEquals(a, Primary) || a.Id == Primary?.Id);
        return Select(stable[(current + 1) % stable.Count].Id);
    }

    /// <summary>Quiet mode on or off. Nothing is removed: filtered activities become eligible again when it ends.</summary>
    public ActivityUpdate SetQuiet(bool quiet)
    {
        var before = Primary;
        _quiet = quiet;
        return Result(before, contentChanged: false);
    }

    /// <summary>Full-screen suppression on or off (only activities allowed over full screen stay eligible).</summary>
    public ActivityUpdate SetSuppressed(bool suppressed)
    {
        var before = Primary;
        _suppressed = suppressed;
        return Result(before, contentChanged: false);
    }

    public void Clear()
    {
        _items.Clear();
        _selectedId = null;
    }

    private bool Eligible(IslandActivity a) => (!_quiet || a.AllowedInQuietMode) && (!_suppressed || a.AllowedWhenSuppressed);

    private IEnumerable<IslandActivity> Ordered()
    {
        var selected = _selectedId is null ? null : Find(_selectedId);
        return _items
            .OrderByDescending(a => selected is not null && ReferenceEquals(a, selected) && Eligible(a))
            .ThenByDescending(a => a.Priority)
            .ThenByDescending(a => a.UpdatedAt);
    }

    private ActivityUpdate Result(IslandActivity? before, bool contentChanged = true)
    {
        var after = Primary;
        var primaryChanged = !string.Equals(before?.Id, after?.Id, StringComparison.Ordinal);
        return new ActivityUpdate(contentChanged || primaryChanged, primaryChanged, after);
    }
}
