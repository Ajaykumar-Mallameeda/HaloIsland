using Halo.Core.Display;
using Halo.Core.Island;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>J-F03: the read-only brightness pill.</summary>
public class BrightnessTests
{
    [Fact]
    public void TheFirstReadingIsSilentAndChangesShowALevelPill()
    {
        var monitor = new BrightnessMonitor();
        Assert.Null(monitor.Update(40)); // start-up
        Assert.Null(monitor.Update(40)); // unchanged
        var alert = monitor.Update(55)!;
        Assert.Equal((BrightnessMonitor.Key, AlertKind.Brightness, AlertPriority.Normal), (alert.Key, alert.Kind, alert.Priority));
        Assert.Equal(0.55, alert.Content.ClampedLevel!.Value, 3);
        Assert.Equal("55", alert.Content.Value);
        Assert.Equal("Brightness 55 percent", alert.Content.Spoken);
        Assert.False(alert.AllowedWhenSuppressed);
    }

    [Fact]
    public void LevelsAreClampedAndResetForgets()
    {
        var monitor = new BrightnessMonitor();
        monitor.Update(10);
        Assert.Equal(1.0, monitor.Update(250)!.Content.ClampedLevel);
        Assert.Equal(100, monitor.Current);
        monitor.Reset();
        Assert.Null(monitor.Update(30));
    }
}
