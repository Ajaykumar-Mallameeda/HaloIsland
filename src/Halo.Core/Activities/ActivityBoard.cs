using Halo.Core.Island;

namespace Halo.Core.Activities;

/// <summary>What an operation on the board changed.</summary>
public readonly record struct ActivityUpdate(bool Changed, bool PrimaryChanged, IslandActivity? Primary)
{
    public static ActivityUpdate None(IslandActivity? primary) => new(false, false, primary);
}

/// <summary>One entry of the board's bounded history (diagnostics only: IDs and tokens, never content).</summary>
public readonly record struct ActivityHistoryEntry(string Id, string Provider, ActivityEnd Reason, DateTimeOffset At);

/// <summary>
/// Live Activity Engine 2.0: every live activity, its lifecycle, and the deterministic choice of what the compact
/// island shows (the primary activity, attached to the island) and what waits beside it (the secondary activity, shown
/// detached as Apple's minimal presentation). See docs/LIVE_ACTIVITY_ENGINE_2_DESIGN.md §4.
/// <para>Ranking, compared in order (the first difference decides):</para>
/// <list type="number">
/// <item>critical priority;</item>
/// <item>operational urgency (<see cref="IslandActivity.Urgent"/>, for example network lost);</item>
/// <item>the user's explicit selection, while that activity exists and is eligible;</item>
/// <item>priority (high, normal, low);</item>
/// <item>active before paused;</item>
/// <item>relevance score, higher first (Apple's <c>relevanceScore</c>);</item>
/// <item>start time, earlier first (Apple: with equal scores the first activity started stays in the island);</item>
/// <item>ID, ordinal (a total order, so equal inputs always give the same answer).</item>
/// </list>
/// <para>
/// Updates never reorder by themselves: the ranking does not look at when an activity last changed, so a countdown
/// tick or a media progress update cannot take the island from another activity. Optional rotation (off by default)
/// steps through activities that tie on the first four keys, once per call to <see cref="Rotate"/>.
/// </para>
/// <para>
/// Lifecycle: <see cref="Upsert"/> starts or updates (same ID = same activity, timestamps kept; equal content changes
/// nothing). <see cref="End"/> removes it. <see cref="Dismiss"/> removes it and remembers the ID, so the provider's next
/// identical publication does not bring it back; the memory clears when the provider ends the activity. Expiry works
/// the same way, so a finished activity does not reappear unless its provider ends it and starts a new one.
/// </para>
/// Single-threaded: only the UI thread calls it (providers post their readings to the window first). Time comes from
/// the injected <see cref="TimeProvider"/>.
/// </summary>
public sealed class ActivityBoard(TimeProvider? time = null)
{
    public const int MaxActivities = 8;
    public const int MaxHistory = 16;

    /// <summary>Remembered dismissed or expired IDs; bounded so a misbehaving provider cannot grow it.</summary>
    public const int MaxRetired = 32;

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<IslandActivity> _items = [];
    private readonly List<string> _retired = [];
    private readonly Queue<ActivityHistoryEntry> _history = new();
    private string? _selectedId;
    private bool _quiet;
    private bool _suppressed;
    private int _rotation;

    /// <summary>Every activity, ranked (eligible or not).</summary>
    public IReadOnlyList<IslandActivity> All => Ranked().ToList();

    public int Count => _items.Count;

    /// <summary>The activity attached to the island in the compact presentation, or null (the island may rest as a line).</summary>
    public IslandActivity? Primary => Compact().FirstOrDefault();

    /// <summary>The next activity asking for the compact island, shown detached beside it, or null.</summary>
    public IslandActivity? Secondary => Compact().Skip(1).FirstOrDefault();

    /// <summary>Eligible activities that are not primary (shown as a count and as further pages).</summary>
    public int OthersCount => Math.Max(0, _items.Count(Eligible) - (Primary is null ? 0 : 1));

    /// <summary>Earliest expiry among held activities, or null.</summary>
    public DateTimeOffset? NextExpiry => _items.Where(a => a.ExpiresAt is not null).Select(a => a.ExpiresAt).Min();

    /// <summary>Quiet mode (Do Not Disturb): only activities allowed in quiet mode can be primary.</summary>
    public bool Quiet => _quiet;

    /// <summary>Rotation through tied activities (off by default; the window calls <see cref="Rotate"/> on a timer).</summary>
    public bool RotationEnabled { get; set; }

    /// <summary>True when rotation is on and more than one activity ties for the island.</summary>
    public bool CanRotate => RotationEnabled && _selectedId is null && TopGroup().Count > 1;

    /// <summary>The ID the user selected, or null.</summary>
    public string? SelectedId => _selectedId;

