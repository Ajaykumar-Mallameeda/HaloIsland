using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Harness;

/// <summary>Locations of the repository, the overlay executables and the ignored results folder.</summary>
internal static class Paths
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string ResultsRoot => Path.Combine(RepositoryRoot, "perf-results");

    public static string OverlayExe(Technique t) => t == Technique.C
        ? Path.Combine(RepositoryRoot, "spikes", "src", "Halo.Spikes.CompositionBaseline", "bin", "Release", "net10.0-windows10.0.19041.0", "Halo.Spikes.CompositionBaseline.exe")
        : Path.Combine(RepositoryRoot, "spikes", "src", "Halo.Spikes.Overlay", "bin", "Release", "net10.0-windows", "Halo.Spikes.Overlay.exe");

    /// <summary>SHA-256 (first 16 hex chars) of the overlay's main assembly: the build identity of a run.</summary>
    public static string BuildHash(Technique t)
    {
        var dll = Path.ChangeExtension(OverlayExe(t), ".dll");
        using var stream = File.OpenRead(dll);
        return Convert.ToHexString(SHA256.HashData(stream))[..16].ToLowerInvariant();
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Halo.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Halo.slnx) not found.");
    }
}

/// <summary>One parsed overlay log line.</summary>
internal sealed record OverlayEvent(string Name, JsonObject Fields)
{
    public long Qpc => Fields["qpc"]?.GetValue<long>() ?? 0;

    public double Num(string key) => Fields[key]?.GetValue<double>() ?? double.NaN;

    public string? Tok(string key) => Fields[key]?.GetValue<string>();
}

/// <summary>Starts an overlay (A/B/C), forwards commands to its standard input and collects its log events.</summary>
internal sealed class OverlayProcess : IDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<OverlayEvent> _events = new();
    private readonly List<string> _raw = [];
    private readonly Lock _rawGate = new();

    private OverlayProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, e) => OnLine(e.Data);
        _process.BeginOutputReadLine();
    }

    public long StartQpc { get; private init; }

    public int Pid => _process.Id;

    public Process Process => _process;

    public nint Hwnd { get; private set; }

    public static OverlayProcess Start(Technique technique, IEnumerable<string> extraArgs)
    {
        var info = new ProcessStartInfo(Paths.OverlayExe(technique))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("--technique");
        info.ArgumentList.Add(technique.ToString());
        foreach (var a in extraArgs)
        {
            info.ArgumentList.Add(a);
        }

        var start = Stopwatch.GetTimestamp();
        var process = Process.Start(info) ?? throw new InvalidOperationException("Overlay did not start.");
        return new OverlayProcess(process) { StartQpc = start };
    }

    public IReadOnlyList<OverlayEvent> Events => _events.ToArray();

    public IEnumerable<OverlayEvent> Named(string name) => _events.Where(e => e.Name == name);

    public void Send(OverlayCommand command)
    {
        _process.StandardInput.WriteLine(command.ToWire());
        _process.StandardInput.Flush();
    }

    /// <summary>Waits until an event matching the predicate appears; returns it or null on timeout.</summary>
    public OverlayEvent? WaitFor(string name, TimeSpan timeout, Func<OverlayEvent, bool>? predicate = null)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var found = _events.FirstOrDefault(e => e.Name == name && (predicate is null || predicate(e)));
            if (found is not null)
            {
                return found;
            }

            if (_process.HasExited)
            {
                break;
            }

            Thread.Sleep(5);
        }

        return _events.FirstOrDefault(e => e.Name == name && (predicate is null || predicate(e)));
    }

    public OverlayEvent WaitReady(TimeSpan timeout)
    {
        var ready = WaitFor("overlay.ready", timeout) ?? throw new InvalidOperationException("Overlay did not report ready.");
        Hwnd = (nint)(long)ready.Num("hwnd");
        return ready;
    }

    /// <summary>Sends quit and waits for exit. Returns the exit code, or null if it had to be killed.</summary>
    public int? Quit(TimeSpan timeout)
    {
        if (!_process.HasExited)
        {
            try
            {
                Send(OverlayCommand.Quit);
            }
            catch (IOException)
            {
                // Standard input already closed.
            }
        }

        if (_process.WaitForExit(timeout))
        {
            _process.WaitForExit();
            return _process.ExitCode;
        }

        _process.Kill();
        _process.WaitForExit();
        return null;
    }

    public void SaveLog(string path)
    {
        lock (_rawGate)
        {
            File.WriteAllLines(path, _raw);
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit();
        }

        _process.Dispose();
    }

    private void OnLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_rawGate)
        {
            _raw.Add(line);
        }

        try
        {
            if (JsonNode.Parse(line) is JsonObject obj && obj["event"]?.GetValue<string>() is { } name)
            {
                _events.Enqueue(new OverlayEvent(name, obj));
            }
        }
        catch (JsonException)
        {
            // Not a log line; kept in the raw log only.
        }
    }
}

/// <summary>Validity conditions of PROTOTYPE_S1_S2_REPORT §8.4, collected per run.</summary>
internal sealed class Validity
{
    private readonly List<string> _violations = [];

    public IReadOnlyList<string> Violations => _violations;

    public bool IsValid => _violations.Count == 0;

    public void Require(bool condition, string violationToken)
    {
        if (!condition && !_violations.Contains(violationToken))
        {
            _violations.Add(violationToken);
        }
    }
}

