using System.Diagnostics;
using System.Text.Json.Nodes;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Harness;

/// <summary>Durations of an S1 session. <see cref="Gated"/> is the protocol; anything else is a DEV run.</summary>
internal sealed record SessionTiming(TimeSpan WarmUp, TimeSpan Settled, int Transitions, TimeSpan Spacing)
{
    public static readonly SessionTiming Gated = new(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(600), 60, TimeSpan.FromSeconds(2));

    public bool IsGated => this == Gated;
}

internal sealed record CounterSample(double T, long Qpc, double CpuMs, ulong Cycles, long PrivateBytes, long WorkingSet, ProcessCounters.Sample Pdh, uint LastInput, int Foreign, bool CursorIn);

/// <summary>Shared run scaffolding: environment, backdrop, Desktop Duplication, overlay, validity checks.</summary>
internal sealed class MeasuredRun : IDisposable
{
    private readonly int _hz;
    private readonly UiThread _ui = new();
    private readonly List<OverlayProcess> _overlays = [];

    public MeasuredRun(string runId, int hz, string batchDate, string? expectedBuildHash, Technique? technique)
    {
        _hz = hz;
        Artifacts = new RunArtifacts(runId, batchDate);
        EnvStart = RunEnvironment.Capture();
        EnvStart.Check(Validity, hz, "start");
        Artifacts.WriteJson("env-start.json", EnvStart.ToJson());
        Geometry = IslandGeometry.Compute(EnvStart.WorkArea, EnvStart.Dpi);
        Backdrop = new Backdrop(_ui, Geometry.Canvas);
        WindowChecks.ParkCursor(Geometry.Canvas);
        Duplication = DesktopDuplication.Create(Geometry.Canvas);
        Duplication.Start();
        LastInputAtStart = Win32.LastInputTick();
        if (technique is { } t)
        {
            BuildHash = Paths.BuildHash(t);
            Validity.Require(expectedBuildHash is null || expectedBuildHash == BuildHash, "build_changed");
        }
    }

    public RunArtifacts Artifacts { get; }

    public Validity Validity { get; } = new();

    public RunEnvironment EnvStart { get; }

    public IslandGeometry Geometry { get; }

    public Backdrop Backdrop { get; }

    public DesktopDuplication Duplication { get; }

    public uint LastInputAtStart { get; }

    public string? BuildHash { get; }

    public double RefreshMs => 1000.0 / _hz;

    public static long Qpc => Stopwatch.GetTimestamp();

    public static long QpcFrequency => Stopwatch.Frequency;

    public OverlayProcess StartOverlay(Technique technique, IEnumerable<string> args, bool expectCalibration)
    {
        var overlay = OverlayProcess.Start(technique, args);
        _overlays.Add(overlay);
        overlay.WaitReady(TimeSpan.FromSeconds(15));
        var config = overlay.WaitFor("overlay.config", TimeSpan.FromSeconds(1)) ?? throw new InvalidOperationException("No overlay config.");
        var calibrationOn = config.Num("calibration") != 0 || config.Num("stall_ms") != 0 || config.Num("tick_ms") != 0 || config.Num("continuous") != 0;
        Validity.Require(calibrationOn == expectCalibration, expectCalibration ? "calibration_mode_missing" : "calibration_feature_enabled");
        var ready = overlay.Named("overlay.ready").First();
        var c = Geometry.Canvas;
        Validity.Require(ready.Num("left") == c.Left && ready.Num("top") == c.Top && ready.Num("width") == c.Width && ready.Num("height") == c.Height, "overlay_rect_mismatch");
        Thread.Sleep(300);
        var shape = Geometry.ScreenShape(false);
        Validity.Require(WindowChecks.RootAt((int)(shape.Left + (shape.Width / 2)), (int)(shape.Top + (shape.Height / 2))) == overlay.Hwnd, "overlay_not_on_top");
        return overlay;
    }

