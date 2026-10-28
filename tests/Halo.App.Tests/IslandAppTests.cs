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
/// focus, UI Automation, single instance and clean exit. One class so the tests never run concurrently.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IslandAppTests
{
    private const int WsExTopmost = 0x00000008;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExNoRedirectionBitmap = 0x00200000;
    private const int WsExAppWindow = 0x00040000;

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
        var foreground = GetForegroundWindow();
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
            Assert.Equal(foreground, GetForegroundWindow());
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
        var foreground = GetForegroundWindow();
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
            Assert.Equal(foreground, GetForegroundWindow());
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

    private static IslandLayout ExpectedLayout()
    {
        var monitor = MonitorFromPoint(default, 1);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        Assert.True(GetMonitorInfoW(monitor, ref info));
        Assert.Equal(0, GetDpiForMonitor(monitor, 0, out var dpi, out _));
        return IslandLayout.Compute(new PixelRect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top), (int)dpi);
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
#pragma warning restore SYSLIB1054

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

        public int ExitCode => _process.ExitCode;

        public static IslandProcess Start()
        {
            // A previous failed test could have left an island running; the single-instance check would then exit.
            var stale = FindWindowW("ProjectHalo.Island", null);
            if (stale != 0)
            {
                using var closer = Process.Start(new ProcessStartInfo(ExePath, "--exit") { UseShellExecute = false })!;
                closer.WaitForExit(5000);
                Thread.Sleep(1000);
            }

            var info = new ProcessStartInfo(ExePath, "--diagnostics") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            var island = new IslandProcess(Process.Start(info)!);
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

        public bool WaitForExit(TimeSpan timeout) => _process.WaitForExit(timeout);

        /// <summary>Closes the island through a second "Halo.exe --exit" launch and returns how long it took to exit.</summary>
        public TimeSpan Exit()
        {
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
        }
    }
}
