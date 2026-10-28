using System.Numerics;
using Halo.App.Interop;
using Halo.Core.Clock;
using Halo.Core.Island;
using Windows.UI;
using Windows.UI.Composition;

namespace Halo.App.Island;

/// <summary>
/// The island's Windows.UI.Composition visual tree (ADR-001 §4.5, option C): a black rounded shape whose size,
/// position and corner radius animate in DWM, with compact and expanded content cross-fading inside a clip that
/// follows the shape. Nothing renders while settled except a text redraw when the clock's minute changes.
/// </summary>
internal sealed class IslandVisuals : IDisposable
{
    private const string DisplayFont = "Segoe UI Variable Display";
    private const string TextFont = "Segoe UI Variable Text";
    private const int Regular = 400;
    private const int Semibold = 600;

    private static readonly Color Black = Color.FromArgb(255, 0, 0, 0);
    private static readonly Color HoverFill = Color.FromArgb(255, 22, 22, 24);
    private static readonly Color Edge = Color.FromArgb(40, 255, 255, 255);
    private static readonly Color HoverEdge = Color.FromArgb(72, 255, 255, 255);
    private static readonly Color Primary = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color Secondary = Color.FromArgb(184, 255, 255, 255);
    private static readonly Color Tertiary = Color.FromArgb(140, 255, 255, 255);
    private static readonly Color Track = Color.FromArgb(46, 255, 255, 255);
    private static readonly Color Fill = Color.FromArgb(230, 255, 255, 255);

    private readonly Compositor _compositor;
    private readonly TextRenderer _text;
    private readonly CompositionEasingFunction _ease;
    private readonly ContainerVisual _root;
    private readonly ContainerVisual _island;
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionColorBrush _fill;
    private readonly CompositionColorBrush _edge;
    private readonly ContainerVisual _compactContent;
    private readonly ContainerVisual _expandedContent;
    private readonly TextLine _compactDate;
    private readonly TextLine _compactTime;
    private readonly TextLine _bigTime;
    private readonly TextLine _longDate;
    private readonly TextLine _wordmark;
    private readonly TextLine _progressLabel;
    private readonly SpriteVisual _progressTrack;
    private readonly SpriteVisual _progressFill;
    private readonly List<TextFormat> _formats = [];
    private IslandLayout _layout;
    private ClockReading? _clock;

