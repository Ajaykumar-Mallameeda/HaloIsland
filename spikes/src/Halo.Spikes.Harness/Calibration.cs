using System.Diagnostics;
using System.Text.Json.Nodes;
using Halo.Spikes.Measurement;

namespace Halo.Spikes.Harness;

/// <summary>Predeclared calibration runs (PROTOTYPE_S1_S2_REPORT §8.7).</summary>
internal static class Calibration
{
    private static readonly TimeSpan CalibrationWarmUp = TimeSpan.FromSeconds(30);
    private const int NegTransitions = 20;

    public static JsonObject Baseline(int hz, string runId, string batchDate)
    {
        using var run = new MeasuredRun(runId, hz, batchDate, null, null);
        Thread.Sleep(1000);
        var start = MeasuredRun.Qpc;
        var foreign = 0;
        var cursorIn = false;
        for (var i = 0; i < 6; i++)
        {
            Thread.Sleep(TimeSpan.FromSeconds(10));
            var c = run.Contamination([]);
            foreign += c.Foreign;
            cursorIn |= c.CursorIn;
            run.CheckContamination(c.Foreign, c.CursorIn, c.LastInput);
        }

        var end = MeasuredRun.Qpc;
        run.Finish();
        var frames = FrameTimeline.CountInWindow(run.AttributedFrames(), start, end);
        var all = run.Duplication.Frames().Count(f => f.Qpc > start && f.Qpc <= end);
        var outcome = run.Validity.IsValid ? CalibrationRules.Baseline(frames) : Outcome.Invalid;
        var summary = run.SummaryHeader("cal-base", null, true);
        summary["validity"] = run.ValidityJson();
        summary["outcome"] = outcome.Label();
        summary["metrics"] = new JsonObject
        {
            ["attributed_frames"] = frames,
            ["system_desktop_updates_per_s"] = all / ((end - start) / (double)MeasuredRun.QpcFrequency),
        };
        run.Artifacts.WriteJson("summary.json", summary);
        return summary;
    }

    public static JsonObject Positive(int hz, string runId, string batchDate, string? buildHashC)
    {
        using var run = new MeasuredRun(runId, hz, batchDate, buildHashC, Technique.C);
        var overlay = run.StartOverlay(Technique.C, ["--calibration", "--continuous"], expectCalibration: true);
        Thread.Sleep(CalibrationWarmUp);
        var start = MeasuredRun.Qpc;
        Thread.Sleep(TimeSpan.FromSeconds(10));
        var end = MeasuredRun.Qpc;
        var c = run.Contamination([overlay.Hwnd]);
        run.CheckContamination(c.Foreign, c.CursorIn, c.LastInput);
        run.Validity.Require(overlay.Quit(TimeSpan.FromSeconds(5)) == 0, "overlay_exit_abnormal");
        run.Finish();
        var frames = run.AttributedFrames().Where(f => f.Qpc > start && f.Qpc <= end).ToList();
        var seconds = (end - start) / (double)MeasuredRun.QpcFrequency;
        var outcome = run.Validity.IsValid ? CalibrationRules.Positive(frames, seconds, hz, MeasuredRun.QpcFrequency) : Outcome.Invalid;
        var intervals = frames.Zip(frames.Skip(1), (a, b) => FrameTimeline.ToMs(b.Qpc - a.Qpc, MeasuredRun.QpcFrequency)).ToArray();
        var summary = run.SummaryHeader("cal-pos", Technique.C, true);
        summary["validity"] = run.ValidityJson();
        summary["outcome"] = outcome.Label();
        summary["metrics"] = new JsonObject
        {
            ["attributed_frames"] = frames.Count,
            ["rate_per_s"] = frames.Count / seconds,
            ["median_interval_ms"] = intervals.Length > 0 ? Statistics.Median(intervals) : null,
            ["p95_interval_ms"] = intervals.Length > 0 ? Statistics.Percentile(intervals, 95) : null,
            ["coalesced_fraction"] = frames.Count == 0 ? null : frames.Count(f => f.Coalesced) / (double)frames.Count,
        };
        run.Artifacts.WriteJson("summary.json", summary);
        return summary;
    }

