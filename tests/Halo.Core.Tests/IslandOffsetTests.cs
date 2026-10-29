using Halo.Core.Island;
using Halo.Core.Settings;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>I-F04: horizontal position on the chosen display, always inside the work area.</summary>
public class IslandOffsetTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void ZeroIsCentredAndAnOffsetMovesByDipTimesScale()
    {
        var centred = IslandLayout.Compute(Work, 120);
        Assert.Equal(centred, IslandLayout.Compute(Work, 120, offsetDip: 0));
        var right = IslandLayout.Compute(Work, 120, offsetDip: 200);
        Assert.Equal(centred.Canvas.Left + 250, right.Canvas.Left); // 200 DIP at 125 % = 250 px
        Assert.Equal(centred.Canvas.Width, right.Canvas.Width);
        var left = IslandLayout.Compute(Work, 120, offsetDip: -200);
        Assert.Equal(centred.Canvas.Left - 250, left.Canvas.Left);
        Assert.Equal(centred.Compact, left.Compact); // shapes are canvas-relative: hit-testing is unchanged
    }

    [Theory]
    [InlineData(800)]
    [InlineData(-800)]
    [InlineData(5000)]
    public void TheIslandNeverLeavesTheWorkArea(int offset)
    {
        var layout = IslandLayout.Compute(Work, 120, offsetDip: offset);
        Assert.InRange(layout.Canvas.Left, Work.Left, Work.Right - layout.Canvas.Width);

        var small = new PixelRect(-1280, -100, 1280, 720); // a monitor left of and above the primary
        var onSmall = IslandLayout.Compute(small, 96, offsetDip: offset);
        Assert.InRange(onSmall.Canvas.Left, small.Left, small.Right - onSmall.Canvas.Width);
        Assert.Equal(small.Top, onSmall.Canvas.Top);
    }

    [Fact]
    public void TheSettingIsBoundedAndDefaultsToCentred()
    {
        Assert.Equal(0, HaloSettings.Defaults.IslandOffsetDip);
        Assert.True(HaloSettings.TryParse("{ \"schemaVersion\": 1, \"islandOffsetDip\": -320 }", out var s, out _, out var issues));
        Assert.Equal((-320, 0), (s.IslandOffsetDip, issues.Count));
        Assert.True(HaloSettings.TryParse("{ \"schemaVersion\": 1, \"islandOffsetDip\": 99999 }", out var bad, out _, out var badIssues));
        Assert.Equal(0, bad.IslandOffsetDip);
        Assert.Contains("islandOffsetDip", badIssues);
    }
}
