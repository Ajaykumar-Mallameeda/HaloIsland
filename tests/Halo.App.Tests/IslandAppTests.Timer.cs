using System.Collections.Concurrent;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// G-F01/G-F02/G-F03 and C-F02 on the desktop: the countdown in the pill, completion with an announcement, actions
/// through UI Automation, restart persistence, pages, focus phases, quiet mode and the tray submenu. Timers are
/// started with a diagnostics-only message; everything after that is the real path.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestTimer = 0x8000 + 53;
    private const uint WmTestQuiet = 0x8000 + 54;
    private const uint WmTestFocus = 0x8000 + 55;

    [Fact]
    public void ATimerCountsDownInThePillAndItsCompletionIsAnnounced()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var notes = new ConcurrentQueue<string>();
        using var handler = automation.GetDesktop().RegisterNotificationEvent(TreeScope.Subtree, (_, _, _, text, activity) =>
        {
            if (activity.StartsWith("Halo.", StringComparison.Ordinal))
            {
                notes.Enqueue(text);
            }
        });
        var element = automation.FromHandle(island.Hwnd);

        Assert.True(PostMessageW(island.Hwnd, WmTestTimer, 3, 0));
        Assert.Equal("started", island.WaitFor("timer")["state"]!.GetValue<string>());
        Assert.Equal("timer", island.WaitFor("activity")["primary"]!.GetValue<string>());
        Assert.True(WaitUntil(() => element.Name.Contains("Timer,", StringComparison.Ordinal)), element.Name);

        var done = island.WaitFor("timer", skip: 1);
        Assert.Equal("completed", done["state"]!.GetValue<string>());
        Assert.Equal("timer", island.Events("alert").Last(a => a["state"]!.GetValue<string>() == "shown")["kind"]!.GetValue<string>());
        Assert.True(WaitUntil(() => notes.Contains("Timer finished"), 5000), string.Join(" | ", notes));
        Assert.True(WaitUntil(() => element.Name.Contains("Timer finished", StringComparison.Ordinal)), element.Name);

        // Dismiss through the activity's own button (UIA Invoke), without taking focus.
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        var dismiss = WaitForChild(element, "Halo.Activity.Action.dismiss");
        Assert.Equal("Dismiss", dismiss.Name);
        dismiss.Patterns.Invoke.Pattern.Invoke();
        var action = island.WaitFor("activity.action");
        Assert.Equal(("dismiss", "done", "automation"), (action["action"]!.GetValue<string>(), action["result"]!.GetValue<string>(), action["source"]!.GetValue<string>()));
        Assert.True(WaitUntil(() => island.Events("activity").Last()["primary"]!.GetValue<string>() == "none"));
        Assert.True(WaitUntil(() => element.FindAllChildren().All(c => c.AutomationId != "Halo.Activity")));
    }

    [Fact]
    public void APausedTimerKeepsItsTimeAndResumes()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        Assert.True(PostMessageW(island.Hwnd, WmTestTimer, 120, 0));
        island.WaitFor("timer");
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");

        WaitForChild(element, "Halo.Activity.Action.pause").Patterns.Invoke.Pattern.Invoke();
        Assert.Equal("pause", island.WaitFor("activity.action")["action"]!.GetValue<string>());
        var paused = WaitForChild(element, "Halo.Activity");
        Assert.True(WaitUntil(() => paused.Name.StartsWith("Timer paused", StringComparison.Ordinal)), paused.Name);
        var value = paused.Patterns.RangeValue.Pattern.Value.Value;
        Thread.Sleep(2500);
        Assert.Equal(value, WaitForChild(element, "Halo.Activity").Patterns.RangeValue.Pattern.Value.Value); // no time passes while paused

        WaitForChild(element, "Halo.Activity.Action.resume").Patterns.Invoke.Pattern.Invoke();
        Assert.Equal("resume", island.WaitFor("activity.action", skip: 1)["action"]!.GetValue<string>());
        Assert.True(WaitUntil(() => WaitForChild(element, "Halo.Activity").Name.StartsWith("Timer,", StringComparison.Ordinal)));
    }

    [Fact]
    public void ARunningTimerSurvivesARestart()
    {
        // The harness deletes a run's folder when it ends, so the second run gets a copy of the first one's files.
        var dataDir = Path.Combine(Path.GetTempPath(), "halo-desktop-tests", Guid.NewGuid().ToString("N"));
        using (var island = IslandProcess.Start())
        {
            Assert.True(PostMessageW(island.Hwnd, WmTestTimer, 300, 0));
            island.WaitFor("timer");
            island.Exit();
            Directory.CreateDirectory(dataDir);
            foreach (var file in Directory.GetFiles(island.DataDir))
            {
                File.Copy(file, Path.Combine(dataDir, Path.GetFileName(file)));
            }
        }

        var saved = File.ReadAllText(Path.Combine(dataDir, "state.json"));
        Assert.Contains("\"Running\"", saved, StringComparison.Ordinal);
        Assert.Contains("endUtc", saved, StringComparison.Ordinal);

        using var again = IslandProcess.Start(dataDirectory: dataDir);
        var state = again.WaitFor("state");
        Assert.Equal(("loaded", 1), (state["status"]!.GetValue<string>(), state["timer"]!.GetValue<int>()));
        Assert.Equal("timer", again.WaitFor("activity")["primary"]!.GetValue<string>());
        using var automation = new UIA3Automation();
        var name = automation.FromHandle(again.Hwnd).Name;
        Assert.True(name.Contains("5 minutes left", StringComparison.Ordinal) || name.Contains("4 minutes left", StringComparison.Ordinal), name);
    }

    [Fact]
    public void TheOpenIslandHasPagesForTheActivityAndTheClock()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        Assert.True(PostMessageW(island.Hwnd, WmTestTimer, 600, 0));
        island.WaitFor("timer");
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");

        Assert.Equal("Activity, shown", WaitForChild(element, "Halo.Page.Activity").Name);
        var showClock = WaitForChild(element, "Halo.Page.Clock");
        Assert.Equal("Show clock", showClock.Name);
        showClock.Patterns.Invoke.Pattern.Invoke();
        Assert.Equal("clock", island.WaitFor("page")["page"]!.GetValue<string>());
        Assert.True(WaitUntil(() => element.FindAllChildren().Any(c => c.AutomationId == "Halo.Time")), "the clock page did not appear");
        Assert.DoesNotContain(element.FindAllChildren(), c => c.AutomationId == "Halo.Activity");

        // Collapsing and opening again starts on the activity page.
        element.Patterns.ExpandCollapse.Pattern.Collapse();
        island.WaitFor("transition.end", skip: 1);
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end", skip: 2);
        Assert.True(WaitUntil(() => element.FindAllChildren().Any(c => c.AutomationId == "Halo.Activity")));
    }

    [Fact]
    public void AFocusSessionMovesFromFocusToABreakWithAnAlert()
    {
        using var island = IslandProcess.Start();
        Assert.True(PostMessageW(island.Hwnd, WmTestFocus, 2, 0));
        Assert.Equal("started", island.WaitFor("focus")["state"]!.GetValue<string>());
        Assert.Equal("focus", island.WaitFor("activity")["primary"]!.GetValue<string>());
        var ended = island.WaitFor("focus", skip: 1);
        Assert.Equal(("phase_ended", "shortbreak"), (ended["state"]!.GetValue<string>(), ended["next"]!.GetValue<string>()));
        Assert.True(WaitUntil(() => island.Events("alert").Any(a => a["kind"]!.GetValue<string>() == "timer" && a["state"]!.GetValue<string>() == "shown")));
    }

    [Fact]
    public void QuietModeHoldsAlertsLetsTheTimerThroughAndSummarisesAtTheEnd()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);

        Assert.Equal(0, island.WaitFor("quiet")["active"]!.GetValue<int>()); // start-up: off
        Assert.True(PostMessageW(island.Hwnd, WmTestQuiet, 3, 0)); // until turned off
        Assert.Equal(1, island.WaitFor("quiet", skip: 1)["active"]!.GetValue<int>());
        Assert.True(WaitUntil(() => element.Name.Contains("Do Not Disturb on", StringComparison.Ordinal)), element.Name);

        RaiseTestAlert(island, priority: 2, durationMs: 1500);
        Assert.Equal("held_quiet", island.WaitFor("alert")["state"]!.GetValue<string>());

        Assert.True(PostMessageW(island.Hwnd, WmTestTimer, 1, 0));
        island.WaitFor("timer", skip: 1); // completed
        Assert.True(WaitUntil(() => island.Events("alert").Any(a => a["kind"]!.GetValue<string>() == "timer" && a["state"]!.GetValue<string>() == "shown")), island.Trace());

        Assert.True(PostMessageW(island.Hwnd, WmTestQuiet, 0, 0));
        var off = island.WaitFor("quiet", skip: 2);
        Assert.Equal((0, 1), (off["active"]!.GetValue<int>(), off["held"]!.GetValue<int>()));
        Assert.True(WaitUntil(() => island.Events("alert").Any(a => a["kind"]!.GetValue<string>() == "halo")), "no summary after quiet mode");
    }

    [Fact]
    public void TheTraySubmenuStartsATimer()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        // Open, Collapse, Settings…, Timer → Right opens the submenu → Enter on "Start 1 minute".
        OpenTrayMenuDirect(island);
        PressKeys(0x28, 0x28, 0x28, 0x28, 0x27, 0x0D);
        var started = island.WaitFor("timer");
        Assert.Equal((60, "tray"), (started["seconds"]!.GetValue<int>(), started["source"]!.GetValue<string>()));
    }

    private static AutomationElement WaitForChild(AutomationElement parent, string automationId)
    {
        AutomationElement? found = null;
        Assert.True(WaitUntil(() => (found = parent.FindAllChildren().FirstOrDefault(c => c.AutomationId == automationId)) is not null, 5000), $"no child {automationId}: {string.Join(",", parent.FindAllChildren().Select(c => c.AutomationId))}");
        return found!;
    }
}
