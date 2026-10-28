using Halo.Core.Island;
using Halo.Core.Power;
using Halo.Core.Settings;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Power readings and battery alert rules (M3-F04, FR-SYS-02/03), on synthetic readings.</summary>
public class BatteryTests
{
    private static readonly int[] Discharge = [25, 22, 21, 20, 19, 18, 12, 11, 10, 9, 5, 1];

    private static PowerReading Battery(int percent) => new(true, percent, PowerSource.Battery, false);

    private static PowerReading Charging(int percent) => new(true, percent, PowerSource.Ac, percent < 100);

    [Theory]
    [InlineData(1, 8, 55, true, 55, PowerSource.Ac, true)] // plugged in, charging
    [InlineData(0, 0, 40, true, 40, PowerSource.Battery, false)]
    [InlineData(1, 1, 100, true, 100, PowerSource.Ac, false)] // full, not charging (this machine today)
    [InlineData(1, 128, 255, false, null, PowerSource.Ac, false)] // desktop: no system battery
    [InlineData(255, 255, 255, false, null, PowerSource.Unknown, false)] // status unknown
    [InlineData(0, 2, 255, true, null, PowerSource.Battery, false)] // battery, level unknown
    [InlineData(0, 4, 101, true, null, PowerSource.Battery, false)] // invalid level
    public void SystemStatusIsMapped(byte ac, byte flag, byte percent, bool hasBattery, int? level, PowerSource source, bool charging)
    {
        var r = PowerReading.FromSystem(ac, flag, percent);
        Assert.Equal(new PowerReading(hasBattery, level, source, charging), r);
    }

    [Theory]
    [InlineData(0, PowerSource.Battery, "")]
    [InlineData(54, PowerSource.Battery, "")]
    [InlineData(96, PowerSource.Battery, "")]
    [InlineData(50, PowerSource.Ac, "")]
    [InlineData(88, PowerSource.Ac, "")]
    [InlineData(100, PowerSource.Ac, "")]
    public void GlyphShowsTheLevelAndTheCharger(int percent, PowerSource source, string glyph) =>
        Assert.Equal(glyph, new PowerReading(true, percent, source, false).Glyph);

    [Fact]
    public void UnknownLevelHasTheUnknownGlyphAndNoText()
    {
        var r = new PowerReading(true, null, PowerSource.Battery, false);
        Assert.Equal("", r.Glyph);
        Assert.Equal(string.Empty, r.ShortText);
        Assert.Equal("Battery level unknown", r.Spoken);
    }

    [Fact]
    public void SpokenTextSaysChargingOrPluggedIn()
    {
        Assert.Equal("Battery 82 percent, charging", Charging(82).Spoken);
        Assert.Equal("Battery 100 percent, plugged in", Charging(100).Spoken);
        Assert.Equal("Battery 40 percent", Battery(40).Spoken);
    }

    [Fact]
    public void StartUpAlertsOnlyWhenAlreadyLowOnBattery()
    {
        Assert.Null(new BatteryMonitor(BatteryThresholds.Default).Update(Charging(100)));
        Assert.Null(new BatteryMonitor(BatteryThresholds.Default).Update(Battery(60)));

        var low = new BatteryMonitor(BatteryThresholds.Default).Update(Battery(15))!;
        Assert.Equal((AlertKind.Battery, AlertPriority.High, "Battery low"), (low.Kind, low.Priority, low.Content.Text));
        Assert.False(low.AllowedWhenSuppressed);

        var critical = new BatteryMonitor(BatteryThresholds.Default).Update(Battery(8))!;
        Assert.Equal((AlertPriority.Critical, "Battery critical"), (critical.Priority, critical.Content.Text));
        Assert.True(critical.AllowedWhenSuppressed); // the one alert allowed over a full-screen app
        Assert.Equal("Battery critical, 8 percent", critical.Content.Spoken);
    }

    [Fact]
    public void EachThresholdAlertsOncePerDischarge()
    {
        var monitor = new BatteryMonitor(BatteryThresholds.Default);
        var alerts = Discharge.Select(p => monitor.Update(Battery(p))).ToList();

        Assert.Equal(new List<string> { "Battery low", "Battery critical" }, alerts.Where(a => a is not null).Select(a => a!.Content.Text).ToList());
        Assert.NotNull(alerts[3]); // at 20 %, the threshold itself
        Assert.NotNull(alerts[8]); // at 10 %
    }

