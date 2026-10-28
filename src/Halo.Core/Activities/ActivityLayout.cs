using Halo.Core.Island;

namespace Halo.Core.Activities;

/// <summary>Pages of the expanded island (FR-ISL-08): the primary activity, what is playing, the clock.</summary>
public enum IslandPage
{
    Activity,
    Media,
    Clock,
}

/// <summary>Which pages exist and which one the island opens on.</summary>
public static class IslandPages
{
    /// <summary>Pages in display order. The clock page always exists.</summary>
    public static IReadOnlyList<IslandPage> Available(bool hasActivity, bool hasMedia)
    {
        var pages = new List<IslandPage>(3);
        if (hasActivity)
        {
            pages.Add(IslandPage.Activity);
        }

        if (hasMedia)
        {
            pages.Add(IslandPage.Media);
        }

        pages.Add(IslandPage.Clock);
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
        _ => "clock",
    };
}

/// <summary>One clickable square in canvas pixels.</summary>
public readonly record struct HitSquare(int Index, double CenterX, double CenterY, double Size)
{
    public bool Contains(double x, double y) => Math.Abs(x - CenterX) <= Size / 2 && Math.Abs(y - CenterY) <= Size / 2;

    public RoundedBox Bounds => new(CenterX - (Size / 2), CenterY - (Size / 2), Size, Size, 0);
}

/// <summary>
/// Geometry of the expanded activity card and the page dots, shared by drawing, hit-testing and UI Automation.
/// Positions mirror the clock view so pages read as one layout (MOTION_SYSTEM.md: preserve layout).
/// </summary>
public static class ActivityLayout
{
    public const int MaxActions = 3;
    public const double ActionDip = 36;
    public const double ActionStepDip = 44;
    public const double ActionCenterYDip = 42;
    public const double DotsCenterYDip = 164;

    /// <summary>FR-A11Y-07: pointer targets of at least 24 DIP in the expanded island.</summary>
    public const double DotHitDip = 24;
    public const double DotStepDip = 24;

    /// <summary>Action buttons at the top right of the card, in reading order from left to right.</summary>
    public static IReadOnlyList<HitSquare> Actions(IslandLayout layout, int count)
    {
        ArgumentNullException.ThrowIfNull(layout);
        count = Math.Clamp(count, 0, MaxActions);
        var e = layout.Expanded;
        var size = layout.Dip(ActionDip);
        var y = e.Top + layout.Dip(ActionCenterYDip);
        var right = e.Right - layout.Dip(24) - (size / 2);
        var buttons = new List<HitSquare>(count);
        for (var i = 0; i < count; i++)
        {
            // Reading order left to right: the first action is the leftmost of the group.
            var x = right - (layout.Dip(ActionStepDip) * (count - 1 - i));
            buttons.Add(new HitSquare(i, x, y, size));
        }

        return buttons;
    }

    /// <summary>Page dots at the bottom right, under the status cluster; none when there is only one page.</summary>
    public static IReadOnlyList<HitSquare> Dots(IslandLayout layout, int pages)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (pages < 2)
        {
            return [];
        }

        var e = layout.Expanded;
        var step = layout.Dip(DotStepDip);
        var last = e.Right - layout.Dip(24) - (layout.Dip(DotHitDip) / 2);
        var first = last - (step * (pages - 1));
        var y = e.Top + layout.Dip(DotsCenterYDip);
        return Enumerable.Range(0, pages).Select(i => new HitSquare(i, first + (i * step), y, layout.Dip(DotHitDip))).ToList();
    }
}
