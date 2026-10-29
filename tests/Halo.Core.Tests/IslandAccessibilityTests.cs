using Halo.Core.Accessibility;
using Halo.Core.Activities;
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

        // Information in reading order; the page controls and page dots (Visual Experience 2.0) are checked below.
        Assert.Equal(
            [IslandAccessibility.TimeId, IslandAccessibility.DateId, IslandAccessibility.DayProgressId, IslandAccessibility.NetworkId, IslandAccessibility.BatteryId, IslandAccessibility.MuteId, IslandAccessibility.VolumeId],
            nodes.Where(n => n.Command is not (AccessibleCommand.PageControl or AccessibleCommand.ShowPage)).Select(n => n.Id));
        Assert.Equal(
            [PageControls.PreviousId, PageControls.NextId, "Halo.Quiet", "Halo.Settings"],
            nodes.Where(n => n.Command == AccessibleCommand.PageControl).Select(n => n.Id));
        var quiet = nodes.Single(n => n.Id == "Halo.Quiet");
        Assert.Equal((AccessibleRole.ToggleButton, false), (quiet.Role, quiet.Toggled));
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
    public void TheActivityPageExposesTheActivityItsActionsAndThePageButtons()
    {
        var timer = new IslandActivity("timer", ActivityKind.Timer, AlertPriority.Normal)
        {
            Title = "4:00",
            Level = 0.8,
            Accessible = "Timer, 4 minutes left",
            Actions = [new ActivityAction("pause", "Pause", "p"), new ActivityAction("cancel", "Cancel timer", "c")],
        };
        var media = new MediaSnapshot { Title = "Song", Artist = "Band", CanPlayPause = true };
        var nodes = IslandAccessibility.Children(Input(Expanded) with { Activity = timer, Media = media });

        var activity = nodes.Single(n => n.Id == IslandAccessibility.ActivityId);
        Assert.Equal((AccessibleRole.ProgressBar, "Timer, 4 minutes left", 80d), (activity.Role, activity.Name, activity.Value));
        var actions = nodes.Where(n => n.Command == AccessibleCommand.ActivityAction).ToList();
        Assert.Equal(["Pause", "Cancel timer"], actions.Select(a => a.Name));
        Assert.Equal([0, 1], actions.Select(a => a.Argument));
        Assert.DoesNotContain(nodes, n => n.Id == IslandAccessibility.MediaId); // one page at a time, like the screen

        var pages = nodes.Where(n => n.Command == AccessibleCommand.ShowPage).ToList();
        Assert.Equal(["Activity, shown", "Show now playing", "Show clock", "Show focus timer", "Show to-do list", "Show shelf"], pages.Select(p => p.Name));
        Assert.All(pages, p => Assert.True(p.Bounds.Width >= Layout.Dip(24))); // FR-A11Y-07

        var mediaPage = IslandAccessibility.Children(Input(Expanded) with { Activity = timer, Media = media, Page = IslandPage.Media });
        Assert.Contains(mediaPage, n => n.Id == IslandAccessibility.MediaId);
        Assert.DoesNotContain(mediaPage, n => n.Id == IslandAccessibility.ActivityId);
        Assert.Empty(IslandAccessibility.Children(Input(Collapsed) with { Activity = timer }));
    }

    [Fact]
    public void HalosOwnFeaturePagesAreAlwaysThere()
    {
        // Visual Experience 2.0: the clock, focus timer, to-do list and shelf live in the island itself.
        var pages = IslandAccessibility.Children(Input(Expanded)).Where(n => n.Command == AccessibleCommand.ShowPage).Select(n => n.Name);
        Assert.Equal(["Clock, shown", "Show focus timer", "Show to-do list", "Show shelf"], pages);
    }

    [Fact]
    public void TheFocusPageOffersThePresetsAsButtons()
    {
        var nodes = IslandAccessibility.Children(Input(Expanded) with { Page = IslandPage.Focus });
        Assert.Contains(nodes, n => n.Id == IslandAccessibility.FocusId && n.Role == AccessibleRole.Text);
        var buttons = nodes.Where(n => n.Command == AccessibleCommand.PageControl && n.Id.StartsWith("Halo.Focus.", StringComparison.Ordinal)).ToList();
        Assert.Equal(["Halo.Focus.Start", "Halo.Focus.Break", "Halo.Focus.Timer"], buttons.Select(b => b.Id));
        Assert.All(buttons, b => Assert.True(b.Bounds.Width >= Layout.Dip(24) && b.Bounds.Height >= Layout.Dip(24)));
    }

    [Fact]
    public void TaskRowsAreOnlyExposedWhilePreviewsAreAllowed()
    {
        var shown = IslandAccessibility.Children(Input(Expanded) with { Page = IslandPage.Tasks, TasksDone = 1, TasksPending = 2, TaskRows = [("a", "Buy milk"), ("b", "Call Sam")] });
        Assert.Equal("1 of 3 tasks done today", shown.Single(n => n.Id == IslandAccessibility.TasksId).Name);
        Assert.Equal(["Mark done: Buy milk", "Mark done: Call Sam"], shown.Where(n => n.Id.StartsWith("Halo.Tasks.Done.", StringComparison.Ordinal)).Select(n => n.Name));

        var hidden = IslandAccessibility.Children(Input(Expanded) with { Page = IslandPage.Tasks, TasksPending = 2, TasksHidden = true });
        Assert.Equal("2 tasks pending, previews hidden", hidden.Single(n => n.Id == IslandAccessibility.TasksId).Name);
        Assert.DoesNotContain(hidden, n => n.Id.StartsWith("Halo.Tasks.Done.", StringComparison.Ordinal));
    }

    [Fact]
    public void ShelfTilesCopyTheirItem()
    {
        var nodes = IslandAccessibility.Children(Input(Expanded) with { Page = IslandPage.Shelf, ShelfCount = 5, ShelfTiles = [("1", "notes.txt"), ("2", "Hello")] });
        Assert.Equal("5 items on the shelf", nodes.Single(n => n.Id == IslandAccessibility.ShelfId).Name);
        Assert.Equal(["Copy notes.txt", "Copy Hello"], nodes.Where(n => n.Id.StartsWith("Halo.Shelf.Item.", StringComparison.Ordinal)).Select(n => n.Name));
    }

    [Fact]
    public void PageControlArgumentsIndexTheControlList()
    {
        // UIA posts the index; the window resolves it against PageControls.For for the same state.
        var input = Input(Expanded) with { Page = IslandPage.Clock, QuietOn = true };
        var controls = PageControls.For(Layout, IslandPage.Clock, new PageContext { QuietOn = true, PageCount = 4 });
        foreach (var node in IslandAccessibility.Children(input).Where(n => n.Command == AccessibleCommand.PageControl))
        {
            Assert.Equal(node.Id, controls[node.Argument].Id);
        }

        Assert.True(IslandAccessibility.Children(input).Single(n => n.Id == "Halo.Quiet").Toggled);
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
