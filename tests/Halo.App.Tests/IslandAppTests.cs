using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Halo.Core.Clock;
using Halo.Core.Island;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Starts the real Halo.exe and checks it on the desktop: placement, window styles, input region, click toggling,
/// focus, UI Automation, single instance, hover-intent timing, the tray icon and its menu, injected start-up
/// failures and clean exit. One class so the tests never run concurrently.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class IslandAppTests
{
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const int WsExAppWindow = 0x00040000;

    private static (int X, int Y) s_lastMove;

    [Fact]
    public void LaunchShowsCompactIslandCentredAtTopAndExitsCleanly()
    {
        using var island = IslandProcess.Start();
        var layout = ExpectedLayout();

        var ready = island.WaitFor("ready");
        Assert.Equal(layout.Canvas.Left, ready["left"]!.GetValue<int>());
        Assert.Equal(layout.Canvas.Top, ready["top"]!.GetValue<int>());

        Assert.True(GetWindowRect(island.Hwnd, out var rect));
        Assert.Equal((layout.Canvas.Left, layout.Canvas.Top, layout.Canvas.Right, layout.Canvas.Bottom), (rect.Left, rect.Top, rect.Right, rect.Bottom));

        var ex = (int)GetWindowLongPtrW(island.Hwnd, -20);
        Assert.Equal(WsExTopmost | WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap, ex & (WsExTopmost | WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap));
        Assert.Equal(0, ex & WsExAppWindow);

        // Only the compact pill takes input; the rest of the canvas passes clicks through (FR-ISL-05).
        AssertRegion(island.Hwnd, layout.Compact);
        Assert.Equal(island.Hwnd, RootAt(layout.Canvas.Left + (int)layout.Compact.CenterX, layout.Canvas.Top + (int)(layout.Compact.Top + (layout.Compact.Height / 2))));
        Assert.NotEqual(island.Hwnd, RootAt(layout.Canvas.Left + 2, layout.Canvas.Bottom - 2));

        var exited = island.Exit();
        Assert.True(exited.TotalMilliseconds < 5000, $"exit took {exited.TotalMilliseconds} ms");
        Assert.Equal(0, island.ExitCode);
        Assert.Equal(0, FindWindowW("ProjectHalo.Island", null));
    }

    [Fact]
    public void ClicksToggleTheIslandWithoutTakingFocus()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        var layout = ExpectedLayout();
        var foreground = SettledForeground();
        GetCursorPos(out var cursor);
        try
        {
            var x = layout.Canvas.Left + (int)layout.Compact.CenterX;
            var y = layout.Canvas.Top + (int)(layout.Compact.Top + (layout.Compact.Height / 2));

            Click(x, y);
            Assert.Equal("expand", island.WaitFor("transition.end")["dir"]!.GetValue<string>());
            AssertRegion(island.Hwnd, layout.Expanded);

            Click(x, y);
            Assert.Equal("collapse", island.WaitFor("transition.end", skip: 1)["dir"]!.GetValue<string>());
            AssertRegion(island.Hwnd, layout.Compact);

            // FR-ISL-06: pointer interaction never moves the foreground window.
            AssertForeground(foreground);
            Assert.Equal(2, island.Count("transition.start"));
        }
        finally
        {
            SetCursorPos(cursor.X, cursor.Y);
        }
    }

    [Fact]
    public void AutomationExposesLiveClockAndExpandCollapse()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);

        Assert.Equal("HaloIsland", element.AutomationId);
        Assert.Equal(ControlType.Button, element.ControlType);
        var now = DateTimeOffset.Now;
        var accepted = new[] { now, now.AddMinutes(-1) }.Select(t => ClockFace.Read(t, CultureInfo.CurrentCulture).TimeWithDesignator);
        Assert.Contains(accepted, time => element.Name.Contains(time, StringComparison.Ordinal));

        var pattern = element.Patterns.ExpandCollapse.Pattern;
        Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState.Value);
        pattern.Expand();
        Assert.Equal("automation", island.WaitFor("transition.start")["source"]!.GetValue<string>());
        island.WaitFor("transition.end");
        Assert.Equal(ExpandCollapseState.Expanded, pattern.ExpandCollapseState.Value);
        Assert.Contains("% of today", element.Properties.FullDescription.ValueOrDefault ?? string.Empty, StringComparison.Ordinal);

        element.Patterns.Invoke.Pattern.Invoke();
        island.WaitFor("transition.end", skip: 1);
        Assert.Equal(ExpandCollapseState.Collapsed, pattern.ExpandCollapseState.Value);
    }

    [Fact]
    public void RightClickMenuExitsAndReturnsTheForeground()
    {
        using var island = IslandProcess.Start();
        var layout = ExpectedLayout();
        var foreground = SettledForeground();
        GetCursorPos(out var cursor);
        try
        {
            var x = layout.Canvas.Left + (int)layout.Compact.CenterX;
            var y = layout.Canvas.Top + (int)(layout.Compact.Top + (layout.Compact.Height / 2));
            RightClick(x, y);
            var menu = WaitForWindow("#32768");
            Assert.NotEqual(0, menu);

            // Down selects "Expand", Down skips the separator to "Exit Halo", Enter chooses it.
            PressKeys(0x28, 0x28, 0x0D);
            Assert.True(island.WaitForExit(TimeSpan.FromSeconds(10)), "Halo did not exit from the menu");
            Assert.Equal(0, island.ExitCode);
            Assert.Equal(0, FindWindowW("ProjectHalo.Island", null));
            Thread.Sleep(300);
            AssertForeground(foreground);
        }
        finally
        {
            SetCursorPos(cursor.X, cursor.Y);
        }
    }

    [Fact]
    public void SecondLaunchExpandsTheRunningIslandAndExits()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");

        using var second = Process.Start(new ProcessStartInfo(IslandProcess.ExePath) { UseShellExecute = false })!;
        Assert.True(second.WaitForExit(10_000), "second instance did not exit");
        Assert.Equal(0, second.ExitCode);
        Assert.Equal("instance", island.WaitFor("transition.start")["source"]!.GetValue<string>());
        Assert.False(island.HasExited);
        Assert.Single(Process.GetProcessesByName("Halo"));
    }

    [Fact]
    public void TextIsDrawnInsideThePill()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        Thread.Sleep(300);
        var layout = ExpectedLayout();
        var c = layout.Compact;
        var capture = FlaUI.Core.Capturing.Capture.Rectangle(new System.Drawing.Rectangle(layout.Canvas.Left + (int)c.Left, layout.Canvas.Top + (int)c.Top, (int)c.Width, (int)c.Height));
        var bitmap = capture.Bitmap;
        int bright = 0, dark = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                // Only the straight middle part, away from the anti-aliased rounded ends and edges.
                if (x < c.Height / 2 || x > c.Width - (c.Height / 2) || y < 3 || y > c.Height - 3)
                {
                    continue;
                }

                var p = bitmap.GetPixel(x, y);
                var luma = (p.R + p.G + p.B) / 3;
                bright += luma > 160 ? 1 : 0;
                dark += luma < 30 ? 1 : 0;
            }
        }

        // A black pill with white text: mostly dark pixels and a few hundred bright glyph pixels.
        Assert.True(dark > bright * 4, $"dark={dark} bright={bright}");
        Assert.True(bright > 80, $"too few text pixels: {bright}");
    }

    [Fact]
    public void HoverDwellExpandsThePanelStaysOpenAndLeaveGraceCollapses()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();
        var foreground = SettledForeground();

        MoveTo(pointer.PillX, pointer.PillY);
        var pending = island.WaitForPhase("expand_pending");
        var opened = island.WaitForPhase("expanded_hover");
        Assert.InRange(Ms(opened, pending), 100, 400);
        Assert.Equal("hover", island.WaitFor("transition.start")["source"]!.GetValue<string>());
        Assert.Equal("expand", island.WaitFor("transition.end")["dir"]!.GetValue<string>());
        AssertRegion(island.Hwnd, layout.Expanded);

        // Pill → panel: the pointer stays inside the expanded shape, so nothing is scheduled.
        MoveTo(pointer.PillX, pointer.PanelY);
        Thread.Sleep(900);
        AssertPointerUndisturbed();
        Assert.True(island.Count("phase", "collapse_pending") == 0 && island.Count("transition.start") == 1, island.Trace());

        MoveTo(pointer.PillX, pointer.OutsideY);
        var leaving = island.WaitForPhase("collapse_pending");
        var closed = island.WaitForPhase("collapsed", after: leaving);
        Assert.InRange(Ms(closed, leaving), 380, 900);
        var collapse = island.WaitFor("transition.start", skip: 1);
        Assert.Equal("collapse", collapse["dir"]!.GetValue<string>());
        Assert.Equal("hover", collapse["source"]!.GetValue<string>());
        island.WaitFor("transition.end", skip: 1);
        AssertRegion(island.Hwnd, layout.Compact);
        AssertForeground(foreground);
    }

    [Fact]
    public void LeavingBeforeTheDwellDoesNotExpand()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();

        MoveTo(pointer.PillX, pointer.PillY);
        island.WaitForPhase("expand_pending");
        MoveTo(pointer.PillX, pointer.OutsideY);
        island.WaitForPhase("collapsed");
        Thread.Sleep(700);

        AssertPointerUndisturbed();
        Assert.True(island.Count("transition.start") == 0 && island.Count("phase", "expanded_hover") == 0, island.Trace());
    }

    [Fact]
    public void ReturningWithinTheGraceCancelsTheCollapse()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();

        MoveTo(pointer.PillX, pointer.PillY);
        island.WaitFor("transition.end");
        MoveTo(pointer.PillX, pointer.OutsideY);
        var leaving = island.WaitForPhase("collapse_pending");
        Thread.Sleep(120);
        MoveTo(pointer.PillX, pointer.PanelY);
        var back = island.WaitForPhase("expanded_hover", after: leaving);
        Assert.True(Ms(back, leaving) < 400, "the pointer came back after the grace");
        Thread.Sleep(900);

        AssertPointerUndisturbed();
        Assert.True(island.Count("transition.start") == 1 && island.Count("phase", "collapsed") == 0, island.Trace());
    }

    [Fact]
    public void ExitWhileHoverTimersArePendingIsClean()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);

        using (var island = IslandProcess.Start())
        {
            MoveTo(pointer.PillX, pointer.PillY);
            island.WaitForPhase("expand_pending");
            Assert.True(island.Exit().TotalMilliseconds < 5000);
            Assert.Equal(0, island.ExitCode);
        }

        MoveTo(pointer.PillX, pointer.OutsideY);
        using (var island = IslandProcess.Start())
        {
            MoveTo(pointer.PillX, pointer.PillY);
            island.WaitFor("transition.end");
            MoveTo(pointer.PillX, pointer.OutsideY);
            island.WaitForPhase("collapse_pending");
            Assert.True(island.Exit().TotalMilliseconds < 5000);
            Assert.Equal(0, island.ExitCode);
        }

        Assert.Empty(Process.GetProcessesByName("Halo"));
        Assert.Equal(0, FindWindowW("ProjectHalo.Island", null));
    }

    [Fact]
    public void TrayIconIsAddedWhileRunningAndRemovedOnExit()
    {
        using var island = IslandProcess.Start();
        Assert.Equal("added", island.WaitFor("tray")["status"]!.GetValue<string>());

        // The shell knows the icon (its rectangle is the overflow chevron when the icon is hidden there).
        Assert.Equal(0, TrayIconRect(island.Hwnd));

        island.Exit();

        // After exit the shell answers "not found" for any dead window, so the proof is its reply to our delete.
        Assert.Contains(island.Events("tray"), e => e["status"]?.GetValue<string>() == "removed");
        Assert.DoesNotContain(island.Events("tray"), e => e["status"]?.GetValue<string>() == "remove_failed");
    }

    /// <summary>
    /// The real path: Explorer's notification-area button (UIA Invoke = left click, SendInput right click) sends the
    /// callback, the island shows its menu, and keyboard input picks items.
    /// </summary>
    [Fact]
    public void TrayIconTogglesAndItsMenuOpensCollapsesAndExits()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var automation = new UIA3Automation();
        using var island = IslandProcess.Start();
        island.WaitFor("tray");
        try
        {
            // Left click on the icon toggles.
            FindTrayButton(automation).Patterns.Invoke.Pattern.Invoke();
            var expand = island.WaitFor("transition.start");
            Assert.Equal(("expand", "tray"), (expand["dir"]!.GetValue<string>(), expand["source"]!.GetValue<string>()));
            island.WaitFor("transition.end");

            // Menu: "Collapse Halo" (Down, Down, Enter).
            OpenTrayMenu(automation);
            PressKeys(0x28, 0x28, 0x0D);
            var collapse = island.WaitFor("transition.start", skip: 1);
            Assert.Equal(("collapse", "tray"), (collapse["dir"]!.GetValue<string>(), collapse["source"]!.GetValue<string>()));
            island.WaitFor("transition.end", skip: 1);

            // Menu: "Open Halo" (Down, Enter) pins the island open.
            OpenTrayMenu(automation);
            PressKeys(0x28, 0x0D);
            var open = island.WaitFor("transition.start", skip: 2);
            Assert.Equal(("expand", "tray"), (open["dir"]!.GetValue<string>(), open["source"]!.GetValue<string>()));
            Assert.Equal("expanded_pinned", island.LastPhase());
            island.WaitFor("transition.end", skip: 2);

            // Menu: "Exit Halo" is the last item; Up from a fresh menu selects the last item, then Enter.
            OpenTrayMenu(automation);
            PressKeys(0x26, 0x0D);
            Assert.True(island.WaitForExit(TimeSpan.FromSeconds(10)), "Halo did not exit from the tray menu");
            Assert.Equal(0, island.ExitCode);
            Assert.Contains(island.Events("exit.request"), e => e["source"]?.GetValue<string>() == "tray");
            Assert.Equal(0, FindWindowW("ProjectHalo.Island", null));
            Assert.Contains(island.Events("tray"), e => e["status"]?.GetValue<string>() == "removed");
            Assert.Empty(Process.GetProcessesByName("Halo"));

            // What the user sees: no Halo button left in the notification area.
            Assert.Null(FindTrayButton(automation, TimeSpan.FromSeconds(2)));
        }
        finally
        {
            CloseTrayOverflow(automation);
        }
    }

    [Fact]
    public void TrayFailureKeepsTheIslandUsableAndExitsCleanly()
    {
        using var island = IslandProcess.Start(fault: "tray");

        Assert.Equal("failed", island.WaitFor("tray")["status"]!.GetValue<string>());
        Assert.NotEqual(0, TrayIconRect(island.Hwnd));
        using var automation = new UIA3Automation();
        automation.FromHandle(island.Hwnd).Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");

        island.Exit();
        Assert.Equal(0, island.ExitCode);
        Assert.Single(island.Events("tray")); // never added, so nothing to remove
        Assert.Empty(Process.GetProcessesByName("Halo"));
    }

    [Fact]
    public void StartupFailureLeavesNoWindowTrayIconOrProcess()
    {
        using (var failed = IslandProcess.Start(fault: "startup", waitForReady: false))
        {
            Assert.True(failed.WaitForExit(TimeSpan.FromSeconds(10)), "Halo kept running after a startup failure");
            Assert.Equal(1, failed.ExitCode);
            var tray = failed.Events("tray").Select(e => e["status"]!.GetValue<string>()).ToList();
            Assert.Equal(["added", "removed"], tray); // the icon existed before the failure and the shell deleted it
            Assert.Empty(failed.Events("ready"));
            Assert.Equal(0, FindWindowW("ProjectHalo.Island", null));
            Assert.Empty(Process.GetProcessesByName("Halo"));
        }

        // The single-instance mutex was released: a normal launch works straight away.
        using var next = IslandProcess.Start();
        Assert.False(next.HasExited);
    }

    private static double Ms(JsonObject later, JsonObject earlier) =>
        (later["qpc"]!.GetValue<long>() - earlier["qpc"]!.GetValue<long>()) * 1000.0 / Stopwatch.Frequency;

    /// <summary>S_OK (0) while the shell has the icon registered.</summary>
    private static int TrayIconRect(nint hwnd)
    {
        var id = new NotifyIconIdentifier { Size = (uint)Marshal.SizeOf<NotifyIconIdentifier>(), Hwnd = hwnd, Id = 1 };
        return ShellNotifyIconGetRect(ref id, out _);
    }

    /// <summary>The Halo button in the notification area, opening the overflow flyout when the icon lives there.</summary>
    private static AutomationElement FindTrayButton(UIA3Automation automation) =>
        FindTrayButton(automation, TimeSpan.FromSeconds(8)) ?? throw new TimeoutException("The Halo tray button was not found in the notification area.");

    private static AutomationElement? FindTrayButton(UIA3Automation automation, TimeSpan timeout)
    {
        var desktop = automation.GetDesktop();
        var sw = Stopwatch.StartNew();
        var openedOverflow = false;
        while (sw.Elapsed < timeout)
        {
            foreach (var host in new[] { "TopLevelWindowForOverflowXamlIsland", "Shell_TrayWnd" })
            {
                var button = desktop.FindFirstChild(cf => cf.ByClassName(host))?
                    .FindFirstDescendant(cf => cf.ByAutomationId("NotifyItemIcon").And(cf.ByName("Halo")));
                if (button is not null && !button.IsOffscreen && button.BoundingRectangle.Width > 0)
                {
                    // The flyout animates open and closed (clicking an icon closes it): accept the button only when its
                    // rectangle is the same 120 ms later, so a click never lands on a flyout that is mid-animation.
                    var first = button.BoundingRectangle;
                    Thread.Sleep(120);
                    if (!button.IsOffscreen && button.BoundingRectangle == first)
                    {
                        return button;
                    }
                }
            }

            if (!openedOverflow)
            {
                desktop.FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"))?
                    .FindFirstDescendant(cf => cf.ByName("Show Hidden Icons"))?.Patterns.Invoke.Pattern.Invoke();
                openedOverflow = true;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    private static void OpenTrayMenu(UIA3Automation automation)
    {
        var button = FindTrayButton(automation);
        var r = button.BoundingRectangle;
        var (x, y) = (r.X + (r.Width / 2), r.Y + (r.Height / 2));
        var under = RootAt(x, y);
        RightClick(x, y);
        var menu = WaitForWindow("#32768");
        Assert.True(menu != 0, $"no tray menu after a right click at {x},{y} (button {r}, parent {button.Parent?.ClassName}); under the point before: {Describe(under)}; now: {Describe(RootAt(x, y))}; foreground: {Describe(GetForegroundWindow())}");
        Thread.Sleep(150);
    }

    /// <summary>Closes the overflow flyout if it is open and waits until it is gone, so later tests start settled.</summary>
    private static void CloseTrayOverflow(UIA3Automation automation)
    {
        bool Open() => automation.GetDesktop().FindFirstChild(cf => cf.ByClassName("TopLevelWindowForOverflowXamlIsland"))?
            .FindFirstDescendant(cf => cf.ByAutomationId("NotifyItemIcon")) is { IsOffscreen: false };
        // Esc only when the flyout itself has the foreground: otherwise it would reach whatever window does (for
        // example the settings window the tray menu just opened).
        if (Open() && Describe(GetForegroundWindow()).Contains("TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal))
        {
            PressKeys(0x1B); // Esc closes the flyout
        }
        else if (Open())
        {
            // Another window (for example the settings window) took the foreground while the flyout stayed open: its
            // own chevron button toggles it closed without sending keys anywhere.
            automation.GetDesktop().FindFirstChild(cf => cf.ByClassName("Shell_TrayWnd"))?
                .FindFirstDescendant(cf => cf.ByName("Show Hidden Icons"))?.Patterns.Invoke.Pattern.Invoke();
        }

        var sw = Stopwatch.StartNew();
        while (Open() && sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            Thread.Sleep(50);
        }

        _ = SettledForeground();
    }

    /// <summary>Moves the pointer without clicking (one absolute SendInput move) and waits until it is there.</summary>
    private static void MoveTo(int x, int y)
    {
        var w = GetSystemMetrics(0);
        var h = GetSystemMetrics(1);
        Input[] inputs = [new() { Type = 0, Mouse = new MouseInput { X = ((x * 65536) + (w / 2)) / w, Y = ((y * 65536) + (h / 2)) / h, Flags = 0x0001 | 0x8000 } }];
        Assert.Equal(1u, SendInput(1, inputs, Marshal.SizeOf<Input>()));
        s_lastMove = (x, y);
        var sw = Stopwatch.StartNew();
        while (!(GetCursorPos(out var p) && Math.Abs(p.X - x) <= 1 && Math.Abs(p.Y - y) <= 1))
        {
            Assert.True(sw.ElapsedMilliseconds < 500, $"the pointer did not reach {x},{y}");
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// The foreground window once it has not changed for 500 ms (at most 5 s), so a flyout or menu closing from an
    /// earlier step is not taken as the baseline.
    /// </summary>
    private static nint SettledForeground()
    {
        var current = GetForegroundWindow();
        var stable = Stopwatch.StartNew();
        var total = Stopwatch.StartNew();
        while (stable.ElapsedMilliseconds < 500 && total.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(25);
            var now = GetForegroundWindow();
            if (now != current)
            {
                current = now;
                stable.Restart();
            }
        }

        return current;
    }

    /// <summary>FR-ISL-06: the foreground window is unchanged. The message names both windows.</summary>
    private static void AssertForeground(nint expected)
    {
        var actual = GetForegroundWindow();
        Assert.True(actual == expected, $"the foreground changed from {Describe(expected)} to {Describe(actual)}");
    }

    private static string Describe(nint hwnd)
    {
        var buffer = new char[256];
        var length = GetClassNameW(hwnd, buffer, buffer.Length);
        var name = new string(buffer, 0, Math.Max(0, length));
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch (ArgumentException)
        {
            process = "exited";
        }

        return $"0x{hwnd:X} class={name} process={process}";
    }

    /// <summary>Fails clearly when something other than the test (for example a real mouse) moved the pointer.</summary>
    private static void AssertPointerUndisturbed()
    {
        Assert.True(GetCursorPos(out var p), "GetCursorPos failed");
        Assert.True(Math.Abs(p.X - s_lastMove.X) <= 1 && Math.Abs(p.Y - s_lastMove.Y) <= 1, $"the pointer was moved during the test (expected {s_lastMove.X},{s_lastMove.Y}, found {p.X},{p.Y}); rerun without touching the mouse");
    }

    /// <param name="volumeRow">The default settings show the volume row (M3-F03), which makes the expanded shape taller.</param>
    private static IslandLayout ExpectedLayout(bool volumeRow = true)
    {
        var monitor = MonitorFromPoint(default, 1);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfoW(monitor, ref info));
        Assert.Equal(0, GetDpiForMonitor(monitor, 0, out var dpi, out _));
        return IslandLayout.Compute(new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top), (int)dpi, volumeRow);
    }

    private static void AssertRegion(nint hwnd, RoundedBox expected)
    {
        Assert.NotEqual(0, GetWindowRgnBox(hwnd, out var box));
        Assert.InRange(box.Left, (int)Math.Floor(expected.Left) - 1, (int)Math.Floor(expected.Left) + 1);
        Assert.InRange(box.Top, (int)Math.Floor(expected.Top) - 1, (int)Math.Floor(expected.Top) + 1);
        Assert.InRange(box.Right, (int)Math.Ceiling(expected.Right) - 1, (int)Math.Ceiling(expected.Right) + 2);
        Assert.InRange(box.Bottom, (int)Math.Ceiling(expected.Bottom) - 1, (int)Math.Ceiling(expected.Bottom) + 2);
    }

    private static nint RootAt(int x, int y) => GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2);

    /// <summary>One SendInput batch per click (move, down, up), so the click lands where it was aimed.</summary>
    private static void Click(int x, int y)
    {
        var w = GetSystemMetrics(0);
        var h = GetSystemMetrics(1);
        var nx = ((x * 65536) + (w / 2)) / w;
        var ny = ((y * 65536) + (h / 2)) / h;
        Input[] inputs =
        [
            new() { Type = 0, Mouse = new MouseInput { X = nx, Y = ny, Flags = 0x0001 | 0x8000 } },
            new() { Type = 0, Mouse = new MouseInput { X = nx, Y = ny, Flags = 0x0002 | 0x8000 } },
            new() { Type = 0, Mouse = new MouseInput { X = nx, Y = ny, Flags = 0x0004 | 0x8000 } },
        ];
        Assert.Equal((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));
    }

    private static void RightClick(int x, int y)
    {
        var w = GetSystemMetrics(0);
        var h = GetSystemMetrics(1);
        var nx = ((x * 65536) + (w / 2)) / w;
        var ny = ((y * 65536) + (h / 2)) / h;
        Input[] inputs =
        [
            new() { Type = 0, Mouse = new MouseInput { X = nx, Y = ny, Flags = 0x0001 | 0x8000 } },
            new() { Type = 0, Mouse = new MouseInput { X = nx, Y = ny, Flags = 0x0008 | 0x8000 } },
            new() { Type = 0, Mouse = new MouseInput { X = nx, Y = ny, Flags = 0x0010 | 0x8000 } },
        ];
        Assert.Equal((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));
    }

    private static void PressKeys(params ushort[] keys)
    {
        foreach (var key in keys)
        {
            KeyInput[] inputs =
            [
                new() { Type = 1, Vk = key },
                new() { Type = 1, Vk = key, Flags = 0x0002 },
            ];
            Assert.Equal((uint)inputs.Length, SendKeyInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyInput>()));
            Thread.Sleep(80);
        }
    }

    private static nint WaitForWindow(string className)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(5))
        {
            var found = FindWindowW(className, null);
            if (found != 0)
            {
                return found;
            }

            Thread.Sleep(20);
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint Size;
        public nint Hwnd;
        public uint Id;
        public Guid Item;
    }

    /// <summary>INPUT with a KEYBDINPUT (padded to the size of the union, 40 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public uint Type;
        public uint UnionAlignment; // the INPUT union starts at offset 8 on x64
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
        public long Padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
    }

#pragma warning disable SYSLIB1054 // test-only signatures; DllImport keeps them short
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgnBox(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, [Out] char[] name, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", EntryPoint = "SendInput")]
    private static extern uint SendKeyInput(uint count, KeyInput[] inputs, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    private static extern int ShellNotifyIconGetRect(ref NotifyIconIdentifier identifier, out Rect rect);
#pragma warning restore SYSLIB1054

    /// <summary>Points used by the hover tests; parks the pointer away from the island and restores it afterwards.</summary>
    private sealed class PointerScope : IDisposable
    {
        private readonly Point _saved;

        public PointerScope(IslandLayout layout)
        {
            GetCursorPos(out _saved);
            PillX = layout.Canvas.Left + (int)layout.Compact.CenterX;
            PillY = layout.Canvas.Top + (int)(layout.Compact.Top + (layout.Compact.Height / 2));
            PanelY = layout.Canvas.Top + (int)(layout.Expanded.Bottom - layout.Dip(24));
            OutsideY = layout.Canvas.Bottom + (int)layout.Dip(80);
            MoveTo(PillX, OutsideY);
            Thread.Sleep(50);
        }

        public int PillX { get; }

        public int PillY { get; }

        /// <summary>Inside the expanded panel, below the pill.</summary>
        public int PanelY { get; }

        /// <summary>Below the island canvas.</summary>
        public int OutsideY { get; }

        public void Dispose() => SetCursorPos(_saved.X, _saved.Y);
    }

    /// <summary>A running Halo.exe with --diagnostics; its JSON events are collected from standard output.</summary>
    private sealed class IslandProcess : IDisposable
    {
        private readonly Process _process;
        private readonly ConcurrentQueue<JsonObject> _events = new();

        private IslandProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } line && JsonNode.Parse(line) is JsonObject o)
                {
                    _events.Enqueue(o);
                }
            };
            _process.BeginOutputReadLine();
        }

        public static string ExePath
        {
            get
            {
                var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ? "Debug" : "Release";
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Halo.slnx")))
                {
                    dir = dir.Parent;
                }

                return Path.Combine(dir!.FullName, "src", "Halo.App", "bin", configuration, "net10.0-windows10.0.19041.0", "Halo.exe");
            }
        }

        public nint Hwnd { get; private set; }

        public bool HasExited => _process.HasExited;

        public int Pid => _process.Id;

        public int ExitCode => _process.ExitCode;

        /// <summary>Private data folder of this run: tests never read or change the owner's own settings.</summary>
        public string DataDir { get; private set; } = string.Empty;

        public static IslandProcess Start(string? fault = null, bool waitForReady = true, string? settingsJson = null, string? dataDirectory = null)
        {
            // A previous failed test could have left an island running; the single-instance check would then exit.
            var stale = FindWindowW("ProjectHalo.Island", null);
            if (stale != 0)
            {
                using var closer = Process.Start(new ProcessStartInfo(ExePath, "--exit") { UseShellExecute = false })!;
                closer.WaitForExit(5000);
                Thread.Sleep(1000);
            }

            // A test may pass the folder of an earlier run to check what survives a restart.
            var dataDir = dataDirectory ?? Path.Combine(Path.GetTempPath(), "halo-desktop-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataDir);
            if (settingsJson is not null)
            {
                File.WriteAllText(Path.Combine(dataDir, "settings.json"), settingsJson);
            }

            var info = new ProcessStartInfo(ExePath) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            info.ArgumentList.Add("--diagnostics");
            info.ArgumentList.Add("--data-dir");
            info.ArgumentList.Add(dataDir);
            info.Environment.Remove("HALO_TEST_FAULT");
            if (fault is not null)
            {
                info.Environment["HALO_TEST_FAULT"] = fault;
            }

            var island = new IslandProcess(Process.Start(info)!) { DataDir = dataDir };
            if (!waitForReady)
            {
                return island;
            }

            island.WaitFor("ready");
            island.Hwnd = FindWindowW("ProjectHalo.Island", null);
            Assert.NotEqual(0, island.Hwnd);
            return island;
        }

        public JsonObject WaitFor(string name, int skip = 0)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(15))
            {
                var found = _events.Where(e => e["event"]?.GetValue<string>() == name).Skip(skip).FirstOrDefault();
                if (found is not null)
                {
                    return found;
                }

                Assert.False(_process.HasExited, $"Halo exited while waiting for '{name}'");
                Thread.Sleep(10);
            }

            throw new TimeoutException($"No '{name}' event.");
        }

        public int Count(string name) => _events.Count(e => e["event"]?.GetValue<string>() == name);

        public int Count(string name, string phase) => _events.Count(e => e["event"]?.GetValue<string>() == name && e["phase"]?.GetValue<string>() == phase);

        public List<JsonObject> Events(string name) => _events.Where(e => e["event"]?.GetValue<string>() == name).ToList();

        /// <summary>Phases and transitions in order, for assertion messages.</summary>
        public string Trace() => string.Join(" → ", _events
            .Where(e => e["event"]?.GetValue<string>() is "phase" or "transition.start")
            .Select(e => e["event"]!.GetValue<string>() == "phase" ? $"{e["phase"]}({e["source"]})" : $"[{e["dir"]}]"));

        /// <summary>The current phase token (the last "phase" event), or "collapsed" if none was logged.</summary>
        public string State() => LastPhase() ?? "collapsed";

        public string? LastPhase() => _events.LastOrDefault(e => e["event"]?.GetValue<string>() == "phase")?["phase"]?.GetValue<string>();

        /// <summary>Waits for a "phase" event with the given phase, optionally one logged after another event.</summary>
        public JsonObject WaitForPhase(string phase, JsonObject? after = null)
        {
            var since = after?["qpc"]!.GetValue<long>() ?? 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(15))
            {
                var found = _events.FirstOrDefault(e => e["event"]?.GetValue<string>() == "phase" && e["phase"]?.GetValue<string>() == phase && e["qpc"]!.GetValue<long>() > since);
                if (found is not null)
                {
                    return found;
                }

                Assert.False(_process.HasExited, $"Halo exited while waiting for phase '{phase}'");
                Thread.Sleep(5);
            }

            throw new TimeoutException($"No phase '{phase}'.");
        }

        public bool WaitForExit(TimeSpan timeout)
        {
            if (!_process.WaitForExit(timeout))
            {
                return false;
            }

            _process.WaitForExit(); // also drains redirected output, so every event is collected
            return true;
        }

        /// <summary>Closes the island through a second "Halo.exe --exit" launch and returns how long it took to exit.</summary>
        public TimeSpan Exit()
        {
            if (_process.HasExited)
            {
                return TimeSpan.Zero;
            }

            var sw = Stopwatch.StartNew();
            using var closer = Process.Start(new ProcessStartInfo(ExePath, "--exit") { UseShellExecute = false })!;
            Assert.True(closer.WaitForExit(5000));
            Assert.True(_process.WaitForExit(10_000), "Halo did not exit");
            _process.WaitForExit();
            return sw.Elapsed;
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                _ = Exit();
            }

            _process.Dispose();
            try
            {
                Directory.Delete(DataDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
