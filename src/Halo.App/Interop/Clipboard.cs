using System.Runtime.InteropServices;

namespace Halo.App.Interop;

/// <summary>
/// The Windows clipboard for the shelf (H-F01/H-F02), used only when the user presses a shelf button: read the copied
/// text or file list once, or put an item back. Halo never watches the clipboard (no clipboard listener is
/// registered), so nothing is captured unless the user asks.
/// </summary>
internal static unsafe partial class Clipboard
{
    private const uint CfUnicodeText = 13;
    private const uint CfHdrop = 15;
    private const uint GmemMoveable = 0x0002;

    /// <summary>What is on the clipboard now: text, file paths, or neither.</summary>
    public static (string? Text, IReadOnlyList<string> Files) Read(nint owner)
    {
        if (!Open(owner))
        {
            return (null, []);
        }

        try
        {
            if (IsClipboardFormatAvailable(CfHdrop) && GetClipboardData(CfHdrop) is var drop and not 0)
            {
                var count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
                var files = new List<string>((int)Math.Min(count, 100));
                for (uint i = 0; i < count && i < 100; i++)
                {
                    var length = DragQueryFileW(drop, i, null, 0);
                    var buffer = new char[length + 1];
                    fixed (char* b = buffer)
                    {
                        _ = DragQueryFileW(drop, i, b, (uint)buffer.Length);
                        files.Add(new string(b, 0, (int)length));
                    }
                }

                return (null, files);
            }

            if (IsClipboardFormatAvailable(CfUnicodeText) && GetClipboardData(CfUnicodeText) is var handle and not 0)
            {
                var p = GlobalLock(handle);
                try
                {
                    // Bounded read: a text larger than the shelf keeps is cut by the shelf anyway.
                    var size = (int)Math.Min(GlobalSize(handle) / 2, 1_000_000);
                    var span = new ReadOnlySpan<char>((void*)p, size);
                    var end = span.IndexOf('\0');
                    return (new string(end >= 0 ? span[..end] : span), []);
                }
                finally
                {
                    _ = GlobalUnlock(handle);
                }
            }

            return (null, []);
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>Puts text on the clipboard (the user pressed Copy).</summary>
    public static bool SetText(nint owner, string text) => Set(owner, CfUnicodeText, (text + '\0').ToCharArray(), 0);

    /// <summary>Puts file references on the clipboard as a file drop list, so Paste in File Explorer copies them.</summary>
    public static bool SetFiles(nint owner, IReadOnlyList<string> paths)
    {
        // DROPFILES (20 bytes) then a double-null-terminated UTF-16 list.
        var list = (string.Join('\0', paths) + "\0\0").ToCharArray();
        return Set(owner, CfHdrop, list, 20);
    }

    private static bool Set(nint owner, uint format, char[] chars, int headerBytes)
    {
        if (!Open(owner))
        {
            return false;
        }

        try
        {
            _ = EmptyClipboard();
            var bytes = headerBytes + (chars.Length * 2);
            var memory = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (memory == 0)
            {
                return false;
            }

            var p = (byte*)GlobalLock(memory);
            if (headerBytes > 0)
            {
                new Span<byte>(p, headerBytes).Clear();
                *(uint*)p = (uint)headerBytes; // pFiles: offset of the list
                *(uint*)(p + 16) = 1; // fWide: UTF-16
            }

            fixed (char* c = chars)
            {
                Buffer.MemoryCopy(c, p + headerBytes, chars.Length * 2, chars.Length * 2);
            }

            _ = GlobalUnlock(memory);
            if (SetClipboardData(format, memory) == 0)
            {
                _ = GlobalFree(memory); // still ours when SetClipboardData fails
                return false;
            }

            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>Another app may hold the clipboard for a moment; try a few times.</summary>
    private static bool Open(nint owner)
    {
        for (var i = 0; i < 5; i++)
        {
            if (OpenClipboard(owner))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll")]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll")]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nuint GlobalSize(nint memory);

    [LibraryImport("shell32.dll")]
    private static partial uint DragQueryFileW(nint drop, uint index, char* file, uint length);
}
