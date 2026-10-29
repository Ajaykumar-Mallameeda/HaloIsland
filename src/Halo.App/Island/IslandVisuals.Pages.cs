using System.Globalization;
using System.Numerics;
using Halo.App.Interop;
using Halo.Core.Activities;
using Halo.Core.Audio;
using Halo.Core.Media;
using Halo.Core.Motion;
using Halo.Core.Theming;
using Windows.UI;
using Windows.UI.Composition;

namespace Halo.App.Island;

/// <summary>What the to-do page shows. Text is the user's own: it is only here while previews are allowed.</summary>
internal sealed record TasksView(int Done, int Pending, bool Hidden, IReadOnlyList<(string Id, string Text)> Rows)
{
    public static TasksView Empty { get; } = new(0, 0, false, []);
}

/// <summary>What the shelf page shows: the item count and the first tiles.</summary>
internal sealed record ShelfView(int Count, IReadOnlyList<(string Id, string Name, bool IsFile)> Tiles)
{
    public static ShelfView Empty { get; } = new(0, []);
}

/// <summary>
/// The pages of the expanded island (Visual Experience 2.0): each page is a group in the body that slides on a page
/// change, and every chip, chevron, check button and tile is drawn from the same <see cref="PageControl"/> list that the
/// window hit-tests and UI Automation exposes.
/// </summary>
internal sealed partial class IslandVisuals
{
    private const int ControlSlots = 9;

    // Activity page (B-F03/C-F02): a ring for timers, a tinted disc for other activities, the readout and the chips.
    private ContainerVisual _activityGroup = null!;
    private Ring _activityRing = null!;
    private SpriteVisual _activityDisc = null!;
    private CompositionColorBrush _activityDiscBrush = null!;
    private TextLine _activityGlyph = null!;
    private TextLine _activityTitle = null!;
    private TextLine _activityHeadline = null!;
    private TextLine _activitySubtitle = null!;
    private SpriteVisual _activityTrack = null!;
    private SpriteVisual _activityFill = null!;
    private CompositionColorBrush _activityFillBrush = null!;
    private IslandActivity? _activity;
    private int _activityOthers;
    private bool _showPrivate;
    private double? _activityLevelShown;

    // Focus page: the Pomodoro launcher.
    private ContainerVisual _focusGroup = null!;
    private Ring _focusRing = null!;
    private TextLine _focusGlyph = null!;
    private TextLine _focusTitle = null!;
    private TextLine _focusSubtitle = null!;

    // Clock page.
    private ContainerVisual _clockGroup = null!;
    private TextLine _bigTime = null!;
    private TextLine _longDate = null!;
    private TextLine _progressLabel = null!;
    private SpriteVisual _progressTrack = null!;
    private SpriteVisual _progressFill = null!;

    // Media page (M3-F01): cover, title and artist, the progress row with elapsed and remaining time, the transport row.
    private ContainerVisual _mediaGroup = null!;
    private SpriteVisual _artBackground = null!;
    private SpriteVisual _art = null!;
    private CompositionSurfaceBrush _artBrush = null!;
    private TextLine _artPlaceholder = null!;
    private TextLine _mediaTitle = null!;
    private TextLine _mediaArtist = null!;
    private TextLine _mediaElapsed = null!;
    private TextLine _mediaRemaining = null!;
    private SpriteVisual _mediaTrack = null!;
    private SpriteVisual _mediaFill = null!;
    private CompositionColorBrush _mediaFillBrush = null!;
    private SpriteVisual _mediaThumb = null!;
    private SpriteVisual _playDisc = null!;
    private CompositionColorBrush _playDiscBrush = null!;
    private TextLine _buttonPrevious = null!;
    private TextLine _buttonPlayPause = null!;
    private TextLine _buttonNext = null!;
    private TextLine _buttonSwitch = null!;
    private CompositionDrawingSurface? _artSurface;
    private MediaSnapshot? _media;
    private int _artShown = -1;
    private Color _mediaAccent;
    private int _mediaAccentFor = -2;

    // To-do page.
    private ContainerVisual _tasksGroup = null!;
    private TextLine _tasksReadout = null!;
    private TextLine _tasksCaption = null!;
    private SpriteVisual _taskPanel = null!;
    private TextLine[] _taskRows = null!;
    private TextLine _tasksEmptyGlyph = null!;
    private TextLine _tasksEmptyText = null!;
    private TasksView _tasks = TasksView.Empty;

    // Shelf page.
    private ContainerVisual _shelfGroup = null!;
    private ShapeVisual _shelfDash = null!;
    private CompositionRoundedRectangleGeometry _shelfDashGeometry = null!;
    private TextLine _shelfEmptyGlyph = null!;
    private TextLine _shelfEmptyText = null!;
    private TextLine _shelfEmptyHint = null!;
    private TextLine _shelfCount = null!;
    private ShelfView _shelf = ShelfView.Empty;

    // Controls: two chevrons in the chrome (they do not slide) and the page's own controls in the body.
    private ControlSlot[] _chevrons = null!;
    private ControlSlot[] _slots = null!;
    private IReadOnlyList<PageControl> _controls = [];
    private Tone _controlsTone;
    private TextFormat? _fmtChevron;
    private TextFormat? _fmtChipGlyph;
    private TextFormat? _fmtChipLabel;
    private TextFormat? _fmtCheck;
    private TextFormat? _fmtTileGlyph;
    private TextFormat? _fmtTileLabel;

    // Volume row (M3-F03): mute button, slider and value at the bottom of the expanded island.
    private ContainerVisual _volumeGroup = null!;
    private TextLine _volumeGlyph = null!;
    private TextLine _volumeValue = null!;
    private TextLine _volumeNone = null!;
    private SpriteVisual _volumeTrack = null!;
    private SpriteVisual _volumeFill = null!;
    private CompositionColorBrush _volumeFillBrush = null!;
    private SpriteVisual _volumeThumb = null!;
    private VolumeReading? _volume;
    private IReadOnlyList<MotionStep> _volumeLevel = [];

