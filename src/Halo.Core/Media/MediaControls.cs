using Halo.Core.Island;

namespace Halo.Core.Media;

/// <summary>Transport commands (FR-MED-02).</summary>
public enum MediaCommand
{
    Previous,
    PlayPause,
    Next,

    /// <summary>Show the next media session (J-F01); only offered when several exist.</summary>
    SwitchSource,
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
    public const double SpacingDip = 54;
    public const double RowCenterDip = 156;

    /// <summary>
    /// The progress row of the media card (Visual Experience 2.0): the bar runs from 92 DIP to 368 DIP between the
    /// elapsed and remaining times, and the transport row is centred under it.
    /// </summary>
    public const double ColumnLeftDip = 92;
    public const double ColumnWidthDip = 276;

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

        if (media.SessionCount > 1)
        {
            // J-F01: at the right end of the row, clear of Next (266–302 DIP vs 332–368 DIP).
            buttons.Add(new MediaButton(MediaCommand.SwitchSource, e.Left + layout.Dip(ColumnLeftDip + ColumnWidthDip - (ButtonDip / 2)), centerY, size));
        }

        return buttons;
    }

    /// <summary>The seek band over the media progress bar (J-F02), or null when the session cannot seek.</summary>
    public static SeekBand? Seek(IslandLayout layout, MediaSnapshot? media)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (media is not { CanSeek: true, Timeline: not null })
        {
            return null;
        }

        var e = layout.Expanded;
        return new SeekBand(e.Left + layout.Dip(ColumnLeftDip), layout.Dip(ColumnWidthDip), e.Top + layout.Dip(ProgressCenterDip), layout.Dip(12));
    }

    /// <summary>Centre of the media progress bar below the expanded shape's top.</summary>
    public const double ProgressCenterDip = 130;

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
        MediaCommand.SwitchSource => "\uE8AB",
        MediaCommand.Previous => "",
        MediaCommand.Next => "",
        _ => playing ? "" : "",
    };
}

/// <summary>The clickable band over the progress bar: canvas pixels.</summary>
public sealed record SeekBand(double Left, double Width, double CenterY, double HalfHeight)
{
    public bool Contains(double x, double y) => Math.Abs(y - CenterY) <= HalfHeight && x >= Left - HalfHeight && x <= Left + Width + HalfHeight;

    /// <summary>The position for a pointer x, as a fraction from 0 to 1.</summary>
    public double FractionAt(double x) => Math.Clamp((x - Left) / Width, 0, 1);
}
