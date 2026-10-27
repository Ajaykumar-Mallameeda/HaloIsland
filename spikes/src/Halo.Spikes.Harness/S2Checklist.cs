using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Media;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Harness;

/// <summary>
/// Automated S2 items (PROTOTYPE_S1_S2_REPORT §8.11). Input is real (SendInput via FlaUI / SetCursorPos), so the
/// machine must not be used during the run. UI Automation names are compared in memory only and never logged.
/// </summary>
internal sealed class S2Checklist : IDisposable
{
    private const int EdgeMargin = 4;
    private const string TestString = "halo s2 keys";
    private static int s_screenWidth = 1920;
    private static int s_screenHeight = 1080;
    private readonly Technique _technique;
    private readonly RunArtifacts _artifacts;
    private readonly JsonObject _items = [];
    private readonly UiThread _ui = new();
    private IslandGeometry _geo = null!;
    private Probe _probe = null!;
    private OverlayProcess _overlay = null!;
    private DesktopDuplication _dd = null!;

    private S2Checklist(Technique technique, RunArtifacts artifacts)
    {
        _technique = technique;
        _artifacts = artifacts;
    }

    public static JsonObject Run(Technique technique, string runId, string batchDate, string? buildHash)
    {
        var artifacts = new RunArtifacts(runId, batchDate);
        using var s2 = new S2Checklist(technique, artifacts);
        return s2.Execute(buildHash);
    }

    public void Dispose() => _ui.Dispose();

    private JsonObject Execute(string? buildHash)
    {
        var validity = new Validity();
        var env = RunEnvironment.Capture();
        env.Check(validity, env.Snapshot.PrimaryRefreshHz, "start");
        _artifacts.WriteJson("env-start.json", env.ToJson());
        _geo = IslandGeometry.Compute(env.WorkArea, env.Dpi);
        var hash = Paths.BuildHash(_technique);
        validity.Require(buildHash is null || buildHash == hash, "build_changed");
        s_screenWidth = env.Snapshot.PrimaryWidthPx;
        s_screenHeight = env.Snapshot.PrimaryHeightPx;

        _probe = new Probe(_ui);
        ActivateProbe();
        _dd = DesktopDuplication.Create(_geo.Canvas);
        _dd.Start();
        _overlay = OverlayProcess.Start(_technique, []);
        try
        {
            _overlay.WaitReady(TimeSpan.FromSeconds(15));
            Thread.Sleep(500);
            Item01Start();
            Item08Topmost();
            Item09Placement();
            Item02And03And04();
            Item05Routing();
            Item09FixedCanvas();
            Item06NoActivation();
            Item07TaskbarAndAltTab();
            Item12Automation();
            Item10Suppression();
            Item11Automated();
            Item01End();
        }
        finally
        {
            _dd.Dispose();
            _overlay.SaveLog(_artifacts.File("overlay.jsonl"));
            _overlay.Dispose();
            _probe.Close();
        }

        var summary = new JsonObject
        {
            ["run_id"] = _artifacts.RunId,
            ["kind"] = "s2",
            ["technique"] = _technique.ToString(),
            ["protocol"] = "v1.3",
            ["build_hash"] = hash,
            ["validity"] = new JsonObject { ["valid"] = validity.IsValid, ["violations"] = new JsonArray(validity.Violations.Select(v => (JsonNode)v).ToArray()) },
            ["items"] = _items,
        };
        if (!validity.IsValid)
        {
            foreach (var (_, node) in _items)
            {
                node!["outcome"] = Outcome.Invalid.Label();
            }
        }

        _artifacts.WriteJson("summary.json", summary);
        return summary;
    }

    private void Record(string id, Outcome outcome, JsonObject evidence)
    {
        evidence["outcome"] = outcome.Label();
        _items[id] = evidence;
    }

    private static Outcome Of(bool pass) => pass ? Outcome.Pass : Outcome.Fail;

    private bool ProbeForeground() => Win32.GetForegroundWindow() == _probe.Hwnd;

    private void ActivateProbe()
    {
        var p = _probe.TextBoxCenter;
        for (var i = 0; i < 3 && !ProbeForeground(); i++)
        {
            Click(p.X, p.Y);
            Thread.Sleep(300);
        }
    }

