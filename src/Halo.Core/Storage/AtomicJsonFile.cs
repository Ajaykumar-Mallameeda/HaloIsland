using System.Globalization;
using System.Text;

namespace Halo.Core.Storage;

/// <summary>How a data file was read.</summary>
public enum DataLoadStatus
{
    Missing,
    Loaded,
    RecoveredFromBackup,
    Unreadable,
}

/// <summary>
/// One small local data file with the ADR-001 §8 rules, shared by every store: size cap, write to <c>*.tmp</c>, flush,
/// <c>File.Replace</c> with a <c>*.bak</c>; an unreadable file is set aside as <c>*.corrupt-&lt;time&gt;</c> and the backup
/// is tried. Unchanged content is not rewritten. Parsing is the caller's (strict, no polymorphic deserialisation).
/// </summary>
public sealed class AtomicJsonFile(string path, int maxBytes = 256 * 1024)
{
    private string? _lastWritten;

    public string Path { get; } = path;

    public string BackupPath => Path + ".bak";

    /// <summary>Reads and parses the file, falling back to the backup. <paramref name="parse"/> returns false for invalid content.</summary>
    public DataLoadStatus Load<T>(Func<string, (bool Ok, T Value)> parse, out T? value)
    {
        ArgumentNullException.ThrowIfNull(parse);
        value = default;
        if (!File.Exists(Path))
        {
            return DataLoadStatus.Missing;
        }

        if (TryRead(Path, parse, out value, out var text))
        {
            _lastWritten = text;
            return DataLoadStatus.Loaded;
        }

        SetAside();
        if (File.Exists(BackupPath) && TryRead(BackupPath, parse, out value, out _))
        {
            return DataLoadStatus.RecoveredFromBackup;
        }

        value = default;
        return DataLoadStatus.Unreadable;
    }

    /// <summary>Writes atomically. Returns false when the content equals what was last read or written.</summary>
    public bool Save(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.Equals(json, _lastWritten, StringComparison.Ordinal))
        {
            return false;
        }

        if (Encoding.UTF8.GetByteCount(json) > maxBytes)
        {
            throw new InvalidOperationException("The data is larger than the file's size limit.");
        }

        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var tmp = Path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(json));
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(Path))
        {
            File.Replace(tmp, Path, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, Path);
        }

        _lastWritten = json;
        return true;
    }

    /// <summary>Deletes the file and its backup (the user cleared the data).</summary>
    public void Delete()
    {
        File.Delete(Path);
        File.Delete(BackupPath);
        _lastWritten = null;
    }

    private bool TryRead<T>(string file, Func<string, (bool Ok, T Value)> parse, out T? value, out string text)
    {
        value = default;
        text = string.Empty;
        try
        {
            if (new FileInfo(file).Length > maxBytes)
            {
                return false;
            }

            text = File.ReadAllText(file, Encoding.UTF8);
            var (ok, parsed) = parse(text);
            value = parsed;
            return ok;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void SetAside()
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(Path, $"{Path}.corrupt-{stamp}", overwrite: true);
        }
        catch (IOException)
        {
            // Leave it in place; the next save replaces it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