    private void CreatePages()
    {
        var c = _compositor;
        ContainerVisual Group()
        {
            var g = c.CreateContainerVisual();
            g.IsVisible = false;
            _body.Children.InsertAtTop(g);
            return g;
        }

        _activityGroup = Group();
        _activityDiscBrush = c.CreateColorBrush(default);
        _activityDisc = c.CreateSpriteVisual();
        _activityDisc.Brush = _activityDiscBrush;
        _activityGroup.Children.InsertAtTop(_activityDisc);
        _activityRing = new Ring(this, _activityGroup);
        _activityGlyph = new TextLine(this, _activityGroup);
        _activityTitle = new TextLine(this, _activityGroup);
        _activityHeadline = new TextLine(this, _activityGroup);
        _activitySubtitle = new TextLine(this, _activityGroup);
        _activityTrack = Bar(_activityGroup, p => p.Track);
        _activityFillBrush = c.CreateColorBrush(Fill);
        _activityFill = c.CreateSpriteVisual();
        _activityFill.Brush = _activityFillBrush;
        _activityGroup.Children.InsertAtTop(_activityFill);

        _focusGroup = Group();
        _focusRing = new Ring(this, _focusGroup);
        _focusGlyph = new TextLine(this, _focusGroup);
        _focusTitle = new TextLine(this, _focusGroup);
        _focusSubtitle = new TextLine(this, _focusGroup);

        _clockGroup = Group();
        _bigTime = new TextLine(this, _clockGroup);
        _longDate = new TextLine(this, _clockGroup);
        _progressLabel = new TextLine(this, _clockGroup);
        _progressTrack = Bar(_clockGroup, p => p.Track);
        _progressFill = Bar(_clockGroup, p => p.Bar);

        _mediaGroup = Group();
        _artBackground = Bar(_mediaGroup, p => p.Elevated);
        _artPlaceholder = new TextLine(this, _mediaGroup);
        _art = c.CreateSpriteVisual();
        _artBrush = c.CreateSurfaceBrush();
        _artBrush.Stretch = CompositionStretch.Fill;
        _art.Brush = _artBrush;
        _mediaGroup.Children.InsertAtTop(_art);
        _mediaTitle = new TextLine(this, _mediaGroup);
        _mediaArtist = new TextLine(this, _mediaGroup);
        _mediaElapsed = new TextLine(this, _mediaGroup);
        _mediaRemaining = new TextLine(this, _mediaGroup);
        _mediaTrack = Bar(_mediaGroup, p => p.Track);
        _mediaFillBrush = c.CreateColorBrush(Fill);
        _mediaFill = c.CreateSpriteVisual();
        _mediaFill.Brush = _mediaFillBrush;
        _mediaGroup.Children.InsertAtTop(_mediaFill);
        _mediaThumb = Bar(_mediaGroup, p => p.Primary);
        _playDiscBrush = c.CreateColorBrush(Fill);
        _playDisc = c.CreateSpriteVisual();
        _playDisc.Brush = _playDiscBrush;
        _mediaGroup.Children.InsertAtTop(_playDisc);
        _buttonPrevious = new TextLine(this, _mediaGroup);
        _buttonPlayPause = new TextLine(this, _mediaGroup);
        _buttonNext = new TextLine(this, _mediaGroup);
        _buttonSwitch = new TextLine(this, _mediaGroup);

        _tasksGroup = Group();
        _tasksReadout = new TextLine(this, _tasksGroup);
        _tasksCaption = new TextLine(this, _tasksGroup);
        _taskPanel = Bar(_tasksGroup, p => p.Elevated);
        _taskRows = [new TextLine(this, _tasksGroup), new TextLine(this, _tasksGroup), new TextLine(this, _tasksGroup)];
        _tasksEmptyGlyph = new TextLine(this, _tasksGroup);
        _tasksEmptyText = new TextLine(this, _tasksGroup);

        _shelfGroup = Group();
        _shelfDash = c.CreateShapeVisual();
        _shelfDashGeometry = c.CreateRoundedRectangleGeometry();
        var dash = c.CreateSpriteShape(_shelfDashGeometry);
        dash.StrokeBrush = Bound(p => p.Tertiary);
        dash.StrokeThickness = 1;
        dash.StrokeDashArray.Add(4);
        dash.StrokeDashArray.Add(4);
        _shelfDash.Shapes.Add(dash);
        _shelfGroup.Children.InsertAtTop(_shelfDash);
        _shelfEmptyGlyph = new TextLine(this, _shelfGroup);
        _shelfEmptyText = new TextLine(this, _shelfGroup);
        _shelfEmptyHint = new TextLine(this, _shelfGroup);
        _shelfCount = new TextLine(this, _shelfGroup);

        var controlsLayer = c.CreateContainerVisual();
        _body.Children.InsertAtTop(controlsLayer);
        _slots = Enumerable.Range(0, ControlSlots).Select(_ => new ControlSlot(this, controlsLayer)).ToArray();
        _chevrons = [new ControlSlot(this, _chrome), new ControlSlot(this, _chrome)];

        _volumeGroup = c.CreateContainerVisual();
        _expandedContent.Children.InsertAtTop(_volumeGroup);
        _volumeGlyph = new TextLine(this, _volumeGroup);
        _volumeValue = new TextLine(this, _volumeGroup);
        _volumeNone = new TextLine(this, _volumeGroup);
        _volumeTrack = Bar(_volumeGroup, p => p.Track);
        _volumeFillBrush = c.CreateColorBrush(Fill);
        _volumeFill = c.CreateSpriteVisual();
        _volumeFill.Brush = _volumeFillBrush;
        _volumeGroup.Children.InsertAtTop(_volumeFill);
        _volumeThumb = Bar(_volumeGroup, p => p.Primary);
    }

    private IEnumerable<TextLine> PageLines() =>
    [
        _activityGlyph, _activityTitle, _activityHeadline, _activitySubtitle,
        _focusGlyph, _focusTitle, _focusSubtitle,
        _bigTime, _longDate, _progressLabel,
        _artPlaceholder, _mediaTitle, _mediaArtist, _mediaElapsed, _mediaRemaining, _buttonPrevious, _buttonPlayPause, _buttonNext, _buttonSwitch,
        _tasksReadout, _tasksCaption, .. _taskRows, _tasksEmptyGlyph, _tasksEmptyText,
        _shelfEmptyGlyph, _shelfEmptyText, _shelfEmptyHint, _shelfCount,
        _volumeGlyph, _volumeValue, _volumeNone,
        .. _slots.SelectMany(s => s.Lines), .. _chevrons.SelectMany(s => s.Lines),
    ];

