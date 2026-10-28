namespace Halo.Core.Settings;

/// <summary>
/// The optional shortcut that opens or closes the island (I-F05, FR-ISL-07). A short fixed list, so every choice is
/// one Windows accepts for <c>RegisterHotKey</c> and none needs a keyboard hook. Off by default.
/// </summary>
public enum HotkeyChoice
{
    Off,
    CtrlAltH,
    CtrlAltSpace,
    CtrlShiftH,
}

/// <summary>Modifier flags and virtual-key codes for <see cref="HotkeyChoice"/> (WinUser.h values).</summary>
public static class Hotkeys
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;

    /// <summary>MOD_NOREPEAT: holding the keys does not repeat the toggle.</summary>
    public const uint ModNoRepeat = 0x4000;

    public static readonly IReadOnlyList<HotkeyChoice> All = Enum.GetValues<HotkeyChoice>();

    /// <summary>(modifiers, virtual key) for RegisterHotKey, or null for Off.</summary>
    public static (uint Modifiers, uint Key)? Keys(HotkeyChoice choice) => choice switch
    {
        HotkeyChoice.CtrlAltH => (ModControl | ModAlt | ModNoRepeat, 0x48),
        HotkeyChoice.CtrlAltSpace => (ModControl | ModAlt | ModNoRepeat, 0x20),
        HotkeyChoice.CtrlShiftH => (ModControl | ModShift | ModNoRepeat, 0x48),
        _ => null,
    };

    public static string Label(HotkeyChoice choice) => choice switch
    {
        HotkeyChoice.CtrlAltH => "Ctrl+Alt+H",
        HotkeyChoice.CtrlAltSpace => "Ctrl+Alt+Space",
        HotkeyChoice.CtrlShiftH => "Ctrl+Shift+H",
        _ => "Off",
    };

    /// <summary>Settings-file token (stable, not localised).</summary>
    public static string Token(HotkeyChoice choice) => choice switch
    {
        HotkeyChoice.CtrlAltH => "ctrl+alt+h",
        HotkeyChoice.CtrlAltSpace => "ctrl+alt+space",
        HotkeyChoice.CtrlShiftH => "ctrl+shift+h",
        _ => "off",
    };

    public static HotkeyChoice? Parse(string? token) => All.Cast<HotkeyChoice?>().FirstOrDefault(c => string.Equals(Token(c!.Value), token, StringComparison.OrdinalIgnoreCase));
}
