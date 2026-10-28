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
        Assert.Equal(545, layout.Canvas.Width); // (420 + 2 × 8) × 1.25 = 545
        Assert.Equal(233, layout.Canvas.Height); // (6 + 172 + 8) × 1.25 = 232.5 → 233
        Assert.Equal(0, layout.Canvas.Top);
        Assert.Equal((1920 - 545) / 2, layout.Canvas.Left);
    }

    [Fact]
    public void ShapesAreTopCentredInsideTheCanvas()
    {
        var layout = IslandLayout.Compute(WorkArea125, 120);

        foreach (var shape in new[] { layout.Compact, layout.Expanded })
        {
            Assert.Equal(layout.Canvas.Width / 2.0, shape.CenterX, 0.5);
            Assert.Equal(8, shape.Top); // 6 DIP × 1.25 = 7.5 → 8
            Assert.True(shape.Right <= layout.Canvas.Width && shape.Bottom <= layout.Canvas.Height);
        }

        Assert.Equal(295, layout.Compact.Width);
        Assert.Equal(48, layout.Compact.Height);
        Assert.Equal(24, layout.Compact.Radius, 6); // a pill: radius = half the compact height in DIP
        Assert.Equal(525, layout.Expanded.Width);
        Assert.Equal(215, layout.Expanded.Height);
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