    private static void Click(int x, int y)
    {
        PointerInput.ClickAt(x, y, s_screenWidth, s_screenHeight);
        Thread.Sleep(15);
    }

    private void Park() => Win32.SetCursorPos((int)_geo.Canvas.CenterX, _geo.Canvas.Bottom + 300);

    private bool WaitTransitionEnd(int afterCount, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (_overlay.Named("transition.end").Count() > afterCount)
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return false;
    }

    private void Command(OverlayCommand command, bool waitTransition = true)
    {
        var before = _overlay.Named("transition.end").Count();
        _overlay.Send(command);
        if (waitTransition)
        {
            WaitTransitionEnd(before, TimeSpan.FromSeconds(3));
            Thread.Sleep(250);
        }
    }

    private void Item01Start()
    {
        var hwnd = _overlay.Hwnd;
        var r = Win32.WindowRect(hwnd);
        var c = _geo.Canvas;
        var rectOk = r.Left == c.Left && r.Top == c.Top && r.Right == c.Right && r.Bottom == c.Bottom;
        _items["S2-01"] = new JsonObject
        {
            ["visible"] = Win32.IsWindowVisible(hwnd),
            ["rect_matches"] = rectOk,
            ["responsive"] = Win32.IsResponsive(hwnd, 1000),
        };
    }

    private void Item01End()
    {
        var hwnd = _overlay.Hwnd;
        var sw = Stopwatch.StartNew();
        var code = _overlay.Quit(TimeSpan.FromSeconds(5));
        var item = (JsonObject)_items["S2-01"]!;
        item["exit_code"] = code;
        item["exit_ms"] = sw.Elapsed.TotalMilliseconds;
        item["window_gone"] = !Win32.IsWindow(hwnd);
        item["process_gone"] = _overlay.Process.HasExited;
        var pass = item["visible"]!.GetValue<bool>() && item["rect_matches"]!.GetValue<bool>() && item["responsive"]!.GetValue<bool>()
            && code == 0 && sw.Elapsed < TimeSpan.FromSeconds(5) && !Win32.IsWindow(hwnd) && _overlay.Process.HasExited;
        Record("S2-01", Of(pass), item);
    }

    private void Item08Topmost()
    {
        var shape = _geo.ScreenShape(false);
        var root = WindowChecks.RootAt((int)(shape.Left + (shape.Width / 2)), (int)(shape.Top + (shape.Height / 2)));
        var above = WindowChecks.IsAbove(_overlay.Hwnd, _probe.Hwnd);
        Record("S2-08", Of(root == _overlay.Hwnd && above && ProbeForeground()), new JsonObject
        {
            ["window_from_point_is_overlay"] = root == _overlay.Hwnd,
            ["overlay_above_probe"] = above,
            ["probe_is_foreground"] = ProbeForeground(),
        });
    }

    private void Item09Placement()
    {
        var (work, _) = Win32.PrimaryWorkArea();
        var r = Win32.WindowRect(_overlay.Hwnd);
        var centreError = Math.Abs(((r.Left + r.Right) / 2.0) - ((work.Left + work.Right) / 2.0));
        var topError = (double)Math.Abs(r.Top - work.Top);
        _items["S2-09"] = new JsonObject { ["centre_error_px"] = centreError, ["top_error_px"] = topError };
    }

    private void Item09FixedCanvas()
    {
        var hwnd = _overlay.Hwnd;
        var initial = Win32.WindowRect(hwnd);
        var changes = 0;
        var samples = 0;
        for (var k = 0; k < 20; k++)
        {
            var before = _overlay.Named("transition.end").Count();
            _overlay.Send(k % 2 == 0 ? OverlayCommand.Expand : OverlayCommand.Collapse);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromMilliseconds(600))
            {
                var r = Win32.WindowRect(hwnd);
                samples++;
                changes += r.Left != initial.Left || r.Top != initial.Top || r.Right != initial.Right || r.Bottom != initial.Bottom ? 1 : 0;
                Thread.Sleep(10);
            }

            WaitTransitionEnd(before, TimeSpan.FromSeconds(2));
        }

