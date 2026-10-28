using System.Globalization;
using System.Runtime.InteropServices;
using Halo.Core.Tasks;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// The to-do list and reminders window (F-F04/F-F05): standard Win32 controls, so keyboard use and UI Automation names
/// come from Windows. Every change goes through the island (<see cref="IslandWindow"/>), which saves <c>tasks.json</c>
/// and re-arms the reminder timer. Runs on the island's UI thread.
/// </summary>
internal sealed unsafe class TasksWindow : IDisposable
{
    public const string ClassName = "ProjectHalo.Tasks";
    public const string Title = "Halo tasks and reminders";

    private const int IdList = 301;
    private const int IdText = 302;
    private const int IdRemind = 303;
    private const int IdDate = 304;
    private const int IdTime = 305;
    private const int IdRepeat = 306;
    private const int IdAdd = 307;
    private const int IdSave = 308;
    private const int IdComplete = 309;
    private const int IdDelete = 310;
    private const int IdSummary = 311;
    private const int IdNew = 312;
    private const int IdClose = 2; // IDCANCEL

    private static TasksWindow? s_current;
    private static bool s_registered;

    private readonly IslandWindow _owner;
    private readonly Dictionary<int, nint> _controls = [];
    private readonly List<string> _rowIds = [];
    private nint _font;

    private TasksWindow(IslandWindow owner) => _owner = owner;

    public nint Hwnd { get; private set; }

    public bool IsOpen => Hwnd != 0 && IsWindow(Hwnd);

    public static TasksWindow Open(IslandWindow owner, TasksWindow? existing)
    {
        if (existing is { IsOpen: true })
        {
            _ = SetForegroundWindow(existing.Hwnd);
            return existing;
        }

        var window = new TasksWindow(owner);
        window.Create();
        return window;
    }

    public bool PreTranslate(Msg* msg) => IsOpen && IsDialogMessageW(Hwnd, msg);

    /// <summary>Redraws the list after a change made elsewhere (a reminder snoozed or completed from the island).</summary>
    public void Refresh()
    {
        if (IsOpen)
        {
            Fill(SelectedId());
        }
    }

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

    /// <summary>One line in the list: "[ ] text — due Thu 3:00 PM (daily)". Done items are marked and listed last.</summary>
    internal static string Row(TaskItem item, TimeZoneInfo zone)
    {
        var box = item.Done ? "☑" : "☐";
        var due = item.DueUtc is { } d ? " — " + TimeZoneInfo.ConvertTime(d, zone).ToString("ddd d MMM, t", CultureInfo.CurrentCulture) : string.Empty;
        var repeat = item.Recurrence switch { Recurrence.Daily => " (daily)", Recurrence.Weekly => " (weekly)", _ => string.Empty };
        var snoozed = item.SnoozedUntilUtc is { } s && !item.Done ? " · snoozed to " + TimeZoneInfo.ConvertTime(s, zone).ToString("t", CultureInfo.CurrentCulture) : string.Empty;
        return $"{box} {item.Text}{due}{repeat}{snoozed}";
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

    private static int Scale(int dip, uint dpi) => (int)Math.Round(dip * dpi / 96.0);

    private void Create()
    {
        s_current = this;
        var instance = GetModuleHandleW(null);
        var icc = new InitCommonControlsInfo { Size = (uint)sizeof(InitCommonControlsInfo), Classes = IccDateClasses };
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

            var (work, dpi) = PrimaryWorkArea();
            var (w, h) = (Scale(500, dpi), Scale(470, dpi));
            var x = work.Left + ((work.Right - work.Left - w) / 2);
            var y = work.Top + ((work.Bottom - work.Top - h) / 2);
            Hwnd = CreateWindowExW(WsExControlParent, className, title, WsOverlapped | (uint)WsCaption | WsSysMenu | WsMinimizeBox, x, y, w, h, 0, 0, instance, 0);
        }

        if (Hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed for the tasks window.");
        }

        BuildControls();
        Fill(null);
        ShowSelection();
        _ = ShowWindow(Hwnd, SwShow);
        _ = SetForegroundWindow(Hwnd);
        _ = SetFocus(_controls[IdText]);
    }

