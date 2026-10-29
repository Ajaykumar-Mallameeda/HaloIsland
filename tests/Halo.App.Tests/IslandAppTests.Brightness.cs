using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// J-F03 on the desktop: the real start-up level comes from WMI on this machine's built-in panel; changes are
/// synthetic, because Halo and its tests never change the real brightness.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestBrightness = 0x8000 + 59;

    [Fact]
    public void BrightnessIsReadFromThePanelAndChangesShowAPill()
    {
        using var island = IslandProcess.Start();
        var watch = island.WaitFor("brightness.watch");
        if (watch["status"]!.GetValue<string>() != "running")
        {
            Assert.Skip("This machine has no WMI-controllable panel (external monitors are not supported).");
        }

        var start = island.WaitFor("brightness");
        Assert.InRange(start["percent"]!.GetValue<int>(), 0, 100);
        Thread.Sleep(300);
        Assert.Empty(island.Events("alert")); // the start-up level is not announced

        var level = (start["percent"]!.GetValue<int>() + 37) % 101;
        Assert.True(PostMessageW(island.Hwnd, WmTestBrightness, level, 0));
        var shown = island.WaitFor("alert");
        Assert.Equal(("shown", "brightness"), (shown["state"]!.GetValue<string>(), shown["kind"]!.GetValue<string>()));
    }

    [Fact]
    public void BrightnessCanBeTurnedOff()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showBrightness\": false }");
        island.WaitFor("ready");
        Thread.Sleep(300);
        Assert.Empty(island.Events("brightness.watch"));
    }
}