    private void PlacePages(Halo.Core.Island.IslandLayout layout)
    {
        // Positions are DIP inside the expanded shape (ActivityLayout and PageControls hold the shared ones).
        double X(double dip) => layout.Expanded.Left + layout.Dip(dip);
        double Y(double dip) => layout.Expanded.Top + layout.Dip(dip);
        double D(double dip) => layout.Dip(dip);
        var ringY = ActivityLayout.BodyCenterDip;
        var readout = ActivityLayout.ReadoutLeftDip;

        // Activity.
        _activityRing.Place(X(ActivityLayout.RingCenterXDip), Y(ringY), D(HaloDesign.RingDiameter), D(HaloDesign.RingStroke));
        var disc = Snap(D(60));
        _activityDisc.Size = new Vector2(disc, disc);
        _activityDisc.Offset = new Vector3(Snap(X(ActivityLayout.RingCenterXDip) - (disc / 2)), Snap(Y(ringY) - (disc / 2)), 0);
        RoundCorners(_activityDisc, disc / 2);
        _activityGlyph.Place(X(ActivityLayout.RingCenterXDip - 20), Y(ringY - 20), D(40), D(40), Format(IconFont, 22, Regular, TextAlign.Center), Primary);
        _activityTitle.Place(X(readout), Y(46), D(284), D(56), Format(DisplayFont, HaloDesign.ReadoutSize, Semibold, TextAlign.Leading, trim: true), Primary);
        _activityHeadline.Place(X(readout), Y(54), D(284), D(30), Format(DisplayFont, HaloDesign.HeadlineSize, Semibold, TextAlign.Leading, trim: true), Primary);
        _activitySubtitle.Place(X(readout + 2), Y(100), D(282), D(20), Format(TextFont, HaloDesign.BodySize, Regular, TextAlign.Leading, trim: true), Secondary);
        _activityTrack.Offset = new Vector3(Snap(X(readout + 2)), Snap(Y(118)), 0);
        _activityTrack.Size = new Vector2(Snap(D(282)), Snap(D(HaloDesign.BarThickness)));
        _activityFill.Offset = _activityTrack.Offset;
        RoundEnds(_activityTrack);

        // Focus.
        _focusRing.Place(X(ActivityLayout.RingCenterXDip), Y(ringY), D(HaloDesign.RingDiameter), D(HaloDesign.RingStroke));
        _focusGlyph.Place(X(ActivityLayout.RingCenterXDip - 20), Y(ringY - 20), D(40), D(40), Format(IconFont, 22, Regular, TextAlign.Center), Primary);
        _focusTitle.Place(X(readout), Y(46), D(284), D(56), Format(DisplayFont, HaloDesign.ReadoutSize, Semibold, TextAlign.Leading), Primary);
        _focusSubtitle.Place(X(readout + 2), Y(100), D(282), D(20), Format(TextFont, HaloDesign.BodySize, Regular, TextAlign.Leading, trim: true), Secondary);

        // Clock.
        _bigTime.Place(X(38), Y(34), D(250), D(68), Format(DisplayFont, HaloDesign.ClockSize, Semibold, TextAlign.Leading), Primary, smallSize: Px(18));
        _longDate.Place(X(42), Y(102), D(240), D(22), Format(TextFont, 15, Regular, TextAlign.Leading, trim: true), Secondary);
        _progressTrack.Offset = new Vector3(Snap(X(42)), Snap(Y(134)), 0);
        _progressTrack.Size = new Vector2(Snap(D(220)), Snap(D(HaloDesign.BarThickness)));
        _progressFill.Offset = _progressTrack.Offset;
        RoundEnds(_progressTrack);
        _progressLabel.Place(X(42), Y(144), D(240), D(18), Format(TextFont, HaloDesign.CaptionSize, Regular, TextAlign.Leading, trim: true), Tertiary);

        // Media.
        var artPx = Snap(D(72));
        _artBackground.Offset = new Vector3(Snap(X(HaloDesign.Inset)), Snap(Y(42)), 0);
        _artBackground.Size = new Vector2(artPx, artPx);
        RoundCorners(_artBackground, (float)D(HaloDesign.ArtRadius));
        _art.Offset = _artBackground.Offset;
        _art.Size = _artBackground.Size;
        RoundCorners(_art, (float)D(HaloDesign.ArtRadius));
        _artSurface?.Dispose();
        _artSurface = _text.CreateSurface((int)artPx, (int)artPx);
        _artBrush.Surface = _artSurface;
        _artShown = -1;
        _artPlaceholder.Place(X(HaloDesign.Inset), Y(42), D(72), D(72), Format(IconFont, 26, Regular, TextAlign.Center), Tertiary);
        _mediaTitle.Place(X(128), Y(48), D(292), D(24), Format(TextFont, HaloDesign.TitleSize, Semibold, TextAlign.Leading, trim: true), Primary);
        _mediaArtist.Place(X(128), Y(72), D(292), D(20), Format(TextFont, HaloDesign.BodySize, Regular, TextAlign.Leading, trim: true), Secondary);
        var trackH = Snap(D(4));
        _mediaTrack.Offset = new Vector3(Snap(X(MediaControls.ColumnLeftDip)), Snap(Y(MediaControls.ProgressCenterDip) - (trackH / 2)), 0);
        _mediaTrack.Size = new Vector2(Snap(D(MediaControls.ColumnWidthDip)), trackH);
        _mediaFill.Offset = _mediaTrack.Offset;
        RoundEnds(_mediaTrack);
        var thumb = Snap(D(9));
        _mediaThumb.Size = new Vector2(thumb, thumb);
        RoundCorners(_mediaThumb, thumb / 2);
        _mediaElapsed.Place(X(HaloDesign.Inset - 4), Y(MediaControls.ProgressCenterDip - 9), D(46), D(18), Format(TextFont, HaloDesign.CaptionSize, Semibold, TextAlign.Trailing), Secondary);
        _mediaRemaining.Place(X(MediaControls.ColumnLeftDip + MediaControls.ColumnWidthDip + 6), Y(MediaControls.ProgressCenterDip - 9), D(50), D(18), Format(TextFont, HaloDesign.CaptionSize, Semibold, TextAlign.Leading), Secondary);
        var all = new MediaSnapshot { Title = string.Empty, Artist = string.Empty, CanPrevious = true, CanPlayPause = true, CanNext = true, SessionCount = 2 };
        foreach (var b in MediaControls.Layout(layout, all))
        {
            var line = b.Command switch { MediaCommand.Previous => _buttonPrevious, MediaCommand.Next => _buttonNext, MediaCommand.SwitchSource => _buttonSwitch, _ => _buttonPlayPause };
            line.Place(b.CenterX - (b.Size / 2), b.CenterY - (b.Size / 2), b.Size, b.Size, Format(IconFont, b.Command == MediaCommand.PlayPause ? 15 : b.Command == MediaCommand.SwitchSource ? 14 : 17, Regular, TextAlign.Center), Primary);
            if (b.Command == MediaCommand.PlayPause)
            {
                var d = Snap(b.Size);
                _playDisc.Size = new Vector2(d, d);
                _playDisc.Offset = new Vector3(Snap(b.CenterX - (d / 2)), Snap(b.CenterY - (d / 2)), 0);
                RoundCorners(_playDisc, d / 2);
            }
        }

        // To-do.
        _tasksReadout.Place(X(HaloDesign.Inset), Y(44), D(190), D(54), Format(DisplayFont, HaloDesign.ReadoutSize, Semibold, TextAlign.Leading), Primary);
        _tasksCaption.Place(X(HaloDesign.Inset + 2), Y(98), D(190), D(20), Format(TextFont, HaloDesign.BodySize, Regular, TextAlign.Leading), Secondary);
        _taskPanel.Offset = new Vector3(Snap(X(PageControls.TaskPanelLeftDip)), Snap(Y(PageControls.TaskPanelTopDip)), 0);
        _taskPanel.Size = new Vector2(Snap(D(PageControls.TaskPanelWidthDip)), Snap(D(PageControls.TaskPanelHeightDip)));
        RoundCorners(_taskPanel, (float)D(HaloDesign.PanelRadius));
        for (var i = 0; i < _taskRows.Length; i++)
        {
            var row = PageControls.TaskRow(layout, i);
            _taskRows[i].Place(row.Left + D(40), row.Top, row.Width - D(48), row.Height, Format(TextFont, 12.5, Regular, TextAlign.Leading, trim: true), Primary);
        }

        _tasksEmptyGlyph.Place(X(PageControls.TaskPanelLeftDip), Y(64), D(PageControls.TaskPanelWidthDip), D(30), Format(IconFont, 20, Regular, TextAlign.Center), Tertiary);
        _tasksEmptyText.Place(X(PageControls.TaskPanelLeftDip + 8), Y(100), D(PageControls.TaskPanelWidthDip - 16), D(20), Format(TextFont, 12, Regular, TextAlign.Center, trim: true), Secondary);

        // Shelf.
        var panel = ActivityLayout.Box(layout, HaloDesign.Inset, PageControls.ShelfTopDip, IslandLayoutWidthInside, PageControls.ShelfHeightDip, HaloDesign.PanelRadius);
        _shelfDash.Size = new Vector2((float)(panel.Right + 2), (float)(panel.Bottom + 2));
        _shelfDashGeometry.Offset = new Vector2((float)panel.Left + 0.5f, (float)panel.Top + 0.5f);
        _shelfDashGeometry.Size = new Vector2((float)panel.Width - 1, (float)panel.Height - 1);
        _shelfDashGeometry.CornerRadius = new Vector2((float)panel.Radius);
        _shelfEmptyGlyph.Place(panel.Left, Y(52), panel.Width, D(28), Format(IconFont, 20, Regular, TextAlign.Center), Tertiary);
        _shelfEmptyText.Place(panel.Left, Y(82), panel.Width, D(20), Format(TextFont, HaloDesign.BodySize, Semibold, TextAlign.Center), Secondary);
        _shelfEmptyHint.Place(panel.Left, Y(102), panel.Width, D(18), Format(TextFont, HaloDesign.CaptionSize, Regular, TextAlign.Center), Tertiary);
        _shelfCount.Place(X(300), Y(PageControls.ShelfChipCenterDip - 10), D(120), D(20), Format(TextFont, HaloDesign.CaptionSize, Regular, TextAlign.Trailing), Tertiary);

        // Control formats; the slots re-place themselves on the next ShowControls.
        _fmtChevron = Format(IconFont, 9, Regular, TextAlign.Center);
        _fmtChipGlyph = Format(IconFont, 11.5, Regular, TextAlign.Center);
        _fmtChipLabel = Format(TextFont, HaloDesign.ChipSize, Semibold, TextAlign.Leading, trim: true);
        _fmtCheck = Format(IconFont, 16, Regular, TextAlign.Center);
        _fmtTileGlyph = Format(IconFont, 20, Regular, TextAlign.Center);
        _fmtTileLabel = Format(TextFont, HaloDesign.CaptionSize, Regular, TextAlign.Center, trim: true);
        foreach (var slot in _slots.Concat(_chevrons))
        {
            slot.Reset();
        }

        DrawControls();
    }

