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
/// The island's Windows.UI.Composition visual tree (ADR-001 §4.5, option C), Visual Experience 2.0:
/// <list type="bullet">
/// <item>a flush-top surface: a rounded shape that starts above the top edge of the work area, so only its lower corners
/// show, filled with a subtle vertical gradient and lit by a rim along its lower edge;</item>
/// <item>two concave shoulders that join it to the screen edge, driven by expressions on the shape, so they follow every
/// morph in DWM without managed code per frame;</item>
/// <item>compact, alert and expanded content cross-fading inside a clip that follows the shape. The expanded content has a
/// header row, a body that slides between pages, chevrons and page dots.</item>
/// </list>
/// Nothing renders while settled except a text redraw when the clock's minute changes.
/// </summary>
internal sealed partial class IslandVisuals : IDisposable
{
    private const string DisplayFont = "Segoe UI Variable Display";
    private const string TextFont = "Segoe UI Variable Text";

    /// <summary>Windows 11 icon font: icons are glyphs, so no image assets are shipped.</summary>
    public const string IconFont = "Segoe Fluent Icons";
    private const int Regular = 400;
    private const int Semibold = 600;

    // Brushes whose colour is a palette role; ApplyPalette recolours them all.
    private readonly List<(CompositionColorBrush Brush, Func<IslandPalette, Rgba> Role)> _bound = [];
    private IslandPalette _palette = IslandPalette.Dark;

    private Color Primary => ToColor(_palette.Primary);

    private Color Secondary => ToColor(_palette.Secondary);

    private Color Tertiary => ToColor(_palette.Tertiary);

    private Color Fill => ToColor(_palette.Bar);

    private readonly Compositor _compositor;
    private readonly TextRenderer _text;
    private readonly CompositionEasingFunction _ease;
    private readonly ContainerVisual _root;
    private readonly ContainerVisual _island;

    // Surface (Visual Experience 2.0).
    private readonly CompositionRoundedRectangleGeometry _shape;
    private readonly CompositionColorGradientStop _surfaceTop;
    private readonly CompositionColorGradientStop _surfaceBottom;
    private readonly CompositionColorGradientStop _rimFade;
    private readonly CompositionColorGradientStop _rimMid;
    private readonly CompositionColorGradientStop _rimEdge;
    private readonly CompositionColorBrush _shoulderColor;
    private readonly SpriteVisual _shoulderLeft;
    private readonly SpriteVisual _shoulderRight;
    private readonly CompositionColorGradientStop[] _shoulderStops;

    private readonly ContainerVisual _compactContent;
    private readonly ContainerVisual _expandedContent;
    private readonly ContainerVisual _alertContent;
    private readonly List<TextFormat> _formats = [];
    private IslandLayout _layout;
    private ClockReading? _clock;

    // Alert pill.
    private readonly TextLine _alertGlyph;
    private readonly TextLine _alertWide;
    private readonly TextLine _alertNarrow;
    private readonly TextLine _alertValue;
    private readonly SpriteVisual _alertTrack;
    private readonly SpriteVisual _alertFill;
    private readonly CompositionColorBrush _alertFillBrush;
    private IslandAlert? _alert;

    // Compact pill.
    private readonly TextLine _compactDate;
    private readonly TextLine _compactTime;
    private readonly TextLine _compactMediaGlyph;
    private readonly TextLine _compactMediaTitle;
    private readonly SpriteVisual _compactArt;
    private readonly ContainerVisual _compactActivity;
    private readonly TextLine _compactActivityGlyph;
    private readonly TextLine _compactActivityText;
    private readonly Ring _compactRing;
    private readonly SpriteVisual _compactLevelTrack;
    private readonly SpriteVisual _compactLevelFill;
    private readonly CompositionColorBrush _compactLevelBrush;
    private PillContent _pill = PillContent.Default;
    private string _pillBattery = string.Empty;

    // Expanded frame: header (title, date and time, status), chrome (chevrons, dots), body (pages and controls).
    private readonly ContainerVisual _header;
    private readonly TextLine _headerGlyph;
    private readonly TextLine _headerTitle;
    private readonly TextLine _headerDate;
    private readonly TextLine _headerTime;
    private readonly TextLine _statusGlyphs;
    private readonly TextLine _statusText;
    private string _statusGlyphText = string.Empty;
    private string _statusValue = string.Empty;
    private readonly ContainerVisual _chrome;
    private readonly ShapeVisual _dotsVisual;
    private readonly CompositionRoundedRectangleGeometry[] _dots;
    private readonly CompositionSpriteShape[] _dotShapes;
    private readonly CompositionColorBrush[] _dotBrushes;
    private readonly ContainerVisual _body;
    private IslandPage _page = IslandPage.Clock;
    private IReadOnlyList<IslandPage> _pages = [IslandPage.Clock];

