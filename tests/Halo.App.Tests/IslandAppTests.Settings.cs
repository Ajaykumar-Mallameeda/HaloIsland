using System.IO;
using Xunit;

namespace Halo.App.Tests;

/// <summary>Persistent settings take effect in the running island (M2-F01). Each run has its own data folder.</summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void ClickOnlyModeFromSettingsDoesNotExpandOnHover()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"hoverToExpand\": false }");
        Assert.Equal("loaded", island.WaitFor("settings")["status"]!.GetValue<string>());
        Assert.Equal(0, island.WaitFor("settings.applied")["hover"]!.GetValue<int>());

        MoveTo(pointer.PillX, pointer.PillY);
        Thread.Sleep(700);
        AssertPointerUndisturbed();
        Assert.True(island.Count("transition.start") == 0, island.Trace());

        // A click still opens it.
        ClickAt(pointer.PillX, pointer.PillY);
        Assert.Equal("expand", island.WaitFor("transition.start")["dir"]!.GetValue<string>());
    }

    [Fact]
    public void StartExpandedAndCustomDwellAreApplied()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"startExpanded\": true, \"hoverDwellMs\": 300 }");

        Assert.Equal(300, island.WaitFor("settings.applied")["dwell_ms"]!.GetValue<int>());
        Assert.Equal("startup", island.WaitFor("transition.start")["source"]!.GetValue<string>());
        island.WaitFor("transition.end");
        AssertRegion(island.Hwnd, ExpectedLayout().Expanded);
    }

    [Fact]
    public void FullscreenSuppressionCanBeTurnedOff()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"hideInFullscreen\": false }");
        Assert.Equal(0, island.WaitFor("settings.applied")["fullscreen"]!.GetValue<int>());
        Assert.DoesNotContain(island.Events("fullscreen.watch"), e => e["status"]?.GetValue<string>() == "running"); // hooks never installed
        using var window = TestWindow.Create();
        window.Fullscreen();
        window.ClickToForeground();

        Thread.Sleep(800);
        Assert.True(IsWindowVisible(island.Hwnd), "hidden although full-screen suppression is off");
        Assert.Empty(island.Events("fullscreen"));
    }

    [Fact]
    public void CorruptSettingsFileIsSetAsideAndTheIslandStartsWithDefaults()
    {
        using var island = IslandProcess.Start(settingsJson: "{ this is not json");

        Assert.Equal("recovered_defaults", island.WaitFor("settings")["status"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(island.DataDir, "settings.json.corrupt-*"));
        Assert.Equal(120, island.WaitFor("settings.applied")["dwell_ms"]!.GetValue<int>());
        Assert.False(island.HasExited);
    }

    /// <summary>One SendInput click at a point.</summary>
    private static void ClickAt(int x, int y) => Click(x, y);
}
