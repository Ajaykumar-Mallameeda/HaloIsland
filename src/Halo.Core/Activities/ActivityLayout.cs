using Halo.Core.Island;
using Halo.Core.Theming;

namespace Halo.Core.Activities;

/// <summary>
/// Pages of the expanded island (FR-ISL-08). Live pages come first (the primary activity, what is playing), then the
/// pages that put Halo's own features inside the island itself (Visual Experience 2.0): the clock with Do Not Disturb,
/// the focus timer, the to-do list and the shelf.
/// </summary>
public enum IslandPage
{
    Activity,
    Media,
    Clock,
    Focus,
    Tasks,
    Shelf,
}

/// <summary>Which pages exist and which one the island opens on.</summary>
public static class IslandPages
{
    /// <summary>Pages in display order. The clock, focus, tasks and shelf pages always exist.</summary>
    public static IReadOnlyList<IslandPage> Available(bool hasActivity, bool hasMedia)
    {
        var pages = new List<IslandPage>(6);
        if (hasActivity)
        {
            pages.Add(IslandPage.Activity);
        }

        if (hasMedia)
        {
            pages.Add(IslandPage.Media);
        }

        pages.Add(IslandPage.Clock);
        pages.Add(IslandPage.Focus);
        pages.Add(IslandPage.Tasks);
        pages.Add(IslandPage.Shelf);
        return pages;
    }

    /// <summary>The page to show: the requested one while it exists, else the first available.</summary>
    public static IslandPage Resolve(IslandPage? requested, IReadOnlyList<IslandPage> available)
    {
        ArgumentNullException.ThrowIfNull(available);
        return requested is { } r && available.Contains(r) ? r : available[0];
    }

    /// <summary>The next (or previous) page, wrapping around.</summary>
    public static IslandPage Step(IslandPage current, IReadOnlyList<IslandPage> available, int direction)
    {
        ArgumentNullException.ThrowIfNull(available);
        var i = Math.Max(0, available.ToList().IndexOf(current));
        var n = available.Count;
        return available[(((i + direction) % n) + n) % n];
    }

    public static string Name(IslandPage page) => page switch
    {
        IslandPage.Activity => "activity",
        IslandPage.Media => "now playing",
        IslandPage.Focus => "focus timer",
        IslandPage.Tasks => "to-do list",
        IslandPage.Shelf => "shelf",
        _ => "clock",
    };

    /// <summary>The page's heading in the island's header row.</summary>
    public static string Title(IslandPage page, IslandActivity? activity) => page switch
    {
        IslandPage.Activity => activity?.Kind switch
        {
            ActivityKind.Timer => "Timer",
            ActivityKind.Focus => "Focus",
            ActivityKind.Reminder => "Reminder",
            ActivityKind.Task => "Task",
            ActivityKind.Notification => "Notification",
            ActivityKind.Calendar => "Calendar",
            _ => "Live",
        },
        IslandPage.Media => "Now Playing",
        IslandPage.Focus => "Pomodoro",
        IslandPage.Tasks => "To-do",
        IslandPage.Shelf => "Shelf",
        _ => "Today",
    };

    /// <summary>The header glyph (icon font) of a page.</summary>
    public static string Glyph(IslandPage page, IslandActivity? activity) => page switch
    {
        IslandPage.Activity => activity?.Glyph ?? "",
        IslandPage.Media => "",
        IslandPage.Focus => "",
        IslandPage.Tasks => "",
        IslandPage.Shelf => "",
        _ => "",
    };

    /// <summary>The colour of a page's header glyph, ring and primary chip.</summary>
    public static Tone ToneOf(IslandPage page, IslandActivity? activity) => page switch
    {
        IslandPage.Activity when activity is not null => HaloDesign.ToneOf(activity),
        IslandPage.Media => Tone.Media,
        IslandPage.Focus => Tone.Focus,
        IslandPage.Tasks => Tone.Tasks,
        IslandPage.Shelf => Tone.Shelf,
        _ => Tone.Neutral,
    };
}

