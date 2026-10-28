using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>I-F01/I-F02 on the desktop: the light theme is drawn light, and the pill follows its content settings.</summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void TheLightThemeDrawsALightIslandWithDarkText()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"theme\": \"light\" }");
        var theme = island.WaitFor("theme");
        Assert.Equal(("light", 1), (theme["mode"]!.GetValue<string>(), theme["light"]!.GetValue<int>()));
        Thread.Sleep(400);
        var (bright, dark) = PillPixels();
        Assert.True(bright > dark * 4, $"bright={bright} dark={dark}: the pill is not light");
        Assert.True(dark > 60, $"too few dark text pixels: {dark}");
    }

    [Fact]
    public void TheDarkThemeIsTheDefault()
    {
        using var island = IslandProcess.Start();
        Assert.Equal(("dark", 0), (island.WaitFor("theme")["mode"]!.GetValue<string>(), island.WaitFor("theme")["light"]!.GetValue<int>()));
    }

    [Fact]
    public void ThePillCanHideTheDateAndShowsTheTimerOnlyWhenAllowed()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"pillShowsDate\": false, \"pillShowsActivity\": false }");
        island.WaitFor("ready");
        Thread.Sleep(300);
        var withoutDate = LeftHalfBrightPixels();

        Assert.True(PostMessageW(island.Hwnd, WmTestTimer, 600, 0));
        island.WaitFor("timer");
        Thread.Sleep(400);
        // Activities are hidden from the pill: its left half stays empty, although the timer runs (and UIA says so).
        Assert.True(LeftHalfBrightPixels() < 20, "the timer appeared in the pill although the setting hides it");
        Assert.True(withoutDate < 20, $"the date is drawn although hidden ({withoutDate} bright pixels)");
        using var automation = new UIA3Automation();
        Assert.Contains("Timer,", automation.FromHandle(island.Hwnd).Name, StringComparison.Ordinal);
    }

    private static (int Bright, int Dark) PillPixels()
    {
        var layout = ExpectedLayout();
        var c = layout.Compact;
        using var capture = FlaUI.Core.Capturing.Capture.Rectangle(new System.Drawing.Rectangle(layout.Canvas.Left + (int)c.Left, layout.Canvas.Top + (int)c.Top, (int)c.Width, (int)c.Height));
        var bitmap = capture.Bitmap;
        int bright = 0, dark = 0;
        for (var y = 3; y < bitmap.Height - 3; y++)
        {
            for (var x = (int)(c.Height / 2); x < bitmap.Width - (int)(c.Height / 2); x++)
            {
                var p = bitmap.GetPixel(x, y);
                var luma = (p.R + p.G + p.B) / 3;
                bright += luma > 200 ? 1 : 0;
                dark += luma < 90 ? 1 : 0;
            }
        }

        return (bright, dark);
    }

    private static int LeftHalfBrightPixels()
    {
        var layout = ExpectedLayout();
        var c = layout.Compact;
        using var capture = FlaUI.Core.Capturing.Capture.Rectangle(new System.Drawing.Rectangle(layout.Canvas.Left + (int)c.Left, layout.Canvas.Top + (int)c.Top, (int)(c.Width / 2), (int)c.Height));
        var bitmap = capture.Bitmap;
        var bright = 0;
        for (var y = 3; y < bitmap.Height - 3; y++)
        {
            for (var x = (int)(c.Height / 2); x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                bright += (p.R + p.G + p.B) / 3 > 160 ? 1 : 0;
            }
        }

        return bright;
    }
}