    private const double IslandLayoutWidthInside = Halo.Core.Island.IslandLayout.ExpandedWidthDip - (2 * HaloDesign.Inset);

    private void ShowPageGroups()
    {
        _activityGroup.IsVisible = _page == IslandPage.Activity;
        _mediaGroup.IsVisible = _page == IslandPage.Media;
        _clockGroup.IsVisible = _page == IslandPage.Clock;
        _focusGroup.IsVisible = _page == IslandPage.Focus;
        _tasksGroup.IsVisible = _page == IslandPage.Tasks;
        _shelfGroup.IsVisible = _page == IslandPage.Shelf;
    }

    private bool DrawPages()
    {
        var ok = DrawClockPage();
        ok &= DrawMedia();
        ok &= DrawVolume();
        ok &= DrawActivity();
        ok &= DrawFocus();
        ok &= DrawTasks();
        ok &= DrawShelf();
        ok &= DrawControls();
        return ok;
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

    /// <summary>The page's controls (chips, chevrons, task checks, shelf tiles) in the page's accent.</summary>
    public void ShowControls(IReadOnlyList<PageControl> controls, Tone tone)
    {
        (_controls, _controlsTone) = (controls, tone);
        _ = DrawControls();
    }

    /// <summary>The to-do page's counts and rows.</summary>
    public void ShowTasks(TasksView view)
    {
        if (view.Equals(_tasks) && view.Rows.SequenceEqual(_tasks.Rows))
        {
            return;
        }

        _tasks = view;
        _ = DrawTasks();
    }

    /// <summary>The shelf page's count and tiles.</summary>
    public void ShowShelf(ShelfView view)
    {
        if (view.Count == _shelf.Count && view.Tiles.SequenceEqual(_shelf.Tiles))
        {
            return;
        }

        _shelf = view;
        _ = DrawShelf();
        _ = DrawControls();
    }

    /// <summary>Feedback (MOTION_SYSTEM.md): a pressed control scales to 94 % and springs back on release.</summary>
    public void PressControl(string id, bool pressed, bool reducedMotion)
    {
        foreach (var slot in _slots.Concat(_chevrons))
        {
            if (string.Equals(slot.Id, id, StringComparison.Ordinal))
            {
                var s = pressed && !reducedMotion ? 0.94f : 1f;
                slot.Container.StartAnimation("Scale", Vector3To(new Vector3(s, s, 1), pressed ? 90 : 180));
            }
        }
    }

    private bool DrawControls()
    {
        if (_slots is null || _fmtChipLabel is null)
        {
            return true;
        }

        var ok = true;
        var (chevron, body) = (0, 0);
        foreach (var control in _controls)
        {
            if (control.Command is PageCommand.PreviousPage or PageCommand.NextPage)
            {
                if (chevron < _chevrons.Length)
                {
                    ok &= _chevrons[chevron++].Show(control, this);
                }
            }
            else if (body < _slots.Length)
            {
                ok &= _slots[body++].Show(control, this);
            }
        }

        for (var i = chevron; i < _chevrons.Length; i++)
        {
            _chevrons[i].Hide();
        }

        for (var i = body; i < _slots.Length; i++)
        {
            _slots[i].Hide();
        }

        return ok;
    }

    private bool DrawClockPage()
    {
        if (_clock is not { } clock)
        {
            return true;
        }

        var ok = _bigTime.Show(clock.TimeWithDesignator, clock.Designator.Length);
        ok &= _longDate.Show(clock.LongDate);
        ok &= _progressLabel.Show(clock.DayProgressText);
        _progressFill.Size = new Vector2(Math.Max(_progressTrack.Size.Y, Snap(_progressTrack.Size.X * clock.DayFraction)), _progressTrack.Size.Y);
        RoundEnds(_progressFill);
        return ok;
    }

    private bool DrawFocus()
    {
        var accent = Accent(Tone.Focus);
        _focusRing.SetColor(accent);
        _focusRing.SetLevel(1);
        _focusGlyph.SetColor(accent);
        var ok = _focusGlyph.Show("");
        ok &= _focusTitle.Show(CountdownLabel(Halo.Core.Timers.FocusPlan.Default.Work));
        ok &= _focusSubtitle.Show($"{Minutes(Halo.Core.Timers.FocusPlan.Default.Work)} focus · {Minutes(Halo.Core.Timers.FocusPlan.Default.ShortBreak)} break");
        return ok;
    }

    private static string CountdownLabel(TimeSpan t) => t.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    private static string Minutes(TimeSpan t) => string.Create(CultureInfo.CurrentCulture, $"{(int)t.TotalMinutes} min");

    private bool DrawTasks()
    {
        var t = _tasks;
        var ok = _tasksReadout.Show(string.Create(CultureInfo.CurrentCulture, $"{t.Done}/{t.Done + t.Pending}"));
        ok &= _tasksCaption.Show("done today");
        var rows = t.Hidden ? [] : t.Rows;
        for (var i = 0; i < _taskRows.Length; i++)
        {
            ok &= _taskRows[i].Show(i < rows.Count ? rows[i].Text : string.Empty);
        }

        var (glyph, text) = t.Hidden
            ? ("", t.Pending == 1 ? "1 pending · previews off" : $"{t.Pending} pending · previews off")
            : rows.Count == 0 ? ("", "No tasks") : (string.Empty, string.Empty);
        ok &= _tasksEmptyGlyph.Show(glyph);
        ok &= _tasksEmptyText.Show(text);
        return ok;
    }

    private bool DrawShelf()
    {
        var empty = _shelf.Count == 0;
        _shelfDash.IsVisible = empty;
        var ok = _shelfEmptyGlyph.Show(empty ? "" : string.Empty);
        ok &= _shelfEmptyText.Show(empty ? "Nothing on the shelf" : string.Empty);
        ok &= _shelfEmptyHint.Show(empty ? "Copy text or files, then pin them here" : string.Empty);
        ok &= _shelfCount.Show(empty ? string.Empty : _shelf.Count == 1 ? "1 item" : $"{_shelf.Count} items");
        return ok;
    }

    /// <summary>Activity text in the compact slot and the card; true unless the GPU device was lost.</summary>
    private bool DrawActivity()
    {
        var a = _activity;
        var ring = a is { Level: not null, Kind: ActivityKind.Timer or ActivityKind.Focus };
        var tone = a is null ? Tone.Neutral : HaloDesign.ToneOf(a);
        var accent = tone == Tone.Neutral ? Primary : Accent(tone);
        var inPill = a is not null && CompactSlot() == PillSlot.Activity;
        _compactActivity.IsVisible = inPill;
        _compactRing.Visual.IsVisible = inPill && ring;
        _compactRing.SetColor(accent);
        _compactLevelBrush.Color = accent;
        _compactActivityGlyph.SetColor(accent);
        var ok = _compactActivityGlyph.Show(inPill && !ring ? a!.Glyph : string.Empty);
        var compact = !inPill ? string.Empty : a!.CompactFor(_showPrivate) + (_activityOthers > 0 ? $"  +{_activityOthers}" : string.Empty);
        ok &= _compactActivityText.Show(compact);

        _activityRing.Visual.IsVisible = ring;
        _activityRing.SetColor(accent);
        _activityDisc.IsVisible = a is not null && !ring;
        _activityDiscBrush.Color = Color.FromArgb(46, accent.R, accent.G, accent.B);
        _activityFillBrush.Color = accent;
        _activityGlyph.SetColor(accent);
        ok &= _activityGlyph.Show(a?.Glyph ?? string.Empty);
        var title = a is null ? string.Empty : a.Privacy == ActivityPrivacy.Private && !_showPrivate ? a.RedactedText : a.Title;
        ok &= _activityTitle.Show(ring ? title : string.Empty);
        ok &= _activityHeadline.Show(ring ? string.Empty : title);
        _activitySubtitle.Visual.Offset = _activitySubtitle.Visual.Offset with { Y = Snap(_layout.Expanded.Top + _layout.Dip(ring ? 100 : 88)) };
        ok &= _activitySubtitle.Show(a?.Subtitle ?? string.Empty);
        return ok;
    }

    /// <summary>The activity's level: the rings for timers, bars for other activities, animated from the previous value.</summary>
    private void DrawActivityLevel(IReadOnlyList<MotionStep> steps)
    {
        var level = _activity?.Level;
        var ring = _activity is { Level: not null, Kind: ActivityKind.Timer or ActivityKind.Focus };
        _compactLevelTrack.IsVisible = level is not null && !ring;
        _compactLevelFill.IsVisible = level is not null && !ring;
        _activityTrack.IsVisible = level is not null && !ring;
        _activityFill.IsVisible = level is not null && !ring;
        if (level is not { } l || _activityLevelShown == l)
        {
            _activityLevelShown = level;
            return;
        }

        var ms = steps.Count > 0 ? Ms(steps[0].Spec.Duration) : 0;
        _activityLevelShown = l;
        l = Math.Clamp(l, 0, 1);
        _compactRing.SetLevel(l, ms);
        _activityRing.SetLevel(l, ms);
        foreach (var (track, fill) in new[] { (_compactLevelTrack, _compactLevelFill), (_activityTrack, _activityFill) })
        {
            var size = new Vector2(Math.Max(track.Size.Y, Snap(track.Size.X * l)), track.Size.Y);
            RoundEnds(fill, track.Size);
            if (ms > 0)
            {
                fill.StartAnimation("Size", Vector2To(size, ms));
            }
            else
            {
                fill.Size = size;
            }
        }
    }

    /// <summary>
    /// Shows the selected media session (null = none: the clock view and the date come back). Only changed lines are
    /// redrawn; the art surface and its accent only when the picture changes.
    /// </summary>
    public void ShowMedia(MediaSnapshot? media, DateTimeOffset now, bool reducedMotion = false)
    {
        // C-F05: a new track or session swaps the card and the pill's title together (no flash, no layout jump).
        // Playback-state and progress-only changes do not animate.
        var changedTrack = _media is { } before && media is { } after
            && (!string.Equals(before.Title, after.Title, StringComparison.Ordinal) || !string.Equals(before.Artist, after.Artist, StringComparison.Ordinal)
                || before.ArtVersion != after.ArtVersion || before.SessionIndex != after.SessionIndex);
        var playChanged = _media is { } b && media is { } a && b.IsPlaying != a.IsPlaying;
        _media = media;
        DrawText();
        UpdateMediaProgress(now);
        if (changedTrack)
        {
            Dip(_mediaGroup, MotionPlanner.Swap(reducedMotion));
            Dip(_compactMediaTitle.Visual, MotionPlanner.Swap(reducedMotion));
            Dip(_compactArt, MotionPlanner.Swap(reducedMotion));
        }
        else if (playChanged && !reducedMotion)
        {
            // Feedback: the play disc gives a small press-like pulse when playback starts or stops.
            _playDisc.CenterPoint = new Vector3(_playDisc.Size / 2, 0);
            var pulse = _compositor.CreateVector3KeyFrameAnimation();
            pulse.InsertKeyFrame(0.35f, new Vector3(0.9f, 0.9f, 1), _ease);
            pulse.InsertKeyFrame(1f, Vector3.One, _ease);
            pulse.Duration = TimeSpan.FromMilliseconds(260);
            _playDisc.StartAnimation("Scale", pulse);
        }
    }

    /// <summary>Moves the media progress bar and labels (called at most once a second, only while it is visible).</summary>
    public void UpdateMediaProgress(DateTimeOffset now)
    {
        var timeline = _media?.Timeline;
        _mediaTrack.IsVisible = timeline is not null;
        _mediaFill.IsVisible = timeline is not null;
        _mediaThumb.IsVisible = timeline is not null;
        if (timeline is null || _media is null)
        {
            _ = _mediaElapsed.Show(string.Empty);
            _ = _mediaRemaining.Show(string.Empty);
            return;
        }

        var fraction = Math.Clamp(timeline.FractionAt(now, _media.IsPlaying), 0, 1);
        var width = Snap(_mediaTrack.Size.X * fraction);
        _mediaFill.Size = new Vector2(Math.Max(_mediaTrack.Size.Y, width), _mediaTrack.Size.Y);
        RoundEnds(_mediaFill);
        var t = _mediaThumb.Size.X;
        _mediaThumb.Offset = new Vector3(Snap(_mediaTrack.Offset.X + width - (t / 2)), Snap(_mediaTrack.Offset.Y + (_mediaTrack.Size.Y / 2) - (t / 2)), 0);
        _ = _mediaElapsed.Show(timeline.ElapsedAt(now, _media.IsPlaying));
        _ = _mediaRemaining.Show(timeline.RemainingAt(now, _media.IsPlaying));
    }

    /// <summary>Media text and art; true unless the GPU device was lost.</summary>
    private bool DrawMedia()
    {
        var m = _media;
        var ok = true;
        var hasArt = m?.Art is { Count: > 0 };

        // The compact slot shows one thing: the primary activity, else what is playing, else the date.
        var compactMedia = m is not null && CompactSlot() == PillSlot.Media;
        _compactArt.IsVisible = compactMedia && hasArt;
        ok &= _compactMediaGlyph.Show(!compactMedia || hasArt ? string.Empty : m!.IsPlaying ? "" : "");
        ok &= _compactMediaTitle.Show(!compactMedia ? string.Empty : m!.Title.Length > 0 ? m.Title : m.Headline);
        ok &= _mediaTitle.Show(m is null ? string.Empty : m.Title.Length > 0 ? m.Title : "Unknown title");

        // J-F01: with several sessions, the app name tells them apart.
        var source = m is { SessionCount: > 1 } ? m.SourceName : string.Empty;
        ok &= _mediaArtist.Show(m is null ? string.Empty : source.Length == 0 ? m.Artist : m.Artist.Length == 0 ? source : $"{m.Artist} · {source}");

        // A restrained accent from the cover, computed once per picture (fixed cost), else the media tone.
        if (m is not null && m.ArtVersion != _mediaAccentFor)
        {
            _mediaAccentFor = m.ArtVersion;
            var fallback = HaloDesign.BaseAccent(Tone.Media);
            var art = hasArt && !_palette.HighContrast ? m.Art switch { byte[] bytes => bytes, { } list => [.. list], _ => [] } : [];
            var colour = _palette.HighContrast ? _palette.Bar : IslandPalette.ReadableAccent(HaloDesign.ArtAccent(art, m.ArtSize, fallback), _palette.Fill);
            _mediaAccent = ToColor(colour);
        }

        _mediaFillBrush.Color = _mediaAccent;
        _playDiscBrush.Color = _mediaAccent;
        _compactMediaGlyph.SetColor(_mediaAccent);
        _buttonPlayPause.SetColor(OnAccent(_mediaAccent));

        // FR-MED-02: only controls the session reports as enabled are shown.
        var playing = m?.IsPlaying == true;
        _playDisc.IsVisible = m?.CanPlayPause == true;
        ok &= _buttonPrevious.Show(m?.CanPrevious == true ? MediaControls.Glyph(MediaCommand.Previous, playing) : string.Empty);
        ok &= _buttonPlayPause.Show(m?.CanPlayPause == true ? MediaControls.Glyph(MediaCommand.PlayPause, playing) : string.Empty);
        ok &= _buttonNext.Show(m?.CanNext == true ? MediaControls.Glyph(MediaCommand.Next, playing) : string.Empty);
        ok &= _buttonSwitch.Show(m is { SessionCount: > 1 } ? MediaControls.Glyph(MediaCommand.SwitchSource, playing) : string.Empty);
        _art.IsVisible = hasArt;
        ok &= _artPlaceholder.Show(m is not null && !hasArt ? "" : string.Empty);
        if (hasArt && _artSurface is not null && m!.ArtVersion != _artShown)
        {
            var drawn = _text.TryDrawImage(_artSurface, [.. m.Art!], m.ArtSize);
            _artShown = drawn ? m.ArtVersion : -1;
            ok &= drawn;
        }

        return ok;
    }

    /// <summary>Shows the endpoint volume in the expanded row (null = no output device).</summary>
    /// <param name="reading">The endpoint volume, or null for no device.</param>
    /// <param name="animate">C-F04: changes made elsewhere glide (Level motion); a drag on the slider stays direct.</param>
    /// <param name="reducedMotion">The reduce-motion preference.</param>
    public void ShowVolume(VolumeReading? reading, bool animate = false, bool reducedMotion = false)
    {
        var before = _volume;
        _volume = reading;
        _volumeLevel = animate ? MotionPlanner.Level(before?.Percent, reading?.Percent, reducedMotion) : [];
        _ = DrawVolume();
        _volumeLevel = [];
        if (before is { } b && reading is { } r && b.Muted != r.Muted)
        {
            // Mute and unmute swap the glyph with the Swap dip, so the change is noticed without a pulse.
            Dip(_volumeGlyph.Visual, MotionPlanner.Swap(reducedMotion));
        }
    }

    private void PlaceVolume(Halo.Core.Island.IslandLayout layout)
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
        var ok = _volumeGlyph.Show(v?.Glyph ?? "");
        ok &= _volumeValue.Show(v is { } r ? r.Percent.ToString(CultureInfo.CurrentCulture) : string.Empty);
        ok &= _volumeNone.Show(v is null ? "No audio output" : string.Empty);
        _volumeTrack.IsVisible = v is not null;
        _volumeFill.IsVisible = v is not null;
        _volumeThumb.IsVisible = v is not null;
        if (v is { } reading)
        {
            // Muted: the bar keeps the level but dims, as in Windows' own flyout.
            _volumeFillBrush.Color = reading.Muted ? Tertiary : Fill;
            var width = _volumeTrack.Size.X * reading.Percent / 100f;
            var size = new Vector2(Math.Max(_volumeTrack.Size.Y, Snap(width)), _volumeTrack.Size.Y);
            var t = _volumeThumb.Size.X;
            var thumb = new Vector3(Snap(_volumeTrack.Offset.X + width - (t / 2)), Snap(_volumeTrack.Offset.Y + (_volumeTrack.Size.Y / 2) - (t / 2)), 0);
            RoundEnds(_volumeFill, _volumeTrack.Size);
            if (_volumeLevel is { Count: > 0 } level)
            {
                var ms = Ms(level[0].Spec.Duration);
                _volumeFill.StartAnimation("Size", Vector2To(size, ms));
                _volumeThumb.StartAnimation("Offset", Vector3To(thumb, ms));
            }
            else
            {
                _volumeFill.Size = size;
                _volumeThumb.Offset = thumb;
            }
        }

        return ok;
    }

