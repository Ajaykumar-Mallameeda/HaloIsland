using System.Runtime.InteropServices;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Display handling on the one available display (M2-F04). The work area is changed for real with a temporary top app
/// bar owned by the test; display-mode and DPI changes are reserved for the owner, so those use the messages only.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmDisplayChange = 0x007E;
    private const uint WmPowerBroadcast = 0x0218;

    [Fact]
    public void TheIslandFollowsARealWorkAreaChangeAndComesBack()
    {
        using var island = IslandProcess.Start();
        var before = ExpectedLayout();
        Assert.True(GetWindowRect(island.Hwnd, out var start));
        Assert.Equal(before.Canvas.Top, start.Top);

        using var window = TestWindow.Create();
        var bar = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Hwnd = window.Hwnd };
        try
        {
            Assert.NotEqual(0u, (uint)SHAppBarMessage(0, ref bar)); // ABM_NEW
            bar.Edge = 1; // ABE_TOP
            bar.Rect = new Rect { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = 60 };
            _ = SHAppBarMessage(2, ref bar); // ABM_QUERYPOS
            bar.Rect.Bottom = bar.Rect.Top + 60;
            _ = SHAppBarMessage(3, ref bar); // ABM_SETPOS: the shell shrinks the work area

            var moved = ExpectedLayout();
            Assert.True(moved.Canvas.Top > before.Canvas.Top, "the shell did not reserve the app bar space");
            Assert.True(WaitUntil(() => GetWindowRect(island.Hwnd, out var r) && r.Top == moved.Canvas.Top), "the island did not move below the app bar");
            AssertRegion(island.Hwnd, moved.Compact);
        }
        finally
        {
            _ = SHAppBarMessage(1, ref bar); // ABM_REMOVE: give the space back
        }

        Assert.True(WaitUntil(() => GetWindowRect(island.Hwnd, out var r) && r.Top == before.Canvas.Top), "the island did not return when the app bar went away");
    }

    [Fact]
    public void RepeatedDisplayNotificationsDoNotRebuildAndResumeRePlaces()
    {
        using var island = IslandProcess.Start();
        var placements = island.Count("placement");

        Assert.True(PostMessageW(island.Hwnd, WmDisplayChange, 32, 0));
        Assert.True(PostMessageW(island.Hwnd, WmPowerBroadcast, 0x12, 0)); // PBT_APMRESUMEAUTOMATIC
        island.WaitFor("clock");
        Assert.True(WaitUntil(() => island.Count("placement.unchanged") >= 2));

        // Same display, same layout: nothing was moved, rebuilt or redrawn.
        Assert.Equal(placements, island.Count("placement"));
        Assert.Contains(island.Events("placement.unchanged"), e => e["reason"]?.GetValue<string>() == "resume");
        Assert.Contains(island.Events("placement.unchanged"), e => e["reason"]?.GetValue<string>() == "display_changed");
        Assert.False(island.HasExited);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public nint Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Rect;
        public nint LParam;
    }

#pragma warning disable SYSLIB1054
    [DllImport("shell32.dll")]
    private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);
#pragma warning restore SYSLIB1054
}
