using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FlaUI.UIA3;
using Halo.Core.Island;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Harness;

/// <summary>
/// SMOKE check of the real island (src/Halo.App), not a gated S1 run: startup, settled idle (frames, CPU, memory),
/// 20 UIA-driven transitions with Desktop Duplication frame intervals, screenshots over a grey backdrop, and exit.
/// Same instruments as S1, shorter windows and no repeat sessions, so results are informational only.
/// </summary>
internal static partial class AppCheck
{
    private const int Launches = 6;
    private const int Transitions = 20;
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(90);

    public static string AppExe => Path.Combine(Paths.RepositoryRoot, "src", "Halo.App", "bin", "Release", "net10.0-windows10.0.19041.0", "Halo.exe");

    public static JsonObject Run(string runId, string date)
    {
        var artifacts = new RunArtifacts(runId, date);
        var validity = new Validity();
        var env = RunEnvironment.Capture();
        var hz = env.Snapshot.PrimaryRefreshHz;
        env.Check(validity, hz, "start");
        // The island's canvas with its default settings (the volume row is on by default since M3-F03), so the backdrop
        // and the capture cover the whole island and nothing behind it is counted.
        var layout = IslandLayout.Compute(new PixelRect(env.WorkArea.Left, env.WorkArea.Top, env.WorkArea.Width, env.WorkArea.Height), env.Dpi, Halo.Core.Settings.HaloSettings.Defaults.ShowVolume);
        var canvas = new PxRect(layout.Canvas.Left, layout.Canvas.Top, layout.Canvas.Width, layout.Canvas.Height);
        var refreshMs = 1000.0 / hz;
        var lastInput = Win32.LastInputTick();

        using var ui = new UiThread();
        var backdrop = new Backdrop(ui, canvas);
        WindowChecks.ParkCursor(canvas);
        using var capture = DesktopDuplication.Create(canvas);
        capture.Start();
        List<FrameRecord> Frames() => capture.Frames().Where(f => f.Attributed).Select(f => new FrameRecord(f.Qpc, f.AccumulatedFrames > 1)).OrderBy(f => f.Qpc).ToList();

        // 1. Warm startup and exit, first launch discarded.
        var toReady = new List<double>();
        var toFrame = new List<double>();
        var exitMs = new List<double>();
        var orphans = 0;
        for (var i = 0; i < Launches; i++)
        {
            using var island = Island.Start();
            var ready = island.WaitFor("ready", TimeSpan.FromSeconds(15));
            Thread.Sleep(1500);
            var first = Frames().FirstOrDefault(f => f.Qpc > island.StartQpc);
            var exited = island.ExitViaSecondLaunch();
            if (i > 0)
            {
                toReady.Add(ready is null ? double.NaN : FrameTimeline.ToMs(ready.Value.Qpc - island.StartQpc, Stopwatch.Frequency));
                toFrame.Add(first.Qpc == 0 ? double.NaN : FrameTimeline.ToMs(first.Qpc - island.StartQpc, Stopwatch.Frequency));
                exitMs.Add(exited.TotalMilliseconds);
            }

            orphans += Process.GetProcessesByName("Halo").Length;
            Thread.Sleep(1500);
        }

        // 2. One session: settled idle, screenshots, transitions.
        using var main = Island.Start();
        main.WaitFor("ready", TimeSpan.FromSeconds(15));
        Thread.Sleep(2000);
        Png.Save(ScreenRegion.Capture(canvas), artifacts.File("compact.png"));
        var proc = Process.GetProcessById(main.Pid);
        proc.Refresh();
        var cpu0 = proc.TotalProcessorTime;
        var idleStart = Stopwatch.GetTimestamp();
        Thread.Sleep(Idle);
        var idleEnd = Stopwatch.GetTimestamp();
        proc.Refresh();
        var idleCpu = proc.TotalProcessorTime - cpu0;
        var idleFrames = FrameTimeline.CountInWindow(Frames(), idleStart, idleEnd);
        var clockEvents = main.Count("clock");
        var privateMb = proc.PrivateMemorySize64 / 1_000_000.0;
        var workingSetMb = proc.WorkingSet64 / 1_000_000.0;

        using var automation = new UIA3Automation();
        var pattern = automation.FromHandle(main.Hwnd).Patterns.ExpandCollapse.Pattern;
        var marks = new List<TransitionMark>();
        for (var i = 0; i < Transitions; i++)
        {
            var expand = i % 2 == 0;
            var startsBefore = main.Count("transition.start");
            var endsBefore = main.Count("transition.end");
            if (expand)
            {
                pattern.Expand();
            }
            else
            {
                pattern.Collapse();
            }

            var start = main.WaitFor("transition.start", TimeSpan.FromSeconds(5), startsBefore);
            var end = main.WaitFor("transition.end", TimeSpan.FromSeconds(5), endsBefore);
            if (start is { } s && end is { } e)
            {
                marks.Add(new TransitionMark(s.Qpc, e.Qpc));
            }

            Thread.Sleep(1200);
            if (i == 0)
            {
                Png.Save(ScreenRegion.Capture(canvas), artifacts.File("expanded.png"));
            }
        }

        var frames = Frames();
        var perTransition = marks.Select(m => FrameTimeline.ForTransition(frames, m, Stopwatch.Frequency, refreshMs)).ToList();
        var mainExit = main.ExitViaSecondLaunch();
        capture.Stop();
        backdrop.Close();
        Thread.Sleep(500);

        validity.Require(capture.ErrorHResult == 0 && !capture.AccessLost, "duplication_error");
        validity.Require(Win32.LastInputTick() == lastInput, "user_input_during_run");
        RunEnvironment.Capture().Check(validity, hz, "end");
        var intervals = perTransition.SelectMany(t => t.IntervalsMs).ToList();
        var summary = new JsonObject
        {
            ["run_id"] = runId,
            ["kind"] = "app-smoke",
            ["gated"] = false,
            ["refresh_hz"] = hz,
            ["canvas"] = $"{canvas.Left},{canvas.Top},{canvas.Width},{canvas.Height}",
            ["validity"] = new JsonObject { ["valid"] = validity.IsValid, ["violations"] = new JsonArray(validity.Violations.Select(v => (JsonNode)v).ToArray()) },
            ["startup_to_ready_p50_ms"] = Statistics.Median(toReady),
            ["startup_to_first_frame_p50_ms"] = toFrame.All(double.IsFinite) ? Statistics.Median(toFrame) : null,
            ["startup_to_first_frame_ms"] = string.Join(";", toFrame.Select(RunArtifacts.F)),
            ["exit_p50_ms"] = Statistics.Median(exitMs),
            ["exit_max_ms"] = exitMs.Max(),
            ["halo_processes_after_each_exit"] = orphans,
            ["idle_window_s"] = Idle.TotalSeconds,
            ["idle_cpu_ms"] = idleCpu.TotalMilliseconds,
            ["idle_attributed_frames"] = idleFrames,
            ["idle_clock_updates_during_session"] = clockEvents,
            ["private_mb"] = privateMb,
            ["working_set_mb"] = workingSetMb,
            ["transitions_marked"] = marks.Count,
            ["transitions_valid"] = perTransition.Count(t => t.Valid),
            ["transitions_invalid_reasons"] = string.Join(";", perTransition.Where(t => !t.Valid).GroupBy(t => t.InvalidReason).Select(g => $"{g.Key}:{g.Count()}")),
            ["coalesced_frames_in_transitions"] = marks.Sum(m => frames.Count(f => f.Coalesced && f.Qpc > m.StartQpc && f.Qpc <= m.EndQpc)),
            ["transitions_with_long_frame"] = perTransition.Count(t => t.IntervalsMs.Any(v => v > 1.5 * refreshMs)),
            ["interval_p95_ms"] = intervals.Count > 0 ? Statistics.Percentile(intervals, 95) : null,
            ["interval_max_ms"] = intervals.Count > 0 ? intervals.Max() : null,
            ["median_frames_per_transition"] = perTransition.Count > 0 ? Statistics.Median(perTransition.Select(t => (double)t.FrameCount).ToList()) : null,
            ["final_exit_ms"] = mainExit.TotalMilliseconds,
            ["halo_processes_at_end"] = Process.GetProcessesByName("Halo").Length,
        };
        artifacts.WriteJson("summary.json", summary);
        return summary;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string className, string? windowName);

