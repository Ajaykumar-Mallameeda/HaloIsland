using System.Globalization;
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

    /// <summary>Extra elements contributed by activities (timer, notifications) while expanded, in display order.</summary>
    public IReadOnlyList<AccessibleNode> Activities { get; init; } = [];

    /// <summary>When the media progress label is computed.</summary>
    public DateTimeOffset Now { get; init; }
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
    public const string DateId = "Halo.Date";
    public const string DayProgressId = "Halo.DayProgress";
    public const string BatteryId = "Halo.Battery";
    public const string NetworkId = "Halo.Network";
    public const string MediaId = "Halo.Media";
    public const string MediaProgressId = "Halo.MediaProgress";
    public const string PreviousId = "Halo.Media.Previous";
    public const string PlayPauseId = "Halo.Media.PlayPause";
    public const string NextId = "Halo.Media.Next";
    public const string MuteId = "Halo.Volume.Mute";
    public const string VolumeId = "Halo.Volume";

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
        if (input.Media is { } media)
        {
            nodes.Add(new AccessibleNode(MediaId, AccessibleRole.Text, media.AccessibleSummary, Box(layout, 112, 24, 284, 48)));
            if (media.Timeline is { } timeline)
            {
                var fraction = Math.Clamp(timeline.FractionAt(input.Now, media.IsPlaying), 0, 1);
                nodes.Add(new AccessibleNode(MediaProgressId, AccessibleRole.ProgressBar, "Track position " + timeline.LabelAt(input.Now, media.IsPlaying), Box(layout, 112, 82, 284, 32))
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
                    _ => (PlayPauseId, media.IsPlaying ? "Pause" : "Play", AccessibleCommand.MediaPlayPause),
                };
                nodes.Add(new AccessibleNode(id, AccessibleRole.Button, name, new RoundedBox(button.CenterX - (button.Size / 2), button.CenterY - (button.Size / 2), button.Size, button.Size, 0), command));
            }
        }
        else
        {
            nodes.Add(new AccessibleNode(TimeId, AccessibleRole.Text, input.Clock.TimeWithDesignator, Box(layout, 24, 16, 260, 62)));
            nodes.Add(new AccessibleNode(DateId, AccessibleRole.Text, input.Clock.LongDate, Box(layout, 26, 78, 368, 24)));
            nodes.Add(new AccessibleNode(DayProgressId, AccessibleRole.ProgressBar, input.Clock.DayProgressText, Box(layout, 26, 112, 368, 40))
            {
                Value = Math.Floor(input.Clock.DayFraction * 100),
            });
            if (input.Network is { State: not NetworkState.Unknown } net)
            {
                nodes.Add(new AccessibleNode(NetworkId, AccessibleRole.Text, net.Spoken, new RoundedBox(e.Right - layout.Dip(24 + 38 + 3 + 64), e.Top + layout.Dip(24), layout.Dip(40), layout.Dip(24), 0)));
            }

            if (input.Power is { HasBattery: true } power)
            {
                nodes.Add(new AccessibleNode(BatteryId, AccessibleRole.Text, power.Spoken, new RoundedBox(e.Right - layout.Dip(24 + 62), e.Top + layout.Dip(24), layout.Dip(62), layout.Dip(24), 0)));
            }
        }

        nodes.AddRange(input.Activities);

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

    private static RoundedBox Box(IslandLayout layout, double leftDip, double topDip, double widthDip, double heightDip)
    {
        var e = layout.Expanded;
        return new RoundedBox(e.Left + layout.Dip(leftDip), e.Top + layout.Dip(topDip), layout.Dip(widthDip), layout.Dip(heightDip), 0);
    }

    private static RoundedBox Square(double centerX, double centerY, double size) => new(centerX - (size / 2), centerY - (size / 2), size, size, 0);
}