    /// <summary>Contamination checks for one sample (§8.4).</summary>
    public (int Foreign, bool CursorIn, uint LastInput) Contamination(IEnumerable<nint> ours)
    {
        var set = new HashSet<nint>(ours) { Backdrop.Hwnd };
        return (WindowChecks.ForeignWindowsAbove(Backdrop.Hwnd, Geometry.Canvas, set), WindowChecks.CursorIn(Geometry.Canvas), Win32.LastInputTick());
    }

    public void CheckContamination(int foreign, bool cursorIn, uint lastInput, bool inputAllowed = false)
    {
        Validity.Require(foreign == 0, "foreign_window_over_region");
        Validity.Require(!cursorIn, "cursor_in_canvas");
        Validity.Require(inputAllowed || lastInput == LastInputAtStart, "user_input_during_run");
    }

    /// <summary>Attributed frames as protocol records, sorted by time.</summary>
    public List<FrameRecord> AttributedFrames() => Duplication.Frames()
        .Where(f => f.Attributed)
        .Select(f => new FrameRecord(f.Qpc, f.AccumulatedFrames > 1))
        .OrderBy(f => f.Qpc)
        .ToList();

    /// <summary>Stops capture, checks the end environment and writes the frame metadata.</summary>
    public void Finish()
    {
        Duplication.Stop();
        Validity.Require(Duplication.ErrorHResult == 0, Duplication.AccessLost ? "duplication_access_lost" : "duplication_error");
        var envEnd = RunEnvironment.Capture();
        envEnd.Check(Validity, _hz, "end");
        Artifacts.WriteJson("env-end.json", envEnd.ToJson());
        Artifacts.WriteCsv("frames.csv", "qpc,accumulated,attributed,rects_coalesced", Duplication.Frames().Select(f => $"{f.Qpc},{f.AccumulatedFrames},{(f.Attributed ? 1 : 0)},{(f.RectsCoalesced ? 1 : 0)}"));
        for (var i = 0; i < _overlays.Count; i++)
        {
            _overlays[i].SaveLog(Artifacts.File(_overlays.Count == 1 ? "overlay.jsonl" : $"overlay-{i + 1}.jsonl"));
        }
    }

    public JsonObject SummaryHeader(string kind, Technique? technique, bool gated) => new()
    {
        ["run_id"] = Artifacts.RunId,
        ["kind"] = kind,
        ["technique"] = technique?.ToString(),
        ["refresh_hz"] = _hz,
        ["gated"] = gated && !Artifacts.RunId.StartsWith("DEV-", StringComparison.Ordinal),
        ["protocol"] = "v1.3",
        ["build_hash"] = BuildHash,
        ["qpc_frequency"] = QpcFrequency,
        ["canvas"] = $"{Geometry.Canvas.Left},{Geometry.Canvas.Top},{Geometry.Canvas.Width},{Geometry.Canvas.Height}",
    };

    public JsonObject ValidityJson() => new()
    {
        ["valid"] = Validity.IsValid,
        ["violations"] = new JsonArray(Validity.Violations.Select(v => (JsonNode)v).ToArray()),
    };

    public void Dispose()
    {
        foreach (var o in _overlays)
        {
            o.Dispose();
        }

        Duplication.Dispose();
        Backdrop.Close();
        _ui.Dispose();
    }
}

internal static class S1Session
{
    /// <summary>One S1 session: warm-up, settled window (PB-01/02/03/10/12), transitions (PB-05) — §8.6.</summary>
    public static JsonObject Run(Technique technique, int hz, string runId, string batchDate, SessionTiming timing, string? expectedBuildHash)
    {
        using var run = new MeasuredRun(runId, hz, batchDate, expectedBuildHash, technique);
        var overlay = run.StartOverlay(technique, [], expectCalibration: false);
        var ours = new[] { overlay.Hwnd };
        var process = overlay.Process;
        var dwmPid = Process.GetProcessesByName("dwm").FirstOrDefault()?.Id ?? 0;
        using var counters = new ProcessCounters();

        // Warm-up from overlay.ready, then the settled window with a counter sample every 10 s.
        var readyQpc = overlay.Named("overlay.ready").First().Qpc;
        SleepUntil(readyQpc, timing.WarmUp);
        var samples = new List<CounterSample>();
        var windowStart = Stopwatch.StartNew();
        var windowStartQpc = MeasuredRun.Qpc;
        var count = (int)(timing.Settled.TotalSeconds / 10) + 1;
        for (var i = 0; i < count; i++)
        {
            var due = TimeSpan.FromSeconds(i * 10);
            var wait = due - windowStart.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                Thread.Sleep(wait);
            }

            samples.Add(Sample(windowStart.Elapsed.TotalSeconds, process, counters, overlay.Pid, dwmPid, run, ours));
        }

