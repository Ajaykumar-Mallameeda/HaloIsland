using System.Numerics;
using Halo.App.Interop;
using Halo.Core.Clock;
using Halo.Core.Island;
using Halo.Core.Media;
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

    /// <summary>Windows 11 icon font: icons are glyphs, so no image assets are shipped.</summary>
    public const string IconFont = "Segoe Fluent Icons";
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
    private readonly ContainerVisual _alertContent;
    private readonly TextLine _alertGlyph;
    private readonly TextLine _alertWide;
    private readonly TextLine _alertNarrow;
    private readonly TextLine _alertValue;
    private readonly SpriteVisual _alertTrack;
    private readonly SpriteVisual _alertFill;
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
    private AlertContent? _alert;

    // Media (M3-F01): an expanded media card beside the clock group, and a compact glyph + title.
    private readonly ContainerVisual _clockGroup;
    private readonly ContainerVisual _mediaGroup;
    private readonly SpriteVisual _artBackground;
    private readonly SpriteVisual _art;
    private readonly CompositionSurfaceBrush _artBrush;
    private readonly TextLine _artPlaceholder;
    private readonly TextLine _mediaTitle;
    private readonly TextLine _mediaArtist;
    private readonly TextLine _mediaTime;
    private readonly TextLine _mediaClock;
    private readonly SpriteVisual _mediaTrack;
    private readonly SpriteVisual _mediaFill;
    private readonly TextLine _compactMediaGlyph;
    private readonly TextLine _compactMediaTitle;
    private readonly TextLine _buttonPrevious;
    private readonly TextLine _buttonPlayPause;
    private readonly TextLine _buttonNext;
    private CompositionDrawingSurface? _artSurface;
    private MediaSnapshot? _media;
    private int _artShown = -1;

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
        _alertContent = compositor.CreateContainerVisual();
        _alertContent.Opacity = 0;
        content.Children.InsertAtTop(_compactContent);
        content.Children.InsertAtTop(_expandedContent);
        content.Children.InsertAtTop(_alertContent);

        _compactDate = new TextLine(this, _compactContent);
        _compactTime = new TextLine(this, _compactContent);
        _clockGroup = compositor.CreateContainerVisual();
        _mediaGroup = compositor.CreateContainerVisual();
        _mediaGroup.IsVisible = false;
        _expandedContent.Children.InsertAtTop(_clockGroup);
        _expandedContent.Children.InsertAtTop(_mediaGroup);
        _bigTime = new TextLine(this, _clockGroup);
        _longDate = new TextLine(this, _clockGroup);
        _wordmark = new TextLine(this, _clockGroup);
        _progressLabel = new TextLine(this, _clockGroup);
        _progressTrack = compositor.CreateSpriteVisual();
        _progressTrack.Brush = compositor.CreateColorBrush(Track);
        _progressFill = compositor.CreateSpriteVisual();
        _progressFill.Brush = compositor.CreateColorBrush(Fill);
        _clockGroup.Children.InsertAtTop(_progressTrack);
        _clockGroup.Children.InsertAtTop(_progressFill);

        _artBackground = compositor.CreateSpriteVisual();
        _artBackground.Brush = compositor.CreateColorBrush(Track);
        _mediaGroup.Children.InsertAtTop(_artBackground);
        _artPlaceholder = new TextLine(this, _mediaGroup);
        _art = compositor.CreateSpriteVisual();
        _artBrush = compositor.CreateSurfaceBrush();
        _artBrush.Stretch = CompositionStretch.Fill;
        _art.Brush = _artBrush;
        _mediaGroup.Children.InsertAtTop(_art);
        _mediaTitle = new TextLine(this, _mediaGroup);
        _mediaArtist = new TextLine(this, _mediaGroup);
        _mediaTime = new TextLine(this, _mediaGroup);
        _mediaClock = new TextLine(this, _mediaGroup);
        _mediaTrack = compositor.CreateSpriteVisual();
        _mediaTrack.Brush = compositor.CreateColorBrush(Track);
        _mediaFill = compositor.CreateSpriteVisual();
        _mediaFill.Brush = compositor.CreateColorBrush(Fill);
        _mediaGroup.Children.InsertAtTop(_mediaTrack);
        _mediaGroup.Children.InsertAtTop(_mediaFill);
        _buttonPrevious = new TextLine(this, _mediaGroup);
        _buttonPlayPause = new TextLine(this, _mediaGroup);
        _buttonNext = new TextLine(this, _mediaGroup);
        _compactMediaGlyph = new TextLine(this, _compactContent);
        _compactMediaTitle = new TextLine(this, _compactContent);

        _alertGlyph = new TextLine(this, _alertContent);
        _alertWide = new TextLine(this, _alertContent);
        _alertNarrow = new TextLine(this, _alertContent);
        _alertValue = new TextLine(this, _alertContent);
        _alertTrack = compositor.CreateSpriteVisual();
        _alertTrack.Brush = compositor.CreateColorBrush(Track);
        _alertFill = compositor.CreateSpriteVisual();
        _alertFill.Brush = compositor.CreateColorBrush(Fill);
        _alertContent.Children.InsertAtTop(_alertTrack);
        _alertContent.Children.InsertAtTop(_alertFill);

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
        _compactContent.Opacity = state.Presentation == IslandPresentation.Compact ? 1 : 0;
        _expandedContent.Opacity = state.Presentation == IslandPresentation.Expanded ? 1 : 0;
        _alertContent.Opacity = state.Presentation == IslandPresentation.Alert ? 1 : 0;
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

        // Media card (expanded) and compact media summary.
        var artPx = Snap(layout.Dip(72));
        _artBackground.Offset = new Vector3(Snap(e.Left + layout.Dip(24)), Snap(e.Top + layout.Dip(22)), 0);
        _artBackground.Size = new Vector2(artPx, artPx);
        RoundCorners(_artBackground, (float)layout.Dip(10));
        _art.Offset = _artBackground.Offset;
        _art.Size = _artBackground.Size;
        RoundCorners(_art, (float)layout.Dip(10));
        _artSurface?.Dispose();
        _artSurface = _text.CreateSurface((int)artPx, (int)artPx);
        _artBrush.Surface = _artSurface;
        _artShown = -1;
        _artPlaceholder.Place(e.Left + layout.Dip(24), e.Top + layout.Dip(22), layout.Dip(72), layout.Dip(72), Format(IconFont, 26, Regular, TextAlign.Center), Tertiary);
        _mediaTitle.Place(e.Left + layout.Dip(112), e.Top + layout.Dip(24), layout.Dip(284), layout.Dip(26), Format(DisplayFont, 17, Semibold, TextAlign.Leading, trim: true), Primary);
        _mediaArtist.Place(e.Left + layout.Dip(112), e.Top + layout.Dip(52), layout.Dip(284), layout.Dip(20), Format(TextFont, 13, Regular, TextAlign.Leading, trim: true), Secondary);
        _mediaTrack.Offset = new Vector3(Snap(e.Left + layout.Dip(112)), Snap(e.Top + layout.Dip(86)), 0);
        _mediaTrack.Size = new Vector2(Snap(layout.Dip(284)), Snap(layout.Dip(4)));
        _mediaFill.Offset = _mediaTrack.Offset;
        RoundEnds(_mediaTrack);
        _mediaTime.Place(e.Left + layout.Dip(112), e.Top + layout.Dip(96), layout.Dip(170), layout.Dip(18), Format(TextFont, 11.5, Regular, TextAlign.Leading), Tertiary);
        _mediaClock.Place(e.Right - layout.Dip(24 + 110), e.Top + layout.Dip(96), layout.Dip(110), layout.Dip(18), Format(TextFont, 11.5, Semibold, TextAlign.Trailing), Tertiary, smallSize: Px(9.5f));
        var all = new MediaSnapshot { Title = string.Empty, Artist = string.Empty, CanPrevious = true, CanPlayPause = true, CanNext = true };
        foreach (var b in MediaControls.Layout(layout, all))
        {
            var line = b.Command switch { MediaCommand.Previous => _buttonPrevious, MediaCommand.Next => _buttonNext, _ => _buttonPlayPause };
            line.Place(b.CenterX - (b.Size / 2), b.CenterY - (b.Size / 2), b.Size, b.Size, Format(IconFont, b.Command == MediaCommand.PlayPause ? 20 : 15, Regular, TextAlign.Center), Primary);
        }

        _compactMediaGlyph.Place(c.Left + layout.Dip(14), c.Top, layout.Dip(18), c.Height, Format(IconFont, 11, Regular, TextAlign.Center), Secondary);
        _compactMediaTitle.Place(c.Left + layout.Dip(34), c.Top, layout.Dip(80), c.Height, Format(TextFont, 12.5, Semibold, TextAlign.Leading, trim: true), Secondary);

        var a = layout.Alert;
        _alertGlyph.Place(a.Left + layout.Dip(14), a.Top, layout.Dip(26), a.Height, Format(IconFont, 16, Regular, TextAlign.Center), Primary);
        _alertWide.Place(a.Left + layout.Dip(46), a.Top, layout.Dip(236), a.Height, Format(TextFont, 14, Semibold, TextAlign.Leading), Primary);
        _alertNarrow.Place(a.Left + layout.Dip(46), a.Top, layout.Dip(118), a.Height, Format(TextFont, 14, Semibold, TextAlign.Leading), Primary);
        _alertValue.Place(a.Right - layout.Dip(16 + 34), a.Top, layout.Dip(34), a.Height, Format(TextFont, 12.5, Semibold, TextAlign.Trailing), Secondary);
        _alertTrack.Offset = new Vector3(Snap(a.Left + layout.Dip(168)), Snap(a.Top + ((a.Height - layout.Dip(4)) / 2)), 0);
        _alertTrack.Size = new Vector2(Snap(layout.Dip(78)), Snap(layout.Dip(4)));
        _alertFill.Offset = _alertTrack.Offset;
        RoundEnds(_alertTrack);
        RedrawAll();
    }

    /// <summary>
    /// Shows the selected media session (null = none: the clock view and the date come back). Only changed lines are
    /// redrawn; the art surface only when the picture changes.
    /// </summary>
    public void ShowMedia(MediaSnapshot? media, DateTimeOffset now)
    {
        _media = media;
        _clockGroup.IsVisible = media is null;
        _mediaGroup.IsVisible = media is not null;
        DrawText();
        UpdateMediaProgress(now);
    }

    /// <summary>Moves the media progress bar and label (called at most once a second, only while it is visible).</summary>
    public void UpdateMediaProgress(DateTimeOffset now)
    {
        var timeline = _media?.Timeline;
        _mediaTrack.IsVisible = timeline is not null;
        _mediaFill.IsVisible = timeline is not null;
        if (timeline is null || _media is null)
        {
            _ = _mediaTime.Show(string.Empty);
            return;
        }

        var fraction = Math.Clamp(timeline.FractionAt(now, _media.IsPlaying), 0, 1);
        _mediaFill.Size = new Vector2(Math.Max(_mediaTrack.Size.Y, Snap(_mediaTrack.Size.X * fraction)), _mediaTrack.Size.Y);
        RoundEnds(_mediaFill);
        _ = _mediaTime.Show(timeline.LabelAt(now, _media.IsPlaying));
    }

    /// <summary>Shows the content of a transient alert (null clears it). Only changed lines are redrawn.</summary>
    public void ShowAlert(AlertContent? content)
    {
        _alert = content;
        DrawAlert();
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

        var expand = transition.Next.Presentation == IslandPresentation.Expanded;
        var target = _layout.ShapeFor(transition.Next.Presentation);
        var shapeMs = reducedMotion ? 1 : (int)IslandMotion.Transition.TotalMilliseconds;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _shape.StartAnimation("Size", Vector2To(new Vector2((float)target.Width, (float)target.Height), shapeMs));
        _shape.StartAnimation("Offset", Vector2To(new Vector2((float)target.Left, (float)target.Top), shapeMs));
        _shape.StartAnimation("CornerRadius", Vector2To(new Vector2((float)target.Radius), shapeMs));

        // Outgoing content leaves fast; incoming content waits until the shape has mostly grown or shrunk.
        var (outgoing, incoming) = (ContentFor(transition.Previous.Presentation), ContentFor(transition.Next.Presentation));
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
        DrawAlert();
    }

    public void Dispose()
    {
        foreach (var line in Lines())
        {
            line.Dispose();
        }

        _artSurface?.Dispose();
        _artSurface = null;

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

    private IEnumerable<TextLine> Lines() => [_compactDate, _compactTime, _bigTime, _longDate, _wordmark, _progressLabel, _alertGlyph, _alertWide, _alertNarrow, _alertValue,
        _artPlaceholder, _mediaTitle, _mediaArtist, _mediaTime, _mediaClock, _compactMediaGlyph, _compactMediaTitle, _buttonPrevious, _buttonPlayPause, _buttonNext];

    private void RoundCorners(SpriteVisual visual, float radius)
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        geometry.Size = visual.Size;
        geometry.CornerRadius = new Vector2(radius);
        visual.Clip = _compositor.CreateGeometricClip(geometry);
    }

    /// <summary>Media text and art; true unless the GPU device was lost.</summary>
    private bool DrawMedia()
    {
        var m = _media;
        var ok = true;
        ok &= _compactMediaGlyph.Show(m is null ? string.Empty : m.IsPlaying ? "\uE768" : "\uE769");
        ok &= _compactMediaTitle.Show(m is null ? string.Empty : m.Title.Length > 0 ? m.Title : m.Headline);
        ok &= _mediaTitle.Show(m is null ? string.Empty : m.Title.Length > 0 ? m.Title : "Unknown title");
        ok &= _mediaArtist.Show(m?.Artist ?? string.Empty);
        ok &= _mediaClock.Show(m is null || _clock is null ? string.Empty : _clock.TimeWithDesignator, _clock?.Designator.Length ?? 0);
        // FR-MED-02: only controls the session reports as enabled are shown.
        var playing = m?.IsPlaying == true;
        ok &= _buttonPrevious.Show(m?.CanPrevious == true ? MediaControls.Glyph(MediaCommand.Previous, playing) : string.Empty);
        ok &= _buttonPlayPause.Show(m?.CanPlayPause == true ? MediaControls.Glyph(MediaCommand.PlayPause, playing) : string.Empty);
        ok &= _buttonNext.Show(m?.CanNext == true ? MediaControls.Glyph(MediaCommand.Next, playing) : string.Empty);
        var hasArt = m?.Art is { Count: > 0 };
        _art.IsVisible = hasArt;
        ok &= _artPlaceholder.Show(m is not null && !hasArt ? "\uE8D6" : string.Empty);
        if (hasArt && _artSurface is not null && m!.ArtVersion != _artShown)
        {
            var drawn = _text.TryDrawImage(_artSurface, [.. m.Art!], m.ArtSize);
            _artShown = drawn ? m.ArtVersion : -1;
            ok &= drawn;
        }

        return ok;
    }

    private ContainerVisual ContentFor(IslandPresentation presentation) => presentation switch
    {
        IslandPresentation.Expanded => _expandedContent,
        IslandPresentation.Alert => _alertContent,
        _ => _compactContent,
    };

    private void DrawAlert()
    {
        var content = _alert;
        var level = content?.ClampedLevel;
        _ = _alertGlyph.Show(content?.Glyph ?? string.Empty);
        _ = _alertWide.Show(level is null ? content?.Text ?? string.Empty : string.Empty);
        _ = _alertNarrow.Show(level is null ? string.Empty : content?.Text ?? string.Empty);
        _ = _alertValue.Show(level is null ? string.Empty : content?.Value ?? string.Empty);
        _alertTrack.IsVisible = level is not null;
        _alertFill.IsVisible = level is not null;
        if (level is { } l)
        {
            _alertFill.Size = new Vector2(Math.Max(_alertTrack.Size.Y, Snap(_alertTrack.Size.X * l)), _alertTrack.Size.Y);
            RoundEnds(_alertFill);
        }
    }

    private float Px(double dip) => (float)_layout.Dip(dip);

    private TextFormat Format(string family, double sizeDip, int weight, TextAlign align, bool trim = false)
    {
        var format = _text.CreateFormat(family, Px(sizeDip), weight, align, trim);
        _formats.Add(format);
        return format;
    }

    private void DrawText()
    {
        var ok = true;
        if (_clock is { } clock)
        {
            ok &= _compactDate.Show(_media is null ? clock.ShortDate : string.Empty);
            ok &= _compactTime.Show(clock.TimeWithDesignator, clock.Designator.Length);
            ok &= _bigTime.Show(clock.TimeWithDesignator, clock.Designator.Length);
            ok &= _longDate.Show(clock.LongDate);
            ok &= _progressLabel.Show(clock.DayProgressText);
            _progressFill.Size = new Vector2(Math.Max(_progressTrack.Size.Y, Snap(_progressTrack.Size.X * clock.DayFraction)), _progressTrack.Size.Y);
            RoundEnds(_progressFill);
        }

        ok &= _wordmark.Show("Halo");
        ok &= DrawMedia();
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
                _ = _compactDate.Show(_media is null ? again.ShortDate : string.Empty) & _compactTime.Show(again.TimeWithDesignator, again.Designator.Length)
                    & _bigTime.Show(again.TimeWithDesignator, again.Designator.Length) & _longDate.Show(again.LongDate) & _progressLabel.Show(again.DayProgressText);
            }

            _ = _wordmark.Show("Halo");
            _artShown = -1;
            _ = DrawMedia();
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
