using System.Runtime.InteropServices;

namespace Halo.App.Interop;

#pragma warning disable SA1600, CA1711

[ComImport]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    [PreserveSig]
    int BindToHandler(nint bindContext, ref Guid handler, ref Guid riid, out nint result);
}

[ComImport]
[Guid("00000121-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropSource
{
    [PreserveSig]
    int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escapePressed, uint keyState);

    [PreserveSig]
    int GiveFeedback(uint effect);
}

#pragma warning restore SA1600, CA1711

/// <summary>
/// Drags a pinned file out of the shelf (H-F02) with the shell's own data object for that file, so every drop target
/// (File Explorer, mail, editors) receives an ordinary file drop. Only copy and link are offered, never move, so a drop
/// can never move or delete the original. Runs the OLE drag loop on the UI thread; returns when the drag ends.
/// </summary>
internal static class DragOut
{
    private const int DropEffectCopy = 1;
    private const int DropEffectLink = 4;
    private const int DragDropSDrop = 0x00040100;
    private const int DragDropSCancel = 0x00040101;
    private const int DragDropSUseDefaultCursors = 0x00040102;
    private const uint MkLButton = 0x0001;
    private static readonly Guid DataObjectHandler = new("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4"); // BHID_DataObject
    private static readonly Guid DataObjectIid = new("0000010e-0000-0000-C000-000000000046");
    private static readonly Guid ShellItemIid = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static bool s_oleReady;

    /// <summary>Drags the file. Returns the drop effect ("copy", "link", "none") or "failed" when the file has no data object.</summary>
    public static string Run(string path)
    {
        if (!s_oleReady)
        {
            // DoDragDrop needs OLE on the calling (STA) thread; harmless when COM is already initialised.
            s_oleReady = OleInitialize(0) >= 0;
        }

        var shellIid = ShellItemIid;
        if (SHCreateItemFromParsingName(path, 0, ref shellIid, out var obj) < 0 || obj is not IShellItem item)
        {
            return "failed";
        }

        nint dataObject = 0;
        try
        {
            var handler = DataObjectHandler;
            var iid = DataObjectIid;
            if (item.BindToHandler(0, ref handler, ref iid, out dataObject) < 0 || dataObject == 0)
            {
                return "failed";
            }

            var hr = DoDragDrop(dataObject, new Source(), DropEffectCopy | DropEffectLink, out var effect);
            return hr == DragDropSDrop ? ((effect & DropEffectLink) != 0 ? "link" : (effect & DropEffectCopy) != 0 ? "copy" : "none") : "none";
        }
        finally
        {
            if (dataObject != 0)
            {
                _ = Marshal.Release(dataObject);
            }

            _ = Marshal.ReleaseComObject(item);
        }
    }

    private sealed class Source : IDropSource
    {
        public int QueryContinueDrag(bool escapePressed, uint keyState) =>
            escapePressed ? DragDropSCancel : (keyState & MkLButton) == 0 ? DragDropSDrop : 0;

        public int GiveFeedback(uint effect) => DragDropSUseDefaultCursors;
    }

#pragma warning disable SYSLIB1054
    [DllImport("ole32.dll")]
    private static extern int OleInitialize(nint reserved);

    [DllImport("ole32.dll")]
    private static extern int DoDragDrop(nint dataObject, IDropSource dropSource, int okEffects, out int effect);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? item);
#pragma warning restore SYSLIB1054
}
