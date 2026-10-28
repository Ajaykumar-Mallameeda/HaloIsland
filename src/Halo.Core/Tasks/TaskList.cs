using System.Globalization;
using System.Text;
using System.Text.Json;
using Halo.Core.Storage;

namespace Halo.Core.Tasks;

/// <summary>How a reminder repeats. Recurring reminders keep their local time of day across DST and zone changes.</summary>
public enum Recurrence
{
    None,
    Daily,
    Weekly,
}

/// <summary>
/// One to-do item (F-F05) or reminder (F-F04). A reminder is an item with a due time; a plain task may have none.
/// Text is the user's own and is never logged. Times are UTC instants; <see cref="Recurrence"/> items repeat at the
/// same local time of day.
/// </summary>
public sealed record TaskItem(string Id, string Text, DateTimeOffset CreatedUtc)
{
    public const int MaxTextLength = 200;

    public DateTimeOffset? DueUtc { get; init; }

    public Recurrence Recurrence { get; init; }

    public bool Done { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    /// <summary>Set by snooze: the reminder comes back at this time instead of the due time.</summary>
    public DateTimeOffset? SnoozedUntilUtc { get; init; }

    /// <summary>The reminder for the current due time was dismissed (it does not come back until the next occurrence).</summary>
    public bool ReminderDismissed { get; init; }

    /// <summary>When the reminder should show, or null (no due time, done, or dismissed).</summary>
    public DateTimeOffset? AlertAt => Done || ReminderDismissed || DueUtc is null ? null : SnoozedUntilUtc ?? DueUtc;
}

/// <summary>What changed in the list (for diagnostics; never the text).</summary>
public readonly record struct TaskChange(bool Changed, string? Id);

/// <summary>
/// The local to-do list and reminder engine (F-F04/F-F05). No cloud, no account. Rules:
/// <list type="bullet">
/// <item>Text is trimmed, control characters removed, at most 200 characters; empty text is rejected.</item>
/// <item>A reminder is due when its alert time has passed; reminders missed while Halo was closed are due at the next
/// start ("overdue"), once each.</item>
/// <item>Snooze moves only the alert time; dismiss stops the alert; completing a recurring item moves it to its next
/// occurrence in local time.</item>
/// <item>At most 500 items; completed items older than 30 days are removed on load.</item>
/// </list>
/// </summary>
public sealed class TaskList(TimeProvider? time = null)
{
    public const int MaxItems = 500;
    public static readonly TimeSpan KeepCompleted = TimeSpan.FromDays(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<TaskItem> _items = [];

    public IReadOnlyList<TaskItem> Items => _items;

    /// <summary>Open tasks, earliest due first, undated last.</summary>
    public IReadOnlyList<TaskItem> Pending => _items.Where(i => !i.Done).OrderBy(i => i.DueUtc ?? DateTimeOffset.MaxValue).ThenBy(i => i.CreatedUtc).ToList();

    public int PendingCount => _items.Count(i => !i.Done);

    /// <summary>The open item due next (or overdue), or null.</summary>
    public TaskItem? NextDue => _items.Where(i => !i.Done && i.DueUtc is not null).OrderBy(i => i.DueUtc).FirstOrDefault();

    /// <summary>The earliest future alert time, for one timer.</summary>
    public DateTimeOffset? NextAlert(DateTimeOffset now) => _items.Select(i => i.AlertAt).Where(t => t > now).Min();

    public TaskItem? Find(string id) => _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    /// <summary>Items whose alert time has passed and that are not done or dismissed.</summary>
    public IReadOnlyList<TaskItem> DueNow(DateTimeOffset now) => _items.Where(i => i.AlertAt is { } at && at <= now).OrderBy(i => i.AlertAt).ToList();

    public static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Control and format characters become spaces, runs of whitespace collapse, then the ends are trimmed.
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var ch = char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format ? ' ' : c;
            if (!char.IsWhiteSpace(ch) || (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])))
            {
                _ = sb.Append(char.IsWhiteSpace(ch) ? ' ' : ch);
            }
        }

        var s = sb.ToString().Trim();
        return s.Length > TaskItem.MaxTextLength ? s[..TaskItem.MaxTextLength] : s;
    }

    /// <summary>Adds an item. Returns null when the text is empty or the list is full.</summary>
    public TaskItem? Add(string text, DateTimeOffset? dueUtc = null, Recurrence recurrence = Recurrence.None)
    {
        var clean = Clean(text);
        if (clean.Length == 0 || _items.Count >= MaxItems)
        {
            return null;
        }

        var item = new TaskItem(Guid.NewGuid().ToString("N"), clean, _time.GetUtcNow()) { DueUtc = dueUtc, Recurrence = dueUtc is null ? Recurrence.None : recurrence };
        _items.Add(item);
        return item;
    }

    public TaskChange Edit(string id, string text, DateTimeOffset? dueUtc, Recurrence recurrence = Recurrence.None)
    {
        var clean = Clean(text);
        var i = Index(id);
        if (i < 0 || clean.Length == 0)
        {
            return default;
        }

        var before = _items[i];
        var after = before with
        {
            Text = clean,
            DueUtc = dueUtc,
            Recurrence = dueUtc is null ? Recurrence.None : recurrence,
            SnoozedUntilUtc = dueUtc == before.DueUtc ? before.SnoozedUntilUtc : null,
            ReminderDismissed = dueUtc == before.DueUtc && before.ReminderDismissed,
        };
        return Replace(i, after);
    }

    /// <summary>Completes an item. A recurring item moves to its next occurrence instead (and stays open).</summary>
    public TaskChange Complete(string id, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var i = Index(id);
        if (i < 0 || _items[i].Done)
        {
            return default;
        }

        var item = _items[i];
        var now = _time.GetUtcNow();
        if (item.Recurrence != Recurrence.None && item.DueUtc is { } due)
        {
            return Replace(i, item with { DueUtc = NextOccurrence(due, item.Recurrence, now, zone), SnoozedUntilUtc = null, ReminderDismissed = false });
        }

        return Replace(i, item with { Done = true, CompletedUtc = now, SnoozedUntilUtc = null });
    }

    public TaskChange Reopen(string id)
    {
        var i = Index(id);
        return i < 0 || !_items[i].Done ? default : Replace(i, _items[i] with { Done = false, CompletedUtc = null, ReminderDismissed = true });
    }

    public TaskChange Delete(string id)
    {
        var i = Index(id);
        if (i < 0)
        {
            return default;
        }

        _items.RemoveAt(i);
        return new TaskChange(true, id);
    }

    /// <summary>Snoozes a due reminder. Returns false for an unknown, done or undated item.</summary>
    public TaskChange Snooze(string id, TimeSpan by)
    {
        var i = Index(id);
        if (i < 0 || _items[i].Done || _items[i].DueUtc is null || by <= TimeSpan.Zero)
        {
            return default;
        }

        return Replace(i, _items[i] with { SnoozedUntilUtc = _time.GetUtcNow() + by, ReminderDismissed = false });
    }

    /// <summary>Stops the reminder for the current occurrence; a recurring item comes back at its next occurrence.</summary>
    public TaskChange Dismiss(string id, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var i = Index(id);
        if (i < 0 || _items[i].Done)
        {
            return default;
        }

        var item = _items[i];
        if (item.Recurrence != Recurrence.None && item.DueUtc is { } due)
        {
            return Replace(i, item with { DueUtc = NextOccurrence(due, item.Recurrence, _time.GetUtcNow(), zone), SnoozedUntilUtc = null, ReminderDismissed = false });
        }

        return Replace(i, item with { ReminderDismissed = true, SnoozedUntilUtc = null });
    }

    public void Clear() => _items.Clear();

    /// <summary>
    /// The first occurrence after <paramref name="nowUtc"/> at the same local time of day as <paramref name="dueUtc"/>
    /// (daily or weekly). Computed on local dates, so a 9:00 reminder stays at 9:00 across a DST change.
    /// </summary>
    public static DateTimeOffset NextOccurrence(DateTimeOffset dueUtc, Recurrence recurrence, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var step = recurrence == Recurrence.Weekly ? 7 : 1;
        var local = TimeZoneInfo.ConvertTime(dueUtc, zone).DateTime;
        var timeOfDay = local.TimeOfDay;
        var date = local.Date;
        for (var guard = 0; guard < 5000; guard++)
        {
            date = date.AddDays(step);
            var candidate = date + timeOfDay;
            while (zone.IsInvalidTime(candidate))
            {
                candidate = candidate.AddMinutes(15); // a time skipped by DST moves to just after the gap
            }

            var utc = new DateTimeOffset(candidate, zone.GetUtcOffset(candidate)).ToUniversalTime();
            if (utc > nowUtc)
            {
                return utc;
            }
        }

        return nowUtc.AddDays(step);
    }

    // --- persistence (tasks.json) ---

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", 1);
            w.WriteStartArray("items");
            foreach (var i in _items)
            {
                w.WriteStartObject();
                w.WriteString("id", i.Id);
                w.WriteString("text", i.Text);
                w.WriteString("createdUtc", Iso(i.CreatedUtc));
                if (i.DueUtc is { } due)
                {
                    w.WriteString("dueUtc", Iso(due));
                }

                if (i.Recurrence != Recurrence.None)
                {
                    w.WriteString("recurrence", i.Recurrence.ToString());
                }

                if (i.Done)
                {
                    w.WriteBoolean("done", true);
                    w.WriteString("completedUtc", Iso(i.CompletedUtc ?? i.CreatedUtc));
                }

                if (i.SnoozedUntilUtc is { } snooze)
                {
                    w.WriteString("snoozedUntilUtc", Iso(snooze));
                }

                if (i.ReminderDismissed)
                {
                    w.WriteBoolean("reminderDismissed", true);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Replaces the list with the items in <paramref name="json"/>. Invalid items are skipped; old completed ones dropped.</summary>
    public bool Load(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var now = _time.GetUtcNow();
        _items.Clear();
        foreach (var e in items.EnumerateArray())
        {
            if (_items.Count >= MaxItems || e.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = Str(e, "id");
            var text = Clean(Str(e, "text"));
            if (id is not { Length: > 0 and <= 64 } || text.Length == 0 || Time(e, "createdUtc") is not { } created || Find(id) is not null)
            {
                continue;
            }

            var item = new TaskItem(id, text, created)
            {
                DueUtc = Time(e, "dueUtc"),
                Done = e.TryGetProperty("done", out var d) && d.ValueKind == JsonValueKind.True,
                CompletedUtc = Time(e, "completedUtc"),
                SnoozedUntilUtc = Time(e, "snoozedUntilUtc"),
                ReminderDismissed = e.TryGetProperty("reminderDismissed", out var r) && r.ValueKind == JsonValueKind.True,
                Recurrence = Enum.TryParse<Recurrence>(Str(e, "recurrence"), out var rec) ? rec : Recurrence.None,
            };
            if (item.Done && item.CompletedUtc is { } completed && now - completed > KeepCompleted)
            {
                continue;
            }

            _items.Add(item.DueUtc is null ? item with { Recurrence = Recurrence.None } : item);
        }

        return true;
    }

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    private int Index(string id) => _items.FindIndex(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    private TaskChange Replace(int index, TaskItem item)
    {
        if (_items[index] == item)
        {
            return default;
        }

        _items[index] = item;
        return new TaskChange(true, item.Id);
    }
}

/// <summary>Loads and saves the task list as <c>tasks.json</c>.</summary>
public sealed class TaskStore(string directory)
{
    public const string FileName = "tasks.json";

    private readonly AtomicJsonFile _file = new(Path.Combine(directory, FileName), 512 * 1024);

    public string FilePath => _file.Path;

    public DataLoadStatus Load(TaskList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return _file.Load(json => (list.Load(json), true), out _);
    }

    public bool Save(TaskList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return _file.Save(list.ToJson());
    }

    /// <summary>Removes the file and its backup ("delete all my tasks").</summary>
    public void Delete() => _file.Delete();
}
