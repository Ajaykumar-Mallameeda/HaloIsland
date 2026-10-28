using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Transient alerts on the real desktop, raised through the island's diagnostics-only test message (the same
/// controller path the system integrations use).
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestAlert = 0x8000 + 50;

    [Fact]
    public void AlertAppearsAsAWiderPillWithContentAndExpires()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();
        var foreground = SettledForeground();

        RaiseTestAlert(island, priority: 2, durationMs: 1500);
        Assert.Equal("shown", island.WaitFor("alert")["state"]!.GetValue<string>());
        island.WaitFor("transition.end");
        AssertRegion(island.Hwnd, layout.Alert);
        Thread.Sleep(150);
        AssertDrawn(layout, layout.Alert, minBright: 60);

        Assert.Equal("ended", island.WaitFor("alert", skip: 1)["state"]!.GetValue<string>());
        island.WaitFor("transition.end", skip: 1);
        AssertRegion(island.Hwnd, layout.Compact);
        AssertForeground(foreground);
    }

    [Fact]
    public void AlertWaitsWhileExpandedAndShowsAfterCollapseIfStillFresh()
    {
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var pattern = automation.FromHandle(island.Hwnd).Patterns.ExpandCollapse.Pattern;
        pattern.Expand();
        island.WaitFor("transition.end");

        RaiseTestAlert(island, priority: 2, durationMs: 1500);
        Assert.Equal("queued", island.WaitFor("alert")["state"]!.GetValue<string>());
        Thread.Sleep(300);
        AssertRegion(island.Hwnd, layout.Expanded); // the user's expanded island is not interrupted

        pattern.Collapse();
        Assert.Equal("shown", island.WaitFor("alert", skip: 1)["state"]!.GetValue<string>());
        Assert.Equal("ended", island.WaitFor("alert", skip: 2)["state"]!.GetValue<string>());
    }

    [Fact]
    public void OnlyAllowedAlertsAppearOverAFullscreenApp()
    {
        using var island = IslandProcess.Start();
        using var window = TestWindow.Create();
        window.Fullscreen();
        window.ClickToForeground();
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)));

        RaiseTestAlert(island, priority: 2, durationMs: 1000);
        Assert.Equal("queued", island.WaitFor("alert")["state"]!.GetValue<string>());
        Assert.False(IsWindowVisible(island.Hwnd));

        RaiseTestAlert(island, priority: 3, durationMs: 1000, allowedOverFullscreen: true);
        Assert.True(WaitUntil(() => IsWindowVisible(island.Hwnd)), "a critical alert did not appear over full screen");
        AssertForeground(window.Hwnd);
        Assert.True(WaitUntil(() => !IsWindowVisible(island.Hwnd)), "the island stayed visible after the critical alert");
    }

    private static void RaiseTestAlert(IslandProcess island, int priority, int durationMs, bool allowedOverFullscreen = false) =>
        Assert.True(PostMessageW(island.Hwnd, WmTestAlert, priority, ((nint)durationMs << 8) | (allowedOverFullscreen ? 1 : 0)));

    /// <summary>Some bright (text or glyph) pixels inside a shape, as in <c>TextIsDrawnInsidePill</c>.</summary>
    private static void AssertDrawn(Halo.Core.Island.IslandLayout layout, Halo.Core.Island.RoundedBox shape, int minBright)
    {
        var rect = new System.Drawing.Rectangle(layout.Canvas.Left + (int)shape.Left, layout.Canvas.Top + (int)shape.Top, (int)shape.Width, (int)shape.Height);
        var bitmap = FlaUI.Core.Capturing.Capture.Rectangle(rect).Bitmap;
        var bright = 0;
        for (var y = 3; y < bitmap.Height - 3; y++)
        {
            for (var x = (int)(shape.Height / 2); x < bitmap.Width - (int)(shape.Height / 2); x++)
            {
                var p = bitmap.GetPixel(x, y);
                bright += (p.R + p.G + p.B) / 3 > 160 ? 1 : 0;
            }
        }

        Assert.True(bright >= minBright, $"too few drawn pixels: {bright}");
    }
}
