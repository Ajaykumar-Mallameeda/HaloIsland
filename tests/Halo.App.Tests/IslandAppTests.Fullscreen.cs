using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// FR-ISL-09 on the real desktop: a window owned by the test is made ordinary, maximised and borderless full screen,
/// and the island's actual window visibility is checked. Part of <see cref="IslandAppTests"/> so desktop tests never
/// run concurrently.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint TrayCallback = 0x8000 + 2; // TrayIcon.CallbackMessage
    private const uint WmContextMenuCode = 0x007B;

    [Fact]
    public void FullscreenWindowHidesTheIslandAndOrdinaryOrMaximisedWindowsDoNot()
    {
        using var island = IslandProcess.Start();
        using var window = TestWindow.Create();
        window.Normal();
        window.ClickToForeground();

        Thread.Sleep(500);
        Assert.True(IsWindowVisible(island.Hwnd), "hidden by an ordinary window");

        window.Maximize();
        Thread.Sleep(500);
        Assert.True(IsWindowVisible(island.Hwnd), "hidden by a maximised window");

        window.Fullscreen();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "not hidden by a full-screen window");
        Assert.Equal("started", island.WaitFor("fullscreen")["state"]!.GetValue<string>());

        // Full screen ends in the same window (no foreground change): the location hook sees it.
        window.Normal();
        Assert.True(WaitUntil(() => IsWindowVisible(island.Hwnd)), "not shown again after full screen ended");
        Assert.Equal("ended", island.WaitFor("fullscreen", skip: 1)["state"]!.GetValue<string>());

        // FR-ISL-06: hiding and showing never took the foreground.
        AssertForeground(window.Hwnd);
    }

    [Fact]
    public void SwitchingTheForegroundUpdatesSuppression()
    {
        using var island = IslandProcess.Start();
        using var other = TestWindow.Create();
        using var full = TestWindow.Create();
        other.Normal();
        full.Fullscreen();
        full.ClickToForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "not hidden by the full-screen window");

        other.SetForeground();
        Assert.True(WaitUntil(() => IsWindowVisible(island.Hwnd)), "still hidden after an ordinary window took the foreground");

        full.SetForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "not hidden again when the full-screen window returned");
        AssertForeground(full.Hwnd);
    }

    /// <summary>
    /// The taskbar is covered while a window is full screen, so the test sends the tray callback itself (the same
    /// message Explorer sends) and grants the island the foreground right as Explorer does; the menu is real.
    /// </summary>
    [Fact]
    public void ShowIslandNowOverridesSuppressionAndEndsWithFullscreen()
    {
        using var island = IslandProcess.Start();
        using var window = TestWindow.Create();
        window.Fullscreen();
        window.ClickToForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "not hidden by the full-screen window");

        // Open, Collapse, Show island now (Down ×3), Enter.
        OpenTrayMenuDirect(island);
        PressKeys(0x28, 0x28, 0x28, 0x0D);
        Assert.True(WaitUntil(() => IsWindowVisible(island.Hwnd)), "Show island now did not show the island");
        Thread.Sleep(600);
        Assert.True(IsWindowVisible(island.Hwnd), "the island was hidden again while the override was on");
        AssertForeground(window.Hwnd); // no focus loop: the full-screen app keeps the foreground

        // The same item again returns to automatic suppression.
        OpenTrayMenuDirect(island);
        PressKeys(0x28, 0x28, 0x28, 0x0D);
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "clearing the override did not hide the island");

        // Override on, then full screen ends: the override ends with it, so the next full screen hides the island.
        OpenTrayMenuDirect(island);
        PressKeys(0x28, 0x28, 0x28, 0x0D);
        Assert.True(WaitUntil(() => IsWindowVisible(island.Hwnd)));
        window.Normal();
        island.WaitFor("fullscreen", skip: 1);
        window.Fullscreen();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "the override outlived the full-screen condition");
    }

    [Fact]
    public void ExplorerRestartSignalReRegistersAndDetectionStillWorks()
    {
        using var island = IslandProcess.Start();
        using var window = TestWindow.Create();

        // A real Explorer restart would disrupt the owner's session; TaskbarCreated is what the island reacts to.
        Assert.True(PostMessageW(island.Hwnd, RegisterWindowMessageW("TaskbarCreated"), 0, 0));
        var tray = island.WaitFor("tray", skip: 1);
        Assert.Equal("taskbar_created", tray["reason"]!.GetValue<string>());
        Assert.Equal("added", tray["status"]!.GetValue<string>());

        window.Fullscreen();
        window.ClickToForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "detection stopped after TaskbarCreated");
        Assert.False(island.HasExited);
    }

    [Fact]
    public void ExitWhileSuppressedIsCleanAndDetectionIsCheap()
    {
        using var island = IslandProcess.Start();
        using var window = TestWindow.Create();
        window.Fullscreen();
        window.ClickToForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)));

        // No polling: with a full-screen window in front and no input, the island uses almost no CPU.
        using var process = Process.GetProcessById(island.Pid);
        var before = process.TotalProcessorTime;
        Thread.Sleep(5000);
        process.Refresh();
        var used = (process.TotalProcessorTime - before).TotalMilliseconds;
        Assert.True(used < 100, $"island used {used} ms CPU in 5 s while suppressed");

        Assert.True(island.Exit().TotalMilliseconds < 5000);
        Assert.Equal(0, island.ExitCode);
        Assert.Contains(island.Events("tray"), e => e["status"]?.GetValue<string>() == "removed");
        Assert.Empty(Process.GetProcessesByName("Halo"));
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    private static void OpenTrayMenuDirect(IslandProcess island)
    {
        // The test owns the foreground, so it may pass the right to set it on, as Explorer does for tray icons.
        Assert.True(AllowSetForegroundWindow((uint)island.Pid));
        var (x, y) = (GetSystemMetrics(0) - 100, GetSystemMetrics(1) - 20);
        Assert.True(PostMessageW(island.Hwnd, TrayCallback, (y << 16) | x, (1 << 16) | (int)WmContextMenuCode));
        Assert.NotEqual(0, WaitForWindow("#32768"));
        Thread.Sleep(150);
    }