    [Fact]
    public void ALevelWobblingAroundTheThresholdDoesNotRepeat()
    {
        var monitor = new BatteryMonitor(BatteryThresholds.Default);
        Assert.NotNull(monitor.Update(Battery(20)));
        Assert.Null(monitor.Update(Battery(21)));
        Assert.Null(monitor.Update(Battery(22)));
        Assert.Null(monitor.Update(Battery(20))); // within 2 points: still the same discharge

        Assert.Null(monitor.Update(Battery(23)));
        Assert.NotNull(monitor.Update(Battery(20))); // re-armed after climbing above 22
    }

    [Fact]
    public void ChargerChangesRaiseOnePowerAlertEach()
    {
        var monitor = new BatteryMonitor(BatteryThresholds.Default);
        Assert.Null(monitor.Update(Battery(50)));

        var plugged = monitor.Update(Charging(50))!;
        Assert.Equal((BatteryMonitor.PowerKey, AlertKind.Power, AlertPriority.Normal, "Charging"), (plugged.Key, plugged.Kind, plugged.Priority, plugged.Content.Text));
        Assert.Null(monitor.Update(Charging(51))); // charging on: no alert per percent
        Assert.Equal("On battery", monitor.Update(Battery(51))!.Content.Text);
        Assert.Null(monitor.Update(Battery(51)));

        var full = new BatteryMonitor(BatteryThresholds.Default);
        full.Update(Battery(100));
        Assert.Equal("Plugged in", full.Update(new PowerReading(true, 100, PowerSource.Ac, false))!.Content.Text);
    }

    [Fact]
    public void UnpluggingWhileLowShowsTheBatteryAlertInstead()
    {
        var monitor = new BatteryMonitor(BatteryThresholds.Default);
        monitor.Update(Battery(15)); // low shown
        Assert.Equal("Charging", monitor.Update(Charging(15))!.Content.Text);

        var unplugged = monitor.Update(Battery(15))!;
        Assert.Equal("Battery low", unplugged.Content.Text); // charging re-armed it
    }

    [Fact]
    public void NoBatteryOrUnknownLevelNeverAlertsAboutTheLevel()
    {
        var desktop = new BatteryMonitor(BatteryThresholds.Default);
        Assert.Null(desktop.Update(PowerReading.FromSystem(1, 128, 255)));
        Assert.Null(desktop.Update(PowerReading.FromSystem(0, 128, 255)));

        var unknown = new BatteryMonitor(BatteryThresholds.Default);
        Assert.Null(unknown.Update(new PowerReading(true, null, PowerSource.Battery, false)));
        var plugged = unknown.Update(new PowerReading(true, null, PowerSource.Ac, true))!;
        Assert.Equal("Charging", plugged.Content.Text);
        Assert.Null(plugged.Content.Level); // no bar without a level
    }

    [Fact]
    public void ThresholdsCanChangeWhileRunning()
    {
        var monitor = new BatteryMonitor(BatteryThresholds.Default);
        Assert.Null(monitor.Update(Battery(30)));
        monitor.Thresholds = new BatteryThresholds(35, 10);
        Assert.Equal("Battery low", monitor.Update(Battery(29))!.Content.Text);
    }

    [Theory]
    [InlineData("{ \"batteryLowPercent\": 4 }", 20, 10)] // below range
    [InlineData("{ \"batteryLowPercent\": 51 }", 20, 10)] // above range
    [InlineData("{ \"batteryCriticalPercent\": 30 }", 20, 10)] // above range
    [InlineData("{ \"batteryLowPercent\": 8, \"batteryCriticalPercent\": 9 }", 8, 7)] // critical must stay below low
    [InlineData("{ \"batteryLowPercent\": 30, \"batteryCriticalPercent\": 15 }", 30, 15)]
    public void BatteryThresholdSettingsAreValidated(string json, int low, int critical)
    {
        Assert.True(HaloSettings.TryParse(json, out var s, out _, out _));
        Assert.Equal((low, critical), (s.BatteryLowPercent, s.BatteryCriticalPercent));
    }
}
