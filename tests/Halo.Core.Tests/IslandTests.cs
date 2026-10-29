using Halo.Core.Island;
using Xunit;

namespace Halo.Core.Tests;

public class IslandLayoutTests
{
    private static readonly PixelRect WorkArea125 = new(0, 0, 1920, 1020);

    [Fact]
    public void CanvasIsCentredUnderTheTopOfTheWorkArea()
    {
        var layout = IslandLayout.Compute(WorkArea125, 120);

        Assert.Equal(1.25, layout.Scale);
        // Live Activity Engine 2.0 (floating 8 DIP below the top, 460 × 196 expanded, 14 DIP margin for the overshoot).
        Assert.Equal(610, layout.Canvas.Width); // (460 + 2 × 14) × 1.25 = 610
        Assert.Equal(273, layout.Canvas.Height); // (8 + 196 + 14) × 1.25 = 272.5 → 273
        Assert.Equal(0, layout.Canvas.Top);
        Assert.Equal((1920 - 610) / 2, layout.Canvas.Left);
    }

    [Fact]
    public void ShapesFloatTopCentredInsideTheCanvas()
    {
        var layout = IslandLayout.Compute(WorkArea125, 120);

        foreach (var shape in new[] { layout.Compact, layout.Expanded, layout.Alert })
        {
            Assert.Equal(layout.Canvas.Width / 2.0, shape.CenterX, 0.5);
            Assert.Equal(10, shape.Top); // Live Activity Engine 2.0: 8 DIP below the top edge (floating, no notch)
            Assert.True(shape.Left >= 0 && shape.Right <= layout.Canvas.Width && shape.Bottom <= layout.Canvas.Height);
        }

        Assert.Equal(295, layout.Compact.Width);
        Assert.Equal(48, layout.Compact.Height); // 38 DIP × 1.25 = 47.5 → 48
        Assert.Equal(24, layout.Compact.Radius, 6); // a pill: radius = half the compact height
        Assert.Equal(575, layout.Expanded.Width);
        Assert.Equal(245, layout.Expanded.Height);
        Assert.Equal(50, layout.Expanded.Radius, 6);
    }

    [Theory]
    [InlineData(96, 2)]
    [InlineData(120, 3)]
    [InlineData(144, 3)]
    [InlineData(192, 4)]
    public void TheIdleLineIsAThinCentredCapsuleAtEveryScale(int dpi, int thickness)
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 3840, 2160), dpi);
        var line = layout.Line;
        Assert.Equal(thickness, line.Height); // 2–4 physical pixels
        Assert.Equal(line.Height / 2, line.Radius, 6); // rounded ends
        Assert.Equal(layout.Canvas.Width / 2.0, line.CenterX, 0.5);
        Assert.Equal(Math.Round(IslandLayout.LineWidthDip * dpi / 96.0, MidpointRounding.AwayFromZero), line.Width);
        Assert.True(line.Bottom <= layout.Compact.Top, "the line sits in the gap above where the pill appears");

        // The pointer zone is larger than the line, starts at the top edge, and stays inside the pill's own zone, so
        // moving from the line to the pill never counts as leaving the island.
        var hit = layout.LineHit;
        Assert.Equal(0, hit.Top);
        Assert.True(hit.Left <= line.Left && hit.Right >= line.Right && hit.Bottom >= line.Bottom);
        foreach (var (x, y) in new[] { (hit.Left, 0.0), (hit.Right - 1, hit.Bottom - 1), (hit.CenterX, hit.Bottom - 1) })
        {
            Assert.True(layout.Hit(IslandPresentation.Line, x, y));
            Assert.True(layout.Hit(IslandPresentation.Compact, x, y));
        }

        Assert.False(layout.Hit(IslandPresentation.Line, hit.CenterX, hit.Bottom + 1)); // below the zone: the app's click
        Assert.False(layout.Hit(IslandPresentation.Line, hit.Left - 1, 0));
    }

    [Fact]
    public void TheDetachedBubbleSitsBesideTheCompactPillInsideTheCanvas()
    {
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        {
            var layout = IslandLayout.Compute(new PixelRect(0, 0, 3840, 2160), dpi);
            var m = layout.Minimal;
            Assert.Equal(layout.Compact.Height, m.Height);
            Assert.Equal(m.Height, m.Width); // a circle
            Assert.Equal(layout.Compact.Top, m.Top);
            Assert.True(m.Left > layout.Compact.Right, "detached: a gap separates it from the pill");
            Assert.True(m.Right <= layout.Expanded.Right && m.Right <= layout.Canvas.Width, "inside the open island's footprint");
        }
    }

    [Fact]
    public void TheRegionReachesTheTopEdgeSoTheGapIsPartOfTheIsland()
    {
        var layout = IslandLayout.Compute(WorkArea125, 120);
        foreach (var shape in new[] { layout.Compact, layout.Expanded, layout.Alert })
        {
            var reach = shape.Reach();
            Assert.Equal(shape.Bottom, reach.Bottom, 6);
            Assert.True(reach.Top <= -shape.Radius + 0.001, "the rounded top corners lie above the window");
            Assert.Equal((shape.Left, shape.Width, shape.Radius), (reach.Left, reach.Width, reach.Radius));
        }

        Assert.Equal(layout.LineHit, layout.RegionFor(IslandPresentation.Line));
    }

    [Fact]
    public void TheFloatingIslandCountsItsGapForHitTesting()
    {
        var box = new RoundedBox(0, 10, 200, 40, 20);
        Assert.False(box.Contains(1, 11)); // the plain rounded box excludes its top-left corner
        Assert.True(box.ContainsWithGap(1, 0)); // the gap above the island belongs to it
        Assert.True(box.ContainsWithGap(199, 5));
        Assert.True(box.ContainsWithGap(1, 29)); // up to where the upper corners end
        Assert.False(box.ContainsWithGap(1, 49)); // lower corners stay round
        Assert.False(box.ContainsWithGap(100, 51));
        Assert.False(box.ContainsWithGap(-1, 5));
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(168)]
    public void LayoutScalesWithDpi(int dpi)
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 3840, 2100), dpi);

        Assert.Equal(dpi / 96.0, layout.Scale);
        Assert.Equal(Math.Round(236 * layout.Scale, MidpointRounding.AwayFromZero), layout.Compact.Width);
    }

    [Fact]
    public void FollowsAWorkAreaThatDoesNotStartAtZero()
    {
        // A taskbar docked at the top moves the work area down; the island stays inside it (FR-ISL-01).
        var layout = IslandLayout.Compute(new PixelRect(100, 48, 1920, 1032), 96);

        Assert.Equal(48, layout.Canvas.Top);
        Assert.Equal(100 + ((1920 - layout.Canvas.Width) / 2), layout.Canvas.Left);
    }

    [Fact]
    public void RejectsAWorkAreaSmallerThanTheCanvas() =>
        Assert.Throws<ArgumentException>(() => IslandLayout.Compute(new PixelRect(0, 0, 300, 100), 96));

    [Fact]
    public void RoundedHitTestExcludesCorners()
    {
        var pill = new RoundedBox(0, 0, 200, 40, 20);

        Assert.True(pill.Contains(100, 20));
        Assert.True(pill.Contains(20, 1));
        Assert.True(pill.Contains(0, 20));
        Assert.False(pill.Contains(1, 1)); // outside the rounded corner
        Assert.False(pill.Contains(199, 39));
        Assert.False(pill.Contains(201, 20));
    }
}

