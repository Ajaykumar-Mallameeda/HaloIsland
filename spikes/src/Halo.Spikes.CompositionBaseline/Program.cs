using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Halo.Spikes.CompositionBaseline;

/// <summary>
/// Technique C baseline: same canvas, shapes, glyph, timing and commands as the WPF overlay, without text or UIA
/// (documented differences, PROTOTYPE_S1_S2_REPORT §6). Logs numbers and fixed tokens only.
/// </summary>
internal static unsafe partial class Program
{
    private const uint WmDestroy = 0x0002;
    private const uint WmTimer = 0x0113;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmMouseLeave = 0x02A3;
    private const uint WmDpiChanged = 0x02E0;
    private const uint WmApp = 0x8000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint TmeLeave = 0x00000002;

    private static SpikeLog s_log = null!;
    private static OverlayOptions s_options = null!;
    private static nint s_hwnd;
    private static Compositor s_compositor = null!;
    private static ContainerVisual s_root = null!;
    private static ShapeVisual s_shapeVisual = null!;
    private static CompositionRoundedRectangleGeometry s_shape = null!;
    private static CompositionEllipseGeometry s_glyph = null!;
    private static CompositionColorBrush s_glyphBrush = null!;
    private static CompositionRoundedRectangleGeometry s_bar = null!;
    private static CompositionColorBrush s_barBrush = null!;
    private static IslandGeometry s_geometry = null!;
    private static bool s_expanded;
    private static bool s_hover;
    private static bool s_tracking;
    private static bool s_tickOn;
    private static int s_transitionId;

    [STAThread]
    public static int Main(string[] args)
    {
        s_options = OverlayOptions.Parse(args, new HashSet<Technique> { Technique.C });
        if (s_options.Mode != OverlayMode.Run)
        {
            return 2;
        }

        var stream = s_options.LogPath is { } path ? (Stream)File.Create(path) : Console.OpenStandardOutput();
        using var sink = TextWriter.Synchronized(new StreamWriter(stream) { AutoFlush = true });
        s_log = new SpikeLog(sink, TimeProvider.System);
        try
        {
            var env = EnvironmentSnapshot.Capture();
            s_log.Write(
                "env.captured",
                LogField.Tok("os", env.OsVersion),
                LogField.Tok("runtime", env.RuntimeVersion.Replace(' ', '_')),
                LogField.Num("width_px", env.PrimaryWidthPx),
                LogField.Num("height_px", env.PrimaryHeightPx),
                LogField.Num("refresh_hz", env.PrimaryRefreshHz),
                LogField.Num("dpi", env.PrimaryDpi),
                LogField.Tok("power", env.PowerSource.ToString()),
                LogField.Tok("power_overlay", env.PowerModeOverlay?.ToString("D", CultureInfo.InvariantCulture) ?? "unknown"));
            s_log.Write(
                "overlay.config",
                LogField.Tok("technique", "C"),
                LogField.Num("calibration", s_options.Calibration ? 1 : 0),
                LogField.Num("stall_ms", 0),
                LogField.Num("tick_ms", s_options.TickMs),
                LogField.Num("continuous", s_options.Continuous ? 1 : 0),
                LogField.Num("desired_fps", env.PrimaryRefreshHz),
                LogField.Num("pid", Environment.ProcessId),
                LogField.Num("qpc_frequency", Stopwatch.Frequency));
            var code = Run();
            s_log.Write("overlay.exit", LogField.Num("code", code));
            return code;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            s_log.WriteError("spike.failed", ex);
            return 1;
        }
    }

    private static LogField Qpc() => LogField.Num("qpc", Stopwatch.GetTimestamp());

