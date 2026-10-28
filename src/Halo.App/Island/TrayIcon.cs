using Halo.Core.Tray;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// The notification-area icon (FR-APP-01) through <c>Shell_NotifyIconW</c> with NOTIFYICON_VERSION_4. Events arrive
/// as <see cref="CallbackMessage"/> on the island window: low word of lParam = event (WM_CONTEXTMENU, NIN_SELECT,
/// NIN_KEYSELECT), wParam = anchor point in screen coordinates. Identified by window + id rather than a GUID, because
/// a GUID icon is bound to one executable path and dev builds move. The image is drawn by <see cref="TrayGlyph"/>.
/// </summary>
internal sealed unsafe class TrayIcon(nint hwnd) : IDisposable
{
    /// <summary>Window message for icon events.</summary>
    public const uint CallbackMessage = WmApp + 2;

    public const uint IconId = 1;
    private const string Tip = "Halo";

    private nint _icon;

    public bool IsAdded { get; private set; }

    /// <summary>
    /// Adds the icon (again after an Explorer restart). Returns false when the shell refuses, for example while
    /// Explorer is not running; the caller keeps the island running and retries on TaskbarCreated.
    /// </summary>
    public bool Add(uint dpi)
    {
        ReplaceImage(dpi);
        var data = Data(NifMessage | NifIcon | NifTip | NifShowTip);
        // If the icon is still registered (TaskbarCreated can arrive without an Explorer restart), add fails and a
        // modify succeeds.
        if (!Shell_NotifyIconW(NimAdd, &data) && !Shell_NotifyIconW(NimModify, &data))
        {
            IsAdded = false;
            return false;
        }

        data.Version = NotifyIconVersion4;
        _ = Shell_NotifyIconW(NimSetVersion, &data);
        IsAdded = true;
        return true;
    }

    /// <summary>Redraws the image for a new DPI (for example after a scale change).</summary>
    public void UpdateDpi(uint dpi)
    {
        if (!IsAdded)
        {
            return;
        }

        ReplaceImage(dpi);
        var data = Data(NifIcon);
        _ = Shell_NotifyIconW(NimModify, &data);
    }

    /// <summary>
    /// Deletes the icon. Returns the shell's answer (true only if it had the icon), or null if it was never added.
    /// </summary>
    public bool? Remove()
    {
        if (!IsAdded)
        {
            return null;
        }

        var data = Data(0);
        IsAdded = false;
        return Shell_NotifyIconW(NimDelete, &data);
    }

    public void Dispose()
    {
        _ = Remove();
        if (_icon != 0)
        {
            _ = DestroyIcon(_icon);
            _icon = 0;
        }
    }

    private static nint CreateIcon(int size)
    {
        var pixels = TrayGlyph.Render(size);
        var header = new BitmapInfoHeader { Size = (uint)sizeof(BitmapInfoHeader), Width = size, Height = -size, Planes = 1, BitCount = 32 };
        void* bits;
        var color = CreateDIBSection(0, &header, 0, &bits, 0, 0);
        if (color == 0)
        {
            return 0;
        }

        // A 1-bpp mask of zeros: the colour bitmap's alpha decides transparency.
        var maskBytes = new byte[((size + 15) / 16) * 2 * size];
        nint mask;
        fixed (byte* m = maskBytes)
        {
            mask = CreateBitmap(size, size, 1, 1, m);
        }

        try
        {
            if (mask == 0)
            {
                return 0;
            }

            fixed (uint* source = pixels)
            {
                Buffer.MemoryCopy(source, bits, pixels.Length * sizeof(uint), pixels.Length * sizeof(uint));
            }

            var info = new IconInfo { IsIcon = 1, Mask = mask, Color = color };
            return CreateIconIndirect(&info);
        }
        finally
        {
            // CreateIconIndirect copies the bitmaps.
            _ = DeleteGdiObject(color);
            if (mask != 0)
            {
                _ = DeleteGdiObject(mask);
            }
        }
    }

    private void ReplaceImage(uint dpi)
    {
        var size = GetSystemMetricsForDpi(SmCxSmIcon, dpi);
        var icon = CreateIcon(size is >= 8 and <= 256 ? size : 16);
        if (icon == 0)
        {
            return;
        }

        if (_icon != 0)
        {
            _ = DestroyIcon(_icon);
        }

        _icon = icon;
    }

    private NotifyIconData Data(uint flags)
    {
        var data = new NotifyIconData
        {
            Size = (uint)sizeof(NotifyIconData),
            Hwnd = hwnd,
            Id = IconId,
            Flags = flags,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
        };
        Tip.AsSpan().CopyTo(new Span<char>(data.Tip, 128));
        return data;
    }
}
