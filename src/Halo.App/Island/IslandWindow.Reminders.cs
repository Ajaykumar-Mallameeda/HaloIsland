using Halo.Core.Activities;
using Halo.Core.Diagnostics;
using Halo.Core.Island;
using Halo.Core.Notifications;
using Halo.Core.Storage;
using Halo.Core.Tasks;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Local reminders and the to-do list (F-F04/F-F05) through the notification framework (D-F01): a due item becomes a
/// <see cref="HaloNotification"/> from the "halo.reminders" provider, which the center deduplicates and turns into an
/// activity (snooze 10 min, done, dismiss) and an arrival alert. One one-shot timer waits for the next alert time.
/// Task text is private: it shows only when the user allows previews.
/// </summary>
internal sealed partial class IslandWindow
{
    public const string RemindersProvider = "halo.reminders";
    private const nuint ReminderTimerId = 13;
    private static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(10);
    private static readonly ActivityAction SnoozeReminder = new("snooze", "Snooze 10 minutes", "");
    private static readonly ActivityAction CompleteReminder = new("done", "Mark done", "");

    private readonly TaskList _tasks = new();
    private readonly NotificationCenter _notifications = new();
    private TaskStore? _taskStore;
    private TasksWindow? _tasksWindow;

    /// <summary>The to-do list (read by the tasks window).</summary>
    internal TaskList Tasks => _tasks;

    internal TaskItem? AddTask(string text, DateTimeOffset? dueUtc, Recurrence recurrence)
    {
        var item = _tasks.Add(text, dueUtc, recurrence);
        _log?.Write("task", ("change", item is null ? "rejected" : "added"), ("due", dueUtc is null ? 0 : 1), ("recurrence", recurrence.ToString().ToLowerInvariant()));
        AfterTaskChange();
        return item;
    }

    internal bool EditTask(string id, string text, DateTimeOffset? dueUtc, Recurrence recurrence)
    {
        var changed = _tasks.Edit(id, text, dueUtc, recurrence).Changed;
        _log?.Write("task", ("change", changed ? "edited" : "unchanged"));
        WithdrawReminder(id);
        AfterTaskChange();
        return changed;
    }

    internal bool CompleteTask(string id)
    {
        var changed = _tasks.Complete(id, TimeZoneInfo.Local).Changed;
        _log?.Write("task", ("change", changed ? "completed" : "unchanged"));
        WithdrawReminder(id);
        AfterTaskChange();
        return changed;
    }

    internal bool DeleteTask(string id)
    {
        var changed = _tasks.Delete(id).Changed;
        _log?.Write("task", ("change", changed ? "deleted" : "unchanged"));
        WithdrawReminder(id);
        AfterTaskChange();
        return changed;
    }

    private void OpenTasksWindow()
    {
        _tasksWindow = TasksWindow.Open(this, _tasksWindow);
        _log?.Write("tasks.window", ("state", "open"));
    }

    private void InitReminders()
    {
        _taskStore = new TaskStore(_store.Directory);
        DataLoadStatus status;
        try
        {
            status = _taskStore.Load(_tasks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status = DataLoadStatus.Unreadable;
        }

        _log?.Write("tasks", ("status", status.ToString().ToLowerInvariant()), ("count", _tasks.Items.Count), ("pending", _tasks.PendingCount));
        if (status is DataLoadStatus.RecoveredFromBackup or DataLoadStatus.Unreadable)
        {
            _fileLog?.Write(LogLevel.Warning, "tasks.recovered", ("status", status.ToString().ToLowerInvariant()));
        }

        RegisterReminderProvider();
        CheckReminders("startup"); // reminders missed while Halo was closed are shown now, once each
    }

    /// <summary>The reminder provider's policy follows the settings: previews, over full screen (ADR-001 §6.2 default).</summary>
    private void RegisterReminderProvider() =>
        _notifications.Register(
            RemindersProvider,
            ProviderPolicy.Default with
            {
                Enabled = _settings.ShowReminders,
                ShowPreview = _settings.ShowPreviews,
                AllowedOverFullscreen = true,
                AlertDuration = TimeSpan.FromSeconds(8),
                Lifetime = TimeSpan.FromMinutes(30),
            },
            OnReminderAction);

    /// <summary>Shows every due reminder (deduplicated by the center) and arms the timer for the next one.</summary>
    private void CheckReminders(string reason)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _tasks.DueNow(now))
        {
            var outcome = _notifications.Submit(new HaloNotification(RemindersProvider, SourceId(item), AlertPriority.High)
            {
                Title = item.Text,
                Redacted = item.SnoozedUntilUtc is null ? "Reminder" : "Snoozed reminder",
                Glyph = "",
                OccurredAt = item.AlertAt ?? now,
                Actions = [SnoozeReminder, CompleteReminder],
            });
            if (outcome.Result is SubmitResult.Shown or SubmitResult.Summarised)
            {
                _log?.Write("reminder", ("state", outcome.Result == SubmitResult.Shown ? "shown" : "summarised"), ("overdue_s", (int)(now - (item.AlertAt ?? now)).TotalSeconds), ("reason", reason));
            }

            if (outcome.Activity is { } activity)
            {
                _ = _board.Upsert(activity);
            }

            if (outcome.Alert is { } alert)
            {
                RaiseAlert(alert, "reminder");
            }
        }