    /// <summary>Most recent departures, oldest first (at most <see cref="MaxHistory"/>).</summary>
    public IReadOnlyCollection<ActivityHistoryEntry> History => _history;

    public IslandActivity? Find(string id) => _items.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>The first eligible activity (any presence) that matches, in rank order.</summary>
    public IslandActivity? First(Func<IslandActivity, bool> match) => Ranked().Where(Eligible).FirstOrDefault(match);

    /// <summary>True when the ID was dismissed or expired and its provider has not ended it yet.</summary>
    public bool IsRetired(string id) => _retired.Contains(id, StringComparer.Ordinal);

    /// <summary>Starts or updates an activity by ID. Ignored while the ID is retired (dismissed or expired).</summary>
    public ActivityUpdate Upsert(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentException.ThrowIfNullOrEmpty(activity.Id);
        var before = Primary;
        if (IsRetired(activity.Id))
        {
            return ActivityUpdate.None(before);
        }

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
                // The least important goes: lowest priority, then the oldest start, then the ID.
                var drop = _items.OrderBy(a => a.Priority).ThenBy(a => a.CreatedAt).ThenBy(a => a.Id, StringComparer.Ordinal).First();
                _ = _items.Remove(drop);
                Record(drop, ActivityEnd.Evicted, now);
            }

