using System.Globalization;
using Halo.Core.Island;

namespace Halo.Core.Power;

/// <summary>Where power comes from, as Windows reports it.</summary>
public enum PowerSource
{
    Unknown,
    Ac,
    Battery,
}

/// <summary>
/// One power reading (FR-SYS-02/03), built from <c>GetSystemPowerStatus</c>. A machine without a battery has
/// <see cref="HasBattery"/> false and nothing is shown or alerted. An unknown charge (255) is <see cref="Percent"/> null.
/// </summary>
public readonly record struct PowerReading(bool HasBattery, int? Percent, PowerSource Source, bool Charging)
{
    private const byte NoSystemBattery = 128;
    private const byte UnknownStatus = 255;
    private const byte ChargingFlag = 8;

    /// <summary>Maps SYSTEM_POWER_STATUS fields: ACLineStatus, BatteryFlag and BatteryLifePercent.</summary>
    public static PowerReading FromSystem(byte acLineStatus, byte batteryFlag, byte lifePercent)
    {
        var source = acLineStatus switch
        {
            0 => PowerSource.Battery,
            1 => PowerSource.Ac,
            _ => PowerSource.Unknown,
        };
        var hasBattery = batteryFlag != UnknownStatus && (batteryFlag & NoSystemBattery) == 0;
        int? percent = hasBattery && lifePercent <= 100 ? lifePercent : null;
        var charging = hasBattery && batteryFlag != UnknownStatus && (batteryFlag & ChargingFlag) != 0;
        return new PowerReading(hasBattery, percent, source, charging);
    }

    /// <summary>Battery glyph (Segoe Fluent Icons): 0–10 bars, the charging set while plugged in, or "unknown".</summary>
    public string Glyph
    {
        get
        {
            if (Percent is not { } p)
            {
                return "";
            }

            var step = (int)Math.Round(p / 10.0, MidpointRounding.AwayFromZero);
            if (Source == PowerSource.Ac)
            {
                return step switch
                {
                    >= 10 => "",
                    9 => "",
                    _ => ((char)(0xE85A + step)).ToString(),
                };
            }

            return step >= 10 ? "" : ((char)(0xE850 + step)).ToString();
        }
    }

    /// <summary>Short status for the open island, for example "82%", or empty when unknown.</summary>
    public string ShortText => Percent is { } p ? string.Create(CultureInfo.CurrentCulture, $"{p}%") : string.Empty;

    /// <summary>What a screen reader hears, for example "Battery 82 percent, charging".</summary>
    public string Spoken
    {
        get
        {
            var level = Percent is { } p ? string.Create(CultureInfo.InvariantCulture, $"Battery {p} percent") : "Battery level unknown";
            return Source switch
            {
                PowerSource.Ac when Charging => $"{level}, charging",
                PowerSource.Ac => $"{level}, plugged in",
                _ => level,
            };
        }
    }
}

/// <summary>Low and critical thresholds in percent (FR-SYS-03: 20 and 10 by default, configurable).</summary>
public sealed record BatteryThresholds(int Low, int Critical)
{
    public static BatteryThresholds Default { get; } = new(20, 10);
}

/// <summary>
/// Decides which power alerts to raise (FR-SYS-02/03). Pure and event-driven: <see cref="Update"/> is called with
/// each new reading and returns the alert to raise, or null.
/// <list type="bullet">
/// <item>Charger connected or disconnected: one Normal "power" alert per change of source.</item>
/// <item>Discharging at or below the low threshold: one High alert; at or below critical: one Critical alert that may
/// appear over full screen. Each fires once per discharge; it is re-armed only after charging starts or the level
/// climbs 2 points above the threshold, so a level wobbling around the line does not repeat it.</item>
/// <item>Unplugging below a threshold shows the battery alert instead of "On battery".</item>
/// <item>The first reading (start-up) only alerts when already low or critical on battery.</item>
/// <item>No battery or an unknown level: no alerts.</item>
/// </list>
/// </summary>
public sealed class BatteryMonitor(BatteryThresholds thresholds)
{
    public const string PowerKey = "power";
    public const string BatteryKey = "battery";
    private const int Hysteresis = 2;

    private bool _lowShown;
    private bool _criticalShown;

    public BatteryThresholds Thresholds { get; set; } = thresholds ?? throw new ArgumentNullException(nameof(thresholds));

    public PowerReading? Current { get; private set; }

    public IslandAlert? Update(PowerReading next)
    {
        var previous = Current;
        Current = next;
        if (!next.HasBattery)
        {
            return null;
        }

        if (next.Source == PowerSource.Ac)
        {
            (_lowShown, _criticalShown) = (false, false);
        }
        else if (next.Percent is { } p)
        {
            _lowShown &= p <= Thresholds.Low + Hysteresis;
            _criticalShown &= p <= Thresholds.Critical + Hysteresis;
        }

        var sourceChanged = previous is { HasBattery: true } before && before.Source != PowerSource.Unknown && next.Source != PowerSource.Unknown && before.Source != next.Source;
        var level = LevelAlert(next);
        if (level is not null)
        {
            return level;
        }

        if (sourceChanged)
        {
            return next.Source == PowerSource.Ac
                ? Power(next, next.Charging || next.Percent is null or < 100 ? "Charging" : "Plugged in")
                : Power(next, "On battery");
        }

        return null;
    }

    private IslandAlert? LevelAlert(PowerReading r)
    {
        if (r.Source != PowerSource.Battery || r.Percent is not { } p)
        {
            return null;
        }

        if (p <= Thresholds.Critical && !_criticalShown)
        {
            (_criticalShown, _lowShown) = (true, true);
            return new IslandAlert(BatteryKey, AlertKind.Battery, AlertPriority.Critical, Content(r, "Battery critical"), TimeSpan.FromSeconds(8), TimeSpan.FromMinutes(1), AllowedWhenSuppressed: true);
        }

        if (p <= Thresholds.Low && !_lowShown)
        {
            _lowShown = true;
            return new IslandAlert(BatteryKey, AlertKind.Battery, AlertPriority.High, Content(r, "Battery low"), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
        }

        return null;
    }

    private static IslandAlert Power(PowerReading r, string text) =>
        new(PowerKey, AlertKind.Power, AlertPriority.Normal, Content(r, text), TimeSpan.FromMilliseconds(2500), TimeSpan.FromSeconds(3));

    private static AlertContent Content(PowerReading r, string text) =>
        r.Percent is { } p
            ? new AlertContent(r.Glyph, text, p / 100.0, p.ToString(CultureInfo.InvariantCulture)) { Accessible = string.Create(CultureInfo.InvariantCulture, $"{text}, {p} percent") }
            : new AlertContent(r.Glyph, text);
}