    private void BuildControls()
    {
        var dpi = GetDpiForWindow(Hwnd);
        _font = CreateFontW(-(int)Math.Round(9 * dpi / 72.0), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        int S(int dip) => Scale(dip, dpi);
        Add(0, "STATIC", "&Tasks:", 0, 16, 14, 300, 20);
        Add(IdList, "LISTBOX", string.Empty, WsTabStop | WsBorder | WsVScroll | LbsNotify | LbsNoIntegralHeight, 16, 36, 452, 190);
        Add(IdSummary, "STATIC", string.Empty, 0, 16, 232, 452, 20);
        Add(0, "STATIC", "Task &text:", 0, 16, 262, 90, 20);
        Add(IdText, "EDIT", string.Empty, WsTabStop | WsBorder | EsAutoHScroll, 110, 258, 358, 26);
        Add(IdRemind, "BUTTON", "&Remind me on", BsAutoCheckBox | WsTabStop, 16, 296, 120, 24);
        Add(IdDate, DateTimePickClass, string.Empty, WsTabStop | DtsShortDateFormat, 140, 294, 150, 26);
        Add(IdTime, DateTimePickClass, string.Empty, WsTabStop | DtsTimeFormat, 300, 294, 168, 26);
        Add(0, "STATIC", "Re&peat:", 0, 16, 334, 90, 20);
        Add(IdRepeat, "COMBOBOX", string.Empty, WsTabStop | CbsDropDownList | WsVScroll, 110, 330, 180, 120);
        Add(IdAdd, "BUTTON", "&Add", BsPushButton | WsTabStop, 16, 374, 80, 28);
        Add(IdSave, "BUTTON", "&Save changes", BsPushButton | WsTabStop, 104, 374, 110, 28);
        Add(IdComplete, "BUTTON", "&Complete", BsPushButton | WsTabStop, 222, 374, 90, 28);
        Add(IdDelete, "BUTTON", "De&lete", BsPushButton | WsTabStop, 320, 374, 70, 28);
        Add(IdNew, "BUTTON", "&New", BsPushButton | WsTabStop, 398, 374, 70, 28);
        Add(IdClose, "BUTTON", "Close", BsPushButton | WsTabStop, 388, 412, 80, 28);
        _ = SendMessageW(_controls[IdText], EmSetLimitText, TaskItem.MaxTextLength, 0);
        foreach (var label in new[] { "Does not repeat", "Every day", "Every week" })
        {
            _ = SendMessageText(_controls[IdRepeat], CbAddString, 0, label);
        }

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

    /// <summary>Fills the list: open items (earliest due first), then done items. Keeps the selection by ID.</summary>
    private void Fill(string? keep)
    {
        var tasks = _owner.Tasks;
        var zone = TimeZoneInfo.Local;
        var list = _controls[IdList];
        _ = SendMessageW(list, LbResetContent, 0, 0);
        _rowIds.Clear();
        foreach (var item in tasks.Pending.Concat(tasks.Items.Where(i => i.Done).OrderByDescending(i => i.CompletedUtc)))
        {
            _ = SendMessageText(list, LbAddString, 0, Row(item, zone));
            _rowIds.Add(item.Id);
        }

        var index = keep is null ? -1 : _rowIds.IndexOf(keep);
        _ = SendMessageW(list, LbSetCurSel, index, 0);
        var next = tasks.NextDue is { DueUtc: { } d } ? " · next due " + TimeZoneInfo.ConvertTime(d, zone).ToString("ddd t", CultureInfo.CurrentCulture) : string.Empty;
        _ = SetWindowTextW(_controls[IdSummary], tasks.PendingCount == 1 ? "1 open task" + next : $"{tasks.PendingCount} open tasks{next}");
    }

    private string? SelectedId()
    {
        var index = (int)SendMessageW(_controls[IdList], LbGetCurSel, 0, 0);
        return index >= 0 && index < _rowIds.Count ? _rowIds[index] : null;
    }

    /// <summary>Puts the selected item (or a new one, due in an hour) into the editing controls.</summary>
    private void ShowSelection()
    {
        var item = SelectedId() is { } id ? _owner.Tasks.Find(id) : null;
        _ = SetWindowTextW(_controls[IdText], item?.Text ?? string.Empty);
        var due = item?.DueUtc is { } d ? TimeZoneInfo.ConvertTime(d, TimeZoneInfo.Local) : RoundUp(DateTimeOffset.Now.AddHours(1));
        SetDateTime(due);
        _ = SendMessageW(_controls[IdRemind], BmSetCheck, item?.DueUtc is not null ? 1 : 0, 0);
        _ = SendMessageW(_controls[IdRepeat], CbSetCurSel, (int)(item?.Recurrence ?? Recurrence.None), 0);
        _ = EnableWindow(_controls[IdSave], item is not null);
        _ = EnableWindow(_controls[IdComplete], item is { Done: false });
        _ = EnableWindow(_controls[IdDelete], item is not null);
    }

    private static DateTimeOffset RoundUp(DateTimeOffset t) => new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, 0, 0, t.Offset).AddMinutes(t.Minute > 0 ? 60 : 0);

    private void SetDateTime(DateTimeOffset local)
    {
        var st = new SystemTime { Year = (ushort)local.Year, Month = (ushort)local.Month, Day = (ushort)local.Day, Hour = (ushort)local.Hour, Minute = (ushort)local.Minute };
        _ = SendMessageW(_controls[IdDate], DtmSetSystemTime, 0, (nint)(&st));
        _ = SendMessageW(_controls[IdTime], DtmSetSystemTime, 0, (nint)(&st));
    }

    /// <summary>The chosen local date and time as UTC, or null when no reminder is wanted.</summary>
    private DateTimeOffset? ReadDue()
    {
        if (SendMessageW(_controls[IdRemind], BmGetCheck, 0, 0) != 1)
        {
            return null;
        }

        SystemTime date, time;
        _ = SendMessageW(_controls[IdDate], DtmGetSystemTime, 0, (nint)(&date));
        _ = SendMessageW(_controls[IdTime], DtmGetSystemTime, 0, (nint)(&time));
        var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.Local;
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private Recurrence ReadRepeat() => (int)SendMessageW(_controls[IdRepeat], CbGetCurSel, 0, 0) switch
    {
        1 => Recurrence.Daily,
        2 => Recurrence.Weekly,
        _ => Recurrence.None,
    };

    private void OnCommand(int id)
    {
        var selected = SelectedId();
        string? keep = selected;
        switch (id)
        {
            case IdAdd:
                keep = _owner.AddTask(WindowText(_controls[IdText]), ReadDue(), ReadRepeat())?.Id;
                if (keep is null)
                {
                    _ = MessageBeep(0x30);
                    return;
                }

                break;
            case IdSave when selected is not null:
                _ = _owner.EditTask(selected, WindowText(_controls[IdText]), ReadDue(), ReadRepeat());
                break;
            case IdComplete when selected is not null:
                _ = _owner.CompleteTask(selected);
                break;
            case IdDelete when selected is not null:
                _ = _owner.DeleteTask(selected);
                keep = null;
                break;
            case IdNew:
                keep = null;
                break;
            default:
                return;
        }

        Fill(keep);
        ShowSelection();
        if (id is IdAdd or IdNew)
        {
            _ = SetFocus(_controls[IdText]);
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
                var code = (uint)((wParam >> 16) & 0xFFFF);
                if (id == IdList && code == LbnSelChange)
                {
                    ShowSelection();
                    return 0;
                }

                if (id == IdClose)
                {
                    _ = DestroyWindow(hwnd);
                    return 0;
                }

                if (id is IdAdd or IdSave or IdComplete or IdDelete or IdNew)
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

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint type);
#pragma warning restore SYSLIB1054
}
