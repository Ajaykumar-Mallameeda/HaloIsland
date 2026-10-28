using System.Runtime.InteropServices;

namespace Halo.App.Interop;

// UI Automation provider interfaces (UIAutomationCore.h). Built-in COM interop creates the COM wrappers.
#pragma warning disable SA1600, CA1711

[Flags]
internal enum ProviderOptions
{
    ServerSideProvider = 0x1,
    UseComThreading = 0x20,
}

internal enum ExpandCollapseState
{
    Collapsed = 0,
    Expanded = 1,
}

[ComImport]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderSimple
{
    ProviderOptions ProviderOptions { get; }

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    object? GetPropertyValue(int propertyId);

    IRawElementProviderSimple? HostRawElementProvider { get; }
}

[ComImport]
[Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExpandCollapseProvider
{
    void Expand();

    void Collapse();

    ExpandCollapseState ExpandCollapseState { get; }
}

[ComImport]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInvokeProvider
{
    void Invoke();
}

#pragma warning restore SA1600, CA1711

/// <summary>What the island exposes to UI Automation; implemented by the island window.</summary>
internal interface IIslandAutomationSource
{
    bool IsExpanded { get; }

    /// <summary>Live summary of the compact island (FR-A11Y-01).</summary>
    string AccessibleName { get; }

    /// <summary>Extra detail shown when expanded, or null.</summary>
    string? AccessibleDescription { get; }

    void RequestExpanded(bool expanded);

    void RequestToggle();
}

/// <summary>
/// UI Automation provider for the island window (FR-A11Y-01): a button named with the live clock summary that
/// supports Invoke (toggle) and ExpandCollapse. Uses COM threading, so UIA calls arrive on the window thread.
/// </summary>
internal sealed partial class IslandAutomationProvider(nint hwnd, IIslandAutomationSource source) : IRawElementProviderSimple, IExpandCollapseProvider, IInvokeProvider
{
    public const string AutomationId = "HaloIsland";
    private const int InvokePatternId = 10000;
    private const int ExpandCollapsePatternId = 10005;
    private const int ControlTypePropertyId = 30003;
    private const int LocalizedControlTypePropertyId = 30004;
    private const int NamePropertyId = 30005;
    private const int IsKeyboardFocusablePropertyId = 30009;
    private const int AutomationIdPropertyId = 30011;
    private const int FullDescriptionPropertyId = 30159;
    private const int ExpandCollapseStatePropertyId = 30070;
    private const int ButtonControlTypeId = 50000;
    private const int UiaRootObjectId = -25;

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple? HostRawElementProvider =>
        UiaHostProviderFromHwnd(hwnd, out var host) >= 0 ? host : null;

    public ExpandCollapseState ExpandCollapseState => source.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <summary>Answers WM_GETOBJECT for the UIA root; returns null when the message is for another client.</summary>
    public nint? HandleGetObject(nint wParam, nint lParam) =>
        (int)lParam == UiaRootObjectId ? UiaReturnRawElementProvider(hwnd, wParam, lParam, this) : null;

    public object? GetPatternProvider(int patternId) => patternId is InvokePatternId or ExpandCollapsePatternId ? this : null;

    public object? GetPropertyValue(int propertyId) => propertyId switch
    {
        ControlTypePropertyId => ButtonControlTypeId,
        LocalizedControlTypePropertyId => "island",
        NamePropertyId => source.AccessibleName,
        AutomationIdPropertyId => AutomationId,
        FullDescriptionPropertyId => source.AccessibleDescription,
        IsKeyboardFocusablePropertyId => false,
        _ => null,
    };

    public void Expand() => source.RequestExpanded(true);

    public void Collapse() => source.RequestExpanded(false);

    public void Invoke() => source.RequestToggle();

    /// <summary>Tells listening clients that the state or the name changed (no-op when nobody listens).</summary>
    public void RaiseChanged(bool expandedBefore, bool expandedAfter, string nameBefore, string nameAfter)
    {
        if (!UiaClientsAreListening())
        {
            return;
        }

        if (expandedBefore != expandedAfter)
        {
            _ = UiaRaiseAutomationPropertyChangedEvent(this, ExpandCollapseStatePropertyId, (int)(expandedBefore ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed), (int)(expandedAfter ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed));
        }

        if (!string.Equals(nameBefore, nameAfter, StringComparison.Ordinal))
        {
            _ = UiaRaiseAutomationPropertyChangedEvent(this, NamePropertyId, nameBefore, nameAfter);
        }
    }

    /// <summary>Releases UIA's references before the window is destroyed.</summary>
    public void Disconnect()
    {
        _ = UiaReturnRawElementProvider(hwnd, 0, 0, null);
        _ = UiaDisconnectProvider(this);
    }

    // Built-in COM marshalling (interfaces and VARIANTs) is not available to LibraryImport.
#pragma warning disable SYSLIB1054
    [DllImport("UIAutomationCore.dll")]
    private static extern nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, IRawElementProviderSimple? element);

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaHostProviderFromHwnd(nint hwnd, out IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UiaClientsAreListening();

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaRaiseAutomationPropertyChangedEvent(IRawElementProviderSimple provider, int propertyId, [MarshalAs(UnmanagedType.Struct)] object oldValue, [MarshalAs(UnmanagedType.Struct)] object newValue);

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaDisconnectProvider(IRawElementProviderSimple provider);
#pragma warning restore SYSLIB1054
}
