using System.Globalization;
using Halo.Core.Activities;
using Halo.Core.Audio;
using Halo.Core.Clock;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Network;
using Halo.Core.Power;

namespace Halo.Core.Accessibility;

/// <summary>UI Automation role of an island element (mapped to a UIA control type by the app).</summary>
public enum AccessibleRole
{
    /// <summary>Read-only text (time, date, track, battery, network).</summary>
    Text,

    /// <summary>A push button (Invoke pattern).</summary>
    Button,

    /// <summary>A two-state button (Toggle pattern), for example mute.</summary>
    ToggleButton,

    /// <summary>A 0–100 slider (RangeValue pattern), for example volume.</summary>
    Slider,

    /// <summary>A read-only progress indicator (RangeValue pattern, read-only).</summary>
    ProgressBar,

    /// <summary>A transient activity announced as a live region (an alert, a timer completion).</summary>
    Status,
}

/// <summary>What activating an element does. The island routes each command through its single interaction path.</summary>
public enum AccessibleCommand
{
    None,
    MediaPrevious,
    MediaPlayPause,
    MediaNext,
    ToggleMute,
    SetVolume,

    /// <summary>One of the primary activity's actions; <see cref="AccessibleNode.Argument"/> is its index.</summary>
    ActivityAction,

    /// <summary>Show a page of the expanded island; <see cref="AccessibleNode.Argument"/> is the page index.</summary>
    ShowPage,

    /// <summary>Show the next media session (J-F01).</summary>
    MediaSwitch,

    /// <summary>Seek the media session (J-F02): the slider's value is the position in percent.</summary>
    Seek,

    /// <summary>
    /// A control of the page on screen (Visual Experience 2.0): chevrons, focus presets, Do Not Disturb, task and shelf
    /// buttons. <see cref="AccessibleNode.Argument"/> is its index in <c>PageControls.For</c> for the current state.
    /// </summary>
    PageControl,
}

/// <summary>
/// One element under the island in the UI Automation tree. <see cref="Id"/> is stable for as long as the element means
/// the same thing, so clients can keep a reference across content updates; a different meaning gets a different ID.
/// </summary>
/// <param name="Id">Stable identifier, also the UIA AutomationId.</param>
/// <param name="Role">Role (control type).</param>
/// <param name="Name">The accessible name. Never logged.</param>
/// <param name="Bounds">Bounds in canvas pixels (the app adds the window origin).</param>
/// <param name="Command">What Invoke, Toggle or SetValue does.</param>
public sealed record AccessibleNode(string Id, AccessibleRole Role, string Name, RoundedBox Bounds, AccessibleCommand Command = AccessibleCommand.None)
{
    /// <summary>Toggle state for <see cref="AccessibleRole.ToggleButton"/>.</summary>
    public bool? Toggled { get; init; }

    /// <summary>Value from 0 to 100 for <see cref="AccessibleRole.Slider"/> and <see cref="AccessibleRole.ProgressBar"/>.</summary>
    public double? Value { get; init; }

    /// <summary>Live-region politeness for <see cref="AccessibleRole.Status"/>: true = assertive (critical), false = polite.</summary>
    public bool Assertive { get; init; }

    /// <summary>Index for <see cref="AccessibleCommand.ActivityAction"/> and <see cref="AccessibleCommand.ShowPage"/>.</summary>
    public int Argument { get; init; }
}

/// <summary>Everything the accessibility tree is built from: one immutable snapshot of what the island shows.</summary>
public sealed record IslandAccessibilityInput(IslandLayout Layout, IslandState State, ClockReading Clock)
{
    public MediaSnapshot? Media { get; init; }

    /// <summary>The volume reading when the volume row is shown and a device is bound; null otherwise.</summary>
    public VolumeReading? Volume { get; init; }

    /// <summary>True when the volume row is shown but Windows reports no output device.</summary>
    public bool NoAudioDevice { get; init; }

    public PowerReading? Power { get; init; }

    public NetworkReading? Network { get; init; }

    /// <summary>The primary activity (timer, focus session, reminder…), or null. Private text must already be redacted.</summary>
    public IslandActivity? Activity { get; init; }

