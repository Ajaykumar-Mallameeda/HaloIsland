using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;
using Xunit;

namespace Halo.Spikes.Tests;

/// <summary>Needs an interactive desktop; briefly shows the test surface at the top of the screen.</summary>
[Trait("Category", "Integration")]
public class InteropIntegrationTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    [InlineData("C")]
    public void TechniqueStartsTransitionsAndQuitsWhileCaptureAttributesUpdates(string technique)
    {
        var (work, dpi) = Win32.PrimaryWorkArea();
        var geometry = IslandGeometry.Compute(PxRect.FromEdges(work.Left, work.Top, work.Right, work.Bottom), (int)dpi);
        using var capture = DesktopDuplication.Create(geometry.Canvas);
        capture.Start();
        using var counters = new ProcessCounters();

        var events = new ConcurrentQueue<JsonObject>();
        using var process = Start(technique, events);
        var ready = Wait(events, "overlay.ready", process);
        Assert.Equal(geometry.Canvas.Left, ready["left"]!.GetValue<double>());
        Assert.Equal(geometry.Canvas.Width, ready["width"]!.GetValue<double>());
        var config = events.First(e => e["event"]!.GetValue<string>() == "overlay.config");
        Assert.Equal(0, config["calibration"]!.GetValue<double>());

        var sample = counters.Read(process.Id, 0);
        Assert.NotEmpty(sample.ContextSwitchesByThread);

        var before = Stopwatch.GetTimestamp();
        process.StandardInput.WriteLine("expand");
        Wait(events, "transition.end", process);
        Thread.Sleep(300);
        process.StandardInput.WriteLine("quit");
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(10)), "overlay did not exit");
        Assert.Equal(0, process.ExitCode);

        capture.Stop();
        Assert.Equal(0, capture.ErrorHResult);
        Assert.True(capture.Frames().Count(f => f.Attributed && f.Qpc > before) >= 2, "expected attributed desktop updates during the transition");
    }

    private static Process Start(string technique, ConcurrentQueue<JsonObject> events)
    {
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Debug" : "Release";
        var info = technique == "C"
            ? new ProcessStartInfo(Path.Combine(RepoRoot(), "spikes", "src", "Halo.Spikes.CompositionBaseline", "bin", configuration, "net10.0-windows10.0.19041.0", "Halo.Spikes.CompositionBaseline.exe"))
            : new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") { ArgumentList = { Path.Combine(AppContext.BaseDirectory, "Halo.Spikes.Overlay.dll") } };
        info.ArgumentList.Add("--technique");
        info.ArgumentList.Add(technique);
        info.UseShellExecute = false;
        info.RedirectStandardInput = true;
        info.RedirectStandardOutput = true;
        info.CreateNoWindow = true;
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line && JsonNode.Parse(line) is JsonObject o)
            {
                events.Enqueue(o);
            }
        };
        process.BeginOutputReadLine();
        return process;
    }

    private static JsonObject Wait(ConcurrentQueue<JsonObject> events, string name, Process process)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(20) && !process.HasExited)
        {
            var found = events.FirstOrDefault(e => e["event"]?.GetValue<string>() == name);
            if (found is not null)
            {
                return found;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"No '{name}' event.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Halo.slnx")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
