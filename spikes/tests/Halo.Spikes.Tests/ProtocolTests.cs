using System.IO;
using Halo.Spikes.Measurement;
using Xunit;

namespace Halo.Spikes.Tests;

public class OutcomeTests
{
    [Fact]
    public void LabelsMatchReportVocabulary()
    {
        Assert.Equal("PASS", Outcome.Pass.Label());
        Assert.Equal("FAIL", Outcome.Fail.Label());
        Assert.Equal("INVALID", Outcome.Invalid.Label());
        Assert.Equal("BLOCKED", Outcome.Blocked.Label());
        Assert.Equal("NOT RUN", Outcome.NotRun.Label());
    }

    [Fact]
    public void CombineNeverTurnsMissingDataIntoAPass()
    {
        Assert.Equal(Outcome.Pass, OutcomeExtensions.Combine([Outcome.Pass, Outcome.Pass]));
        Assert.Equal(Outcome.Fail, OutcomeExtensions.Combine([Outcome.Pass, Outcome.Invalid, Outcome.Fail]));
        Assert.Equal(Outcome.Invalid, OutcomeExtensions.Combine([Outcome.Pass, Outcome.Invalid]));
        Assert.Equal(Outcome.Blocked, OutcomeExtensions.Combine([Outcome.Pass, Outcome.Blocked]));
        Assert.Equal(Outcome.NotRun, OutcomeExtensions.Combine([Outcome.Pass, Outcome.NotRun]));
        Assert.Equal(Outcome.NotRun, OutcomeExtensions.Combine([]));
    }
}

public class IslandGeometryTests
{
    private static readonly PxRect Work125 = new(0, 0, 1920, 1032);

    [Fact]
    public void CanvasIsCentredUnderTheTopEdgeAt125Percent()
    {
        var g = IslandGeometry.Compute(Work125, 120);

        Assert.Equal(new PxRect(667, 0, 585, 260), g.Canvas);
        Assert.Equal(1.25, g.Scale);
        Assert.Equal(275, g.Compact.Width, 6);
        Assert.Equal(50, g.Compact.Height, 6);
        Assert.Equal(25, g.Compact.Radius, 6);
        Assert.Equal(155, g.Compact.Left, 6);
        Assert.Equal(0, g.Compact.Top, 6);
        Assert.Equal(525, g.Expanded.Width, 6);
        Assert.Equal(30, g.Expanded.Left, 6);
    }

    [Fact]
    public void WorkAreaOffsetMovesCanvas()
    {
        var g = IslandGeometry.Compute(new PxRect(-1920, 40, 1920, 1000), 96);

        Assert.Equal(new PxRect(-1920 + 726, 40, 468, 208), g.Canvas);
    }

    [Theory]
    [InlineData(96, 468, 208)]
    [InlineData(144, 702, 312)]
    [InlineData(168, 819, 364)]
    public void CanvasScalesWithDpi(int dpi, int width, int height)
    {
        var g = IslandGeometry.Compute(new PxRect(0, 0, 3840, 2100), dpi);

        Assert.Equal(width, g.Canvas.Width);
        Assert.Equal(height, g.Canvas.Height);
    }

    [Fact]
    public void RejectsWorkAreaSmallerThanCanvas()
    {
        Assert.Throws<ArgumentException>(() => IslandGeometry.Compute(new PxRect(0, 0, 400, 300), 96));
    }

    [Fact]
    public void SignedDistanceIsNegativeInsideAndPositiveOutside()
    {
        var r = new RoundedRect(0, 0, 100, 40, 20);

        Assert.Equal(-20, r.SignedDistance(50, 20), 6);
        Assert.Equal(10, r.SignedDistance(50, 50), 6);
        Assert.Equal(10, r.SignedDistance(110, 20), 6);
        // A corner point inside the bounding box but outside the rounding.
        Assert.True(r.SignedDistance(1, 1) > 0);
        Assert.True(r.SignedDistance(20, 1) < 0.5);
    }

    [Fact]
    public void ScreenShapeAddsCanvasOrigin()
    {
        var g = IslandGeometry.Compute(Work125, 120);

        Assert.Equal(667 + 155, g.ScreenShape(false).Left, 6);
        Assert.Equal(667 + 30, g.ScreenShape(true).Left, 6);
    }

    [Fact]
    public void PxRectIntersectionExcludesTouchingEdges()
    {
        var a = new PxRect(0, 0, 10, 10);

        Assert.True(a.Intersects(new PxRect(9, 9, 5, 5)));
        Assert.False(a.Intersects(new PxRect(10, 0, 5, 5)));
        Assert.True(a.Contains(0, 0));
        Assert.False(a.Contains(10, 10));
    }
}

