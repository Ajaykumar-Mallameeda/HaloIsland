using System.Runtime.InteropServices;

namespace Halo.Spikes.Harness;

/// <summary>
/// One click = one SendInput batch (absolute move, button down, button up). Windows inserts a batch serially, so a
/// click always lands where it was aimed. Moving with SetCursorPos and clicking with a separate SendInput can race:
/// the next SetCursorPos may overtake a click that has not been processed yet.
/// </summary>
internal static partial class PointerInput
{
    private const uint InputMouse = 0;
    private const uint MoveFlag = 0x0001;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint Absolute = 0x8000;

    public static void ClickAt(int x, int y, int screenWidth, int screenHeight)
    {
        // Normalised coordinates aimed at the pixel centre: Windows maps norm → floor(norm * size / 65536).
        var nx = ((x * 65536) + (screenWidth / 2)) / screenWidth;
        var ny = ((y * 65536) + (screenHeight / 2)) / screenHeight;
        Input[] inputs =
        [
            new() { Type = InputMouse, Mouse = new MouseInput { X = nx, Y = ny, Flags = MoveFlag | Absolute } },
            new() { Type = InputMouse, Mouse = new MouseInput { X = nx, Y = ny, Flags = LeftDown | Absolute } },
            new() { Type = InputMouse, Mouse = new MouseInput { X = nx, Y = ny, Flags = LeftUp | Absolute } },
        ];
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
        {
            throw new InvalidOperationException("SendInput was blocked.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseInput Mouse; // largest union member on x64 is MOUSEINPUT (32 bytes), 8-byte aligned after Type
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