    private enum ControlKind
    {
        Chevron,
        Chip,
        IconChip,
        Check,
        Tile,
    }

    /// <summary>
    /// One drawn control: a background (capsule, circle or tile), a glyph and a label, in a container that scales on
    /// press. It is placed again only when its kind or bounds change, and redraws text only when the text changes.
    /// </summary>
    private sealed class ControlSlot : IDisposable
    {
        private readonly SpriteVisual _background;
        private readonly CompositionColorBrush _backgroundBrush;
        private readonly TextLine _glyph;
        private readonly TextLine _label;
        private (ControlKind Kind, Halo.Core.Island.RoundedBox Bounds)? _placed;

        public ControlSlot(IslandVisuals owner, ContainerVisual parent)
        {
            var c = owner._compositor;
            Container = c.CreateContainerVisual();
            Container.IsVisible = false;
            parent.Children.InsertAtTop(Container);
            _backgroundBrush = c.CreateColorBrush(default);
            _background = c.CreateSpriteVisual();
            _background.Brush = _backgroundBrush;
            Container.Children.InsertAtTop(_background);
            _glyph = new TextLine(owner, Container);
            _label = new TextLine(owner, Container);
        }

        public ContainerVisual Container { get; }

        public string? Id { get; private set; }

        public IEnumerable<TextLine> Lines => [_glyph, _label];

