using Halo.Core.Audio;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>J-F04: Bluetooth audio classification and connection pills (no battery level, ever).</summary>
public class AudioDeviceTests
{
    [Theory]
    [InlineData("BTHENUM", true)]
    [InlineData("bthledevice", true)]
    [InlineData("BTHHFENUM", true)]
    [InlineData("HDAUDIO", false)]
    [InlineData("USB", false)]
    [InlineData("SWD", false)]
    [InlineData(null, false)]
    public void BluetoothIsRecognisedByTheDeviceEnumerator(string? enumerator, bool expected) =>
        Assert.Equal(expected, AudioDeviceInfo.IsBluetoothEnumerator(enumerator));

    [Fact]
    public void TheFirstReadingIsSilentThenConnectAndDisconnectShowPills()
    {
        var monitor = new AudioDeviceMonitor();
        var speakers = new AudioDeviceInfo(false, "Speakers (Realtek)");
        var buds = new AudioDeviceInfo(true, "Headphones (Buds)");

        Assert.Null(monitor.Update(speakers)); // start-up: record only
        var connected = monitor.Update(buds)!;
        Assert.Equal("Connected · Headphones (Buds)", connected.Content.Text);
        Assert.Equal("Headphones (Buds) connected", connected.Content.Spoken);
        Assert.Null(connected.Content.Level); // no battery bar: nothing is inferred
        Assert.Null(monitor.Update(buds)); // the same device again (re-bind): nothing

        var gone = monitor.Update(speakers)!;
        Assert.Equal("Disconnected · Headphones (Buds)", gone.Content.Text);
        Assert.Null(monitor.Update(new AudioDeviceInfo(false, "USB speakers"))); // wired to wired: nothing
        Assert.NotNull(monitor.Update(buds));
        Assert.NotNull(monitor.Update(null)); // Bluetooth gone, no output at all
    }

    [Fact]
    public void ResetMakesTheNextReadingSilentAndNamesAreCleaned()
    {
        var monitor = new AudioDeviceMonitor();
        monitor.Update(new AudioDeviceInfo(false, "a"));
        monitor.Reset();
        Assert.Null(monitor.Update(new AudioDeviceInfo(true, "b")));
        Assert.Equal("Bad name", AudioDeviceInfo.From("BTHENUM", "Bad\u0007 name").Name);
        Assert.Equal(64, AudioDeviceInfo.From("BTHENUM", new string('x', 200)).Name.Length);
        Assert.Equal("Connected · Bluetooth audio", new AudioDeviceMonitor() is var m && m.Update(null) is null ? m.Update(new AudioDeviceInfo(true, string.Empty))!.Content.Text : string.Empty);
    }
}
