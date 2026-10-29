using System.Runtime.InteropServices;

namespace Halo.App.Interop;

/// <summary>Win32 entry points used by the island window. Internal: nothing outside the app calls Win32 directly.</summary>
internal static unsafe partial class Native
{
    public const uint WmDestroy = 0x0002;
    public const uint WmClose = 0x0010;
    public const uint WmTimeChange = 0x001E;
    public const uint WmSettingChange = 0x001A;
    public const uint WmDisplayChange = 0x007E;
    public const uint WmGetObject = 0x003D;
    public const uint WmTimer = 0x0113;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLButtonDown = 0x0201;
    public const uint WmLButtonUp = 0x0202;
    public const uint WmRButtonDown = 0x0204;
    public const uint WmRButtonUp = 0x0205;
    public const uint WmContextMenu = 0x007B;
    public const uint WmMouseLeave = 0x02A3;
    public const uint WmMouseWheel = 0x020A;
    public const uint WmHotkey = 0x0312;
    public const nint PbtApmSuspend = 0x4;
    public const uint WmCaptureChanged = 0x0215;
    public const uint WmPowerBroadcast = 0x0218;
    public const uint WmDpiChanged = 0x02E0;
    public const uint WmApp = 0x8000;
    public const uint SpiSetWorkArea = 0x002F;
    public const uint SpiGetClientAreaAnimation = 0x1042;
    public const nint PbtApmResumeAutomatic = 0x12;

    public const int WsPopup = unchecked((int)0x80000000);
    public const int WsExTopmost = 0x00000008;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExNoRedirectionBitmap = 0x00200000;

    public const int SwHide = 0;
    public const int SwShowNoActivate = 4;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public static readonly nint HwndTopmost = -1;
    public const uint TmeLeave = 0x00000002;
    public const uint MonitorDefaultToPrimary = 1;
    public const int RgnOr = 2;

    public const uint MfString = 0x0000;
    public const uint MfSeparator = 0x0800;
    public const uint TpmReturnCmd = 0x0100;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmTopAlign = 0x0000;
    public const uint TpmCenterAlign = 0x0004;
    public const uint TpmRightAlign = 0x0008;
    public const uint TpmBottomAlign = 0x0020;
    public const int SmMenuDropAlignment = 40;
    public const int SmCxSmIcon = 49;

