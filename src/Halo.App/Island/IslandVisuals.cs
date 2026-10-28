using System.Numerics;
using Halo.App.Interop;
using Halo.Core.Activities;
using Halo.Core.Audio;
using Halo.Core.Clock;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Motion;
using Halo.Core.Theming;
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

    // Colours come from the theme palette (I-F01); the names are the roles they play.
    private readonly Dictionary<BrushRole, List<CompositionColorBrush>> _roleBrushes = [];
    private IslandPalette _palette = IslandPalette.Dark;
    private IslandState _lastState = IslandState.Initial;

    private enum BrushRole
    {
        Track,
        Bar,
        Primary,
    }

    private Color Black => ToColor(_palette.Fill);

    private Color HoverFill => ToColor(_palette.HoverFill);

    private Color Edge => ToColor(_palette.Edge);

    private Color HoverEdge => ToColor(_palette.HoverEdge);

    private Color Primary => ToColor(_palette.Primary);

    private Color Secondary => ToColor(_palette.Secondary);

    private Color Tertiary => ToColor(_palette.Tertiary);

    private Color Track => ToColor(_palette.Track);

    private Color Fill => ToColor(_palette.Bar);

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
    private readonly TextLine _buttonSwitch;
    private CompositionDrawingSurface? _artSurface;
    private MediaSnapshot? _media;
    private int _artShown = -1;

    // Volume row (M3-F03): mute button, slider and value at the bottom of the expanded island.
    private readonly ContainerVisual _volumeGroup;
    private readonly TextLine _volumeGlyph;
    private readonly TextLine _volumeValue;
    private readonly TextLine _volumeNone;
    private readonly SpriteVisual _volumeTrack;
    private readonly SpriteVisual _volumeFill;
    private readonly CompositionColorBrush _volumeFillBrush;
    private readonly SpriteVisual _volumeThumb;
    private VolumeReading? _volume;

    // Activities (B-F03/C-F02): compact slot, expanded card, page dots.
    private readonly ContainerVisual _compactActivity;
    private readonly TextLine _compactActivityGlyph;
    private readonly TextLine _compactActivityText;
    private readonly SpriteVisual _compactLevelTrack;
    private readonly SpriteVisual _compactLevelFill;
    private readonly ContainerVisual _activityGroup;
    private readonly TextLine _activityGlyph;
    private readonly TextLine _activityTitle;
    private readonly TextLine _activitySubtitle;
    private readonly SpriteVisual _activityTrack;
    private readonly SpriteVisual _activityFill;
    private readonly TextLine[] _activityButtons;
    private readonly SpriteVisual[] _dots;
    private readonly CompositionColorBrush[] _dotBrushes;
    private IslandActivity? _activity;
    private PillContent _pill = PillContent.Default;
    private string _pillBattery = string.Empty;
    private int _activityOthers;
    private bool _showPrivate;
    private IslandPage _page = IslandPage.Clock;
    private IReadOnlyList<IslandPage> _pages = [IslandPage.Clock];

    // Status cluster (M3-F04/F05): top right of the open clock view, in place of the wordmark.
    private readonly TextLine _statusGlyphs;
    private readonly TextLine _statusText;
    private string _statusGlyphText = string.Empty;
    private string _statusValue = string.Empty;

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
        _statusGlyphs = new TextLine(this, _clockGroup);
        _statusText = new TextLine(this, _clockGroup);
        _progressTrack = compositor.CreateSpriteVisual();
        _progressTrack.Brush = RoleBrush(compositor, BrushRole.Track);
        _progressFill = compositor.CreateSpriteVisual();
        _progressFill.Brush = RoleBrush(compositor, BrushRole.Bar);
        _clockGroup.Children.InsertAtTop(_progressTrack);
        _clockGroup.Children.InsertAtTop(_progressFill);

        _artBackground = compositor.CreateSpriteVisual();
        _artBackground.Brush = RoleBrush(compositor, BrushRole.Track);
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
        _mediaTrack.Brush = RoleBrush(compositor, BrushRole.Track);
        _mediaFill = compositor.CreateSpriteVisual();
        _mediaFill.Brush = RoleBrush(compositor, BrushRole.Bar);
        _mediaGroup.Children.InsertAtTop(_mediaTrack);
        _mediaGroup.Children.InsertAtTop(_mediaFill);
        _buttonPrevious = new TextLine(this, _mediaGroup);
        _buttonPlayPause = new TextLine(this, _mediaGroup);
        _buttonNext = new TextLine(this, _mediaGroup);
        _buttonSwitch = new TextLine(this, _mediaGroup);
        _compactMediaGlyph = new TextLine(this, _compactContent);
        _compactMediaTitle = new TextLine(this, _compactContent);

        _compactActivity = compositor.CreateContainerVisual();
        _compactContent.Children.InsertAtTop(_compactActivity);
        _compactActivityGlyph = new TextLine(this, _compactActivity);
        _compactActivityText = new TextLine(this, _compactActivity);
        _compactLevelTrack = compositor.CreateSpriteVisual();
        _compactLevelTrack.Brush = RoleBrush(compositor, BrushRole.Track);
        _compactLevelFill = compositor.CreateSpriteVisual();
        _compactLevelFill.Brush = RoleBrush(compositor, BrushRole.Bar);
        _compactActivity.Children.InsertAtTop(_compactLevelTrack);
        _compactActivity.Children.InsertAtTop(_compactLevelFill);

        _activityGroup = compositor.CreateContainerVisual();
        _activityGroup.IsVisible = false;
        _expandedContent.Children.InsertAtTop(_activityGroup);
        _activityGlyph = new TextLine(this, _activityGroup);
        _activityTitle = new TextLine(this, _activityGroup);
        _activitySubtitle = new TextLine(this, _activityGroup);
        _activityTrack = compositor.CreateSpriteVisual();
        _activityTrack.Brush = RoleBrush(compositor, BrushRole.Track);
        _activityFill = compositor.CreateSpriteVisual();
        _activityFill.Brush = RoleBrush(compositor, BrushRole.Bar);
        _activityGroup.Children.InsertAtTop(_activityTrack);
        _activityGroup.Children.InsertAtTop(_activityFill);
        _activityButtons = [new TextLine(this, _activityGroup), new TextLine(this, _activityGroup), new TextLine(this, _activityGroup)];
        _dots = new SpriteVisual[3];
        _dotBrushes = new CompositionColorBrush[3];
        for (var i = 0; i < _dots.Length; i++)
        {
            _dotBrushes[i] = compositor.CreateColorBrush(Tertiary);
            _dots[i] = compositor.CreateSpriteVisual();
            _dots[i].Brush = _dotBrushes[i];
            _dots[i].IsVisible = false;
            _expandedContent.Children.InsertAtTop(_dots[i]);
        }

        _volumeGroup = compositor.CreateContainerVisual();
        _expandedContent.Children.InsertAtTop(_volumeGroup);
        _volumeGlyph = new TextLine(this, _volumeGroup);
        _volumeValue = new TextLine(this, _volumeGroup);
        _volumeNone = new TextLine(this, _volumeGroup);
        _volumeTrack = compositor.CreateSpriteVisual();
        _volumeTrack.Brush = RoleBrush(compositor, BrushRole.Track);
        _volumeFillBrush = RoleBrush(compositor, BrushRole.Bar);
        _volumeFill = compositor.CreateSpriteVisual();
        _volumeFill.Brush = _volumeFillBrush;
        _volumeThumb = compositor.CreateSpriteVisual();
        _volumeThumb.Brush = RoleBrush(compositor, BrushRole.Primary);
        _volumeGroup.Children.InsertAtTop(_volumeTrack);
        _volumeGroup.Children.InsertAtTop(_volumeFill);
        _volumeGroup.Children.InsertAtTop(_volumeThumb);

        _alertGlyph = new TextLine(this, _alertContent);
        _alertWide = new TextLine(this, _alertContent);
        _alertNarrow = new TextLine(this, _alertContent);
        _alertValue = new TextLine(this, _alertContent);
        _alertTrack = compositor.CreateSpriteVisual();
        _alertTrack.Brush = RoleBrush(compositor, BrushRole.Track);
        _alertFill = compositor.CreateSpriteVisual();
        _alertFill.Brush = RoleBrush(compositor, BrushRole.Bar);
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
        _lastState = state;
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
        _statusText.Place(e.Right - layout.Dip(24 + 38), e.Top + layout.Dip(24), layout.Dip(38), layout.Dip(24), Format(TextFont, 12.5, Semibold, TextAlign.Trailing), Secondary);
        _statusGlyphs.Place(e.Right - layout.Dip(24 + 38 + 3 + 64), e.Top + layout.Dip(24), layout.Dip(64), layout.Dip(24), Format(IconFont, 15, Regular, TextAlign.Trailing), Secondary);
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
        var all = new MediaSnapshot { Title = string.Empty, Artist = string.Empty, CanPrevious = true, CanPlayPause = true, CanNext = true, SessionCount = 2 };
        foreach (var b in MediaControls.Layout(layout, all))
        {
            var line = b.Command switch { MediaCommand.Previous => _buttonPrevious, MediaCommand.Next => _buttonNext, MediaCommand.SwitchSource => _buttonSwitch, _ => _buttonPlayPause };
            line.Place(b.CenterX - (b.Size / 2), b.CenterY - (b.Size / 2), b.Size, b.Size, Format(IconFont, b.Command == MediaCommand.PlayPause ? 20 : 15, Regular, TextAlign.Center), Primary);
        }

        _compactMediaGlyph.Place(c.Left + layout.Dip(14), c.Top, layout.Dip(18), c.Height, Format(IconFont, 11, Regular, TextAlign.Center), Secondary);
        _compactMediaTitle.Place(c.Left + layout.Dip(34), c.Top, layout.Dip(80), c.Height, Format(TextFont, 12.5, Semibold, TextAlign.Leading, trim: true), Secondary);
        PlaceActivity(layout);

        PlaceVolume(layout);

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
        DrawText();
        UpdateMediaProgress(now);
    }

    /// <summary>
    /// Shows the primary activity (null = none) in the compact slot and on the activity page. <paramref name="others"/>
    /// further activities show as "+N". Private text is redacted unless <paramref name="showPrivate"/>.
    /// </summary>
    public void ShowActivity(IslandActivity? activity, int others, bool showPrivate, bool reducedMotion)
    {
        var levelBefore = _activity?.Level;
        (_activity, _activityOthers, _showPrivate) = (activity, others, showPrivate);
        DrawText();
        DrawActivityLevel(MotionPlanner.Level(levelBefore, activity?.Level, reducedMotion));
    }

    /// <summary>
    /// Shows one page of the expanded island and its page dots (FR-ISL-08). A change while the page is on screen
    /// swaps the content (MOTION_SYSTEM.md); <paramref name="animate"/> is false when the island is not expanded.
    /// </summary>
    public void ShowPage(IslandPage page, IReadOnlyList<IslandPage> pages, bool animate, bool reducedMotion)
    {
        var changed = page != _page;
        (_page, _pages) = (page, pages);
        _activityGroup.IsVisible = page == IslandPage.Activity;
        _mediaGroup.IsVisible = page == IslandPage.Media;
        _clockGroup.IsVisible = page == IslandPage.Clock;
        var dots = ActivityLayout.Dots(_layout, pages.Count);
        for (var i = 0; i < _dots.Length; i++)
        {
            _dots[i].IsVisible = i < dots.Count;
            if (i < dots.Count)
            {
                var size = Snap(_layout.Dip(6));
                _dots[i].Size = new Vector2(size, size);
                _dots[i].Offset = new Vector3(Snap(dots[i].CenterX - (size / 2)), Snap(dots[i].CenterY - (size / 2)), 0);
                RoundCorners(_dots[i], size / 2);
                _dotBrushes[i].Color = pages[i] == page ? Primary : Tertiary;
            }
        }

        if (changed && animate && MotionPlanner.Swap(reducedMotion) is { Count: > 0 } swap)
        {
            var spec = swap[0].Spec;
            var a = _compositor.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0f, 0.35f);
            a.InsertKeyFrame(1f, 1f, _ease);
            a.Duration = spec.Duration;
            _expandedContent.StartAnimation("Opacity", a);
        }
    }

    /// <summary>
    /// What the compact pill shows (I-F02): its left slot takes the first enabled of activity, media and date that
    /// exists; <paramref name="battery"/> ("82%") goes beside the time when the user asked for it.
    /// </summary>
    public void ShowPillOptions(PillContent pill, string battery)
    {
        if (pill == _pill && string.Equals(battery, _pillBattery, StringComparison.Ordinal))
        {
            return;
        }

        (_pill, _pillBattery) = (pill, battery);
        DrawText();
    }

    /// <summary>Emphasis (MOTION_SYSTEM.md): one gentle brightness step on the activity, never a flash.</summary>
    public void Emphasise(IReadOnlyList<MotionStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var a = _compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0.5f, 0.7f, _ease);
        a.InsertKeyFrame(1f, 1f, _ease);
        a.Duration = steps[0].Spec.Duration;
        _compactActivity.StartAnimation("Opacity", a);
        _activityTitle.Visual.StartAnimation("Opacity", a);
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

    /// <summary>Shows the endpoint volume in the expanded row (null = no output device).</summary>
    public void ShowVolume(VolumeReading? reading)
    {
        _volume = reading;
        _ = DrawVolume();
    }

    /// <summary>
    /// Shows status icons (icon-font glyphs, for example network and battery) and a short value ("82%") at the top right
    /// of the open clock view. Both empty brings back the "Halo" wordmark.
    /// </summary>
    public void ShowStatus(string glyphs, string text)
    {
        (_statusGlyphText, _statusValue) = (glyphs, text);
        _ = DrawStatus();
    }

    /// <summary>Shows the content of a transient alert (null clears it). Only changed lines are redrawn.</summary>
    public void ShowAlert(AlertContent? content, bool reducedMotion = false)
    {
        var before = _alert?.ClampedLevel;
        _alert = content;
        DrawAlert(MotionPlanner.Level(before, content?.ClampedLevel, reducedMotion));
    }

    /// <summary>Shows a new clock reading; only lines whose text changed are redrawn.</summary>
    public void ShowClock(ClockReading reading)
    {
        _clock = reading;
        DrawText();
    }

    /// <summary>
    /// Starts the animations for a state change. <paramref name="plan"/> comes from <see cref="MotionPlanner"/> for a
    /// presentation change (null otherwise). Returns the batch of a presentation change, else null.
    /// </summary>
    public CompositionScopedBatch? Animate(IslandTransition transition, MotionPlan? plan, bool reducedMotion)
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

        if (!transition.PresentationChanged || plan is null)
        {
            return null;
        }

        var target = _layout.ShapeFor(transition.Next.Presentation);
        var morph = plan.Spec(MotionKind.Morph);
        var shapeMs = morph.IsInstant ? 1 : Ms(morph.Duration);
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _shape.StartAnimation("Size", Vector2To(new Vector2((float)target.Width, (float)target.Height), shapeMs));
        _shape.StartAnimation("Offset", Vector2To(new Vector2((float)target.Left, (float)target.Top), shapeMs));
        _shape.StartAnimation("CornerRadius", Vector2To(new Vector2((float)target.Radius), shapeMs));

        // Outgoing content leaves fast; incoming content waits until the shape has mostly grown or shrunk.
        var (outgoing, incoming) = (ContentFor(transition.Previous.Presentation), ContentFor(transition.Next.Presentation));
        var fadeOut = plan.Spec(MotionKind.FadeOut);
        var fadeIn = plan.Spec(MotionKind.FadeIn);
        outgoing.StartAnimation("Opacity", ScalarTo(0, Ms(fadeOut.Duration), Ms(fadeOut.Delay)));
        incoming.StartAnimation("Opacity", ScalarTo(1, Ms(fadeIn.Duration), Ms(fadeIn.Delay)));
        if (plan.Has(MotionKind.Settle))
        {
            var settle = plan.Spec(MotionKind.Settle);
            _expandedContent.Offset = new Vector3(0, -Snap(_layout.Dip(8)), 0);
            _expandedContent.StartAnimation("Offset", Vector3To(Vector3.Zero, Ms(settle.Duration), delayMs: Ms(settle.Delay)));
        }

        batch.End();
        return batch;
    }

    /// <summary>
    /// Swap (MOTION_SYSTEM.md): the alert's content dips and rises when one alert replaces another in place, so the
    /// change is visible without a layout jump. The new text is already drawn; the dip never reaches zero (no flash).
    /// </summary>
    public void SwapAlertContent(IReadOnlyList<MotionStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var spec = steps[0].Spec;
        var total = (float)spec.End.TotalMilliseconds;
        var a = _compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, 1f);
        a.InsertKeyFrame((float)(spec.Delay.TotalMilliseconds / total), 0.35f, _ease);
        a.InsertKeyFrame(1f, 1f, _ease);
        a.Duration = spec.End;
        _alertContent.StartAnimation("Opacity", a);
    }

    /// <summary>Arrival (MOTION_SYSTEM.md): the island grows 4 % from its top centre and returns, once.</summary>
    public void Arrive(IReadOnlyList<MotionStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var a = _compositor.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0.4f, new Vector3(1.04f, 1.04f, 1), _ease);
        a.InsertKeyFrame(1f, Vector3.One, _ease);
        a.Duration = steps[0].Spec.Duration;
        _island.StartAnimation("Scale", a);
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

    /// <summary>
    /// Switches the theme (I-F01): every brush and text colour follows the new palette, and every text line is
    /// redrawn once. Nothing animates.
    /// </summary>
    public void ApplyPalette(IslandPalette palette, IslandState state)
    {
        if (palette == _palette)
        {
            return;
        }

        _palette = palette;
        foreach (var (role, brushes) in _roleBrushes)
        {
            var color = role switch { BrushRole.Track => Track, BrushRole.Primary => Primary, _ => Fill };
            foreach (var brush in brushes)
            {
                brush.Color = color;
            }
        }

        ApplyLayout(_layout, state);
        _ = DrawVolume(); // the muted fill colour depends on the palette
    }

    /// <summary>The palette in use (for tests and the settings window).</summary>
    public IslandPalette Palette => _palette;

    private static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private CompositionColorBrush RoleBrush(Compositor compositor, BrushRole role)
    {
        var brush = compositor.CreateColorBrush(role switch { BrushRole.Track => Track, BrushRole.Primary => Primary, _ => Fill });
        if (!_roleBrushes.TryGetValue(role, out var list))
        {
            _roleBrushes[role] = list = [];
        }

        list.Add(brush);
        return brush;
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

    /// <summary>Clips a bar to a capsule (radius = half its height), optionally over a larger area than its size.</summary>
    private void RoundEnds(SpriteVisual bar, Vector2? area = null)
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        geometry.Size = area ?? bar.Size;
        geometry.CornerRadius = new Vector2(bar.Size.Y / 2);
        bar.Clip = _compositor.CreateGeometricClip(geometry);
    }

    private IEnumerable<TextLine> Lines() => [_compactDate, _compactTime, _bigTime, _longDate, _wordmark, _progressLabel, _alertGlyph, _alertWide, _alertNarrow, _alertValue,
        _artPlaceholder, _mediaTitle, _mediaArtist, _mediaTime, _mediaClock, _compactMediaGlyph, _compactMediaTitle, _buttonPrevious, _buttonPlayPause, _buttonNext, _buttonSwitch,
        _volumeGlyph, _volumeValue, _volumeNone, _statusGlyphs, _statusText, _compactActivityGlyph, _compactActivityText, _activityGlyph, _activityTitle,
        _activitySubtitle, .. _activityButtons];

    private bool DrawStatus()
    {
        var any = _statusGlyphText.Length > 0 || _statusValue.Length > 0;
        var ok = _wordmark.Show(any ? string.Empty : "Halo");
        ok &= _statusGlyphs.Show(_statusGlyphText);
        ok &= _statusText.Show(_statusValue);
        return ok;
    }

    private void PlaceVolume(IslandLayout layout)
    {
        var row = VolumeRow.For(layout);
        _volumeGroup.IsVisible = row is not null;
        if (row is null)
        {
            return;
        }

        var size = row.MuteSize;
        _volumeGlyph.Place(row.MuteX - (size / 2), row.MuteY - (size / 2), size, size, Format(IconFont, 15, Regular, TextAlign.Center), Primary);
        var trackHeight = Snap(layout.Dip(4));
        _volumeTrack.Offset = new Vector3(Snap(row.TrackLeft), Snap(row.TrackY - (trackHeight / 2)), 0);
        _volumeTrack.Size = new Vector2(Snap(row.TrackWidth), trackHeight);
        _volumeFill.Offset = _volumeTrack.Offset;
        RoundEnds(_volumeTrack);
        var thumb = Snap(layout.Dip(12));
        _volumeThumb.Size = new Vector2(thumb, thumb);
        RoundCorners(_volumeThumb, thumb / 2);
        var textHeight = layout.Dip(20);
        _volumeValue.Place(row.ValueLeft, row.TrackY - (textHeight / 2), row.ValueWidth, textHeight, Format(TextFont, 12, Semibold, TextAlign.Trailing), Secondary);
        _volumeNone.Place(row.TrackLeft, row.TrackY - (textHeight / 2), row.TrackWidth, textHeight, Format(TextFont, 12.5, Regular, TextAlign.Leading), Tertiary);
    }

    /// <summary>Volume row text and bar; true unless the GPU device was lost.</summary>
    private bool DrawVolume()
    {
        var v = _volume;
        var ok = _volumeGlyph.Show(v?.Glyph ?? "");
        ok &= _volumeValue.Show(v is { } r ? r.Percent.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty);
        ok &= _volumeNone.Show(v is null ? "No audio output" : string.Empty);
        _volumeTrack.IsVisible = v is not null;
        _volumeFill.IsVisible = v is not null;
        _volumeThumb.IsVisible = v is not null;
        if (v is { } reading)
        {
            // Muted: the bar keeps the level but dims, as in Windows' own flyout.
            _volumeFillBrush.Color = reading.Muted ? Tertiary : Fill;
            var width = _volumeTrack.Size.X * reading.Percent / 100f;
            _volumeFill.Size = new Vector2(Math.Max(_volumeTrack.Size.Y, Snap(width)), _volumeTrack.Size.Y);
            RoundEnds(_volumeFill);
            var t = _volumeThumb.Size.X;
            _volumeThumb.Offset = new Vector3(Snap(_volumeTrack.Offset.X + width - (t / 2)), Snap(_volumeTrack.Offset.Y + (_volumeTrack.Size.Y / 2) - (t / 2)), 0);
        }

        return ok;
    }

    private void PlaceActivity(IslandLayout layout)
    {
        var c = layout.Compact;
        var e = layout.Expanded;
        _compactActivityGlyph.Place(c.Left + layout.Dip(14), c.Top, layout.Dip(18), c.Height, Format(IconFont, 12, Regular, TextAlign.Center), Primary);
        _compactActivityText.Place(c.Left + layout.Dip(34), c.Top - layout.Dip(1.5), layout.Dip(84), c.Height, Format(TextFont, 13, Semibold, TextAlign.Leading, trim: true), Primary);
        _compactLevelTrack.Offset = new Vector3(Snap(c.Left + layout.Dip(35)), Snap(c.Bottom - layout.Dip(8)), 0);
        _compactLevelTrack.Size = new Vector2(Snap(layout.Dip(56)), Snap(layout.Dip(2.5)));
        _compactLevelFill.Offset = _compactLevelTrack.Offset;
        RoundEnds(_compactLevelTrack);

        _activityGlyph.Place(e.Left + layout.Dip(24), e.Top + layout.Dip(28), layout.Dip(28), layout.Dip(40), Format(IconFont, 20, Regular, TextAlign.Center), Secondary);
        _activityTitle.Place(e.Left + layout.Dip(58), e.Top + layout.Dip(16), layout.Dip(220), layout.Dip(62), Format(DisplayFont, 44, Semibold, TextAlign.Leading, trim: true), Primary);
        _activitySubtitle.Place(e.Left + layout.Dip(26), e.Top + layout.Dip(78), layout.Dip(368), layout.Dip(24), Format(TextFont, 15, Regular, TextAlign.Leading, trim: true), Secondary);
        _activityTrack.Offset = new Vector3(Snap(e.Left + layout.Dip(26)), Snap(e.Top + layout.Dip(116)), 0);
        _activityTrack.Size = new Vector2(Snap(layout.Dip(368)), Snap(layout.Dip(6)));
        _activityFill.Offset = _activityTrack.Offset;
        RoundEnds(_activityTrack);
        foreach (var square in ActivityLayout.Actions(layout, ActivityLayout.MaxActions))
        {
            _activityButtons[square.Index].Place(square.CenterX - (square.Size / 2), square.CenterY - (square.Size / 2), square.Size, square.Size, Format(IconFont, 16, Regular, TextAlign.Center), Primary);
        }

        _activityLevelShown = null;
    }

    private double? _activityLevelShown;

    /// <summary>Activity text in the compact slot and the card; true unless the GPU device was lost.</summary>
    private bool DrawActivity()
    {
        var a = _activity;
        var inPill = a is not null && _pill.Slot(true, _media is not null) == PillSlot.Activity;
        _compactActivity.IsVisible = inPill;
        var ok = _compactActivityGlyph.Show(inPill ? a!.Glyph : string.Empty);
        var compact = !inPill ? string.Empty : a!.CompactFor(_showPrivate) + (_activityOthers > 0 ? $"  +{_activityOthers}" : string.Empty);
        ok &= _compactActivityText.Show(compact);
        ok &= _activityGlyph.Show(a?.Glyph ?? string.Empty);
        ok &= _activityTitle.Show(a is null ? string.Empty : a.Privacy == ActivityPrivacy.Private && !_showPrivate ? a.RedactedText : a.Title);
        ok &= _activitySubtitle.Show(a?.Subtitle ?? string.Empty);
        var squares = ActivityLayout.Actions(_layout, a?.Actions.Count ?? 0);
        for (var i = 0; i < _activityButtons.Length; i++)
        {
            // Buttons sit right-aligned: the placed slots are for three; fewer actions use the rightmost slots.
            var slot = i - (ActivityLayout.MaxActions - squares.Count);
            ok &= _activityButtons[i].Show(a is not null && slot >= 0 && slot < a.Actions.Count ? a.Actions[slot].Glyph : string.Empty);
        }

        return ok;
    }

    /// <summary>The activity's level bars (compact and card), animated between values when it has a previous value.</summary>
    private void DrawActivityLevel(IReadOnlyList<MotionStep> steps)
    {
        var level = _activity?.Level;
        _compactLevelTrack.IsVisible = level is not null;
        _compactLevelFill.IsVisible = level is not null;
        _activityTrack.IsVisible = level is not null;
        _activityFill.IsVisible = level is not null;
        if (level is not { } l || _activityLevelShown == l)
        {
            _activityLevelShown = level;
            return;
        }

        _activityLevelShown = l;
        l = Math.Clamp(l, 0, 1);
        foreach (var (track, fill) in new[] { (_compactLevelTrack, _compactLevelFill), (_activityTrack, _activityFill) })
        {
            var size = new Vector2(Math.Max(track.Size.Y, Snap(track.Size.X * l)), track.Size.Y);
            RoundEnds(fill, track.Size);
            if (steps.Count > 0)
            {
                fill.StartAnimation("Size", Vector2To(size, Ms(steps[0].Spec.Duration)));
            }
            else
            {
                fill.Size = size;
            }
        }
    }

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
        // The compact slot shows one thing: the primary activity, else what is playing, else the date.
        var compactMedia = m is not null && _pill.Slot(_activity is not null, true) == PillSlot.Media;
        ok &= _compactMediaGlyph.Show(!compactMedia ? string.Empty : m!.IsPlaying ? "\uE768" : "\uE769");
        ok &= _compactMediaTitle.Show(!compactMedia ? string.Empty : m!.Title.Length > 0 ? m.Title : m.Headline);
        ok &= _mediaTitle.Show(m is null ? string.Empty : m.Title.Length > 0 ? m.Title : "Unknown title");
        // J-F01: with several sessions, the app name tells them apart.
        var source = m is { SessionCount: > 1 } ? m.SourceName : string.Empty;
        ok &= _mediaArtist.Show(m is null ? string.Empty : source.Length == 0 ? m.Artist : m.Artist.Length == 0 ? source : $"{m.Artist} \u00b7 {source}");
        ok &= _mediaClock.Show(m is null || _clock is null ? string.Empty : _clock.TimeWithDesignator, _clock?.Designator.Length ?? 0);
        // FR-MED-02: only controls the session reports as enabled are shown.
        var playing = m?.IsPlaying == true;
        ok &= _buttonPrevious.Show(m?.CanPrevious == true ? MediaControls.Glyph(MediaCommand.Previous, playing) : string.Empty);
        ok &= _buttonPlayPause.Show(m?.CanPlayPause == true ? MediaControls.Glyph(MediaCommand.PlayPause, playing) : string.Empty);
        ok &= _buttonNext.Show(m?.CanNext == true ? MediaControls.Glyph(MediaCommand.Next, playing) : string.Empty);
        ok &= _buttonSwitch.Show(m is { SessionCount: > 1 } ? MediaControls.Glyph(MediaCommand.SwitchSource, playing) : string.Empty);
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

    private void DrawAlert(IReadOnlyList<MotionStep>? level = null)
    {
        var content = _alert;
        var value = content?.ClampedLevel;
        _ = _alertGlyph.Show(content?.Glyph ?? string.Empty);
        _ = _alertWide.Show(value is null ? content?.Text ?? string.Empty : string.Empty);
        _ = _alertNarrow.Show(value is null ? string.Empty : content?.Text ?? string.Empty);
        _ = _alertValue.Show(value is null ? string.Empty : content?.Value ?? string.Empty);
        _alertTrack.IsVisible = value is not null;
        _alertFill.IsVisible = value is not null;
        if (value is { } l)
        {
            var size = new Vector2(Math.Max(_alertTrack.Size.Y, Snap(_alertTrack.Size.X * l)), _alertTrack.Size.Y);
            if (level is { Count: > 0 } steps)
            {
                // Level motion: the fill follows the value instead of jumping (a rapid change restarts from where it is).
                // The clip covers the full track and the fill is a capsule-ended bar, so only its width animates.
                RoundEnds(_alertFill, _alertTrack.Size);
                _alertFill.StartAnimation("Size", Vector2To(size, Ms(steps[0].Spec.Duration)));
            }
            else
            {
                _alertFill.Size = size;
                RoundEnds(_alertFill);
            }
        }
    }

    private float Px(double dip) => (float)_layout.Dip(dip);

    private static int Ms(TimeSpan span) => (int)Math.Round(span.TotalMilliseconds);

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
            var slot = _pill.Slot(_activity is not null, _media is not null);
            ok &= _compactDate.Show(slot == PillSlot.Date ? clock.ShortDate : string.Empty);
            ok &= _compactTime.Show(_pillBattery.Length > 0 ? $"{_pillBattery}  \u00b7  {clock.TimeWithDesignator}" : clock.TimeWithDesignator, clock.Designator.Length);
            ok &= _bigTime.Show(clock.TimeWithDesignator, clock.Designator.Length);
            ok &= _longDate.Show(clock.LongDate);
            ok &= _progressLabel.Show(clock.DayProgressText);
            _progressFill.Size = new Vector2(Math.Max(_progressTrack.Size.Y, Snap(_progressTrack.Size.X * clock.DayFraction)), _progressTrack.Size.Y);
            RoundEnds(_progressFill);
        }

        ok &= DrawStatus();
        ok &= DrawMedia();
        ok &= DrawVolume();
        ok &= DrawActivity();
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

            _ = DrawStatus();
            _artShown = -1;
            _ = DrawMedia();
            _ = DrawVolume();
            _ = DrawActivity();
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

        /// <summary>The sprite that shows the text (for opacity motions).</summary>
        public SpriteVisual Visual => _visual;

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