/// <summary>One clickable square in canvas pixels.</summary>
public readonly record struct HitSquare(int Index, double CenterX, double CenterY, double Size)
{
    public bool Contains(double x, double y) => Math.Abs(x - CenterX) <= Size / 2 && Math.Abs(y - CenterY) <= Size / 2;

    public RoundedBox Bounds => new(CenterX - (Size / 2), CenterY - (Size / 2), Size, Size, 0);
}

/// <summary>
/// Geometry of the expanded island shared by drawing, hit-testing and UI Automation (Visual Experience 2.0). Every page
/// has the same frame: a header row (page title, date and time, status), chevrons at the sides, page dots centred at the
/// bottom, and a body between the chevrons. All values are DIP below and right of the expanded shape's top left.
/// </summary>
public static class ActivityLayout
{
    public const int MaxActions = 3;

    public const double HeaderCenterDip = 21;
    public const double HeaderHeightDip = 26;
    public const double BodyCenterDip = 102;

    /// <summary>Chevron buttons: 28 DIP targets (FR-A11Y-07 asks for at least 24), centred 17 DIP from the sides.</summary>
    public const double ChevronDip = 28;
    public const double ChevronInsetDip = 17;

    public const double DotsCenterYDip = 184;

    /// <summary>FR-A11Y-07: pointer targets of at least 24 DIP in the expanded island.</summary>
    public const double DotHitDip = 24;
    public const double DotStepDip = 24;

    // Chips (activity actions, page commands) and the readout beside the ring.
    public const double ChipWidthDip = 88;
    public const double ChipRowCenterDip = 142;
    public const double ReadoutLeftDip = 136;
    public const double RingCenterXDip = 79;

    /// <summary>A box in canvas pixels from DIP values relative to the expanded shape.</summary>
    public static RoundedBox Box(IslandLayout layout, double leftDip, double topDip, double widthDip, double heightDip, double radiusDip = 0)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var e = layout.Expanded;
        return new RoundedBox(e.Left + layout.Dip(leftDip), e.Top + layout.Dip(topDip), layout.Dip(widthDip), layout.Dip(heightDip), layout.Dip(radiusDip));
    }

    /// <summary>A chip centred vertically on <paramref name="centerYDip"/>.</summary>
    public static RoundedBox Chip(IslandLayout layout, double leftDip, double centerYDip, double widthDip) =>
        Box(layout, leftDip, centerYDip - (HaloDesign.ChipHeight / 2), widthDip, HaloDesign.ChipHeight, HaloDesign.ChipRadius);

    /// <summary>The activity's action chips in one row under the readout, in reading order from left to right.</summary>
    public static IReadOnlyList<HitSquare> Actions(IslandLayout layout, int count)
    {
        ArgumentNullException.ThrowIfNull(layout);
        count = Math.Clamp(count, 0, MaxActions);
        var buttons = new List<HitSquare>(count);
        for (var i = 0; i < count; i++)
        {
            var chip = ActionChip(layout, i);
            buttons.Add(new HitSquare(i, chip.CenterX, chip.Top + (chip.Height / 2), chip.Height));
        }

        return buttons;
    }

    /// <summary>The chip of action <paramref name="index"/> (0 = leftmost) in a row of fixed-width chips.</summary>
    public static RoundedBox ActionChip(IslandLayout layout, int index) =>
        Chip(layout, ReadoutLeftDip + (index * (ChipWidthDip + HaloDesign.Gap)), ChipRowCenterDip, ChipWidthDip);

    /// <summary>Width of the chip row beside the ring (from the readout to the right inset).</summary>
    public const double ChipRowWidthDip = IslandLayout.ExpandedWidthDip - HaloDesign.Inset - ReadoutLeftDip;

    /// <summary>An icon-only chip: a 44 DIP capsule (above the 24 DIP minimum target).</summary>
    public const double IconChipDip = 44;

    /// <summary>
    /// A conservative width estimate for a chip label (12 DIP semibold): DirectWrite measures on the UI thread only, and
    /// the same geometry must also serve hit-testing and UI Automation, so Core estimates generously instead.
    /// </summary>
    public static double LabelWidthDip(string label) => (label?.Length ?? 0) * 6.9;

    /// <summary>
    /// A row of chips sized to their labels, left to right from <paramref name="leftDip"/>. When the labels do not fit in
    /// <paramref name="widthDip"/>, the widest secondary chips become icon-only until they do (the first chip, the page's
    /// main action, keeps its label). Returns each chip's box and whether its label shows.
    /// </summary>
    public static IReadOnlyList<(RoundedBox Box, bool ShowLabel)> ChipRow(IslandLayout layout, IReadOnlyList<string> labels, double leftDip, double centerYDip, double widthDip)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var widths = labels.Select(l => Math.Max(64, LabelWidthDip(l) + 44)).ToArray();
        var shows = Enumerable.Repeat(true, labels.Count).ToArray();
        double Total() => widths.Sum() + (HaloDesign.Gap * Math.Max(0, labels.Count - 1));
        while (Total() > widthDip)
        {
            var widest = -1;
            for (var i = 1; i < widths.Length; i++)
            {
                if (shows[i] && (widest < 0 || widths[i] > widths[widest]))
                {
                    widest = i;
                }
            }

            if (widest < 0)
            {
                break;
            }

            (widths[widest], shows[widest]) = (IconChipDip, false);
        }

        var row = new List<(RoundedBox, bool)>(labels.Count);
        var x = leftDip;
        for (var i = 0; i < widths.Length; i++)
        {
            row.Add((Chip(layout, x, centerYDip, widths[i]), shows[i]));
            x += widths[i] + HaloDesign.Gap;
        }

        return row;
    }

    /// <summary>Page dots centred at the bottom; none when there is only one page.</summary>
    public static IReadOnlyList<HitSquare> Dots(IslandLayout layout, int pages)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (pages < 2)
        {
            return [];
        }

        var e = layout.Expanded;
        var step = layout.Dip(DotStepDip);
        var first = e.CenterX - (step * (pages - 1) / 2);
        var y = e.Top + layout.Dip(DotsCenterYDip);
        return Enumerable.Range(0, pages).Select(i => new HitSquare(i, first + (i * step), y, layout.Dip(DotHitDip))).ToList();
    }

    /// <summary>The previous (-1) or next (+1) chevron.</summary>
    public static HitSquare Chevron(IslandLayout layout, int direction)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var e = layout.Expanded;
        var x = direction < 0 ? e.Left + layout.Dip(ChevronInsetDip) : e.Right - layout.Dip(ChevronInsetDip);
        return new HitSquare(direction < 0 ? 0 : 1, x, e.Top + layout.Dip(BodyCenterDip), layout.Dip(ChevronDip));
    }
}

