using Halo.Core.Audio;
using Halo.Core.Island;
using Halo.Core.Media;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Volume reading, the volume pill and the volume row geometry (M3-F03, FR-SYS-01).</summary>
public class VolumeTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.004f, 0)]
    [InlineData(0.645f, 65)] // away from zero, like the Windows flyout
    [InlineData(1f, 100)]
    [InlineData(1.5f, 100)] // out of range is clamped
    [InlineData(-0.2f, 0)]
    [InlineData(float.NaN, 0)]
    public void PercentIsRoundedAndClamped(float level, int percent) => Assert.Equal(percent, new VolumeReading(level, false).Percent);

    [Theory]
    [InlineData(0f, false, "")]
    [InlineData(0.2f, false, "")]
    [InlineData(0.5f, false, "")]
    [InlineData(0.9f, false, "")]
    [InlineData(0.9f, true, "")]
    public void GlyphShowsMuteOrTheLevel(float level, bool muted, string glyph) => Assert.Equal(glyph, new VolumeReading(level, muted).Glyph);

    [Fact]
    public void TheSpokenTextNamesTheLevelAndTheMuteState()
    {
        Assert.Equal("Volume 64 percent", new VolumeReading(0.64f, false).Spoken);
        Assert.Equal("Volume muted, 64 percent", new VolumeReading(0.64f, true).Spoken);
    }

    [Fact]
    public void ThePillIsAnOrdinaryAlertWithTheLevelBar()
    {
        var alert = VolumeAlert.For(new VolumeReading(0.37f, false));

        Assert.Equal(VolumeAlert.Key, alert.Key);
        Assert.Equal(AlertKind.Volume, alert.Kind);
        Assert.Equal(AlertPriority.Normal, alert.Priority);
        Assert.False(alert.AllowedWhenSuppressed); // never over a full-screen game or video
        Assert.Equal(0.37, alert.Content.Level!.Value, 3);
        Assert.Equal("37", alert.Content.Value);
        Assert.Equal("Volume", alert.Content.Text);
        Assert.Equal("Volume 37 percent", alert.Content.Spoken);
        Assert.Equal("Muted", VolumeAlert.For(new VolumeReading(0.37f, true)).Content.Text);
    }

    [Fact]
    public void ABurstOfChangesIsOnePillWithTheNewestLevel()
    {
        var (island, clock) = Create();
        for (var i = 1; i <= 20; i++)
        {
            island.Raise(VolumeAlert.For(new VolumeReading(i / 100f, false)));
            clock.Advance(TimeSpan.FromMilliseconds(30));
        }

        Assert.Equal("20", island.State.Alert!.Content.Value);
        Assert.Empty(island.QueuedAlerts);
        clock.Advance(VolumeAlert.Duration);
        Assert.Null(island.State.Alert);
        Assert.Equal(1, clock.AlertFired); // one pill, its time restarted by each change
    }

    [Fact]
    public void APillThatHadToWaitTooLongIsDropped()
    {
        var (island, clock) = Create();
        island.Apply(IslandInput.Expand);
        island.Raise(VolumeAlert.For(new VolumeReading(0.5f, false))); // waits: the island is open
        clock.Advance(TimeSpan.FromSeconds(2));

        island.Apply(IslandInput.Collapse);

        Assert.Null(island.State.Alert); // stale after 1 s, so not shown late
    }

    [Fact]
    public void WithoutTheRowTheLayoutIsUnchanged()
    {
        var plain = IslandLayout.Compute(WorkArea, 120);
        Assert.False(plain.VolumeRow);
        Assert.Null(VolumeRow.For(plain));
        Assert.Equal(273, plain.Canvas.Height); // (8 + 196 + 14) × 1.25 = 272.5 → 273
    }

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void TheRowSitsInsideTheTallerExpandedShapeBelowEverythingElse(int dpi)
    {
        var layout = IslandLayout.Compute(WorkArea, dpi, volumeRow: true);
        var row = VolumeRow.For(layout)!;
        var e = layout.Expanded;

        Assert.Equal(Math.Round((IslandLayout.ExpandedHeightDip + VolumeRow.HeightDip) * dpi / 96.0), e.Height, 0);
        Assert.True(layout.Canvas.Height >= e.Bottom);

        // Mute button and both track ends (with the hit band) inside the rounded shape.
        Assert.True(e.Contains(row.MuteX - (row.MuteSize / 2), row.MuteY + (row.MuteSize / 2)));
        Assert.True(e.Contains(row.TrackLeft, row.TrackY + row.HitHalfHeight));
        Assert.True(e.Contains(row.TrackLeft + row.TrackWidth, row.TrackY + row.HitHalfHeight));
        Assert.True(e.Contains(row.ValueLeft + row.ValueWidth, row.TrackY));

        // Below the media buttons, with no overlap of hit areas.
        var buttons = MediaControls.Layout(layout, new MediaSnapshot { Title = "t", Artist = "a", CanPrevious = true, CanPlayPause = true, CanNext = true });
        Assert.All(buttons, b => Assert.True(b.CenterY + (b.Size / 2) < row.TrackY - row.HitHalfHeight));

        // Targets of at least 24 DIP (FR-A11Y-07).
        Assert.True(row.MuteSize >= layout.Dip(24));
        Assert.True(row.HitHalfHeight * 2 >= layout.Dip(24));
    }

    [Fact]
    public void HitTestFindsMuteTheTrackOrNothing()
    {
        var row = VolumeRow.For(IslandLayout.Compute(WorkArea, 120, volumeRow: true))!;

        Assert.Equal(VolumePart.Mute, row.HitTest(row.MuteX, row.MuteY));
        Assert.Equal(VolumePart.Track, row.HitTest(row.XFor(0.5), row.TrackY));
        Assert.Equal(VolumePart.Track, row.HitTest(row.XFor(0.5), row.TrackY + row.HitHalfHeight - 1));
        Assert.Equal(VolumePart.Track, row.HitTest(row.XFor(1) + 3, row.TrackY)); // a little past the end
        Assert.Null(row.HitTest(row.XFor(0.5), row.TrackY + row.HitHalfHeight + 2));
        var muteRight = row.MuteX + (row.MuteSize / 2);
        var bandLeft = row.TrackLeft - (row.HitHalfHeight / 2);
        Assert.True(bandLeft > muteRight);
        Assert.Null(row.HitTest((muteRight + bandLeft) / 2, row.TrackY)); // the gap between the button and the slider
        Assert.Null(row.HitTest(row.ValueLeft + row.ValueWidth, row.TrackY)); // the number is not a control
    }

    [Fact]
    public void TheLevelFollowsThePointerInWholePercents()
    {
        var row = VolumeRow.For(IslandLayout.Compute(WorkArea, 120, volumeRow: true))!;

        Assert.Equal(0f, row.LevelAt(row.TrackLeft));
        Assert.Equal(1f, row.LevelAt(row.TrackLeft + row.TrackWidth));
        Assert.Equal(0f, row.LevelAt(row.TrackLeft - 50)); // a drag past the ends clamps
        Assert.Equal(1f, row.LevelAt(row.TrackLeft + row.TrackWidth + 500));
        Assert.Equal(0.5f, row.LevelAt(row.XFor(0.5)));
        Assert.Equal(0.37f, row.LevelAt(row.XFor(0.372)), 3);
    }

    private static (IslandController Island, VirtualTimer Clock) Create()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default, clock);
        clock.Attach(island);
        return (island, clock);
    }
}
