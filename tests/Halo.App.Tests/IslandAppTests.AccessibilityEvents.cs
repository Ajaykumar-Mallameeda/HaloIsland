using System.Collections.Concurrent;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// A-F01: UI Automation events from the island reach a real, out-of-process UIA client (the same path Narrator uses).
/// These tests receive the events; they do not only check that the island raised them.
/// </summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void AlertNotificationIsReceivedByAUiaClientListeningOnTheDesktop()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var received = new ConcurrentQueue<(string Text, string Activity, NotificationProcessing Processing)>();
        using var handler = automation.GetDesktop().RegisterNotificationEvent(
            TreeScope.Subtree,
            (_, _, processing, text, activity) => received.Enqueue((text, activity, processing)));

        RaiseTestAlert(island, priority: 2, durationMs: 2000);
        Assert.True(
            WaitUntil(() => received.Any(r => r.Activity.StartsWith("Halo.", StringComparison.Ordinal)), 5000),
            $"no notification from Halo arrived; announce={string.Join(",", island.Events("announce").Select(e => e.ToJsonString()))}");
        var note = received.First(r => r.Activity.StartsWith("Halo.", StringComparison.Ordinal));
        Assert.Equal("Test alert 2", note.Text);
        Assert.Equal(NotificationProcessing.MostRecent, note.Processing);
    }

    [Fact]
    public void CriticalAlertIsAnnouncedAsImportant()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var received = new ConcurrentQueue<(string Text, NotificationProcessing Processing)>();
        using var handler = automation.GetDesktop().RegisterNotificationEvent(
            TreeScope.Subtree,
            (_, _, processing, text, activity) =>
            {
                if (activity.StartsWith("Halo.", StringComparison.Ordinal))
                {
                    received.Enqueue((text, processing));
                }
            });

        RaiseTestAlert(island, priority: 3, durationMs: 1500);
        Assert.True(WaitUntil(() => !received.IsEmpty, 5000), "no notification arrived");
        Assert.Equal(NotificationProcessing.ImportantMostRecent, received.First().Processing);
    }

    [Fact]
    public void ExpandCollapseStateChangeIsReceivedByAUiaClient()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        var states = new ConcurrentQueue<object>();
        using var handler = element.RegisterPropertyChangedEvent(
            TreeScope.Element,
            (_, _, value) => states.Enqueue(value),
            automation.PropertyLibrary.ExpandCollapse.ExpandCollapseState);

        element.Patterns.ExpandCollapse.Pattern.Expand();
        Assert.True(WaitUntil(() => states.Any(v => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) == (int)ExpandCollapseState.Expanded), 5000), "no ExpandCollapseState change arrived");
    }

    [Fact]
    public void ExpandedIslandExposesItsContentAndControlsAndCollapsingRemovesThem()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        var structure = new ConcurrentQueue<StructureChangeType>();
        using var handler = element.RegisterStructureChangedEvent(TreeScope.Subtree, (_, type, _) => structure.Enqueue(type));

        Assert.Empty(element.FindAllChildren());
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        Assert.True(WaitUntil(() => element.FindAllChildren().Length >= 3, 5000), "no children while expanded");
        Assert.True(WaitUntil(() => !structure.IsEmpty, 5000), "no structure change arrived");

        var children = element.FindAllChildren();
        var ids = children.Select(c => c.AutomationId).ToList();
        Assert.Contains("Halo.DayProgress", ids);
        var progress = children.Single(c => c.AutomationId == "Halo.DayProgress");
        Assert.Equal(ControlType.ProgressBar, progress.ControlType);
        Assert.Contains("% of today", progress.Name, StringComparison.Ordinal);
        Assert.True(progress.Patterns.RangeValue.Pattern.IsReadOnly.Value);

        // Bounds are on screen, inside the island window.
        Assert.True(GetWindowRect(island.Hwnd, out var window));
        foreach (var child in children)
        {
            var r = child.BoundingRectangle;
            Assert.True(r.Left >= window.Left && r.Right <= window.Right && r.Top >= window.Top && r.Bottom <= window.Bottom, $"{child.AutomationId} {r}");
        }

        // Volume row: the slider reports the real endpoint level the island shows.
        if (ids.Contains("Halo.Volume") && children.Single(c => c.AutomationId == "Halo.Volume").ControlType == ControlType.Slider)
        {
            var slider = children.Single(c => c.AutomationId == "Halo.Volume");
            var shown = island.Events("volume").Last()["percent"]!.GetValue<int>();
            Assert.Equal(shown, slider.Patterns.RangeValue.Pattern.Value.Value);
            Assert.False(slider.Patterns.RangeValue.Pattern.IsReadOnly.Value);
            Assert.True(children.Single(c => c.AutomationId == "Halo.Volume.Mute").Patterns.Toggle.IsSupported);
        }

        element.Patterns.ExpandCollapse.Pattern.Collapse();
        island.WaitFor("transition.end", skip: 1);
        Assert.True(WaitUntil(() => element.FindAllChildren().Length == 0, 5000), "children stayed after collapsing");
        Assert.True(island.Count("uia") >= 2, island.Trace());
    }

    [Fact]
    public void AnAlertIsAChildWhileShownAndGoneWhenItEnds()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);

        RaiseTestAlert(island, priority: 2, durationMs: 1500);
        Assert.True(WaitUntil(() => element.FindAllChildren().Any(c => c.AutomationId == "Halo.Alert"), 5000), "the alert is not a child");
        var alert = element.FindAllChildren().Single(c => c.AutomationId == "Halo.Alert");
        Assert.Equal("Test alert 2", alert.Name);
        Assert.Equal(1, Convert.ToInt32(((FlaUI.UIA3.UIA3FrameworkAutomationElement)alert.FrameworkAutomationElement).NativeElement.GetCurrentPropertyValue(30135), System.Globalization.CultureInfo.InvariantCulture)); // LiveSetting Polite

        island.WaitFor("alert", skip: 1); // ended
        Assert.True(WaitUntil(() => element.FindAllChildren().Length == 0, 5000), "the ended alert is still exposed");
    }

    [Fact]
    public void UiaCommandsDoNotTakeTheForeground()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        var foreground = SettledForeground();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);

        element.Patterns.Invoke.Pattern.Invoke();
        island.WaitFor("transition.end");
        element.Patterns.Invoke.Pattern.Invoke();
        island.WaitFor("transition.end", skip: 1);
        AssertForeground(foreground);
        Assert.False(element.Properties.IsKeyboardFocusable.Value);
    }

    [Fact]
    public void ClockTicksDoNotRaiseNotifications()
    {
        using var island = IslandProcess.Start();
        island.WaitFor("ready");
        using var automation = new UIA3Automation();
        var received = new ConcurrentQueue<string>();
        using var handler = automation.GetDesktop().RegisterNotificationEvent(
            TreeScope.Subtree,
            (_, _, _, text, activity) =>
            {
                if (activity.StartsWith("Halo.", StringComparison.Ordinal))
                {
                    received.Enqueue(activity);
                }
            });

        // Expanding and collapsing change content (day progress, media) but are not activities: nothing is announced.
        var element = automation.FromHandle(island.Hwnd);
        element.Patterns.ExpandCollapse.Pattern.Expand();
        island.WaitFor("transition.end");
        element.Patterns.ExpandCollapse.Pattern.Collapse();
        island.WaitFor("transition.end", skip: 1);
        Thread.Sleep(500);
        Assert.Empty(received);
    }
}