/// <summary>What a control inside a page does. The window routes each command through its single interaction path.</summary>
public enum PageCommand
{
    PreviousPage,
    NextPage,

    /// <summary>One of the primary activity's actions; <see cref="PageControl.Argument"/> is its index.</summary>
    ActivityAction,
    StartFocus,
    StartBreak,
    StartTimer,
    ToggleQuiet,
    OpenSettings,
    AddTask,
    OpenTasks,

    /// <summary>Complete the task <see cref="PageControl.Key"/>.</summary>
    CompleteTask,
    PinClipboard,
    OpenShelf,

    /// <summary>Copy the shelf item <see cref="PageControl.Key"/> back to the clipboard.</summary>
    CopyShelfItem,
}

/// <summary>
/// One control on a page: where it is, what it does and how it reads. Pure data, so the same list draws the chips,
/// hit-tests the pointer and builds the UI Automation buttons.
/// </summary>
/// <param name="Id">Stable identifier, also the UIA AutomationId.</param>
/// <param name="Command">What activating it does.</param>
/// <param name="Label">Visible text and accessible name.</param>
/// <param name="Glyph">Icon-font glyph.</param>
/// <param name="Bounds">Hit area and drawn chip, in canvas pixels.</param>
public sealed record PageControl(string Id, PageCommand Command, string Label, string Glyph, RoundedBox Bounds)
{
    /// <summary>Index for <see cref="PageCommand.ActivityAction"/>, minutes for the timer presets.</summary>
    public int Argument { get; init; }

    /// <summary>Task or shelf item ID for commands that act on one.</summary>
    public string? Key { get; init; }

    /// <summary>The page's main action: drawn filled with the page's accent.</summary>
    public bool Primary { get; init; }

    /// <summary>Toggle state (Do Not Disturb), or null for a push button.</summary>
    public bool? Toggled { get; init; }

