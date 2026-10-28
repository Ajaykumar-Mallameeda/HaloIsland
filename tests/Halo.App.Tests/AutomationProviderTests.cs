using Halo.App.Interop;
using Halo.Core.Accessibility;
using Halo.Core.Island;
using Xunit;

namespace Halo.App.Tests;

/// <summary>The island's UIA provider without a window (A-F01): snapshot model, navigation, patterns, stale elements.</summary>
public class AutomationProviderTests
{
    private static readonly AccessibleNode Mute = new(IslandAccessibility.MuteId, AccessibleRole.ToggleButton, "Mute", new RoundedBox(10, 20, 30, 30, 0), AccessibleCommand.ToggleMute) { Toggled = false };
    private static readonly AccessibleNode Volume = new(IslandAccessibility.VolumeId, AccessibleRole.Slider, "Volume", new RoundedBox(50, 20, 200, 30, 0), AccessibleCommand.SetVolume) { Value = 40 };
    private static readonly AccessibleNode Time = new(IslandAccessibility.TimeId, AccessibleRole.Text, "4:05 PM", new RoundedBox(10, 0, 100, 20, 0));

    [Fact]
    public void ExposesAButtonNamedWithTheLiveSummary()
    {
        var provider = Provider(out _);
        provider.Publish(Snapshot(name: "Halo, 4:05 PM, Wednesday, October 28"));

        Assert.Equal(50000, provider.GetPropertyValue(30003)); // ControlType = Button
        Assert.Equal("Halo, 4:05 PM, Wednesday, October 28", provider.GetPropertyValue(30005));
        Assert.Equal("HaloIsland", provider.GetPropertyValue(30011));
        Assert.Equal(false, provider.GetPropertyValue(30009)); // not keyboard focusable (no focus stealing)
        Assert.Null(provider.GetPropertyValue(30001)); // anything else falls back to the host provider
        Assert.Equal(ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading, provider.ProviderOptions);
    }

    [Fact]
    public void ProviderOptionsUseTheDocumentedValues()
    {
        // A-F01 root cause: ServerSideProvider was declared as 0x1, which is ClientSideProvider in UIAutomationCore.h,
        // and UIA does not deliver events raised by client-side providers. 0x22 = ServerSideProvider | UseComThreading.
        Assert.Equal(0x1, (int)ProviderOptions.ClientSideProvider);
        Assert.Equal(0x2, (int)ProviderOptions.ServerSideProvider);
        Assert.Equal(0x22, (int)Provider(out _).ProviderOptions);
    }

    [Fact]
    public void SupportsInvokeExpandCollapseAndWindowOnly()
    {
        var provider = Provider(out _);

        Assert.Same(provider, provider.GetPatternProvider(10000));
        Assert.Same(provider, provider.GetPatternProvider(10005));
        Assert.Same(provider, provider.GetPatternProvider(10009)); // Window pattern (WS_POPUP requirement)
        Assert.Null(provider.GetPatternProvider(10002));
        Assert.False(provider.CanMaximize);
        Assert.False(provider.CanMinimize);
        Assert.True(provider.IsTopmost);
    }

