namespace Halo.Core.Island;

/// <summary>Alert priority (ADR-001 §6.2). Higher replaces lower; equal replaces (the newer event wins).</summary>
public enum AlertPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3,
}

/// <summary>Source of a transient alert; also the diagnostic token.</summary>
public enum AlertKind
{
    Volume,
    Battery,
    Power,
    Network,
    Media,
    Test,
}

/// <summary>What a transient alert shows: an icon glyph, one short line and an optional 0–1 level bar.</summary>
/// <param name="Glyph">A character from the icon font (Segoe Fluent Icons), or empty.</param>
/// <param name="Text">User-visible text. Never logged.</param>
/// <param name="Level">Bar fill from 0 to 1, or null for no bar.</param>
/// <param name="Value">Short text at the end of the bar (for example "64"), or empty.</param>
public sealed record AlertContent(string Glyph, string Text, double? Level = null, string Value = "")
{
    /// <summary>What a screen reader hears instead of <see cref="Text"/> (for example "Volume 64 percent"), or null.</summary>
    public string? Accessible { get; init; }

    /// <summary>The text announced and exposed to UI Automation. Never logged.</summary>
    public string Spoken => Accessible ?? Text;

    public double? ClampedLevel => Level is { } l ? Math.Clamp(double.IsFinite(l) ? l : 0, 0, 1) : null;
}

/// <summary>
/// A transient event panel request (the "Alert" presentation). Alerts never pin the island: they disappear after
/// <see cref="Duration"/>, and an alert that waits in the queue longer than <see cref="StaleAfter"/> is dropped.
/// </summary>
/// <param name="Key">Coalescing key: a new alert with the same key updates the shown one and restarts its time.</param>
/// <param name="Kind">Source (diagnostics token).</param>
/// <param name="Priority">Arbitration priority.</param>
/// <param name="Content">What is shown.</param>
/// <param name="Duration">How long it stays once shown.</param>
/// <param name="StaleAfter">How long it may wait in the queue before it no longer matters.</param>
/// <param name="AllowedWhenSuppressed">May appear over a full-screen app (critical battery only, by default).</param>
public sealed record IslandAlert(
    string Key,
    AlertKind Kind,
    AlertPriority Priority,
    AlertContent Content,
    TimeSpan Duration,
    TimeSpan StaleAfter,
    bool AllowedWhenSuppressed = false)
{
    /// <summary>When the alert was raised (set by the controller).</summary>
    public DateTimeOffset RaisedAt { get; init; }
}
