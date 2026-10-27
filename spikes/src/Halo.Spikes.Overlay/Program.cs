using System.Diagnostics;
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
    private static readonly HashSet<Technique> Implemented = [Technique.A, Technique.B];

    [STAThread]
    public static int Main(string[] args)
    {
        var options = OverlayOptions.Parse(args, Implemented);
        if (options.Mode == OverlayMode.Invalid)
        {
            Console.Error.WriteLine(OverlayOptions.Usage);
            return ExitUsage;
        }

        using var sink = OpenLogSink(options);
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
            var env = LogEnvironment(log);
            return options.Mode == OverlayMode.Smoke ? RunSmoke(log) : RunOverlay(options, log, env.PrimaryRefreshHz);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            log.WriteError("spike.failed", ex);
            return ExitError;
        }
    }

    private static TextWriter OpenLogSink(OverlayOptions options)
    {
        if (options.LogPath is { } path)
        {
            return TextWriter.Synchronized(new StreamWriter(path, append: false) { AutoFlush = true });
        }

        // WinExe has no console. Smoke logs to stderr (M0 behaviour); a run logs to stdout, which the harness reads.
        var stream = options.Mode == OverlayMode.Smoke ? Console.OpenStandardError() : Console.OpenStandardOutput();
        return TextWriter.Synchronized(new StreamWriter(stream) { AutoFlush = true });
    }

    private static EnvironmentSnapshot LogEnvironment(SpikeLog log)
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
        return env;
    }

    private static int RunOverlay(OverlayOptions options, SpikeLog log, int refreshHz)
    {
        var desiredFps = refreshHz > 0 ? refreshHz : 60;
        log.Write(
            "overlay.config",
            LogField.Tok("technique", options.Technique.ToString()),
            LogField.Num("calibration", options.Calibration ? 1 : 0),
            LogField.Num("stall_ms", options.StallMs),
            LogField.Num("tick_ms", options.TickMs),
            LogField.Num("continuous", options.Continuous ? 1 : 0),
            LogField.Num("desired_fps", desiredFps),
            LogField.Num("pid", Environment.ProcessId),
            LogField.Num("qpc_frequency", Stopwatch.Frequency));

        var exitCode = ExitOk;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            log.WriteError("dispatcher.unhandled_exception", e.Exception);
            exitCode = ExitError;
            e.Handled = true;
            app.Shutdown(ExitError);
        };

        var window = new OverlayWindow(options.Technique, options, log, desiredFps);
        var reader = new Thread(() => ReadCommands(app.Dispatcher, window, log, app)) { IsBackground = true, Name = "stdin-commands" };
        window.Show();
        reader.Start();
        app.Run();
        log.Write("overlay.exit", LogField.Num("code", exitCode));
        return exitCode;
    }

    /// <summary>Blocks on standard input (no polling, no wake-ups). End of input is treated as <c>quit</c>,
    /// so an overlay never outlives its harness.</summary>
    private static void ReadCommands(Dispatcher dispatcher, OverlayWindow window, SpikeLog log, Application app)
    {
        while (true)
        {
            var line = Console.In.ReadLine();
            var command = line is null ? OverlayCommand.Quit : OverlayCommands.Parse(line);
            log.Write("cmd.received", LogField.Num("qpc", Stopwatch.GetTimestamp()), LogField.Tok("cmd", command.Token()));
            if (command == OverlayCommand.Quit)
            {
                dispatcher.BeginInvoke(() =>
                {
                    window.Close();
                    app.Shutdown(ExitOk);
                });
                return;
            }

            dispatcher.BeginInvoke(() => window.Handle(command));
        }
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
