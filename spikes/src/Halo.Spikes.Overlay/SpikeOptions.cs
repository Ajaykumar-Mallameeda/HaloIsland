namespace Halo.Spikes.Overlay;

public enum SpikeMode
{
    /// <summary>Unrecognised arguments; print usage and exit with code 2.</summary>
    Invalid,

    /// <summary>Show a small window, wait for its first render, exit 0. Verifies the WPF toolchain.</summary>
    Smoke,
}

/// <summary>Command-line options. Parsing is pure so it can be unit-tested without a desktop.</summary>
public sealed record SpikeOptions(SpikeMode Mode, string? LogPath)
{
    public const string Usage = "Usage: Halo.Spikes.Overlay --smoke [--log <file>]";

    public static SpikeOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var mode = SpikeMode.Invalid;
        string? logPath = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--smoke" when mode == SpikeMode.Invalid:
                    mode = SpikeMode.Smoke;
                    break;
                case "--log" when logPath is null && i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal):
                    logPath = args[++i];
                    break;
                default:
                    return new SpikeOptions(SpikeMode.Invalid, null);
            }
        }

        return new SpikeOptions(mode, logPath);
    }
}