    /// <summary>The expanded page on screen; null = the default (activity, else media, else clock).</summary>
    public IslandPage? Page { get; init; }

    /// <summary>When the media progress label is computed.</summary>
    public DateTimeOffset Now { get; init; }

    /// <summary>Halo's Do Not Disturb is on.</summary>
    public bool QuietOn { get; init; }

    /// <summary>Tasks done today and still pending (the to-do page's readout).</summary>
    public int TasksDone { get; init; }

    public int TasksPending { get; init; }

    /// <summary>Task text is hidden because previews are off: only counts are shown and exposed.</summary>
    public bool TasksHidden { get; init; }

    /// <summary>Rows of the to-do page (empty while previews are hidden).</summary>
    public IReadOnlyList<(string Id, string Text)> TaskRows { get; init; } = [];

    public int ShelfCount { get; init; }

    /// <summary>Tiles of the shelf page.</summary>
    public IReadOnlyList<(string Id, string Name)> ShelfTiles { get; init; } = [];
}

/// <summary>
/// Builds the island's UI Automation children (FR-A11Y-01) from what is on screen. Pure: the same input gives the same
/// list, so the tree is unit-testable without a window. Rules:
/// <list type="bullet">
/// <item>Hidden (full screen, no override): no children, so nothing that cannot be seen is exposed.</item>
/// <item>Compact: no children except a transient alert, exposed as a status element (live region). The island element's
/// own name already carries the compact summary.</item>
/// <item>Expanded: one element per visible piece of information, and one per control that can be operated. Controls a media
/// session disables are absent, exactly as on screen.</item>
/// </list>
/// Element positions mirror <c>IslandVisuals.ApplyLayout</c> (same DIP values).
/// </summary>
public static class IslandAccessibility
{
    public const string AlertId = "Halo.Alert";
    public const string TimeId = "Halo.Time";

    /// <summary>The date and time in the header row of every page except the clock page.</summary>
    public const string HeaderTimeId = "Halo.Header.Time";
    public const string DateId = "Halo.Date";
    public const string DayProgressId = "Halo.DayProgress";
    public const string BatteryId = "Halo.Battery";
    public const string NetworkId = "Halo.Network";
    public const string MediaId = "Halo.Media";
    public const string MediaProgressId = "Halo.MediaProgress";
    public const string PreviousId = "Halo.Media.Previous";
    public const string PlayPauseId = "Halo.Media.PlayPause";
    public const string NextId = "Halo.Media.Next";
    public const string SwitchId = "Halo.Media.Switch";
    public const string MuteId = "Halo.Volume.Mute";
    public const string VolumeId = "Halo.Volume";
    public const string ActivityId = "Halo.Activity";
    public const string ActivityActionPrefix = PageControls.ActivityActionPrefix;
    public const string FocusId = "Halo.Focus";
    public const string TasksId = "Halo.Tasks";
    public const string ShelfId = "Halo.Shelf";
    public const string PagePrefix = "Halo.Page.";