    /// <summary>What a screen reader hears when it differs from the label ("Copy notes.txt").</summary>
    public string? Accessible { get; init; }

    /// <summary>False for an icon-only chip (the label did not fit); the label is still the accessible name.</summary>
    public bool ShowLabel { get; init; } = true;

    public string Spoken => Accessible ?? Label;
}

/// <summary>What the page controls depend on: a snapshot of the island's state.</summary>
public sealed record PageContext
{
    public IslandActivity? Activity { get; init; }

    public bool QuietOn { get; init; }

    /// <summary>Tasks shown as rows on the to-do page (empty while previews are hidden): ID and visible text.</summary>
    public IReadOnlyList<(string Id, string Text)> TaskRows { get; init; } = [];

    /// <summary>Shelf items shown as tiles: ID and visible name.</summary>
    public IReadOnlyList<(string Id, string Name)> ShelfTiles { get; init; } = [];

    public int PageCount { get; init; }
}

/// <summary>The controls of each page (Visual Experience 2.0).</summary>
public static class PageControls
{
    public const string PreviousId = "Halo.Page.Previous";
    public const string NextId = "Halo.Page.Next";
    public const string ActivityActionPrefix = "Halo.Activity.Action.";
    public const int MaxTaskRows = 3;
    public const int MaxShelfTiles = 4;

    // Tasks page: a panel on the right with up to three rows.
    public const double TaskPanelLeftDip = 236;
    public const double TaskPanelTopDip = 42;
    public const double TaskPanelWidthDip = 184;
    public const double TaskPanelHeightDip = 120;
    public const double TaskRowTopDip = 48;
    public const double TaskRowHeightDip = 36;

    // Shelf page: four tiles across the body, chips under them.
    public const double ShelfTopDip = 42;
    public const double ShelfHeightDip = 86;
    public const double ShelfChipCenterDip = 150;

    // Clock page: a column of chips on the right.
    public const double ClockChipLeftDip = 290;
    public const double ClockChipWidthDip = 130;

    /// <summary>Focus presets: one focus session, a 5-minute break, a 15-minute timer.</summary>
    public const int BreakMinutes = 5;
    public const int TimerMinutes = 15;

    public static IReadOnlyList<PageControl> For(IslandLayout layout, IslandPage page, PageContext context)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(context);
        var controls = new List<PageControl>(9);
        if (context.PageCount > 1)
        {
            controls.Add(new PageControl(PreviousId, PageCommand.PreviousPage, "Previous page", "", ActivityLayout.Chevron(layout, -1).Bounds));
            controls.Add(new PageControl(NextId, PageCommand.NextPage, "Next page", "", ActivityLayout.Chevron(layout, 1).Bounds));
        }

        var inset = HaloDesign.Inset;
        switch (page)
        {
            case IslandPage.Activity when context.Activity is { } activity:
                var actions = activity.Actions.Take(ActivityLayout.MaxActions).ToList();
                var row = ActivityLayout.ChipRow(layout, [.. actions.Select(a => a.Label)], ActivityLayout.ReadoutLeftDip, ActivityLayout.ChipRowCenterDip, ActivityLayout.ChipRowWidthDip);
                for (var i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];
                    controls.Add(new PageControl(ActivityActionPrefix + action.Id, PageCommand.ActivityAction, action.Label, action.Glyph, row[i].Box)
                    {
                        Argument = i,
                        Primary = i == 0,
                        ShowLabel = row[i].ShowLabel,
                    });
                }

