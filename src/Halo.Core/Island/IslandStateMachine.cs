namespace Halo.Core.Island;

/// <summary>Presentations implemented in the first vertical slice (ADR-001 §6.1 defines the full set).</summary>
public enum IslandPresentation
{
    Compact,
    Expanded,
}

/// <summary>Inputs to the island. Pointer coordinates are already hit-tested by the window.</summary>
public enum IslandInput
{
    PointerEntered,
    PointerLeft,
    PointerPressed,

    /// <summary>Button released over the island shape: a completed click.</summary>
    PointerReleasedInside,

    /// <summary>Button released away from the shape (drag off): cancels the press without toggling.</summary>
    PointerReleasedOutside,
    Toggle,
    Expand,
    Collapse,
}

/// <summary>Visual state of the island at one instant.</summary>
public readonly record struct IslandState(IslandPresentation Presentation, bool Hovered, bool Pressed)
{
    public static IslandState Initial { get; } = new(IslandPresentation.Compact, Hovered: false, Pressed: false);

    public bool IsExpanded => Presentation == IslandPresentation.Expanded;
}

/// <summary>What the surface must do after an input. <see cref="PresentationChanged"/> starts a transition.</summary>
public readonly record struct IslandTransition(IslandState Previous, IslandState Next)
{
    public bool PresentationChanged => Previous.Presentation != Next.Presentation;

    public bool HoverChanged => Previous.Hovered != Next.Hovered;

    public bool PressChanged => Previous.Pressed != Next.Pressed;

    public bool Changed => Previous != Next;
}

/// <summary>
/// Click-to-open mode (FR-ISL-04) as a pure function: a completed click toggles Compact ↔ Expanded; hover and
/// press only change feedback. Hover-intent expansion (FR-ISL-03) is not part of this slice.
/// </summary>
public static class IslandStateMachine
{
    public static IslandTransition Apply(IslandState state, IslandInput input)
    {
        var next = input switch
        {
            IslandInput.PointerEntered => state with { Hovered = true },
            IslandInput.PointerLeft => state with { Hovered = false, Pressed = false },
            IslandInput.PointerPressed => state with { Pressed = true },
            IslandInput.PointerReleasedInside when state.Pressed => Flip(state) with { Pressed = false },
            IslandInput.PointerReleasedInside => state,
            IslandInput.PointerReleasedOutside => state with { Pressed = false },
            IslandInput.Toggle => Flip(state),
            IslandInput.Expand => state with { Presentation = IslandPresentation.Expanded },
            IslandInput.Collapse => state with { Presentation = IslandPresentation.Compact },
            _ => throw new ArgumentOutOfRangeException(nameof(input), input, null),
        };
        return new IslandTransition(state, next);
    }

    private static IslandState Flip(IslandState state) =>
        state with { Presentation = state.IsExpanded ? IslandPresentation.Compact : IslandPresentation.Expanded };
}
