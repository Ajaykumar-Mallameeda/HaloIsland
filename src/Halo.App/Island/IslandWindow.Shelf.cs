using Halo.App.Interop;
using Halo.Core.Diagnostics;
using Halo.Core.Shelf;
using Halo.Core.Storage;

namespace Halo.App.Island;

/// <summary>The shelf (H-F02) as the island owns it: the list, its file and the window. Diagnostics carry counts and kinds only.</summary>
internal sealed partial class IslandWindow
{
    private readonly ShelfList _shelf = new();
    private ShelfStore? _shelfStore;
    private ShelfWindow? _shelfWindow;

    internal ShelfList Shelf => _shelf;

    private void InitShelf()
    {
        _shelfStore = new ShelfStore(_store.Directory);
        DataLoadStatus status;
        try
        {
            status = _shelfStore.Load(_shelf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status = DataLoadStatus.Unreadable;
        }

        _log?.Write("shelf", ("status", status.ToString().ToLowerInvariant()), ("count", _shelf.Items.Count));
    }

    private void OpenShelfWindow()
    {
        _shelfWindow = ShelfWindow.Open(this, _shelfWindow);
        _log?.Write("shelf.window", ("state", "open"));
    }

    /// <summary>Reads the clipboard once, because the user pressed the button. Returns the number of items pinned.</summary>
    internal int PinClipboard(nint owner)
    {
        var (text, files) = Clipboard.Read(owner);
        var added = files.Count > 0 ? _shelf.AddFiles(files).Count : _shelf.AddText(text) is null ? 0 : 1;
        _log?.Write("shelf.pin", ("kind", files.Count > 0 ? "file" : text is null ? "none" : "text"), ("added", added), ("count", _shelf.Items.Count));
        SaveShelf();
        return added;
    }

    internal void RemoveShelfItem(string id)
    {
        var removed = _shelf.Remove(id);
        _log?.Write("shelf.remove", ("removed", removed ? 1 : 0), ("count", _shelf.Items.Count));
        SaveShelf();
    }

    internal void ClearShelf()
    {
        _shelf.Clear();
        _log?.Write("shelf.clear", ("count", 0));
        try
        {
            _shelfStore?.Delete(); // "clear all" leaves nothing behind, backup included
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _fileLog?.Write(LogLevel.Warning, "shelf.clear_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
        }
    }

    internal void LogShelf(string action, bool ok, ShelfKind kind) =>
        _log?.Write("shelf." + action, ("kind", kind == ShelfKind.File ? "file" : "text"), ("result", ok ? "done" : "failed"));

    private void SaveShelf()
    {
        try
        {
            _ = _shelfStore?.Save(_shelf);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _fileLog?.Write(LogLevel.Warning, "shelf.save_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
        }
    }
}
