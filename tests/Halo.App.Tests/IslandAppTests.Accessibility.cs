using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Accessibility foundation (M2-F05) checked with a real UI Automation client attached. Not a Narrator review.
/// <para>
/// Known defect (PROJECT_STATUS, M2-F05): UIA events raised by the island's provider (property changes and
/// notifications) return S_OK but are not received by UIA clients, so these tests verify what the island does (it
/// raises the announcement only when allowed, and describes the alert), not that a screen reader hears it.
/// </para>
/// </summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void AlertsAreRaisedForScreenReadersAndDescribedByTheIsland()
    {
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd); // a UIA client is now listening

        RaiseTestAlert(island, priority: 2, durationMs: 2000);
        var announce = island.WaitFor("announce");
        Assert.Equal(1, announce["listening"]!.GetValue<int>());
        Assert.Equal(0, announce["hresult"]!.GetValue<int>()); // UiaRaiseNotificationEvent succeeded
        Assert.True(WaitUntil(() => element.Properties.FullDescription.ValueOrDefault == "Test alert 2"), "the alert is not in the island's description");

        island.WaitFor("alert", skip: 1); // ended
        Assert.True(WaitUntil(() => string.IsNullOrEmpty(element.Properties.FullDescription.ValueOrDefault)), "the description kept the ended alert");
    }

    [Fact]
    public void AnnouncementsCanBeTurnedOff()
    {
        using var island = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"announceAlerts\": false }");
        using var automation = new UIA3Automation();
        _ = automation.FromHandle(island.Hwnd);

        RaiseTestAlert(island, priority: 2, durationMs: 1000);
        island.WaitFor("alert");
        Thread.Sleep(500);

        Assert.Empty(island.Events("announce")); // nothing raised at all
    }

    [Fact]
    public void ReduceMotionMakesTransitionsInstant()
    {
        double Duration(IslandProcess island)
        {
            using var automation = new UIA3Automation();
            automation.FromHandle(island.Hwnd).Patterns.ExpandCollapse.Pattern.Expand();
            return Ms(island.WaitFor("transition.end"), island.WaitFor("transition.start"));
        }

        using (var normal = IslandProcess.Start())
        {
            var animated = Duration(normal);
            Assert.True(animated > 250, $"the normal transition took only {animated} ms (control case)");
        }

        using var reduced = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"reduceMotion\": true }");
        var instant = Duration(reduced);
        Assert.True(instant < 200, $"with reduced motion the transition took {instant} ms");
    }
}
