using Halo.Core.Settings;
using Halo.Core.Theming;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>I-F02 compact-pill content priority and the I-F01/I-F02 settings.</summary>
public class PillContentTests
{
    [Theory]
    [InlineData(true, true, true, true, true, PillSlot.Activity)]
    [InlineData(true, true, true, false, true, PillSlot.Media)]
    [InlineData(true, true, true, false, false, PillSlot.Date)]
    [InlineData(false, true, true, true, true, PillSlot.Media)] // activities hidden from the pill
    [InlineData(false, false, true, true, true, PillSlot.Date)]
    [InlineData(false, false, false, true, true, PillSlot.Empty)]
    [InlineData(true, false, false, false, true, PillSlot.Empty)] // media not wanted, no activity, no date
    public void TheFirstEnabledItemThatExistsWins(bool activity, bool media, bool date, bool hasActivity, bool hasMedia, PillSlot expected) =>
        Assert.Equal(expected, new PillContent(activity, media, date).Slot(hasActivity, hasMedia));

    [Fact]
    public void ThemeAndPillSettingsRoundTripAndHaveSafeDefaults()
    {
        var d = HaloSettings.Defaults;
        Assert.Equal((ThemeMode.Dark, false, true, true, true, false), (d.Theme, d.AccentBars, d.PillShowsActivity, d.PillShowsMedia, d.PillShowsDate, d.PillShowsBattery));

        var custom = d with { Theme = ThemeMode.System, AccentBars = true, PillShowsDate = false, PillShowsBattery = true };
        Assert.True(HaloSettings.TryParse(custom.ToJson(), out var back, out _, out var issues));
        Assert.Empty(issues);
        Assert.Equal(custom, back);

        Assert.True(HaloSettings.TryParse("{ \"schemaVersion\": 1, \"theme\": \"neon\" }", out var bad, out _, out var badIssues));
        Assert.Equal(ThemeMode.Dark, bad.Theme);
        Assert.Contains("theme", badIssues);
        Assert.True(HaloSettings.TryParse("{ \"schemaVersion\": 1, \"theme\": \"7\" }", out var numeric, out _, out _));
        Assert.Equal(ThemeMode.Dark, numeric.Theme); // numbers are not theme names
    }
}