    public static IReadOnlyList<AccessibleNode> Children(IslandAccessibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (layout, state) = (input.Layout, input.State);
        if (!state.Visible)
        {
            return [];
        }

        var nodes = new List<AccessibleNode>(12);
        if (state.Alert is { } alert && !state.IsExpanded)
        {
            nodes.Add(new AccessibleNode(AlertId, AccessibleRole.Status, alert.Content.Spoken, layout.Alert) { Assertive = alert.Priority == AlertPriority.Critical });
            return nodes;
        }

        if (state.Hidden || !state.IsExpanded)
        {
            return nodes;
        }

        var e = layout.Expanded;
        var pages = IslandPages.Available(input.Activity is not null, input.Media is not null);
        var page = IslandPages.Resolve(input.Page, pages);
        if (page == IslandPage.Activity && input.Activity is { } activity)
        {
            nodes.Add(new AccessibleNode(ActivityId, activity.Level is null ? AccessibleRole.Text : AccessibleRole.ProgressBar, activity.Accessible.Length > 0 ? activity.Accessible : activity.Title, ActivityLayout.Box(layout, 40, 40, 380, 84))
            {
                Value = activity.Level is { } level ? Math.Round(Math.Clamp(level, 0, 1) * 100) : null,
            });
        }
        else if (page == IslandPage.Media && input.Media is { } media)
        {
            nodes.Add(new AccessibleNode(MediaId, AccessibleRole.Text, media.AccessibleSummary, ActivityLayout.Box(layout, 40, 40, 380, 76)));
            if (media.Timeline is { } timeline)
            {
                var fraction = Math.Clamp(timeline.FractionAt(input.Now, media.IsPlaying), 0, 1);
                // J-F02: a seekable session makes the position a slider; otherwise it is a read-only progress bar.
                nodes.Add(new AccessibleNode(MediaProgressId, media.CanSeek ? AccessibleRole.Slider : AccessibleRole.ProgressBar, "Track position " + timeline.LabelAt(input.Now, media.IsPlaying), ActivityLayout.Box(layout, MediaControls.ColumnLeftDip, MediaControls.ProgressCenterDip - 12, MediaControls.ColumnWidthDip, 24), media.CanSeek ? AccessibleCommand.Seek : AccessibleCommand.None)
                {
                    Value = Math.Round(fraction * 100),
                });
            }

            foreach (var button in MediaControls.Layout(layout, media))
            {
                var (id, name, command) = button.Command switch
                {
                    MediaCommand.Previous => (PreviousId, "Previous track", AccessibleCommand.MediaPrevious),
                    MediaCommand.Next => (NextId, "Next track", AccessibleCommand.MediaNext),
                    MediaCommand.SwitchSource => (SwitchId, $"Switch media source ({media.SessionIndex} of {media.SessionCount})", AccessibleCommand.MediaSwitch),
                    _ => (PlayPauseId, media.IsPlaying ? "Pause" : "Play", AccessibleCommand.MediaPlayPause),
                };
                nodes.Add(new AccessibleNode(id, AccessibleRole.Button, name, new RoundedBox(button.CenterX - (button.Size / 2), button.CenterY - (button.Size / 2), button.Size, button.Size, 0), command));
            }
        }
        else if (page == IslandPage.Focus)
        {
            nodes.Add(new AccessibleNode(FocusId, AccessibleRole.Text, "Pomodoro: 25 minutes of focus, then a 5-minute break", ActivityLayout.Box(layout, 40, 40, 380, 84)));
        }
        else if (page == IslandPage.Tasks)
        {
            var summary = input.TasksHidden
                ? $"{input.TasksPending} tasks pending, previews hidden"
                : $"{input.TasksDone} of {input.TasksDone + input.TasksPending} tasks done today";
            nodes.Add(new AccessibleNode(TasksId, AccessibleRole.Text, summary, ActivityLayout.Box(layout, 40, 40, 190, 76)));
        }
        else if (page == IslandPage.Shelf)
        {
            var count = input.ShelfCount;
            nodes.Add(new AccessibleNode(ShelfId, AccessibleRole.Text, count == 0 ? "Nothing on the shelf" : count == 1 ? "1 item on the shelf" : $"{count} items on the shelf", ActivityLayout.Box(layout, 40, 40, 380, 90)));
        }
        else
        {
            nodes.Add(new AccessibleNode(TimeId, AccessibleRole.Text, input.Clock.TimeWithDesignator, ActivityLayout.Box(layout, 40, 36, 240, 66)));
            nodes.Add(new AccessibleNode(DateId, AccessibleRole.Text, input.Clock.LongDate, ActivityLayout.Box(layout, 42, 102, 240, 24)));
            nodes.Add(new AccessibleNode(DayProgressId, AccessibleRole.ProgressBar, input.Clock.DayProgressText, ActivityLayout.Box(layout, 42, 130, 240, 36))
            {
                Value = Math.Floor(input.Clock.DayFraction * 100),
            });
        }

        // The header row: date and time (not on the clock page, which shows them large), then the status cluster.
        if (page != IslandPage.Clock)
        {
            // The header row shows the date and time on every page except the clock page, which shows them large.
            nodes.Add(new AccessibleNode(HeaderTimeId, AccessibleRole.Text, $"{input.Clock.TimeWithDesignator}, {input.Clock.LongDate}", ActivityLayout.Box(layout, 130, 8, 200, 26)));
        }

        if (input.Network is { State: not NetworkState.Unknown } net)
        {
            nodes.Add(new AccessibleNode(NetworkId, AccessibleRole.Text, net.Spoken, new RoundedBox(e.Right - layout.Dip(24 + 40 + 3 + 64), e.Top + layout.Dip(8), layout.Dip(40), layout.Dip(26), 0)));
        }

        if (input.Power is { HasBattery: true } power)
        {
            nodes.Add(new AccessibleNode(BatteryId, AccessibleRole.Text, power.Spoken, new RoundedBox(e.Right - layout.Dip(24 + 64), e.Top + layout.Dip(8), layout.Dip(64), layout.Dip(26), 0)));
        }

        // Every chip, chevron, check button and tile of the page, from the same list that draws and hit-tests them.
        var context = new PageContext
        {
            Activity = input.Activity,
            QuietOn = input.QuietOn,
            TaskRows = input.TaskRows,
            ShelfTiles = input.ShelfTiles,
            PageCount = pages.Count,
        };
        var controls = PageControls.For(layout, page, context);
        for (var i = 0; i < controls.Count; i++)
        {
            var c = controls[i];
            nodes.Add(c.Command == PageCommand.ActivityAction
                ? new AccessibleNode(c.Id, AccessibleRole.Button, c.Spoken, c.Bounds, AccessibleCommand.ActivityAction) { Argument = c.Argument }
                : new AccessibleNode(c.Id, c.Toggled is null ? AccessibleRole.Button : AccessibleRole.ToggleButton, c.Spoken, c.Bounds, AccessibleCommand.PageControl) { Argument = i, Toggled = c.Toggled });
        }

        foreach (var dot in ActivityLayout.Dots(layout, pages.Count))
        {
            var p = pages[dot.Index];
            var name = p == page ? $"{Capitalise(IslandPages.Name(p))}, shown" : $"Show {IslandPages.Name(p)}";
            nodes.Add(new AccessibleNode(PagePrefix + p, AccessibleRole.Button, name, dot.Bounds, AccessibleCommand.ShowPage) { Argument = dot.Index });
        }

        if (VolumeRow.For(layout) is { } row)
        {
            if (input.Volume is { } volume)
            {
                nodes.Add(new AccessibleNode(MuteId, AccessibleRole.ToggleButton, "Mute", Square(row.MuteX, row.MuteY, row.MuteSize), AccessibleCommand.ToggleMute) { Toggled = volume.Muted });
                nodes.Add(new AccessibleNode(VolumeId, AccessibleRole.Slider, "Volume", new RoundedBox(row.TrackLeft, row.TrackY - row.HitHalfHeight, row.TrackWidth, row.HitHalfHeight * 2, 0), AccessibleCommand.SetVolume)
                {
                    Value = volume.Percent,
                });
            }
            else if (input.NoAudioDevice)
            {
                nodes.Add(new AccessibleNode(VolumeId, AccessibleRole.Text, "No audio output", new RoundedBox(row.TrackLeft, row.TrackY - row.HitHalfHeight, row.TrackWidth, row.HitHalfHeight * 2, 0)));
            }
        }

        return nodes;
    }

    /// <summary>A short spoken text for a 0–100 value ("64 percent").</summary>
    public static string Percent(double value) => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(value)} percent");

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static RoundedBox Box(IslandLayout layout, double leftDip, double topDip, double widthDip, double heightDip)
    {
        var e = layout.Expanded;
        return new RoundedBox(e.Left + layout.Dip(leftDip), e.Top + layout.Dip(topDip), layout.Dip(widthDip), layout.Dip(heightDip), 0);
    }

    private static RoundedBox Square(double centerX, double centerY, double size) => new(centerX - (size / 2), centerY - (size / 2), size, size, 0);
}
