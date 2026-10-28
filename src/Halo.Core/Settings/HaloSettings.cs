using System.Text;
using System.Text.Json;

namespace Halo.Core.Settings;

/// <summary>
/// User preferences that exist in the app today (M2-F01). Only settings with a real effect are here; indicator
/// switches arrive with their features. Numbers have documented ranges; out-of-range values fall back to the default.
/// </summary>
public sealed record HaloSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int MinDwellMs = 0;
    public const int MaxDwellMs = 500;
    public const int MinGraceMs = 100;
    public const int MaxGraceMs = 2000;
    public const int MinBatteryLow = 5;
    public const int MaxBatteryLow = 50;
    public const int MinBatteryCritical = 2;
    public const int MaxBatteryCritical = 25;

    public static HaloSettings Defaults { get; } = new();

    /// <summary>Expand when the pointer rests on the pill (FR-ISL-03). Off = click only (FR-ISL-04).</summary>
    public bool HoverToExpand { get; init; } = true;

    /// <summary>Hover dwell in ms, 0–500 (FR-ISL-03).</summary>
    public int HoverDwellMs { get; init; } = 120;

    /// <summary>Leave grace in ms, 100–2000.</summary>
    public int LeaveGraceMs { get; init; } = 400;

    /// <summary>Open the island (pinned) when Halo starts.</summary>
    public bool StartExpanded { get; init; }

    /// <summary>Use instant transitions even when Windows animations are on (FR-ISL-13 in-app setting).</summary>
    public bool ReduceMotion { get; init; }

    /// <summary>Hide while a full-screen app is in front (FR-ISL-09 "user can disable").</summary>
    public bool HideInFullscreen { get; init; } = true;

    /// <summary>Announce transient alerts to screen readers (FR-A11Y-02 "announcements can be turned off").</summary>
    public bool AnnounceAlerts { get; init; } = true;

    /// <summary>Show what is playing (media sessions, FR-MED-01). Off: Halo does not open the media session manager.</summary>
    public bool ShowMedia { get; init; } = true;

    /// <summary>Volume pill on changes and the volume row in the expanded island (FR-SYS-01, M3-F03).</summary>
    public bool ShowVolume { get; init; } = true;

    /// <summary>Battery level in the open island and charger / low-battery alerts (FR-SYS-02/03, M3-F04).</summary>
    public bool ShowBattery { get; init; } = true;

    /// <summary>Low-battery alert threshold in percent, 5–50 (FR-SYS-03).</summary>
    public int BatteryLowPercent { get; init; } = 20;

    /// <summary>Critical-battery alert threshold in percent, 2–25, and always below the low threshold.</summary>
    public int BatteryCriticalPercent { get; init; } = 10;

    /// <summary>Serialises to the versioned JSON file format (indented, stable key order).</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", CurrentSchemaVersion);
            w.WriteBoolean("hoverToExpand", HoverToExpand);
            w.WriteNumber("hoverDwellMs", HoverDwellMs);
            w.WriteNumber("leaveGraceMs", LeaveGraceMs);
            w.WriteBoolean("startExpanded", StartExpanded);
            w.WriteBoolean("reduceMotion", ReduceMotion);
            w.WriteBoolean("hideInFullscreen", HideInFullscreen);
            w.WriteBoolean("announceAlerts", AnnounceAlerts);
            w.WriteBoolean("showMedia", ShowMedia);
            w.WriteBoolean("showVolume", ShowVolume);
            w.WriteBoolean("showBattery", ShowBattery);
            w.WriteNumber("batteryLowPercent", BatteryLowPercent);
            w.WriteNumber("batteryCriticalPercent", BatteryCriticalPercent);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Parses settings JSON. Never trusts the file: each field is type- and range-checked on its own, and a bad field
    /// takes its default while the rest is kept. Returns false only when the text is not a JSON object at all.
    /// </summary>
    public static bool TryParse(string json, out HaloSettings settings, out int schemaVersion, out List<string> issues)
    {
        settings = Defaults;
        schemaVersion = 0;
        issues = [];
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false });
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            schemaVersion = root.TryGetProperty("schemaVersion", out var v) && v.TryGetInt32(out var n) ? n : 0;
            var d = Defaults;
            var list = issues;
            bool Bool(string name, bool fallback)
            {
                if (!root.TryGetProperty(name, out var e))
                {
                    return fallback;
                }

                if (e.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return e.GetBoolean();
                }

                list.Add(name);
                return fallback;
            }

            int Int(string name, int fallback, int min, int max)
            {
                if (!root.TryGetProperty(name, out var e))
                {
                    return fallback;
                }

                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var value) && value >= min && value <= max)
                {
                    return value;
                }

                list.Add(name);
                return fallback;
            }

            settings = new HaloSettings
            {
                HoverToExpand = Bool("hoverToExpand", d.HoverToExpand),
                HoverDwellMs = Int("hoverDwellMs", d.HoverDwellMs, MinDwellMs, MaxDwellMs),
                LeaveGraceMs = Int("leaveGraceMs", d.LeaveGraceMs, MinGraceMs, MaxGraceMs),
                StartExpanded = Bool("startExpanded", d.StartExpanded),
                ReduceMotion = Bool("reduceMotion", d.ReduceMotion),
                HideInFullscreen = Bool("hideInFullscreen", d.HideInFullscreen),
                AnnounceAlerts = Bool("announceAlerts", d.AnnounceAlerts),
                ShowMedia = Bool("showMedia", d.ShowMedia),
                ShowVolume = Bool("showVolume", d.ShowVolume),
                ShowBattery = Bool("showBattery", d.ShowBattery),
                BatteryLowPercent = Int("batteryLowPercent", d.BatteryLowPercent, MinBatteryLow, MaxBatteryLow),
                BatteryCriticalPercent = Int("batteryCriticalPercent", d.BatteryCriticalPercent, MinBatteryCritical, MaxBatteryCritical),
            };

            // Critical must stay below low; otherwise it falls back (to the default, or just under low).
            if (settings.BatteryCriticalPercent >= settings.BatteryLowPercent)
            {
                list.Add("batteryCriticalPercent");
                settings = settings with { BatteryCriticalPercent = Math.Min(d.BatteryCriticalPercent, settings.BatteryLowPercent - 1) };
            }

            return true;
        }
    }
}
