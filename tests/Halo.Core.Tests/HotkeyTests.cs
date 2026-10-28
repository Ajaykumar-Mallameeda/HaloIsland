using Halo.Core.Settings;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>I-F05: shortcut choices, their key codes and their settings-file form.</summary>
public class HotkeyTests
{
    [Fact]
    public void EveryChoiceRoundTripsThroughItsToken()
    {
        foreach (var choice in Hotkeys.All)
        {
            Assert.Equal(choice, Hotkeys.Parse(Hotkeys.Token(choice)));
        }

        Assert.Null(Hotkeys.Parse("ctrl+alt+delete"));
        Assert.Null(Hotkeys.Parse(null));
    }

    [Fact]
    public void ChoicesUseModifiersAndNoRepeatAndOffHasNoKeys()
    {
        Assert.Null(Hotkeys.Keys(HotkeyChoice.Off));
        foreach (var choice in Hotkeys.All.Where(c => c != HotkeyChoice.Off))
        {
            var (mods, key) = Hotkeys.Keys(choice)!.Value;
            Assert.NotEqual(0u, mods & Hotkeys.ModControl); // never a bare letter that would swallow typing
            Assert.NotEqual(0u, mods & Hotkeys.ModNoRepeat);
            Assert.NotEqual(0u, key);
        }
    }

    [Fact]
    public void TheSettingIsOffByDefaultAndInvalidValuesFallBack()
    {
        Assert.Equal(HotkeyChoice.Off, HaloSettings.Defaults.Hotkey);
        Assert.True(HaloSettings.TryParse("{ \"schemaVersion\": 1, \"hotkey\": \"ctrl+alt+space\" }", out var s, out _, out var issues));
        Assert.Equal((HotkeyChoice.CtrlAltSpace, 0), (s.Hotkey, issues.Count));
        Assert.True(HaloSettings.TryParse("{ \"schemaVersion\": 1, \"hotkey\": \"win+l\" }", out var bad, out _, out var badIssues));
        Assert.Equal(HotkeyChoice.Off, bad.Hotkey);
        Assert.Contains("hotkey", badIssues);
        Assert.True(HaloSettings.TryParse(new HaloSettings { Hotkey = HotkeyChoice.CtrlShiftH }.ToJson(), out var round, out _, out _));
        Assert.Equal(HotkeyChoice.CtrlShiftH, round.Hotkey);
    }
}
