using Halo.Core.Privacy;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>K / C-F08: the camera-in-use indicator keeps only "in use or not" and leaves Halo itself out.</summary>
public class CameraActivityTests
{
    [Fact]
    public void AStreamByAnotherAppTurnsItOnOnceAndEndsWhenStreamingStops()
    {
        var monitor = new CameraActivityMonitor(ownProcessId: 100);
        Assert.Null(monitor.Update([]));
        Assert.Null(monitor.Activity());

        var on = monitor.Update([new CameraUse(200, true)]);
        Assert.Equal("Camera on", on!.Content.Text);
        Assert.True(on.AllowedInQuietMode);
        Assert.Equal("Camera in use", monitor.Activity()!.Title);
        Assert.Null(monitor.Update([new CameraUse(200, true), new CameraUse(300, true)])); // still on: no second pill
        Assert.Equal("2 camera streams in use", monitor.Activity()!.Subtitle);

        Assert.Null(monitor.Update([new CameraUse(200, false)])); // opened but not streaming
        Assert.False(monitor.InUse);
        Assert.Null(monitor.Activity());
    }

    [Fact]
    public void HaloItselfIsIgnoredAndResetForgets()
    {
        var monitor = new CameraActivityMonitor(ownProcessId: 100);
        Assert.Null(monitor.Update([new CameraUse(100, true)]));
        Assert.False(monitor.InUse);
        monitor.Update([new CameraUse(5, true)]);
        monitor.Reset();
        Assert.False(monitor.InUse);
        Assert.Equal(0, monitor.StreamCount);
    }

    [Fact]
    public void TheActivityNamesNoAppAndTheSettingIsOffByDefault()
    {
        var monitor = new CameraActivityMonitor(1);
        monitor.Update([new CameraUse(4242, true)]);
        var a = monitor.Activity()!;
        Assert.DoesNotContain("4242", a.Subtitle + a.Title + a.Accessible + a.CompactText, StringComparison.Ordinal);
        Assert.False(Halo.Core.Settings.HaloSettings.Defaults.ShowCameraActivity);
    }
}
