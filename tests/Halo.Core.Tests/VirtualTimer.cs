using Halo.Core.Island;

namespace Halo.Core.Tests;

/// <summary>
/// The island's timers and clock on virtual time, with Win32 SetTimer semantics per slot (Start replaces, Cancel
/// stops). <see cref="IsArmed"/>, <see cref="Started"/> and <see cref="Fired"/> describe the hover (intent) slot.
/// </summary>
internal sealed class VirtualTimer : TimeProvider, IIslandTimer
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private readonly Dictionary<IslandTimerSlot, TimeSpan> _due = [];
    private IslandController? _island;
    private TimeSpan _now;

    public bool IsArmed => _due.ContainsKey(IslandTimerSlot.Intent);

    public bool IsAlertArmed => _due.ContainsKey(IslandTimerSlot.Alert);

    public int Started { get; private set; }

    public int Fired { get; private set; }

    public int AlertFired { get; private set; }

    public List<IslandTransition> Transitions { get; } = [];

    public void Attach(IslandController island) => _island = island;

    public override DateTimeOffset GetUtcNow() => Epoch + _now;

    public void Start(IslandTimerSlot slot, TimeSpan delay)
    {
        if (slot == IslandTimerSlot.Intent)
        {
            Started++;
        }

        _due[slot] = _now + delay;
    }

    public void Cancel(IslandTimerSlot slot) => _due.Remove(slot);

    /// <summary>Moves time forward, firing every timer that falls due, earliest first.</summary>
    public void Advance(TimeSpan by)
    {
        var end = _now + by;
        while (_due.Count > 0 && _due.MinBy(d => d.Value) is { Value: var due, Key: var slot } && due <= end)
        {
            _now = due;
            _due.Remove(slot);
            if (slot == IslandTimerSlot.Intent)
            {
                Fired++;
            }
            else
            {
                AlertFired++;
            }

            Transitions.Add(_island!.TimerElapsed(slot));
        }

        _now = end;
    }
}
