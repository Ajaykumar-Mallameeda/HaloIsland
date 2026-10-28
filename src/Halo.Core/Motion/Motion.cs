using Halo.Core.Island;

namespace Halo.Core.Motion;

/// <summary>The island's motion vocabulary (MOTION_SYSTEM.md §1). No feature adds its own kind of movement.</summary>
public enum MotionKind
{
    /// <summary>Shape size, position and corner radius between presentations.</summary>
    Morph,

    /// <summary>Outgoing content.</summary>
    FadeOut,

    /// <summary>Incoming content after a morph.</summary>
    FadeIn,

    /// <summary>Expanded content drops a few DIP into place.</summary>
    Settle,

    /// <summary>The same presentation with new content (a new alert replaces another).</summary>
    Swap,

    /// <summary>A bar or ring value changes.</summary>
    Level,

    /// <summary>One gentle brightness step per second in a countdown's last seconds.</summary>
    Emphasis,

    /// <summary>A completion or a new activity: the pill grows slightly and returns once.</summary>
    Arrival,
}

/// <summary>Independent animation channels. At most one plan runs per channel; a new one replaces it.</summary>
public enum MotionChannel
{
    Shape,
    Content,
    Level,
    Pulse,
}

/// <summary>Duration and start delay of one motion. <see cref="IsInstant"/> motions jump to the end value.</summary>
public readonly record struct MotionSpec(TimeSpan Duration, TimeSpan Delay)
{
    public static MotionSpec Instant { get; } = new(TimeSpan.Zero, TimeSpan.Zero);

    public bool IsInstant => Duration == TimeSpan.Zero;

    public TimeSpan End => Delay + Duration;

    public static MotionSpec Ms(int duration, int delay = 0) => new(TimeSpan.FromMilliseconds(duration), TimeSpan.FromMilliseconds(delay));
}

/// <summary>One animated step of a plan.</summary>
public readonly record struct MotionStep(MotionChannel Channel, MotionKind Kind, MotionSpec Spec);

/// <summary>Durations from MOTION_SYSTEM.md, with the reduced-motion alternatives.</summary>
public static class MotionVocabulary
{
    /// <summary>Apple's guidance caps Live Activity animations at 2 s; Halo's longest sequence stays far below it.</summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(2);

    public static MotionSpec For(MotionKind kind, bool reducedMotion, bool expanding = false) => (kind, reducedMotion) switch
    {
        (MotionKind.Morph, false) => MotionSpec.Ms(350),
        (MotionKind.Morph, true) => MotionSpec.Instant,
        (MotionKind.FadeOut, false) => MotionSpec.Ms(110),
        (MotionKind.FadeOut, true) => MotionSpec.Ms(150),
        (MotionKind.FadeIn, false) => MotionSpec.Ms(200, expanding ? 150 : 170),
        (MotionKind.FadeIn, true) => MotionSpec.Ms(150),
        (MotionKind.Settle, false) => MotionSpec.Ms(320, 60),
        (MotionKind.Swap, false) => MotionSpec.Ms(160, 90),
        (MotionKind.Level, false) => MotionSpec.Ms(240),
        (MotionKind.Emphasis, false) => MotionSpec.Ms(400),
        (MotionKind.Arrival, false) => MotionSpec.Ms(260),
        _ => MotionSpec.Instant, // Settle, Swap, Level, Emphasis and Arrival are off with reduced motion
    };
}

/// <summary>A finite set of steps started together for one change. Every plan ends by <see cref="Duration"/>.</summary>
public sealed record MotionPlan(int Id, IReadOnlyList<MotionStep> Steps)
{
    public TimeSpan Duration => Steps.Count == 0 ? TimeSpan.Zero : Steps.Max(s => s.Spec.End);

    public bool Has(MotionKind kind) => Steps.Any(s => s.Kind == kind);

    public MotionSpec Spec(MotionKind kind) => Steps.First(s => s.Kind == kind).Spec;
}

