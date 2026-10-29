using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Live Activity Engine 2.0 on the real desktop: the thin-line idle island, its small pointer zone, the line → pill →
/// open → line cycle without focus theft, live activities growing the line into the pill and back, and the detached
/// bubble swapping activities.
/// </summary>
public sealed partial class IslandAppTests
{
    private const string LineSettings = "{ \"schemaVersion\": 1, \"idleLine\": true }";
    private const uint WmTestFocusLine = 0x8000 + 55;
    private const uint WmTestTimerLine = 0x8000 + 53;
    private const uint WmTestNetworkLine = 0x8000 + 52;

    [Fact]
    public void TheIdleIslandIsAThinLineWhoseSmallZoneLetsClicksBelowThrough()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start(settingsJson: LineSettings);

        AssertRegion(island.Hwnd, layout.LineHit);
        var x = layout.Canvas.Left + (int)layout.LineHit.CenterX;
        Assert.Equal(island.Hwnd, RootAt(x, layout.Canvas.Top + 1)); // the top edge above the line is the island
        Assert.NotEqual(island.Hwnd, RootAt(x, layout.Canvas.Top + (int)layout.LineHit.Bottom + 2)); // just below: the app
        Assert.NotEqual(island.Hwnd, RootAt(layout.Canvas.Left + (int)layout.LineHit.Left - 3, layout.Canvas.Top + 1)); // beside it too
        Assert.NotEqual(island.Hwnd, RootAt(pointer.PillX, pointer.PillY)); // where the pill would be: still the app
        Assert.Equal(0, island.Count("transition.start")); // nothing animates while idle
    }

    [Fact]
    public void ThePointerGrowsTheLineIntoThePillOpensItAndItReturnsToTheLine()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start(settingsJson: LineSettings);
        var foreground = SettledForeground();

        MoveTo(layout.Canvas.Left + (int)layout.LineHit.CenterX, layout.Canvas.Top + 2);
        Assert.Equal("compact", island.WaitFor("transition.start")["to"]!.GetValue<string>());
        Assert.Equal("expanded", island.WaitFor("transition.start", skip: 1)["to"]!.GetValue<string>()); // after the dwell
        // The growth may still be running when the dwell opens the island; it is then interrupted and logs no end of its
        // own, so wait for the open's end rather than counting ends.
        Assert.True(WaitUntil(() => island.Events("transition.end").Any(e => e["dir"]?.GetValue<string>() == "expand")), island.Trace());
        AssertRegion(island.Hwnd, layout.Expanded);
        var ends = island.Count("transition.end");

        MoveTo(pointer.PillX, pointer.OutsideY);
        Assert.Equal("line", island.WaitFor("transition.start", skip: 2)["to"]!.GetValue<string>()); // after the grace
        Assert.True(WaitUntil(() => island.Count("transition.end") > ends), island.Trace());
        AssertRegion(island.Hwnd, layout.LineHit);
        AssertForeground(foreground);
        Assert.Equal(3, island.Count("transition.start")); // no flicker: exactly line → pill → open → line
    }

    [Fact]
    public void ALiveActivityGrowsTheLineIntoThePillAndItsEndReturnsTheLine()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start(settingsJson: LineSettings);
        island.WaitFor("network");

        Assert.True(PostMessageW(island.Hwnd, WmTestNetworkLine, 1, 0)); // offline (state 1, kind none)
        Assert.True(WaitUntil(() => island.Events("live").Any(e => e["lead"]?.GetValue<string>() == "network")), island.Trace());
        Assert.True(WaitUntil(() => island.Events("transition.start").Any(e => e["to"]?.GetValue<string>() == "compact"), 8000), island.Trace());
        Assert.True(WaitUntil(() => RegionMatches(island.Hwnd, layout.Compact.Reach()), 8000), "the pill's region"); // after the 3 s "Offline" alert

        Assert.True(PostMessageW(island.Hwnd, WmTestNetworkLine, 3 | (1 << 8), 0)); // connected over Wi-Fi
        Assert.True(WaitUntil(() => island.Events("live").Any(e => e["lead"]?.GetValue<string>() == "none" && e["reason"]?.GetValue<string>() == "network")), island.Trace());
        Assert.True(WaitUntil(() => RegionMatches(island.Hwnd, layout.LineHit), 10000), island.Trace()); // after the "Connected" alert
    }

    [Fact]
    public void ClickingTheDetachedBubbleSwapsActivitiesWithoutOpeningOrTakingFocus()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start(settingsJson: LineSettings);
        var foreground = SettledForeground();

        Assert.True(PostMessageW(island.Hwnd, WmTestTimerLine, 300, 0));
        Assert.True(PostMessageW(island.Hwnd, WmTestFocusLine, 600, 0));
        Assert.True(WaitUntil(() => island.Events("live").Any(e => e["secondary"]?.GetValue<string>() == "focus")), island.Trace());
        Assert.True(WaitUntil(() => RegionContains(island.Hwnd, layout.Minimal), 3000), "the bubble takes input");
        Thread.Sleep(400);

        var m = layout.Minimal;
        Click(layout.Canvas.Left + (int)(m.Left + (m.Width / 2)), layout.Canvas.Top + (int)(m.Top + (m.Height / 2)));
        Assert.Equal("focus", island.WaitFor("live.select")["provider"]!.GetValue<string>());
        Assert.True(WaitUntil(() => island.Events("live").Any(e => e["lead"]?.GetValue<string>() == "focus" && e["secondary"]?.GetValue<string>() == "timer")), island.Trace());
        MoveTo(pointer.PillX, pointer.OutsideY);
        Thread.Sleep(300);
        Assert.DoesNotContain(island.Events("transition.start"), e => e["to"]?.GetValue<string>() == "expanded");
        AssertForeground(foreground);
    }

    private static bool RegionMatches(nint hwnd, Halo.Core.Island.RoundedBox shape) =>
        GetWindowRgnBox(hwnd, out var box) != 0
        && Math.Abs(box.Left - (int)Math.Floor(shape.Left)) <= 1 && Math.Abs(box.Top - (int)Math.Floor(shape.Top)) <= 1
        && Math.Abs(box.Right - (int)Math.Ceiling(shape.Right)) <= 2 && Math.Abs(box.Bottom - (int)Math.Ceiling(shape.Bottom)) <= 2;

    private static bool RegionContains(nint hwnd, Halo.Core.Island.RoundedBox shape) =>
        GetWindowRgnBox(hwnd, out var box) != 0 && box.Right >= (int)shape.Right - 1;
}