public class ContextSwitchTests
{
    private static ThreadSample S(double t, params (int Tid, long Count)[] counts) =>
        new(t, counts.ToDictionary(c => c.Tid, c => c.Count));

    [Fact]
    public void SumsPositiveDeltasPerThread()
    {
        var r = ContextSwitches.Aggregate([S(0, (1, 100), (2, 50)), S(10, (1, 110), (2, 50)), S(20, (1, 115), (2, 60))]);

        Assert.Equal(25, r.TotalSwitches);
        Assert.Equal(20, r.WindowSeconds);
        Assert.Equal(1.25, r.PerSecond, 6);
        Assert.False(r.NewThreadStarted);
        Assert.Equal(0, r.ThreadsEnded);
    }

    [Fact]
    public void NewThreadIsFlaggedButEndedThreadIsOnlyCounted()
    {
        var started = ContextSwitches.Aggregate([S(0, (1, 100)), S(10, (1, 100), (7, 4))]);
        var ended = ContextSwitches.Aggregate([S(0, (1, 100), (7, 4)), S(10, (1, 101))]);

        Assert.True(started.NewThreadStarted);
        Assert.Equal(4, started.TotalSwitches); // a new thread's switches all happened inside the window
        Assert.Equal(0, started.ThreadsEnded);
        Assert.False(ended.NewThreadStarted);
        Assert.Equal(1, ended.ThreadsEnded);
        Assert.Equal(1, ended.TotalSwitches);
    }

    [Fact]
    public void ThreadIdReuseCountsAsNewThread()
    {
        var reused = ContextSwitches.Aggregate([S(0, (1, 100), (7, 40)), S(10, (1, 100), (7, 3))]);

        Assert.True(reused.NewThreadStarted);
        Assert.Equal(0, reused.TotalSwitches);
    }

    [Fact]
    public void EndedThreadDoesNotInvalidateLaterSamples()
    {
        var r = ContextSwitches.Aggregate([S(0, (1, 100), (7, 4)), S(10, (1, 100)), S(20, (1, 102))]);

        Assert.False(r.NewThreadStarted);
        Assert.Equal(1, r.ThreadsEnded);
        Assert.Equal(2, r.TotalSwitches);
    }

    [Fact]
    public void RequiresIncreasingTimes()
    {
        Assert.Throws<ArgumentException>(() => ContextSwitches.Aggregate([S(0, (1, 1))]));
        Assert.Throws<ArgumentException>(() => ContextSwitches.Aggregate([S(5, (1, 1)), S(5, (1, 2))]));
    }
}

public class FrameTimelineTests
{
    private const long Freq = 10_000_000; // 100 ns ticks, like this machine's QPC
    private const double Refresh = 1000.0 / 120;

    private static long Ms(double ms) => (long)Math.Round(ms * Freq / 1000);

    private static List<FrameRecord> Frames(params double[] ms) => ms.Select(m => new FrameRecord(Ms(m), false)).ToList();

    [Fact]
    public void UsesFramesAfterStartUntilEndPlusGrace()
    {
        var frames = Frames(0, 10, 18.3, 26.7, 35, 400, 420, 500);
        var t = FrameTimeline.ForTransition(frames, new TransitionMark(Ms(5), Ms(395)), Freq, Refresh);

        // 10, 18.3, 26.7, 35, 400 (end 395 + 3 x 8.33 ms grace = 420 → 420 included, 500 not).
        Assert.True(t.Valid);
        Assert.Equal(6, t.FrameCount);
        Assert.Equal(5, t.FirstFrameLatencyMs!.Value, 3);
        Assert.Equal(365, t.MaxIntervalMs, 3);
    }

    [Fact]
    public void CoalescedOrTooFewFramesMakeTransitionInvalid()
    {
        var coalesced = new List<FrameRecord> { new(Ms(10), false), new(Ms(20), true), new(Ms(30), false) };

        Assert.Equal("coalesced", FrameTimeline.ForTransition(coalesced, new TransitionMark(0, Ms(30)), Freq, Refresh).InvalidReason);
        Assert.Equal("too_few_frames", FrameTimeline.ForTransition(Frames(10), new TransitionMark(0, Ms(30)), Freq, Refresh).InvalidReason);
    }

