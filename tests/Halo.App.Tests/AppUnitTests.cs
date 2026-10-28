using System.IO;
using Halo.App.Interop;
using Halo.App.Island;
using Xunit;

namespace Halo.App.Tests;

public class TrayInteropTests
{
    [Fact]
    public unsafe void NotifyIconDataMatchesTheWindowsLayout()
    {
        // sizeof(NOTIFYICONDATAW) on x64; a wrong size makes Shell_NotifyIconW fail.
        Assert.Equal(976, sizeof(Native.NotifyIconData));
        Assert.Equal(32, sizeof(Native.IconInfo));
        Assert.Equal(40, sizeof(Native.BitmapInfoHeader));
    }

    [Fact]
    public void CallbackMessageDoesNotCollideWithOtherIslandMessages()
    {
        Assert.NotEqual(IslandWindow.WmCommandFromInstance, TrayIcon.CallbackMessage);
        Assert.InRange(TrayIcon.CallbackMessage, Native.WmApp, 0xBFFFu);
    }
}

public class DiagnosticsLogTests
{
    [Fact]
    public void WritesOneJsonObjectPerEventWithNumbersAndTokens()
    {
        using var writer = new StringWriter();
        var log = new DiagnosticsLog(writer);

        log.Write("transition.start", ("dir", "expand"), ("left", 687));

        var line = writer.ToString().TrimEnd();
        var json = System.Text.Json.Nodes.JsonNode.Parse(line)!;
        Assert.Equal("transition.start", json["event"]!.GetValue<string>());
        Assert.Equal("expand", json["dir"]!.GetValue<string>());
        Assert.Equal(687, json["left"]!.GetValue<int>());
        Assert.True(json["qpc"]!.GetValue<long>() > 0);
    }
}