        var item = (JsonObject)_items["S2-09"]!;
        item["rect_samples"] = samples;
        item["rect_changes"] = changes;
        var pass = item["centre_error_px"]!.GetValue<double>() <= 1 && item["top_error_px"]!.GetValue<double>() <= 1 && changes == 0;
        Record("S2-09", Of(pass), item);
    }

    /// <summary>Points ≥ margin outside every given shape (screen pixels), spread evenly over the canvas.</summary>
    private List<(int X, int Y)> OutsidePoints(int count, params RoundedRect[] shapes)
    {
        var c = _geo.Canvas;
        var candidates = new List<(int, int)>();
        for (var gy = 0; gy < 40; gy++)
        {
            for (var gx = 0; gx < 60; gx++)
            {
                var x = c.Left + 3 + (int)(gx * (c.Width - 6) / 59.0);
                var y = c.Top + 3 + (int)(gy * (c.Height - 6) / 39.0);
                if (shapes.All(s => s.SignedDistance(x, y) >= EdgeMargin))
                {
                    candidates.Add((x, y));
                }
            }
        }

        return Spread(candidates, count);
    }

    private static List<(int X, int Y)> InsidePoints(int count, RoundedRect shape)
    {
        var candidates = new List<(int, int)>();
        for (var gy = 0; gy < 30; gy++)
        {
            for (var gx = 0; gx < 60; gx++)
            {
                var x = (int)(shape.Left + 2 + (gx * (shape.Width - 4) / 59.0));
                var y = (int)(shape.Top + 2 + (gy * (shape.Height - 4) / 29.0));
                if (shape.SignedDistance(x, y) <= -EdgeMargin)
                {
                    candidates.Add((x, y));
                }
            }
        }

        return Spread(candidates, count);
    }

    private static List<(int X, int Y)> Spread(List<(int X, int Y)> candidates, int count)
    {
        if (candidates.Count < count)
        {
            throw new InvalidOperationException("Not enough test points.");
        }

        return Enumerable.Range(0, count).Select(i => candidates[(int)((long)i * candidates.Count / count)]).ToList();
    }

    /// <summary>Clicks each point; returns (reached probe at that point, reached overlay).</summary>
    private (int ToProbe, int ToOverlay) ClickAll(List<(int X, int Y)> points)
    {
        var probeBefore = _probe.Clicks.Count;
        var overlayBefore = _overlay.Named("click.inside").Count();
        foreach (var (x, y) in points)
        {
            Click(x, y);
            Thread.Sleep(60);
        }

        Thread.Sleep(300);
        var probeClicks = _probe.Clicks.Skip(probeBefore).ToList();
        var toProbe = points.Count(p => probeClicks.Any(c => Math.Abs(c.X - p.X) <= 1 && Math.Abs(c.Y - p.Y) <= 1));
        var toOverlay = _overlay.Named("click.inside").Count() - overlayBefore;
        Park();
        return (toProbe, toOverlay);
    }

    private JsonObject Transparency(bool expanded, string png)
    {
        var pixels = ScreenRegion.Capture(_geo.Canvas);
        Png.Save(pixels, _artifacts.File(png));
        var shape = expanded ? _geo.Expanded : _geo.Compact;
        int outside = 0, outsideOk = 0, interiorBlack = 0;
        for (var y = 0; y < pixels.Rect.Height; y++)
        {
            for (var x = 0; x < pixels.Rect.Width; x++)
            {
                var d = shape.SignedDistance(x + 0.5, y + 0.5);
                var (r, g, b) = pixels.At(x, y);
                if (d >= 2)
                {
                    outside++;
                    outsideOk += Near(r, g, b, Probe.ProbeColor) ? 1 : 0;
                }
                else if (d <= -2 && r <= 2 && g <= 2 && b <= 2)
                {
                    interiorBlack++;
                }
            }
        }

        return new JsonObject { ["outside_pixels"] = outside, ["outside_matching_probe"] = outsideOk, ["interior_black_pixels"] = interiorBlack, ["evidence_png"] = png };
    }

    private static bool Near(byte r, byte g, byte b, Color c) => Math.Abs(r - c.R) <= 2 && Math.Abs(g - c.G) <= 2 && Math.Abs(b - c.B) <= 2;

    private void Item02And03And04()
    {
        Park();
        var compactT = Transparency(false, "s2-02-compact.png");
        var outsideCompact = ClickAll(OutsidePoints(100, _geo.ScreenShape(false)));
        var insideCompact = ClickAll(InsidePoints(50, _geo.ScreenShape(false)));

        Command(OverlayCommand.Expand);
        var expandedT = Transparency(true, "s2-02-expanded.png");
        var outsideExpanded = ClickAll(OutsidePoints(100, _geo.ScreenShape(true)));
        var insideExpanded = ClickAll(InsidePoints(50, _geo.ScreenShape(true)));
        Command(OverlayCommand.Collapse);

        bool TransparencyPass(JsonObject t) => t["outside_pixels"]!.GetValue<int>() > 0
            && t["outside_pixels"]!.GetValue<int>() == t["outside_matching_probe"]!.GetValue<int>()
            && t["interior_black_pixels"]!.GetValue<int>() >= 1;
        Record("S2-02", Of(TransparencyPass(compactT) && TransparencyPass(expandedT)), new JsonObject { ["compact"] = compactT, ["expanded"] = expandedT });
        Record("S2-03", Of(outsideCompact.ToProbe == 100 && outsideExpanded.ToProbe == 100 && outsideCompact.ToOverlay == 0 && outsideExpanded.ToOverlay == 0), new JsonObject
        {
            ["compact_to_probe"] = outsideCompact.ToProbe,
            ["compact_to_overlay"] = outsideCompact.ToOverlay,
            ["expanded_to_probe"] = outsideExpanded.ToProbe,
            ["expanded_to_overlay"] = outsideExpanded.ToOverlay,
            ["clicks"] = 200,
        });
        Record("S2-04", Of(insideCompact.ToOverlay == 50 && insideExpanded.ToOverlay == 50 && insideCompact.ToProbe == 0 && insideExpanded.ToProbe == 0), new JsonObject
        {
            ["compact_to_overlay"] = insideCompact.ToOverlay,
            ["compact_to_probe"] = insideCompact.ToProbe,
            ["expanded_to_overlay"] = insideExpanded.ToOverlay,
            ["expanded_to_probe"] = insideExpanded.ToProbe,
            ["clicks"] = 100,
        });
    }

    private void Item05Routing()
    {
        var outsideUnion = OutsidePoints(40, _geo.ScreenShape(false), _geo.ScreenShape(true));
        var insideCompact = InsidePoints(40, _geo.ScreenShape(false));
        int toProbe = 0, toOverlay = 0, misroutedOutside = 0, insideAtProbe = 0;
        for (var k = 0; k < 20; k++)
        {
            var probeBefore = _probe.Clicks.Count;
            var overlayBefore = _overlay.Named("click.inside").Count();
            var endBefore = _overlay.Named("transition.end").Count();
            var startBefore = _overlay.Named("transition.start").Count();
            _overlay.Send(OverlayCommand.Expand);
            var wait = Stopwatch.StartNew();
            while (_overlay.Named("transition.start").Count() == startBefore && wait.Elapsed < TimeSpan.FromSeconds(1))
            {
                Thread.Sleep(1);
            }
            var o1 = outsideUnion[2 * k];
            var o2 = outsideUnion[(2 * k) + 1];
            var i1 = insideCompact[2 * k];
            var i2 = insideCompact[(2 * k) + 1];
            Click(o1.X, o1.Y);
            Click(i1.X, i1.Y);
            Click(o2.X, o2.Y);
            Click(i2.X, i2.Y);
            Park();
            WaitTransitionEnd(endBefore, TimeSpan.FromSeconds(2));
            Thread.Sleep(200);
            var probeClicks = _probe.Clicks.Skip(probeBefore).ToList();
            var hits = new[] { o1, o2 }.Count(p => probeClicks.Any(c => Math.Abs(c.X - p.X) <= 1 && Math.Abs(c.Y - p.Y) <= 1));
            toProbe += hits;
            misroutedOutside += 2 - hits;
            toOverlay += _overlay.Named("click.inside").Count() - overlayBefore;
            insideAtProbe += new[] { i1, i2 }.Count(p => probeClicks.Any(c => Math.Abs(c.X - p.X) <= 1 && Math.Abs(c.Y - p.Y) <= 1));
            Command(OverlayCommand.Collapse);
        }

        Record("S2-05", Of(toProbe == 40 && toOverlay == 40), new JsonObject
        {
            ["outside_union_to_probe"] = toProbe,
            ["outside_union_not_at_probe"] = misroutedOutside,
            ["inside_compact_to_overlay"] = toOverlay,
            ["inside_compact_at_probe"] = insideAtProbe,
            ["transitions"] = 20,
        });
    }

    private void Item06NoActivation()
    {
        // Give the probe's text box keyboard focus with a real click, then never touch the probe again until typing.
        var box = _probe.TextBoxCenter;
        Click(box.X, box.Y);
        Thread.Sleep(300);
        _probe.ClearText();
        var startForeground = ProbeForeground();
        _overlay.Send(OverlayCommand.HoverOn);
        Thread.Sleep(100);
        var shape = _geo.ScreenShape(false);
        var centre = ((int)(shape.Left + (shape.Width / 2)), (int)(shape.Top + (shape.Height / 2)));
        var below = ((int)_geo.Canvas.CenterX, _geo.Canvas.Bottom + 200);
        int checks = 0, lost = 0, cyclesWithTransitions = 0;
        var hoverLatencies = new List<double>();
        for (var k = 0; k < 100; k++)
        {
            var endBefore = _overlay.Named("transition.end").Count();
            var moveQpc = Stopwatch.GetTimestamp();
            Win32.SetCursorPos(centre.Item1, centre.Item2);
            var expanded = WaitTransitionEnd(endBefore, TimeSpan.FromSeconds(2));
            var enter = _overlay.Named("hover.enter").LastOrDefault(e => e.Qpc > moveQpc);
            if (enter is not null)
            {
                hoverLatencies.Add(FrameTimeline.ToMs(enter.Qpc - moveQpc, Stopwatch.Frequency));
            }

            checks++;
            lost += ProbeForeground() ? 0 : 1;
            Win32.SetCursorPos(below.Item1, below.Item2);
            var collapsed = WaitTransitionEnd(endBefore + 1, TimeSpan.FromSeconds(2));
            checks++;
            lost += ProbeForeground() ? 0 : 1;
            cyclesWithTransitions += expanded && collapsed ? 1 : 0;
        }

        foreach (var (x, y) in InsidePoints(20, shape))
        {
            Click(x, y);
            Thread.Sleep(150);
            checks++;
            lost += ProbeForeground() ? 0 : 1;
        }

        _overlay.Send(OverlayCommand.HoverOff);
        Park();
        Thread.Sleep(800);
        Keyboard.Type(TestString);
        Thread.Sleep(500);
        var received = string.Equals(_probe.Text, TestString, StringComparison.Ordinal);
        Record("S2-06", Of(startForeground && lost == 0 && received && cyclesWithTransitions == 100), new JsonObject
        {
            ["probe_foreground_at_start"] = startForeground,
            ["foreground_checks"] = checks,
            ["foreground_lost"] = lost,
            ["hover_cycles_with_both_transitions"] = cyclesWithTransitions,
            ["keystrokes_received"] = received,
            ["cursor_to_hover_enter_p50_ms"] = hoverLatencies.Count > 0 ? Statistics.Median(hoverLatencies) : null,
        });
    }

    private void Item07TaskbarAndAltTab()
    {
        var ex = Win32.ExStyle(_overlay.Hwnd);
        var styles = (ex & Win32.WsExToolWindow) != 0 && (ex & Win32.WsExNoActivate) != 0 && (ex & Win32.WsExTopmost) != 0 && (ex & Win32.WsExAppWindow) == 0;
        string[] overlayNames = ["Halo Spike Overlay", "Halo.Spikes.Overlay", "Halo.Spikes.CompositionBaseline"];
        string[] probeNames = ["Halo S2 Probe", "Halo.Spikes.Harness"];
        bool Has(IEnumerable<string> names, string[] needles) => names.Any(n => needles.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)));

        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var taskbar = desktop.FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"));
        var buttons = taskbar?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).Select(b => b.Properties.Name.ValueOrDefault ?? string.Empty).ToList() ?? [];
        var taskbarProbe = Has(buttons, probeNames);
        var taskbarOverlay = Has(buttons, overlayNames);

        ActivateProbe();
        List<string> switcher = [];
        Keyboard.Press(VirtualKeyShort.ALT);
        try
        {
            Keyboard.Type(VirtualKeyShort.TAB);
            var wait = Stopwatch.StartNew();
            while (switcher.Count == 0 && wait.Elapsed < TimeSpan.FromSeconds(4))
            {
                Thread.Sleep(250);
                foreach (var cls in new[] { "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "TaskSwitcherWnd" })
                {
                    var w = desktop.FindFirstChild(cf => cf.ByClassName(cls));
                    var items = w?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Select(i => i.Properties.Name.ValueOrDefault ?? string.Empty).ToList();
                    if (items is { Count: > 0 })
                    {
                        switcher = items;
                        break;
                    }
                }
            }

            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.ALT);
        }

        Thread.Sleep(500);
        var altTabRead = switcher.Count > 0 && Has(switcher, probeNames);
        var altTabOverlay = Has(switcher, overlayNames);
        var foregroundKept = ProbeForeground();
        var evidence = new JsonObject
        {
            ["styles_ok"] = styles,
            ["taskbar_buttons_read"] = buttons.Count,
            ["taskbar_probe_present"] = taskbarProbe,
            ["taskbar_overlay_present"] = taskbarOverlay,
            ["alt_tab_items_read"] = switcher.Count,
            ["alt_tab_probe_present"] = altTabRead,
            ["alt_tab_overlay_present"] = altTabOverlay,
            ["probe_foreground_after"] = foregroundKept,
        };
        if (!altTabRead || !taskbarProbe)
        {
            // Positive control not seen: the automation cannot prove absence; manual S2-07M is required.
            evidence["manual_required"] = true;
            Record("S2-07", styles && !taskbarOverlay && !altTabOverlay ? Outcome.NotRun : Outcome.Fail, evidence);
            return;
        }

        Record("S2-07", Of(styles && !taskbarOverlay && !altTabOverlay && foregroundKept), evidence);
    }

    private void Item12Automation()
    {
        var before = ProbeForeground();
        using var automation = new UIA3Automation();
        var root = automation.FromHandle(_overlay.Hwnd);
        var rootName = root.Properties.Name.ValueOrDefault ?? string.Empty;
        var text = root.FindFirstDescendant(cf => cf.ByAutomationId("CompactText"));
        var textOk = text is not null && (text.Properties.Name.ValueOrDefault ?? string.Empty) == "12:34";
        var after = ProbeForeground();
        Record("S2-12", Of(rootName.Length > 0 && textOk && before && after), new JsonObject
        {
            ["root_has_name"] = rootName.Length > 0,
            ["compact_text_exposed"] = textOk,
            ["foreground_unchanged"] = before && after,
        });
    }

    private bool OverlayVisibleInCapture(Color background, out int foreign)
    {
        var pixels = ScreenRegion.Capture(_geo.Canvas);
        foreign = 0;
        var black = 0;
        for (var y = 0; y < pixels.Rect.Height; y++)
        {
            for (var x = 0; x < pixels.Rect.Width; x++)
            {
                var (r, g, b) = pixels.At(x, y);
                if (!Near(r, g, b, background))
                {
                    foreign++;
                }

                if (_geo.Compact.SignedDistance(x + 0.5, y + 0.5) <= -2 && r <= 2 && g <= 2 && b <= 2)
                {
                    black++;
                }
            }
        }

        return black > 0;
    }

    private void Item10Suppression()
    {
        Park();
        var monitor = new PxRect(0, 0, 1920, 1080);
        var latencies = new List<double>();

        // (i) non-topmost full-screen window: overlay is above it until suppressed.
        var normal = new FullscreenProbe(_ui, monitor, topmost: false);
        var visibleBefore = OverlayVisibleInCapture(FullscreenProbe.FullscreenColor, out _);
        var cmdQpc = Stopwatch.GetTimestamp();
        _overlay.Send(OverlayCommand.Suppress);
        _overlay.WaitFor("suppressed", TimeSpan.FromSeconds(2), e => e.Qpc > cmdQpc);
        Thread.Sleep(400);
        var hide = _dd.Frames().FirstOrDefault(f => f.Attributed && f.Qpc > cmdQpc);
        if (hide.Qpc != 0)
        {
            latencies.Add(FrameTimeline.ToMs(hide.Qpc - cmdQpc, Stopwatch.Frequency));
        }

        OverlayVisibleInCapture(FullscreenProbe.FullscreenColor, out var foreignWhileSuppressed1);
        _overlay.Send(OverlayCommand.Restore);
        _overlay.WaitFor("restored", TimeSpan.FromSeconds(2));
        Thread.Sleep(400);
        var visibleAfter1 = OverlayVisibleInCapture(FullscreenProbe.FullscreenColor, out _);
        normal.Close();

        // (ii) topmost full-screen window shown after the overlay.
        var topmost = new FullscreenProbe(_ui, monitor, topmost: true);
        var cmdQpc2 = Stopwatch.GetTimestamp();
        _overlay.Send(OverlayCommand.Suppress);
        _overlay.WaitFor("suppressed", TimeSpan.FromSeconds(2), e => e.Qpc > cmdQpc2);
        Thread.Sleep(400);
        OverlayVisibleInCapture(FullscreenProbe.FullscreenColor, out var foreignWhileSuppressed2);
        topmost.Close();
        Thread.Sleep(300);
        _overlay.Send(OverlayCommand.Restore);
        Thread.Sleep(600);
        var visibleAfter2 = OverlayVisibleInCapture(Probe.ProbeColor, out _);
        ActivateProbe();

        Record("S2-10", Of(visibleBefore && foreignWhileSuppressed1 == 0 && visibleAfter1 && foreignWhileSuppressed2 == 0 && visibleAfter2), new JsonObject
        {
            ["overlay_visible_over_normal_fullscreen_before"] = visibleBefore,
            ["non_background_pixels_while_suppressed_normal"] = foreignWhileSuppressed1,
            ["visible_after_restore_normal"] = visibleAfter1,
            ["non_background_pixels_while_suppressed_topmost"] = foreignWhileSuppressed2,
            ["visible_after_restore_topmost_closed"] = visibleAfter2,
            ["hide_latency_ms_not_judged"] = latencies.Count > 0 ? latencies[0] : null,
        });
    }

    private void Item11Automated()
    {
        var before = _overlay.Named("placement").Count();
        // Sent, not posted: like the system's own broadcast. Windows refuses to post WM_SETTINGCHANGE because its
        // lParam can carry a string pointer.
        Win32.SendMessageTimeout(_overlay.Hwnd, Win32.WmSettingChange, (nint)Win32.SpiSetWorkArea, 0, Win32.SmtoAbortIfHung, 2000, out _);
        Thread.Sleep(500);
        Win32.SendMessageTimeout(_overlay.Hwnd, Win32.WmDisplayChange, 32, (nint)((1080 << 16) | 1920), Win32.SmtoAbortIfHung, 2000, out _);
        Thread.Sleep(800);
        var placements = _overlay.Named("placement").Skip(before).ToList();
        var c = _geo.Canvas;
        var r = Win32.WindowRect(_overlay.Hwnd);
        var rectOk = r.Left == c.Left && r.Top == c.Top && r.Right == c.Right && r.Bottom == c.Bottom;
        var reasons = placements.Select(p => p.Tok("reason")).ToList();
        var handled = reasons.Contains("work_area_changed") && reasons.Contains("display_changed");
        // Automated part only; the single-display DPI part (S2-11M) is manual and multi-monitor/mixed DPI is BLOCKED.
        Record("S2-11a", Of(handled && rectOk && Win32.IsResponsive(_overlay.Hwnd, 1000)), new JsonObject
        {
            ["work_area_and_display_change_handled"] = handled,
            ["placement_correct_after"] = rectOk,
        });
    }
}
