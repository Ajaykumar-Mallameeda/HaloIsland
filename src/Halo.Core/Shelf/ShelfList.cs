using System.Globalization;
using System.Text;
using System.Text.Json;
using Halo.Core.Storage;

namespace Halo.Core.Shelf;

/// <summary>What a shelf item holds (H-F02).</summary>
public enum ShelfKind
{
    Text,

    /// <summary>A reference to a file or folder: the path only. The file itself is never copied, moved or deleted.</summary>
    File,
}

/// <summary>One pinned item. <see cref="Value"/> is the text or the full path; it is never logged.</summary>
public sealed record ShelfItem(string Id, ShelfKind Kind, string Value, DateTimeOffset AddedUtc)
{
    /// <summary>One line for a list: the file name with its folder, or the first line of the text.</summary>
    public string Label
    {
        get
        {
            if (Kind == ShelfKind.File)
            {
                var name = Path.GetFileName(Value.TrimEnd('\\', '/'));
                return name.Length > 0 ? $"{name}  —  {Path.GetDirectoryName(Value)}" : Value;
            }

            var first = Value.Split('\n', 2)[0].TrimEnd('\r');
            return first.Length > 80 ? first[..80] + "…" : first;
        }
    }
}

/// <summary>
/// The user-controlled shelf (H-F02): text snippets and file references the user explicitly pins. Nothing is captured
/// automatically, nothing leaves the machine, and diagnostics never contain the content. Rules:
/// <list type="bullet">
/// <item>At most <see cref="MaxItems"/> items (the oldest is dropped first); text up to 10 000 characters.</item>
/// <item>The same text or path pinned again moves to the top instead of being duplicated.</item>
/// <item>File items store the path only; a file that was moved or deleted is reported missing, never recreated.</item>
/// </list>
/// </summary>
public sealed class ShelfList(TimeProvider? time = null)
{
    public const int MaxItems = 30;
    public const int MaxTextLength = 10_000;
    public const int MaxPathLength = 32_767;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<ShelfItem> _items = [];

    /// <summary>Newest first.</summary>
    public IReadOnlyList<ShelfItem> Items => _items;

    public ShelfItem? Find(string id) => _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    /// <summary>Pins text. Returns null for empty text; long text is cut at 10 000 characters.</summary>
    public ShelfItem? AddText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = new string(text.Where(c => c is '\n' or '\r' or '\t' || !char.IsControl(c)).ToArray());
        if (value.Length > MaxTextLength)
        {
            value = value[..MaxTextLength];
        }

        return Add(ShelfKind.Text, value);
    }

    /// <summary>Pins file references (absolute paths only). Returns the items added.</summary>
    public IReadOnlyList<ShelfItem> AddFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var added = new List<ShelfItem>();
        foreach (var path in paths.Where(p => p.Length is > 0 and <= MaxPathLength && Path.IsPathFullyQualified(p) && p.IndexOfAny(Path.GetInvalidPathChars()) < 0))
        {
            added.Add(Add(ShelfKind.File, path));
        }

        return added;
    }

    public bool Remove(string id) => _items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.Ordinal)) > 0;

    public void Clear() => _items.Clear();

    /// <summary>True when a file item's file or folder no longer exists (moved, deleted, or the drive is gone).</summary>
    public static bool IsMissing(ShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind == ShelfKind.File && !File.Exists(item.Value) && !Directory.Exists(item.Value);
    }

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
                w.WriteString("kind", i.Kind == ShelfKind.File ? "file" : "text");
                w.WriteString("value", i.Value);
                w.WriteString("addedUtc", i.AddedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public bool Load(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        _items.Clear();
        foreach (var e in items.EnumerateArray())
        {
            if (_items.Count >= MaxItems || e.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var kind = S("kind") switch { "file" => ShelfKind.File, "text" => ShelfKind.Text, _ => (ShelfKind?)null };
            var value = S("value");
            if (S("id") is not { Length: > 0 and <= 64 } id || kind is null || string.IsNullOrEmpty(value) || value.Length > (kind == ShelfKind.File ? MaxPathLength : MaxTextLength)
                || !DateTimeOffset.TryParse(S("addedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var added)
                || (kind == ShelfKind.File && !Path.IsPathFullyQualified(value)))
            {
                continue;
            }

            _items.Add(new ShelfItem(id, kind.Value, value, added));
        }

        return true;
    }

    private ShelfItem Add(ShelfKind kind, string value)
    {
        _ = _items.RemoveAll(i => i.Kind == kind && string.Equals(i.Value, value, kind == ShelfKind.File ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        var item = new ShelfItem(Guid.NewGuid().ToString("N"), kind, value, _time.GetUtcNow());
        _items.Insert(0, item);
        if (_items.Count > MaxItems)
        {
            _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        }

        return item;
    }
}

/// <summary>Loads and saves the shelf as <c>shelf.json</c>.</summary>
public sealed class ShelfStore(string directory)
{
    public const string FileName = "shelf.json";
    private readonly AtomicJsonFile _file = new(Path.Combine(directory, FileName), 2 * 1024 * 1024);

    public string FilePath => _file.Path;

    public DataLoadStatus Load(ShelfList shelf)
    {
        ArgumentNullException.ThrowIfNull(shelf);
        return _file.Load(json => (shelf.Load(json), true), out _);
    }

    public bool Save(ShelfList shelf)
    {
        ArgumentNullException.ThrowIfNull(shelf);
        return _file.Save(shelf.ToJson());
    }

    public void Delete() => _file.Delete();
}
