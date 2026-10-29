using Halo.Core.Island;
using Xunit;

namespace Halo.App.Tests;

/// <summary>I-F04 on the desktop: the island moves by the chosen offset, keeps its input region and still toggles by click.</summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void AnOffsetMovesTheIslandAndItStillTakesClicksThere()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"islandOffsetDip\": -240 }");
        var ready = island.WaitFor("ready");
        var centred = ExpectedLayout();
        var scale = centred.Scale;
        var expectedLeft = Math.Max(WorkLeft(), centred.Canvas.Left - (int)Math.Round(240 * scale, MidpointRounding.AwayFromZero));
        Assert.Equal(expectedLeft, ready["left"]!.GetValue<int>());
        Assert.True(GetWindowRect(island.Hwnd, out var rect));
        Assert.Equal(expectedLeft, rect.Left);

        var foreground = SettledForeground();
        GetCursorPos(out var cursor);
        try
        {
            var x = expectedLeft + (int)centred.Compact.CenterX;
            var y = centred.Canvas.Top + (int)(centred.Compact.Top + (centred.Compact.Height / 2));
            Assert.Equal(island.Hwnd, RootAt(x, y));
            Assert.NotEqual(island.Hwnd, RootAt(centred.Canvas.Left + (int)centred.Compact.CenterX + (int)(300 * scale), y)); // the old centre is free
            Click(x, y);
            Assert.Equal("expand", island.WaitFor("transition.end")["dir"]!.GetValue<string>());
            Click(x, y);
            island.WaitFor("transition.end", skip: 1);
            AssertForeground(foreground);
        }
        finally
        {
            SetCursorPos(cursor.X, cursor.Y);
        }
    }

    private static int WorkLeft()
    {
        var info = new MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        _ = GetMonitorInfoW(MonitorFromPoint(default, 1), ref info);
        return info.Work.Left;
    }
}
