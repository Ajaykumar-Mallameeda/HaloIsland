namespace Halo.Core.Island;

/// <summary>Presentations implemented in the first vertical slice (ADR-001 §6.1 defines the full set).</summary>
public enum IslandPresentation
{
    Compact,
    Expanded,

    /// <summary>A transient event panel (volume, battery, network…): a wider pill that never pins.</summary>
    Alert,
}

/// <summary>
/// Interaction phase (FR-ISL-03 hover-intent, FR-ISL-04 click). Exactly one phase holds at a time, so a pending hover
/// action and an explicit expansion can never both be in effect.
/// </summary>
public enum IslandPhase
{
    /// <summary>Compact, nothing pending.</summary>
    Collapsed,

    /// <summary>Compact; the pointer rests on the pill and the hover dwell timer runs.</summary>
    ExpandPending,

    /// <summary>Expanded by hover-intent; leaving the island starts the leave-grace timer.</summary>
    ExpandedByHover,

    /// <summary>Expanded by hover-intent; the pointer is outside and the leave-grace timer runs.</summary>
    CollapsePending,

    /// <summary>
    /// Expanded by an explicit action (click, menu, tray, UI Automation, second launch). Stays open until an explicit
    /// collapse; pointer movement and hover timers never close it.
    /// </summary>
    ExpandedPinned,
}

/// <summary>The one-shot timer a phase needs while it lasts.</summary>
public enum IslandTimer
{
    None,
    HoverDwell,
    LeaveGrace,

    /// <summary>The shown alert's lifetime (its own timer, independent of the hover timers).</summary>
    Alert,
}

/// <summary>Inputs to the island. Pointer coordinates are already hit-tested by the window.</summary>
public enum IslandInput
{
    /// <summary>The pointer moved onto the visible shape.</summary>
    PointerEntered,

    /// <summary>The pointer left the visible shape (or the window).</summary>
    PointerLeft,
    PointerPressed,

    /// <summary>Button released over the island shape: a completed click.</summary>
    PointerReleasedInside,

    /// <summary>Button released away from the shape (drag off): cancels the press without toggling.</summary>
    PointerReleasedOutside,
    Toggle,
    Expand,
    Collapse,

    /// <summary>The hover dwell timer expired.</summary>
    HoverDwellElapsed,

    /// <summary>The leave-grace timer expired.</summary>
    LeaveGraceElapsed,

    /// <summary>A context menu is about to open: a pending hover expansion is dropped.</summary>
    MenuRequested,

    /// <summary>A full-screen app took the foreground on the island's monitor (FR-ISL-09).</summary>
    FullscreenStarted,

    /// <summary>The full-screen condition ended.</summary>
    FullscreenEnded,

    /// <summary>The user asked to see the island despite full screen (tray "Show island now").</summary>
    ShowNowRequested,

    /// <summary>The user returned to automatic suppression.</summary>
    ShowNowCleared,
}

/// <summary>
/// Visual and interaction state of the island at one instant. <see cref="Suppressed"/> means a full-screen app is in
/// front; <see cref="ShowNow"/> is the user's override. Precedence: the user's override, then full-screen suppression,
/// then normal behaviour.
/// </summary>
public readonly record struct IslandState(IslandPhase Phase, bool Hovered, bool Pressed)
{
    public static IslandState Initial { get; } = new(IslandPhase.Collapsed, Hovered: false, Pressed: false);

    public bool Suppressed { get; init; }

    public bool ShowNow { get; init; }

    /// <summary>The transient alert on screen, or null. Never set while the island is expanded.</summary>
    public IslandAlert? Alert { get; init; }

    /// <summary>Suppressed by full screen without the user's override (pointer input is ignored).</summary>
    public bool Hidden => Suppressed && !ShowNow;

    /// <summary>
    /// False while suppressed without an override, unless the alert on screen is allowed over full screen: the window
    /// is hidden and does not render.
    /// </summary>
    public bool Visible => !Hidden || Alert is { AllowedWhenSuppressed: true };

    public bool IsExpanded => Phase is IslandPhase.ExpandedByHover or IslandPhase.CollapsePending or IslandPhase.ExpandedPinned;

    public IslandPresentation Presentation => IsExpanded ? IslandPresentation.Expanded : Alert is not null ? IslandPresentation.Alert : IslandPresentation.Compact;

    /// <summary>The timer this phase waits for, if any.</summary>
    public IslandTimer PendingTimer => Phase switch
    {
        IslandPhase.ExpandPending => IslandTimer.HoverDwell,
        IslandPhase.CollapsePending => IslandTimer.LeaveGrace,
        _ => IslandTimer.None,
    };
}

/// <summary>What the surface must do after an input. <see cref="PresentationChanged"/> starts a transition.</summary>
public readonly record struct IslandTransition(IslandState Previous, IslandState Next)
{
    public bool PresentationChanged => Previous.Presentation != Next.Presentation;

    public bool PhaseChanged => Previous.Phase != Next.Phase;

    public bool HoverChanged => Previous.Hovered != Next.Hovered;

    public bool PressChanged => Previous.Pressed != Next.Pressed;

    public bool VisibilityChanged => Previous.Visible != Next.Visible;

    /// <summary>The alert appeared, ended or changed content (same key coalesced).</summary>
    public bool AlertChanged => !Equals(Previous.Alert, Next.Alert);

    public bool Changed => Previous != Next;

    /// <summary>True when the previous phase's timer must be stopped.</summary>
    public bool CancelsTimer => PhaseChanged && Previous.PendingTimer != IslandTimer.None;

    /// <summary>The timer to start for the new phase (none if the phase did not change).</summary>
    public IslandTimer StartsTimer => PhaseChanged ? Next.PendingTimer : IslandTimer.None;
}

