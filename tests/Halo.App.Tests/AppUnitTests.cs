using System.IO;
using Halo.App.Interop;
using Halo.App.Island;
using Xunit;

namespace Halo.App.Tests;

public class AutomationProviderTests
{
    [Fact]
    public void ExposesAButtonNamedWithTheLiveSummary()
    {
        var source = new FakeSource { AccessibleName = "Halo, 4:05 PM, Wednesday, October 28" };
        var provider = new IslandAutomationProvider(0, source);

        Assert.Equal(50000, provider.GetPropertyValue(30003)); // ControlType = Button
        Assert.Equal(source.AccessibleName, provider.GetPropertyValue(30005));
        Assert.Equal("HaloIsland", provider.GetPropertyValue(30011));
        Assert.Equal(false, provider.GetPropertyValue(30009)); // not keyboard focusable (no focus stealing)
        Assert.Null(provider.GetPropertyValue(30001)); // anything else falls back to the host provider
        Assert.Equal(ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading, provider.ProviderOptions);
    }

    [Fact]
    public void SupportsInvokeAndExpandCollapseOnly()
    {
        var provider = new IslandAutomationProvider(0, new FakeSource());

        Assert.Same(provider, provider.GetPatternProvider(10000));
        Assert.Same(provider, provider.GetPatternProvider(10005));
        Assert.Null(provider.GetPatternProvider(10002));
    }

    [Fact]
    public void PatternsDriveTheIsland()
    {
        var source = new FakeSource();
        var provider = new IslandAutomationProvider(0, source);

        provider.Expand();
        Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
        provider.Collapse();
        Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);
        provider.Invoke();
        Assert.True(source.IsExpanded);
    }

    [Fact]
    public void AnswersOnlyTheUiaRootObjectRequest()
    {
        var provider = new IslandAutomationProvider(0, new FakeSource());

        Assert.Null(provider.HandleGetObject(0, -4)); // OBJID_CLIENT (MSAA) is left to the default handler
    }

    private sealed class FakeSource : IIslandAutomationSource
    {
        public bool IsExpanded { get; private set; }

        public string AccessibleName { get; init; } = "Halo";

        public string? AccessibleDescription => null;

        public void RequestExpanded(bool expanded) => IsExpanded = expanded;

        public void RequestToggle() => IsExpanded = !IsExpanded;
    }
}

public class TrayInteropTests
{
    [Fact]
    public unsafe void NotifyIconDataMatchesTheWindowsLayout()
    {
        // sizeof(NOTIFYICONDATAW) on x64; a wrong size makes Shell_NotifyIconW fail.
        Assert.Equal(976, sizeof(Native.NotifyIconData));
        Assert.Equal(32, sizeof(Native.IconInfo));
        Assert.Equal(40, sizeof(Native.BitmapInfoHeader));
    }

    [Fact]
    public void CallbackMessageDoesNotCollideWithOtherIslandMessages()
    {
        Assert.NotEqual(IslandWindow.WmCommandFromInstance, TrayIcon.CallbackMessage);
        Assert.InRange(TrayIcon.CallbackMessage, Native.WmApp, 0xBFFFu);
    }
}

public class DiagnosticsLogTests
{
    [Fact]
    public void WritesOneJsonObjectPerEventWithNumbersAndTokens()
    {
        using var writer = new StringWriter();
        var log = new DiagnosticsLog(writer);

        log.Write("transition.start", ("dir", "expand"), ("left", 687));

        var line = writer.ToString().TrimEnd();
        var json = System.Text.Json.Nodes.JsonNode.Parse(line)!;
        Assert.Equal("transition.start", json["event"]!.GetValue<string>());
        Assert.Equal("expand", json["dir"]!.GetValue<string>());
        Assert.Equal(687, json["left"]!.GetValue<int>());
        Assert.True(json["qpc"]!.GetValue<long>() > 0);
    }
}