#pragma warning disable SYSLIB1054 // test-only signatures
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
#pragma warning restore SYSLIB1054

    /// <summary>
    /// A plain top-level window owned by the test, with its own message loop thread and DefWindowProc as its window
    /// procedure, so it behaves like an ordinary application window.
    /// </summary>
    private sealed unsafe class TestWindow : IDisposable
    {
        private const string ClassName = "HaloTestWindow";
        private const long WsOverlappedWindow = 0x00CF0000;
        private const long WsPopup = unchecked((int)0x80000000);
        private const long WsVisible = 0x10000000;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;
        private static bool s_registered;

        private readonly Thread _thread;
        private uint _threadId;

        private TestWindow()
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() => Loop(ready)) { IsBackground = true, Name = "TestWindow" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            Assert.True(ready.Wait(5000), "test window thread did not start");
            Assert.NotEqual(0, Hwnd);
        }

        public nint Hwnd { get; private set; }

        public static TestWindow Create() => new();

        public void Normal()
        {
            _ = ShowWindow(Hwnd, 9); // SW_RESTORE
            _ = SetWindowLongPtrW(Hwnd, -16, (nint)(WsOverlappedWindow | WsVisible));
            _ = SetWindowPos(Hwnd, 0, 200, 150, 800, 600, SwpFrameChanged | SwpNoZOrder);
        }

        public void Maximize()
        {
            Normal();
            _ = ShowWindow(Hwnd, 3); // SW_MAXIMIZE
        }

        public void Fullscreen()
        {
            _ = ShowWindow(Hwnd, 9);
            _ = SetWindowLongPtrW(Hwnd, -16, (nint)(WsPopup | WsVisible));
            _ = SetWindowPos(Hwnd, 0, 0, 0, GetSystemMetrics(0), GetSystemMetrics(1), SwpFrameChanged | SwpNoZOrder);
        }

        /// <summary>
        /// Makes the window foreground the way a user does: a click on it. A window created by a background process
        /// is not raised, so it is made topmost for the click (the click must land on it, not on whatever is in
        /// front) and returned to normal stacking afterwards.
        /// </summary>
        public void ClickToForeground()
        {
            _ = SetWindowPos(Hwnd, -1, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate); // HWND_TOPMOST
            _ = GetWindowRect(Hwnd, out var r);
            ClickAt((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            Assert.True(WaitUntil(() => GetForegroundWindow() == Hwnd), "test window did not become foreground");
            _ = SetWindowPos(Hwnd, -2, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate); // HWND_NOTOPMOST
        }

        /// <summary>The test process is foreground after a click, so it may move the foreground between its windows.</summary>
        public void SetForeground()
        {
            _ = SetForegroundWindow(Hwnd);
            Assert.True(WaitUntil(() => GetForegroundWindow() == Hwnd), "test window did not become foreground");
        }

        public void Dispose()
        {
            if (Hwnd != 0)
            {
                _ = PostMessageW(Hwnd, 0x0010, 0, 0); // WM_CLOSE → DefWindowProc destroys it
            }

            _ = PostThreadMessageW(_threadId, 0x0012, 0, 0); // WM_QUIT
            _ = _thread.Join(5000);
        }

        private static void ClickAt(int x, int y)
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

        private void Loop(ManualResetEventSlim ready)
        {
            _threadId = GetCurrentThreadId();
            var instance = GetModuleHandleW(null);
            fixed (char* name = ClassName)
            {
                if (!s_registered)
                {
                    var wc = new WndClass
                    {
                        Size = (uint)sizeof(WndClass),
                        WndProc = GetProcAddress(GetModuleHandleW("user32.dll"), "DefWindowProcW"),
                        Instance = instance,
                        Cursor = LoadCursorW(0, 32512),
                        Background = GetStockObject(4), // BLACK_BRUSH
                        ClassName = name,
                    };
                    s_registered = RegisterClassExW(&wc) != 0;
                }

                Hwnd = CreateWindowExW(0, name, name, (uint)(WsOverlappedWindow | WsVisible), 200, 150, 800, 600, 0, 0, instance, 0);
            }

            ready.Set();
            while (GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(ref msg);
                _ = DispatchMessageW(ref msg);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WndClass
        {
            public uint Size;
            public uint Style;
            public nint WndProc;
            public int ClassExtra;
            public int WindowExtra;
            public nint Instance;
            public nint Icon;
            public nint Cursor;
            public nint Background;
            public char* MenuName;
            public char* ClassName;
            public nint SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public nint Hwnd;
            public uint Message;
            public nint WParam;
            public nint LParam;
            public uint Time;
            public int X;
            public int Y;
            public uint Private;
        }

#pragma warning disable SYSLIB1054
        [DllImport("user32.dll")]
        private static extern ushort RegisterClassExW(WndClass* wc);

        [DllImport("user32.dll")]
        private static extern nint CreateWindowExW(uint exStyle, char* className, char* name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll")]
        private static extern int GetMessageW(out Msg msg, nint hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref Msg msg);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessageW(ref Msg msg);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessageW(uint thread, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hwnd, int cmd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

        [DllImport("user32.dll")]
        private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hwnd);

        [DllImport("user32.dll")]
        private static extern nint LoadCursorW(nint instance, nint name);

        [DllImport("gdi32.dll")]
        private static extern nint GetStockObject(int index);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandleW(string? name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
        private static extern nint GetProcAddress(nint module, string name);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
#pragma warning restore SYSLIB1054
    }
}
