using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// H-F02 on the desktop: nothing is captured until the user pins; pinned text and files go back on the real clipboard;
/// a deleted file is shown as missing; Clear all leaves no file behind; no content reaches the log.
/// </summary>
public sealed partial class IslandAppTests
{
    private const string ShelfText = "Halo shelf test snippet é你";

    [Fact]
    public void TheShelfPinsOnlyOnRequestAndPutsTextAndFilesBack()
    {
        var dir = Path.Combine(Path.GetTempPath(), "halo-shelf-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "pinned.txt");
        File.WriteAllText(file, "x");
        var saved = StaClipboard.GetText();
        try
        {
            StaClipboard.SetText(ShelfText);
            using var island = IslandProcess.Start();
            island.WaitFor("ready");
            Thread.Sleep(500);
            Assert.Empty(island.Events("shelf.pin")); // copying alone captures nothing
            Assert.False(File.Exists(Path.Combine(island.DataDir, "shelf.json")));

            using var automation = new UIA3Automation();
            var window = OpenShelf(island, automation);
            Button(window, "402").Invoke(); // Pin copied text or files
            var pin = island.WaitFor("shelf.pin");
            Assert.Equal(("text", 1), (pin["kind"]!.GetValue<string>(), pin["added"]!.GetValue<int>()));

            StaClipboard.SetText("something else");
            Button(window, "403").Invoke(); // Copy (the pinned text is selected)
            Assert.True(WaitUntil(() => StaClipboard.GetText() == ShelfText), "the pinned text is not back on the clipboard");

            StaClipboard.SetFiles(file);
            Button(window, "402").Invoke();
            Assert.Equal("file", island.WaitFor("shelf.pin", skip: 1)["kind"]!.GetValue<string>());
            var list = window.FindFirstDescendant(cf => cf.ByAutomationId("401"))!;
            Assert.True(WaitUntil(() => list.FindAllChildren().Any(i => i.Name.Contains("pinned.txt", StringComparison.Ordinal))));

            File.Delete(file);
            Button(window, "402").Invoke(); // re-pin refreshes the list; the deleted file shows as missing
            Assert.True(WaitUntil(() => list.FindAllChildren().Any(i => i.Name.Contains("missing", StringComparison.Ordinal))), string.Join(" | ", list.FindAllChildren().Select(i => i.Name)));
            Assert.False(File.Exists(file)); // never recreated

            Button(window, "406").Invoke(); // Clear all
            island.WaitFor("shelf.clear");
            Assert.False(File.Exists(Path.Combine(island.DataDir, "shelf.json")));
            Button(window, "2").Invoke();

            island.Exit();
            var log = File.ReadAllText(Path.Combine(island.DataDir, "logs", "halo.log"));
            Assert.DoesNotContain("shelf test snippet", log, StringComparison.Ordinal);
            Assert.DoesNotContain("pinned.txt", log, StringComparison.Ordinal);
            Assert.DoesNotContain(island.Events("shelf.pin").Select(e => e.ToJsonString()), j => j.Contains("snippet", StringComparison.Ordinal));
        }
        finally
        {
            StaClipboard.SetText(saved ?? string.Empty);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static AutomationElement OpenShelf(IslandProcess island, UIA3Automation automation)
    {
        // Open, Collapse, Settings…, Tasks and reminders…, Shelf… → Enter.
        OpenTrayMenuDirect(island);
        PressKeys(0x28, 0x28, 0x28, 0x28, 0x28, 0x0D);
        island.WaitFor("shelf.window");
        AutomationElement? window = null;
        Assert.True(WaitUntil(() => (window = automation.GetDesktop().FindFirstChild(cf => cf.ByName("Halo shelf"))) is not null, 5000), "no shelf window");
        return window!;
    }

    private static FlaUI.Core.Patterns.IInvokePattern Button(AutomationElement window, string id) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(id))!.Patterns.Invoke.Pattern;

    /// <summary>The clipboard from an STA thread (the test runner's threads are MTA).</summary>
    private static class StaClipboard
    {
        public static string? GetText() => Run(() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null);

        public static void SetText(string text) => Run<object?>(() =>
        {
            if (text.Length == 0)
            {
                System.Windows.Clipboard.Clear();
            }
            else
            {
                System.Windows.Clipboard.SetText(text);
            }

            return null;
        });

        public static void SetFiles(params string[] paths) => Run<object?>(() =>
        {
            var list = new System.Collections.Specialized.StringCollection();
            list.AddRange(paths);
            System.Windows.Clipboard.SetFileDropList(list);
            return null;
        });

        private static T Run<T>(Func<T> action)
        {
            T result = default!;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                for (var i = 0; i < 10; i++)
                {
                    try
                    {
                        result = action();
                        return;
                    }
                    catch (System.Runtime.InteropServices.COMException ex)
                    {
                        error = ex; // the clipboard is briefly held by another process
                        Thread.Sleep(50);
                    }
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return error is not null && result is null && typeof(T) != typeof(object) ? throw error : result;
        }
    }
}