    [Fact]
    public void Pb05EvaluatesFirstFiftyValidTransitions()
    {
        var smooth = Enumerable.Range(0, 43).Select(i => i * Refresh).ToArray();
        var good = FrameTimeline.ForTransition(Frames(smooth), new TransitionMark(-1, Ms(350)), Freq, Refresh);
        var invalid = FrameTimeline.ForTransition(Frames(1), new TransitionMark(0, Ms(350)), Freq, Refresh);
        var stutter = Enumerable.Range(0, 43).Select(i => i * Refresh).Concat([400.0, 450.0]).ToArray();
        var bad = FrameTimeline.ForTransition(Frames(stutter), new TransitionMark(-1, Ms(450)), Freq, Refresh);

        var passing = FrameTimeline.EvaluatePb05(Enumerable.Repeat(invalid, 10).Concat(Enumerable.Repeat(good, 50)).Concat([bad]), Refresh);
        var tooFew = FrameTimeline.EvaluatePb05(Enumerable.Repeat(invalid, 11).Concat(Enumerable.Repeat(good, 49)), Refresh);
        var failing = FrameTimeline.EvaluatePb05(Enumerable.Repeat(good, 49).Concat([bad]), Refresh);

        Assert.Equal(Outcome.Pass, passing.Outcome);
        Assert.Equal(Outcome.Invalid, tooFew.Outcome);
        Assert.Equal(Outcome.Fail, failing.Outcome);
        Assert.Equal(2, failing.WorstLongFrames);
    }

    [Fact]
    public void CountsFramesInHalfOpenWindow()
    {
        Assert.Equal(2, FrameTimeline.CountInWindow(Frames(0, 5, 10, 15), Ms(0), Ms(10)));
    }
}

public class CalibrationRulesTests
{
    private const long Freq = 10_000_000;

    private static TransitionFrames T(double maxInterval, bool valid = true) =>
        new(valid, valid ? null : "coalesced", valid ? [8.3, maxInterval] : [], 1, valid ? 3 : 0);

    [Fact]
    public void PositiveControlRequiresRefreshCadence()
    {
        static List<FrameRecord> Every(double ms, int n) => Enumerable.Range(0, n).Select(i => new FrameRecord((long)(i * ms * Freq / 1000), false)).ToList();

        Assert.Equal(Outcome.Pass, CalibrationRules.Positive(Every(1000.0 / 120, 1200), 10, 120, Freq));
        Assert.Equal(Outcome.Fail, CalibrationRules.Positive(Every(1000.0 / 60, 600), 10, 120, Freq)); // half rate
        Assert.Equal(Outcome.Fail, CalibrationRules.Positive([], 10, 120, Freq));
    }

    [Fact]
    public void NegativeControlMustDetectEveryStall()
    {
        Assert.Equal(Outcome.Pass, CalibrationRules.Negative(Enumerable.Repeat(T(55), 20).ToArray(), 20));
        Assert.Equal(Outcome.Fail, CalibrationRules.Negative(Enumerable.Repeat(T(55), 19).Append(T(12)).ToArray(), 20));
        Assert.Equal(Outcome.Invalid, CalibrationRules.Negative(Enumerable.Repeat(T(55), 19).Append(T(0, valid: false)).ToArray(), 20));
        Assert.Equal(Outcome.Invalid, CalibrationRules.Negative(Enumerable.Repeat(T(55), 19).ToArray(), 20));
    }

    [Fact]
    public void UnstalledControlAllowsAtMostTwoGaps()
    {
        Assert.Equal(Outcome.Pass, CalibrationRules.NegativeControl(Enumerable.Repeat(T(9), 18).Concat(Enumerable.Repeat(T(45), 2)).ToArray(), 20));
        Assert.Equal(Outcome.Fail, CalibrationRules.NegativeControl(Enumerable.Repeat(T(9), 17).Concat(Enumerable.Repeat(T(45), 3)).ToArray(), 20));
    }

    [Fact]
    public void TickRequiresOneFramePerTickAndNoStrayFrame()
    {
        long Ms(double ms) => (long)(ms * Freq / 1000);
        long[] ticks = [Ms(0), Ms(5000)];

        Assert.Equal(Outcome.Pass, CalibrationRules.Tick(ticks, [new(Ms(30), false), new(Ms(5040), false)], Freq));
        Assert.Equal(Outcome.Fail, CalibrationRules.Tick(ticks, [new(Ms(30), false)], Freq)); // second tick unseen
        Assert.Equal(Outcome.Fail, CalibrationRules.Tick(ticks, [new(Ms(30), false), new(Ms(2000), false), new(Ms(5040), false)], Freq)); // stray frame
        Assert.Equal(Outcome.Invalid, CalibrationRules.Tick([], [], Freq));
    }

    [Fact]
    public void BaselineMustBeQuiet()
    {
        Assert.Equal(Outcome.Pass, CalibrationRules.Baseline(0));
        Assert.Equal(Outcome.Fail, CalibrationRules.Baseline(1));
    }
}

