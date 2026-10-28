namespace Halo.Core.Tests;

/// <summary>
/// A test clock with separate wall-clock and monotonic time: <see cref="Advance"/> moves both (time passes),
/// <see cref="JumpWallClock"/> moves only the wall clock (the user or the network changed the time).
/// </summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _wall = start;
    private long _ticks;

    public ManualClock()
        : this(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero))
    {
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => _wall;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by)
    {
        _wall += by;
        _ticks += by.Ticks;
    }

    public void JumpWallClock(TimeSpan by) => _wall += by;
}