    private static int Run()
    {
        // A DispatcherQueue on this thread is required by Windows.UI.Composition in a Win32 app.
        var dq = new DispatcherQueueOptions { Size = sizeof(DispatcherQueueOptions), ThreadType = 2, ApartmentType = 2 };
        Check(CreateDispatcherQueueController(dq, out _), "CreateDispatcherQueueController");

        var instance = GetModuleHandleW(null);
        fixed (char* className = "HaloSpikeCompositionBaseline")
        fixed (char* title = "Halo Spike Overlay")
        {
            var wc = new WndClassEx
            {
                Size = (uint)sizeof(WndClassEx),
                WndProc = &WndProc,
                Instance = instance,
                Cursor = LoadCursorW(0, 32512),
                ClassName = className,
            };
            if (RegisterClassExW(&wc) == 0)
            {
                throw new InvalidOperationException("RegisterClassEx failed.");
            }

            const uint ex = (uint)(Win32.WsExNoRedirectionBitmap | Win32.WsExTopmost | Win32.WsExToolWindow | Win32.WsExNoActivate);
            s_hwnd = CreateWindowExW(ex, className, title, unchecked((uint)Win32.WsPopup), 0, 0, 1, 1, 0, 0, instance, 0);
        }

        if (s_hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed.");
        }

        Place("initial");
        BuildVisualTree();
        ShowWindow(s_hwnd, SwShowNoActivate);

        // The commit batch completes once the first visual tree has been committed to DWM.
        var ready = s_compositor.GetCommitBatch(CompositionBatchTypes.Animation);
        ready.Completed += (_, _) =>
        {
            var c = s_geometry.Canvas;
            s_log.Write("overlay.ready", Qpc(), LogField.Num("hwnd", s_hwnd), LogField.Num("pid", Environment.ProcessId), LogField.Num("left", c.Left), LogField.Num("top", c.Top), LogField.Num("width", c.Width), LogField.Num("height", c.Height));
        };

        if (s_options.TickMs > 0)
        {
            SetTimer(s_hwnd, 1, (uint)s_options.TickMs, 0);
        }

        if (s_options.Continuous)
        {
            StartContinuous();
        }

        new Thread(ReadCommands) { IsBackground = true, Name = "stdin-commands" }.Start();

        Msg msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        return 0;
    }

