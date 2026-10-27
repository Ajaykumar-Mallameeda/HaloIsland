using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Harness;

/// <summary>A dedicated STA thread running a WPF dispatcher for the harness's own windows.</summary>
internal sealed class UiThread : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher? _dispatcher;

    public UiThread()
    {
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        { IsBackground = true, Name = "harness-ui" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait();
    }

    public T Invoke<T>(Func<T> func) => _dispatcher!.Invoke(func);

    public void Invoke(Action action) => _dispatcher!.Invoke(action);

    public void Dispose()
    {
        _dispatcher?.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }

    /// <summary>Places a window at exact physical pixels after it has a handle.</summary>
    public static nint PlacePx(Window window, PxRect rect, bool topmost)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        Win32.SetWindowPos(hwnd, topmost ? Win32.HwndTopmost : 0, rect.Left, rect.Top, rect.Width, rect.Height, Win32.SwpNoActivate | (topmost ? 0 : Win32.SwpNoZOrder));
        return hwnd;
    }
}

/// <summary>Static, topmost, non-activating solid window under the overlay canvas (§8.3/§8.5).</summary>
internal sealed class Backdrop
{
    private readonly UiThread _ui;
    private Window? _window;

    public Backdrop(UiThread ui, PxRect canvas)
    {
        _ui = ui;
        Hwnd = ui.Invoke(() =>
        {
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                Title = "Halo Harness Backdrop",
            };
            var hwnd = UiThread.PlacePx(_window, canvas, topmost: true);
            Win32.SetWindowLongPtr(hwnd, Win32.GwlExStyle, (nint)(Win32.ExStyle(hwnd) | Win32.WsExNoActivate | Win32.WsExToolWindow));
            _window.Show();
            UiThread.PlacePx(_window, canvas, topmost: true);
            return hwnd;
        });
        Thread.Sleep(500); // let the static backdrop paint before measuring
    }

    public nint Hwnd { get; }

    public void Close() => _ui.Invoke(() => _window?.Close());
}

/// <summary>S2 probe: maximised normal window in a known colour that records clicks and keystrokes (§8.11).</summary>
internal sealed class Probe
{
    public static readonly Color ProbeColor = Color.FromRgb(0xFF, 0x00, 0xFF);
    private readonly UiThread _ui;
    private readonly ConcurrentQueue<(int X, int Y, long Qpc)> _clicks = new();
    private Window? _window;
    private TextBox? _text;

    public Probe(UiThread ui)
    {
        _ui = ui;
        Hwnd = ui.Invoke(() =>
        {
            _text = new TextBox { Width = 300, Height = 32, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 120) };
            _window = new Window
            {
                Title = "Halo S2 Probe",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                WindowState = WindowState.Maximized,
                Background = new SolidColorBrush(ProbeColor),
                Content = new Grid { Children = { _text } },
            };
            _window.PreviewMouseLeftButtonDown += (_, e) =>
            {
                var p = _window.PointToScreen(e.GetPosition(_window));
                _clicks.Enqueue(((int)Math.Round(p.X), (int)Math.Round(p.Y), System.Diagnostics.Stopwatch.GetTimestamp()));
            };
            _window.Show();
            return new WindowInteropHelper(_window).Handle;
        });
        Thread.Sleep(500);
    }

    public nint Hwnd { get; }

    public IReadOnlyList<(int X, int Y, long Qpc)> Clicks => _clicks.ToArray();

    public System.Drawing.Point TextBoxCenter => _ui.Invoke(() =>
    {
        var p = _text!.PointToScreen(new System.Windows.Point(_text.ActualWidth / 2, _text.ActualHeight / 2));
        return new System.Drawing.Point((int)p.X, (int)p.Y);
    });

    public string Text => _ui.Invoke(() => _text!.Text);

    public void ClearText() => _ui.Invoke(() => _text!.Clear());

    public void Close() => _ui.Invoke(() => _window?.Close());
}

/// <summary>Monitor-sized borderless window used as a full-screen stand-in for S2-10.</summary>
internal sealed class FullscreenProbe
{
    public static readonly Color FullscreenColor = Color.FromRgb(0x00, 0xFF, 0x00);
    private readonly UiThread _ui;
    private Window? _window;

    public FullscreenProbe(UiThread ui, PxRect monitor, bool topmost)
    {
        _ui = ui;
        Hwnd = ui.Invoke(() =>
        {
            _window = new Window
            {
                Title = "Halo S2 Fullscreen Probe",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = topmost,
                Background = new SolidColorBrush(FullscreenColor),
            };
            var hwnd = UiThread.PlacePx(_window, monitor, topmost);
            _window.Show();
            UiThread.PlacePx(_window, monitor, topmost);
            return hwnd;
        });
        Thread.Sleep(500);
    }

    public nint Hwnd { get; }

    public void Close() => _ui.Invoke(() => _window?.Close());
}

internal static class Png
{
    /// <summary>Saves captured test-region pixels (probe colours and overlay test content only) as evidence.</summary>
    public static void Save(RegionPixels pixels, string path)
    {
        var bitmap = BitmapSource.Create(pixels.Rect.Width, pixels.Rect.Height, 96, 96, PixelFormats.Bgr32, null, pixels.Bgra, pixels.Rect.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