                break;
            case IslandPage.Focus:
                controls.Add(new PageControl("Halo.Focus.Start", PageCommand.StartFocus, "Focus", "", ActivityLayout.ActionChip(layout, 0))
                {
                    Primary = true,
                    Accessible = "Start a focus session: 25 minutes of focus, then a 5-minute break",
                });
                controls.Add(new PageControl("Halo.Focus.Break", PageCommand.StartBreak, "Break", "", ActivityLayout.ActionChip(layout, 1))
                {
                    Argument = BreakMinutes,
                    Accessible = "Start a 5-minute break timer",
                });
                controls.Add(new PageControl("Halo.Focus.Timer", PageCommand.StartTimer, $"{TimerMinutes} min", "", ActivityLayout.ActionChip(layout, 2))
                {
                    Argument = TimerMinutes,
                    Accessible = "Start a 15-minute timer",
                });
                break;
            case IslandPage.Clock:
                controls.Add(new PageControl("Halo.Quiet", PageCommand.ToggleQuiet, "Do Not Disturb", "", ActivityLayout.Chip(layout, ClockChipLeftDip, 64, ClockChipWidthDip))
                {
                    Toggled = context.QuietOn,
                });
                controls.Add(new PageControl("Halo.Settings", PageCommand.OpenSettings, "Settings", "", ActivityLayout.Chip(layout, ClockChipLeftDip, 104, ClockChipWidthDip)));
                break;
            case IslandPage.Tasks:
                controls.Add(new PageControl("Halo.Tasks.Add", PageCommand.AddTask, "Add task", "", ActivityLayout.Chip(layout, inset, ActivityLayout.ChipRowCenterDip, 96)) { Primary = true });
                controls.Add(new PageControl("Halo.Tasks.Open", PageCommand.OpenTasks, "All", "", ActivityLayout.Chip(layout, inset + 104, ActivityLayout.ChipRowCenterDip, 76))
                {
                    Accessible = "Open tasks and reminders",
                });
                for (var i = 0; i < Math.Min(context.TaskRows.Count, MaxTaskRows); i++)
                {
                    var (id, text) = context.TaskRows[i];
                    controls.Add(new PageControl("Halo.Tasks.Done." + i, PageCommand.CompleteTask, "Done", "", TaskCheck(layout, i))
                    {
                        Key = id,
                        Accessible = "Mark done: " + text,
                    });
                }

                break;
            case IslandPage.Shelf:
                controls.Add(new PageControl("Halo.Shelf.Pin", PageCommand.PinClipboard, "Pin clipboard", "", ActivityLayout.Chip(layout, inset, ShelfChipCenterDip, 128)) { Primary = true });
                controls.Add(new PageControl("Halo.Shelf.Open", PageCommand.OpenShelf, "Open shelf", "", ActivityLayout.Chip(layout, inset + 136, ShelfChipCenterDip, 112)));
                for (var i = 0; i < Math.Min(context.ShelfTiles.Count, MaxShelfTiles); i++)
                {
                    var (id, name) = context.ShelfTiles[i];
                    controls.Add(new PageControl("Halo.Shelf.Item." + i, PageCommand.CopyShelfItem, name, string.Empty, ShelfTile(layout, i))
                    {
                        Key = id,
                        Accessible = "Copy " + name,
                    });
                }

                break;
        }

        return controls;
    }

    /// <summary>The control under a canvas point, or null.</summary>
    public static PageControl? HitTest(IReadOnlyList<PageControl> controls, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(controls);
        foreach (var c in controls)
        {
            var b = c.Bounds;
            if (x >= b.Left && x <= b.Right && y >= b.Top && y <= b.Bottom)
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>Row <paramref name="index"/> of the task panel (the whole row; the check button is its left part).</summary>
    public static RoundedBox TaskRow(IslandLayout layout, int index) =>
        ActivityLayout.Box(layout, TaskPanelLeftDip, TaskRowTopDip + (index * TaskRowHeightDip), TaskPanelWidthDip, TaskRowHeightDip);

    /// <summary>The 28 DIP check button at the start of a task row.</summary>
    public static RoundedBox TaskCheck(IslandLayout layout, int index) =>
        ActivityLayout.Box(layout, TaskPanelLeftDip + 6, TaskRowTopDip + (index * TaskRowHeightDip) + 4, 28, 28, 14);

    /// <summary>Tile <paramref name="index"/> of the shelf (four across the body).</summary>
    public static RoundedBox ShelfTile(IslandLayout layout, int index)
    {
        var width = (IslandLayout.ExpandedWidthDip - (2 * HaloDesign.Inset) - (3 * HaloDesign.Gap)) / MaxShelfTiles;
        return ActivityLayout.Box(layout, HaloDesign.Inset + (index * (width + HaloDesign.Gap)), ShelfTopDip, width, ShelfHeightDip, 12);
    }
}
