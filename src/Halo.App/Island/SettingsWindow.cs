using System.Runtime.InteropServices;
using Halo.Core.Settings;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// The settings window (FR-APP-02, M2-F02): a small native window of standard Win32 controls, so keyboard use
/// (Tab, Space, arrows, Esc) and UI Automation names come from Windows itself. Every change applies at once through
/// <see cref="IslandWindow.UpdateSettings"/>, which also saves it (debounced). Only settings with an effect appear.
/// Runs on the island's UI thread; the island's message loop routes its messages through IsDialogMessage.
/// </summary>
internal sealed unsafe class SettingsWindow : IDisposable
{
    public const string ClassName = "ProjectHalo.Settings";
    public const string Title = "Halo settings";

    private const int IdHover = 101;
    private const int IdDwell = 102;
    private const int IdGrace = 103;
    private const int IdFullscreen = 104;
    private const int IdMotion = 105;
    private const int IdStartExpanded = 106;
    private const int IdDefaults = 107;
    private const int IdLogFolder = 108;
    private const int IdAnnounce = 109;
    private const int IdMedia = 110;
    private const int IdVolume = 111;
    private const int IdBattery = 112;
    private const int IdClose = 2; // IDCANCEL: Esc and the Close button
    private const int IdDwellValue = 202;
    private const int IdGraceValue = 203;
    private const int DwellStep = 10;
    private const int GraceStep = 50;

    private static SettingsWindow? s_current;
    private static bool s_registered;

    private readonly IslandWindow _owner;
    private readonly Dictionary<int, nint> _controls = [];
    private nint _font;
    private bool _updating;

    private SettingsWindow(IslandWindow owner) => _owner = owner;

    public nint Hwnd { get; private set; }

    public bool IsOpen => Hwnd != 0 && IsWindow(Hwnd);

    /// <summary>Opens the window, or brings the open one forward.</summary>
    public static SettingsWindow Open(IslandWindow owner, SettingsWindow? existing)
    {
        if (existing is { IsOpen: true })
        {
            _ = SetForegroundWindow(existing.Hwnd);
            return existing;
        }

        var window = new SettingsWindow(owner);
        window.Create();
        return window;
    }

