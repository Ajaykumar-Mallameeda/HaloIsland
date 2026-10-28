using Halo.Core.Island;

namespace Halo.Core.Shell;

/// <summary>What kind of window is in the foreground, classified by the window layer.</summary>
public enum ForegroundKind
{
    /// <summary>No foreground window (for example during a switch).</summary>
    None,

    /// <summary>The desktop (Progman/WorkerW): nothing is full screen.</summary>
    Desktop,

    /// <summary>Taskbar, Start, Alt+Tab, flyouts and menus: they pass over a full-screen app without ending it.</summary>
    TransientShell,

    /// <summary>One of Halo's own windows (island, menus, settings).</summary>
    OwnProcess,

    /// <summary>Any other top-level window.</summary>
    Application,
}

/// <summary>The rule's result. <see cref="Unchanged"/> keeps the previous decision.</summary>
public enum FullscreenVerdict
{
    NotFullscreen,
    Fullscreen,
    Unchanged,
}

/// <summary>Everything the rule needs about the current foreground window, captured by the window layer.</summary>
/// <param name="NotificationState">SHQueryUserNotificationState result (QUNS_*), or 0 if the call failed.</param>
/// <param name="Kind">Foreground window class.</param>
/// <param name="Window">Foreground window rectangle in physical pixels.</param>
/// <param name="Monitor">Full rectangle (not the work area) of the island's monitor.</param>
/// <param name="OnIslandMonitor">True when the foreground window is on the island's monitor.</param>
/// <param name="Minimized">The foreground window is minimised.</param>
/// <param name="MaximizedWithCaption">The window is maximised and still has a title bar: an ordinary maximised window.</param>
/// <param name="Cloaked">DWM reports the window as cloaked (not actually shown).</param>
public readonly record struct FullscreenProbe(
    int NotificationState,
    ForegroundKind Kind,
    PixelRect Window,
    PixelRect Monitor,
    bool OnIslandMonitor,
    bool Minimized,
    bool MaximizedWithCaption,
    bool Cloaked);

/// <summary>
/// FR-ISL-09 full-screen rule (ADR-001 §4.9). Windows sends no "full screen started" notification, so the window
/// layer captures a <see cref="FullscreenProbe"/> after foreground, location and shell events, and this pure function
/// decides. A window counts as full screen when it is an application window on the island's monitor, visible, and its
/// rectangle covers the whole monitor (taskbar included) without being an ordinary maximised window with a title bar.
/// Exclusive Direct3D full screen and presentation mode count regardless of geometry. Shell overlays and Halo's own
/// windows keep the previous decision so that Alt+Tab, Start or a tray menu over a game does not flash the island.
/// </summary>
public static class FullscreenRule
{
    /// <summary>QUNS_RUNNING_D3D_FULL_SCREEN.</summary>
    public const int QunsRunningD3DFullScreen = 3;

    /// <summary>QUNS_PRESENTATION_MODE.</summary>
    public const int QunsPresentationMode = 4;

    public static FullscreenVerdict Evaluate(FullscreenProbe probe)
    {
        if (probe.NotificationState is QunsRunningD3DFullScreen or QunsPresentationMode)
        {
            return FullscreenVerdict.Fullscreen;
        }

        switch (probe.Kind)
        {
            case ForegroundKind.TransientShell or ForegroundKind.OwnProcess or ForegroundKind.None:
                return FullscreenVerdict.Unchanged;
            case ForegroundKind.Desktop:
                return FullscreenVerdict.NotFullscreen;
        }

        if (!probe.OnIslandMonitor || probe.Minimized || probe.Cloaked || probe.MaximizedWithCaption)
        {
            return FullscreenVerdict.NotFullscreen;
        }

        var w = probe.Window;
        var m = probe.Monitor;
        var covers = w.Left <= m.Left && w.Top <= m.Top && w.Right >= m.Right && w.Bottom >= m.Bottom;
        return covers ? FullscreenVerdict.Fullscreen : FullscreenVerdict.NotFullscreen;
    }
}

/// <summary>Maps a foreground window's class name to a <see cref="ForegroundKind"/> (shell class names, Windows 11).</summary>
public static class ForegroundClassifier
{
    private static readonly HashSet<string> Desktop = new(StringComparer.Ordinal) { "Progman", "WorkerW" };

    private static readonly HashSet<string> TransientShell = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "TopLevelWindowForOverflowXamlIsland",
        "NotifyIconOverflowWindow",
        "XamlExplorerHostIslandWindow",
        "Windows.UI.Core.CoreWindow",
        "MultitaskingViewFrame",
        "TaskSwitcherWnd",
        "ForegroundStaging",
        "LockScreenControllerProxyWindow",
        "#32768",
    };

    public static ForegroundKind Classify(nint hwnd, bool ownProcess, string className)
    {
        if (hwnd == 0)
        {
            return ForegroundKind.None;
        }

        if (ownProcess)
        {
            return ForegroundKind.OwnProcess;
        }

        if (Desktop.Contains(className))
        {
            return ForegroundKind.Desktop;
        }

        return TransientShell.Contains(className) ? ForegroundKind.TransientShell : ForegroundKind.Application;
    }
}
