using System.Globalization;

namespace Halo.Spikes.Measurement;

public enum OverlayMode
{
    /// <summary>Unrecognised or contradictory arguments; print usage and exit with code 2.</summary>
    Invalid,

    /// <summary>Show a small window, wait for its first render, exit 0 (M0 toolchain check).</summary>
    Smoke,

    /// <summary>Run a technique and obey standard-input commands until <c>quit</c>.</summary>
    Run,
}

/// <summary>Transparency techniques from ADR-001 §4.5.</summary>
public enum Technique
{
    A,
    B,
    C,
}

/// <summary>
/// Command-line options shared by the WPF overlay (A/B) and the composition baseline (C). Calibration-only
/// features (stall, tick, continuous) are rejected unless <c>--calibration</c> is also given, so a gated run cannot
/// enable them by accident (PROTOTYPE_S1_S2_REPORT §8.7 containment).
/// </summary>
public sealed record OverlayOptions(
    OverlayMode Mode,
    Technique Technique,
    string? LogPath,
    bool Calibration,
    int StallMs,
    int TickMs,
    bool Continuous)
{
    public const string Usage =
        "Usage: --smoke [--log <file>] | --technique A|B|C [--log <file>] " +
        "[--calibration [--stall-ms <1-1000>] [--tick-ms <100-60000>] [--continuous]]";

    public static readonly OverlayOptions Invalid = new(OverlayMode.Invalid, Technique.A, null, false, 0, 0, false);

    /// <param name="allowed">Techniques the executable implements.</param>
    public static OverlayOptions Parse(IReadOnlyList<string> args, IReadOnlySet<Technique> allowed)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(allowed);
        bool smoke = false, calibration = false, continuous = false;
        Technique? technique = null;
        string? logPath = null;
        int? stall = null, tick = null;

        for (var i = 0; i < args.Count; i++)
        {
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            switch (args[i])
            {
                case "--smoke" when !smoke:
                    smoke = true;
                    break;
                case "--calibration" when !calibration:
                    calibration = true;
                    break;
                case "--continuous" when !continuous:
                    continuous = true;
                    break;
                case "--technique" when technique is null && hasValue && args[i + 1] is "A" or "B" or "C":
                    technique = Enum.Parse<Technique>(args[i + 1]);
                    i++;
                    break;
                case "--log" when logPath is null && hasValue:
                    logPath = args[++i];
                    break;
                case "--stall-ms" when stall is null && hasValue && TryInt(args[i + 1], 1, 1000, out var s):
                    stall = s;
                    i++;
                    break;
                case "--tick-ms" when tick is null && hasValue && TryInt(args[i + 1], 100, 60000, out var k):
                    tick = k;
                    i++;
                    break;
                default:
                    return Invalid;
            }
        }

        var calibrationFeature = stall is not null || tick is not null || continuous;
        if (smoke)
        {
            return technique is null && !calibration && !calibrationFeature
                ? new OverlayOptions(OverlayMode.Smoke, Technique.A, logPath, false, 0, 0, false)
                : Invalid;
        }

        if (technique is not { } chosen || !allowed.Contains(chosen) || (calibrationFeature && !calibration))
        {
            return Invalid;
        }

        // A UI-thread stall cannot affect C, whose animations run in DWM; reject it rather than silently ignore it.
        if (stall is not null && chosen == Technique.C)
        {
            return Invalid;
        }

        return new OverlayOptions(OverlayMode.Run, chosen, logPath, calibration, stall ?? 0, tick ?? 0, continuous);
    }

    private static bool TryInt(string text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
}

public enum OverlayCommand
{
    Unknown,
    Expand,
    Collapse,
    Suppress,
    Restore,
    HoverOn,
    HoverOff,
    Ping,
    Quit,
}

public static class OverlayCommands
{
    public static OverlayCommand Parse(string? line) => line?.Trim() switch
    {
        "expand" => OverlayCommand.Expand,
        "collapse" => OverlayCommand.Collapse,
        "suppress" => OverlayCommand.Suppress,
        "restore" => OverlayCommand.Restore,
        "hover-on" => OverlayCommand.HoverOn,
        "hover-off" => OverlayCommand.HoverOff,
        "ping" => OverlayCommand.Ping,
        "quit" => OverlayCommand.Quit,
        _ => OverlayCommand.Unknown,
    };

    public static string ToWire(this OverlayCommand command) => command switch
    {
        OverlayCommand.Expand => "expand",
        OverlayCommand.Collapse => "collapse",
        OverlayCommand.Suppress => "suppress",
        OverlayCommand.Restore => "restore",
        OverlayCommand.HoverOn => "hover-on",
        OverlayCommand.HoverOff => "hover-off",
        OverlayCommand.Ping => "ping",
        OverlayCommand.Quit => "quit",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    /// <summary>Short token used in logs.</summary>
    public static string Token(this OverlayCommand command) => command == OverlayCommand.Unknown ? "unknown" : command.ToWire();
}
