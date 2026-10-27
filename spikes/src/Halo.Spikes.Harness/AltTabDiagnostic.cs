using System.Diagnostics;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Halo.Spikes.Harness;

/// <summary>DEV diagnostic: prints the class names and item counts UIA sees while Alt+Tab is held (no item names).</summary>
internal static class AltTabDiagnostic
{
    public static int Run()
    {
        using var ui = new UiThread();
        var probe = new Probe(ui);
        var p = probe.TextBoxCenter;
        PointerInput.ClickAt(p.X, p.Y, 1920, 1080);
        Thread.Sleep(500);
        using var automation = new UIA3Automation();
        var desktop = automation.GetDesktop();
        var before = desktop.FindAllChildren().Select(c => c.Properties.ClassName.ValueOrDefault ?? "?").ToHashSet();
        Keyboard.Press(VirtualKeyShort.ALT);
        try
        {
            Keyboard.Type(VirtualKeyShort.TAB);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(3))
            {
                Thread.Sleep(300);
                var now = desktop.FindAllChildren();
                var fresh = now.Where(c => !before.Contains(c.Properties.ClassName.ValueOrDefault ?? "?")).ToList();
                Console.WriteLine($"t={sw.ElapsedMilliseconds}ms new top-level classes: " + string.Join(",", fresh.Select(c => $"{c.Properties.ClassName.ValueOrDefault}[listitems={c.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Length}]")));
                foreach (var cls in new[] { "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "TaskSwitcherWnd" })
                {
                    var w = desktop.FindFirstChild(cf => cf.ByClassName(cls));
                    Console.WriteLine($"   {cls}: {(w is null ? "absent" : $"present, listitems={w.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Length}, descendants={w.FindAllDescendants().Length}")}");
                }
            }

            Keyboard.Type(VirtualKeyShort.ESCAPE);
        }
        finally
        {
            Keyboard.Release(VirtualKeyShort.ALT);
        }

        probe.Close();
        return 0;
    }
}
