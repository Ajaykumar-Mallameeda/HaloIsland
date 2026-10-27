using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Harness;

/// <summary>
/// Commands:
///   env                         check run conditions and print them (no measurement)
///   batch --hz 120|60           gated run matrix of PROTOTYPE_S1_S2_REPORT §8.8 (protocol run IDs)
///   s2-dpi --technique A|B      manual S2-11M: owner changes scaling while the harness checks placement
///   report [--date yyyy-MM-dd]  summarise a results folder into REPORT.md
///   dev-s1|dev-s2|dev-cal ...   shake-down runs with DEV- IDs; never used as results
/// </summary>
public static class Program
{
    private static readonly Technique[] All = [Technique.A, Technique.B, Technique.C];
    private static StreamWriter? s_batchLog;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: env | batch --hz 120|60 | s2-dpi --technique A|B | report [--date d] | dev-s1|dev-s2|dev-cal");
            return 2;
        }

        var hz = IntArg(args, "--hz") ?? EnvironmentSnapshot.Capture().PrimaryRefreshHz;
        var technique = Enum.TryParse<Technique>(StringArg(args, "--technique"), out var t) ? t : Technique.A;
        var date = StringArg(args, "--date") ?? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var stamp = DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var previous = Win32.SetThreadExecutionState(Win32.EsContinuous | Win32.EsDisplayRequired | Win32.EsSystemRequired);
        try
        {
            switch (args[0])
            {
                case "env":
                    return Env(hz);
                case "batch":
                    return Batch(hz, date);
                case "s2-dpi":
                    return Print(S2Dpi.Run(technique, $"S2-11M-{technique}", date));
                case "report":
                    return Report.Write(date);
                case "dev-s1":
                    var timing = new SessionTiming(
                        TimeSpan.FromSeconds(IntArg(args, "--warmup") ?? 5),
                        TimeSpan.FromSeconds(IntArg(args, "--settled") ?? 30),
                        IntArg(args, "--transitions") ?? 10,
                        TimeSpan.FromSeconds(2));
                    return Print(S1Session.Run(technique, hz, $"DEV-S1-{technique}-{hz}-{stamp}", date, timing, null));
                case "dev-alttab":
                    return AltTabDiagnostic.Run();
                case "dev-s2":
                    return Print(S2Checklist.Run(technique, $"DEV-S2-{technique}-{stamp}", date, null));
                case "dev-cal":
                    return Print(StringArg(args, "--kind") switch
                    {
                        "base" => Calibration.Baseline(hz, $"DEV-CAL-{hz}-BASE-{stamp}", date),
                        "pos" => Calibration.Positive(hz, $"DEV-CAL-{hz}-POS-{stamp}", date, null),
                        "neg" => Calibration.Negative(technique, true, hz, $"DEV-CAL-{hz}-NEG-{technique}-{stamp}", date, null),
                        "negctl" => Calibration.Negative(technique, false, hz, $"DEV-CAL-{hz}-NEGCTL-{technique}-{stamp}", date, null),
                        "tick" => Calibration.Tick(technique, hz, $"DEV-CAL-{hz}-TICK-{technique}-{stamp}", date, null),
                        "startup" => Startup.Run(technique, hz, $"DEV-STU-{technique}-{hz}-{stamp}", date, null),
                        _ => throw new ArgumentException("--kind base|pos|neg|negctl|tick|startup"),
                    });
                default:
                    Console.Error.WriteLine("Unknown command.");
                    return 2;
            }
        }
        finally
        {
            _ = Win32.SetThreadExecutionState(previous == 0 ? Win32.EsContinuous : previous);
            s_batchLog?.Dispose();
        }
    }

    private static int Print(JsonObject summary)
    {
        Console.WriteLine(summary.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int Env(int hz)
    {
        var v = new Validity();
        var env = RunEnvironment.Capture();
        env.Check(v, hz, "now");
        var geo = IslandGeometry.Compute(env.WorkArea, env.Dpi);
        Console.WriteLine(env.ToJson().ToJsonString());
        Console.WriteLine($"canvas={geo.Canvas} foreign_windows_over_canvas={WindowChecks.ForeignWindowsAbove(0, geo.Canvas, new HashSet<nint>())}");
        Console.WriteLine(v.IsValid ? "conditions: OK" : "conditions: " + string.Join(",", v.Violations));
        return v.IsValid ? 0 : 3;
    }

    private static void Log(string line)
    {
        var text = $"{DateTime.Now:HH:mm:ss} {line}";
        Console.WriteLine(text);
        s_batchLog?.WriteLine(text);
        s_batchLog?.Flush();
    }

    /// <summary>Gated matrix (§8.8) with the rerun rule of §8.9.</summary>
    private static int Batch(int hz, string date)
    {
        Directory.CreateDirectory(Path.Combine(Paths.ResultsRoot, date));
        s_batchLog = new StreamWriter(Path.Combine(Paths.ResultsRoot, date, $"batch-{hz}.log"), append: true);
        var pre = new Validity();
        RunEnvironment.Capture().Check(pre, hz, "precheck");
        if (!pre.IsValid)
        {
            Log($"precheck failed: {string.Join(",", pre.Violations)}");
            return 3;
        }

        var hashAb = Paths.BuildHash(Technique.A);
        var hashC = Paths.BuildHash(Technique.C);
        string HashOf(Technique x) => x == Technique.C ? hashC : hashAb;
        Log($"batch {hz} Hz start; build A/B={hashAb} C={hashC}");

        // Calibration (§8.7).
        var baseOk = Pass(Gated($"CAL-{hz}-BASE", id => Calibration.Baseline(hz, id, date)));
        var posOk = Pass(Gated($"CAL-{hz}-POS", id => Calibration.Positive(hz, id, date, hashC)));
        var negA = Pass(Gated($"CAL-{hz}-NEG-A", id => Calibration.Negative(Technique.A, true, hz, id, date, hashAb)));
        var negCtlA = Gated($"CAL-{hz}-NEGCTL-A", id => Calibration.Negative(Technique.A, false, hz, id, date, hashAb));
        var negOk = negA && Pass(negCtlA);
        if (OutcomeOf(negCtlA) != Outcome.Pass.Label())
        {
            var negB = Pass(Gated($"CAL-{hz}-NEG-B", id => Calibration.Negative(Technique.B, true, hz, id, date, hashAb)));
            var negCtlB = Pass(Gated($"CAL-{hz}-NEGCTL-B", id => Calibration.Negative(Technique.B, false, hz, id, date, hashAb)));
            negOk |= negB && negCtlB;
        }

        var tickOk = All.Select(x => Pass(Gated($"CAL-{hz}-TICK-{x}", id => Calibration.Tick(x, hz, id, date, HashOf(x))))).ToList().All(b => b);
        var calibrationValid = baseOk && posOk && negOk && tickOk;
        Log($"calibration {hz} Hz: base={baseOk} pos={posOk} neg={negOk} tick={tickOk} => {(calibrationValid ? "VALID" : "INVALID")}");
        if (!calibrationValid)
        {
            Log("Calibration failed: PB-03/PB-05 INVALID at this rate; gate stops (§8.7, §8.14).");
            return 4;
        }

        if (hz == 120)
        {
            foreach (var x in All)
            {
                Gated($"STU-{x}-{hz}", id => Startup.Run(x, hz, id, date, HashOf(x)));
            }
        }

        var sessions = hz == 120 ? 3 : 1;
        for (var s = 1; s <= sessions; s++)
        {
            foreach (var x in All)
            {
                var session = s;
                Gated($"S1-{x}-{hz}-{session}", id => S1Session.Run(x, hz, id, date, SessionTiming.Gated, HashOf(x)));
            }
        }

        if (hz == 120)
        {
            foreach (var x in All)
            {
                Gated($"S2-{x}", id => S2Checklist.Run(x, id, date, HashOf(x)));
            }
        }

        Log($"batch {hz} Hz complete");
        Report.Write(date);
        return 0;
    }

    private static bool Pass(JsonObject summary) => OutcomeOf(summary) == Outcome.Pass.Label();

    private static string OutcomeOf(JsonObject summary) => summary["outcome"]?.GetValue<string>() ?? Outcome.Invalid.Label();

    /// <summary>Runs once; an INVALID run (or a crash) is repeated once as -r1 (§8.9). A valid FAIL is final.</summary>
    private static JsonObject Gated(string runId, Func<string, JsonObject> run)
    {
        var result = Attempt(runId, run);
        if (result["validity"]?["valid"]?.GetValue<bool>() != true)
        {
            Log($"{runId}: INVALID ({Violations(result)}); rerunning once as {runId}-r1");
            result = Attempt(runId + "-r1", run);
        }

        return result;
    }

    private static JsonObject Attempt(string runId, Func<string, JsonObject> run)
    {
        Log($"{runId}: start");
        try
        {
            var summary = run(runId);
            Log($"{runId}: done valid={summary["validity"]?["valid"]} outcome={summary["outcome"]?.ToJsonString() ?? summary["outcomes"]?.ToJsonString(new System.Text.Json.JsonSerializerOptions()) ?? Items(summary)} {Violations(summary)}");
            return summary;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Persisted log: exception type and HRESULT only (messages can contain paths).
            Log($"{runId}: ERROR {ex.GetType().Name} 0x{ex.HResult:X8}");
            Console.Error.WriteLine(ex);
            return new JsonObject
            {
                ["run_id"] = runId,
                ["validity"] = new JsonObject { ["valid"] = false, ["violations"] = new JsonArray("harness_error_" + ex.GetType().Name) },
                ["outcome"] = Outcome.Invalid.Label(),
            };
        }
    }

    private static string Violations(JsonObject s) => s["validity"]?["violations"]?.ToJsonString() ?? string.Empty;

    private static string Items(JsonObject s) =>
        s["items"] is JsonObject items ? string.Join(" ", items.Select(kv => $"{kv.Key}={kv.Value?["outcome"]}")) : string.Empty;

    private static int? IntArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? StringArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

/// <summary>Manual S2-11M: the owner changes display scaling; the harness checks placement after each change.</summary>
internal static class S2Dpi
{
    public static JsonObject Run(Technique technique, string runId, string date)
    {
        var artifacts = new RunArtifacts(runId, date);
        using var overlay = OverlayProcess.Start(technique, []);
        overlay.WaitReady(TimeSpan.FromSeconds(15));
        Console.WriteLine("Change Settings > Display > Scale to 100 %, then 150 %, then 175 %, then back to 125 %. Wait ~5 s after each change.");
        var checks = new JsonArray();
        var seen = new List<uint>();
        var lastDpi = Win32.GetDpiForWindow(overlay.Hwnd);
        var allOk = true;
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMinutes(20) && !(seen.Contains(96) && seen.Contains(144) && seen.Contains(168) && seen.Count >= 4 && seen[^1] == 120))
        {
            Thread.Sleep(500);
            var dpi = Win32.GetDpiForWindow(overlay.Hwnd);
            if (dpi == lastDpi || dpi == 0)
            {
                continue;
            }

            Thread.Sleep(3000);
            dpi = Win32.GetDpiForWindow(overlay.Hwnd);
            lastDpi = dpi;
            seen.Add(dpi);
            var (work, monitorDpi) = Win32.PrimaryWorkArea();
            var expected = IslandGeometry.Compute(PxRect.FromEdges(work.Left, work.Top, work.Right, work.Bottom), (int)monitorDpi).Canvas;
            var r = Win32.WindowRect(overlay.Hwnd);
            var ok = Math.Abs(r.Left - expected.Left) <= 1 && Math.Abs(r.Top - expected.Top) <= 1 && Math.Abs((r.Right - r.Left) - expected.Width) <= 1 && Math.Abs((r.Bottom - r.Top) - expected.Height) <= 1;
            var responsive = Win32.IsResponsive(overlay.Hwnd, 1000);
            allOk &= ok && responsive && !overlay.Process.HasExited;
            checks.Add(new JsonObject { ["dpi"] = dpi, ["monitor_dpi"] = monitorDpi, ["placement_ok"] = ok, ["responsive"] = responsive, ["rect"] = $"{r.Left},{r.Top},{r.Right - r.Left},{r.Bottom - r.Top}", ["expected"] = $"{expected.Left},{expected.Top},{expected.Width},{expected.Height}" });
            Console.WriteLine($"DPI {dpi}: placement_ok={ok} responsive={responsive}");
        }

        var complete = seen.Contains(96) && seen.Contains(144) && seen.Contains(168) && seen.Count > 0 && seen[^1] == 120;
        var code = overlay.Quit(TimeSpan.FromSeconds(5));
        overlay.SaveLog(artifacts.File("overlay.jsonl"));
        var outcome = seen.Count == 0 ? Outcome.NotRun : !complete ? Outcome.Invalid : allOk && code == 0 ? Outcome.Pass : Outcome.Fail;
        var summary = new JsonObject
        {
            ["run_id"] = runId,
            ["kind"] = "s2-dpi",
            ["technique"] = technique.ToString(),
            ["protocol"] = "v1.2",
            ["validity"] = new JsonObject { ["valid"] = complete, ["violations"] = new JsonArray() },
            ["outcome"] = outcome.Label(),
            ["sequence_complete"] = complete,
            ["checks"] = checks,
            ["exit_code"] = code,
        };
        artifacts.WriteJson("summary.json", summary);
        return summary;
    }
}

/// <summary>Writes perf-results/&lt;date&gt;/REPORT.md from every summary.json (numbers and tokens only).</summary>
internal static class Report
{
    public static int Write(string date)
    {
        var root = Path.Combine(Paths.ResultsRoot, date);
        var summaries = Directory.GetDirectories(root)
            .Select(d => Path.Combine(d, "summary.json"))
            .Where(File.Exists)
            .Select(f => JsonNode.Parse(File.ReadAllText(f))!.AsObject())
            .OrderBy(s => s["run_id"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToList();
        var md = new StringBuilder();
        md.AppendLine(CultureInfo.InvariantCulture, $"# Run summary {date}").AppendLine();
        md.AppendLine("| Run | Kind | Valid | Outcome(s) | Key metrics | Violations |").AppendLine("|---|---|---|---|---|---|");
        foreach (var s in summaries)
        {
            var outcome = s["outcome"]?.GetValue<string>()
                ?? (s["outcomes"] is JsonObject o ? string.Join(" ", o.Select(kv => $"{kv.Key}={kv.Value}")) : null)
                ?? (s["items"] is JsonObject i ? string.Join(" ", i.Select(kv => $"{kv.Key}={kv.Value?["outcome"]}")) : string.Empty);
            var metrics = s["metrics"] is JsonObject m ? string.Join("; ", m.Where(kv => kv.Value is JsonValue).Select(kv => $"{kv.Key}={Fmt(kv.Value)}")) : string.Empty;
            if (s["informational"] is JsonObject inf)
            {
                metrics += (metrics.Length > 0 ? "; " : string.Empty) + string.Join("; ", inf.Where(kv => kv.Value is JsonValue).Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
            }

            md.AppendLine(CultureInfo.InvariantCulture, $"| {s["run_id"]} | {s["kind"]} | {s["validity"]?["valid"]} | {outcome} | {metrics} | {s["validity"]?["violations"]?.ToJsonString()} |");
        }

        File.WriteAllText(Path.Combine(root, "REPORT.md"), md.ToString());
        Console.WriteLine($"Wrote REPORT.md ({summaries.Count} runs)");
        return 0;
    }

    private static string Fmt(JsonNode? v) => v is JsonValue jv && jv.TryGetValue<double>(out var d) ? d.ToString("0.###", CultureInfo.InvariantCulture) : v?.ToJsonString() ?? "null";
}
