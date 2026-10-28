using Halo.Core.Media;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Media model (M3-F01): synthetic sessions and timelines, no real player needed.</summary>
public class MediaModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoSessionsSelectsNothing() => Assert.Null(MediaSessionSelector.Select([], null));

    [Fact]
    public void WindowsCurrentSessionWinsWhileItIsListed()
    {
        MediaSessionInfo[] sessions = [new("a", MediaPlayback.Playing), new("b", MediaPlayback.Paused)];

        Assert.Equal("b", MediaSessionSelector.Select(sessions, "b"));
    }

    [Fact]
    public void WithoutAValidCurrentSessionThePlayingOneWinsThenTheFirst()
    {
        MediaSessionInfo[] sessions = [new("a", MediaPlayback.Paused), new("b", MediaPlayback.Playing)];

        Assert.Equal("b", MediaSessionSelector.Select(sessions, "gone"));
        Assert.Equal("b", MediaSessionSelector.Select(sessions, null));
        Assert.Equal("a", MediaSessionSelector.Select([new("a", MediaPlayback.Paused), new("c", MediaPlayback.Stopped)], null));
    }

    [Theory]
    [InlineData("  Song\tName  ", "Song Name")]
    [InlineData("A\u0000B​C", "ABC")] // control and zero-width characters dropped
    [InlineData("Line\r\nBreak", "Line Break")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void TextIsCleaned(string? raw, string expected) => Assert.Equal(expected, MediaText.Clean(raw));

    [Fact]
    public void TextIsCappedAt512Characters() => Assert.Equal(MediaText.MaxLength, MediaText.Clean(new string('x', 10_000)).Length);

    [Fact]
    public void TimelineInterpolatesWhilePlayingAndStopsWhenPaused()
    {
        var t = MediaTimeline.FromPlayer(TimeSpan.Zero, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(30), T0)!;

        Assert.Equal(TimeSpan.FromSeconds(40), t.PositionAt(T0.AddSeconds(10), playing: true));
        Assert.Equal(TimeSpan.FromSeconds(30), t.PositionAt(T0.AddSeconds(10), playing: false));
        Assert.Equal(TimeSpan.FromMinutes(3), t.PositionAt(T0.AddHours(1), playing: true)); // clamped to the end
        Assert.Equal("0:40 / 3:00", t.LabelAt(T0.AddSeconds(10), playing: true));
        Assert.Equal(40.0 / 180, t.FractionAt(T0.AddSeconds(10), playing: true), 6);
    }

    [Fact]
    public void LongTimelinesShowHours()
    {
        var t = MediaTimeline.FromPlayer(TimeSpan.Zero, TimeSpan.FromHours(2), TimeSpan.FromMinutes(75), T0)!;

        Assert.Equal("1:15:00 / 2:00:00", t.LabelAt(T0, playing: false));
    }

    [Theory]
    [InlineData(0, 0, 0)] // empty
    [InlineData(10, 5, 7)] // end before start
    [InlineData(0, 90_000, 10)] // over 24 h
    [InlineData(0, 180, 400)] // position far past the end
    public void BadTimelinesAreHidden(int startS, int endS, int positionS) =>
        Assert.Null(MediaTimeline.FromPlayer(TimeSpan.FromSeconds(startS), TimeSpan.FromSeconds(endS), TimeSpan.FromSeconds(positionS), T0));

    [Fact]
    public void TimelineRelativeToANonZeroStartIsNormalised()
    {
        var t = MediaTimeline.FromPlayer(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(280), TimeSpan.FromSeconds(130), T0)!;

        Assert.Equal(TimeSpan.FromSeconds(180), t.Duration);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Position);
    }

    [Theory]
    [InlineData("Song", "Band", MediaPlayback.Playing, "Playing: Song by Band", "Song — Band")]
    [InlineData("Song", "", MediaPlayback.Paused, "Paused: Song", "Song")]
    [InlineData("", "", MediaPlayback.Stopped, "Media: unknown title", "Unknown title")]
    [InlineData("", "Band", MediaPlayback.Unknown, "Media: unknown title by Band", "Band")]
    public void SummariesHandleMissingMetadata(string title, string artist, MediaPlayback playback, string accessible, string headline)
    {
        var m = new MediaSnapshot { Title = title, Artist = artist, Playback = playback };

        Assert.Equal(accessible, m.AccessibleSummary);
        Assert.Equal(headline, m.Headline);
    }
}
