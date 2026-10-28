namespace Halo.Core.Theming;

/// <summary>What the compact pill's left slot shows (I-F02).</summary>
public enum PillSlot
{
    Empty,
    Activity,
    Media,
    Date,
}

/// <summary>
/// The user's choice of compact-pill content (I-F02) and the fixed priority between items: the primary activity, then
/// what is playing, then the date. Only one item fits beside the time, so the first enabled item that exists wins;
/// the pill never rotates or reorders by itself.
/// </summary>
public readonly record struct PillContent(bool Activity, bool Media, bool Date)
{
    public static PillContent Default { get; } = new(true, true, true);

    public PillSlot Slot(bool hasActivity, bool hasMedia) =>
        Activity && hasActivity ? PillSlot.Activity
        : Media && hasMedia ? PillSlot.Media
        : Date ? PillSlot.Date
        : PillSlot.Empty;
}
