namespace Halo.Core.Timers;

/// <summary>Do Not Disturb choices (G-F03). Halo's own quiet mode; Windows' Focus/Do Not Disturb setting is never changed.</summary>
public enum QuietKind
{
    Off,
    OneHour,
    UntilTomorrow,
    UntilTurnedOff,
}

/// <summary>
/// The quiet-mode schedule. <see cref="UntilUtc"/> is a wall-clock instant because it must survive a restart and mean
/// "the end of today" for the user; it is re-evaluated when the time or time zone changes.
/// </summary>
public sealed record QuietMode(QuietKind Kind, DateTimeOffset? UntilUtc)
{
    public static QuietMode Off { get; } = new(QuietKind.Off, null);

    /// <summary>A new quiet period starting now. "Until tomorrow" ends at the next local midnight (DST-aware).</summary>
    public static QuietMode Start(QuietKind kind, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return kind switch
        {
            QuietKind.Off => Off,
            QuietKind.OneHour => new QuietMode(kind, nowUtc.AddHours(1)),
            QuietKind.UntilTomorrow => new QuietMode(kind, NextLocalMidnight(nowUtc, zone)),
            QuietKind.UntilTurnedOff => new QuietMode(kind, null),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public bool IsActive(DateTimeOffset nowUtc) => Kind != QuietKind.Off && (UntilUtc is null || nowUtc < UntilUtc);

    /// <summary>The local midnight after <paramref name="nowUtc"/>, as UTC. Skipped or repeated hours are handled by the zone.</summary>
    public static DateTimeOffset NextLocalMidnight(DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(nowUtc, zone);
        var midnight = local.Date.AddDays(1);
        // A midnight that does not exist (a DST gap at midnight in some zones) moves to the first valid minute.
        while (zone.IsInvalidTime(midnight))
        {
            midnight = midnight.AddMinutes(1);
        }

        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }
}

/// <summary>
/// What may appear while quiet mode is on, and the summary when it ends (G-F03). Precedence, highest first:
/// <list type="number">
/// <item>The user opening the island: always works, quiet or not.</item>
/// <item>Full-screen suppression: hides the island; only alerts allowed over full screen appear.</item>
/// <item>Quiet mode: only critical alerts and the completion of a timer or focus session the user started appear;
/// everything else is counted, not queued.</item>
/// </list>
/// When quiet mode ends, one summary ("3 alerts while Do Not Disturb was on") replaces the missed items, so they never
/// arrive all at once.
/// </summary>
public sealed class QuietGate
{
    private int _held;

    public QuietMode Mode { get; private set; } = QuietMode.Off;

    /// <summary>Items held back during the current quiet period.</summary>
    public int Held => _held;

    public void Set(QuietMode mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        Mode = mode;
        if (mode.Kind != QuietKind.Off)
        {
            _held = 0;
        }
    }

    public bool IsActive(DateTimeOffset nowUtc) => Mode.IsActive(nowUtc);

    /// <summary>True if an item may show now; otherwise it is counted for the summary.</summary>
    public bool Admit(DateTimeOffset nowUtc, bool allowedInQuietMode)
    {
        if (!IsActive(nowUtc) || allowedInQuietMode)
        {
            return true;
        }

        _held++;
        return false;
    }

    /// <summary>Ends quiet mode (by the user or because its time passed). Returns how many items were held.</summary>
    public int End()
    {
        var held = _held;
        _held = 0;
        Mode = QuietMode.Off;
        return held;
    }
}