        if (_tasks.NextAlert(now) is { } next)
        {
            var ms = Math.Clamp(Math.Ceiling((next - now).TotalMilliseconds) + 5, 10, int.MaxValue);
            _ = SetTimer(_hwnd, ReminderTimerId, (uint)ms, 0);
        }
        else
        {
            _ = KillTimer(_hwnd, ReminderTimerId);
        }

        if (_stateStore is not null)
        {
            SyncActivities(reason);
        }
    }

    /// <summary>The center's deduplication key: a snoozed reminder at a new time is a new notification.</summary>
    private static string SourceId(TaskItem item) => $"{item.Id}@{(item.AlertAt ?? item.CreatedUtc).ToUnixTimeSeconds()}";

    private static string TaskIdOf(string sourceId) => sourceId.Split('@')[0];

    /// <summary>Provider-side actions: snooze and done act on the local list; their result is what really happened.</summary>
    private ActionOutcome OnReminderAction(string sourceId, string action)
    {
        var id = TaskIdOf(sourceId);
        var done = action switch
        {
            "snooze" => _tasks.Snooze(id, SnoozeFor).Changed,
            "done" => _tasks.Complete(id, TimeZoneInfo.Local).Changed,
            _ => false,
        };
        return done ? ActionOutcome.Done : _tasks.Find(id) is null ? ActionOutcome.Gone : ActionOutcome.Rejected;
    }

    /// <summary>An action on a notification activity (island button, UIA). Returns false when the activity is not a notification.</summary>
    private bool RunNotificationAction(string activityId, string action, string source)
    {
        if (!activityId.StartsWith(NotificationCenter.ActivityPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var outcome = _notifications.RunAction(activityId, action);
        var provider = activityId[NotificationCenter.ActivityPrefix.Length..].Split('/')[0];
        if (provider == RemindersProvider && action == NotificationCenter.DismissAction && outcome == ActionOutcome.Done)
        {
            var sourceId = activityId[(NotificationCenter.ActivityPrefix.Length + provider.Length + 1)..];
            _ = _tasks.Dismiss(TaskIdOf(sourceId), TimeZoneInfo.Local);
        }

        _log?.Write("activity.action", ("activity", "notification"), ("action", action), ("result", outcome.ToString().ToLowerInvariant()), ("source", source));
        if (outcome is ActionOutcome.Done or ActionOutcome.Gone)
        {
            _ = _board.Remove(activityId);
        }

        if (outcome == ActionOutcome.Rejected)
        {
            // The provider refused: say so instead of pretending it worked.
            RaiseAlert(new IslandAlert("note.failed", AlertKind.Notification, AlertPriority.Normal, new AlertContent("", "That didn't work"), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5)), source);
        }

        AfterTaskChange();
        return true;
    }

    /// <summary>Removes the reminder of an edited, completed or deleted task from the island.</summary>
    private void WithdrawReminder(string taskId)
    {
        foreach (var activity in _board.All.Where(a => a.Id.StartsWith($"{NotificationCenter.ActivityPrefix}{RemindersProvider}/{taskId}@", StringComparison.Ordinal)).ToList())
        {
            var sourceId = activity.Id[(NotificationCenter.ActivityPrefix.Length + RemindersProvider.Length + 1)..];
            _ = _notifications.Dismiss(RemindersProvider, sourceId);
            _ = _board.Remove(activity.Id);
        }
    }

    private void AfterTaskChange()
    {
        SaveTasks();
        _tasksWindow?.Refresh();
        CheckReminders("change");
    }

    private void SaveTasks()
    {
        try
        {
            _ = _taskStore?.Save(_tasks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _fileLog?.Write(LogLevel.Warning, "tasks.save_failed", ("type", ex.GetType().Name), ("hresult", ex.HResult));
        }
    }

    /// <summary>Notification activities the board expired are forgotten by the center too.</summary>
    private void ForgetExpiredNotifications() => _notifications.Expire(_notifications.LiveActivityIds.Where(id => _board.Find(id) is null).ToList());

    private nint? HandleReminderMessage(uint msg, nint wParam)
    {
        switch (msg)
        {
            case WmTimer when (nuint)wParam == ReminderTimerId:
                _ = KillTimer(_hwnd, ReminderTimerId);
                CheckReminders("due");
                return 0;
            case WmTestTask when _log is not null:
                // Diagnostics only: a task due in wParam seconds, with fixed text.
                _ = AddTask("Test reminder", DateTimeOffset.UtcNow.AddSeconds(Math.Clamp((int)wParam, 0, 86_400)), Recurrence.None);
                return 0;
            default:
                return null;
        }
    }

    /// <summary>Diagnostics-only: add a task due in wParam seconds.</summary>
    private const uint WmTestTask = WmApp + 56;
}
