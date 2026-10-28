using System.Runtime.InteropServices;
using Halo.App.Interop;
using Halo.Core.Shelf;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// The shelf window (H-F02): pinned text and file references, under the user's control. "Pin copied text or files"
/// reads the clipboard once; "Copy" puts an item back (files as a drop list, so Paste in File Explorer works);
/// "Show in folder" opens File Explorer with the file selected; "Remove" and "Clear all" forget items. Files are never
/// copied, moved, opened or deleted by the shelf.
/// </summary>
internal sealed unsafe class ShelfWindow : IDisposable
{
    public const string ClassName = "ProjectHalo.Shelf";
    public const string Title = "Halo shelf";

    private const int IdList = 401;
    private const int IdPin = 402;
    private const int IdCopy = 403;
    private const int IdShow = 404;
    private const int IdRemove = 405;
    private const int IdClear = 406;
    private const int IdStatus = 407;
    private const int IdClose = 2;

    private static ShelfWindow? s_current;
    private static bool s_registered;

    private readonly IslandWindow _owner;
    private readonly Dictionary<int, nint> _controls = [];
    private readonly List<string> _rowIds = [];
    private nint _font;

    private ShelfWindow(IslandWindow owner) => _owner = owner;

    public nint Hwnd { get; private set; }

    public bool IsOpen => Hwnd != 0 && IsWindow(Hwnd);

    public static ShelfWindow Open(IslandWindow owner, ShelfWindow? existing)
    {
        if (existing is { IsOpen: true })
        {
            _ = SetForegroundWindow(existing.Hwnd);
            return existing;
        }

        var window = new ShelfWindow(owner);
        window.Create();
        return window;
    }

    public bool PreTranslate(Msg* msg) => IsOpen && IsDialogMessageW(Hwnd, msg);

