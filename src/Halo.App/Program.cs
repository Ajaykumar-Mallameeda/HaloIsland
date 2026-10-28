using System.Runtime.InteropServices;
using Halo.App.Island;
using Halo.Core.Diagnostics;
using Halo.Core.Settings;
using static Halo.App.Interop.Native;

namespace Halo.App;

/// <summary>
/// Project Halo entry point. Arguments:
/// <c>--exit</c> closes a running island; <c>--diagnostics</c> writes privacy-safe JSON events to standard output.
/// A second launch while the island runs asks it to expand and exits (single instance, FR-APP-04). The tray icon
/// (FR-APP-01) offers Open, Collapse and Exit.
/// </summary>
internal static class Program
{
    private const string InstanceMutexName = @"Local\ProjectHalo.Island";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--exit", StringComparer.Ordinal))
        {
            var running = FindWindowW(IslandWindow.ClassName, null);
            return running != 0 && PostMessageW(running, WmClose, 0, 0) ? 0 : 1;
        }

        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            var running = FindWindowW(IslandWindow.ClassName, null);
            if (running != 0)
            {
                _ = PostMessageW(running, IslandWindow.WmCommandFromInstance, 1, 0);
            }

            return 0;
        }

        var log = args.Contains("--diagnostics", StringComparer.Ordinal) ? new DiagnosticsLog(Console.Out) : null;
        var dataDir = DataDirectory(args, log is not null);
        var fileLog = new LogFile(Path.Combine(dataDir, "logs"));

        // A last-resort crash record (type and HRESULT only); the app does not restart itself (ADR-001 §9.1).
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            fileLog.Write(LogLevel.Error, "app.crash", ("type", e.ExceptionObject.GetType().Name), ("hresult", (e.ExceptionObject as Exception)?.HResult ?? 0));
        try
        {
            using var window = new IslandWindow(log, new SettingsStore(dataDir), fileLog, log is null ? IslandFault.None : FaultFromEnvironment());
            return window.Run();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Type and HRESULT only: messages can contain paths.
            Console.Error.WriteLine($"Halo failed to start: {ex.GetType().Name} 0x{ex.HResult:X8}");
            fileLog.Write(LogLevel.Error, "app.start_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
            return 1;
        }
        finally
        {
            instance.ReleaseMutex();
        }
    }

    /// <summary>
    /// Settings folder: <c>%LOCALAPPDATA%\ProjectHalo</c>. Tests pass <c>--data-dir &lt;path&gt;</c>, honoured only with
    /// --diagnostics, so they never read or change the user's own settings.
    /// </summary>
    private static string DataDirectory(string[] args, bool diagnostics)
    {
        var i = Array.IndexOf(args, "--data-dir");
        return diagnostics && i >= 0 && i + 1 < args.Length ? args[i + 1] : SettingsStore.DefaultDirectory();
    }

    /// <summary>Desktop-test fault injection: <c>HALO_TEST_FAULT=tray|startup|audio</c>, read only with --diagnostics.</summary>
    private static IslandFault FaultFromEnvironment() => Environment.GetEnvironmentVariable("HALO_TEST_FAULT") switch
    {
        "tray" => IslandFault.Tray,
        "startup" => IslandFault.Startup,
        "audio" => IslandFault.NoAudio,
        _ => IslandFault.None,
    };
}