    /// <summary>Halo.exe --diagnostics with its JSON events collected.</summary>
    private sealed class Island : IDisposable
    {
        private readonly Process _process;
        private readonly ConcurrentQueue<(string Name, long Qpc)> _events = new();

        private Island(Process process, long startQpc)
        {
            _process = process;
            StartQpc = startQpc;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } line && JsonNode.Parse(line) is JsonObject o)
                {
                    _events.Enqueue((o["event"]!.GetValue<string>(), o["qpc"]!.GetValue<long>()));
                }
            };
            _process.BeginOutputReadLine();
        }

        public long StartQpc { get; }

        public int Pid => _process.Id;

        public nint Hwnd { get; private set; }

        public static Island Start()
        {
            var start = Stopwatch.GetTimestamp();
            var process = Process.Start(new ProcessStartInfo(AppExe, "--diagnostics") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })
                ?? throw new InvalidOperationException("Halo did not start.");
            var island = new Island(process, start);
            _ = island.WaitFor("ready", TimeSpan.FromSeconds(15));
            island.Hwnd = FindWindowW("ProjectHalo.Island", null);
            return island;
        }

        public int Count(string name) => _events.Count(e => e.Name == name);

        public (string Name, long Qpc)? WaitFor(string name, TimeSpan timeout, int skip = 0)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                var match = _events.Where(e => e.Name == name).Skip(skip).ToList();
                if (match.Count > 0)
                {
                    return match[0];
                }

                Thread.Sleep(5);
            }

            return null;
        }

        public TimeSpan ExitViaSecondLaunch()
        {
            var sw = Stopwatch.StartNew();
            using (var closer = Process.Start(new ProcessStartInfo(AppExe, "--exit") { UseShellExecute = false }))
            {
                closer?.WaitForExit(5000);
            }

            _process.WaitForExit(10_000);
            return sw.Elapsed;
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _ = ExitViaSecondLaunch();
            }

            _process.Dispose();
        }
    }
}