    public const uint NimAdd = 0;
    public const uint NimModify = 1;
    public const uint NimDelete = 2;
    public const uint NimSetVersion = 4;
    public const uint NifMessage = 0x01;
    public const uint NifIcon = 0x02;
    public const uint NifTip = 0x04;
    public const uint NifShowTip = 0x80;
    public const uint NotifyIconVersion4 = 4;
    // Settings window: styles, standard-control messages and colours.
    public const uint WsExControlParent = 0x00010000;
    public const uint WsOverlapped = 0x00000000;
    public const uint WsSysMenu = 0x00080000;
    public const uint WsMinimizeBox = 0x00020000;
    public const long WsChild = 0x40000000;
    public const long WsVisible = 0x10000000;
    public const long WsTabStop = 0x00010000;
    public const long BsPushButton = 0x0;
    public const long BsDefPushButton = 0x1;
    public const long BsAutoCheckBox = 0x3;
    public const long TbsNoTicks = 0x10;
    public const string TrackbarClass = "msctls_trackbar32";
    public const uint WmCommand = 0x0111;
    public const uint WmHScroll = 0x0114;
    public const uint WmCtlColorStatic = 0x0138;
    public const uint WmSetFont = 0x0030;
    public const uint BmGetCheck = 0x00F0;
    public const uint BmSetCheck = 0x00F1;
    public const uint TbmGetPos = 0x0400;
    public const uint TbmSetPos = 0x0405;
    public const uint TbmSetRangeMin = 0x0407;
    public const uint TbmSetRangeMax = 0x0408;
    public const uint TbmSetPageSize = 0x0415;
    public const uint TbmSetLineSize = 0x0417;
    public const int SwShow = 5;
    public const int ColorWindow = 5;
    public const uint IccBarClasses = 0x4;
    public const uint WmWtsSessionChange = 0x02B1;
    public const nint WtsSessionLock = 0x7;
    public const nint WtsSessionUnlock = 0x8;
    public const nint PbtPowerSettingChange = 0x8013;
    public static readonly Guid GuidSessionDisplayStatus = new("2B84C20E-AD23-4ddf-93DB-05FFBD7EFCA5");
    public const nint PbtApmPowerStatusChange = 0xA;
    public static readonly Guid GuidAcDcPowerSource = new("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    public static readonly Guid GuidBatteryPercentageRemaining = new("A7AD8041-B45A-4CAE-87A3-EECBB468A9E1");
    public const uint EventSystemForeground = 0x0003;
    public const uint EventObjectLocationChange = 0x800B;
    public const uint WinEventOutOfContext = 0x0000;
    public const uint WinEventSkipOwnProcess = 0x0002;
    public const int ObjIdWindow = 0;
    public const uint AbmNew = 0;
    public const uint AbmRemove = 1;
    public const nint AbnFullscreenApp = 2;
    public const uint MonitorDefaultToNull = 0;
    public const int GwlStyle = -16;
    public const long WsCaption = 0x00C00000;
    public const int DwmwaCloaked = 14;
    public const uint MfChecked = 0x0008;
    public const uint MfGrayed = 0x0001;
    public const uint MfPopup = 0x0010;
    public const uint NinSelect = 0x0400;
    public const uint NinKeySelect = 0x0401;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint> WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public char* MenuName;
        public char* ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TrackMouseEventInfo
    {
        public uint Size;
        public uint Flags;
        public nint Hwnd;
        public uint HoverTime;
    }

    /// <summary>NOTIFYICONDATAW (shellapi.h), 976 bytes on x64.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NotifyIconData
    {
        public uint Size;
        public nint Hwnd;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint Icon;
        public fixed char Tip[128];
        public uint State;
        public uint StateMask;
        public fixed char Info[256];
        public uint Version;
        public fixed char InfoTitle[64];
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InitCommonControlsInfo
    {
        public uint Size;
        public uint Classes;
    }

    /// <summary>APPBARDATA (shellapi.h), 48 bytes on x64.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AppBarData
    {
        public uint Size;
        public nint Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public Rect Rect;
        public nint LParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint Mask;
        public nint Color;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    public static short LowWord(nint value) => (short)(value & 0xFFFF);

    public static short HighWord(nint value) => (short)((value >> 16) & 0xFFFF);

    /// <summary>Work area and effective DPI of the primary monitor.</summary>
    public static (Rect Work, uint Dpi) PrimaryWorkArea()
    {
        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        if (!GetMonitorInfoW(monitor, &info))
        {
            throw new InvalidOperationException("GetMonitorInfo failed.");
        }

        return (info.Work, GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX : 96u);
    }

    /// <summary>Sets the window region to the union of rounded rectangles (input and rendering are clipped to it).</summary>
    public static void SetRoundedRegion(nint hwnd, params ReadOnlySpan<(int Left, int Top, int Right, int Bottom, int Radius)> shapes)
    {
        nint combined = 0;
        foreach (var s in shapes)
        {
            var diameter = s.Radius * 2;
            var rgn = CreateRoundRectRgn(s.Left, s.Top, s.Right + 1, s.Bottom + 1, diameter, diameter);
            if (combined == 0)
            {
                combined = rgn;
            }
            else
            {
                _ = CombineRgn(combined, combined, rgn, RgnOr);
                _ = DeleteObject(rgn);
            }
        }

        // The system owns the region after a successful SetWindowRgn.
        if (SetWindowRgn(hwnd, combined, true) == 0)
        {
            _ = DeleteObject(combined);
        }
    }

    /// <summary>System "Show animations in Windows" setting (FR-ISL-13). Defaults to animations on if unreadable.</summary>
    public static bool AnimationsEnabled()
    {
        int enabled;
        return !SystemParametersInfoW(SpiGetClientAreaAnimation, 0, &enabled, 0) || enabled != 0;
    }

    [LibraryImport("CoreMessaging.dll")]
    public static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? name);

    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(WndClassEx* wc);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClassW(string className, nint instance);

    [LibraryImport("user32.dll")]
    public static partial nint CreateWindowExW(uint exStyle, char* className, char* windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowW(string className, string? windowName);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(Msg* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(Msg* msg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(Msg* msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hwnd, nuint id, uint ms, nint callback);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hwnd, nuint id);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(TrackMouseEventInfo* info);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    public static partial nint GetCapture();

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint monitor, MonitorInfo* info);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoW(uint action, uint param, void* value, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint menu, uint flags, nuint id, string? text);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenuDefaultItem(nint menu, uint item, uint byPosition);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, NotifyIconData* data);

    [LibraryImport("user32.dll")]
    public static partial nint CreateIconIndirect(IconInfo* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint icon);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, BitmapInfoHeader* info, uint usage, void** bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteGdiObject(nint handle);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, delegate* unmanaged[Stdcall]<nint, uint, nint, int, int, uint, uint, void> callback, uint processId, uint threadId, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("shell32.dll")]
    public static partial nuint SHAppBarMessage(uint message, AppBarData* data);

    [LibraryImport("shell32.dll")]
    public static partial int SHQueryUserNotificationState(out int state);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetClassNameW(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int attribute, void* value, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InitCommonControlsEx(InitCommonControlsInfo* info);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnableWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool enable);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hwnd, string text);