public class OverlayOptionsTests
{
    private static readonly HashSet<Technique> Wpf = [Technique.A, Technique.B];
    private static readonly HashSet<Technique> Composition = [Technique.C];

    [Fact]
    public void ParsesSmokeAndRunModes()
    {
        Assert.Equal(OverlayMode.Smoke, OverlayOptions.Parse(["--smoke"], Wpf).Mode);
        Assert.Equal("out.jsonl", OverlayOptions.Parse(["--log", "out.jsonl", "--smoke"], Wpf).LogPath);
        var run = OverlayOptions.Parse(["--technique", "B"], Wpf);
        Assert.Equal(new OverlayOptions(OverlayMode.Run, Technique.B, null, false, 0, 0, false), run);
    }

    [Fact]
    public void CalibrationFeaturesRequireCalibrationFlag()
    {
        Assert.Equal(OverlayMode.Invalid, OverlayOptions.Parse(["--technique", "A", "--stall-ms", "50"], Wpf).Mode);
        Assert.Equal(OverlayMode.Invalid, OverlayOptions.Parse(["--technique", "A", "--tick-ms", "5000"], Wpf).Mode);
        Assert.Equal(OverlayMode.Invalid, OverlayOptions.Parse(["--technique", "C", "--continuous"], Composition).Mode);

        var cal = OverlayOptions.Parse(["--technique", "A", "--calibration", "--stall-ms", "50"], Wpf);
        Assert.True(cal.Calibration);
        Assert.Equal(50, cal.StallMs);
    }

    [Theory]
    [InlineData]
    [InlineData("--unknown")]
    [InlineData("--smoke", "--smoke")]
    [InlineData("--smoke", "--log")]
    [InlineData("--smoke", "--technique", "A")]
    [InlineData("--technique", "C")]
    [InlineData("--technique", "a")]
    [InlineData("--technique", "AB")]
    [InlineData("--technique", "0")]
    [InlineData("--technique", "A", "--calibration", "--stall-ms", "0")]
    [InlineData("--technique", "A", "--calibration", "--stall-ms", "1001")]
    [InlineData("--technique", "A", "--calibration", "--tick-ms", "99")]
    [InlineData("--technique", "A", "--technique", "B")]
    public void RejectsInvalidArgumentsForWpf(params string[] args)
    {
        Assert.Equal(OverlayMode.Invalid, OverlayOptions.Parse(args, Wpf).Mode);
    }

    [Fact]
    public void StallIsRejectedForComposition()
    {
        Assert.Equal(OverlayMode.Invalid, OverlayOptions.Parse(["--technique", "C", "--calibration", "--stall-ms", "50"], Composition).Mode);
        Assert.True(OverlayOptions.Parse(["--technique", "C", "--calibration", "--continuous"], Composition).Continuous);
    }

    [Theory]
    [InlineData("expand", OverlayCommand.Expand)]
    [InlineData(" collapse ", OverlayCommand.Collapse)]
    [InlineData("quit", OverlayCommand.Quit)]
    [InlineData("hover-on", OverlayCommand.HoverOn)]
    [InlineData("EXPAND", OverlayCommand.Unknown)]
    [InlineData(null, OverlayCommand.Unknown)]
    public void ParsesCommands(string? line, OverlayCommand expected)
    {
        Assert.Equal(expected, OverlayCommands.Parse(line));
    }

    [Fact]
    public void CommandsRoundTrip()
    {
        foreach (var c in Enum.GetValues<OverlayCommand>().Where(c => c != OverlayCommand.Unknown))
        {
            Assert.Equal(c, OverlayCommands.Parse(c.ToWire()));
        }
    }
}

public class InteropSurfaceTests
{
    /// <summary>Protocol G5: no spike assembly may be able to change the display mode.</summary>
    [Fact]
    public void NoSpikeAssemblyDeclaresDisplayModeSetters()
    {
        var assemblies = new[] { typeof(Halo.Spikes.Windows.EnvironmentSnapshot).Assembly, typeof(Halo.Spikes.Overlay.Program).Assembly };
        var methods = assemblies
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(m => m.Name)
            .ToList();

        Assert.NotEmpty(methods);
        Assert.DoesNotContain(methods, n => n.StartsWith("ChangeDisplaySettings", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(methods, n => n.Contains("SetDisplayConfig", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Same rule over every spike source file, including the harness and the C baseline.</summary>
    [Fact]
    public void NoSpikeSourceCallsDisplayModeSetters()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Halo.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var sources = Directory.GetFiles(Path.Combine(dir!.FullName, "spikes", "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(sources);
        foreach (var file in sources)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("ChangeDisplaySettings", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SetDisplayConfig", text, StringComparison.Ordinal);
        }
    }
}
