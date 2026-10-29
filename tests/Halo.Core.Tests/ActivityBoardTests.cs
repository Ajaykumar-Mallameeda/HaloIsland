using Halo.Core.Activities;
using Halo.Core.Island;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>B-F03: the activity board — primary selection, coalescing, expiry, switching, quiet mode, suppression.</summary>
public class ActivityBoardTests
{
    private readonly ManualClock _time = new();

    [Fact]
    public void TheHighestPriorityIsPrimaryThenTheFirstStarted()
    {
        // Live Activity Engine 2.0 (Apple's rule): with equal priority and relevance the first activity started stays in
        // the island; the newer one waits beside it (secondary). Previously the most recent one took over.
        var board = new ActivityBoard(_time);
        board.Upsert(A("timer", AlertPriority.Normal));
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Upsert(A("note", AlertPriority.Normal));
        Assert.Equal("timer", board.Primary!.Id);
        Assert.Equal("note", board.Secondary!.Id);

        board.Upsert(A("reminder", AlertPriority.High));
        Assert.Equal("reminder", board.Primary!.Id);
        Assert.Equal(2, board.OthersCount);
    }

    [Fact]
    public void AnEqualUpdateChangesNothingAndDoesNotReorder()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(A("a", AlertPriority.Normal));
        _time.Advance(TimeSpan.FromSeconds(1));
        board.Upsert(A("b", AlertPriority.Normal));
        _time.Advance(TimeSpan.FromSeconds(1));

        var again = board.Upsert(A("b", AlertPriority.Normal));
        Assert.False(again.Changed);
        Assert.Equal("a", board.Primary!.Id);

        // A real update (a tick) changes content but never the order: progress cannot take the island.
        var real = board.Upsert(A("b", AlertPriority.Normal) with { CompactText = "04:58" });
        Assert.True(real.Changed);
        Assert.False(real.PrimaryChanged);
        Assert.Equal("a", board.Primary!.Id);
        Assert.Equal("04:58", board.Secondary!.CompactText);
    }

    [Fact]
    public void UpdatesKeepTheCreationTime()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(A("a", AlertPriority.Normal));
        var created = board.Find("a")!.CreatedAt;
        _time.Advance(TimeSpan.FromSeconds(5));
        board.Upsert(A("a", AlertPriority.Normal) with { CompactText = "x" });
        Assert.Equal(created, board.Find("a")!.CreatedAt);
        Assert.Equal(_time.GetUtcNow(), board.Find("a")!.UpdatedAt);
    }

    [Fact]
    public void TheBoardIsBoundedAndDropsTheLeastImportant()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(A("important", AlertPriority.High));
        for (var i = 0; i < 20; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            board.Upsert(A($"n{i}", AlertPriority.Low));
        }

        Assert.Equal(ActivityBoard.MaxActivities, board.Count);
        Assert.NotNull(board.Find("important"));
        Assert.Null(board.Find("n0")); // the oldest low-priority ones went first
        Assert.NotNull(board.Find("n19"));
    }

    [Fact]
    public void ExpiredActivitiesAreRemovedAndTheNextExpiryIsKnown()
    {
        var board = new ActivityBoard(_time);
        var now = _time.GetUtcNow();
        board.Upsert(A("soon", AlertPriority.High) with { Ongoing = false, ExpiresAt = now.AddSeconds(5) });
        board.Upsert(A("later", AlertPriority.Normal) with { Ongoing = false, ExpiresAt = now.AddSeconds(30) });
        board.Upsert(A("ongoing", AlertPriority.Low));
        Assert.Equal(now.AddSeconds(5), board.NextExpiry);

        Assert.False(board.RemoveExpired().Changed);
        _time.Advance(TimeSpan.FromSeconds(5));
        var update = board.RemoveExpired();
        Assert.True(update.PrimaryChanged);
        Assert.Equal("later", board.Primary!.Id);
        Assert.Equal(now.AddSeconds(30), board.NextExpiry);
    }

    [Fact]
    public void ExplicitSelectionWinsUntilTheActivityEnds()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(A("high", AlertPriority.High));
        board.Upsert(A("low", AlertPriority.Low));

        Assert.True(board.Select("low").PrimaryChanged);
        Assert.Equal("low", board.Primary!.Id);
        board.Upsert(A("high", AlertPriority.High) with { CompactText = "changed" });
        Assert.Equal("low", board.Primary!.Id); // an update elsewhere does not take the user's choice away

        board.Remove("low");
        Assert.Equal("high", board.Primary!.Id);
        Assert.False(board.Select("missing").Changed);
    }

    [Fact]
    public void SelectNextVisitsEveryActivityInAStableOrder()
    {
        var board = new ActivityBoard(_time);
        foreach (var id in new[] { "a", "b", "c" })
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            board.Upsert(A(id, AlertPriority.Normal));
        }

        var seen = new List<string> { board.Primary!.Id };
        for (var i = 0; i < 3; i++)
        {
            board.SelectNext();
            seen.Add(board.Primary!.Id);
        }

        Assert.Equal(3, seen.Take(3).Distinct().Count());
        Assert.Equal(seen[0], seen[3]); // wraps around
    }

    [Fact]
    public void QuietModeKeepsOnlyAllowedActivitiesEligibleAndRestoresTheRest()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(A("note", AlertPriority.High));
        board.Upsert(A("timer", AlertPriority.Normal) with { AllowedInQuietMode = true });

        var quiet = board.SetQuiet(true);
        Assert.True(quiet.PrimaryChanged);
        Assert.Equal("timer", board.Primary!.Id);
        Assert.Equal(0, board.OthersCount);
        Assert.Equal(2, board.Count); // nothing is thrown away

        board.SetQuiet(false);
        Assert.Equal("note", board.Primary!.Id);
    }

    [Fact]
    public void FullScreenSuppressionLeavesOnlyActivitiesAllowedOverFullScreen()
    {
        var board = new ActivityBoard(_time);
        board.Upsert(A("timer", AlertPriority.Normal));
        board.SetSuppressed(true);
        Assert.Null(board.Primary);
        board.Upsert(A("done", AlertPriority.High) with { AllowedWhenSuppressed = true });
        Assert.Equal("done", board.Primary!.Id);
        board.SetSuppressed(false);
        Assert.Equal("done", board.Primary!.Id);
    }

    [Fact]
    public void PrivateTextIsRedactedUnlessPreviewsAreAllowed()
    {
        var task = A("task", AlertPriority.Normal) with { Privacy = ActivityPrivacy.Private, CompactText = "Call the bank", RedactedText = "Task due" };
        Assert.Equal("Task due", task.CompactFor(showPrivate: false));
        Assert.Equal("Call the bank", task.CompactFor(showPrivate: true));
        Assert.Equal("04:59", A("t", AlertPriority.Normal).CompactFor(false));
    }

    private static IslandActivity A(string id, AlertPriority priority) =>
        new(id, ActivityKind.Timer, priority) { Glyph = "x", CompactText = "04:59", Title = "04:59" };
}
