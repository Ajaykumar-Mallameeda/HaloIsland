using System.Runtime.InteropServices;
using Halo.Core.Accessibility;
using Halo.Core.Island;

namespace Halo.App.Interop;

// UI Automation provider interfaces (UIAutomationCore.h). Built-in COM interop creates the COM wrappers. Member order
// is the native vtable order.
#pragma warning disable SA1600, CA1711

/// <summary>ProviderOptions (UIAutomationCore.h), with the documented values (see A-F01 in PROJECT_STATUS).</summary>
[Flags]
internal enum ProviderOptions
{
    ClientSideProvider = 0x1,
    ServerSideProvider = 0x2,
    UseComThreading = 0x20,
}

internal enum ExpandCollapseState
{
    Collapsed = 0,
    Expanded = 1,
}

internal enum ToggleState
{
    Off = 0,
    On = 1,
}

internal enum NavigateDirection
{
    Parent = 0,
    NextSibling = 1,
    PreviousSibling = 2,
    FirstChild = 3,
    LastChild = 4,
}

internal enum WindowVisualState
{
    Normal = 0,
    Maximized = 1,
    Minimized = 2,
}

internal enum WindowInteractionState
{
    Running = 0,
    Closing = 1,
    ReadyForUserInteraction = 2,
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
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
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragment
{
    IRawElementProviderFragment? Navigate(NavigateDirection direction);

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[]? GetRuntimeId();

    UiaRect BoundingRectangle { get; }

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)]
    object[]? GetEmbeddedFragmentRoots();

    void SetFocus();

    IRawElementProviderFragmentRoot FragmentRoot { get; }
}

