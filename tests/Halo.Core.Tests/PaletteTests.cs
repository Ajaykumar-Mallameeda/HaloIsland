using Halo.Core.Theming;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>I-F01: theme palettes, contrast (FR-A11Y-07) and high contrast.</summary>
public class PaletteTests
{
    [Fact]
    public void DarkAndLightPalettesMeetTextContrast()
    {
        Assert.True(IslandPalette.Dark.MeetsContrast(), "dark palette below 4.5:1");
        Assert.True(IslandPalette.Light.MeetsContrast(), "light palette below 4.5:1");
    }

    [Theory]
    [InlineData(ThemeMode.Dark, false, false)]
    [InlineData(ThemeMode.Dark, true, false)]
    [InlineData(ThemeMode.Light, false, true)]
    [InlineData(ThemeMode.System, true, true)]
    [InlineData(ThemeMode.System, false, false)]
    public void ModeAndSystemStateChooseThePalette(ThemeMode mode, bool systemLight, bool expectLight) =>
        Assert.Equal(expectLight ? IslandPalette.Light : IslandPalette.Dark, IslandPalette.For(mode, systemLight, null, null));

    [Fact]
    public void AnAccentIsUsedForBarsAndKeptVisible()
    {
        var blue = Rgba.Opaque(0, 120, 212);
        var dark = IslandPalette.For(ThemeMode.Dark, false, blue, null);
        Assert.Equal(IslandPalette.Dark.Primary, dark.Primary); // text is unchanged
        Assert.True(Rgba.Contrast(dark.Bar, dark.Fill) >= 3);

        // A near-black accent on the black island is lightened until it stands out.
        var tooDark = IslandPalette.For(ThemeMode.Dark, false, Rgba.Opaque(10, 10, 30), null);
        Assert.True(Rgba.Contrast(tooDark.Bar, tooDark.Fill) >= 3);
        var tooPale = IslandPalette.For(ThemeMode.Light, false, Rgba.Opaque(250, 250, 200), null);
        Assert.True(Rgba.Contrast(tooPale.Bar, tooPale.Fill) >= 3);
    }

    [Fact]
    public void HighContrastUsesTheSystemColoursWhateverTheTheme()
    {
        var hc = new HighContrastColors(Rgba.Opaque(0, 0, 0), Rgba.Opaque(255, 255, 0), Rgba.Opaque(0, 255, 255), Rgba.Opaque(128, 128, 128));
        var p = IslandPalette.For(ThemeMode.Light, true, Rgba.Opaque(255, 0, 0), hc);
        Assert.Equal((hc.Window, hc.WindowText, hc.Highlight), (p.Fill, p.Primary, p.Bar));
        Assert.True(p.MeetsContrast());
    }

    [Fact]
    public void ContrastMathMatchesWcagReferenceValues()
    {
        Assert.Equal(21, Rgba.Contrast(Rgba.Opaque(0, 0, 0), Rgba.Opaque(255, 255, 255)), 1);
        Assert.Equal(1, Rgba.Contrast(Rgba.Opaque(119, 119, 119), Rgba.Opaque(119, 119, 119)), 3);
        Assert.Equal(4.48, Rgba.Contrast(Rgba.Opaque(119, 119, 119), Rgba.Opaque(255, 255, 255)), 2); // #777 on white
        Assert.Equal(Rgba.Opaque(128, 128, 128), new Rgba(128, 255, 255, 255).Over(Rgba.Opaque(0, 0, 0))); // half white over black
    }
}
