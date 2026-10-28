using Halo.Core.Island;

namespace Halo.Core.Media;

/// <summary>Transport commands (FR-MED-02).</summary>
public enum MediaCommand
{
    Previous,
    PlayPause,
    Next,
}

/// <summary>One transport button: its command and its square hit area in canvas pixels.</summary>
public readonly record struct MediaButton(MediaCommand Command, double CenterX, double CenterY, double Size)
{
    public bool Contains(double x, double y) =>
        Math.Abs(x - CenterX) <= Size / 2 && Math.Abs(y - CenterY) <= Size / 2;
}

/// <summary>
/// Where the transport buttons sit in the expanded media card, and which are offered. Buttons are 36 DIP squares
/// (FR-A11Y-07 asks for ≥ 24 DIP targets) on one row under the progress bar. A control the session reports as
/// disabled is not shown at all, so nothing on screen does nothing.
/// </summary>
public static class MediaControls
{
    public const double ButtonDip = 36;
    public const double SpacingDip = 56;
    public const double RowCenterDip = 136;

    /// <summary>Content column of the media card: from 112 DIP to 396 DIP inside the expanded shape.</summary>
    public const double ColumnLeftDip = 112;
    public const double ColumnWidthDip = 284;

    public static IReadOnlyList<MediaButton> Layout(IslandLayout layout, MediaSnapshot? media)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (media is null)
        {
            return [];
        }

        var e = layout.Expanded;
        var centerX = e.Left + layout.Dip(ColumnLeftDip + (ColumnWidthDip / 2));
        var centerY = e.Top + layout.Dip(RowCenterDip);
        var size = layout.Dip(ButtonDip);
        var step = layout.Dip(SpacingDip);
        var buttons = new List<MediaButton>(3);
        if (media.CanPrevious)
        {
            buttons.Add(new MediaButton(MediaCommand.Previous, centerX - step, centerY, size));
        }

        if (media.CanPlayPause)
        {
            buttons.Add(new MediaButton(MediaCommand.PlayPause, centerX, centerY, size));
        }

        if (media.CanNext)
        {
            buttons.Add(new MediaButton(MediaCommand.Next, centerX + step, centerY, size));
        }

        return buttons;
    }

    /// <summary>The button under a canvas point, if any.</summary>
    public static MediaCommand? HitTest(IReadOnlyList<MediaButton> buttons, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        foreach (var b in buttons)
        {
            if (b.Contains(x, y))
            {
                return b.Command;
            }
        }

        return null;
    }

    /// <summary>Icon-font glyph for a button (Segoe Fluent Icons); play/pause shows the action it will take.</summary>
    public static string Glyph(MediaCommand command, bool playing) => command switch
    {
        MediaCommand.Previous => "",
        MediaCommand.Next => "",
        _ => playing ? "" : "",
    };
}
