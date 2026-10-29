using Halo.App.Interop;
using Halo.App.Island;
using Xunit;

namespace Halo.App.Tests;

/// <summary>Every island window message has its own ID (a collision silently routes one feature's messages to another).</summary>
public class MessageIdTests
{
    [Fact]
    public void WindowMessagesAreDistinctAndInTheAppRange()
    {
        uint[] ids =
        [
            IslandWindow.WmCommandFromInstance,
            TrayIcon.CallbackMessage,
            FullscreenWatcher.AppBarMessage,
            MediaWatcher.DirtyMessage,
            MediaWatcher.SnapshotMessage,
            VolumeWatcher.ChangedMessage,
            VolumeWatcher.DeviceMessage,
            NetworkWatcher.ChangedMessage,
            NetworkWatcher.ReadingMessage,
            CameraWatcher.ReportMessage,
        ];
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, Native.WmApp, 0xBFFFu));
    }
}