    [LibraryImport("user32.dll")]
    public static partial nint GetSysColorBrush(int index);

    // Tasks window (F-F05): edit, list box, combo box and date-time picker controls.
    public const long WsBorder = 0x00800000;
    public const long WsVScroll = 0x00200000;
    public const long EsAutoHScroll = 0x0080;
    public const long LbsNotify = 0x0001;
    public const long LbsNoIntegralHeight = 0x0100;
    public const long CbsDropDownList = 0x0003;
    public const long DtsShortDateFormat = 0x0000;
    public const long DtsTimeFormat = 0x0009;
    public const string DateTimePickClass = "SysDateTimePick32";
    public const uint IccDateClasses = 0x100;
    public const uint LbAddString = 0x0180;
    public const uint LbResetContent = 0x0184;
    public const uint LbSetCurSel = 0x0186;
    public const uint LbGetCurSel = 0x0188;
    public const uint LbnSelChange = 1;
    public const uint CbAddString = 0x0143;
    public const uint CbGetCurSel = 0x0147;
    public const uint CbSetCurSel = 0x014E;
    public const uint DtmGetSystemTime = 0x1001;
    public const uint DtmSetSystemTime = 0x1002;
    public const uint EmSetLimitText = 0x00C5;

    [StructLayout(LayoutKind.Sequential)]
    public struct SystemTime
    {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SendMessageText(nint hwnd, uint msg, nint wParam, string lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextLengthW(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextW(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    public const uint SpiGetHighContrast = 0x0042;
    public const uint SpiSetHighContrast = 0x0043;

    [StructLayout(LayoutKind.Sequential)]
    public struct HighContrast
    {
        public uint Size;
        public uint Flags;
        public nint DefaultScheme;
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SystemParametersInfoHighContrast(uint action, uint param, HighContrast* value, uint winIni);

    [LibraryImport("user32.dll")]
    public static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    /// <summary>The text of a window or control.</summary>
    public static string WindowText(nint hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        fixed (char* b = buffer)
        {
            var read = GetWindowTextW(hwnd, b, buffer.Length);
            return new string(b, 0, Math.Max(0, read));
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsDialogMessageW(nint dialog, Msg* msg);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(nint hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint RegisterPowerSettingNotification(nint recipient, Guid* powerSetting, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>True when the process has package identity (runs from the MSIX). APPMODEL_ERROR_NO_PACKAGE = 15700.</summary>
    public static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint length, char* name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterPowerSettingNotification(nint handle);

    /// <summary>Full rectangle of the primary monitor (not the work area) and its handle.</summary>
    public static (nint Monitor, Rect Bounds) PrimaryMonitor()
    {
        var monitor = MonitorFromPoint(default, MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        return (monitor, GetMonitorInfoW(monitor, &info) ? info.Monitor : default);
    }

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(nint hwnd, nint region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("gdi32.dll")]
    private static partial int CombineRgn(nint destination, nint source1, nint source2, int mode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);
}

/// <summary>SYSTEM_POWER_STATUS (only the fields Halo reads are named).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SystemPowerStatus
{
    public byte AcLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}