    private static void BuildVisualTree()
    {
        s_compositor = new Compositor();
        var unknown = ((WinRT.IWinRTObject)s_compositor).NativeObject.ThisPtr;
        var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
        Check(Marshal.QueryInterface(unknown, in iid, out var interop), "QueryInterface(ICompositorDesktopInterop)");
        nint targetAbi;
        // ICompositorDesktopInterop::CreateDesktopWindowTarget(HWND, BOOL isTopmost, IDesktopWindowTarget**) is vtable slot 3.
        Check(((delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)(*(nint**)interop)[3])(interop, s_hwnd, 1, &targetAbi), "CreateDesktopWindowTarget");
        Marshal.Release(interop);
        var target = DesktopWindowTarget.FromAbi(targetAbi);

        s_root = s_compositor.CreateContainerVisual();
        s_shapeVisual = s_compositor.CreateShapeVisual();
        s_shape = s_compositor.CreateRoundedRectangleGeometry();
        var shape = s_compositor.CreateSpriteShape(s_shape);
        shape.FillBrush = s_compositor.CreateColorBrush(Color.FromArgb(255, 0, 0, 0));
        s_glyph = s_compositor.CreateEllipseGeometry();
        s_glyphBrush = s_compositor.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));
        var glyph = s_compositor.CreateSpriteShape(s_glyph);
        glyph.FillBrush = s_glyphBrush;
        // Placeholder for A/B's second text line (C has no text): a bar that fades in when expanded.
        s_bar = s_compositor.CreateRoundedRectangleGeometry();
        s_barBrush = s_compositor.CreateColorBrush(Color.FromArgb(0, 255, 255, 255));
        var bar = s_compositor.CreateSpriteShape(s_bar);
        bar.FillBrush = s_barBrush;
        s_shapeVisual.Shapes.Add(shape);
        s_shapeVisual.Shapes.Add(glyph);
        s_shapeVisual.Shapes.Add(bar);
        s_root.Children.InsertAtTop(s_shapeVisual);
        target.Root = s_root;
        ApplyStaticLayout();
    }

    /// <summary>Sets sizes for the current DPI and presentation without animation.</summary>
    private static void ApplyStaticLayout()
    {
        var g = s_geometry;
        var canvas = new Vector2(g.Canvas.Width, g.Canvas.Height);
        s_root.Size = canvas;
        s_shapeVisual.Size = canvas;
        var r = s_expanded ? g.Expanded : g.Compact;
        s_shape.Size = new Vector2((float)r.Width, (float)r.Height);
        s_shape.Offset = new Vector2((float)r.Left, (float)r.Top);
        s_shape.CornerRadius = new Vector2((float)r.Radius);
        var glyphRadius = (float)(IslandGeometry.GlyphDiameterDip / 2 * g.Scale);
        s_glyph.Radius = new Vector2(glyphRadius);
        s_glyph.Center = new Vector2((float)(g.Canvas.Width / 2.0 + (30 * g.Scale)), (float)(IslandGeometry.CompactHeightDip / 2 * g.Scale));
        s_bar.Size = new Vector2((float)(160 * g.Scale), (float)(6 * g.Scale));
        s_bar.CornerRadius = new Vector2((float)(3 * g.Scale));
        s_bar.Offset = new Vector2((float)((g.Canvas.Width - (160 * g.Scale)) / 2), (float)(100 * g.Scale));
        s_barBrush.Color = Color.FromArgb(s_expanded ? (byte)255 : (byte)0, 255, 255, 255);
    }

    private static (Vector2 Size, Vector2 Offset, Vector2 Radius) Target(bool expanded)
    {
        var r = expanded ? s_geometry.Expanded : s_geometry.Compact;
        return (new Vector2((float)r.Width, (float)r.Height), new Vector2((float)r.Left, (float)r.Top), new Vector2((float)r.Radius));
    }

    private static void Transition(bool expand)
    {
        if (expand == s_expanded)
        {
            s_log.Write("cmd.ignored", LogField.Tok("reason", "same_state"));
            return;
        }

        s_expanded = expand;
        ApplyRegion(union: true);
        var id = ++s_transitionId;
        var (size, offset, radius) = Target(expand);
        var ease = s_compositor.CreateCubicBezierEasingFunction(new Vector2(0.33f, 1f), new Vector2(0.68f, 1f));
        var duration = IslandGeometry.TransitionDuration;
        var batch = s_compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        s_shape.StartAnimation("Size", V2(size, ease, duration));
        s_shape.StartAnimation("Offset", V2(offset, ease, duration));
        s_shape.StartAnimation("CornerRadius", V2(radius, ease, duration));
        var fade = s_compositor.CreateColorKeyFrameAnimation();
        fade.InsertKeyFrame(1f, Color.FromArgb(expand ? (byte)255 : (byte)0, 255, 255, 255), ease);
        fade.Duration = duration;
        s_barBrush.StartAnimation("Color", fade);
        batch.End();
        s_log.Write("transition.start", Qpc(), LogField.Tok("dir", expand ? "expand" : "collapse"));
        batch.Completed += (_, _) =>
        {
            if (id == s_transitionId)
            {
                s_log.Write("transition.end", Qpc(), LogField.Tok("dir", expand ? "expand" : "collapse"));
                // Region changes must happen on the window thread.
                Win32.PostMessage(s_hwnd, WmApp + 100, 0, 0);
            }
        };
    }

    private static Vector2KeyFrameAnimation V2(Vector2 to, CompositionEasingFunction ease, TimeSpan duration)
    {
        var a = s_compositor.CreateVector2KeyFrameAnimation();
        a.InsertKeyFrame(1f, to, ease);
        a.Duration = duration;
        return a;
    }

    /// <summary>Calibration positive control: animation runs forever in DWM, alternating Compact ↔ Expanded.</summary>
    private static void StartContinuous()
    {
        var ease = s_compositor.CreateCubicBezierEasingFunction(new Vector2(0.33f, 1f), new Vector2(0.68f, 1f));
        var from = Target(false);
        var to = Target(true);
        foreach (var (property, a, b) in new[] { ("Size", from.Size, to.Size), ("Offset", from.Offset, to.Offset), ("CornerRadius", from.Radius, to.Radius) })
        {
            var anim = s_compositor.CreateVector2KeyFrameAnimation();
            anim.InsertKeyFrame(0f, a);
            anim.InsertKeyFrame(1f, b, ease);
            anim.Duration = IslandGeometry.TransitionDuration;
            anim.IterationBehavior = AnimationIterationBehavior.Forever;
            anim.Direction = AnimationDirection.Alternate;
            s_shape.StartAnimation(property, anim);
        }

        s_log.Write("continuous.start", Qpc());
    }

    private static void ApplyRegion(bool union)
    {
        static (int, int, int, int, int) Shape(RoundedRect r) =>
            ((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom), (int)Math.Round(r.Radius));
        var compact = Shape(s_geometry.Compact);
        var expanded = Shape(s_geometry.Expanded);
        if (s_options.Continuous)
        {
            Win32.SetRoundedRegion(s_hwnd, expanded);
        }
        else if (union)
        {
            Win32.SetRoundedRegion(s_hwnd, compact, expanded);
        }
        else
        {
            Win32.SetRoundedRegion(s_hwnd, s_expanded ? expanded : compact);
        }
    }

    private static void Place(string reason)
    {
        var (work, dpi) = Win32.PrimaryWorkArea();
        var windowDpi = s_hwnd != 0 ? GetDpiForWindowSafe() : dpi;
        s_geometry = IslandGeometry.Compute(PxRect.FromEdges(work.Left, work.Top, work.Right, work.Bottom), (int)windowDpi);
        var c = s_geometry.Canvas;
        Win32.SetWindowPos(s_hwnd, Win32.HwndTopmost, c.Left, c.Top, c.Width, c.Height, Win32.SwpNoActivate);
        ApplyRegion(union: false);
        if (s_root is not null)
        {
            ApplyStaticLayout();
        }

        s_log.Write("placement", LogField.Tok("reason", reason), LogField.Num("left", c.Left), LogField.Num("top", c.Top), LogField.Num("width", c.Width), LogField.Num("height", c.Height), LogField.Num("dpi", windowDpi));
    }

    private static uint GetDpiForWindowSafe()
    {
        var dpi = Win32.GetDpiForWindow(s_hwnd);
        return dpi == 0 ? 96 : dpi;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            switch (msg)
            {
                case WmApp + 1:
                    Handle((OverlayCommand)wParam);
                    return 0;
                case WmApp + 100:
                    ApplyRegion(union: false);
                    return 0;
                case WmTimer:
                    s_tickOn = !s_tickOn;
                    s_glyphBrush.Color = Color.FromArgb(s_tickOn ? (byte)250 : (byte)255, 255, 255, 255);
                    s_log.Write("tick", Qpc());
                    return 0;
                case WmMouseMove when s_hover && !s_tracking:
                    s_tracking = true;
                    var tme = new TrackMouseEventInfo { Size = (uint)sizeof(TrackMouseEventInfo), Flags = TmeLeave, Hwnd = hwnd };
                    TrackMouseEvent(&tme);
                    s_log.Write("hover.enter", Qpc());
                    Transition(true);
                    return 0;
                case WmMouseLeave:
                    s_tracking = false;
                    if (s_hover)
                    {
                        s_log.Write("hover.leave", Qpc());
                        Transition(false);
                    }

                    return 0;
                case WmLButtonDown:
                    var r = Win32.WindowRect(hwnd);
                    s_log.Write("click.inside", Qpc(), LogField.Num("x", r.Left + (short)(lParam & 0xFFFF)), LogField.Num("y", r.Top + (short)((lParam >> 16) & 0xFFFF)));
                    return 0;
                case WmDpiChanged:
                    Place("dpi_changed");
                    return 0;
                case Win32.WmDisplayChange:
                    Place("display_changed");
                    break;
                case Win32.WmSettingChange when wParam == (nint)Win32.SpiSetWorkArea:
                    Place("work_area_changed");
                    break;
                case WmDestroy:
                    PostQuitMessage(0);
                    return 0;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            s_log.WriteError("wndproc.failed", ex);
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static void Handle(OverlayCommand command)
    {
        switch (command)
        {
            case OverlayCommand.Expand:
                Transition(true);
                break;
            case OverlayCommand.Collapse:
                Transition(false);
                break;
            case OverlayCommand.Suppress:
                ShowWindow(s_hwnd, SwHide);
                s_log.Write("suppressed", Qpc());
                break;
            case OverlayCommand.Restore:
                ShowWindow(s_hwnd, SwShowNoActivate);
                Win32.SetWindowPos(s_hwnd, Win32.HwndTopmost, 0, 0, 0, 0, Win32.SwpNoMove | Win32.SwpNoSize | Win32.SwpNoActivate);
                s_log.Write("restored", Qpc());
                break;
            case OverlayCommand.HoverOn:
                s_hover = true;
                break;
            case OverlayCommand.HoverOff:
                s_hover = false;
                break;
            case OverlayCommand.Ping:
                s_log.Write("pong", Qpc());
                break;
            case OverlayCommand.Quit:
                DestroyWindow(s_hwnd);
                break;
            default:
                s_log.Write("cmd.unknown");
                break;
        }
    }

    /// <summary>Blocks on standard input; end of input means quit, so the baseline never outlives its harness.</summary>
    private static void ReadCommands()
    {
        while (true)
        {
            var line = Console.In.ReadLine();
            var command = line is null ? OverlayCommand.Quit : OverlayCommands.Parse(line);
            s_log.Write("cmd.received", Qpc(), LogField.Tok("cmd", command.Token()));
            Win32.PostMessage(s_hwnd, WmApp + 1, (nint)command, 0);
            if (command == OverlayCommand.Quit)
            {
                return;
            }
        }
    }

    private static void Check(int hr, string what)
    {
        if (hr != 0)
        {
            throw new InvalidOperationException($"{what} failed: 0x{hr:X8}") { HResult = hr };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WndProc;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public nint Hwnd;
        public uint HoverTime;
    }

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WndClassEx* wc);

    [LibraryImport("user32.dll")]
    private static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Msg* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(TrackMouseEventInfo* info);
}
