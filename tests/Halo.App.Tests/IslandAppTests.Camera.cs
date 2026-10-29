using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// K / C-F08 on the desktop. The camera is never opened: the real Windows sensor activity monitor is started passively,
/// and the in-use transitions are synthetic reports through a diagnostics-only message.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestCamera = 0x8000 + 58;

    [Fact]
    public void TheCameraIndicatorIsOffByDefault()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        Thread.Sleep(300);
        Assert.Empty(island.Events("camera.watch")); // nothing is started at all
    }

    [Fact]
    public void WhenEnabledItStartsWindowsReportsAndShowsCameraUse()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showCameraActivity\": true }");
        var watch = island.WaitFor("camera.watch");
        Assert.Equal("running", watch["status"]!.GetValue<string>()); // the real monitor started (passive, no camera opened)

        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        Assert.True(PostMessageW(island.Hwnd, WmTestCamera, 1, 0)); // synthetic: another process streams
        Assert.Equal(1, island.WaitFor("camera")["in_use"]!.GetValue<int>());
        Assert.True(WaitUntil(() => element.Name.Contains("Camera in use", StringComparison.Ordinal)), element.Name);

        Assert.True(PostMessageW(island.Hwnd, WmTestCamera, 0, 0)); // synthetic: streaming stopped
        Assert.Equal(0, island.WaitFor("camera", skip: 1)["in_use"]!.GetValue<int>());
        Assert.True(WaitUntil(() => !element.Name.Contains("Camera", StringComparison.Ordinal)), element.Name);
    }
}