/// <summary>Environment as recorded at the start and end of a run.</summary>
internal sealed record RunEnvironment(EnvironmentSnapshot Snapshot, PxRect WorkArea, int Dpi, bool Elevated, bool NusaRunning)
{
    public const string BestPerformanceOverlay = "ded574b5-45a0-4f42-8737-46345c09c238";

    public static RunEnvironment Capture()
    {
        var snapshot = EnvironmentSnapshot.Capture();
        var (work, dpi) = Win32.PrimaryWorkArea();
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        // Read-only check: the reference app is never started, stopped or otherwise touched.
        var nusa = Process.GetProcessesByName("nusa").Length > 0;
        return new RunEnvironment(snapshot, PxRect.FromEdges(work.Left, work.Top, work.Right, work.Bottom), (int)dpi, elevated, nusa);
    }

    public void Check(Validity validity, int expectedHz, string phase)
    {
        validity.Require(Snapshot.PrimaryRefreshHz == expectedHz, $"refresh_mismatch_{phase}");
        validity.Require(Snapshot.PrimaryWidthPx == 1920 && Snapshot.PrimaryHeightPx == 1080, $"resolution_mismatch_{phase}");
        validity.Require(Dpi == 120, $"dpi_mismatch_{phase}");
        validity.Require(Snapshot.PowerSource == PowerSource.AC, $"not_on_ac_{phase}");
        validity.Require(string.Equals(Snapshot.PowerModeOverlay?.ToString("D"), BestPerformanceOverlay, StringComparison.OrdinalIgnoreCase), $"power_mode_mismatch_{phase}");
        validity.Require(!Elevated, $"elevated_{phase}");
        validity.Require(!NusaRunning, $"reference_app_running_{phase}");
    }

    public JsonObject ToJson() => new()
    {
        ["os"] = Snapshot.OsVersion,
        ["os_display_version"] = Snapshot.OsDisplayVersion,
        ["runtime"] = Snapshot.RuntimeVersion,
        ["width_px"] = Snapshot.PrimaryWidthPx,
        ["height_px"] = Snapshot.PrimaryHeightPx,
        ["refresh_hz"] = Snapshot.PrimaryRefreshHz,
        ["dpi"] = Dpi,
        ["work_area"] = $"{WorkArea.Left},{WorkArea.Top},{WorkArea.Right},{WorkArea.Bottom}",
        ["power"] = Snapshot.PowerSource.ToString(),
        ["battery_percent"] = Snapshot.BatteryPercent,
        ["power_overlay"] = Snapshot.PowerModeOverlay?.ToString("D"),
        ["elevated"] = Elevated,
        ["reference_app_running"] = NusaRunning,
        ["captured_utc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    };
}

/// <summary>Artifacts of one run in perf-results/&lt;date&gt;/&lt;run-id&gt;/.</summary>
internal sealed class RunArtifacts
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public RunArtifacts(string runId, string? batchDate = null)
    {
        RunId = runId;
        Directory = Path.Combine(Paths.ResultsRoot, batchDate ?? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), runId);
        if (System.IO.Directory.Exists(Directory))
        {
            throw new InvalidOperationException($"Run directory already exists: {runId}. Results are never overwritten.");
        }

        System.IO.Directory.CreateDirectory(Directory);
    }

    public string RunId { get; }

    public string Directory { get; }

    public string File(string name) => Path.Combine(Directory, name);

    public void WriteJson(string name, JsonNode node) => System.IO.File.WriteAllText(File(name), node.ToJsonString(Indented));

    public void WriteCsv(string name, string header, IEnumerable<string> rows) =>
        System.IO.File.WriteAllLines(File(name), new[] { header }.Concat(rows));

    public static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}

/// <summary>Contamination and placement checks against other windows (no titles or names are read).</summary>
internal static class WindowChecks
{
    /// <summary>Visible, uncloaked top-level windows above <paramref name="stopAt"/> that intersect the region,
    /// excluding our own windows.</summary>
    public static int ForeignWindowsAbove(nint stopAt, PxRect region, IReadOnlySet<nint> ours)
    {
        var count = 0;
        for (var w = Win32.GetTopWindow(0); w != 0 && w != stopAt; w = Win32.GetWindow(w, Win32.GwHwndNext))
        {
            if (ours.Contains(w) || !Win32.IsWindowVisible(w) || Win32.IsCloaked(w))
            {
                continue;
            }

            var r = Win32.WindowRect(w);
            var rect = PxRect.FromEdges(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width > 0 && rect.Height > 0 && rect.Intersects(region))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>True if <paramref name="upper"/> comes before <paramref name="lower"/> in the top-level Z-order.</summary>
    public static bool IsAbove(nint upper, nint lower)
    {
        for (var w = Win32.GetTopWindow(0); w != 0; w = Win32.GetWindow(w, Win32.GwHwndNext))
        {
            if (w == upper)
            {
                return true;
            }

            if (w == lower)
            {
                return false;
            }
        }

        return false;
    }

    public static nint RootAt(int x, int y) => Win32.GetAncestor(Win32.WindowFromPoint(new Point { X = x, Y = y }), Win32.GaRoot);

    public static bool CursorIn(PxRect region) => Win32.GetCursorPos(out var p) && region.Contains(p.X, p.Y);

    /// <summary>Moves the cursor below the canvas so hover cannot occur during unattended runs.</summary>
    public static void ParkCursor(PxRect canvas) => Win32.SetCursorPos((int)canvas.CenterX, canvas.Bottom + 300);
}
