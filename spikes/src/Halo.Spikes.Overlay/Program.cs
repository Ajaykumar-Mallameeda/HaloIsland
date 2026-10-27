using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Overlay;

public static class Program
{
    public const int ExitOk = 0;
    public const int ExitError = 1;
    public const int ExitUsage = 2;
    public const int ExitTimeout = 3;

    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(10);

    [STAThread]
    public static int Main(string[] args)
    {
        var options = SpikeOptions.Parse(args);
        if (options.Mode == SpikeMode.Invalid)
        {
            Console.Error.WriteLine(SpikeOptions.Usage);
            return ExitUsage;
        }

        using var sink = OpenLogSink(options.LogPath);
        var log = new SpikeLog(sink, TimeProvider.System);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                log.WriteError("process.unhandled_exception", ex);
            }
        };

        try
        {
            LogEnvironment(log);
            return RunSmoke(log);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            log.WriteError("spike.failed", ex);
            return ExitError;
        }
    }

    private static TextWriter OpenLogSink(string? logPath)
    {
        if (logPath is null)
        {
            // WinExe has no console; stderr is only visible when the caller redirects it.
            return TextWriter.Synchronized(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }

        return TextWriter.Synchronized(new StreamWriter(logPath, append: false) { AutoFlush = true });
    }

    private static void LogEnvironment(SpikeLog log)
    {
        var env = EnvironmentSnapshot.Capture();
        log.Write(
            "env.captured",
            LogField.Tok("os", env.OsVersion),
            LogField.Tok("runtime", env.RuntimeVersion.Replace(' ', '_')),
            LogField.Num("width_px", env.PrimaryWidthPx),
            LogField.Num("height_px", env.PrimaryHeightPx),
            LogField.Num("refresh_hz", env.PrimaryRefreshHz),
            LogField.Num("dpi", env.PrimaryDpi),
            LogField.Tok("power", env.PowerSource.ToString()),
            LogField.Tok("power_overlay", env.PowerModeOverlay?.ToString("D", CultureInfo.InvariantCulture) ?? "unknown"));
    }

    private static int RunSmoke(SpikeLog log)
    {
        var exitCode = ExitTimeout;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            log.WriteError("dispatcher.unhandled_exception", e.Exception);
            exitCode = ExitError;
            e.Handled = true;
            app.Shutdown(ExitError);
        };

        var window = new SmokeWindow();
        window.ContentRendered += (_, _) =>
        {
            log.Write("smoke.rendered");
            exitCode = ExitOk;
            window.Close();
            app.Shutdown(ExitOk);
        };

        var timeout = new DispatcherTimer(SmokeTimeout, DispatcherPriority.Normal, (_, _) =>
        {
            log.Write("smoke.timeout");
            app.Shutdown(ExitTimeout);
        }, app.Dispatcher);

        window.Show();
        app.Run();
        timeout.Stop();
        return exitCode;
    }
}
