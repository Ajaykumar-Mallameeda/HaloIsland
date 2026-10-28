using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using FlaUI.UIA3;
using Halo.App.Interop;
using Halo.Core.Audio;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Volume against the real default output endpoint (M3-F03, FR-SYS-01). Safe procedure: every test changes the
/// level only a few percent from where it was (nothing is playing while the suite runs) and restores the original
/// level and mute state in <see cref="Endpoint.Dispose"/>, even when an assertion fails.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmVolumeDevice = 0x8000 + 7; // VolumeWatcher.DeviceMessage

    [Fact]
    public void ExternalVolumeChangesShowThePillWithoutTakingFocus()
    {
        using var endpoint = Endpoint.Open();
        using var island = IslandProcess.Start();
        WaitForVolumeDevice(island, "bound");
        var foreground = SettledForeground();

        // A change made elsewhere (here: the test, as an app or the volume keys would) shows the pill.
        var target = endpoint.Near(0.03f);
        var before = Stopwatch.GetTimestamp();
        endpoint.SetLevel(target);
        var shown = WaitForVolumeAlert(island, "shown");
        var latencyMs = (shown["qpc"]!.GetValue<long>() - before) * 1000.0 / Stopwatch.Frequency;
        RecordMetric("volume.change_to_alert_ms", latencyMs);
        Assert.True(latencyMs < 250, $"change → pill took {latencyMs:F1} ms");
        WaitForVolume(island, Percent(target), source: "external");
        Assert.Equal("collapsed", island.State()); // a pill is not an expansion

        // Mute and unmute update the same pill (one key: coalesced, not a second pill).
        var muted = !endpoint.Muted;
        endpoint.SetMuted(muted);
        WaitForVolume(island, Percent(target), source: "external", muted: muted);
        Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() == "volume" && e["state"]?.GetValue<string>() == "replaced");
        endpoint.SetMuted(!muted);

        // The pill expires back to the compact island, and focus never moved.
        WaitForVolumeAlert(island, "ended", timeoutMs: 5000);
        AssertForeground(foreground);
    }

    [Fact]
    public void TheSliderAndMuteButtonSetTheEndpointAndRaiseNoPill()
    {
        using var endpoint = Endpoint.Open();
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();
        WaitForVolumeDevice(island, "bound");
        var foreground = SettledForeground();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        var row = VolumeRow.For(layout)!;
        (int X, int Y) At(double x, double y) => (layout.Canvas.Left + (int)Math.Round(x), layout.Canvas.Top + (int)Math.Round(y));

        // Click on the track: the endpoint takes the level under the pointer.
        var first = endpoint.Near(0.08f);
        var (cx, cy) = At(row.XFor(first), row.TrackY);
        Click(cx, cy);
        Assert.True(WaitUntil(() => Math.Abs(endpoint.Level - first) <= 0.011f, 3000), $"endpoint {endpoint.Level:F3}, wanted {first:F2}; commands: {Commands(island)}");
        WaitForVolume(island, Percent(first), source: "halo");

        // Drag along the track to a second level.
        var second = endpoint.Near(-0.05f, from: first);
        Drag(At(row.XFor(first), row.TrackY), At(row.XFor(second), row.TrackY));
        Assert.True(WaitUntil(() => Math.Abs(endpoint.Level - second) <= 0.011f, 3000), $"endpoint {endpoint.Level:F3} after the drag, wanted {second:F2}; commands: {Commands(island)}");

        // The mute button toggles mute both ways.
        var wasMuted = endpoint.Muted;
        var (mx, my) = At(row.MuteX, row.MuteY);
        Click(mx, my);
        Assert.True(WaitUntil(() => endpoint.Muted == !wasMuted, 3000), "the mute button did not toggle mute");
        Click(mx, my);
        Assert.True(WaitUntil(() => endpoint.Muted == wasMuted, 3000), "the mute button did not toggle mute back");

        // The open island already shows the slider: Halo's own changes raise no pill, never collapse it, keep focus.
        Assert.Equal("expanded_pinned", island.State());
        Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() == "volume");
        Assert.Contains("Volume", element.Properties.FullDescription.ValueOrDefault ?? string.Empty, StringComparison.Ordinal);
        AssertForeground(foreground);
    }

    [Fact]
    public void ADeviceChangeRebindsAndAMissingDeviceIsHandled()
    {
        using var endpoint = Endpoint.Open();

        // Device change: the island drops its endpoint and binds the default one again; changes still arrive.
        using (var island = IslandProcess.Start())
        {
            WaitForVolumeDevice(island, "bound");
            Assert.True(PostMessageW(island.Hwnd, WmVolumeDevice, 0, 0));
            Assert.True(PostMessageW(island.Hwnd, WmVolumeDevice, 0, 0)); // a burst: one re-bind
            Assert.True(WaitUntil(() => island.Events("volume.device").Any(e => e["reason"]?.GetValue<string>() == "device_changed"), 3000), "no re-bind after a device change");
            Thread.Sleep(400);
            Assert.Single(island.Events("volume.device"), e => e["reason"]?.GetValue<string>() == "device_changed");
            var target = endpoint.Near(0.02f);
            endpoint.SetLevel(target);
            WaitForVolume(island, Percent(target), source: "external");
            WaitForVolumeAlert(island, "shown");
        }

        // No output device: no row controls, no pill, nothing sent to the endpoint; the island keeps working.
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var none = IslandProcess.Start(fault: "audio");
        WaitForVolumeDevice(none, "none");
        endpoint.SetLevel(endpoint.Near(0.02f)); // an external change: no device bound, so no pill
        var level = endpoint.Level;
        using var automation = new UIA3Automation();
        automation.FromHandle(none.Hwnd).Patterns.ExpandCollapse.Pattern.Expand();
        none.WaitFor("transition.end");
        var row = VolumeRow.For(layout)!;
        Click(layout.Canvas.Left + (int)row.XFor(0.2), layout.Canvas.Top + (int)row.TrackY);
        Thread.Sleep(500);
        Assert.Empty(none.Events("volume.command"));
        Assert.Empty(none.Events("volume"));
        Assert.DoesNotContain(none.Events("alert"), e => e["kind"]?.GetValue<string>() == "volume");
        Assert.Equal(level, endpoint.Level, 3); // the click on the empty row changed nothing
    }

    [Fact]
    public void VolumeOffRemovesTheRowAndThePill()
    {
        using var endpoint = Endpoint.Open();
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showVolume\": false }");

        // The expanded island keeps its original height without the row.
        var ready = island.WaitFor("ready");
        Assert.Equal(ExpectedLayout(volumeRow: false).Canvas.Height, ready["height"]!.GetValue<int>());

        endpoint.SetLevel(endpoint.Near(0.02f));
        Thread.Sleep(800);
        Assert.Empty(island.Events("volume"));
        Assert.DoesNotContain(island.Events("alert"), e => e["kind"]?.GetValue<string>() == "volume");
        Assert.Empty(island.Events("volume.device")); // the endpoint is never opened
    }

    private static int Percent(float level) => new VolumeReading(level, false).Percent;

    private static string Commands(IslandProcess island) =>
        string.Join(",", island.Events("volume.command").Select(e => $"{e["command"]}:{e["percent"]}={e["result"]}"));

    private static void WaitForVolumeDevice(IslandProcess island, string status)
    {
        var ok = WaitUntil(() => island.Events("volume.device").Any(e => e["status"]?.GetValue<string>() == status), 5000);
        Assert.True(ok, $"no volume device status '{status}'; seen: {string.Join(",", island.Events("volume.device").Select(e => $"{e["status"]}/{e["hresult"]}"))}");
    }

    private static void WaitForVolume(IslandProcess island, int percent, string source, bool? muted = null)
    {
        var ok = WaitUntil(
            () => island.Events("volume").Any(e => e["percent"]?.GetValue<int>() == percent && e["source"]?.GetValue<string>() == source && (muted is null || e["muted"]?.GetValue<int>() == (muted.Value ? 1 : 0))),
            5000);
        Assert.True(ok, $"no volume {percent} % from {source}; seen: {string.Join(",", island.Events("volume").Select(e => $"{e["percent"]}/{e["muted"]}/{e["source"]}"))}");
    }

    private static JsonObject WaitForVolumeAlert(IslandProcess island, string state, int timeoutMs = 3000)
    {
        JsonObject? found = null;
        var ok = WaitUntil(() => (found = island.Events("alert").FirstOrDefault(e => e["kind"]?.GetValue<string>() == "volume" && e["state"]?.GetValue<string>() == state)) is not null, timeoutMs);
        Assert.True(ok, $"no volume alert '{state}'; alerts: {string.Join(",", island.Events("alert").Select(e => $"{e["kind"]}:{e["state"]}"))}");
        return found!;
    }

    /// <summary>Optional: appends a smoke measurement to the file named by HALO_TEST_METRICS (not a budget verdict).</summary>
    private static void RecordMetric(string name, double value)
    {
        if (Environment.GetEnvironmentVariable("HALO_TEST_METRICS") is { Length: > 0 } path)
        {
            File.AppendAllText(path, string.Create(CultureInfo.InvariantCulture, $"{name}\t{value:F2}{Environment.NewLine}"));
        }
    }

    /// <summary>Press, move in steps, release: one real pointer drag.</summary>
    private static void Drag((int X, int Y) from, (int X, int Y) to)
    {
        var w = GetSystemMetrics(0);
        var h = GetSystemMetrics(1);
        MouseInput At(int x, int y, uint flags) => new() { X = ((x * 65536) + (w / 2)) / w, Y = ((y * 65536) + (h / 2)) / h, Flags = flags | 0x0001 | 0x8000 };
        Assert.Equal(1u, SendInput(1, [new Input { Type = 0, Mouse = At(from.X, from.Y, 0x0002) }], Marshal.SizeOf<Input>()));
        for (var i = 1; i <= 8; i++)
        {
            Thread.Sleep(15);
            var x = from.X + ((to.X - from.X) * i / 8);
            var y = from.Y + ((to.Y - from.Y) * i / 8);
            Assert.Equal(1u, SendInput(1, [new Input { Type = 0, Mouse = At(x, y, 0) }], Marshal.SizeOf<Input>()));
        }

        Thread.Sleep(15);
        Assert.Equal(1u, SendInput(1, [new Input { Type = 0, Mouse = At(to.X, to.Y, 0x0004) }], Marshal.SizeOf<Input>()));
        s_lastMove = to;
    }

    /// <summary>The default output endpoint, opened by the test with its own event context; restores itself on dispose.</summary>
    private sealed class Endpoint : IDisposable
    {
        private static readonly Guid TestContext = new("0B9C2E47-1A55-4C3B-9E0D-7F4A6C2B8D13");
        private readonly IMMDeviceEnumerator _enumerator;
        private readonly IMMDevice _device;
        private readonly IAudioEndpointVolume _volume;
        private readonly float _originalLevel;
        private readonly bool _originalMuted;

        private Endpoint(IMMDeviceEnumerator enumerator, IMMDevice device, IAudioEndpointVolume volume)
        {
            (_enumerator, _device, _volume) = (enumerator, device, volume);
            _originalLevel = Level;
            _originalMuted = Muted;
        }

        public float Level => _volume.GetMasterVolumeLevelScalar(out var level) >= 0 ? level : float.NaN;

        public bool Muted => _volume.GetMute(out var muted) >= 0 && muted;

        public static Endpoint Open()
        {
            var enumerator = CoreAudio.CreateEnumerator();
            var (device, volume, hr) = CoreAudio.DefaultRender(enumerator);
            Assert.True(device is not null && volume is not null, $"this machine has no default output device (0x{hr:X8}); the volume tests need one");
            return new Endpoint(enumerator, device!, volume!);
        }

        /// <summary>A whole-percent level <paramref name="delta"/> away from <paramref name="from"/> (default: the original), kept in 0–1.</summary>
        public float Near(float delta, float? from = null)
        {
            var start = MathF.Round((from ?? _originalLevel) * 100) / 100;
            var target = start + delta;
            if (target is < 0 or > 1)
            {
                target = start - delta;
            }

            return MathF.Round(Math.Clamp(target, 0, 1) * 100) / 100;
        }

        public void SetLevel(float level)
        {
            var context = TestContext;
            Assert.True(_volume.SetMasterVolumeLevelScalar(level, ref context) >= 0, "SetMasterVolumeLevelScalar failed");
        }

        public void SetMuted(bool muted)
        {
            var context = TestContext;
            Assert.True(_volume.SetMute(muted, ref context) >= 0, "SetMute failed");
        }

        public void Dispose()
        {
            var context = TestContext;
            _ = _volume.SetMasterVolumeLevelScalar(_originalLevel, ref context);
            _ = _volume.SetMute(_originalMuted, ref context);
            CoreAudio.Release(_volume);
            CoreAudio.Release(_device);
            CoreAudio.Release(_enumerator);
        }
    }
}