[ComImport]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragmentRoot
{
    IRawElementProviderFragment? ElementProviderFromPoint(double x, double y);

    IRawElementProviderFragment? GetFocus();
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

[ComImport]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToggleProvider
{
    void Toggle();

    ToggleState ToggleState { get; }
}

[ComImport]
[Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRangeValueProvider
{
    void SetValue(double value);

    double Value { get; }

    bool IsReadOnly { [return: MarshalAs(UnmanagedType.Bool)] get; }

    double Maximum { get; }

    double Minimum { get; }

    double LargeChange { get; }

    double SmallChange { get; }
}

[ComImport]
[Guid("987df77b-db06-4d77-8f8a-86a9c3bb90b9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowProvider
{
    void SetVisualState(WindowVisualState state);

    void Close();

    [return: MarshalAs(UnmanagedType.Bool)]
    bool WaitForInputIdle(int milliseconds);

    bool CanMaximize { [return: MarshalAs(UnmanagedType.Bool)] get; }

    bool CanMinimize { [return: MarshalAs(UnmanagedType.Bool)] get; }

    bool IsModal { [return: MarshalAs(UnmanagedType.Bool)] get; }

    WindowVisualState WindowVisualState { get; }

    WindowInteractionState WindowInteractionState { get; }

    bool IsTopmost { [return: MarshalAs(UnmanagedType.Bool)] get; }
}

#pragma warning restore SA1600, CA1711

/// <summary>A command requested through UI Automation, carried to the UI thread in a posted message.</summary>
internal enum AutomationRequest
{
    Expand = 1,
    Collapse = 2,
    Toggle = 3,
    MediaPrevious = 10,
    MediaPlayPause = 11,
    MediaNext = 12,
    ToggleMute = 13,
    SetVolume = 14,
}

/// <summary>
/// What the island exposes to UI Automation at one instant. Built on the UI thread and published as a whole, so UIA
/// threads only ever read a consistent, immutable snapshot.
/// </summary>
/// <param name="Expanded">ExpandCollapse state.</param>
/// <param name="Name">Live summary of the island (FR-A11Y-01).</param>
/// <param name="Description">Extra detail (alert text or day progress), or null.</param>
/// <param name="Shape">The visible shape in canvas pixels.</param>
/// <param name="OriginX">Canvas origin on screen.</param>
/// <param name="OriginY">Canvas origin on screen.</param>
/// <param name="Visible">False while full screen hides the island.</param>
/// <param name="Children">Elements under the island.</param>
internal sealed record AutomationSnapshot(bool Expanded, string Name, string? Description, RoundedBox Shape, int OriginX, int OriginY, bool Visible, IReadOnlyList<AccessibleNode> Children)
{
    public static AutomationSnapshot Empty { get; } = new(false, "Halo", null, default, 0, 0, true, []);

    public AccessibleNode? Find(string id)
    {
        foreach (var node in Children)
        {
            if (string.Equals(node.Id, id, StringComparison.Ordinal))
            {
                return node;
            }
        }

        return null;
    }

    public int IndexOf(string id)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            if (string.Equals(Children[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// UIA_E_ELEMENTNOTAVAILABLE (0x80040201): the element a client holds is no longer on screen. COM interop returns the
/// exception's HResult to the UIA client.
/// </summary>
internal sealed class ElementNotAvailableException : Exception
{
    public ElementNotAvailableException()
        : this("The element is not available.")
    {
    }

    public ElementNotAvailableException(string message)
        : base(message) => HResult = IslandAutomationProvider.UiaElementNotAvailable;

    public ElementNotAvailableException(string message, Exception innerException)
        : base(message, innerException) => HResult = IslandAutomationProvider.UiaElementNotAvailable;
}

/// <summary>Which UIA events one <see cref="IslandAutomationProvider.Publish"/> raised.</summary>
internal readonly record struct AutomationChanges(bool ExpandState, bool Name, bool Structure, bool LiveRegion);

/// <summary>
/// UI Automation provider for the island window (FR-A11Y-01/02), a fragment root:
/// <list type="bullet">
/// <item>the island itself is a button named with the live summary, with Invoke (toggle), ExpandCollapse and the Window
/// pattern (required for WS_POPUP windows that raise events, per the UiaRaiseNotificationEvent remarks);</item>
/// <item>its children (<see cref="IslandAutomationElement"/>) are the time, date, day progress, status, media and its
/// buttons, the volume slider and mute, and a live status element while an alert is shown.</item>
/// </list>
/// UIA calls providers on its own threads, so every read goes to the published <see cref="AutomationSnapshot"/> and every
/// command is posted to the window (the single interaction path) instead of running on the caller's thread. Events are
/// raised only from <see cref="Publish"/> and <see cref="Announce"/>, which the window calls on its UI thread.
/// </summary>
internal sealed partial class IslandAutomationProvider(nint hwnd, uint requestMessage, Func<AutomationRequest, int, bool>? post = null) :
    IRawElementProviderSimple, IRawElementProviderFragment, IRawElementProviderFragmentRoot, IExpandCollapseProvider, IInvokeProvider, IWindowProvider
{
    public const string AutomationId = "HaloIsland";
    internal const int UiaAppendRuntimeId = 3;
    internal const int InvokePatternId = 10000;
    internal const int RangeValuePatternId = 10003;
    internal const int ExpandCollapsePatternId = 10005;
    internal const int WindowPatternId = 10009;
    internal const int TogglePatternId = 10015;
    internal const int ControlTypePropertyId = 30003;
    internal const int LocalizedControlTypePropertyId = 30004;
    internal const int NamePropertyId = 30005;
    internal const int IsKeyboardFocusablePropertyId = 30009;
    internal const int IsEnabledPropertyId = 30010;
    internal const int AutomationIdPropertyId = 30011;
    internal const int IsOffscreenPropertyId = 30022;
    internal const int RangeValueValuePropertyId = 30047;
    internal const int ExpandCollapseStatePropertyId = 30070;
    internal const int ToggleStatePropertyId = 30086;
    internal const int LiveSettingPropertyId = 30135;
    internal const int FullDescriptionPropertyId = 30159;
    internal const int ButtonControlTypeId = 50000;
    internal const int ProgressBarControlTypeId = 50012;
    internal const int SliderControlTypeId = 50015;
    internal const int TextControlTypeId = 50020;
    internal const int LiveRegionChangedEventId = 20024;
    internal const int UiaElementNotAvailable = unchecked((int)0x80040201);
    private const int UiaRootObjectId = -25;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, IslandAutomationElement> _elements = new(StringComparer.Ordinal);
    private volatile AutomationSnapshot _snapshot = AutomationSnapshot.Empty;
    private int _nextRuntimeId;
    private volatile bool _disconnected;

    public AutomationSnapshot Snapshot => _snapshot;

    public bool Disconnected => _disconnected;

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple? HostRawElementProvider =>
        UiaHostProviderFromHwnd(hwnd, out var host) >= 0 ? host : null;

    public ExpandCollapseState ExpandCollapseState => _snapshot.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public UiaRect BoundingRectangle => ToScreen(_snapshot, _snapshot.Shape);

    public IRawElementProviderFragmentRoot FragmentRoot => this;

    // Window pattern: the island cannot be maximised, minimised or closed through UIA; it is a topmost, non-modal surface.
    public bool CanMaximize => false;

    public bool CanMinimize => false;

    public bool IsModal => false;

    public bool IsTopmost => true;

    public WindowVisualState WindowVisualState => WindowVisualState.Normal;

    public WindowInteractionState WindowInteractionState => WindowInteractionState.ReadyForUserInteraction;

    /// <summary>Answers WM_GETOBJECT for the UIA root; returns null when the message is for another client.</summary>
    public nint? HandleGetObject(nint wParam, nint lParam) =>
        (int)lParam == UiaRootObjectId && !_disconnected ? UiaReturnRawElementProvider(hwnd, wParam, lParam, this) : null;

    public object? GetPatternProvider(int patternId) => patternId is InvokePatternId or ExpandCollapsePatternId or WindowPatternId ? this : null;

    public object? GetPropertyValue(int propertyId)
    {
        var s = _snapshot;
        return propertyId switch
        {
            ControlTypePropertyId => ButtonControlTypeId,
            LocalizedControlTypePropertyId => "island",
            NamePropertyId => s.Name,
            AutomationIdPropertyId => AutomationId,
            FullDescriptionPropertyId => s.Description,
            IsKeyboardFocusablePropertyId => false, // FR-ISL-06: the island never takes focus from the pointer or UIA
            IsEnabledPropertyId => true,
            _ => null,
        };
    }

    public void Expand() => Post(AutomationRequest.Expand);

    public void Collapse() => Post(AutomationRequest.Collapse);

    public void Invoke() => Post(AutomationRequest.Toggle);

    public void SetVisualState(WindowVisualState state)
    {
    }

    public void Close()
    {
    }

    public bool WaitForInputIdle(int milliseconds) => true;

    // The root is hosted by its HWND: no parent or siblings of its own, and UIA builds its runtime ID from the HWND.
    public IRawElementProviderFragment? Navigate(NavigateDirection direction)
    {
        var s = _snapshot;
        return direction switch
        {
            NavigateDirection.FirstChild when s.Children.Count > 0 => ElementFor(s.Children[0].Id),
            NavigateDirection.LastChild when s.Children.Count > 0 => ElementFor(s.Children[^1].Id),
            _ => null,
        };
    }

    public int[]? GetRuntimeId() => null;

    public object[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
    }

    public IRawElementProviderFragment? ElementProviderFromPoint(double x, double y)
    {
        var s = _snapshot;
        foreach (var node in s.Children)
        {
            var r = ToScreen(s, node.Bounds);
            if (x >= r.Left && x < r.Left + r.Width && y >= r.Top && y < r.Top + r.Height)
            {
                return ElementFor(node.Id);
            }
        }

        return this;
    }

    public IRawElementProviderFragment? GetFocus() => null;

    /// <summary>
    /// Publishes a new snapshot (UI thread) and tells listening clients what changed: the expand state, the name, the set
    /// of children (structure), and values of children that stayed. Unchanged content raises nothing.
    /// </summary>
    /// <returns>Which events were raised, for diagnostics.</returns>
    public AutomationChanges Publish(AutomationSnapshot next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (_disconnected)
        {
            return default;
        }

        var previous = _snapshot;
        _snapshot = next;
        if (!UiaClientsAreListening())
        {
            return default;
        }

        var changes = default(AutomationChanges);
        if (previous.Expanded != next.Expanded)
        {
            _ = UiaRaiseAutomationPropertyChangedEvent(this, ExpandCollapseStatePropertyId, (int)(previous.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed), (int)(next.Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed));
            changes = changes with { ExpandState = true };
        }

        if (!string.Equals(previous.Name, next.Name, StringComparison.Ordinal))
        {
            _ = UiaRaiseAutomationPropertyChangedEvent(this, NamePropertyId, previous.Name, next.Name);
            changes = changes with { Name = true };
        }

        if (!SameIds(previous.Children, next.Children))
        {
            var runtimeId = new[] { 42, (int)hwnd };
            _ = UiaRaiseStructureChangedEvent(this, 2 /* ChildrenInvalidated */, runtimeId, runtimeId.Length);
            changes = changes with { Structure = true };
            if (next.Find(IslandAccessibility.AlertId) is not null && previous.Find(IslandAccessibility.AlertId) is null)
            {
                // A new live status element: screen readers that follow live regions read it (Narrator also gets the
                // notification event raised by Announce).
                _ = UiaRaiseAutomationEvent(ElementFor(IslandAccessibility.AlertId), LiveRegionChangedEventId);
                changes = changes with { LiveRegion = true };
            }

            return changes;
        }

        foreach (var node in next.Children)
        {
            var before = previous.Find(node.Id);
            if (before is null || before == node)
            {
                continue;
            }

            var element = ElementFor(node.Id);
            if (!string.Equals(before.Name, node.Name, StringComparison.Ordinal))
            {
                _ = UiaRaiseAutomationPropertyChangedEvent(element, NamePropertyId, before.Name, node.Name);
                if (node.Role == AccessibleRole.Status)
                {
                    _ = UiaRaiseAutomationEvent(element, LiveRegionChangedEventId);
                    changes = changes with { LiveRegion = true };
                }
            }

            if (before.Toggled != node.Toggled && node.Toggled is { } on)
            {
                _ = UiaRaiseAutomationPropertyChangedEvent(element, ToggleStatePropertyId, (int)(before.Toggled == true ? ToggleState.On : ToggleState.Off), (int)(on ? ToggleState.On : ToggleState.Off));
            }

            if (before.Value != node.Value && node.Value is { } v && node.Role == AccessibleRole.Slider)
            {
                _ = UiaRaiseAutomationPropertyChangedEvent(element, RangeValueValuePropertyId, before.Value ?? 0d, v);
            }
        }

        return changes;
    }

    /// <summary>
    /// FR-A11Y-02: asks screen readers to announce a transient activity. <paramref name="important"/> = interrupt
    /// (critical); otherwise the most recent replaces any queued one (repeated volume changes).
    /// </summary>
    /// <returns>The HRESULT of the raise, or null when no UIA client is listening.</returns>
    public int? Announce(string text, string activityId, bool important)
    {
        if (_disconnected || !UiaClientsAreListening() || string.IsNullOrEmpty(text))
        {
            return null;
        }

        // NotificationKind_Other = 4; NotificationProcessing_ImportantMostRecent = 1, _MostRecent = 3.
        return UiaRaiseNotificationEvent(this, 4, important ? 1 : 3, text, activityId);
    }

    /// <summary>Releases UIA's references before the window is destroyed; later calls report the elements as gone.</summary>
    public void Disconnect()
    {
        if (_disconnected)
        {
            return;
        }

        _disconnected = true;
        _ = UiaReturnRawElementProvider(hwnd, 0, 0, null);
        lock (_gate)
        {
            foreach (var element in _elements.Values)
            {
                _ = UiaDisconnectProvider(element);
            }

            _elements.Clear();
        }

        _ = UiaDisconnectProvider(this);
    }

    /// <summary>
    /// Posts a request to the window's UI thread (tests pass <c>post</c> instead). Throws ElementNotAvailable after
    /// shutdown.
    /// </summary>
    internal void Post(AutomationRequest request, int value = 0)
    {
        var posted = !_disconnected && (post?.Invoke(request, value) ?? PostMessageW(hwnd, requestMessage, (nint)request, value));
        if (!posted)
        {
            throw new ElementNotAvailableException("The island is no longer available.");
        }
    }

    internal static UiaRect ToScreen(AutomationSnapshot s, RoundedBox box) =>
        new() { Left = s.OriginX + box.Left, Top = s.OriginY + box.Top, Width = box.Width, Height = box.Height };

    /// <summary>The provider object for a child ID: the same object for the life of the process, so identity is stable.</summary>
    internal IslandAutomationElement ElementFor(string id)
    {
        lock (_gate)
        {
            if (!_elements.TryGetValue(id, out var element))
            {
                element = new IslandAutomationElement(this, id, ++_nextRuntimeId);
                _elements.Add(id, element);
            }

            return element;
        }
    }

    private static bool SameIds(IReadOnlyList<AccessibleNode> a, IReadOnlyList<AccessibleNode> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Id, b[i].Id, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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
    private static extern int UiaRaiseAutomationEvent(IRawElementProviderSimple provider, int eventId);

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaRaiseStructureChangedEvent(IRawElementProviderSimple provider, int structureChangeType, int[] runtimeId, int runtimeIdLength);

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaDisconnectProvider(IRawElementProviderSimple provider);

    [DllImport("UIAutomationCore.dll", CharSet = CharSet.Unicode)]
    private static extern int UiaRaiseNotificationEvent(IRawElementProviderSimple provider, int kind, int processing, [MarshalAs(UnmanagedType.BStr)] string displayString, [MarshalAs(UnmanagedType.BStr)] string activityId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);
#pragma warning restore SYSLIB1054
}

/// <summary>
/// One child of the island in the UIA tree. It holds only its ID and reads its current meaning from the root's
/// snapshot, so a reference held by a client stays valid while the element exists and reports ElementNotAvailable once
/// it is gone (an ended alert, a collapsed panel) instead of describing something no longer on screen.
/// </summary>
internal sealed class IslandAutomationElement(IslandAutomationProvider root, string id, int runtimeId) :
    IRawElementProviderSimple, IRawElementProviderFragment, IInvokeProvider, IToggleProvider, IRangeValueProvider
{
    public string Id => id;

    public ProviderOptions ProviderOptions => ProviderOptions.ServerSideProvider | ProviderOptions.UseComThreading;

    public IRawElementProviderSimple? HostRawElementProvider => null;

    public UiaRect BoundingRectangle => Current is { } node ? IslandAutomationProvider.ToScreen(root.Snapshot, node.Bounds) : default;

    public IRawElementProviderFragmentRoot FragmentRoot => root;

    public ToggleState ToggleState => Node().Toggled == true ? ToggleState.On : ToggleState.Off;

    public double Value => Node().Value ?? 0;

    public bool IsReadOnly => Node().Role != AccessibleRole.Slider;

    public double Maximum => 100;

    public double Minimum => 0;

    public double LargeChange => 10;

    public double SmallChange => 1;

    private AccessibleNode? Current => root.Disconnected ? null : root.Snapshot.Find(id);

    public object? GetPatternProvider(int patternId)
    {
        if (Current is not { } node)
        {
            return null;
        }

        return (patternId, node.Role) switch
        {
            (IslandAutomationProvider.InvokePatternId, AccessibleRole.Button) => this,
            (IslandAutomationProvider.TogglePatternId, AccessibleRole.ToggleButton) => this,
            (IslandAutomationProvider.RangeValuePatternId, AccessibleRole.Slider or AccessibleRole.ProgressBar) => this,
            _ => null,
        };
    }

    public object? GetPropertyValue(int propertyId)
    {
        if (Current is not { } node)
        {
            return null;
        }

        return propertyId switch
        {
            IslandAutomationProvider.ControlTypePropertyId => node.Role switch
            {
                AccessibleRole.Button or AccessibleRole.ToggleButton => IslandAutomationProvider.ButtonControlTypeId,
                AccessibleRole.Slider => IslandAutomationProvider.SliderControlTypeId,
                AccessibleRole.ProgressBar => IslandAutomationProvider.ProgressBarControlTypeId,
                _ => IslandAutomationProvider.TextControlTypeId,
            },
            IslandAutomationProvider.NamePropertyId => node.Name,
            IslandAutomationProvider.AutomationIdPropertyId => node.Id,
            IslandAutomationProvider.IsKeyboardFocusablePropertyId => false,
            IslandAutomationProvider.IsEnabledPropertyId => true,
            IslandAutomationProvider.IsOffscreenPropertyId => !root.Snapshot.Visible,
            IslandAutomationProvider.LiveSettingPropertyId when node.Role == AccessibleRole.Status => node.Assertive ? 2 : 1, // Assertive : Polite
            _ => null,
        };
    }

    public IRawElementProviderFragment? Navigate(NavigateDirection direction)
    {
        var s = root.Snapshot;
        var i = s.IndexOf(id);
        if (i < 0)
        {
            return null;
        }

        return direction switch
        {
            NavigateDirection.Parent => root,
            NavigateDirection.NextSibling when i + 1 < s.Children.Count => root.ElementFor(s.Children[i + 1].Id),
            NavigateDirection.PreviousSibling when i > 0 => root.ElementFor(s.Children[i - 1].Id),
            _ => null,
        };
    }

    public int[]? GetRuntimeId() => [IslandAutomationProvider.UiaAppendRuntimeId, runtimeId];

    public object[]? GetEmbeddedFragmentRoots() => null;

    public void SetFocus()
    {
        // Elements are not focusable: the island never takes keyboard focus through UIA.
    }

    public void Invoke() => root.Post(Request(Node(), AccessibleRole.Button));

    public void Toggle() => root.Post(Request(Node(), AccessibleRole.ToggleButton));

    public void SetValue(double value)
    {
        var node = Node();
        if (node.Role != AccessibleRole.Slider)
        {
            throw new InvalidOperationException("The element is read-only.");
        }

        if (!double.IsFinite(value) || value < Minimum || value > Maximum)
        {
            throw new ArgumentException("The value is outside 0 to 100.", nameof(value));
        }

        root.Post(AutomationRequest.SetVolume, (int)Math.Round(value));
    }

    private static AutomationRequest Request(AccessibleNode node, AccessibleRole expected)
    {
        if (node.Role != expected)
        {
            throw new InvalidOperationException("The element does not support this action.");
        }

        return node.Command switch
        {
            AccessibleCommand.MediaPrevious => AutomationRequest.MediaPrevious,
            AccessibleCommand.MediaPlayPause => AutomationRequest.MediaPlayPause,
            AccessibleCommand.MediaNext => AutomationRequest.MediaNext,
            AccessibleCommand.ToggleMute => AutomationRequest.ToggleMute,
            _ => throw new InvalidOperationException("The element has no action."),
        };
    }

    private AccessibleNode Node() => Current ?? throw new ElementNotAvailableException("The element is no longer on screen.");
}
