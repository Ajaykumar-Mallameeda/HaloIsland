using Halo.Core.Island;
using Halo.Core.Media;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Transport buttons (M3-F02): availability, geometry and hit testing.</summary>
public class MediaControlsTests
{
    private static readonly IslandLayout Layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1020), 120);

    [Fact]
    public void NoSessionMeansNoButtons() => Assert.Empty(MediaControls.Layout(Layout, null));

    [Fact]
    public void OnlyControlsTheSessionEnablesAreOffered()
    {
        var onlyPlay = Snapshot(previous: false, playPause: true, next: false);

        var buttons = MediaControls.Layout(Layout, onlyPlay);

        Assert.Equal([MediaCommand.PlayPause], buttons.Select(b => b.Command));
        Assert.Empty(MediaControls.Layout(Layout, Snapshot(false, false, false)));
    }

    [Fact]
    public void ButtonsSitInsideTheExpandedShapeBelowTheProgressAndAreLargeEnough()
    {
        var buttons = MediaControls.Layout(Layout, Snapshot(true, true, true));
        var e = Layout.Expanded;

        Assert.Equal(3, buttons.Count);
        Assert.All(buttons, b =>
        {
            Assert.True(b.Size >= Layout.Dip(24)); // FR-A11Y-07 minimum target
            Assert.True(b.CenterX - (b.Size / 2) >= e.Left && b.CenterX + (b.Size / 2) <= e.Right);
            Assert.True(b.CenterY + (b.Size / 2) <= e.Bottom);
            Assert.True(b.CenterY - (b.Size / 2) > e.Top + Layout.Dip(90)); // below the progress bar
            Assert.False(Layout.Compact.Contains(b.CenterX, b.CenterY)); // never under the pill
        });
        Assert.True(buttons[0].CenterX < buttons[1].CenterX && buttons[1].CenterX < buttons[2].CenterX);
    }

    [Fact]
    public void HitTestFindsTheButtonUnderThePointerAndNothingBetweenThem()
    {
        var buttons = MediaControls.Layout(Layout, Snapshot(true, true, true));
        var play = buttons.Single(b => b.Command == MediaCommand.PlayPause);
        var next = buttons.Single(b => b.Command == MediaCommand.Next);

        Assert.Equal(MediaCommand.PlayPause, MediaControls.HitTest(buttons, play.CenterX, play.CenterY));
        Assert.Equal(MediaCommand.Next, MediaControls.HitTest(buttons, next.CenterX + (next.Size / 2) - 1, next.CenterY));
        Assert.Null(MediaControls.HitTest(buttons, (play.CenterX + next.CenterX) / 2, play.CenterY)); // the gap
        Assert.Null(MediaControls.HitTest(buttons, play.CenterX, play.CenterY - play.Size)); // above
    }

    [Fact]
    public void PlayPauseShowsTheActionItWillTake()
    {
        Assert.Equal("", MediaControls.Glyph(MediaCommand.PlayPause, playing: true)); // pause
        Assert.Equal("", MediaControls.Glyph(MediaCommand.PlayPause, playing: false)); // play
        Assert.NotEqual(MediaControls.Glyph(MediaCommand.Previous, true), MediaControls.Glyph(MediaCommand.Next, true));
    }

    private static MediaSnapshot Snapshot(bool previous, bool playPause, bool next) =>
        new() { Title = "t", Artist = "a", CanPrevious = previous, CanPlayPause = playPause, CanNext = next, Playback = MediaPlayback.Playing };
}
