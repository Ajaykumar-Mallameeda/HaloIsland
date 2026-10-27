using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Halo.Spikes.Measurement;
using Halo.Spikes.Windows;

namespace Halo.Spikes.Overlay;

/// <summary>
/// S1/S2 test surface for techniques A (layered, <c>AllowsTransparency</c>) and B (non-layered window with a
/// DWM-extended frame, transparent <c>HwndTarget</c> background and a <c>SetWindowRgn</c> input region), ADR §4.5.
/// Throw-away spike code. Logs numbers and fixed tokens only.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private const string AutomationName = "Island test surface";
    private readonly Technique _technique;
    private readonly OverlayOptions _options;
    private readonly SpikeLog _log;
    private readonly int _refreshHz;
    private readonly Grid _shapeHost;
    private readonly Rectangle _shape;
    private readonly Ellipse _glyph;
    private readonly TextBlock _secondLine;
    private IslandGeometry? _geometry;
    private nint _hwnd;
    private bool _expanded;
    private bool _hoverEnabled;
    private Storyboard? _running;

    public OverlayWindow(Technique technique, OverlayOptions options, SpikeLog log, int refreshHz)
    {
        _technique = technique;
        _options = options;
        _log = log;
        _refreshHz = refreshHz;

        Title = "Halo Spike Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = technique == Technique.A;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Width = IslandGeometry.CanvasWidthDip;
        Height = IslandGeometry.CanvasHeightDip;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -10000;
        Top = -10000;

        _shape = new Rectangle { Fill = Brushes.Black, RadiusX = IslandGeometry.CompactRadiusDip, RadiusY = IslandGeometry.CompactRadiusDip };
        _glyph = new Ellipse { Width = IslandGeometry.GlyphDiameterDip, Height = IslandGeometry.GlyphDiameterDip, Fill = Brushes.White, Margin = new Thickness(10, 0, 0, 0) };
        var time = new TextBlock { Text = "12:34", Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 13 * 96.0 / 72.0, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(time, "CompactText");
        var compact = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Height = IslandGeometry.CompactHeightDip };
        compact.Children.Add(time);
        compact.Children.Add(_glyph);
        _secondLine = new TextBlock { Text = "Expanded test line", Foreground = Brushes.White, FontSize = 16, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        _shapeHost = new Grid
        {
            Width = IslandGeometry.CompactWidthDip,
            Height = IslandGeometry.CompactHeightDip,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
        };
        AutomationProperties.SetName(_shapeHost, AutomationName);
        AutomationProperties.SetAutomationId(_shapeHost, "IslandShape");
        _shapeHost.Children.Add(_shape);
        _shapeHost.Children.Add(compact);
        _shapeHost.Children.Add(_secondLine);
        _shapeHost.MouseEnter += OnShapeMouseEnter;
        _shapeHost.MouseLeave += OnShapeMouseLeave;
        _shapeHost.PreviewMouseLeftButtonDown += OnShapeMouseDown;

        // No background on the root: everything outside the shape is fully transparent (alpha 0).
        Content = new Grid { Children = { _shapeHost } };
        AutomationProperties.SetName(this, AutomationName);

        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Place("dpi_changed"));
    }

    public nint Hwnd => _hwnd;

    public void Handle(OverlayCommand command)
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
                Hide();
                _log.Write("suppressed", Qpc());
                break;
            case OverlayCommand.Restore:
                Show();
                _log.Write("restored", Qpc());
                break;
            case OverlayCommand.HoverOn:
                _hoverEnabled = true;
                break;
            case OverlayCommand.HoverOff:
                _hoverEnabled = false;
                break;
            case OverlayCommand.Ping:
                _log.Write("pong", Qpc());
                break;
            default:
                _log.Write("cmd.unknown");
                break;
        }
    }

    private static LogField Qpc() => LogField.Num("qpc", Stopwatch.GetTimestamp());

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = Win32.ExStyle(_hwnd) | Win32.WsExToolWindow | Win32.WsExNoActivate | Win32.WsExTopmost;
        Win32.SetWindowLongPtr(_hwnd, Win32.GwlExStyle, (nint)ex);

        if (_technique == Technique.B)
        {
            Win32.SetWindowLongPtr(_hwnd, Win32.GwlStyle, (nint)(Win32.WsPopup | (Win32.GetWindowLongPtr(_hwnd, Win32.GwlStyle) & Win32.WsVisible)));
            var source = HwndSource.FromHwnd(_hwnd);
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            Win32.DwmExtendFrameIntoClientArea(_hwnd, new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 });
            Win32.DwmSetWindowAttribute(_hwnd, Win32.DwmwaWindowCornerPreference, Win32.DwmwcpDoNotRound, sizeof(int));
            Win32.DwmSetWindowAttribute(_hwnd, Win32.DwmwaBorderColor, Win32.DwmwaColorNone, sizeof(int));
        }

        HwndSource.FromHwnd(_hwnd).AddHook(WndProc);
        Place("initial");
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == (int)Win32.WmDisplayChange || (msg == (int)Win32.WmSettingChange && wParam == (nint)Win32.SpiSetWorkArea))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => Place(msg == (int)Win32.WmDisplayChange ? "display_changed" : "work_area_changed"));
        }

        return 0;
    }

    /// <summary>Sizes and centres the fixed canvas under the top edge of the primary work area (ADR §4.3).</summary>
    private void Place(string reason)
    {
        var (work, dpi) = Win32.PrimaryWorkArea();
        _geometry = IslandGeometry.Compute(PxRect.FromEdges(work.Left, work.Top, work.Right, work.Bottom), (int)dpi);
        var c = _geometry.Canvas;
        Win32.SetWindowPos(_hwnd, Win32.HwndTopmost, c.Left, c.Top, c.Width, c.Height, Win32.SwpNoActivate);
        ApplyRegion(_expanded ? Target.Expanded : Target.Compact);
        _log.Write("placement", LogField.Tok("reason", reason), LogField.Num("left", c.Left), LogField.Num("top", c.Top), LogField.Num("width", c.Width), LogField.Num("height", c.Height), LogField.Num("dpi", dpi));
    }

    private enum Target
    {
        Compact,
        Expanded,
        Union,
    }

    /// <summary>B only: the input region is the union of current and target shapes during a transition (ADR §4.4).</summary>
    private void ApplyRegion(Target target)
    {
        if (_technique != Technique.B || _geometry is null)
        {
            return;
        }

        var compact = Shape(_geometry.Compact);
        var expanded = Shape(_geometry.Expanded);
        switch (target)
        {
            case Target.Compact:
                Win32.SetRoundedRegion(_hwnd, compact);
                break;
            case Target.Expanded:
                Win32.SetRoundedRegion(_hwnd, expanded);
                break;
            default:
                Win32.SetRoundedRegion(_hwnd, compact, expanded);
                break;
        }

        static (int, int, int, int, int) Shape(RoundedRect r) =>
            ((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom), (int)Math.Round(r.Radius));
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        var c = _geometry?.Canvas ?? default;
        _log.Write(
            "overlay.ready",
            Qpc(),
            LogField.Num("hwnd", _hwnd),
            LogField.Num("pid", Environment.ProcessId),
            LogField.Num("left", c.Left),
            LogField.Num("top", c.Top),
            LogField.Num("width", c.Width),
            LogField.Num("height", c.Height));

        if (_options.TickMs > 0)
        {
            var on = false;
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(_options.TickMs) };
            timer.Tick += (_, _) =>
            {
                on = !on;
                _glyph.Opacity = on ? 0.98 : 1.0;
                _log.Write("tick", Qpc());
            };
            timer.Start();
        }

        if (_options.Continuous)
        {
            Transition(true, forever: true);
        }
    }

    private void Transition(bool expand, bool forever = false)
    {
        if (expand == _expanded && !forever)
        {
            _log.Write("cmd.ignored", LogField.Tok("reason", "same_state"));
            return;
        }

        _expanded = expand;
        ApplyRegion(Target.Union);
        var duration = new Duration(IslandGeometry.TransitionDuration);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb = new Storyboard();
        Timeline.SetDesiredFrameRate(sb, _refreshHz);
        if (forever)
        {
            sb.AutoReverse = true;
            sb.RepeatBehavior = RepeatBehavior.Forever;
        }

        Add(sb, _shapeHost, FrameworkElement.WidthProperty, expand ? IslandGeometry.ExpandedWidthDip : IslandGeometry.CompactWidthDip);
        Add(sb, _shapeHost, FrameworkElement.HeightProperty, expand ? IslandGeometry.ExpandedHeightDip : IslandGeometry.CompactHeightDip);
        var radius = expand ? IslandGeometry.ExpandedRadiusDip : IslandGeometry.CompactRadiusDip;
        Add(sb, _shape, Rectangle.RadiusXProperty, radius);
        Add(sb, _shape, Rectangle.RadiusYProperty, radius);
        Add(sb, _secondLine, OpacityProperty, expand ? 1 : 0);

        void Add(Storyboard board, DependencyObject target, DependencyProperty property, double to)
        {
            var animation = new DoubleAnimation(to, duration) { EasingFunction = ease };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            board.Children.Add(animation);
        }

        sb.Completed += (_, _) =>
        {
            if (ReferenceEquals(_running, sb))
            {
                ApplyRegion(_expanded ? Target.Expanded : Target.Compact);
                _log.Write("transition.end", Qpc(), LogField.Tok("dir", expand ? "expand" : "collapse"));
            }
        };

        _running = sb;
        _log.Write("transition.start", Qpc(), LogField.Tok("dir", expand ? "expand" : "collapse"));
        sb.Begin(this, HandoffBehavior.SnapshotAndReplace);

        if (_options.StallMs > 0)
        {
            var stall = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(175) };
            stall.Tick += (_, _) =>
            {
                stall.Stop();
                _log.Write("stall.injected", Qpc(), LogField.Num("ms", _options.StallMs));
                Thread.Sleep(_options.StallMs);
            };
            stall.Start();
        }
    }

    private void OnShapeMouseEnter(object sender, MouseEventArgs e)
    {
        if (_hoverEnabled)
        {
            _log.Write("hover.enter", Qpc());
            Transition(true);
        }
    }

    private void OnShapeMouseLeave(object sender, MouseEventArgs e)
    {
        if (_hoverEnabled)
        {
            _log.Write("hover.leave", Qpc());
            Transition(false);
        }
    }

    private void OnShapeMouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = PointToScreen(e.GetPosition(this));
        _log.Write("click.inside", Qpc(), LogField.Num("x", Math.Round(p.X)), LogField.Num("y", Math.Round(p.Y)));
    }
}
