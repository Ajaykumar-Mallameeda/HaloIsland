using Halo.Core.Island;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>
/// Hover-intent (FR-ISL-03) through <see cref="IslandController"/> on virtual time: no sleeps, every timing is exact.
/// </summary>
public class HoverIntentTests
{
    private static readonly TimeSpan Dwell = IslandTiming.Default.HoverDwell;
    private static readonly TimeSpan Grace = IslandTiming.Default.LeaveGrace;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void DefaultsFollowFrIsl03()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(120), Dwell);
        Assert.Equal(TimeSpan.FromMilliseconds(400), Grace);
    }

    [Fact]
    public void PointerThatStaysExpandsAfterTheDwell()
    {
        var (island, clock) = Create();

        island.Apply(IslandInput.PointerEntered);
        Assert.Equal(IslandPhase.ExpandPending, island.State.Phase);
        clock.Advance(Dwell - Tick);
        Assert.False(island.State.IsExpanded);

        clock.Advance(Tick);
        Assert.Equal(IslandPhase.ExpandedByHover, island.State.Phase);
        Assert.False(clock.IsArmed);
        Assert.Single(clock.Transitions, t => t.PresentationChanged);
    }

    [Fact]
    public void PointerThatLeavesBeforeTheDwellNeverExpands()
    {
        var (island, clock) = Create();

        island.Apply(IslandInput.PointerEntered);
        clock.Advance(Dwell / 2);
        island.Apply(IslandInput.PointerLeft);
        Assert.False(clock.IsArmed);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(IslandState.Initial, island.State);
        Assert.Equal(0, clock.Fired);
    }

    [Fact]
    public void ReEnteringRestartsTheDwellFromZero()
    {
        var (island, clock) = Create();

        island.Apply(IslandInput.PointerEntered);
        clock.Advance(Dwell - Tick);
        island.Apply(IslandInput.PointerLeft);
        island.Apply(IslandInput.PointerEntered);
        clock.Advance(Dwell - Tick);
        Assert.False(island.State.IsExpanded);

        clock.Advance(Tick);
        Assert.True(island.State.IsExpanded);
    }

    [Fact]
    public void MovingWithinTheIslandDoesNotRestartTheDwell()
    {
        var (island, clock) = Create();

        island.Apply(IslandInput.PointerEntered);
        clock.Advance(Dwell / 2);
        island.Apply(IslandInput.PointerEntered); // repeated enter while inside
        clock.Advance(Dwell / 2);

        Assert.True(island.State.IsExpanded);
    }

    [Fact]
    public void MovingFromThePillIntoThePanelKeepsTheIslandOpen()
    {
        var (island, clock) = HoverOpened();

        // The expanded shape contains the pill, so the window sees no leave while the pointer moves into the panel.
        Assert.True(IslandLayout.Compute(new PixelRect(0, 0, 1920, 1020), 120) is var l && l.Expanded.Contains(l.Compact.Left + l.Compact.Radius, l.Compact.Top + 1));
        island.Apply(IslandInput.PointerEntered);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(IslandPhase.ExpandedByHover, island.State.Phase);
        Assert.False(clock.IsArmed);
    }

    [Fact]
    public void LeavingTheIslandCollapsesItAfterTheGrace()
    {
        var (island, clock) = HoverOpened();

        island.Apply(IslandInput.PointerLeft);
        Assert.Equal(IslandPhase.CollapsePending, island.State.Phase);
        Assert.True(island.State.IsExpanded); // still open during the grace
        clock.Advance(Grace - Tick);
        Assert.True(island.State.IsExpanded);

        clock.Advance(Tick);
        Assert.Equal(IslandState.Initial, island.State);
    }

    [Fact]
    public void ReturningWithinTheGraceCancelsTheCollapse()
    {
        var (island, clock) = HoverOpened();

        island.Apply(IslandInput.PointerLeft);
        clock.Advance(Grace - Tick);
        island.Apply(IslandInput.PointerEntered);
        Assert.Equal(IslandPhase.ExpandedByHover, island.State.Phase);
        Assert.False(clock.IsArmed);
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.True(island.State.IsExpanded);
        Assert.Equal(1, clock.Fired); // only the original dwell
    }

    [Fact]
    public void ClickBeforeTheDwellExpandsAndPinsWithoutASecondTransition()
    {
        var (island, clock) = Create();

        island.Apply(IslandInput.PointerEntered);
        island.Apply(IslandInput.PointerPressed);
        Assert.False(clock.IsArmed); // the press cancels the dwell
        island.Apply(IslandInput.PointerReleasedInside);
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0, clock.Fired); // no hover expansion follows the click
    }

    [Fact]
    public void ExplicitExpansionIsNotUndoneByAStaleTimer()
    {
        var (island, clock) = HoverOpened();
        island.Apply(IslandInput.PointerLeft); // grace running

        island.Apply(IslandInput.Expand); // e.g. tray "Open Halo"
        Assert.False(clock.IsArmed);
        clock.Advance(TimeSpan.FromSeconds(10));
        island.TimerElapsed(); // a callback that arrives late anyway
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);

        // Pointer movement never closes a pinned island.
        island.Apply(IslandInput.PointerEntered);
        island.Apply(IslandInput.PointerLeft);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);
    }

    [Fact]
    public void ClickingAHoverOpenedIslandPinsIt()
    {
        var (island, clock) = HoverOpened();

        island.Apply(IslandInput.PointerPressed);
        island.Apply(IslandInput.PointerReleasedInside);
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);
        island.Apply(IslandInput.PointerLeft);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(island.State.IsExpanded);

        // A click on the pinned island closes it.
        island.Apply(IslandInput.PointerEntered);
        island.Apply(IslandInput.PointerPressed);
        island.Apply(IslandInput.PointerReleasedInside);
        Assert.Equal(IslandPhase.Collapsed, island.State.Phase);
    }

    [Fact]
    public void CollapsingWhileThePointerRestsOnThePillDoesNotReopenIt()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.PointerEntered);
        island.Apply(IslandInput.PointerPressed);
        island.Apply(IslandInput.PointerReleasedInside); // pinned open
        island.Apply(IslandInput.PointerPressed);
        island.Apply(IslandInput.PointerReleasedInside); // closed again, pointer still on the pill

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(new IslandState(IslandPhase.Collapsed, Hovered: true, Pressed: false), island.State);
        Assert.Equal(0, clock.Fired);
    }

    [Fact]
    public void TrayExpandDuringThePendingDwellPinsAndCancelsIt()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.PointerEntered);

        var t = island.Apply(IslandInput.Expand);

        Assert.True(t.PresentationChanged);
        Assert.True(t.CancelsTimer);
        Assert.False(clock.IsArmed);
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase);
    }

    [Fact]
    public void TrayCollapseDuringTheGraceCollapsesOnceAndCancelsIt()
    {
        var (island, clock) = HoverOpened();
        island.Apply(IslandInput.PointerLeft);

        var collapse = island.Apply(IslandInput.Collapse);
        Assert.True(collapse.PresentationChanged);
        Assert.False(clock.IsArmed);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(IslandState.Initial, island.State);
        Assert.Equal(1, clock.Fired); // only the dwell; the grace never fires a second collapse
    }

    [Fact]
    public void MenuRequestDropsAPendingExpansion()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.PointerEntered);

        island.Apply(IslandInput.MenuRequested);
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.False(island.State.IsExpanded);
        Assert.False(clock.IsArmed);
    }

    [Fact]
    public void ShutdownCancelsPendingCallbacksAndIgnoresLaterInput()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.PointerEntered);

        island.Shutdown();
        Assert.False(clock.IsArmed);
        Assert.False(island.TimerElapsed().Changed); // a callback already in flight
        Assert.False(island.Apply(IslandInput.Expand).Changed);
        clock.Advance(TimeSpan.FromSeconds(5));
        island.Shutdown(); // idempotent

        Assert.Equal(IslandPhase.ExpandPending, island.State.Phase);
        Assert.Equal(0, clock.Fired);
    }

    [Fact]
    public void TimerIsOnlyArmedWhileSomethingIsPending()
    {
        var (island, clock) = HoverOpened();
        Assert.False(clock.IsArmed);
        island.Apply(IslandInput.PointerLeft);
        Assert.True(clock.IsArmed);
        clock.Advance(Grace);

        Assert.False(clock.IsArmed);
        Assert.Equal(2, clock.Started); // one dwell, one grace: no periodic timer
    }

    [Fact]
    public void TimingRejectsADwellOutsideZeroToFiveHundredMilliseconds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IslandTiming(TimeSpan.FromMilliseconds(501), Grace));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IslandTiming(TimeSpan.FromMilliseconds(-1), Grace));
        Assert.Equal(TimeSpan.Zero, new IslandTiming(TimeSpan.Zero, Grace).HoverDwell);
    }

    private static (IslandController Island, VirtualTimer Clock) Create()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default);
        clock.Attach(island);
        return (island, clock);
    }

    private static (IslandController Island, VirtualTimer Clock) HoverOpened()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.PointerEntered);
        clock.Advance(Dwell);
        Assert.Equal(IslandPhase.ExpandedByHover, island.State.Phase);
        return (island, clock);
    }

    /// <summary>A one-slot timer on virtual time with Win32 SetTimer semantics (Start replaces, Cancel stops).</summary>
    private sealed class VirtualTimer : IIslandTimer
    {
        private IslandController? _island;
        private TimeSpan _now;
        private TimeSpan? _due;

        public bool IsArmed => _due is not null;

        public int Started { get; private set; }

        public int Fired { get; private set; }

        public List<IslandTransition> Transitions { get; } = [];

        public void Attach(IslandController island) => _island = island;

        public void Start(TimeSpan delay)
        {
            Started++;
            _due = _now + delay;
        }

        public void Cancel() => _due = null;

        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            while (_due is { } due && due <= end)
            {
                _now = due;
                _due = null;
                Fired++;
                Transitions.Add(_island!.TimerElapsed());
            }

            _now = end;
        }
    }
}
