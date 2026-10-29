using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// H-F02 drag-out on the desktop: a real pointer drag from the shelf's list into a WPF drop target in the test
/// process delivers an ordinary file drop with copy (never move), and the original file stays where it was.
/// </summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void APinnedFileCanBeDraggedOutAsACopyAndTheOriginalStays()
    {
        var dir = Path.Combine(Path.GetTempPath(), "halo-shelf-drag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "drag-me.txt");
        File.WriteAllText(file, "x");
        var saved = StaClipboard.GetText();
        using var target = DropTarget.Show(left: 60, top: 420, width: 320, height: 220);
        GetCursorPos(out var cursor);
        try
        {
            using var island = IslandProcess.Start();
            island.WaitFor("ready");
            using var automation = new UIA3Automation();
            StaClipboard.SetFiles(file);
            var window = OpenShelf(island, automation);
            Button(window, "402").Invoke(); // pin the copied file
            island.WaitFor("shelf.pin");
            var list = window.FindFirstDescendant(cf => cf.ByAutomationId("401"))!;
            FlaUI.Core.AutomationElements.AutomationElement? row = null;
            Assert.True(WaitUntil(() => (row = list.FindAllChildren().FirstOrDefault(i => i.Name.Contains("drag-me.txt", StringComparison.Ordinal))) is not null), "the file is not listed");

            var from = row!.BoundingRectangle;
            var (sx, sy) = (from.Left + 40, from.Top + (from.Height / 2));
            var (tx, ty) = (target.CenterX, target.CenterY);
            SetCursorPos(sx, sy);
            Thread.Sleep(150);
            MouseButton(down: true);
            for (var i = 1; i <= 30; i++)
            {
                SetCursorPos(sx + ((tx - sx) * i / 30), sy + ((ty - sy) * i / 30));
                Thread.Sleep(25);
            }

            Thread.Sleep(200);
            MouseButton(down: false);

            var drag = island.WaitFor("shelf.drag");
            Assert.Equal(("done", "copy"), (drag["result"]!.GetValue<string>(), drag["effect"]!.GetValue<string>()));
            Assert.True(target.Dropped.TryTake(out var paths, 5000, TestContext.Current.CancellationToken), "the drop target received nothing");
            Assert.Equal([file], paths);
            Assert.True(File.Exists(file), "the original file was moved or deleted");
            Button(window, "2").Invoke();
        }
        finally
        {
            MouseButton(down: false);
            SetCursorPos(cursor.X, cursor.Y);
            StaClipboard.SetText(saved ?? string.Empty);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void MouseButton(bool down)
    {
        Input[] inputs = [new() { Type = 0, Mouse = new MouseInput { Flags = down ? 0x0002u : 0x0004u } }];
        _ = SendInput(1, inputs, Marshal.SizeOf<Input>());
    }

    /// <summary>A WPF window on its own STA thread that accepts file drops (copy only) and reports the paths.</summary>
    private sealed class DropTarget : IDisposable
    {
        private System.Windows.Window? _window;
        private System.Windows.Threading.Dispatcher? _dispatcher;
        private Thread? _thread;

        public BlockingCollection<string[]> Dropped { get; } = [];

        public int CenterX { get; private set; }

        public int CenterY { get; private set; }

        public static DropTarget Show(int left, int top, int width, int height)
        {
            var t = new DropTarget();
            using var ready = new ManualResetEventSlim();
            t._thread = new Thread(() =>
            {
                var w = new System.Windows.Window
                {
                    Title = "Halo drop target",
                    WindowStyle = System.Windows.WindowStyle.ToolWindow,
                    Topmost = true,
                    AllowDrop = true,
                    ShowActivated = true,
                    Content = new System.Windows.Controls.TextBlock { Text = "Drop here" },
                    Width = width,
                    Height = height,
                    Left = left,
                    Top = top,
                    Background = System.Windows.Media.Brushes.DimGray,
                };
                w.DragOver += (_, e) =>
                {
                    e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
                    e.Handled = true;
                };
                w.Drop += (_, e) =>
                {
                    e.Effects = System.Windows.DragDropEffects.Copy;
                    if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
                    {
                        t.Dropped.Add(files);
                    }

                    e.Handled = true;
                };
                w.Loaded += (_, _) =>
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                    _ = GetWindowRect(handle, out var r);
                    (t.CenterX, t.CenterY) = ((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                    ready.Set();
                };
                t._window = w;
                t._dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                w.Show();
                System.Windows.Threading.Dispatcher.Run();
            });
            t._thread.SetApartmentState(ApartmentState.STA);
            t._thread.IsBackground = true; // never keeps the test host alive
            t._thread.Start();
            Assert.True(ready.Wait(10_000), "the drop target did not appear");
            return t;
        }

        public void Dispose()
        {
            _dispatcher?.Invoke(() => _window?.Close());
            _dispatcher?.InvokeShutdown();
            _thread?.Join(5000);
            Dropped.Dispose();
        }
    }
}