    public IslandVisuals(Compositor compositor, CompositionTarget target, TextRenderer text, IslandLayout layout)
    {
        ArgumentNullException.ThrowIfNull(target);
        _compositor = compositor;
        _text = text;
        _layout = layout;
        _ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.33f, 1f), new Vector2(0.68f, 1f));

        _root = compositor.CreateContainerVisual();
        _island = compositor.CreateContainerVisual();
        var shapeVisual = compositor.CreateShapeVisual();
        _shape = compositor.CreateRoundedRectangleGeometry();
        var sprite = compositor.CreateSpriteShape(_shape);
        _fill = compositor.CreateColorBrush(Black);
        _edge = compositor.CreateColorBrush(Edge);
        sprite.FillBrush = _fill;
        sprite.StrokeBrush = _edge;
        shapeVisual.Shapes.Add(sprite);

        // Content is clipped to the animated shape so it never shows outside the island mid-transition.
        var content = compositor.CreateContainerVisual();
        content.Clip = compositor.CreateGeometricClip(_shape);
        _compactContent = compositor.CreateContainerVisual();
        _expandedContent = compositor.CreateContainerVisual();
        _expandedContent.Opacity = 0;
        content.Children.InsertAtTop(_compactContent);
        content.Children.InsertAtTop(_expandedContent);

        _compactDate = new TextLine(this, _compactContent);
        _compactTime = new TextLine(this, _compactContent);
        _bigTime = new TextLine(this, _expandedContent);
        _longDate = new TextLine(this, _expandedContent);
        _wordmark = new TextLine(this, _expandedContent);
        _progressLabel = new TextLine(this, _expandedContent);
        _progressTrack = compositor.CreateSpriteVisual();
        _progressTrack.Brush = compositor.CreateColorBrush(Track);
        _progressFill = compositor.CreateSpriteVisual();
        _progressFill.Brush = compositor.CreateColorBrush(Fill);
        _expandedContent.Children.InsertAtTop(_progressTrack);
        _expandedContent.Children.InsertAtTop(_progressFill);

        _island.Children.InsertAtTop(shapeVisual);
        _island.Children.InsertAtTop(content);
        _root.Children.InsertAtTop(_island);
        target.Root = _root;
        ApplyLayout(layout, IslandState.Initial);
    }

    /// <summary>Static placement for a layout (first show and after a DPI or work-area change); no animation.</summary>
    public void ApplyLayout(IslandLayout layout, IslandState state)
    {
        _layout = layout;
        var canvas = new Vector2(layout.Canvas.Width, layout.Canvas.Height);
        _root.Size = canvas;
        _island.Size = canvas;
        foreach (var child in _island.Children)
        {
            child.Size = canvas;
        }

        var shape = layout.ShapeFor(state.Presentation);
        _shape.Size = new Vector2((float)shape.Width, (float)shape.Height);
        _shape.Offset = new Vector2((float)shape.Left, (float)shape.Top);
        _shape.CornerRadius = new Vector2((float)shape.Radius);
        _island.CenterPoint = new Vector3((float)(layout.Canvas.Width / 2.0), (float)layout.Compact.Top, 0);
        _island.Scale = Vector3.One;
        _fill.Color = state.Hovered ? HoverFill : Black;
        _edge.Color = state.Hovered ? HoverEdge : Edge;
        _compactContent.Opacity = state.IsExpanded ? 0 : 1;
        _expandedContent.Opacity = state.IsExpanded ? 1 : 0;
        _expandedContent.Offset = Vector3.Zero;

        foreach (var format in _formats)
        {
            format.Dispose();
        }

        _formats.Clear();
        var c = layout.Compact;
        var e = layout.Expanded;
        _compactDate.Place(c.Left + layout.Dip(17), c.Top, layout.Dip(96), c.Height, Format(TextFont, 13, Semibold, TextAlign.Leading), Secondary);
        _compactTime.Place(c.Right - layout.Dip(17 + 120), c.Top, layout.Dip(120), c.Height, Format(TextFont, 14, Semibold, TextAlign.Trailing), Primary, smallSize: Px(11));
        _bigTime.Place(e.Left + layout.Dip(24), e.Top + layout.Dip(16), layout.Dip(260), layout.Dip(62), Format(DisplayFont, 46, Semibold, TextAlign.Leading), Primary, smallSize: Px(18));
        _wordmark.Place(e.Right - layout.Dip(24 + 100), e.Top + layout.Dip(26), layout.Dip(100), layout.Dip(20), Format(TextFont, 12, Semibold, TextAlign.Trailing), Tertiary);
        _longDate.Place(e.Left + layout.Dip(26), e.Top + layout.Dip(78), layout.Dip(368), layout.Dip(24), Format(TextFont, 15, Regular, TextAlign.Leading), Secondary);
        _progressLabel.Place(e.Left + layout.Dip(26), e.Top + layout.Dip(132), layout.Dip(368), layout.Dip(20), Format(TextFont, 12.5, Regular, TextAlign.Leading), Tertiary);
        _progressTrack.Offset = new Vector3(Snap(e.Left + layout.Dip(26)), Snap(e.Top + layout.Dip(116)), 0);
        _progressTrack.Size = new Vector2(Snap(layout.Dip(368)), Snap(layout.Dip(6)));
        _progressFill.Offset = _progressTrack.Offset;
        RoundEnds(_progressTrack);
        RedrawAll();
    }

    /// <summary>Shows a new clock reading; only lines whose text changed are redrawn.</summary>
    public void ShowClock(ClockReading reading)
    {
        _clock = reading;
        DrawText();
    }

    /// <summary>Starts the animations for a state change. Returns the batch of a presentation change, else null.</summary>
    public CompositionScopedBatch? Animate(IslandTransition transition, bool reducedMotion)
    {
        if (transition.HoverChanged)
        {
            var hovered = transition.Next.Hovered;
            _fill.StartAnimation("Color", ColorTo(hovered ? HoverFill : Black, 140));
            _edge.StartAnimation("Color", ColorTo(hovered ? HoverEdge : Edge, 140));
        }

        if (transition.PressChanged)
        {
            var scale = transition.Next.Pressed && !reducedMotion ? 0.965f : 1f;
            _island.StartAnimation("Scale", Vector3To(new Vector3(scale, scale, 1), transition.Next.Pressed ? 90 : 180));
        }

        if (!transition.PresentationChanged)
        {
            return null;
        }

        var expand = transition.Next.IsExpanded;
        var target = _layout.ShapeFor(transition.Next.Presentation);
        var shapeMs = reducedMotion ? 1 : (int)IslandMotion.Transition.TotalMilliseconds;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _shape.StartAnimation("Size", Vector2To(new Vector2((float)target.Width, (float)target.Height), shapeMs));
        _shape.StartAnimation("Offset", Vector2To(new Vector2((float)target.Left, (float)target.Top), shapeMs));
        _shape.StartAnimation("CornerRadius", Vector2To(new Vector2((float)target.Radius), shapeMs));

        // Outgoing content leaves fast; incoming content waits until the shape has mostly grown or shrunk.
        var (outgoing, incoming) = expand ? (_compactContent, _expandedContent) : (_expandedContent, _compactContent);
        outgoing.StartAnimation("Opacity", ScalarTo(0, reducedMotion ? 150 : 110, 0));
        incoming.StartAnimation("Opacity", ScalarTo(1, reducedMotion ? 150 : 200, reducedMotion ? 0 : (expand ? 150 : 170)));
        if (expand && !reducedMotion)
        {
            _expandedContent.Offset = new Vector3(0, -Snap(_layout.Dip(8)), 0);
            _expandedContent.StartAnimation("Offset", Vector3To(Vector3.Zero, 320, delayMs: 60));
        }

        batch.End();
        return batch;
    }

    /// <summary>Redraws every text surface (after a DPI change or a GPU device loss).</summary>
    public void RedrawAll()
    {
        foreach (var line in Lines())
        {
            line.Invalidate();
        }

        DrawText();
    }

    public void Dispose()
    {
        foreach (var line in Lines())
        {
            line.Dispose();
        }

        foreach (var format in _formats)
        {
            format.Dispose();
        }

        _formats.Clear();
        _root.Dispose();
    }

    private static float Snap(double px) => (float)Math.Round(px);

    /// <summary>Clips a bar to a capsule (radius = half its height).</summary>
    private void RoundEnds(SpriteVisual bar)
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        geometry.Size = bar.Size;
        geometry.CornerRadius = new Vector2(bar.Size.Y / 2);
        bar.Clip = _compositor.CreateGeometricClip(geometry);
    }

    private IEnumerable<TextLine> Lines() => [_compactDate, _compactTime, _bigTime, _longDate, _wordmark, _progressLabel];

    private float Px(double dip) => (float)_layout.Dip(dip);

    private TextFormat Format(string family, double sizeDip, int weight, TextAlign align)
    {
        var format = _text.CreateFormat(family, Px(sizeDip), weight, align);
        _formats.Add(format);
        return format;
    }

    private void DrawText()
    {
        var ok = true;
        if (_clock is { } clock)
        {
            ok &= _compactDate.Show(clock.ShortDate);
            ok &= _compactTime.Show(clock.TimeWithDesignator, clock.Designator.Length);
            ok &= _bigTime.Show(clock.TimeWithDesignator, clock.Designator.Length);
            ok &= _longDate.Show(clock.LongDate);
            ok &= _progressLabel.Show(clock.DayProgressText);
            _progressFill.Size = new Vector2(Math.Max(_progressTrack.Size.Y, Snap(_progressTrack.Size.X * clock.DayFraction)), _progressTrack.Size.Y);
            RoundEnds(_progressFill);
        }

        ok &= _wordmark.Show("Halo");
        if (!ok)
        {
            // The GPU device was lost (driver update, adapter switch): replace it and draw everything again once.
            _text.RecoverDevice();
            foreach (var line in Lines())
            {
                line.Invalidate();
            }

            if (_clock is { } again)
            {
                _ = _compactDate.Show(again.ShortDate) & _compactTime.Show(again.TimeWithDesignator, again.Designator.Length)
                    & _bigTime.Show(again.TimeWithDesignator, again.Designator.Length) & _longDate.Show(again.LongDate) & _progressLabel.Show(again.DayProgressText);
            }

            _ = _wordmark.Show("Halo");
        }
    }

    private ColorKeyFrameAnimation ColorTo(Color to, int ms)
    {
        var a = _compositor.CreateColorKeyFrameAnimation();
        a.InsertKeyFrame(1f, to, _ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        return a;
    }

    private Vector2KeyFrameAnimation Vector2To(Vector2 to, int ms)
    {
        var a = _compositor.CreateVector2KeyFrameAnimation();
        a.InsertKeyFrame(1f, to, _ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        return a;
    }

    private Vector3KeyFrameAnimation Vector3To(Vector3 to, int ms, int delayMs = 0)
    {
        var a = _compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(1f, to, _ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        return a;
    }

    private ScalarKeyFrameAnimation ScalarTo(float to, int ms, int delayMs)
    {
        var a = _compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, to, _ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        a.DelayTime = TimeSpan.FromMilliseconds(delayMs);
        return a;
    }

    /// <summary>One line of text: a sprite visual showing a drawing surface that is redrawn only when its text changes.</summary>
    private sealed class TextLine : IDisposable
    {
        private readonly IslandVisuals _owner;
        private readonly SpriteVisual _visual;
        private readonly CompositionSurfaceBrush _brush;
        private CompositionDrawingSurface? _surface;
        private TextFormat? _format;
        private Color _color;
        private float _smallSize;
        private string? _shown;

        public TextLine(IslandVisuals owner, ContainerVisual parent)
        {
            _owner = owner;
            _visual = owner._compositor.CreateSpriteVisual();
            _brush = owner._compositor.CreateSurfaceBrush();
            _brush.Stretch = CompositionStretch.None;
            _brush.HorizontalAlignmentRatio = 0;
            _brush.VerticalAlignmentRatio = 0;
            _visual.Brush = _brush;
            parent.Children.InsertAtTop(_visual);
        }

        public void Place(double left, double top, double width, double height, TextFormat format, Color color, float smallSize = 0)
        {
            var w = (int)Math.Ceiling(width);
            var h = (int)Math.Ceiling(height);
            _visual.Offset = new Vector3(Snap(left), Snap(top), 0);
            _visual.Size = new Vector2(w, h);
            _surface?.Dispose();
            _surface = _owner._text.CreateSurface(w, h);
            _brush.Surface = _surface;
            _format = format;
            _color = color;
            _smallSize = smallSize;
            _shown = null;
        }

        public void Invalidate() => _shown = null;

        /// <summary>Draws the text if it changed. The last <paramref name="smallTail"/> characters use the small size.</summary>
        public bool Show(string text, int smallTail = 0)
        {
            if (_surface is null || _format is null || string.Equals(text, _shown, StringComparison.Ordinal))
            {
                return true;
            }

            var ok = _owner._text.TryDraw(_surface, text, _format, _color, smallTail > 0 && _smallSize > 0 ? text.Length - smallTail : -1, _smallSize);
            _shown = ok ? text : null;
            return ok;
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _brush.Dispose();
            _visual.Dispose();
        }
    }
}

/// <summary>Motion constants shared by the surface and its tests.</summary>
internal static class IslandMotion
{
    /// <summary>Shape transition length; the same easing and duration were measured in spike S1 (PB-05).</summary>
    public static readonly TimeSpan Transition = TimeSpan.FromMilliseconds(350);
}