        var windowEndQpc = samples[^1].Qpc;

        // Transitions phase in the same process.
        process.Refresh();
        var cpuBefore = process.TotalProcessorTime;
        var phaseStart = Stopwatch.StartNew();
        long peakPrivate = 0;
        for (var k = 0; k < timing.Transitions; k++)
        {
            overlay.Send(k % 2 == 0 ? OverlayCommand.Expand : OverlayCommand.Collapse);
            Thread.Sleep(timing.Spacing);
            process.Refresh();
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
        }

        process.Refresh();
        var animationCpu = process.TotalProcessorTime - cpuBefore;
        var animationSeconds = phaseStart.Elapsed.TotalSeconds;
        var final = run.Contamination(ours);
        run.CheckContamination(final.Foreign, final.CursorIn, final.LastInput);
        var exitCode = overlay.Quit(TimeSpan.FromSeconds(5));
        run.Validity.Require(exitCode == 0, "overlay_exit_abnormal");
        run.Finish();

        foreach (var s in samples)
        {
            run.CheckContamination(s.Foreign, s.CursorIn, s.LastInput);
        }

        for (var i = 1; i < samples.Count; i++)
        {
            run.Validity.Require(samples[i].T - samples[i - 1].T <= 15, "sampling_gap");
        }

        WriteSamples(run.Artifacts, samples);

        // Analysis (§8.6).
        var frames = run.AttributedFrames();
        var wall = TimeSpan.FromSeconds(samples[^1].T - samples[0].T);
        var cpu = TimeSpan.FromMilliseconds(samples[^1].CpuMs - samples[0].CpuMs);
        var pb01 = CpuRate.SecondsPerTenMinutes(cpu, wall);
        var cs = ContextSwitches.Aggregate(samples.Select(s => new ThreadSample(s.T, s.Pdh.ContextSwitchesByThread)).ToList());
        var pb03Frames = FrameTimeline.CountInWindow(frames, windowStartQpc, windowEndQpc);
        var pb10 = samples[^1].PrivateBytes / 1_000_000.0;
        var gpu3d = samples[^1].Pdh.Gpu3dRunningTime100ns - samples[0].Pdh.Gpu3dRunningTime100ns;
        var pb12 = gpu3d * 100e-9 / wall.TotalSeconds * 100;
        var dwm3d = (samples[^1].Pdh.Dwm3dRunningTime100ns - samples[0].Pdh.Dwm3dRunningTime100ns) * 100e-9 / wall.TotalSeconds * 100;

        var transitions = Transitions(overlay, windowEndQpc);
        var perTransition = transitions.Select(m => FrameTimeline.ForTransition(frames, m, MeasuredRun.QpcFrequency, run.RefreshMs)).ToList();
        var pb05 = FrameTimeline.EvaluatePb05(perTransition, run.RefreshMs);
        var latencies = CommandLatencies(overlay, frames, windowEndQpc);

