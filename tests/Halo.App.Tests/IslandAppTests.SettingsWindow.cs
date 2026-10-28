using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// The settings window (M2-F02), opened from Explorer's real tray button and driven like a user would: UI Automation
/// for the check box, the keyboard for the slider and navigation. Checks the live effect, the saved file and a restart.
/// </summary>
public sealed partial class IslandAppTests
{
    private const string HoverBox = "Expand when the pointer rests on the island";

    [Fact]
    public void SettingsWindowChangesApplyLiveArePersistedAndSurviveARestart()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var automation = new UIA3Automation();
        string saved;
        using (var island = IslandProcess.Start())
        {
            island.WaitFor("tray");
            try
            {
                // Tray menu: Open, Collapse, Settings… (Down ×3), Enter.
                OpenTrayMenu(automation);
                PressKeys(0x28, 0x28, 0x28, 0x0D);
                island.WaitFor("settings.window");
            }
            finally
            {
                CloseTrayOverflow(automation);
            }

            var window = WaitForSettingsWindow(automation);
            var native = FindWindowW("ProjectHalo.Settings", null);
            Assert.True(window is not null, $"settings window not found by UIA; native hwnd=0x{native:X} visible={IsWindowVisible(native)}; errors={string.Join(",", island.Events("error").Select(e => e["hresult"]?.ToString()))}");

            // Every control has an accessible name and role (FR-APP-02 keyboard/UIA).
            var boxes = window!.FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox));
            Assert.Equal(16, boxes.Length);
            Assert.All(boxes, b => Assert.False(string.IsNullOrWhiteSpace(b.Name)));
            var sliders = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Slider));
            Assert.Equal(2, sliders.Length);
            // The dialog's own buttons by control ID (the title bar has its own "Close" button too).
            Assert.Equal("Restore defaults", window.FindFirstDescendant(cf => cf.ByAutomationId("107"))?.Name);
            Assert.Equal("Close", window.FindFirstDescendant(cf => cf.ByAutomationId("2"))?.Name);

            // Check box: click-only mode, applied at once and saved within the 1 s debounce.
            var hover = boxes.Single(b => b.Name == HoverBox).AsCheckBox();
            Assert.Equal(ToggleState.On, hover.ToggleState);
            hover.Toggle();
            var applied = island.WaitFor("settings.applied", skip: 1);
            Assert.Equal(0, applied["hover"]!.GetValue<int>());
            Assert.Equal(1, island.WaitFor("settings.saved")["written"]!.GetValue<int>());

            // Keyboard: Tab from the check box reaches the next control; Right on the collapse slider adds 50 ms twice.
            hover.Focus();
            PressKeys(0x09); // Tab
            Assert.Equal(ControlType.Slider, automation.FocusedElement().ControlType);
            var grace = sliders[1];
            grace.Focus();
            PressKeys(0x27, 0x27); // Right ×2
            Assert.True(WaitUntil(() => island.Events("settings.applied").Any(e => e["grace_ms"]?.GetValue<int>() == 500)), island.Trace());
            Assert.True(WaitUntil(() => File.ReadAllText(Path.Combine(island.DataDir, "settings.json")).Contains("\"leaveGraceMs\": 500", StringComparison.Ordinal), 4000), "the change was not saved");

            // Live effect: hovering no longer opens the island.
            MoveTo(pointer.PillX, pointer.PillY);
            Thread.Sleep(600);
            Assert.Equal(0, island.Count("transition.start"));
            MoveTo(pointer.PillX, pointer.OutsideY);

            // Esc closes the window; the island keeps running.
            window.Focus();
            PressKeys(0x1B);
            Assert.True(WaitUntil(() => WaitForSettingsWindow(automation, 200) is null), "Esc did not close the settings window");
            Assert.False(island.HasExited);
            saved = File.ReadAllText(Path.Combine(island.DataDir, "settings.json"));
        }

        // A new island reading the saved file starts with the changed settings.
        using var restarted = IslandProcess.Start(settingsJson: saved);
        Assert.Equal("loaded", restarted.WaitFor("settings")["status"]!.GetValue<string>());
        var after = restarted.WaitFor("settings.applied");
        Assert.Equal(0, after["hover"]!.GetValue<int>());
        Assert.Equal(500, after["grace_ms"]!.GetValue<int>());
    }

    private static AutomationElement? WaitForSettingsWindow(UIA3Automation automation, int timeoutMs = 5000)
    {
        AutomationElement? found = null;
        _ = WaitUntil(() => (found = automation.GetDesktop().FindFirstChild(cf => cf.ByName("Halo settings"))) is not null, timeoutMs);
        return found;
    }
}