    public void Dispose()
    {
        if (IsOpen)
        {
            _ = DestroyWindow(Hwnd);
        }

        Hwnd = 0;
        if (_font != 0)
        {
            _ = DeleteGdiObject(_font);
            _font = 0;
        }

        if (ReferenceEquals(s_current, this))
        {
            s_current = null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var window = s_current;
        try
        {
            if (window is not null && window.Handle(hwnd, msg, wParam, lParam) is { } result)
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            // A failed shelf action never takes the island down (NFR-05).
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private static int Scale(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    private void Create()
    {
        s_current = this;
        var instance = GetModuleHandleW(null);
        fixed (char* className = ClassName)
        fixed (char* title = Title)
        {
            if (!s_registered)
            {
                var wc = new WndClassEx
                {
                    Size = (uint)sizeof(WndClassEx),
                    WndProc = &WndProc,
                    Instance = instance,
                    Cursor = LoadCursorW(0, 32512),
                    Background = ColorWindow + 1,
                    ClassName = className,
                };
                s_registered = RegisterClassExW(&wc) != 0;
            }

            var (work, dpi) = PrimaryWorkArea();
            var (w, h) = (Scale(520, dpi), Scale(420, dpi));
            Hwnd = CreateWindowExW(WsExControlParent, className, title, WsOverlapped | (uint)WsCaption | WsSysMenu | WsMinimizeBox, work.Left + ((work.Right - work.Left - w) / 2), work.Top + ((work.Bottom - work.Top - h) / 2), w, h, 0, 0, instance, 0);
        }

        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for the shelf window.");
        }

        var d = GetDpiForWindow(Hwnd);
        _font = CreateFontW(-(int)Math.Round(9 * d / 72.0), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        int S(int dip) => Scale(dip, d);
        void Add(int id, string cls, string text, long style, int x, int top, int width, int height)
        {
            var child = CreateWindowExW(0, cls, text, (uint)(WsChild | WsVisible | style), S(x), S(top), S(width), S(height), Hwnd, id, GetModuleHandleW(null), 0);
            _ = SendMessageW(child, WmSetFont, _font, 1);
            if (id != 0)
            {
                _controls[id] = child;
            }
        }

        Add(0, "STATIC", "&Pinned items (kept on this PC only):", 0, 16, 14, 400, 20);
        Add(IdList, "LISTBOX", string.Empty, WsTabStop | WsBorder | WsVScroll | LbsNotify | LbsNoIntegralHeight, 16, 36, 472, 240);
        Add(IdStatus, "STATIC", string.Empty, 0, 16, 282, 472, 20);
        Add(IdPin, "BUTTON", "Pin &copied text or files", BsPushButton | WsTabStop, 16, 312, 170, 28);
        Add(IdCopy, "BUTTON", "C&opy", BsPushButton | WsTabStop, 194, 312, 70, 28);
        Add(IdShow, "BUTTON", "Show in &folder", BsPushButton | WsTabStop, 272, 312, 110, 28);
        Add(IdRemove, "BUTTON", "&Remove", BsPushButton | WsTabStop, 390, 312, 98, 28);
        Add(IdClear, "BUTTON", "Clear &all", BsPushButton | WsTabStop, 16, 350, 100, 28);
        Add(IdClose, "BUTTON", "Close", BsPushButton | WsTabStop, 408, 350, 80, 28);
        Fill(null);
        _ = ShowWindow(Hwnd, SwShow);
        _ = SetForegroundWindow(Hwnd);
    }

    private void Fill(string? keep)
    {
        var shelf = _owner.Shelf;
        var list = _controls[IdList];
        _ = SendMessageW(list, LbResetContent, 0, 0);
        _rowIds.Clear();
        foreach (var item in shelf.Items)
        {
            var kind = item.Kind == ShelfKind.File ? (ShelfList.IsMissing(item) ? "⚠ missing: " : "\U0001F4C4 ") : "✎ ";
            _ = SendMessageText(list, LbAddString, 0, kind + item.Label);
            _rowIds.Add(item.Id);
        }

        _ = SendMessageW(list, LbSetCurSel, keep is null ? (_rowIds.Count > 0 ? 0 : -1) : _rowIds.IndexOf(keep), 0);
        _ = SetWindowTextW(_controls[IdStatus], shelf.Items.Count == 0 ? "Nothing pinned. Copy text or files, then press “Pin copied text or files”." : $"{shelf.Items.Count} of {ShelfList.MaxItems} items");
        UpdateButtons();
    }

    private ShelfItem? Selected()
    {
        var index = (int)SendMessageW(_controls[IdList], LbGetCurSel, 0, 0);
        return index >= 0 && index < _rowIds.Count ? _owner.Shelf.Find(_rowIds[index]) : null;
    }

    private void UpdateButtons()
    {
        var item = Selected();
        _ = EnableWindow(_controls[IdCopy], item is not null && !(item.Kind == ShelfKind.File && ShelfList.IsMissing(item)));
        _ = EnableWindow(_controls[IdShow], item is { Kind: ShelfKind.File } && !ShelfList.IsMissing(item));
        _ = EnableWindow(_controls[IdRemove], item is not null);
        _ = EnableWindow(_controls[IdClear], _owner.Shelf.Items.Count > 0);
    }

    private void OnCommand(int id)
    {
        var item = Selected();
        switch (id)
        {
            case IdPin:
                var added = _owner.PinClipboard(Hwnd);
                if (added == 0)
                {
                    _ = SetWindowTextW(_controls[IdStatus], "The clipboard holds no text or files to pin.");
                    return;
                }

                Fill(null);
                return;
            case IdCopy when item is not null:
                var ok = item.Kind == ShelfKind.File ? Clipboard.SetFiles(Hwnd, [item.Value]) : Clipboard.SetText(Hwnd, item.Value);
                _owner.LogShelf("copy", ok, item.Kind);
                _ = SetWindowTextW(_controls[IdStatus], ok ? "Copied. Paste it where you need it." : "The clipboard is busy; try again.");
                return;
            case IdShow when item is { Kind: ShelfKind.File } && !ShelfList.IsMissing(item):
                // File Explorer with the item selected; the shelf itself never opens or runs the file.
                using (var explorer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { "/select,", item.Value } }))
                {
                }

                _owner.LogShelf("show", true, item.Kind);
                return;
            case IdRemove when item is not null:
                _owner.RemoveShelfItem(item.Id);
                Fill(null);
                return;
            case IdClear:
                _owner.ClearShelf();
                Fill(null);
                return;
        }
    }

    private nint? Handle(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (hwnd != Hwnd)
        {
            return null;
        }

        switch (msg)
        {
            case WmCommand:
                var id = (int)(wParam & 0xFFFF);
                if (id == IdList && ((wParam >> 16) & 0xFFFF) == LbnSelChange)
                {
                    UpdateButtons();
                    return 0;
                }

                if (id == IdClose)
                {
                    _ = DestroyWindow(hwnd);
                    return 0;
                }

                if (id is IdPin or IdCopy or IdShow or IdRemove or IdClear)
                {
                    OnCommand(id);
                    return 0;
                }

                return null;
            case WmCtlColorStatic:
                return GetSysColorBrush(ColorWindow);
            case WmClose:
                _ = DestroyWindow(hwnd);
                return 0;
            case WmDestroy:
                Hwnd = 0;
                return 0;
            default:
                return null;
        }
    }
}