        public void Reset() => _placed = null;

        /// <summary>The text lines are also disposed through <c>Lines()</c>; disposing twice is harmless.</summary>
        public void Dispose()
        {
            _glyph.Dispose();
            _label.Dispose();
        }

        public void Hide()
        {
            Container.IsVisible = false;
            Id = null;
        }

        public bool Show(PageControl control, IslandVisuals o)
        {
            var kind = control.Command switch
            {
                PageCommand.PreviousPage or PageCommand.NextPage => ControlKind.Chevron,
                PageCommand.CompleteTask => ControlKind.Check,
                PageCommand.CopyShelfItem => ControlKind.Tile,
                _ when !control.ShowLabel => ControlKind.IconChip,
                _ => ControlKind.Chip,
            };
            var b = control.Bounds;
            if (_placed != (kind, b))
            {
                Place(kind, b, o);
            }

            Id = control.Id;
            Container.IsVisible = true;

            // Colours: the primary chip and a toggle that is on are filled with the accent; the rest sit on a raised fill.
            var filled = control.Primary || control.Toggled == true;
            var accent = o.Accent(control.Toggled == true ? Tone.Quiet : o._controlsTone == Tone.Neutral ? Tone.Neutral : o._controlsTone);
            var raised = ToColor(o._palette.Elevated);
            var (background, foreground, glyphColour) = kind switch
            {
                ControlKind.Chevron => (raised, o.Secondary, o.Secondary),
                ControlKind.Check => (Color.FromArgb(0, 0, 0, 0), o.Secondary, o.Secondary),
                ControlKind.Tile => (raised, o.Secondary, o.Accent(Tone.Shelf)),
                _ when filled => (accent, OnAccent(accent), OnAccent(accent)),
                _ => (raised, o.Primary, o.Primary),
            };
            _backgroundBrush.Color = background;
            _glyph.SetColor(glyphColour);
            _label.SetColor(foreground);
            var glyph = kind switch
            {
                ControlKind.Check => "",
                ControlKind.Tile => o._shelf.Tiles.FirstOrDefault(t => string.Equals(t.Id, control.Key, StringComparison.Ordinal)).IsFile ? "" : "",
                _ => control.Glyph,
            };
            var ok = _glyph.Show(glyph);
            ok &= _label.Show(kind is ControlKind.Chip or ControlKind.Tile ? control.Label : string.Empty);
            return ok;
        }

