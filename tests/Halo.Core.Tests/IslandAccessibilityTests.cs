using Halo.Core.Accessibility;
using Halo.Core.Audio;
using Halo.Core.Clock;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Network;
using Halo.Core.Power;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>A-F01: which elements the island exposes to UI Automation, for every presentation.</summary>
public class IslandAccessibilityTests
{
    private static readonly IslandLayout Layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1032), 120, volumeRow: true);
    private static readonly ClockReading Clock = new("4:05", "PM", "Wed 28", "Wednesday, October 28", 0.67, 302);
    private static readonly IslandState Collapsed = IslandState.Initial;
    private static readonly IslandState Expanded = IslandState.Initial with { Phase = IslandPhase.ExpandedPinned };

    [Fact]
    public void CompactIslandHasNoChildren()
    {
        Assert.Empty(IslandAccessibility.Children(Input(Collapsed)));
    }

    [Fact]
    public void AnAlertIsALiveStatusElementWhileCompact()
    {
        var alert = new IslandAlert("battery", AlertKind.Battery, AlertPriority.Critical, new AlertContent("x", "Battery 9%") { Accessible = "Battery critically low, 9 percent" }, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(60));
        var nodes = IslandAccessibility.Children(Input(Collapsed with { Alert = alert }));

        var node = Assert.Single(nodes);
        Assert.Equal(IslandAccessibility.AlertId, node.Id);
        Assert.Equal(AccessibleRole.Status, node.Role);
        Assert.Equal("Battery critically low, 9 percent", node.Name);
        Assert.True(node.Assertive);
        Assert.Equal(Layout.Alert, node.Bounds);
    }

    [Fact]
    public void ANormalAlertIsPolite()
    {
        var alert = new IslandAlert("volume", AlertKind.Volume, AlertPriority.Normal, new AlertContent("x", "Volume"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Assert.False(Assert.Single(IslandAccessibility.Children(Input(Collapsed with { Alert = alert }))).Assertive);
    }

    [Fact]
    public void HiddenIslandExposesNothingEvenWithAnAlertThatIsNotAllowed()
    {
        var hidden = Collapsed with { Suppressed = true };
        Assert.Empty(IslandAccessibility.Children(Input(hidden)));
        Assert.Empty(IslandAccessibility.Children(Input(hidden with { Phase = IslandPhase.ExpandedPinned })));
    }

    [Fact]
    public void ACriticalAlertOverFullScreenIsStillExposed()
    {
        var alert = new IslandAlert("battery", AlertKind.Battery, AlertPriority.Critical, new AlertContent("x", "Battery 5%"), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(60), AllowedWhenSuppressed: true);
        var nodes = IslandAccessibility.Children(Input(Collapsed with { Suppressed = true, Alert = alert }));
        Assert.Equal(IslandAccessibility.AlertId, Assert.Single(nodes).Id);
    }

    [Fact]
    public void ExpandedClockViewListsTimeDateProgressStatusAndVolume()
    {
        var input = Input(Expanded) with
        {
            Volume = new VolumeReading(0.64f, Muted: false),
            Power = new PowerReading(true, 82, PowerSource.Battery, Charging: false),
            Network = new NetworkReading(NetworkState.Connected, NetworkKind.WiFi),
        };
        var nodes = IslandAccessibility.Children(input);

        Assert.Equal(
            [IslandAccessibility.TimeId, IslandAccessibility.DateId, IslandAccessibility.DayProgressId, IslandAccessibility.NetworkId, IslandAccessibility.BatteryId, IslandAccessibility.MuteId, IslandAccessibility.VolumeId],
            nodes.Select(n => n.Id));
        Assert.Equal("4:05 PM", nodes[0].Name);
        Assert.Equal("Wednesday, October 28", nodes[1].Name);
        Assert.Equal(67, nodes[2].Value);
        Assert.Equal(AccessibleRole.ProgressBar, nodes[2].Role);
        var mute = nodes.Single(n => n.Id == IslandAccessibility.MuteId);
        Assert.Equal((AccessibleRole.ToggleButton, false, AccessibleCommand.ToggleMute), (mute.Role, mute.Toggled, mute.Command));
        var volume = nodes.Single(n => n.Id == IslandAccessibility.VolumeId);
        Assert.Equal((AccessibleRole.Slider, 64d, AccessibleCommand.SetVolume), (volume.Role, volume.Value, volume.Command));

        // Every element lies inside the expanded shape (screen readers draw their highlight there).
        foreach (var node in nodes)
        {
            Assert.True(node.Bounds.Left >= Layout.Expanded.Left && node.Bounds.Right <= Layout.Expanded.Right, node.Id);
            Assert.True(node.Bounds.Top >= Layout.Expanded.Top && node.Bounds.Bottom <= Layout.Expanded.Bottom, node.Id);
            Assert.True(node.Bounds.Width > 0 && node.Bounds.Height > 0, node.Id);
        }
    }

    [Fact]
    public void UnknownNetworkAndDesktopsWithoutBatteryAreLeftOut()
    {
        var input = Input(Expanded) with
        {
            Power = new PowerReading(false, null, PowerSource.Ac, Charging: false),
            Network = NetworkReading.Unknown,
        };
        var ids = IslandAccessibility.Children(input).Select(n => n.Id).ToList();
        Assert.DoesNotContain(IslandAccessibility.BatteryId, ids);
        Assert.DoesNotContain(IslandAccessibility.NetworkId, ids);
    }

    [Fact]
    public void NoAudioDeviceIsTextNotASlider()
    {
        var node = IslandAccessibility.Children(Input(Expanded) with { NoAudioDevice = true }).Single(n => n.Id == IslandAccessibility.VolumeId);
        Assert.Equal((AccessibleRole.Text, "No audio output"), (node.Role, node.Name));
        Assert.DoesNotContain(IslandAccessibility.Children(Input(Expanded) with { NoAudioDevice = true }), n => n.Id == IslandAccessibility.MuteId);
    }

    [Fact]
    public void WithoutAVolumeRowThereIsNoVolumeElement()
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1032), 120, volumeRow: false);
        var nodes = IslandAccessibility.Children(new IslandAccessibilityInput(layout, Expanded, Clock) { Volume = new VolumeReading(0.5f, false) });
        Assert.DoesNotContain(nodes, n => n.Id is IslandAccessibility.VolumeId or IslandAccessibility.MuteId);
    }

    [Fact]
    public void MediaViewListsTheTrackAndOnlyTheEnabledControls()
    {
        var media = new MediaSnapshot { Title = "Song", Artist = "Band", Playback = MediaPlayback.Playing, CanPlayPause = true, CanNext = true, CanPrevious = false };
        var nodes = IslandAccessibility.Children(Input(Expanded) with { Media = media });
        var ids = nodes.Select(n => n.Id).ToList();

        Assert.Equal(media.AccessibleSummary, nodes[0].Name);
        Assert.Contains(IslandAccessibility.PlayPauseId, ids);
        Assert.Contains(IslandAccessibility.NextId, ids);
        Assert.DoesNotContain(IslandAccessibility.PreviousId, ids); // disabled by the session, absent like on screen
        Assert.DoesNotContain(IslandAccessibility.TimeId, ids); // the media card replaces the clock view
        Assert.Equal("Pause", nodes.Single(n => n.Id == IslandAccessibility.PlayPauseId).Name);
        Assert.Equal("Play", IslandAccessibility.Children(Input(Expanded) with { Media = media with { Playback = MediaPlayback.Paused } }).Single(n => n.Id == IslandAccessibility.PlayPauseId).Name);
        var next = nodes.Single(n => n.Id == IslandAccessibility.NextId);
        Assert.Equal((AccessibleRole.Button, AccessibleCommand.MediaNext), (next.Role, next.Command));
        Assert.True(next.Bounds.Width >= Layout.Dip(24)); // FR-A11Y-07 target size
    }

    [Fact]
    public void MediaProgressIsAProgressBarWhenTheTimelineIsKnown()
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var media = new MediaSnapshot { Title = "Song", Artist = "Band", Playback = MediaPlayback.Paused, Timeline = new MediaTimeline(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120), now) };
        var node = IslandAccessibility.Children(Input(Expanded) with { Media = media, Now = now }).Single(n => n.Id == IslandAccessibility.MediaProgressId);
        Assert.Equal(AccessibleRole.ProgressBar, node.Role);
        Assert.Equal(25, node.Value);
        Assert.StartsWith("Track position", node.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityElementsAreIncludedWhileExpanded()
    {
        var timer = new AccessibleNode("Halo.Timer", AccessibleRole.ProgressBar, "Timer, 4 minutes left", new RoundedBox(0, 0, 1, 1, 0));
        var nodes = IslandAccessibility.Children(Input(Expanded) with { Activities = [timer] });
        Assert.Contains(timer, nodes);
        Assert.DoesNotContain(timer, IslandAccessibility.Children(Input(Collapsed) with { Activities = [timer] }));
    }

    [Fact]
    public void TheSameInputGivesEqualElements()
    {
        var a = IslandAccessibility.Children(Input(Expanded) with { Volume = new VolumeReading(0.3f, true) });
        var b = IslandAccessibility.Children(Input(Expanded) with { Volume = new VolumeReading(0.3f, true) });
        Assert.Equal(a, b);
    }

    private static IslandAccessibilityInput Input(IslandState state) => new(Layout, state, Clock);
}
