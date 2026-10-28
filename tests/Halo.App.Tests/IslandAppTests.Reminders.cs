using System.Collections.Concurrent;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// F-F04/F-F05 and D-F01/D-F04 on the desktop: due reminders through the notification framework, privacy of task
/// text, snooze and dismiss through UI Automation, reminders missed while closed, and the tasks window.
/// </summary>
public sealed partial class IslandAppTests
{
    private const uint WmTestTask = 0x8000 + 56;

    [Fact]
    public void ADueReminderIsShownWithoutItsTextAndCanBeSnoozed()
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

        Assert.True(PostMessageW(island.Hwnd, WmTestTask, 1, 0));
        Assert.Equal("added", island.WaitFor("task")["change"]!.GetValue<string>());
        Assert.Equal("shown", island.WaitFor("reminder")["state"]!.GetValue<string>());
        Assert.True(WaitUntil(() => notes.Contains("Reminder"), 5000), string.Join(" | ", notes));
        Assert.DoesNotContain(notes, n => n.Contains("Test reminder", StringComparison.Ordinal)); // previews are off by default
        Assert.DoesNotContain("Test reminder", element.Name, StringComparison.Ordinal);

        island.WaitFor("alert", skip: 1); // the arrival pill ends; the reminder stays in the island
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        Assert.Equal("Reminder", WaitForChild(element, "Halo.Activity").Name);
        Assert.Equal("Mark done", WaitForChild(element, "Halo.Activity.Action.done").Name);
        Assert.Equal("Dismiss", WaitForChild(element, "Halo.Activity.Action.dismiss").Name);
        WaitForChild(element, "Halo.Activity.Action.snooze").Patterns.Invoke.Pattern.Invoke();
        var action = island.WaitFor("activity.action");
        Assert.Equal(("notification", "snooze", "done"), (action["activity"]!.GetValue<string>(), action["action"]!.GetValue<string>(), action["result"]!.GetValue<string>()));
        Assert.True(WaitUntil(() => element.FindAllChildren().All(c => c.AutomationId != "Halo.Activity")), "the snoozed reminder stayed");

        island.Exit();
        var tasks = File.ReadAllText(Path.Combine(island.DataDir, "tasks.json"));
        Assert.Contains("snoozedUntilUtc", tasks, StringComparison.Ordinal);
        Assert.Contains("Test reminder", tasks, StringComparison.Ordinal); // the user's own text stays on this machine only
        Assert.DoesNotContain("Test reminder", File.ReadAllText(Path.Combine(island.DataDir, "logs", "halo.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void WithPreviewsAllowedTheReminderTextIsShownAndDismissStopsIt()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showPreviews\": true }");
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        Assert.True(PostMessageW(island.Hwnd, WmTestTask, 0, 0));
        island.WaitFor("reminder");
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        Assert.Contains("Test reminder", WaitForChild(element, "Halo.Activity").Name, StringComparison.Ordinal);

        WaitForChild(element, "Halo.Activity.Action.dismiss").Patterns.Invoke.Pattern.Invoke();
        Assert.Equal("done", island.WaitFor("activity.action")["result"]!.GetValue<string>());
        Assert.True(WaitUntil(() => element.FindAllChildren().All(c => c.AutomationId != "Halo.Activity")));
        Thread.Sleep(500);
        Assert.Single(island.Events("reminder")); // dismissed: it does not come back
    }

    [Fact]
    public void RemindersMissedWhileHaloWasClosedAreShownAtStart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "halo-desktop-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var due = DateTimeOffset.UtcNow.AddHours(-2).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var created = DateTimeOffset.UtcNow.AddDays(-1).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(Path.Combine(dir, "tasks.json"), $"{{ \"schemaVersion\": 1, \"items\": [ {{ \"id\": \"a1\", \"text\": \"Missed one\", \"createdUtc\": \"{created}\", \"dueUtc\": \"{due}\" }} ] }}");

        using var island = IslandProcess.Start(dataDirectory: dir);
        Assert.Equal(1, island.WaitFor("tasks")["pending"]!.GetValue<int>());
        var shown = island.WaitFor("reminder");
        Assert.Equal("startup", shown["reason"]!.GetValue<string>());
        Assert.True(shown["overdue_s"]!.GetValue<int>() >= 7000);
        Assert.True(WaitUntil(() => island.Events("alert").Any(a => a["kind"]!.GetValue<string>() == "notification")));
    }

    [Fact]
    public void TheTasksWindowAddsAndCompletesTasks()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        using var automation = new UIA3Automation();
        try
        {
            // Open, Collapse, Settings…, Tasks and reminders… → Enter.
            OpenTrayMenuDirect(island);
            PressKeys(0x28, 0x28, 0x28, 0x28, 0x0D);
            island.WaitFor("tasks.window");
            AutomationElement? window = null;
            Assert.True(WaitUntil(() => (window = automation.GetDesktop().FindFirstChild(cf => cf.ByName("Halo tasks and reminders"))) is not null, 5000), "no tasks window");

            var text = window!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByAutomationId("302")))!;
            text.Patterns.Value.Pattern.SetValue("Write the report");
            window.FindFirstDescendant(cf => cf.ByAutomationId("307"))!.Patterns.Invoke.Pattern.Invoke(); // Add
            Assert.Equal("added", island.WaitFor("task")["change"]!.GetValue<string>());
            var list = window.FindFirstDescendant(cf => cf.ByAutomationId("301"))!;
            Assert.True(WaitUntil(() => list.FindAllChildren().Any(i => i.Name.Contains("Write the report", StringComparison.Ordinal))), "the task is not listed");

            window.FindFirstDescendant(cf => cf.ByAutomationId("309"))!.Patterns.Invoke.Pattern.Invoke(); // Complete
            Assert.Equal("completed", island.WaitFor("task", skip: 1)["change"]!.GetValue<string>());
            Assert.True(WaitUntil(() => list.FindAllChildren().Any(i => i.Name.StartsWith('\u2611'))), "not marked done");
            window.FindFirstDescendant(cf => cf.ByAutomationId("2"))!.Patterns.Invoke.Pattern.Invoke(); // Close
        }
        finally
        {
            PressKeys(0x1B);
        }
    }
}