/// <summary>
/// The island's interaction rules as a pure function (ADR-001 §6.1). Hover-intent (FR-ISL-03): resting on the pill
/// for the dwell time expands it; leaving the expanded island for the leave grace collapses it; coming back within
/// the grace cancels the collapse. Click (FR-ISL-04): a completed click on a compact or hover-opened island pins it
/// open, and a click on a pinned island collapses it. Pinning a hover-opened island (rather than closing it) matters
/// because the dwell usually expires while the user is still moving in to click. Timer expiries are inputs, and each
/// is ignored unless the phase still waits for it, so a stale timer can never undo a newer decision.
/// </summary>
public static class IslandStateMachine
{
    public static IslandTransition Apply(IslandState state, IslandInput input)
    {
        var previous = state;
        if (state.Hidden && input is IslandInput.Expand or IslandInput.Toggle)
        {
            // An explicit request to open the island while it is suppressed is also a request to see it.
            state = state with { ShowNow = true };
        }
        else if (state.Hidden && IsPointer(input))
        {
            // A hidden window gets no real pointer input; late messages are ignored.
            return new IslandTransition(state, state);
        }

        var next = input switch
        {
            IslandInput.PointerEntered => Enter(state),
            IslandInput.PointerLeft => Leave(state),
            IslandInput.PointerPressed => state with { Pressed = true, Phase = state.Phase == IslandPhase.ExpandPending ? IslandPhase.Collapsed : state.Phase },
            IslandInput.PointerReleasedInside when state.Pressed => Click(state) with { Pressed = false },
            IslandInput.PointerReleasedInside => state,
            IslandInput.PointerReleasedOutside => state with { Pressed = false },
            IslandInput.Toggle => state with { Phase = state.IsExpanded ? IslandPhase.Collapsed : IslandPhase.ExpandedPinned },
            IslandInput.Expand => state with { Phase = IslandPhase.ExpandedPinned },
            IslandInput.Collapse => state with { Phase = IslandPhase.Collapsed },
            IslandInput.HoverDwellElapsed when state.Phase == IslandPhase.ExpandPending && state.Hovered => state with { Phase = IslandPhase.ExpandedByHover },
            IslandInput.HoverDwellElapsed => state,
            IslandInput.LeaveGraceElapsed when state.Phase == IslandPhase.CollapsePending && !state.Hovered => state with { Phase = IslandPhase.Collapsed },
            IslandInput.LeaveGraceElapsed => state,
            IslandInput.MenuRequested when state.Phase == IslandPhase.ExpandPending => state with { Phase = IslandPhase.Collapsed },
            IslandInput.MenuRequested => state,
            IslandInput.FullscreenStarted when state.ShowNow => state with { Suppressed = true },
            IslandInput.FullscreenStarted => Hide(state),
            IslandInput.FullscreenEnded => state with { Suppressed = false, ShowNow = false },
            IslandInput.ShowNowRequested when state.Suppressed => state with { ShowNow = true },
            IslandInput.ShowNowRequested => state,
            IslandInput.ShowNowCleared when state.Suppressed => Hide(state),
            IslandInput.ShowNowCleared => state with { ShowNow = false },
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, null),
        };

        if (next.IsExpanded && next.Alert is not null)
        {
            // The expanded panel supersedes a transient alert.
            next = next with { Alert = null };
        }

        return new IslandTransition(previous, next);
    }

    private static bool IsPointer(IslandInput input) => input is IslandInput.PointerEntered or IslandInput.PointerLeft or IslandInput.PointerPressed
        or IslandInput.PointerReleasedInside or IslandInput.PointerReleasedOutside;

    /// <summary>Suppressed without override: collapsed, no feedback, nothing pending.</summary>
    private static IslandState Hide(IslandState state) =>
        state with
        {
            Suppressed = true,
            ShowNow = false,
            Phase = IslandPhase.Collapsed,
            Hovered = false,
            Pressed = false,
            Alert = state.Alert is { AllowedWhenSuppressed: true } ? state.Alert : null,
        };

    private static IslandState Enter(IslandState state)
    {
        if (state.Hovered)
        {
            // Already inside: a repeated enter must not restart the dwell.
            return state;
        }

        var phase = state.Phase switch
        {
            IslandPhase.Collapsed when !state.Pressed => IslandPhase.ExpandPending,
            IslandPhase.CollapsePending => IslandPhase.ExpandedByHover,
            var p => p,
        };
        return state with { Hovered = true, Phase = phase };
    }

    private static IslandState Leave(IslandState state)
    {
        var phase = state.Phase switch
        {
            IslandPhase.ExpandPending => IslandPhase.Collapsed,
            IslandPhase.ExpandedByHover => IslandPhase.CollapsePending,
            var p => p,
        };
        return state with { Hovered = false, Pressed = false, Phase = phase };
    }

    private static IslandState Click(IslandState state) =>
        state with { Phase = state.Phase == IslandPhase.ExpandedPinned ? IslandPhase.Collapsed : IslandPhase.ExpandedPinned };
}
