using System.IO;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// J-F04 on the desktop: the real default endpoint's bus is read from its property store, and connection pills come
/// through the real alert path. Connect and disconnect are synthetic (no Bluetooth headset is available to the tests).
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestAudioDevice = 0x8000 + 57;

    [Fact]
    public void TheDefaultOutputsBusIsReadAndBluetoothChangesShowAPill()
    {
        using var island = IslandProcess.Start();
        var device = island.WaitFor("volume.device");
        if (device["status"]!.GetValue<string>() != "bound")
        {
            Assert.Skip("No audio output device on this machine.");
        }

        var bus = device["bus"]!.GetValue<string>();
        Assert.NotEqual("unknown", bus); // the property store answered
        Assert.NotEqual("none", bus);

        Assert.True(PostMessageW(island.Hwnd, WmTestAudioDevice, 1, 0)); // synthetic: Bluetooth becomes default
        var connected = island.WaitFor("audio.device");
        Assert.Equal(("connected", "test"), (connected["change"]!.GetValue<string>(), connected["reason"]!.GetValue<string>()));
        Assert.True(WaitUntil(() => island.Events("alert").Any(a => a["state"]!.GetValue<string>() == "shown")), island.Trace());

        island.WaitFor("alert", skip: 1); // ended
        Assert.True(PostMessageW(island.Hwnd, WmTestAudioDevice, 0, 0)); // synthetic: back to wired
        Assert.Equal("disconnected", island.WaitFor("audio.device", skip: 1)["change"]!.GetValue<string>());

        island.Exit();
        var log = File.ReadAllText(Path.Combine(island.DataDir, "logs", "halo.log"));
        Assert.DoesNotContain("Test Headphones", log, StringComparison.Ordinal);
        Assert.DoesNotContain(island.Events("audio.device").Select(e => e.ToJsonString()), j => j.Contains("Headphones", StringComparison.Ordinal));
    }
}
