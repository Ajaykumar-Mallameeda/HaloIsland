using System.Runtime.InteropServices;
using FlaUI.UIA3;
using Halo.Core.Power;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Battery (M3-F04, FR-SYS-02/03). The start-up reading is the machine's real power state. Charger and low-battery
/// transitions cannot be produced safely by a test (no unplugging, no draining), so they are SYNTHETIC: a
/// diagnostics-only message feeds raw SYSTEM_POWER_STATUS values through the same path real notifications take.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestPower = 0x8000 + 51;

    [Fact]
    public void TheRealPowerStateIsShownAndDescribed()
    {
        Assert.True(GetSystemPowerStatus(out var status));
        var expected = PowerReading.FromSystem(status.AcLineStatus, status.BatteryFlag, status.BatteryLifePercent);
        using var island = IslandProcess.Start();

        var power = island.WaitFor("power");
        Assert.Equal("startup", power["reason"]!.GetValue<string>());
        Assert.Equal(expected.HasBattery ? 1 : 0, power["battery"]!.GetValue<int>());
        Assert.Equal(expected.Percent ?? -1, power["percent"]!.GetValue<int>());
        Assert.Equal(expected.Source.ToString().ToLowerInvariant(), power["source"]!.GetValue<string>());
        Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() is "battery" or "power"); // nothing to say at start-up

        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        var description = element.Properties.FullDescription.ValueOrDefault ?? string.Empty;
        if (expected.HasBattery)
        {
            Assert.Contains(expected.Spoken, description, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("Battery", description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SyntheticChargerAndLowBatteryEventsFollowTheAlertRules()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("power");

        // Unplugged at 50 %: "On battery" (Normal, the power key).
        SendPower(island, ac: 0, flag: 0, percent: 50);
        WaitForPower(island, 50, "battery");
        var unplugged = WaitForKindAlert(island, "power", "shown");
        Assert.Equal(1, unplugged["priority"]!.GetValue<int>());

        // Plugged in again while that pill is up: the same pill is updated, not a second one.
        SendPower(island, ac: 1, flag: 8, percent: 50);
        WaitForPower(island, 50, "ac");
        Assert.True(WaitUntil(() => island.Events("alert").Any(e => e["kind"]?.GetValue<string>() == "power" && e["state"]?.GetValue<string>() == "updated"), 2000), Alerts(island));

        // Discharging through 20 %: one High "battery" alert, replacing the Normal power pill; 19 % adds nothing.
        SendPower(island, ac: 0, flag: 0, percent: 21);
        WaitForPower(island, 21, "battery");
        SendPower(island, ac: 0, flag: 0, percent: 20);
        WaitForPower(island, 20, "battery");
        Assert.True(WaitUntil(() => island.Events("alert").Any(e => e["kind"]?.GetValue<string>() == "battery" && e["priority"]?.GetValue<int>() == 2), 2000), Alerts(island));
        SendPower(island, ac: 0, flag: 0, percent: 19);
        WaitForPower(island, 19, "battery");
        Thread.Sleep(300);
        Assert.Single(island.Events("alert"), e => e["kind"]?.GetValue<string>() == "battery" && e["state"]?.GetValue<string>() is "shown" or "replaced");

        // Critical at 10 %: priority 3.
        SendPower(island, ac: 0, flag: 0, percent: 10);
        WaitForPower(island, 10, "battery");
        Assert.True(WaitUntil(() => island.Events("alert").Any(e => e["kind"]?.GetValue<string>() == "battery" && e["priority"]?.GetValue<int>() == 3), 2000), Alerts(island));

        // A repeated identical notification is ignored entirely.
        var powerEvents = island.Events("power").Count;
        SendPower(island, ac: 0, flag: 0, percent: 10);
        Thread.Sleep(300);
        Assert.Equal(powerEvents, island.Events("power").Count);
    }

    [Fact]
    public void BatteryOffReadsNothingAndIgnoresPowerEvents()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showBattery\": false }");
        island.WaitFor("ready");
        SendPower(island, ac: 0, flag: 0, percent: 5);
        Thread.Sleep(600);
        Assert.Empty(island.Events("power"));
        Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() is "battery" or "power");
    }

    private static void SendPower(IslandProcess island, int ac, int flag, int percent) =>
        Assert.True(PostMessageW(island.Hwnd, WmTestPower, ac | (flag << 8) | (percent << 16), 0));

    private static void WaitForPower(IslandProcess island, int percent, string source) =>
        Assert.True(
            WaitUntil(() => island.Events("power").Any(e => e["percent"]?.GetValue<int>() == percent && e["source"]?.GetValue<string>() == source), 2000),
            $"no power reading {percent}/{source}; seen: {string.Join(",", island.Events("power").Select(e => $"{e["percent"]}/{e["source"]}"))}");

    private static System.Text.Json.Nodes.JsonObject WaitForKindAlert(IslandProcess island, string kind, string state)
    {
        System.Text.Json.Nodes.JsonObject? found = null;
        Assert.True(WaitUntil(() => (found = island.Events("alert").FirstOrDefault(e => e["kind"]?.GetValue<string>() == kind && e["state"]?.GetValue<string>() == state)) is not null, 2000), Alerts(island));
        return found!;
    }

    private static string Alerts(IslandProcess island) =>
        "alerts: " + string.Join(",", island.Events("alert").Select(e => $"{e["kind"]}:{e["state"]}:{e["priority"]}"));

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