        var valid = run.Validity.IsValid;
        Outcome Judge(Outcome o) => valid ? o : Outcome.Invalid;
        var outcomes = new JsonObject
        {
            ["PB-01"] = Judge(Budgets.Pb01SettledIdleCpu.Evaluate(pb01)).Label(),
            ["PB-02"] = Judge(cs.NewThreadStarted ? Outcome.Invalid : Budgets.Pb02SettledWakeups.Evaluate(cs.PerSecond)).Label(),
            ["PB-03"] = Judge(Budgets.Pb03SettledFrames.Evaluate(pb03Frames / wall.TotalSeconds)).Label(),
            ["PB-05"] = Judge(transitions.Count < timing.Transitions ? Outcome.Invalid : pb05.Outcome).Label(),
            ["PB-10"] = Judge(Budgets.Pb10PrivateBytes.Evaluate(pb10)).Label(),
            ["PB-12"] = Judge(Budgets.Pb12SettledGpu.Evaluate(pb12)).Label(),
            ["PB-04"] = "DEFERRED (M3)",
        };

        var summary = run.SummaryHeader("s1", technique, timing.IsGated);
        summary["validity"] = run.ValidityJson();
        summary["outcomes"] = outcomes;
        summary["metrics"] = new JsonObject
        {
            ["pb01_cpu_s_per_10min"] = pb01,
            ["pb01_cycles_delta"] = (double)(samples[^1].Cycles - samples[0].Cycles),
            ["pb02_switches_per_s"] = cs.PerSecond,
            ["pb02_total_switches"] = cs.TotalSwitches,
            ["pb02_new_thread_started"] = cs.NewThreadStarted,
            ["pb02_threads_ended"] = cs.ThreadsEnded,
            ["pb02_threads"] = samples[^1].Pdh.ContextSwitchesByThread.Count,
            ["pb03_attributed_frames"] = pb03Frames,
            ["pb03_window_s"] = wall.TotalSeconds,
            ["pb05_transitions_logged"] = transitions.Count,
            ["pb05_valid_transitions"] = perTransition.Count(t => t.Valid),
            ["pb05_invalid_reasons"] = string.Join(";", perTransition.Where(t => !t.Valid).GroupBy(t => t.InvalidReason).Select(g => $"{g.Key}:{g.Count()}")),
            ["pb05_p95_interval_ms"] = double.IsFinite(pb05.P95IntervalMs) ? pb05.P95IntervalMs : null,
            ["pb05_worst_long_frames"] = pb05.WorstLongFrames,
            ["pb05_transitions_with_long_frame"] = perTransition.Where(t => t.Valid).Take(50).Count(t => t.IntervalsMs.Any(v => v > FramePacing.LongFrameFactor * run.RefreshMs)),
            ["pb05_max_interval_ms"] = perTransition.Where(t => t.Valid).Take(50).Select(t => t.MaxIntervalMs).DefaultIfEmpty(0).Max(),
            ["pb05_median_frames_per_transition"] = perTransition.Count == 0 ? 0 : Statistics.Median(perTransition.Select(t => (double)t.FrameCount).ToArray()),
            ["pb10_private_mb"] = pb10,
            ["pb10_private_mib"] = samples[^1].PrivateBytes / 1048576.0,
            ["pb12_gpu3d_percent"] = pb12,
            ["pb12_has_gpu_instances"] = samples[^1].Pdh.HasGpuInstances,
        };
        summary["informational"] = new JsonObject
        {
            ["working_set_mb"] = samples[^1].WorkingSet / 1_000_000.0,
            ["dwm_gpu3d_percent_settled"] = dwm3d,
            ["animation_cpu_s_per_min"] = animationCpu.TotalSeconds / animationSeconds * 60,
            ["animation_peak_private_mb"] = peakPrivate / 1_000_000.0,
            ["cmd_to_first_frame_p50_ms"] = latencies.Count > 0 ? Statistics.Median(latencies) : null,
            ["cmd_to_first_frame_p95_ms"] = latencies.Count > 0 ? Statistics.Percentile(latencies, 95) : null,
            ["system_desktop_updates_per_s"] = run.Duplication.Frames().Count(f => f.Qpc > windowStartQpc && f.Qpc <= windowEndQpc) / wall.TotalSeconds,
        };
        run.Artifacts.WriteCsv("transitions.csv", "index,valid,reason,frames,latency_ms,max_interval_ms,long_frames", perTransition.Select((t, i) =>
            $"{i},{(t.Valid ? 1 : 0)},{t.InvalidReason},{t.FrameCount},{RunArtifacts.F(t.FirstFrameLatencyMs ?? double.NaN)},{RunArtifacts.F(t.MaxIntervalMs)},{t.IntervalsMs.Count(v => v > FramePacing.LongFrameFactor * run.RefreshMs)}"));
        run.Artifacts.WriteJson("summary.json", summary);
        return summary;
    }

    public static CounterSample Sample(double t, Process process, ProcessCounters counters, int pid, int dwmPid, MeasuredRun run, IEnumerable<nint> ours)
    {
        process.Refresh();
        Win32.QueryProcessCycleTime(process.Handle, out var cycles);
        var pdh = counters.Read(pid, dwmPid);
        var (foreign, cursorIn, lastInput) = run.Contamination(ours);
        return new CounterSample(t, MeasuredRun.Qpc, process.TotalProcessorTime.TotalMilliseconds, cycles, process.PrivateMemorySize64, process.WorkingSet64, pdh, lastInput, foreign, cursorIn);
    }

    /// <summary>Pairs transition.start/end events logged after <paramref name="afterQpc"/>.</summary>
    public static List<TransitionMark> Transitions(OverlayProcess overlay, long afterQpc)
    {
        var events = overlay.Events.Where(e => e.Qpc > afterQpc && e.Name is "transition.start" or "transition.end").OrderBy(e => e.Qpc).ToList();
        var marks = new List<TransitionMark>();
        long? start = null;
        foreach (var e in events)
        {
            if (e.Name == "transition.start")
            {
                start = e.Qpc;
            }
            else if (start is { } s)
            {
                marks.Add(new TransitionMark(s, e.Qpc));
                start = null;
            }
        }

        return marks;
    }

    private static List<double> CommandLatencies(OverlayProcess overlay, List<FrameRecord> frames, long afterQpc)
    {
        var result = new List<double>();
        foreach (var cmd in overlay.Named("cmd.received").Where(e => e.Qpc > afterQpc && e.Tok("cmd") is "expand" or "collapse"))
        {
            var first = frames.FirstOrDefault(f => f.Qpc > cmd.Qpc);
            if (first.Qpc != 0 && first.Qpc - cmd.Qpc < MeasuredRun.QpcFrequency)
            {
                result.Add(FrameTimeline.ToMs(first.Qpc - cmd.Qpc, MeasuredRun.QpcFrequency));
            }
        }

        return result;
    }

    private static void SleepUntil(long fromQpc, TimeSpan offset)
    {
        var target = fromQpc + (long)(offset.TotalSeconds * MeasuredRun.QpcFrequency);
        var remaining = (target - MeasuredRun.Qpc) * 1000 / MeasuredRun.QpcFrequency;
        if (remaining > 0)
        {
            Thread.Sleep((int)remaining);
        }
    }

    private static void WriteSamples(RunArtifacts artifacts, List<CounterSample> samples)
    {
        artifacts.WriteCsv("samples.csv", "t_s,qpc,cpu_ms,cycles,private_bytes,working_set,gpu3d_100ns,gpu_all_100ns,dwm3d_100ns,threads,last_input_tick,foreign_windows,cursor_in_canvas", samples.Select(s =>
            $"{RunArtifacts.F(s.T)},{s.Qpc},{RunArtifacts.F(s.CpuMs)},{s.Cycles},{s.PrivateBytes},{s.WorkingSet},{s.Pdh.Gpu3dRunningTime100ns},{s.Pdh.GpuAllRunningTime100ns},{s.Pdh.Dwm3dRunningTime100ns},{s.Pdh.ContextSwitchesByThread.Count},{s.LastInput},{s.Foreign},{(s.CursorIn ? 1 : 0)}"));
        artifacts.WriteCsv("threads.csv", "t_s,thread_id,context_switches", samples.SelectMany(s => s.Pdh.ContextSwitchesByThread.Select(kv => $"{RunArtifacts.F(s.T)},{kv.Key},{kv.Value}")));
    }
}
