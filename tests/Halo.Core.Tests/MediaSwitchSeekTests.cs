using Halo.Core.Accessibility;
using Halo.Core.Island;
using Halo.Core.Media;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>J-F01 manual session switching and J-F02 seeking (Core rules).</summary>
public class MediaSwitchSeekTests
{
    private static readonly IslandLayout Layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1032), 120, volumeRow: true);

    private static readonly MediaSessionInfo[] Sessions =
    [
        new("Spotify.exe", MediaPlayback.Paused),
        new("MSEdge", MediaPlayback.Playing),
        new("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", MediaPlayback.Paused),
    ];

    [Fact]
    public void TheUsersChoiceWinsWhileItsSessionExists()
    {
        Assert.Equal("MSEdge", MediaSessionSelector.Select(Sessions, currentId: null)); // playing first
        Assert.Equal("Spotify.exe", MediaSessionSelector.Select(Sessions, currentId: "Spotify.exe"));
        Assert.Equal("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", MediaSessionSelector.Select(Sessions, "Spotify.exe", preferredId: "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"));
        Assert.Equal("Spotify.exe", MediaSessionSelector.Select(Sessions, "Spotify.exe", preferredId: "gone.exe")); // choice ended: fall back
    }

    [Fact]
    public void NextWrapsAndNeedsTwoSessions()
    {
        Assert.Equal("MSEdge", MediaSessionSelector.Next(Sessions, "Spotify.exe"));
        Assert.Equal("Spotify.exe", MediaSessionSelector.Next(Sessions, "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic"));
        Assert.Equal("Spotify.exe", MediaSessionSelector.Next(Sessions, "unknown")); // from nowhere: the first
        Assert.Null(MediaSessionSelector.Next([Sessions[0]], "Spotify.exe"));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData("MSEdge", "MSEdge")]
    [InlineData("C:\\Apps\\Player.exe", "Player")]
    [InlineData("", "Media")]
    [InlineData(null, "Media")]
    public void SourceNamesAreShort(string? appId, string expected) => Assert.Equal(expected, MediaSessionSelector.SourceName(appId));

    [Fact]
    public void TheSwitchButtonAppearsOnlyWithSeveralSessionsAndDoesNotOverlapNext()
    {
        var one = Media() with { SessionCount = 1 };
        Assert.DoesNotContain(MediaControls.Layout(Layout, one), b => b.Command == MediaCommand.SwitchSource);
        var buttons = MediaControls.Layout(Layout, one with { SessionCount = 3 });
        var sw = buttons.Single(b => b.Command == MediaCommand.SwitchSource);
        var next = buttons.Single(b => b.Command == MediaCommand.Next);
        Assert.True(sw.CenterX - (sw.Size / 2) > next.CenterX + (next.Size / 2), "the switch button overlaps Next");
        Assert.True(sw.CenterX + (sw.Size / 2) <= Layout.Expanded.Right);
    }

    [Fact]
    public void SeekingIsOfferedOnlyWhenTheSessionAllowsIt()
    {
        Assert.Null(MediaControls.Seek(Layout, Media()));
        Assert.Null(MediaControls.Seek(Layout, Media() with { CanSeek = true, Timeline = null }));
        var band = MediaControls.Seek(Layout, Media() with { CanSeek = true })!;
        Assert.Equal(0, band.FractionAt(band.Left - 50));
        Assert.Equal(1, band.FractionAt(band.Left + band.Width + 50));
        Assert.Equal(0.5, band.FractionAt(band.Left + (band.Width / 2)), 3);
        Assert.True(band.Contains(band.Left + 10, band.CenterY));
        Assert.False(band.Contains(band.Left + 10, band.CenterY + 40));
    }

    [Fact]
    public void TheTreeOffersASeekSliderAndASwitchButtonOnlyWhenAvailable()
    {
        var expanded = IslandState.Initial with { Phase = IslandPhase.ExpandedPinned };
        var clock = new Halo.Core.Clock.ClockReading("4:05", "PM", "Wed 28", "Wednesday", 0.5, 300);
        var fixedNow = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        AccessibleNode Progress(MediaSnapshot m) => IslandAccessibility.Children(new IslandAccessibilityInput(Layout, expanded, clock) { Media = m, Now = fixedNow }).Single(n => n.Id == IslandAccessibility.MediaProgressId);

        Assert.Equal((AccessibleRole.ProgressBar, AccessibleCommand.None), (Progress(Media()).Role, Progress(Media()).Command));
        var seekable = Progress(Media() with { CanSeek = true });
        Assert.Equal((AccessibleRole.Slider, AccessibleCommand.Seek), (seekable.Role, seekable.Command));

        var nodes = IslandAccessibility.Children(new IslandAccessibilityInput(Layout, expanded, clock) { Media = Media() with { SessionCount = 2, SessionIndex = 1 }, Now = fixedNow });
        var sw = nodes.Single(n => n.Id == IslandAccessibility.SwitchId);
        Assert.Equal(("Switch media source (1 of 2)", AccessibleCommand.MediaSwitch), (sw.Name, sw.Command));
    }

    private static MediaSnapshot Media() => new()
    {
        Title = "Song",
        Artist = "Band",
        Playback = MediaPlayback.Playing,
        CanPlayPause = true,
        CanNext = true,
        CanPrevious = true,
        SessionCount = 1,
        Timeline = new MediaTimeline(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120), new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero)),
    };
}