public class IslandStateMachineTests
{
    [Fact]
    public void StartsCompactWithoutFeedback()
    {
        Assert.Equal(new IslandState(IslandPhase.Collapsed, false, false), IslandState.Initial);
    }

    [Fact]
    public void CompletedClickTogglesBetweenCompactAndExpanded()
    {
        var s = IslandState.Initial;
        s = Run(s, IslandInput.PointerEntered, IslandInput.PointerPressed, IslandInput.PointerReleasedInside);
        Assert.True(s.IsExpanded);
        Assert.False(s.Pressed);

        s = Run(s, IslandInput.PointerPressed, IslandInput.PointerReleasedInside);
        Assert.False(s.IsExpanded);
        Assert.True(s.Hovered);
    }

    [Fact]
    public void PressStartsAPresentationChangeOnlyOnRelease()
    {
        var pressed = IslandStateMachine.Apply(IslandState.Initial, IslandInput.PointerPressed);

        Assert.False(pressed.PresentationChanged);
        Assert.True(pressed.PressChanged);
        Assert.True(IslandStateMachine.Apply(pressed.Next, IslandInput.PointerReleasedInside).PresentationChanged);
    }

    [Fact]
    public void ReleaseOutsideCancelsThePress()
    {
        var s = Run(IslandState.Initial, IslandInput.PointerPressed, IslandInput.PointerReleasedOutside);

        Assert.Equal(IslandState.Initial, s);
    }

    [Fact]
    public void ReleaseWithoutPressDoesNothing()
    {
        var t = IslandStateMachine.Apply(IslandState.Initial, IslandInput.PointerReleasedInside);

        Assert.False(t.Changed);
    }

    [Fact]
    public void LeavingClearsHoverAndPressButKeepsThePresentation()
    {
        var s = Run(IslandState.Initial, IslandInput.Expand, IslandInput.PointerEntered, IslandInput.PointerPressed, IslandInput.PointerLeft);

        Assert.Equal(new IslandState(IslandPhase.ExpandedPinned, false, false), s);
    }

    [Fact]
    public void ExpandAndCollapseAreIdempotent()
    {
        var expanded = Run(IslandState.Initial, IslandInput.Expand);

        Assert.False(IslandStateMachine.Apply(expanded, IslandInput.Expand).Changed);
        Assert.False(IslandStateMachine.Apply(IslandState.Initial, IslandInput.Collapse).Changed);
        Assert.True(IslandStateMachine.Apply(expanded, IslandInput.Toggle).PresentationChanged);
    }

    [Fact]
    public void HoverChangesFeedbackOnly()
    {
        var t = IslandStateMachine.Apply(IslandState.Initial, IslandInput.PointerEntered);

        Assert.True(t.HoverChanged);
        Assert.False(t.PresentationChanged);
    }

    private static IslandState Run(IslandState state, params IslandInput[] inputs)
    {
        foreach (var input in inputs)
        {
            state = IslandStateMachine.Apply(state, input).Next;
        }

        return state;
    }
}
