using Halo.Core.Storage;
using Halo.Core.Tasks;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>F-F04 local reminders and F-F05 to-do list: editing, due reminders, snooze, recurrence, persistence.</summary>
public class TaskListTests
{
    private readonly ManualClock _clock = new();

    [Fact]
    public void CreateEditCompleteDelete()
    {
        var list = new TaskList(_clock);
        var a = list.Add("  Buy milk \u0007 ")!;
        Assert.Equal("Buy milk", a.Text); // trimmed, control characters removed
        Assert.Null(list.Add("   "));
        var due = _clock.GetUtcNow().AddHours(2);
        var b = list.Add("Call the bank", due)!;
        Assert.Equal(2, list.PendingCount);
        Assert.Equal(b.Id, list.NextDue!.Id);
        Assert.Equal([b.Id, a.Id], list.Pending.Select(i => i.Id)); // dated first

        Assert.True(list.Edit(a.Id, "Buy oat milk", null).Changed);
        Assert.Equal("Buy oat milk", list.Find(a.Id)!.Text);
        Assert.False(list.Edit(a.Id, "", null).Changed);

        Assert.True(list.Complete(a.Id, TimeZoneInfo.Utc).Changed);
        Assert.True(list.Find(a.Id)!.Done);
        Assert.Equal(1, list.PendingCount);
        Assert.True(list.Reopen(a.Id).Changed);
        Assert.True(list.Delete(a.Id).Changed);
        Assert.Null(list.Find(a.Id));
        Assert.False(list.Delete(a.Id).Changed);
    }

    [Fact]
    public void TextIsCappedAndTheListIsBounded()
    {
        var list = new TaskList(_clock);
        Assert.Equal(TaskItem.MaxTextLength, list.Add(new string('x', 1000))!.Text.Length);
        for (var i = 1; i < TaskList.MaxItems; i++)
        {
            list.Add($"t{i}");
        }

        Assert.Null(list.Add("one too many"));
    }

    [Fact]
    public void ARemindersIsDueOnceItsTimePassesAndSnoozeAndDismissWork()
    {
        var list = new TaskList(_clock);
        var r = list.Add("Stand up", _clock.GetUtcNow().AddMinutes(10))!;
        Assert.Empty(list.DueNow(_clock.GetUtcNow()));
        Assert.Equal(_clock.GetUtcNow().AddMinutes(10), list.NextAlert(_clock.GetUtcNow()));

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(r.Id, Assert.Single(list.DueNow(_clock.GetUtcNow())).Id);

        Assert.True(list.Snooze(r.Id, TimeSpan.FromMinutes(5)).Changed);
        Assert.Empty(list.DueNow(_clock.GetUtcNow()));
        _clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(list.DueNow(_clock.GetUtcNow()));

        Assert.True(list.Dismiss(r.Id, TimeZoneInfo.Utc).Changed);
        Assert.Empty(list.DueNow(_clock.GetUtcNow()));
        Assert.False(list.Find(r.Id)!.Done); // dismissing the reminder does not complete the task
        Assert.False(list.Snooze(list.Add("no date")!.Id, TimeSpan.FromMinutes(5)).Changed);
    }

    [Fact]
    public void RecurringRemindersKeepTheirLocalTimeAcrossDaylightSaving()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date,
            DateTime.MaxValue.Date,
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/CE2", TimeSpan.FromHours(1), "Test", "Test", "Test DST", [rule]);

