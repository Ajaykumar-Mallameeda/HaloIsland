using System.Runtime.InteropServices;
using Xunit;

namespace Halo.App.Tests;

/// <summary>I-F05 on the desktop: the optional shortcut toggles the island without taking focus, and a conflict is detected.</summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void TheShortcutIsOffByDefault()
    {
        using var island = IslandProcess.Start();
        var hotkey = island.WaitFor("hotkey");
        Assert.Equal(("off", 0), (hotkey["choice"]!.GetValue<string>(), hotkey["registered"]!.GetValue<int>()));
    }

    [Fact]
    public void TheChosenShortcutOpensAndClosesTheIslandWithoutTakingFocus()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"hotkey\": \"ctrl+alt+h\" }");
        var hotkey = island.WaitFor("hotkey");
        Assert.Equal(("ctrl+alt+h", 1), (hotkey["choice"]!.GetValue<string>(), hotkey["registered"]!.GetValue<int>()));
        var foreground = SettledForeground();

        PressChord(0x11, 0x12, 0x48); // Ctrl+Alt+H
        var open = island.WaitFor("transition.start");
        Assert.Equal(("expand", "hotkey"), (open["dir"]!.GetValue<string>(), open["source"]!.GetValue<string>()));
        island.WaitFor("transition.end");
        PressChord(0x11, 0x12, 0x48);
        Assert.Equal("collapse", island.WaitFor("transition.start", skip: 1)["dir"]!.GetValue<string>());
        AssertForeground(foreground);
    }

    [Fact]
    public void AShortcutAlreadyTakenByAnotherAppIsReportedNotStolen()
    {
        // The test process takes Ctrl+Alt+H first (thread hotkey), as another app might.
        Assert.True(RegisterHotKey(0, 0xB001, 0x0002 | 0x0001 | 0x4000, 0x48));
        try
        {
            using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"hotkey\": \"ctrl+alt+h\" }");
            var hotkey = island.WaitFor("hotkey");
            Assert.Equal(0, hotkey["registered"]!.GetValue<int>());
            Assert.Equal(1409, hotkey["error"]!.GetValue<int>()); // ERROR_HOTKEY_ALREADY_REGISTERED
        }
        finally
        {
            Assert.True(UnregisterHotKey(0, 0xB001));
        }
    }

    /// <summary>Holds the keys down in order, then releases them in reverse (a real chord).</summary>
    private static void PressChord(params ushort[] keys)
    {
        var inputs = keys.Select(k => new KeyInput { Type = 1, Vk = k })
            .Concat(keys.Reverse().Select(k => new KeyInput { Type = 1, Vk = k, Flags = 0x0002 }))
            .ToArray();
        Assert.Equal((uint)inputs.Length, SendKeyInput((uint)inputs.Length, inputs, Marshal.SizeOf<KeyInput>()));
        Thread.Sleep(150);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