        private void Place(ControlKind kind, Halo.Core.Island.RoundedBox b, IslandVisuals o)
        {
            _placed = (kind, b);
            var l = o._layout;
            Container.CenterPoint = new Vector3((float)(b.Left + (b.Width / 2)), (float)(b.Top + (b.Height / 2)), 0);
            switch (kind)
            {
                case ControlKind.Chevron:
                    var d = Snap(l.Dip(24));
                    _background.Size = new Vector2(d, d);
                    _background.Offset = new Vector3(Snap(b.CenterX - (d / 2)), Snap(b.Top + (b.Height / 2) - (d / 2)), 0);
                    o.RoundCorners(_background, d / 2);
                    _glyph.Place(b.Left, b.Top, b.Width, b.Height, o._fmtChevron!, o.Secondary);
                    _label.Place(b.Left, b.Top, 1, 1, o._fmtChipLabel!, o.Primary);
                    break;
                case ControlKind.Check:
                    _background.Size = Vector2.Zero;
                    _glyph.Place(b.Left, b.Top, b.Width, b.Height, o._fmtCheck!, o.Secondary);
                    _label.Place(b.Left, b.Top, 1, 1, o._fmtChipLabel!, o.Primary);
                    break;
                case ControlKind.IconChip:
                    _background.Size = new Vector2(Snap(b.Width), Snap(b.Height));
                    _background.Offset = new Vector3(Snap(b.Left), Snap(b.Top), 0);
                    o.RoundCorners(_background, (float)b.Radius);
                    _glyph.Place(b.Left, b.Top, b.Width, b.Height, o._fmtChipGlyph!, o.Primary);
                    _label.Place(b.Left, b.Top, 1, 1, o._fmtChipLabel!, o.Primary);
                    break;
                case ControlKind.Tile:
                    _background.Size = new Vector2(Snap(b.Width), Snap(b.Height));
                    _background.Offset = new Vector3(Snap(b.Left), Snap(b.Top), 0);
                    o.RoundCorners(_background, (float)b.Radius);
                    _glyph.Place(b.Left, b.Top + l.Dip(14), b.Width, l.Dip(30), o._fmtTileGlyph!, o.Secondary);
                    _label.Place(b.Left + l.Dip(6), b.Top + l.Dip(52), b.Width - l.Dip(12), l.Dip(20), o._fmtTileLabel!, o.Secondary);
                    break;
                default:
                    _background.Size = new Vector2(Snap(b.Width), Snap(b.Height));
                    _background.Offset = new Vector3(Snap(b.Left), Snap(b.Top), 0);
                    o.RoundCorners(_background, (float)b.Radius);
                    _glyph.Place(b.Left + l.Dip(10), b.Top, l.Dip(20), b.Height, o._fmtChipGlyph!, o.Primary);
                    _label.Place(b.Left + l.Dip(32), b.Top, b.Width - l.Dip(40), b.Height, o._fmtChipLabel!, o.Primary);
                    break;
            }
        }
    }
}
