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
        // Visual Experience 2.0 (flush top, 460 × 196 expanded, 14 DIP margin for the shoulders).
        Assert.Equal(610, layout.Canvas.Width); // (460 + 2 × 14) × 1.25 = 610
        Assert.Equal(263, layout.Canvas.Height); // (0 + 196 + 14) × 1.25 = 262.5 → 263
        Assert.Equal(0, layout.Canvas.Top);
        Assert.Equal((1920 - 610) / 2, layout.Canvas.Left);
    }

    [Fact]
    public void ShapesAreTopCentredInsideTheCanvas()
    {
        var layout = IslandLayout.Compute(WorkArea125, 120);

        foreach (var shape in new[] { layout.Compact, layout.Expanded })
        {
            Assert.Equal(layout.Canvas.Width / 2.0, shape.CenterX, 0.5);
            Assert.Equal(0, shape.Top); // flush with the top of the work area
            Assert.True(shape.Right <= layout.Canvas.Width && shape.Bottom <= layout.Canvas.Height);

            // Room for the shoulders on both sides.
            Assert.True(shape.Left - layout.Shoulder >= 0 && shape.Right + layout.Shoulder <= layout.Canvas.Width);
        }

        Assert.Equal(295, layout.Compact.Width);
        Assert.Equal(45, layout.Compact.Height);
        Assert.Equal(22.5, layout.Compact.Radius, 6); // a pill: radius = half the compact height
        Assert.Equal(575, layout.Expanded.Width);
        Assert.Equal(245, layout.Expanded.Height);
        Assert.Equal(13, layout.Shoulder); // 10 DIP × 1.25 = 12.5 → 13
    }

    [Fact]
    public void TheDrawnShapeStartsAboveTheWindowSoOnlyItsLowerCornersShow()
    {
        var layout = IslandLayout.Compute(WorkArea125, 120);
        foreach (var shape in new[] { layout.Compact, layout.Expanded, layout.Alert })
        {
            var drawn = shape.Drawn();
            Assert.Equal(shape.Bottom, drawn.Bottom, 6);
            Assert.Equal(-Math.Min(shape.Radius, shape.Height / 2), drawn.Top, 6);
            Assert.Equal((shape.Left, shape.Width, shape.Radius), (drawn.Left, drawn.Width, drawn.Radius));
        }
    }

    [Fact]
    public void TopCornersAreSquareForHitTesting()
    {
        var box = new RoundedBox(0, 0, 200, 40, 20);
        Assert.False(box.Contains(1, 1)); // the plain rounded box excludes its top-left corner
        Assert.True(box.ContainsSquareTop(1, 1)); // the flush-top island includes it
        Assert.True(box.ContainsSquareTop(199, 0));
        Assert.False(box.ContainsSquareTop(1, 39)); // lower corners stay round
        Assert.False(box.ContainsSquareTop(100, 41));
        Assert.False(box.ContainsSquareTop(-1, 5));
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