    [Fact]
    public void PatternsArePostedToTheUiThreadNotRunOnTheCallersThread()
    {
        var provider = Provider(out var requests);

        provider.Expand();
        provider.Collapse();
        provider.Invoke();

        Assert.Equal([AutomationRequest.Expand, AutomationRequest.Collapse, AutomationRequest.Toggle], requests.Select(r => r.Request));
        Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState); // only a published snapshot changes it
        provider.Publish(Snapshot(expanded: true));
        Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
    }

    [Fact]
    public void AnswersOnlyTheUiaRootObjectRequest()
    {
        var provider = Provider(out _);

        Assert.Null(provider.HandleGetObject(0, -4)); // OBJID_CLIENT (MSAA) is left to the default handler
    }

    [Fact]
    public void ChildrenAreNavigableWithStableIdentityAndRuntimeIds()
    {
        var provider = Provider(out _);
        provider.Publish(Snapshot(children: [Time, Mute, Volume]));

        var first = (IslandAutomationElement)provider.Navigate(NavigateDirection.FirstChild)!;
        var last = (IslandAutomationElement)provider.Navigate(NavigateDirection.LastChild)!;
        Assert.Equal(IslandAccessibility.TimeId, first.Id);
        Assert.Equal(IslandAccessibility.VolumeId, last.Id);
        Assert.Same(provider, first.Navigate(NavigateDirection.Parent));
        Assert.Same(provider, first.FragmentRoot);
        var second = (IslandAutomationElement)first.Navigate(NavigateDirection.NextSibling)!;
        Assert.Equal(IslandAccessibility.MuteId, second.Id);
        Assert.Same(first, second.Navigate(NavigateDirection.PreviousSibling));
        Assert.Null(last.Navigate(NavigateDirection.NextSibling));
        Assert.Null(provider.Navigate(NavigateDirection.Parent)); // hosted by its HWND
        Assert.Null(provider.GetRuntimeId());

        // The same ID gives the same provider object after a republish; runtime IDs are appended and distinct.
        provider.Publish(Snapshot(children: [Time, Mute with { Toggled = true }, Volume]));
        Assert.Same(second, provider.Navigate(NavigateDirection.FirstChild)!.Navigate(NavigateDirection.NextSibling));
        Assert.Equal(3, first.GetRuntimeId()![0]);
        Assert.NotEqual(first.GetRuntimeId()![1], second.GetRuntimeId()![1]);
    }

    [Fact]
    public void ChildrenExposeRolesPatternsAndScreenBounds()
    {
        var provider = Provider(out var requests);
        provider.Publish(Snapshot(children: [Time, Mute, Volume]) with { OriginX = 1000, OriginY = 5 });
        var time = provider.ElementFor(IslandAccessibility.TimeId);
        var mute = provider.ElementFor(IslandAccessibility.MuteId);
        var volume = provider.ElementFor(IslandAccessibility.VolumeId);

        Assert.Equal(50020, time.GetPropertyValue(30003)); // Text
        Assert.Equal("4:05 PM", time.GetPropertyValue(30005));
        Assert.Null(time.GetPatternProvider(10000));
        Assert.Equal(50000, mute.GetPropertyValue(30003)); // Button with Toggle
        Assert.Same(mute, mute.GetPatternProvider(10015));
        Assert.Null(mute.GetPatternProvider(10000));
        Assert.Equal(ToggleState.Off, mute.ToggleState);
        Assert.Equal(50015, volume.GetPropertyValue(30003)); // Slider with RangeValue
        Assert.Same(volume, volume.GetPatternProvider(10003));
        Assert.Equal(40, volume.Value);
        Assert.False(volume.IsReadOnly);
        Assert.Equal((0d, 100d), (volume.Minimum, volume.Maximum));
        var r = mute.BoundingRectangle;
        Assert.Equal((1010d, 25d, 30d, 30d), (r.Left, r.Top, r.Width, r.Height));
        Assert.Same(volume, provider.ElementProviderFromPoint(1100, 30));
        Assert.Same(provider, provider.ElementProviderFromPoint(5, 5));

        mute.Toggle();
        volume.SetValue(55.4);
        Assert.Equal([(AutomationRequest.ToggleMute, 0), (AutomationRequest.SetVolume, 55)], requests);
        Assert.Throws<ArgumentException>(() => volume.SetValue(101));
        Assert.Throws<InvalidOperationException>(() => mute.SetValue(5));
    }

    [Fact]
    public void AnElementThatLeftTheScreenReportsElementNotAvailable()
    {
        var provider = Provider(out var requests);
        provider.Publish(Snapshot(children: [Mute]));
        var mute = provider.ElementFor(IslandAccessibility.MuteId);

        provider.Publish(Snapshot()); // collapsed: the control is gone
        Assert.Null(mute.GetPropertyValue(30005));
        Assert.Null(mute.Navigate(NavigateDirection.Parent));
        var ex = Assert.Throws<ElementNotAvailableException>(mute.Toggle);
        Assert.Equal(unchecked((int)0x80040201), ex.HResult); // UIA_E_ELEMENTNOTAVAILABLE
        Assert.Empty(requests);
    }

    [Fact]
    public void AfterDisconnectNothingIsPostedOrExposed()
    {
        var provider = Provider(out var requests);
        provider.Publish(Snapshot(children: [Mute]));
        var mute = provider.ElementFor(IslandAccessibility.MuteId);

        provider.Disconnect();
        Assert.Null(provider.HandleGetObject(0, -25));
        Assert.Throws<ElementNotAvailableException>(provider.Expand);
        Assert.Null(mute.GetPropertyValue(30005));
        Assert.Empty(requests);
        Assert.Null(provider.Announce("x", "Halo.Test", false));
    }

    [Fact]
    public void AStatusElementIsALiveRegion()
    {
        var provider = Provider(out _);
        var alert = new AccessibleNode(IslandAccessibility.AlertId, AccessibleRole.Status, "Battery low", default) { Assertive = true };
        provider.Publish(Snapshot(children: [alert]));

        var element = provider.ElementFor(IslandAccessibility.AlertId);
        Assert.Equal(2, element.GetPropertyValue(30135)); // LiveSetting = Assertive
        Assert.Equal(50020, element.GetPropertyValue(30003));
        provider.Publish(Snapshot(children: [alert with { Assertive = false }]));
        Assert.Equal(1, element.GetPropertyValue(30135)); // Polite
    }

    private static IslandAutomationProvider Provider(out List<(AutomationRequest Request, int Value)> requests)
    {
        var list = new List<(AutomationRequest Request, int Value)>();
        requests = list;
        return new IslandAutomationProvider(0, 0, (r, v) =>
        {
            list.Add((r, v));
            return true;
        });
    }

    private static AutomationSnapshot Snapshot(bool expanded = false, string name = "Halo", IReadOnlyList<AccessibleNode>? children = null) =>
        new(expanded, name, null, new RoundedBox(0, 0, 300, 40, 20), 0, 0, true, children ?? []);
}
