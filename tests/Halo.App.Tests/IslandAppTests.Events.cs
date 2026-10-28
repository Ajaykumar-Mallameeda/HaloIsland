using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Halo.Core.Network;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// M3-F06: volume, battery and network events presented together by the one island controller. The volume change
/// is real (restored afterwards); the battery and network readings are SYNTHETIC (diagnostics-only messages).
/// </summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void SimultaneousSystemEventsArePresentedByPriority()
    {
        using var endpoint = Endpoint.Open();
        using var island = IslandProcess.Start();
        WaitForVolumeDevice(island, "bound");
        island.WaitFor("power");
        WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "system");
        var foreground = SettledForeground();

        // Three sources at once: a real volume change (Normal), low battery (High) and going offline (Normal).
        endpoint.SetLevel(endpoint.Near(0.02f));
        SendPower(island, ac: 0, flag: 0, percent: 15);
        SendNetwork(island, NetworkState.Disconnected, NetworkKind.None);

        // Low battery is on screen first (it replaces a volume pill that may have appeared a moment earlier) ...
        Assert.True(WaitUntil(() => AlertSequence(island).Any(a => a.Kind == "battery" && a.State is "shown" or "replaced"), 3000), Alerts(island));
        // ... "Offline" follows when it ends (5 s), and the volume pill never comes back (stale after 1 s).
        Assert.True(WaitUntil(() => AlertSequence(island).Any(a => a.Kind == "network" && a.State is "shown" or "replaced"), 8000), Alerts(island));
        Assert.True(WaitUntil(() => island.Events("alert").LastOrDefault()?["state"]?.GetValue<string>() == "ended", 5000), Alerts(island));

        var sequence = AlertSequence(island).Where(a => a.State is "shown" or "replaced").Select(a => a.Kind).ToList();
        Assert.Equal("network", sequence[^1]);
        Assert.Equal("battery", sequence[^2]);
        Assert.DoesNotContain("volume", sequence.SkipWhile(k => k != "battery"));
        Assert.Equal("collapsed", island.State());
        AssertForeground(foreground);
    }

    [Fact]
    public void OverAFullScreenAppOnlyCriticalBatteryBreaksThrough()
    {
        using var endpoint = Endpoint.Open();
        using var island = IslandProcess.Start();
        island.WaitFor("power");
        WaitForNetwork(island, e => e["reason"]?.GetValue<string>() == "system");
        using var window = TestWindow.Create();
        window.Fullscreen();
        window.ClickToForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "the island did not hide for the full-screen window");

        endpoint.SetLevel(endpoint.Near(0.02f));
        SendNetwork(island, NetworkState.Disconnected, NetworkKind.None);
        SendPower(island, ac: 0, flag: 0, percent: 15);
        WaitForPower(island, 15, "battery");
        Thread.Sleep(500);
        Assert.False(IsWindowVisible(island.Hwnd), "an ordinary alert appeared over the full-screen window");

        SendPower(island, ac: 0, flag: 0, percent: 5);
        Assert.True(WaitUntil(() => IsWindowVisible(island.Hwnd)), "critical battery did not appear over full screen");
        AssertForeground(window.Hwnd);
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd), 10_000), "the island stayed visible after the critical alert");
    }

    [Fact]
    public void TurningAFeatureOffRemovesItsPillAtOnce()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var automation = new UIA3Automation();
        using var island = IslandProcess.Start();
        island.WaitFor("tray");
        island.WaitFor("power");
        try
        {
            OpenTrayMenu(automation);
            PressKeys(0x28, 0x28, 0x28, 0x0D); // Open, Collapse, Settings… → Enter
            island.WaitFor("settings.window");
        }
        finally
        {
            CloseTrayOverflow(automation);
        }

        var window = WaitForSettingsWindow(automation)!;
        SendPower(island, ac: 0, flag: 0, percent: 5); // critical: 8 s on screen
        Assert.True(WaitUntil(() => island.Events("alert").Any(e => e["kind"]?.GetValue<string>() == "battery" && e["state"]?.GetValue<string>() == "shown"), 3000), Alerts(island));

        var battery = window.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox)).Single(b => b.Name.StartsWith("Show battery", StringComparison.Ordinal)).AsCheckBox();
        Assert.Equal(ToggleState.On, battery.ToggleState);
        battery.Toggle();

        Assert.True(WaitUntil(() => island.Events("alert").Any(e => e["kind"]?.GetValue<string>() == "battery" && e["state"]?.GetValue<string>() == "ended" && e["source"]?.GetValue<string>() == "settings"), 2000), Alerts(island));
        window.Focus();
        PressKeys(0x1B);
    }

    [Fact]
    public void ExitWithEveryFeedBusyIsClean()
    {
        using var endpoint = Endpoint.Open();
        using var island = IslandProcess.Start();
        WaitForVolumeDevice(island, "bound");
        island.WaitFor("power");

        // A pill on screen, a power alert queued behind it, and a network debounce pending.
        endpoint.SetLevel(endpoint.Near(0.02f));
        SendPower(island, ac: 0, flag: 0, percent: 15);
        Assert.True(PostMessageW(island.Hwnd, WmNetworkChanged, 0, 0));
        Assert.True(WaitUntil(() => AlertSequence(island).Any(a => a.Kind == "battery" && a.State is "shown" or "replaced"), 3000), Alerts(island));

        var exited = island.Exit();
        Assert.True(exited.TotalMilliseconds < 5000, $"exit took {exited.TotalMilliseconds} ms");
        Assert.Equal(0, island.ExitCode);
        Assert.Equal(0, FindWindowW("ProjectHalo.Island", null));
        Assert.Empty(island.Events("network.read").Skip(1)); // the pending debounce never fired after exit began
    }

    private static List<(string Kind, string State)> AlertSequence(IslandProcess island) =>
        [.. island.Events("alert").Select(e => (e["kind"]?.GetValue<string>() ?? "?", e["state"]?.GetValue<string>() ?? "?"))];
}
