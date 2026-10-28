using Halo.Core.Island;
using Halo.Core.Motion;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>B-F02: the motion vocabulary, planner and tracker (MOTION_SYSTEM.md).</summary>
public class MotionTests
{
    [Fact]
    public void ExpandingMorphsFadesAndSettles()
    {
        var steps = MotionPlanner.Transition(IslandPresentation.Compact, IslandPresentation.Expanded, reducedMotion: false);
        Assert.Equal([MotionKind.Morph, MotionKind.FadeOut, MotionKind.FadeIn, MotionKind.Settle], steps.Select(s => s.Kind));
        var plan = new MotionPlan(1, steps);
        Assert.Equal(TimeSpan.FromMilliseconds(350), plan.Spec(MotionKind.Morph).Duration); // the duration measured for PB-05
        Assert.Equal(TimeSpan.FromMilliseconds(150), plan.Spec(MotionKind.FadeIn).Delay);
        Assert.Equal(TimeSpan.FromMilliseconds(380), plan.Duration);
    }

    [Fact]
    public void CollapsingHasNoSettleAndALaterFadeIn()
    {
        var plan = new MotionPlan(1, MotionPlanner.Transition(IslandPresentation.Expanded, IslandPresentation.Compact, false));
        Assert.False(plan.Has(MotionKind.Settle));
        Assert.Equal(TimeSpan.FromMilliseconds(170), plan.Spec(MotionKind.FadeIn).Delay);
    }

    [Fact]
    public void ReducedMotionIsInstantShapeAndShortFadesOnly()
    {
        var plan = new MotionPlan(1, MotionPlanner.Transition(IslandPresentation.Compact, IslandPresentation.Expanded, reducedMotion: true));
        Assert.True(plan.Spec(MotionKind.Morph).IsInstant);
        Assert.False(plan.Has(MotionKind.Settle));
        Assert.Equal(TimeSpan.Zero, plan.Spec(MotionKind.FadeIn).Delay);
        Assert.True(plan.Duration <= TimeSpan.FromMilliseconds(150));
        Assert.Empty(MotionPlanner.Swap(true));
        Assert.Empty(MotionPlanner.Level(0.2, 0.8, true));
        Assert.Empty(MotionPlanner.Arrival(true));
        Assert.Empty(MotionPlanner.Emphasis(TimeSpan.FromSeconds(5), true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryMotionStaysUnderTheCeiling(bool reduced)
    {
        foreach (var kind in Enum.GetValues<MotionKind>())
        {
            Assert.True(MotionVocabulary.For(kind, reduced, expanding: true).End <= TimeSpan.FromMilliseconds(450), kind.ToString());
        }

        foreach (var (a, b) in AllPairs())
        {
            Assert.True(new MotionPlan(1, MotionPlanner.Transition(a, b, reduced)).Duration < MotionVocabulary.Ceiling);
        }
    }

    [Fact]
    public void NoChangePlansNothing()
    {
        Assert.Empty(MotionPlanner.Transition(IslandPresentation.Alert, IslandPresentation.Alert, false));
        Assert.Empty(MotionPlanner.Level(0.5, 0.5, false)); // coalescing: equal values do not animate
        Assert.Empty(MotionPlanner.Level(0.5, null, false));
        Assert.Empty(MotionPlanner.Level(null, 0.5, false)); // a bar that just appeared is set, not animated
        Assert.Single(MotionPlanner.Level(0.5, 0.6, false));
    }

    [Fact]
    public void EmphasisOnlyInTheLastTenSeconds()
    {
        Assert.Empty(MotionPlanner.Emphasis(TimeSpan.FromSeconds(11), false));
        Assert.Single(MotionPlanner.Emphasis(TimeSpan.FromSeconds(10), false));
        Assert.Single(MotionPlanner.Emphasis(TimeSpan.FromSeconds(1), false));
        Assert.Empty(MotionPlanner.Emphasis(TimeSpan.Zero, false));
    }

    [Fact]
    public void RapidTransitionsKeepOnePlanPerChannelAndIgnoreStaleCompletions()
    {
        var tracker = new MotionTracker();
        var plans = new List<MotionPlan>();
        for (var i = 0; i < 50; i++)
        {
            var (from, to) = i % 2 == 0 ? (IslandPresentation.Compact, IslandPresentation.Expanded) : (IslandPresentation.Expanded, IslandPresentation.Compact);
            plans.Add(tracker.Begin(MotionPlanner.Transition(from, to, false))!);
        }

        Assert.Equal(50, plans.Select(p => p.Id).Distinct().Count());
        Assert.True(tracker.ActiveCount <= Enum.GetValues<MotionChannel>().Length); // bounded, however many events
        foreach (var stale in plans.Take(49))
        {
            Assert.False(tracker.Complete(stale.Id)); // a superseded plan's callback does nothing
        }

        Assert.True(tracker.Complete(plans[^1].Id));
        Assert.False(tracker.Complete(plans[^1].Id)); // only once
        Assert.Equal(0, tracker.ActiveCount);
    }

    [Fact]
    public void PlansOnDifferentChannelsDoNotCancelEachOther()
    {
        var tracker = new MotionTracker();
        var level = tracker.Begin(MotionPlanner.Level(0.2, 0.4, false))!;
        var pulse = tracker.Begin(MotionPlanner.Arrival(false))!;
        Assert.True(tracker.IsCurrent(level.Id));
        Assert.True(tracker.IsCurrent(pulse.Id));
        var level2 = tracker.Begin(MotionPlanner.Level(0.4, 0.9, false))!;
        Assert.False(tracker.IsCurrent(level.Id));
        Assert.True(tracker.IsCurrent(level2.Id));
        Assert.True(tracker.IsCurrent(pulse.Id));
    }

    [Fact]
    public void CancelAndShutdownStopEverything()
    {
        var tracker = new MotionTracker();
        var plan = tracker.Begin(MotionPlanner.Transition(IslandPresentation.Compact, IslandPresentation.Alert, false))!;
        tracker.CancelAll();
        Assert.False(tracker.Complete(plan.Id));

        var again = tracker.Begin(MotionPlanner.Arrival(false))!;
        tracker.Shutdown();
        Assert.False(tracker.Complete(again.Id));
        Assert.Null(tracker.Begin(MotionPlanner.Arrival(false)));
        Assert.Equal(0, tracker.ActiveCount);
        Assert.Null(new MotionTracker().Begin([]));
    }

    private static IEnumerable<(IslandPresentation, IslandPresentation)> AllPairs()
    {
        var all = Enum.GetValues<IslandPresentation>();
        return from a in all from b in all select (a, b);
    }
}
