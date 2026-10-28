using System.Runtime.InteropServices;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Clock lifecycle on the running island (M1-F03). Locking the session or switching the display off would disturb
/// the owner, so the test sends the same window messages Windows sends for them.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmWtsSessionChange = 0x02B1;
    private const uint WmTimeChange = 0x001E;
    private const uint WmSettingChange = 0x001A;

    [Fact]
    public void ClockPausesWhileLockedAndRefreshesOnUnlockAndTimeChanges()
    {
        using var island = IslandProcess.Start();

        Assert.True(PostMessageW(island.Hwnd, WmWtsSessionChange, 7, 1)); // WTS_SESSION_LOCK
        var paused = island.WaitFor("clock");
        Assert.Equal("paused", paused["reason"]!.GetValue<string>());

        Assert.True(PostMessageW(island.Hwnd, WmWtsSessionChange, 8, 1)); // WTS_SESSION_UNLOCK
        Assert.Equal("resumed", island.WaitFor("clock", skip: 1)["reason"]!.GetValue<string>());

        Assert.True(PostMessageW(island.Hwnd, WmTimeChange, 0, 0));
        Assert.Equal("time_change", island.WaitFor("clock", skip: 2)["reason"]!.GetValue<string>());

        // WM_SETTINGCHANGE("intl") carries a string, so it must be sent (the system marshals it), not posted.
        Assert.NotEqual(0, SendMessageTimeoutW(island.Hwnd, WmSettingChange, 0, "intl", 2, 2000, out _));
        Assert.Equal("intl", island.WaitFor("clock", skip: 3)["reason"]!.GetValue<string>());
        Assert.False(island.HasExited);
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeoutW(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);
#pragma warning restore SYSLIB1054
}
