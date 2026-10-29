using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Theming;
using Halo.Core.Timers;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Visual Experience 2.0: design tokens, activity accents and the artwork accent.</summary>
public class DesignTokenTests
{
    public static TheoryData<Tone> AllTones()
    {
        var data = new TheoryData<Tone>();
        foreach (var tone in Enum.GetValues<Tone>())
        {
            data.Add(tone);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTones))]
    public void EveryAccentStandsOutFromTheFillInBothThemes(Tone tone)
    {
        // WCAG 1.4.11 non-text contrast: bars, rings and glyph tints need 3:1 against the island.
        foreach (var palette in new[] { IslandPalette.Dark, IslandPalette.Light })
        {
            var accent = HaloDesign.Accent(tone, palette);
            Assert.True(Rgba.Contrast(accent, palette.Fill) >= 3, $"{tone} below 3:1");
        }
    }

    [Fact]
    public void HighContrastUsesTheSystemHighlightForEveryTone()
    {
        var hc = new HighContrastColors(Rgba.Opaque(0, 0, 0), Rgba.Opaque(255, 255, 255), Rgba.Opaque(26, 235, 255), Rgba.Opaque(63, 242, 63));
        var palette = IslandPalette.For(ThemeMode.Dark, false, null, hc);
        Assert.True(palette.HighContrast);
        Assert.All(Enum.GetValues<Tone>(), tone => Assert.Equal(hc.Highlight, HaloDesign.Accent(tone, palette)));

        // Flat surfaces: no gradient and a visible outline.
        Assert.Equal(palette.Fill, palette.SurfaceTop);
        Assert.Equal(palette.Fill, palette.SurfaceBottom);
        Assert.Equal(hc.WindowText, palette.Rim);
    }

    [Fact]
    public void NeutralFollowsTheBarColourIncludingTheUsersAccent()
    {
        var blue = Rgba.Opaque(0, 120, 212);
        var palette = IslandPalette.For(ThemeMode.Dark, false, blue, null);
        Assert.Equal(palette.Bar, HaloDesign.Accent(Tone.Neutral, palette));
    }

    [Fact]
    public void SurfaceGradientIsSubtleAndKeepsTextContrast()
    {
        foreach (var p in new[] { IslandPalette.Dark, IslandPalette.Light })
        {
            // Depth without a visible band: the stops differ by less than 1.25:1.
            Assert.True(Rgba.Contrast(p.SurfaceTop, p.SurfaceBottom) < 1.25);
            foreach (var surface in new[] { p.SurfaceTop, p.SurfaceBottom })
            {
                Assert.True(Rgba.Contrast(p.Tertiary.Over(surface), surface) >= 4.5);
            }
        }
    }

    [Fact]
    public void FocusPhasesHaveTheirOwnColours()
    {
        Assert.Equal(Tone.Focus, TimerActivities.ToneOf(FocusPhase.Work));
        Assert.Equal(Tone.ShortBreak, TimerActivities.ToneOf(FocusPhase.ShortBreak));
        Assert.Equal(Tone.LongBreak, TimerActivities.ToneOf(FocusPhase.LongBreak));
        var session = new FocusSession();
        session.Start(FocusPlan.Default);
        Assert.Equal(Tone.Focus, HaloDesign.ToneOf(TimerActivities.ForFocus(session)!));
    }

    [Fact]
    public void KindsHaveDefaultTonesAndSourcesCanOverride()
    {
        Assert.Equal(Tone.Timer, HaloDesign.ToneOf(new IslandActivity("t", ActivityKind.Timer, AlertPriority.Normal)));
        Assert.Equal(Tone.Reminder, HaloDesign.ToneOf(new IslandActivity("r", ActivityKind.Reminder, AlertPriority.Normal)));
        Assert.Equal(Tone.Neutral, HaloDesign.ToneOf(new IslandActivity("s", ActivityKind.System, AlertPriority.Normal)));
        Assert.Equal(Tone.Camera, HaloDesign.ToneOf(new IslandActivity("s", ActivityKind.System, AlertPriority.Normal) { Tone = Tone.Camera }));

        var plain = new IslandAlert("k", AlertKind.Timer, AlertPriority.High, new AlertContent("", "Time's up"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Assert.Equal(Tone.Timer, HaloDesign.ToneOf(plain));
        Assert.Equal(Tone.Charging, HaloDesign.ToneOf(plain with { Content = plain.Content with { Tone = Tone.Charging } }));
    }

    [Fact]
    public void BatteryAndNetworkAlertsCarryTheirMeaning()
    {
        var battery = new Power.BatteryMonitor(Power.BatteryThresholds.Default);
        _ = battery.Update(Power.PowerReading.FromSystem(0, 0, 50));
        var plugged = battery.Update(Power.PowerReading.FromSystem(1, 8, 50));
        Assert.Equal(Tone.Charging, plugged?.Content.Tone);

        var network = new Network.NetworkMonitor();
        _ = network.Update(new Network.NetworkReading(Network.NetworkState.Connected, Network.NetworkKind.WiFi));
        var lost = network.Update(new Network.NetworkReading(Network.NetworkState.Disconnected, Network.NetworkKind.None));
        Assert.Equal(Tone.Offline, lost?.Content.Tone);
    }

    [Fact]
    public void ArtAccentAveragesColourfulPixels()
    {
        // 4 × 4 premultiplied BGRA: all opaque red.
        var red = Fill(4, b: 0, g: 0, r: 230);
        var accent = HaloDesign.ArtAccent(red, 4, Rgba.Opaque(1, 2, 3));
        Assert.True(accent.R > 200 && accent.G < 40 && accent.B < 40, $"got {accent}");
    }

    [Fact]
    public void ArtAccentFallsBackForGreyOrMissingArt()
    {
        var fallback = Rgba.Opaque(1, 2, 3);
        Assert.Equal(fallback, HaloDesign.ArtAccent(Fill(8, 128, 128, 128), 8, fallback));
        Assert.Equal(fallback, HaloDesign.ArtAccent([], 0, fallback));
        Assert.Equal(fallback, HaloDesign.ArtAccent(new byte[10], 8, fallback)); // too short for the size
    }

    [Fact]
    public void ArtAccentLiftsDarkColours()
    {
        var accent = HaloDesign.ArtAccent(Fill(4, b: 60, g: 10, r: 10), 4, Rgba.Opaque(0, 0, 0));
        Assert.True(Math.Max(accent.R, Math.Max(accent.G, accent.B)) >= 165, $"got {accent}");
    }

    private static byte[] Fill(int size, byte b, byte g, byte r)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (b, g, r, 255);
        }

        return pixels;
    }
}