    public IslandVisuals(Compositor compositor, CompositionTarget target, TextRenderer text, IslandLayout layout)
    {
        ArgumentNullException.ThrowIfNull(target);
        _compositor = compositor;
        _text = text;
        _layout = layout;
        _ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.33f, 1f), new Vector2(0.68f, 1f));

        _root = compositor.CreateContainerVisual();
        _island = compositor.CreateContainerVisual();

        // The surface: a vertical gradient fill and a rim that fades in towards the lower edge.
        var shapeVisual = compositor.CreateShapeVisual();
        _shape = compositor.CreateRoundedRectangleGeometry();
        var sprite = compositor.CreateSpriteShape(_shape);
        var fill = compositor.CreateLinearGradientBrush();
        fill.StartPoint = Vector2.Zero;
        fill.EndPoint = new Vector2(0, 1);
        _surfaceTop = compositor.CreateColorGradientStop(0f, default);
        _surfaceBottom = compositor.CreateColorGradientStop(1f, default);
        fill.ColorStops.Add(_surfaceTop);
        fill.ColorStops.Add(_surfaceBottom);
        var rim = compositor.CreateLinearGradientBrush();
        rim.StartPoint = Vector2.Zero;
        rim.EndPoint = new Vector2(0, 1);
        _rimFade = compositor.CreateColorGradientStop(0f, default);
        _rimMid = compositor.CreateColorGradientStop(0.55f, default);
        _rimEdge = compositor.CreateColorGradientStop(1f, default);
        rim.ColorStops.Add(_rimFade);
        rim.ColorStops.Add(_rimMid);
        rim.ColorStops.Add(_rimEdge);
        sprite.FillBrush = fill;
        sprite.StrokeBrush = rim;
        sprite.StrokeThickness = 1;
        shapeVisual.Shapes.Add(sprite);

        // Shoulders: a square of the surface colour masked by a radial gradient, which leaves a concave quarter circle.
        _shoulderColor = compositor.CreateColorBrush(default);
        _shoulderStops = new CompositionColorGradientStop[4];
        _shoulderLeft = Shoulder(new Vector2(0, 1), 0);
        _shoulderRight = Shoulder(new Vector2(1, 1), 2);

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

        // Compact.
        _compactDate = new TextLine(this, _compactContent);
        _compactTime = new TextLine(this, _compactContent);
        _compactMediaGlyph = new TextLine(this, _compactContent);
        _compactMediaTitle = new TextLine(this, _compactContent);
        _compactArt = compositor.CreateSpriteVisual();
        _compactContent.Children.InsertAtTop(_compactArt);
        _compactActivity = compositor.CreateContainerVisual();
        _compactContent.Children.InsertAtTop(_compactActivity);
        _compactActivityGlyph = new TextLine(this, _compactActivity);
        _compactActivityText = new TextLine(this, _compactActivity);
        _compactRing = new Ring(this, _compactActivity);
        _compactLevelTrack = Bar(_compactActivity, p => p.Track);
        _compactLevelBrush = compositor.CreateColorBrush(Fill);
        _compactLevelFill = compositor.CreateSpriteVisual();
        _compactLevelFill.Brush = _compactLevelBrush;
        _compactActivity.Children.InsertAtTop(_compactLevelFill);

        // Expanded: body first (it slides under the header and chrome), then header, chrome and the volume row.
        _body = compositor.CreateContainerVisual();
        _expandedContent.Children.InsertAtTop(_body);
        _header = compositor.CreateContainerVisual();
        _expandedContent.Children.InsertAtTop(_header);
        _headerGlyph = new TextLine(this, _header);
        _headerTitle = new TextLine(this, _header);
        _headerDate = new TextLine(this, _header);
        _headerTime = new TextLine(this, _header);
        _statusGlyphs = new TextLine(this, _header);
        _statusText = new TextLine(this, _header);
        _chrome = compositor.CreateContainerVisual();
        _expandedContent.Children.InsertAtTop(_chrome);
        // Page dots are rounded-rectangle shapes, so the current page's capsule can grow and shrink without a clip.
        _dotsVisual = compositor.CreateShapeVisual();
        _chrome.Children.InsertAtTop(_dotsVisual);
        _dots = new CompositionRoundedRectangleGeometry[6];
        _dotShapes = new CompositionSpriteShape[6];
        _dotBrushes = new CompositionColorBrush[6];
        for (var i = 0; i < _dots.Length; i++)
        {
            _dotBrushes[i] = compositor.CreateColorBrush(Tertiary);
            _dots[i] = compositor.CreateRoundedRectangleGeometry();
            _dotShapes[i] = compositor.CreateSpriteShape(_dots[i]);
            _dotShapes[i].FillBrush = _dotBrushes[i];
        }

        CreatePages();

        // Alert.
        _alertGlyph = new TextLine(this, _alertContent);
        _alertWide = new TextLine(this, _alertContent);
        _alertNarrow = new TextLine(this, _alertContent);
        _alertValue = new TextLine(this, _alertContent);
        _alertTrack = Bar(_alertContent, p => p.Track);
        _alertFillBrush = compositor.CreateColorBrush(Fill);
        _alertFill = compositor.CreateSpriteVisual();
        _alertFill.Brush = _alertFillBrush;
        _alertContent.Children.InsertAtTop(_alertFill);

        _island.Children.InsertAtTop(shapeVisual);
        _island.Children.InsertAtTop(_shoulderLeft);
        _island.Children.InsertAtTop(_shoulderRight);
        _island.Children.InsertAtTop(content);
        _root.Children.InsertAtTop(_island);
        target.Root = _root;
        ApplySurfaceColours(hovered: false);
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
            if (child != _shoulderLeft && child != _shoulderRight)
            {
                child.Size = canvas;
            }
        }

        var drawn = layout.ShapeFor(state.Presentation).Drawn();
        _shape.Size = new Vector2((float)drawn.Width, (float)drawn.Height);
        _shape.Offset = new Vector2((float)drawn.Left, (float)drawn.Top);
        _shape.CornerRadius = new Vector2((float)drawn.Radius);
        PlaceShoulders(layout);
        _island.CenterPoint = new Vector3((float)(layout.Canvas.Width / 2.0), (float)layout.Compact.Top, 0);
        _island.Scale = Vector3.One;
        ApplySurfaceColours(state.Hovered);
        _compactContent.Opacity = state.Presentation == IslandPresentation.Compact ? 1 : 0;
        _expandedContent.Opacity = state.Presentation == IslandPresentation.Expanded ? 1 : 0;
        _alertContent.Opacity = state.Presentation == IslandPresentation.Alert ? 1 : 0;
        _expandedContent.Offset = Vector3.Zero;

        foreach (var format in _formats)
        {
            format.Dispose();
        }

        _formats.Clear();
        PlaceCompact(layout);
        PlaceHeader(layout);
        PlacePages(layout);
        PlaceVolume(layout);
        PlaceAlert(layout);
        RedrawAll();
    }

    private void PlaceCompact(IslandLayout layout)
    {
        var c = layout.Compact;
        _compactDate.Place(c.Left + layout.Dip(18), c.Top, layout.Dip(100), c.Height, Format(TextFont, 13, Semibold, TextAlign.Leading), Secondary);
        _compactTime.Place(c.Right - layout.Dip(18 + 130), c.Top, layout.Dip(130), c.Height, Format(TextFont, 14, Semibold, TextAlign.Trailing), Primary, smallSize: Px(11));

        // Media: a small cover (or a glyph) and the title.
        var art = Snap(layout.Dip(20));
        _compactArt.Size = new Vector2(art, art);
        _compactArt.Offset = new Vector3(Snap(c.Left + layout.Dip(13)), Snap(c.Top + ((c.Height - art) / 2)), 0);
        RoundCorners(_compactArt, (float)layout.Dip(5));
        _compactArt.Brush = _artBrush;
        _compactMediaGlyph.Place(c.Left + layout.Dip(13), c.Top, layout.Dip(20), c.Height, Format(IconFont, 11, Regular, TextAlign.Center), Secondary);
        _compactMediaTitle.Place(c.Left + layout.Dip(40), c.Top, layout.Dip(84), c.Height, Format(TextFont, 12.5, Semibold, TextAlign.Leading, trim: true), Secondary);

        // Activity: a progress ring (timers) or a tinted glyph, the text, and a level bar for other activities with a level.
        _compactActivityGlyph.Place(c.Left + layout.Dip(13), c.Top, layout.Dip(20), c.Height, Format(IconFont, 12, Regular, TextAlign.Center), Primary);
        _compactActivityText.Place(c.Left + layout.Dip(38), c.Top, layout.Dip(92), c.Height, Format(TextFont, 13, Semibold, TextAlign.Leading, trim: true), Primary);
        _compactRing.Place(c.Left + layout.Dip(23), c.Top + (c.Height / 2), layout.Dip(HaloDesign.CompactRingDiameter), layout.Dip(HaloDesign.CompactRingStroke));
        _compactLevelTrack.Offset = new Vector3(Snap(c.Left + layout.Dip(39)), Snap(c.Bottom - layout.Dip(7)), 0);
        _compactLevelTrack.Size = new Vector2(Snap(layout.Dip(56)), Snap(layout.Dip(2.5)));
        _compactLevelFill.Offset = _compactLevelTrack.Offset;
        RoundEnds(_compactLevelTrack);
        _activityLevelShown = null;
    }

    private void PlaceHeader(IslandLayout layout)
    {
        var e = layout.Expanded;
        var top = e.Top + layout.Dip(ActivityLayout.HeaderCenterDip - (ActivityLayout.HeaderHeightDip / 2));
        var h = layout.Dip(ActivityLayout.HeaderHeightDip);
        _headerGlyph.Place(e.Left + layout.Dip(24), top, layout.Dip(18), h, Format(IconFont, 12, Regular, TextAlign.Center), Secondary);
        _headerTitle.Place(e.Left + layout.Dip(46), top, layout.Dip(110), h, Format(TextFont, HaloDesign.HeaderSize, Semibold, TextAlign.Leading, trim: true), Primary);
        _headerDate.Place(e.CenterX - layout.Dip(104), top, layout.Dip(100), h, Format(TextFont, 12.5, Regular, TextAlign.Trailing), Secondary);
        _headerTime.Place(e.CenterX + layout.Dip(4), top, layout.Dip(100), h, Format(TextFont, 12.5, Semibold, TextAlign.Leading), Primary, smallSize: Px(10));
        _statusText.Place(e.Right - layout.Dip(24 + 40), top, layout.Dip(40), h, Format(TextFont, 12, Semibold, TextAlign.Trailing), Secondary);
        _statusGlyphs.Place(e.Right - layout.Dip(24 + 40 + 3 + 64), top, layout.Dip(64), h, Format(IconFont, 13, Regular, TextAlign.Trailing), Secondary);
    }

    private void PlaceAlert(IslandLayout layout)
    {
        var a = layout.Alert;
        _alertGlyph.Place(a.Left + layout.Dip(16), a.Top, layout.Dip(26), a.Height, Format(IconFont, 16, Regular, TextAlign.Center), Primary);
        _alertWide.Place(a.Left + layout.Dip(50), a.Top, layout.Dip(252), a.Height, Format(TextFont, 14, Semibold, TextAlign.Leading, trim: true), Primary);
        _alertNarrow.Place(a.Left + layout.Dip(50), a.Top, layout.Dip(128), a.Height, Format(TextFont, 14, Semibold, TextAlign.Leading, trim: true), Primary);
        _alertValue.Place(a.Right - layout.Dip(18 + 34), a.Top, layout.Dip(34), a.Height, Format(TextFont, 12.5, Semibold, TextAlign.Trailing), Secondary);
        _alertTrack.Offset = new Vector3(Snap(a.Left + layout.Dip(186)), Snap(a.Top + ((a.Height - layout.Dip(4)) / 2)), 0);
        _alertTrack.Size = new Vector2(Snap(layout.Dip(80)), Snap(layout.Dip(4)));
        _alertFill.Offset = _alertTrack.Offset;
        RoundEnds(_alertTrack);
    }

    /// <summary>
    /// Shows one page of the expanded island, its header and its page dots (FR-ISL-08). A change while the island is
    /// open slides the body in from the side it came from (Swap motion, MOTION_SYSTEM.md); <paramref name="animate"/> is
    /// false when the island is not expanded.
    /// </summary>
    public void ShowPage(IslandPage page, IReadOnlyList<IslandPage> pages, bool animate, bool reducedMotion)
    {
        var before = _pages.ToList().IndexOf(_page);
        var changed = page != _page;
        (_page, _pages) = (page, pages);
        ShowPageGroups();
        DrawHeader();
        var dots = ActivityLayout.Dots(_layout, pages.Count);
        var dot = Snap(_layout.Dip(5));
        var wide = Snap(_layout.Dip(16));
        _dotsVisual.Size = new Vector2(_layout.Canvas.Width, _layout.Canvas.Height);
        for (var i = 0; i < _dots.Length; i++)
        {
            var shown = _dotsVisual.Shapes.Contains(_dotShapes[i]);
            if (i >= dots.Count)
            {
                if (shown)
                {
                    _ = _dotsVisual.Shapes.Remove(_dotShapes[i]);
                }

                continue;
            }

            if (!shown)
            {
                _dotsVisual.Shapes.Add(_dotShapes[i]);
            }

            // The current page is a short capsule, the others small dots (no colour-only meaning: UIA names the page).
            var current = pages[i] == page;
            var size = new Vector2(current ? wide : dot, dot);
            var offset = new Vector2(Snap(dots[i].CenterX - (size.X / 2)), Snap(dots[i].CenterY - (dot / 2)));
            _dotBrushes[i].Color = current ? Primary : Tertiary;
            _dots[i].CornerRadius = new Vector2(dot / 2);
            if (changed && animate && !reducedMotion)
            {
                _dots[i].StartAnimation("Size", Vector2To(size, 220));
                _dots[i].StartAnimation("Offset", Vector2To(offset, 220));
            }
            else
            {
                _dots[i].StopAnimation("Size");
                _dots[i].StopAnimation("Offset");
                _dots[i].Size = size;
                _dots[i].Offset = offset;
            }
        }

        if (changed && animate && MotionPlanner.Swap(reducedMotion) is { Count: > 0 } swap)
        {
            // Direction: forwards slides in from the right. A wrap (last → first) still reads as forwards.
            var after = pages.ToList().IndexOf(page);
            var n = Math.Max(1, pages.Count);
            var forwards = before < 0 || ((after - before + n) % n) <= n / 2;
            var spec = swap[0].Spec;
            var distance = Snap(_layout.Dip(28)) * (forwards ? 1 : -1);
            _body.Offset = new Vector3(distance, 0, 0);
            _body.StartAnimation("Offset", Vector3To(Vector3.Zero, Ms(spec.Duration) + 100));
            var fade = _compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, _ease);
            fade.Duration = spec.Duration;
            _body.StartAnimation("Opacity", fade);
            Dip(_headerTitle.Visual, swap);
            Dip(_headerGlyph.Visual, swap);
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

    /// <summary>
    /// Shows status icons (icon-font glyphs, for example network and battery) and a short value ("82%") at the right of
    /// the header row.
    /// </summary>
    public void ShowStatus(string glyphs, string text, bool reducedMotion = false)
    {
        // C-F03/C-F06: a battery or connectivity change swaps the cluster; the first reading just appears.
        var changed = (_statusGlyphText.Length > 0 || _statusValue.Length > 0) && (!string.Equals(glyphs, _statusGlyphText, StringComparison.Ordinal) || !string.Equals(text, _statusValue, StringComparison.Ordinal));
        (_statusGlyphText, _statusValue) = (glyphs, text);
        _ = DrawStatus();
        if (changed)
        {
            Dip(_statusGlyphs.Visual, MotionPlanner.Swap(reducedMotion));
            Dip(_statusText.Visual, MotionPlanner.Swap(reducedMotion));
        }
    }

    /// <summary>Shows a transient alert (null clears it) tinted with its tone. Only changed lines are redrawn.</summary>
    public void ShowAlert(IslandAlert? alert, bool reducedMotion = false)
    {
        var before = _alert?.Content.ClampedLevel;
        _alert = alert;
        DrawAlert(MotionPlanner.Level(before, alert?.Content.ClampedLevel, reducedMotion));
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
            _surfaceBottom.StartAnimation("Color", ColorTo(ToColor(hovered ? _palette.HoverFill : _palette.SurfaceBottom), 140));
            _rimEdge.StartAnimation("Color", ColorTo(ToColor(hovered ? _palette.HoverEdge : _palette.Rim), 140));
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

        var target = _layout.ShapeFor(transition.Next.Presentation).Drawn();
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
    public void SwapAlertContent(IReadOnlyList<MotionStep> steps) => Dip(_alertContent, steps);

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

        _artShown = -1;
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
        foreach (var (brush, role) in _bound)
        {
            brush.Color = ToColor(role(palette));
        }

        _mediaAccentFor = -2; // the art accent is re-checked against the new fill
        ApplyLayout(_layout, state);
    }

    /// <summary>The palette in use (for tests and the settings window).</summary>
    public IslandPalette Palette => _palette;

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

    private static Color ToColor(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private static Color WithAlpha(Rgba c, byte alpha) => Color.FromArgb((byte)(c.A * alpha / 255), c.R, c.G, c.B);

    /// <summary>The accent of a tone in the current palette.</summary>
    private Color Accent(Tone tone) => ToColor(HaloDesign.Accent(tone, _palette));

    /// <summary>Text on an accent fill: black or white, whichever reads better (WCAG contrast).</summary>
    private static Color OnAccent(Color accent)
    {
        var c = Rgba.Opaque(accent.R, accent.G, accent.B);
        return Rgba.Contrast(c, Rgba.Opaque(0, 0, 0)) >= Rgba.Contrast(c, Rgba.Opaque(255, 255, 255)) ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);
    }

    /// <summary>A brush that always has the colour of a palette role.</summary>
    private CompositionColorBrush Bound(Func<IslandPalette, Rgba> role)
    {
        var brush = _compositor.CreateColorBrush(ToColor(role(_palette)));
        _bound.Add((brush, role));
        return brush;
    }

    /// <summary>A bar sprite in a palette colour, added to <paramref name="parent"/>.</summary>
    private SpriteVisual Bar(ContainerVisual parent, Func<IslandPalette, Rgba> role)
    {
        var bar = _compositor.CreateSpriteVisual();
        bar.Brush = Bound(role);
        parent.Children.InsertAtTop(bar);
        return bar;
    }

    private SpriteVisual Shoulder(Vector2 center, int stopIndex)
    {
        var mask = _compositor.CreateRadialGradientBrush();
        mask.EllipseCenter = center;
        mask.EllipseRadius = Vector2.One;
        _shoulderStops[stopIndex] = _compositor.CreateColorGradientStop(0.9f, Color.FromArgb(0, 0, 0, 0));
        _shoulderStops[stopIndex + 1] = _compositor.CreateColorGradientStop(1f, Color.FromArgb(255, 0, 0, 0));
        mask.ColorStops.Add(_compositor.CreateColorGradientStop(0f, Color.FromArgb(0, 0, 0, 0)));
        mask.ColorStops.Add(_shoulderStops[stopIndex]);
        mask.ColorStops.Add(_shoulderStops[stopIndex + 1]);
        var brush = _compositor.CreateMaskBrush();
        brush.Source = _shoulderColor;
        brush.Mask = mask;
        var visual = _compositor.CreateSpriteVisual();
        visual.Brush = brush;
        return visual;
    }

    /// <summary>The shoulders sit just outside the shape's top corners and follow its width through every morph.</summary>
    private void PlaceShoulders(IslandLayout layout)
    {
        var s = (float)layout.Shoulder;
        var top = (float)layout.Compact.Top;
        foreach (var visual in new[] { _shoulderLeft, _shoulderRight })
        {
            visual.Size = new Vector2(s, s);
            visual.IsVisible = s >= 2 && !_palette.HighContrast;
        }

        // A one-pixel anti-aliased edge whatever the size.
        var edge = Math.Clamp(1 - (1.2f / Math.Max(1, s)), 0.5f, 0.99f);
        foreach (var i in new[] { 0, 2 })
        {
            _shoulderStops[i].Offset = edge;
        }

        var left = _compositor.CreateExpressionAnimation("Vector3(g.Offset.X - s, t, 0)");
        left.SetReferenceParameter("g", _shape);
        left.SetScalarParameter("s", s);
        left.SetScalarParameter("t", top);
        _shoulderLeft.StartAnimation("Offset", left);
        var right = _compositor.CreateExpressionAnimation("Vector3(g.Offset.X + g.Size.X, t, 0)");
        right.SetReferenceParameter("g", _shape);
        right.SetScalarParameter("t", top);
        _shoulderRight.StartAnimation("Offset", right);
    }

    private void ApplySurfaceColours(bool hovered)
    {
        var p = _palette;
        _surfaceTop.Color = ToColor(p.SurfaceTop);
        _surfaceBottom.Color = ToColor(hovered ? p.HoverFill : p.SurfaceBottom);
        _shoulderColor.Color = ToColor(p.SurfaceTop);

        // High contrast: a full outline. Otherwise the rim is invisible near the screen edge and lights the lower edge.
        var rim = hovered ? p.HoverEdge : p.Rim;
        _rimFade.Color = p.HighContrast ? ToColor(rim) : WithAlpha(rim, 0);
        _rimMid.Color = p.HighContrast ? ToColor(rim) : WithAlpha(rim, 0);
        _rimEdge.Color = ToColor(rim);
    }

    private static float Snap(double px) => (float)Math.Round(px);

    /// <summary>Clips a bar to a capsule (radius = half its height), optionally over a larger area than its size.</summary>
    private void RoundEnds(SpriteVisual bar, Vector2? area = null)
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        geometry.Size = area ?? bar.Size;
        geometry.CornerRadius = new Vector2((area ?? bar.Size).Y / 2);
        bar.Clip = _compositor.CreateGeometricClip(geometry);
    }

    private void RoundCorners(SpriteVisual visual, float radius)
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        geometry.Size = visual.Size;
        geometry.CornerRadius = new Vector2(radius);
        visual.Clip = _compositor.CreateGeometricClip(geometry);
    }

    /// <summary>The Swap motion on one visual: a dip to 35 % and back, never to zero.</summary>
    private void Dip(Visual visual, IReadOnlyList<MotionStep> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        var spec = steps[0].Spec;
        var a = _compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0f, 1f);
        a.InsertKeyFrame((float)(spec.Delay.TotalMilliseconds / spec.End.TotalMilliseconds), 0.35f, _ease);
        a.InsertKeyFrame(1f, 1f, _ease);
        a.Duration = spec.End;
        visual.StartAnimation("Opacity", a);
    }

    private IEnumerable<TextLine> Lines() =>
    [
        _compactDate, _compactTime, _compactMediaGlyph, _compactMediaTitle, _compactActivityGlyph, _compactActivityText,
        _headerGlyph, _headerTitle, _headerDate, _headerTime, _statusGlyphs, _statusText,
        _alertGlyph, _alertWide, _alertNarrow, _alertValue, .. PageLines(),
    ];

    private bool DrawStatus()
    {
        var ok = _statusGlyphs.Show(_statusGlyphText);
        ok &= _statusText.Show(_statusValue);
        return ok;
    }

    /// <summary>The header row: the page's glyph and title, and the date and time (except on the clock page).</summary>
    private bool DrawHeader()
    {
        var tone = IslandPages.ToneOf(_page, _activity);
        _headerGlyph.SetColor(tone == Tone.Neutral ? Secondary : Accent(tone));
        var ok = _headerGlyph.Show(IslandPages.Glyph(_page, _activity));
        ok &= _headerTitle.Show(IslandPages.Title(_page, _activity));
        var showTime = _page != IslandPage.Clock && _clock is not null;
        ok &= _headerDate.Show(showTime ? _clock!.ShortDate : string.Empty);
        ok &= _headerTime.Show(showTime ? _clock!.TimeWithDesignator : string.Empty, showTime ? _clock!.Designator.Length : 0);
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
        var content = _alert?.Content;
        var value = content?.ClampedLevel;
        var tone = _alert is null ? Tone.Neutral : HaloDesign.ToneOf(_alert);
        var accent = Accent(tone);
        _alertGlyph.SetColor(tone == Tone.Neutral ? Primary : accent);
        _alertFillBrush.Color = accent;
        _ = _alertGlyph.Show(content?.Glyph ?? string.Empty);
        _ = _alertWide.Show(value is null ? content?.Text ?? string.Empty : string.Empty);
        _ = _alertNarrow.Show(value is null ? string.Empty : content?.Text ?? string.Empty);
        _ = _alertValue.Show(value is null ? string.Empty : content?.Value ?? string.Empty);
        _alertTrack.IsVisible = value is not null;
        _alertFill.IsVisible = value is not null;
        if (value is { } l)
        {
            var size = new Vector2(Math.Max(_alertTrack.Size.Y, Snap(_alertTrack.Size.X * l)), _alertTrack.Size.Y);
            RoundEnds(_alertFill, _alertTrack.Size);
            if (level is { Count: > 0 } steps)
            {
                // Level motion: the fill follows the value instead of jumping (a rapid change restarts from where it is).
                _alertFill.StartAnimation("Size", Vector2To(size, Ms(steps[0].Spec.Duration)));
            }
            else
            {
                _alertFill.Size = size;
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
        var ok = DrawAllOnce();
        if (!ok)
        {
            // The GPU device was lost (driver update, adapter switch): replace it and draw everything again once.
            _text.RecoverDevice();
            foreach (var line in Lines())
            {
                line.Invalidate();
            }

            _artShown = -1;
            _ = DrawAllOnce();
        }
    }

    private bool DrawAllOnce()
    {
        var ok = true;
        if (_clock is { } clock)
        {
            var slot = _pill.Slot(_activity is not null, _media is not null);
            ok &= _compactDate.Show(slot == PillSlot.Date ? clock.ShortDate : string.Empty);
            ok &= _compactTime.Show(_pillBattery.Length > 0 ? $"{_pillBattery}  ·  {clock.TimeWithDesignator}" : clock.TimeWithDesignator, clock.Designator.Length);
        }

        ok &= DrawHeader();
        ok &= DrawStatus();
        ok &= DrawPages();
        return ok;
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

    private ScalarKeyFrameAnimation ScalarTo(float to, int ms, int delayMs = 0)
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
            var w = Math.Max(1, (int)Math.Ceiling(width));
            var h = Math.Max(1, (int)Math.Ceiling(height));
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

        /// <summary>Changes the text colour (a tone or a chip state); the next <see cref="Show"/> redraws if it changed.</summary>
        public void SetColor(Color color)
        {
            if (color != _color)
            {
                _color = color;
                _shown = null;
            }
        }

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

    /// <summary>
    /// A progress ring (timers, the focus page, the compact pill): a track circle and an arc whose end is trimmed to the
    /// level. The arc starts at twelve o'clock and its end animates in DWM (Level motion), so a second's step costs one
    /// short animation and no managed work per frame.
    /// </summary>
    private sealed class Ring
    {
        private readonly IslandVisuals _owner;
        private readonly ShapeVisual _visual;
        private readonly CompositionEllipseGeometry _trackGeometry;
        private readonly CompositionEllipseGeometry _arcGeometry;
        private readonly CompositionSpriteShape _track;
        private readonly CompositionSpriteShape _arc;
        private readonly CompositionColorBrush _arcBrush;
        private double? _level;

        public Ring(IslandVisuals owner, ContainerVisual parent)
        {
            _owner = owner;
            var c = owner._compositor;
            _visual = c.CreateShapeVisual();
            _trackGeometry = c.CreateEllipseGeometry();
            _arcGeometry = c.CreateEllipseGeometry();
            _track = c.CreateSpriteShape(_trackGeometry);
            _track.StrokeBrush = owner.Bound(p => p.Track);
            _arc = c.CreateSpriteShape(_arcGeometry);
            _arcBrush = c.CreateColorBrush(owner.Fill);
            _arc.StrokeBrush = _arcBrush;
            _arc.StrokeStartCap = CompositionStrokeCap.Round;
            _arc.StrokeEndCap = CompositionStrokeCap.Round;
            _arc.RotationAngleInDegrees = -90;
            _visual.Shapes.Add(_track);
            _visual.Shapes.Add(_arc);
            parent.Children.InsertAtTop(_visual);
        }

        public ShapeVisual Visual => _visual;

        public void Place(double centerX, double centerY, double diameter, double stroke)
        {
            var size = (float)Math.Ceiling(diameter + stroke + 2);
            _visual.Size = new Vector2(size, size);
            _visual.Offset = new Vector3(Snap(centerX - (size / 2)), Snap(centerY - (size / 2)), 0);
            var center = new Vector2(size / 2);
            var radius = new Vector2((float)(diameter / 2));
            foreach (var g in new[] { _trackGeometry, _arcGeometry })
            {
                g.Center = center;
                g.Radius = radius;
            }

            _track.StrokeThickness = (float)stroke;
            _arc.StrokeThickness = (float)stroke;
            _arc.CenterPoint = center;
            _level = null;
        }

        public void SetColor(Color color) => _arcBrush.Color = color;

        /// <summary>Shows a level from 0 to 1; <paramref name="ms"/> above 0 glides from the current value.</summary>
        public void SetLevel(double level, int ms = 0)
        {
            var l = (float)Math.Clamp(level, 0, 1);
            _arc.StrokeThickness = l <= 0.001f ? 0 : _track.StrokeThickness;
            if (_level is { } before && ms > 0 && Math.Abs(before - l) > 0.0001)
            {
                _arcGeometry.StartAnimation("TrimEnd", _owner.ScalarTo(l, ms));
            }
            else
            {
                _arcGeometry.StopAnimation("TrimEnd");
                _arcGeometry.TrimEnd = l;
            }

            _level = l;
        }
    }
}