    /// <summary>Gives the island's message loop the chance to handle keyboard navigation for this window.</summary>
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
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            // A failed change must not take the island down (NFR-05).
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private void Create()
    {
        s_current = this;
        var instance = GetModuleHandleW(null);
        var icc = new InitCommonControlsInfo { Size = (uint)sizeof(InitCommonControlsInfo), Classes = IccBarClasses };
        _ = InitCommonControlsEx(&icc);
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

            var dpi = PrimaryWorkArea().Dpi;
            var (w, h) = (Scale(440, dpi), Scale(454, dpi));
            var (work, _) = PrimaryWorkArea();
            var x = work.Left + ((work.Right - work.Left - w) / 2);
            var y = work.Top + ((work.Bottom - work.Top - h) / 2);
            Hwnd = CreateWindowExW(WsExControlParent, className, title, WsOverlapped | (uint)WsCaption | WsSysMenu | WsMinimizeBox, x, y, w, h, 0, 0, instance, 0);
        }

        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for the settings window.");
        }

        BuildControls();
        Show(_owner.Settings);
        _ = ShowWindow(Hwnd, SwShow);
        _ = SetForegroundWindow(Hwnd);
    }

    private static int Scale(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    private void BuildControls()
    {
        var dpi = GetDpiForWindow(Hwnd);
        _font = CreateFontW(-(int)Math.Round(9 * dpi / 72.0), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        int S(int dip) => Scale(dip, dpi);
        var y = 16;
        Add(IdHover, "BUTTON", "&Expand when the pointer rests on the island", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 32;
        Add(0, "STATIC", "&Hover delay (ms):", 0, 16, y + 4, 130, 20);
        Add(IdDwell, TrackbarClass, string.Empty, WsTabStop | TbsNoTicks, 150, y, 200, 28);
        Add(IdDwellValue, "STATIC", string.Empty, 0, 356, y + 4, 60, 20);
        y += 36;
        Add(0, "STATIC", "&Collapse delay (ms):", 0, 16, y + 4, 130, 20);
        Add(IdGrace, TrackbarClass, string.Empty, WsTabStop | TbsNoTicks, 150, y, 200, 28);
        Add(IdGraceValue, "STATIC", string.Empty, 0, 356, y + 4, 60, 20);
        y += 44;
        Add(IdFullscreen, "BUTTON", "Hide while a &full-screen app is in front", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 30;
        Add(IdMotion, "BUTTON", "&Reduce motion (instant transitions)", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 30;
        Add(IdStartExpanded, "BUTTON", "&Open the island when Halo starts", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 30;
        Add(IdAnnounce, "BUTTON", "&Announce alerts to screen readers", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 30;
        Add(IdMedia, "BUTTON", "Show what is &playing (title, artist, artwork)", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 30;
        Add(IdVolume, "BUTTON", "Show &volume changes and the volume slider", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 30;
        Add(IdBattery, "BUTTON", "Show &battery level and charger or low-battery alerts", BsAutoCheckBox | WsTabStop, 16, y, 400, 22);
        y += 42;
        Add(IdDefaults, "BUTTON", "Restore &defaults", BsPushButton | WsTabStop, 16, y, 130, 28);
        Add(IdLogFolder, "BUTTON", "Open &log folder", BsPushButton | WsTabStop, 158, y, 130, 28);
        // Not a default button: the Enter that picked "Settings…" in the tray menu reaches this window as WM_CHAR,
        // and a default Close button would shut the window the moment it opened. Esc (IDCANCEL) still closes it.
        Add(IdClose, "BUTTON", "Close", BsPushButton | WsTabStop, 300, y, 110, 28);

        SetRange(IdDwell, HaloSettings.MinDwellMs, HaloSettings.MaxDwellMs, DwellStep);
        SetRange(IdGrace, HaloSettings.MinGraceMs, HaloSettings.MaxGraceMs, GraceStep);

        void Add(int id, string cls, string text, long style, int x, int top, int width, int height)
        {
            var child = CreateWindowExW(0, cls, text, (uint)(WsChild | WsVisible | style), S(x), S(top), S(width), S(height), Hwnd, id, GetModuleHandleW(null), 0);
            _ = SendMessageW(child, WmSetFont, _font, 1);
            if (id != 0)
            {
                _controls[id] = child;
            }
        }
    }

    private void SetRange(int id, int min, int max, int step)
    {
        var bar = _controls[id];
        _ = SendMessageW(bar, TbmSetRangeMin, 0, min);
        _ = SendMessageW(bar, TbmSetRangeMax, 1, max);
        _ = SendMessageW(bar, TbmSetLineSize, 0, step);
        _ = SendMessageW(bar, TbmSetPageSize, 0, step * 5);
    }

    /// <summary>Puts settings values into the controls without treating them as user changes.</summary>
    private void Show(HaloSettings s)
    {
        _updating = true;
        try
        {
            Check(IdHover, s.HoverToExpand);
            Check(IdFullscreen, s.HideInFullscreen);
            Check(IdMotion, s.ReduceMotion);
            Check(IdStartExpanded, s.StartExpanded);
            Check(IdAnnounce, s.AnnounceAlerts);
            Check(IdMedia, s.ShowMedia);
            Check(IdVolume, s.ShowVolume);
            Check(IdBattery, s.ShowBattery);
            _ = SendMessageW(_controls[IdDwell], TbmSetPos, 1, s.HoverDwellMs);
            _ = SendMessageW(_controls[IdGrace], TbmSetPos, 1, s.LeaveGraceMs);
            _ = EnableWindow(_controls[IdDwell], s.HoverToExpand);
            _ = SetWindowTextW(_controls[IdDwellValue], $"{s.HoverDwellMs} ms");
            _ = SetWindowTextW(_controls[IdGraceValue], $"{s.LeaveGraceMs} ms");
        }
        finally
        {
            _updating = false;
        }
    }

    private void Check(int id, bool on) => _ = SendMessageW(_controls[id], BmSetCheck, on ? 1 : 0, 0);

    private bool IsChecked(int id) => SendMessageW(_controls[id], BmGetCheck, 0, 0) == 1;

    private int Position(int id, int step)
    {
        var raw = (int)SendMessageW(_controls[id], TbmGetPos, 0, 0);
        return (int)Math.Round(raw / (double)step) * step;
    }

    /// <summary>Reads every control into a settings value and applies it.</summary>
    private void Commit()
    {
        if (_updating)
        {
            return;
        }

        var s = _owner.Settings with
        {
            HoverToExpand = IsChecked(IdHover),
            HoverDwellMs = Math.Clamp(Position(IdDwell, DwellStep), HaloSettings.MinDwellMs, HaloSettings.MaxDwellMs),
            LeaveGraceMs = Math.Clamp(Position(IdGrace, GraceStep), HaloSettings.MinGraceMs, HaloSettings.MaxGraceMs),
            HideInFullscreen = IsChecked(IdFullscreen),
            ReduceMotion = IsChecked(IdMotion),
            StartExpanded = IsChecked(IdStartExpanded),
            AnnounceAlerts = IsChecked(IdAnnounce),
            ShowMedia = IsChecked(IdMedia),
            ShowVolume = IsChecked(IdVolume),
            ShowBattery = IsChecked(IdBattery),
        };
        _owner.UpdateSettings(s);
        Show(s);
    }

    /// <summary>FR-APP-07: shows the local log folder in File Explorer (nothing is uploaded).</summary>
    private void OpenLogFolder()
    {
        if (_owner.LogDirectory is not { } folder)
        {
            return;
        }

        _ = System.IO.Directory.CreateDirectory(folder);
        using var explorer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true });
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
                switch (id)
                {
                    case IdHover or IdFullscreen or IdMotion or IdStartExpanded or IdAnnounce or IdMedia or IdVolume or IdBattery:
                        Commit();
                        return 0;
                    case IdDefaults:
                        _owner.UpdateSettings(HaloSettings.Defaults);
                        Show(HaloSettings.Defaults);
                        return 0;
                    case IdLogFolder:
                        OpenLogFolder();
                        return 0;
                    case IdClose:
                        _ = DestroyWindow(hwnd);
                        return 0;
                }

                return null;
            case WmHScroll:
                Commit();
                return 0;
            case WmCtlColorStatic:
                // Labels and check boxes on the window background colour.
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