        // 09:00 local on Saturday 24 October 2026 (summer time, UTC+2) = 07:00 UTC.
        var saturday = new DateTimeOffset(2026, 10, 24, 7, 0, 0, TimeSpan.Zero);
        var next = TaskList.NextOccurrence(saturday, Recurrence.Daily, saturday, zone);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero), next); // Sunday 09:00 winter time (UTC+1)
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 8, 0, 0, TimeSpan.Zero), TaskList.NextOccurrence(saturday, Recurrence.Weekly, saturday, zone));

        // Missed for several days: the next one is in the future, not a replay of each missed day.
        Assert.True(TaskList.NextOccurrence(saturday, Recurrence.Daily, saturday.AddDays(10), zone) > saturday.AddDays(10));
    }

    [Fact]
    public void CompletingOrDismissingARecurringItemMovesItToTheNextOccurrence()
    {
        var list = new TaskList(_clock);
        var due = _clock.GetUtcNow().AddMinutes(1);
        var r = list.Add("Water the plants", due, Recurrence.Daily)!;
        _clock.Advance(TimeSpan.FromMinutes(1));
        list.Complete(r.Id, TimeZoneInfo.Utc);
        var item = list.Find(r.Id)!;
        Assert.False(item.Done);
        Assert.Equal(due.AddDays(1), item.DueUtc);
        list.Dismiss(r.Id, TimeZoneInfo.Utc);
        Assert.Equal(due.AddDays(2), list.Find(r.Id)!.DueUtc);
        Assert.Equal(Recurrence.None, list.Add("plain")!.Recurrence);
    }

    [Fact]
    public void RemindersMissedWhileClosedAreDueAtTheNextStartOnceEach()
    {
        var list = new TaskList(_clock);
        list.Add("A", _clock.GetUtcNow().AddMinutes(1));
        list.Add("B", _clock.GetUtcNow().AddMinutes(2));
        var json = list.ToJson();

        _clock.Advance(TimeSpan.FromHours(3)); // Halo was closed
        var restored = new TaskList(_clock);
        Assert.True(restored.Load(json));
        var due = restored.DueNow(_clock.GetUtcNow());
        Assert.Equal(["A", "B"], due.Select(d => d.Text));
        foreach (var d in due)
        {
            restored.Dismiss(d.Id, TimeZoneInfo.Utc);
        }

        Assert.Empty(restored.DueNow(_clock.GetUtcNow()));
    }

    [Fact]
    public void PersistenceRoundTripsAndDropsBadAndOldItems()
    {
        var list = new TaskList(_clock);
        var a = list.Add("Keep", _clock.GetUtcNow().AddDays(1), Recurrence.Weekly)!;
        list.Snooze(a.Id, TimeSpan.FromMinutes(5));
        var old = list.Add("Old and done")!;
        list.Complete(old.Id, TimeZoneInfo.Utc);
        var json = list.ToJson();

        var same = new TaskList(_clock);
        Assert.True(same.Load(json));
        Assert.Equal(list.Items, same.Items);

        _clock.Advance(TaskList.KeepCompleted + TimeSpan.FromDays(1));
        var later = new TaskList(_clock);
        later.Load(json);
        Assert.Null(later.Find(old.Id)); // completed more than 30 days ago

        var junk = new TaskList(_clock);
        Assert.True(junk.Load("{ \"items\": [ { \"id\": \"x\" }, { \"id\": \"y\", \"text\": \"ok\", \"createdUtc\": \"2026-10-29T09:00:00Z\", \"recurrence\": \"Daily\" }, 5 ] }"));
        var only = Assert.Single(junk.Items);
        Assert.Equal(Recurrence.None, only.Recurrence); // recurrence without a due time is meaningless
        Assert.False(junk.Load("{ \"items\": 3 }"));
    }

    [Fact]
    public void TheStoreWritesAtomicallyAndCanDeleteEverything()
    {
        var dir = Path.Combine(Path.GetTempPath(), "halo-tasks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TaskStore(dir);
            var list = new TaskList(_clock);
            Assert.Equal(DataLoadStatus.Missing, store.Load(list));
            list.Add("One");
            Assert.True(store.Save(list));
            var again = new TaskList(_clock);
            Assert.Equal(DataLoadStatus.Loaded, new TaskStore(dir).Load(again));
            Assert.Equal("One", Assert.Single(again.Items).Text);
            store.Delete();
            Assert.False(File.Exists(store.FilePath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