/// <summary>
/// Turns state changes into motion plans (pure). The interaction controller owns state: a plan describes how to show
/// a change, never what the state is.
/// </summary>
public static class MotionPlanner
{
    /// <summary>A presentation change (compact ↔ alert ↔ expanded): morph, fade out, fade in, and settle when expanding.</summary>
    public static IReadOnlyList<MotionStep> Transition(IslandPresentation from, IslandPresentation to, bool reducedMotion)
    {
        if (from == to)
        {
            return [];
        }

        var expanding = to == IslandPresentation.Expanded;
        var steps = new List<MotionStep>(4)
        {
            new(MotionChannel.Shape, MotionKind.Morph, MotionVocabulary.For(MotionKind.Morph, reducedMotion)),
            new(MotionChannel.Content, MotionKind.FadeOut, MotionVocabulary.For(MotionKind.FadeOut, reducedMotion)),
            new(MotionChannel.Content, MotionKind.FadeIn, MotionVocabulary.For(MotionKind.FadeIn, reducedMotion, expanding)),
        };
        if (expanding && !reducedMotion)
        {
            steps.Add(new(MotionChannel.Content, MotionKind.Settle, MotionVocabulary.For(MotionKind.Settle, reducedMotion)));
        }

        return steps;
    }

    /// <summary>New content in the same presentation (one alert replacing another): a short swap, or nothing with reduced motion.</summary>
    public static IReadOnlyList<MotionStep> Swap(bool reducedMotion) =>
        reducedMotion ? [] : [new(MotionChannel.Content, MotionKind.Swap, MotionVocabulary.For(MotionKind.Swap, false))];

    /// <summary>A level change (bar or ring). Unchanged values plan nothing (coalescing).</summary>
    public static IReadOnlyList<MotionStep> Level(double? from, double? to, bool reducedMotion)
    {
        if (to is null || from == to)
        {
            return [];
        }

        // From nothing (a bar that just appeared) the value is set directly; motion is for changes the user can follow.
        return from is null || reducedMotion ? [] : [new(MotionChannel.Level, MotionKind.Level, MotionVocabulary.For(MotionKind.Level, false))];
    }

    /// <summary>A completion or a new activity arriving.</summary>
    public static IReadOnlyList<MotionStep> Arrival(bool reducedMotion) =>
        reducedMotion ? [] : [new(MotionChannel.Pulse, MotionKind.Arrival, MotionVocabulary.For(MotionKind.Arrival, false))];

    /// <summary>One emphasis step in a countdown's last seconds (≤ 10 per countdown).</summary>
    public static IReadOnlyList<MotionStep> Emphasis(TimeSpan remaining, bool reducedMotion) =>
        reducedMotion || remaining <= TimeSpan.Zero || remaining > CountdownEmphasis.Window
            ? []
            : [new(MotionChannel.Pulse, MotionKind.Emphasis, MotionVocabulary.For(MotionKind.Emphasis, false))];
}

/// <summary>When a countdown shows restrained emphasis.</summary>
public static class CountdownEmphasis
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Gates completion callbacks so a stale callback can never act on a newer state (MOTION_SYSTEM.md rule 1/2). Each
/// <see cref="Begin"/> replaces the channel's running plan; <see cref="Complete"/> is true only for the plan that is still
/// current. After <see cref="Shutdown"/> nothing begins or completes. Single-threaded (the UI thread).
/// </summary>
public sealed class MotionTracker
{
    private readonly Dictionary<MotionChannel, int> _running = [];
    private int _nextId;

    public bool IsShutDown { get; private set; }

    /// <summary>Plans started and not yet completed or replaced (at most one per channel).</summary>
    public int ActiveCount => _running.Count;

    /// <summary>Starts a plan on its channels. Returns null when there is nothing to animate or after shutdown.</summary>
    public MotionPlan? Begin(IReadOnlyList<MotionStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (IsShutDown || steps.Count == 0)
        {
            return null;
        }

        var plan = new MotionPlan(++_nextId, steps);
        foreach (var channel in steps.Select(s => s.Channel).Distinct())
        {
            _running[channel] = plan.Id;
        }

        return plan;
    }

    /// <summary>A plan's completion callback. True only if the plan is still current on at least one of its channels.</summary>
    public bool Complete(int planId)
    {
        if (IsShutDown)
        {
            return false;
        }

        var channels = _running.Where(kv => kv.Value == planId).Select(kv => kv.Key).ToList();
        foreach (var channel in channels)
        {
            _ = _running.Remove(channel);
        }

        return channels.Count > 0;
    }

    /// <summary>True while the plan is current on any channel.</summary>
    public bool IsCurrent(int planId) => _running.ContainsValue(planId);

    /// <summary>Forgets every running plan (the window was hidden or re-laid out: the end state is applied directly).</summary>
    public void CancelAll() => _running.Clear();

    public void Shutdown()
    {
        IsShutDown = true;
        _running.Clear();
    }
}
