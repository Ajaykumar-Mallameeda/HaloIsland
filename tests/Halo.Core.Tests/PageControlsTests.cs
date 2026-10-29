using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Media;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Visual Experience 2.0: the in-island pages and their controls (one list for drawing, hit-testing and UIA).</summary>
public class PageControlsTests
{
    private static readonly IslandLayout Layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1032), 120, volumeRow: true);

    private static readonly IslandActivity Timer = new("timer", ActivityKind.Timer, AlertPriority.Normal)
    {
        Level = 0.5,
        Actions = [new ActivityAction("pause", "Pause", "p"), new ActivityAction("add", "Add 1 minute", "+"), new ActivityAction("cancel", "Cancel timer", "x")],
    };

    private static PageContext Full => new()
    {
        Activity = Timer,
        TaskRows = [("a", "One"), ("b", "Two"), ("c", "Three"), ("d", "Four")],
        ShelfTiles = [("1", "a.txt"), ("2", "b"), ("3", "c"), ("4", "d"), ("5", "e")],
        PageCount = 6,
    };

    public static TheoryData<IslandPage> AllPages()
    {
        var data = new TheoryData<IslandPage>();
        foreach (var page in Enum.GetValues<IslandPage>())
        {
            data.Add(page);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllPages))]
    public void ControlsAreLargeEnoughInsideTheIslandAndDoNotOverlap(IslandPage page)
    {
        var controls = PageControls.For(Layout, page, Full);
        var e = Layout.Expanded;
        foreach (var c in controls)
        {
            Assert.True(c.Bounds.Width >= Layout.Dip(24) - 0.01 && c.Bounds.Height >= Layout.Dip(24) - 0.01, c.Id); // FR-A11Y-07
            Assert.True(c.Bounds.Left >= e.Left && c.Bounds.Right <= e.Right && c.Bounds.Top >= e.Top && c.Bounds.Bottom <= e.Bottom, c.Id);
        }

        for (var i = 0; i < controls.Count; i++)
        {
            for (var j = i + 1; j < controls.Count; j++)
            {
                var (a, b) = (controls[i].Bounds, controls[j].Bounds);
                var overlap = a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;
                Assert.False(overlap, $"{controls[i].Id} overlaps {controls[j].Id}");
            }
        }

        Assert.Equal(controls.Count, controls.Select(c => c.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllPages))]
    public void ControlsStayClearOfThePageDotsAndTheMediaButtons(IslandPage page)
    {
        var dots = ActivityLayout.Dots(Layout, 6);
        var media = page == IslandPage.Media
            ? MediaControls.Layout(Layout, new MediaSnapshot { Title = "t", Artist = "a", CanPrevious = true, CanPlayPause = true, CanNext = true, SessionCount = 2 })
            : [];
        foreach (var c in PageControls.For(Layout, page, Full))
        {
            Assert.DoesNotContain(dots, d => d.Contains(c.Bounds.CenterX, c.Bounds.Top + (c.Bounds.Height / 2)));
            Assert.DoesNotContain(media, m => m.Contains(c.Bounds.CenterX, c.Bounds.Top + (c.Bounds.Height / 2)));
        }
    }

    [Fact]
    public void LongActionLabelsFitByMakingSecondaryChipsIconOnly()
    {
        var chips = PageControls.For(Layout, IslandPage.Activity, Full).Where(c => c.Command == PageCommand.ActivityAction).ToList();
        Assert.Equal(3, chips.Count);
        Assert.True(chips[0].ShowLabel && chips[0].Primary); // the main action keeps its label
        Assert.Contains(chips.Skip(1), c => !c.ShowLabel); // something had to give
        Assert.All(chips, c => Assert.False(string.IsNullOrEmpty(c.Spoken))); // icon-only chips keep their name

        var right = Layout.Expanded.Right - Layout.Dip(Halo.Core.Theming.HaloDesign.Inset);
        Assert.True(chips[^1].Bounds.Right <= right + 0.5);

        // Short labels all fit.
        var row = ActivityLayout.ChipRow(Layout, ["Pause", "Skip", "Stop"], ActivityLayout.ReadoutLeftDip, ActivityLayout.ChipRowCenterDip, ActivityLayout.ChipRowWidthDip);
        Assert.All(row, r => Assert.True(r.ShowLabel));
    }

    [Fact]
    public void ListsAreBoundedAndCarryTheirItem()
    {
        var tasks = PageControls.For(Layout, IslandPage.Tasks, Full).Where(c => c.Command == PageCommand.CompleteTask).ToList();
        Assert.Equal(["a", "b", "c"], tasks.Select(t => t.Key));
        var tiles = PageControls.For(Layout, IslandPage.Shelf, Full).Where(c => c.Command == PageCommand.CopyShelfItem).ToList();
        Assert.Equal(["1", "2", "3", "4"], tiles.Select(t => t.Key));
    }

    [Fact]
    public void HitTestFindsTheControlUnderThePoint()
    {
        var controls = PageControls.For(Layout, IslandPage.Focus, Full);
        var start = controls.Single(c => c.Command == PageCommand.StartFocus);
        Assert.Equal(start, PageControls.HitTest(controls, start.Bounds.CenterX, start.Bounds.Top + 2));
        Assert.Null(PageControls.HitTest(controls, Layout.Expanded.CenterX, Layout.Expanded.Top + Layout.Dip(20)));
        var next = controls.Single(c => c.Command == PageCommand.NextPage);
        Assert.Equal(next, PageControls.HitTest(controls, next.Bounds.CenterX, next.Bounds.Top + (next.Bounds.Height / 2)));
    }

    [Fact]
    public void ASinglePageHasNoChevrons()
    {
        Assert.DoesNotContain(PageControls.For(Layout, IslandPage.Clock, new PageContext { PageCount = 1 }), c => c.Command is PageCommand.PreviousPage or PageCommand.NextPage);
    }

    [Fact]
    public void PagesHaveTitlesAndToneAndTheFeaturePagesAlwaysExist()
    {
        Assert.Equal([IslandPage.Clock, IslandPage.Focus, IslandPage.Tasks, IslandPage.Shelf], IslandPages.Available(false, false));
        Assert.Equal(IslandPage.Activity, IslandPages.Resolve(null, IslandPages.Available(true, true)));
        Assert.Equal("Timer", IslandPages.Title(IslandPage.Activity, Timer));
        Assert.Equal("Now Playing", IslandPages.Title(IslandPage.Media, null));
        Assert.Equal(Halo.Core.Theming.Tone.Timer, IslandPages.ToneOf(IslandPage.Activity, Timer));
        Assert.Equal(Halo.Core.Theming.Tone.Neutral, IslandPages.ToneOf(IslandPage.Clock, null));
    }
}
