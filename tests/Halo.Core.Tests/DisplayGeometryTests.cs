using Halo.Core.Island;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>
/// Placement for display configurations this machine cannot show (M2-F04). One physical 1920×1080 display at 125 %
/// exists, so these are geometry checks, not hardware validation.
/// </summary>
public class DisplayGeometryTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(168, 1.75)]
    [InlineData(192, 2.0)]
    public void ShapesScaleWithDpiAndStayInsideTheCanvas(int dpi, double scale)
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 3840, 2100), dpi);

        Assert.Equal(scale, layout.Scale);
        foreach (var shape in new[] { layout.Compact, layout.Expanded, layout.Alert })
        {
            Assert.True(shape.Left >= 0 && shape.Right <= layout.Canvas.Width && shape.Bottom <= layout.Canvas.Height);
            Assert.Equal(layout.Canvas.Width / 2.0, shape.CenterX, 0.5);
        }
    }

    [Fact]
    public void LeftTaskbarMovesTheCentreOfTheWorkArea()
    {
        var layout = IslandLayout.Compute(new PixelRect(62, 0, 1858, 1080), 96);

        Assert.Equal(62 + ((1858 - layout.Canvas.Width) / 2), layout.Canvas.Left);
        Assert.Equal(0, layout.Canvas.Top);
    }

    [Fact]
    public void TopTaskbarPushesTheIslandBelowIt()
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 60, 1920, 1020), 120);

        Assert.Equal(60, layout.Canvas.Top);
    }

    [Fact]
    public void AMonitorWithNegativeCoordinatesIsHandled()
    {
        // A display to the left of the primary one (for when the island follows other monitors).
        var layout = IslandLayout.Compute(new PixelRect(-2560, -200, 2560, 1400), 144);

        Assert.True(layout.Canvas.Left < 0);
        Assert.Equal(-2560 + ((2560 - layout.Canvas.Width) / 2), layout.Canvas.Left);
        Assert.Equal(-200, layout.Canvas.Top);
    }

    [Fact]
    public void SameInputsGiveEqualLayoutsSoRepeatedNotificationsAreNoOps()
    {
        var a = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1020), 120);
        var b = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1020), 120);

        Assert.Equal(a, b);
        Assert.NotEqual(a, IslandLayout.Compute(new PixelRect(0, 60, 1920, 960), 120)); // work area moved
        Assert.NotEqual(a, IslandLayout.Compute(new PixelRect(0, 0, 1920, 1020), 144)); // DPI changed
        Assert.Equal(a, IslandLayout.Compute(new PixelRect(0, 0, 1920, 1080), 120)); // only the bottom changed: same canvas
    }

    [Fact]
    public void AVerySmallWorkAreaIsRejectedRatherThanOverflowing() =>
        Assert.Throws<ArgumentException>(() => IslandLayout.Compute(new PixelRect(0, 0, 640, 200), 192));
}