            _items.Add(activity with { CreatedAt = now, UpdatedAt = now });
        }

        return Result(before);
    }

    /// <summary>The provider ended the activity (or never had one): it leaves the board and its ID may be used again.</summary>
    public ActivityUpdate End(string id)
    {
        _ = _retired.RemoveAll(r => string.Equals(r, id, StringComparison.Ordinal));
        return Drop(id, ActivityEnd.Ended, retire: false);
    }

    /// <summary>Same as <see cref="End"/> (kept for providers that think in "remove").</summary>
    public ActivityUpdate Remove(string id) => End(id);

    /// <summary>
    /// The user dismissed the activity: it leaves the island and stays hidden while its provider keeps publishing the
    /// same ID. Ignored for activities that are not dismissible (the camera privacy indicator).
    /// </summary>
    public ActivityUpdate Dismiss(string id)
    {
        if (Find(id) is not { Dismissible: true })
        {
            return ActivityUpdate.None(Primary);
        }

        return Drop(id, ActivityEnd.Dismissed, retire: true);
    }

    /// <summary>Removes every activity whose expiry has passed; their IDs stay retired until their providers end them.</summary>
    public ActivityUpdate RemoveExpired()
    {
        var before = Primary;
        var now = _time.GetUtcNow();
        var expired = _items.Where(a => a.ExpiresAt is { } e && e <= now).ToList();
        foreach (var a in expired)
        {
            _ = _items.Remove(a);
            Retire(a.Id);
            Record(a, ActivityEnd.Expired, now);
        }

        ClearStaleSelection();
        return expired.Count > 0 ? Result(before) : ActivityUpdate.None(before);
    }

    /// <summary>Removes every activity of a provider that was turned off (and forgets its retired IDs).</summary>
    public ActivityUpdate StopProvider(string provider)
    {
        var before = Primary;
        var now = _time.GetUtcNow();
        var gone = _items.Where(a => string.Equals(ProviderOf(a), provider, StringComparison.Ordinal)).ToList();
        foreach (var a in gone)
        {
            _ = _items.Remove(a);
            _ = _retired.RemoveAll(r => string.Equals(r, a.Id, StringComparison.Ordinal));
            Record(a, ActivityEnd.ProviderStopped, now);
        }

        ClearStaleSelection();
        return gone.Count > 0 ? Result(before) : ActivityUpdate.None(before);
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
        return Result(before, contentChanged: false);
    }

    /// <summary>Returns to automatic selection.</summary>
    public ActivityUpdate ClearSelection()
    {
        var before = Primary;
        _selectedId = null;
        return Result(before, contentChanged: false);
    }

    /// <summary>Selects the next eligible activity after the primary one, wrapping around, in a stable order.</summary>
    public ActivityUpdate SelectNext()
    {
        // Order without the selection, so stepping visits every activity in the same order every time.
        var stable = _items.Where(Eligible).OrderBy(a => a, new RankComparer(null)).ToList();
        if (stable.Count < 2)
        {
            return ActivityUpdate.None(Primary);
        }

        var current = stable.FindIndex(a => a.Id == Primary?.Id);
        return Select(stable[(current + 1) % stable.Count].Id);
    }

    /// <summary>One rotation step among tied activities (only when <see cref="CanRotate"/>).</summary>
    public ActivityUpdate Rotate()
    {
        var before = Primary;
        if (!CanRotate)
        {
            return ActivityUpdate.None(before);
        }

        _rotation++;
        return Result(before, contentChanged: false);
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
        _retired.Clear();
        _selectedId = null;
        _rotation = 0;
    }

    /// <summary>The provider token of an activity (its <see cref="IslandActivity.Provider"/>, else its kind).</summary>
    public static string ProviderOf(IslandActivity activity) =>
        activity.Provider.Length > 0 ? activity.Provider : activity.Kind.ToString().ToLowerInvariant();

    private bool Eligible(IslandActivity a) => (!_quiet || a.AllowedInQuietMode || a.Priority == AlertPriority.Critical) && (!_suppressed || a.AllowedWhenSuppressed);

    private IEnumerable<IslandActivity> Ranked() => _items.OrderBy(a => a, new RankComparer(SelectedEligible()));

    /// <summary>Eligible activities asking for the compact island, ranked, with the rotation applied to the top group.</summary>
    private List<IslandActivity> Compact()
    {
        var list = Ranked().Where(a => Eligible(a) && a.ShowsCompact).ToList();
        var group = TopGroup(list);
        if (RotationEnabled && _selectedId is null && group.Count > 1)
        {
            var lead = group[_rotation % group.Count];
            _ = list.Remove(lead);
            list.Insert(0, lead);
        }

        return list;
    }

    private List<IslandActivity> TopGroup() => TopGroup(Ranked().Where(a => Eligible(a) && a.ShowsCompact).ToList());

    /// <summary>The leading activities that tie on criticality, urgency and priority (rotation candidates).</summary>
    private static List<IslandActivity> TopGroup(List<IslandActivity> ranked)
    {
        if (ranked.Count == 0)
        {
            return [];
        }

        var head = ranked[0];
        return ranked.TakeWhile(a => a.Priority == head.Priority && a.Urgent == head.Urgent && a.Lifecycle == head.Lifecycle).ToList();
    }

    private string? SelectedEligible() => _selectedId is not null && Find(_selectedId) is { } s && Eligible(s) ? _selectedId : null;

    private ActivityUpdate Drop(string id, ActivityEnd reason, bool retire)
    {
        var before = Primary;
        var held = Find(id);
        if (held is null)
        {
            if (retire)
            {
                Retire(id);
            }

            return ActivityUpdate.None(before);
        }

        _ = _items.Remove(held);
        if (retire)
        {
            Retire(id);
        }

        Record(held, reason, _time.GetUtcNow());
        ClearStaleSelection();
        return Result(before);
    }

    private void Retire(string id)
    {
        if (IsRetired(id))
        {
            return;
        }

        if (_retired.Count >= MaxRetired)
        {
            _retired.RemoveAt(0);
        }

        _retired.Add(id);
    }

    private void Record(IslandActivity a, ActivityEnd reason, DateTimeOffset at)
    {
        if (_history.Count >= MaxHistory)
        {
            _ = _history.Dequeue();
        }

        _history.Enqueue(new ActivityHistoryEntry(a.Id, ProviderOf(a), reason, at));
    }

    private void ClearStaleSelection()
    {
        if (_selectedId is not null && Find(_selectedId) is null)
        {
            _selectedId = null;
        }
    }

    private ActivityUpdate Result(IslandActivity? before, bool contentChanged = true)
    {
        var after = Primary;
        var primaryChanged = !string.Equals(before?.Id, after?.Id, StringComparison.Ordinal);
        return new ActivityUpdate(contentChanged || primaryChanged, primaryChanged, after);
    }

    /// <summary>The ranking documented on the class: a total order.</summary>
    private sealed class RankComparer(string? selectedId) : IComparer<IslandActivity>
    {
        public int Compare(IslandActivity? x, IslandActivity? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);
            int c;
            if ((c = Desc(x.Priority == AlertPriority.Critical, y.Priority == AlertPriority.Critical)) != 0
                || (c = Desc(x.Urgent, y.Urgent)) != 0
                || (c = Desc(Selected(x), Selected(y))) != 0
                || (c = y.Priority.CompareTo(x.Priority)) != 0
                || (c = Desc(x.Lifecycle == ActivityLifecycle.Active, y.Lifecycle == ActivityLifecycle.Active)) != 0
                || (c = y.Relevance.CompareTo(x.Relevance)) != 0
                || (c = x.CreatedAt.CompareTo(y.CreatedAt)) != 0)
            {
                return c;
            }

            return string.CompareOrdinal(x.Id, y.Id);
        }

        private static int Desc(bool x, bool y) => y.CompareTo(x);

        private bool Selected(IslandActivity a) => selectedId is not null && string.Equals(a.Id, selectedId, StringComparison.Ordinal);
    }
}