    /// <summary>NEG (stall) or NEGCTL (no stall) on technique A or B.</summary>
    public static JsonObject Negative(Technique technique, bool stalled, int hz, string runId, string batchDate, string? buildHash)
    {
        using var run = new MeasuredRun(runId, hz, batchDate, buildHash, technique);
        string[] args = stalled ? ["--calibration", "--stall-ms", "50"] : ["--calibration"];
        var overlay = run.StartOverlay(technique, args, expectCalibration: true);
        Thread.Sleep(CalibrationWarmUp);
        var start = MeasuredRun.Qpc;
        for (var k = 0; k < NegTransitions; k++)
        {
            overlay.Send(k % 2 == 0 ? OverlayCommand.Expand : OverlayCommand.Collapse);
            Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        var c = run.Contamination([overlay.Hwnd]);
        run.CheckContamination(c.Foreign, c.CursorIn, c.LastInput);
        run.Validity.Require(overlay.Quit(TimeSpan.FromSeconds(5)) == 0, "overlay_exit_abnormal");
        run.Finish();
        var stalls = overlay.Named("stall.injected").Count(e => e.Qpc > start);
        run.Validity.Require(stalls == (stalled ? NegTransitions : 0), "stall_count_mismatch");
        var frames = run.AttributedFrames();
        var transitions = S1Session.Transitions(overlay, start).Select(m => FrameTimeline.ForTransition(frames, m, MeasuredRun.QpcFrequency, run.RefreshMs)).ToList();
        var outcome = !run.Validity.IsValid
            ? Outcome.Invalid
            : stalled ? CalibrationRules.Negative(transitions, NegTransitions) : CalibrationRules.NegativeControl(transitions, NegTransitions);
        var summary = run.SummaryHeader(stalled ? "cal-neg" : "cal-negctl", technique, true);
        summary["validity"] = run.ValidityJson();
        summary["outcome"] = outcome.Label();
        summary["metrics"] = new JsonObject
        {
            ["transitions"] = transitions.Count,
            ["valid_transitions"] = transitions.Count(t => t.Valid),
            ["transitions_with_gap_ge_40ms"] = transitions.Count(t => t.Valid && t.MaxIntervalMs >= CalibrationRules.StallGapMs),
            ["stalls_injected"] = stalls,
            ["max_interval_ms_per_transition"] = string.Join(";", transitions.Select(t => RunArtifacts.F(t.MaxIntervalMs))),
        };
        run.Artifacts.WriteJson("summary.json", summary);
        return summary;
    }

    /// <summary>TICK: one minimal repaint every 5 s while settled; 60 s window placed between ticks.</summary>
    public static JsonObject Tick(Technique technique, int hz, string runId, string batchDate, string? buildHash)
    {
        using var run = new MeasuredRun(runId, hz, batchDate, buildHash, technique);
        var overlay = run.StartOverlay(technique, ["--calibration", "--tick-ms", "5000"], expectCalibration: true);
        Thread.Sleep(CalibrationWarmUp);
        var lastTick = overlay.Named("tick").Count();
        var waited = Stopwatch.StartNew();
        while (overlay.Named("tick").Count() == lastTick && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            Thread.Sleep(10);
        }

        Thread.Sleep(2500);
        var start = MeasuredRun.Qpc;
        Thread.Sleep(TimeSpan.FromSeconds(60));
        var end = MeasuredRun.Qpc;
        var c = run.Contamination([overlay.Hwnd]);
        run.CheckContamination(c.Foreign, c.CursorIn, c.LastInput);
        run.Validity.Require(overlay.Quit(TimeSpan.FromSeconds(5)) == 0, "overlay_exit_abnormal");
        run.Finish();
        var ticks = overlay.Named("tick").Select(e => e.Qpc).Where(q => q > start && q <= end).ToList();
        var frames = run.AttributedFrames().Where(f => f.Qpc > start && f.Qpc <= end).ToList();
        run.Validity.Require(ticks.Count == 12, "tick_count_mismatch");
        var outcome = run.Validity.IsValid ? CalibrationRules.Tick(ticks, frames, MeasuredRun.QpcFrequency) : Outcome.Invalid;
        var latencies = ticks.Select(t => frames.FirstOrDefault(f => f.Qpc >= t)).Zip(ticks, (f, t) => f.Qpc == 0 ? double.NaN : FrameTimeline.ToMs(f.Qpc - t, MeasuredRun.QpcFrequency)).ToArray();
        var summary = run.SummaryHeader("cal-tick", technique, true);
        summary["validity"] = run.ValidityJson();
        summary["outcome"] = outcome.Label();
        summary["metrics"] = new JsonObject
        {
            ["ticks"] = ticks.Count,
            ["attributed_frames"] = frames.Count,
            ["tick_to_frame_ms"] = string.Join(";", latencies.Select(RunArtifacts.F)),
        };
        run.Artifacts.WriteJson("summary.json", summary);
        return summary;
    }
}

internal static class Startup
{
    /// <summary>Warm startup (informational, §8.12): process start → first attributed frame, 11 launches.</summary>
    public static JsonObject Run(Technique technique, int hz, string runId, string batchDate, string? buildHash)
    {
        using var run = new MeasuredRun(runId, hz, batchDate, buildHash, technique);
        var toFrame = new List<double>();
        var toReady = new List<double>();
        for (var i = 0; i < 11; i++)
        {
            using var overlay = OverlayProcess.Start(technique, []);
            var ready = overlay.WaitFor("overlay.ready", TimeSpan.FromSeconds(15));
            var deadline = Stopwatch.StartNew();
            FrameRecord first = default;
            while (deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                first = run.AttributedFrames().FirstOrDefault(f => f.Qpc > overlay.StartQpc);
                if (first.Qpc != 0)
                {
                    break;
                }

                Thread.Sleep(5);
            }

            if (i > 0)
            {
                toFrame.Add(first.Qpc == 0 ? double.NaN : FrameTimeline.ToMs(first.Qpc - overlay.StartQpc, MeasuredRun.QpcFrequency));
                toReady.Add(ready is null ? double.NaN : FrameTimeline.ToMs(ready.Qpc - overlay.StartQpc, MeasuredRun.QpcFrequency));
            }

            run.Validity.Require(overlay.Quit(TimeSpan.FromSeconds(5)) == 0, "overlay_exit_abnormal");
            Thread.Sleep(2000);
        }

        var c = run.Contamination([]);
        run.CheckContamination(c.Foreign, c.CursorIn, c.LastInput);
        run.Finish();
        run.Validity.Require(toFrame.All(double.IsFinite), "missing_first_frame");
        var summary = run.SummaryHeader("startup", technique, true);
        summary["validity"] = run.ValidityJson();
        summary["informational"] = new JsonObject
        {
            ["start_to_first_frame_p50_ms"] = toFrame.All(double.IsFinite) ? Statistics.Median(toFrame) : null,
            ["start_to_first_frame_ms"] = string.Join(";", toFrame.Select(RunArtifacts.F)),
            ["start_to_ready_p50_ms"] = toReady.All(double.IsFinite) ? Statistics.Median(toReady) : null,
            ["pb09_reference_s"] = Budgets.Pb09WarmStartup.Limit,
        };
        run.Artifacts.WriteJson("summary.json", summary);
        return summary;
    }
}
