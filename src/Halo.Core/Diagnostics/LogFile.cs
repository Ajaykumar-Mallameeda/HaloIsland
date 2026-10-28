using System.Globalization;
using System.Text;

namespace Halo.Core.Diagnostics;

/// <summary>Log levels. Information is the default; there is no debug level that could carry personal content.</summary>
public enum LogLevel
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// One log field value: a number, a boolean or a short machine token. There is deliberately no way to log free
/// text, so titles, artists, task text, notification text, file paths and window titles cannot reach the log
/// (ADR-001 §9.2, SECURITY_AND_PRIVACY_DESIGN §6).
/// </summary>
public readonly record struct LogValue
{
    private LogValue(string text, bool quoted) => (Text, Quoted) = (text, quoted);

    public string Text { get; }

    public bool Quoted { get; }

    public static implicit operator LogValue(long value) => new(value.ToString(CultureInfo.InvariantCulture), false);

    public static implicit operator LogValue(int value) => new(value.ToString(CultureInfo.InvariantCulture), false);

    public static implicit operator LogValue(double value) =>
        new(double.IsFinite(value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : "0", false);

    public static implicit operator LogValue(bool value) => new(value ? "true" : "false", false);

    /// <summary>A token such as "recovered_defaults" or "InvalidOperationException"; anything else becomes "redacted".</summary>
    public static implicit operator LogValue(string token) => new(LogFile.IsToken(token) ? token : LogFile.Redacted, true);

    public static LogValue FromInt64(long value) => value;

    public static LogValue FromInt32(int value) => value;

    public static LogValue FromDouble(double value) => value;

    public static LogValue FromBoolean(bool value) => value;

    public static LogValue FromString(string token) => token;
}

/// <summary>
/// Local rolling diagnostic log (FR-APP-07): JSON lines in <c>logs/halo.log</c>, rolled at
/// <see cref="MaxFileBytes"/> into <c>halo.1.log</c> … <c>halo.4.log</c> (5 files at most). Each event name is
/// limited to <see cref="MaxPerMinute"/> lines a minute; the overflow is counted in one "log.suppressed" line.
/// Write failures (disk full, no access) are counted and dropped: logging never takes the island down.
/// Nothing is uploaded. Single-threaded use from the UI thread; <see cref="Write"/> is also safe from a crash handler.
/// </summary>
public sealed class LogFile
{
    public const long MaxFileBytes = 1024 * 1024;
    public const int MaxFiles = 5;
    public const int MaxPerMinute = 20;
    public const string Redacted = "redacted";

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, (DateTimeOffset Window, int Count, int Dropped)> _rates = new(StringComparer.Ordinal);

    public LogFile(string directory, TimeProvider? time = null)
    {
        Directory = directory;
        _time = time ?? TimeProvider.System;
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, "halo.log");

    /// <summary>Lines that could not be written (disk full, access denied).</summary>
    public int FailedWrites { get; private set; }

    /// <summary>True for strings safe to log: 1–64 characters of letters, digits, '_', '.', '-'.</summary>
    public static bool IsToken(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    public void Write(LogLevel level, string eventName, params ReadOnlySpan<(string Key, LogValue Value)> fields)
    {
        var name = IsToken(eventName) ? eventName : Redacted;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            if (!Admit(name, now, out var resumedAfter))
            {
                return;
            }

            var line = new StringBuilder(128);
            if (resumedAfter > 0)
            {
                Append(line, now, LogLevel.Warning, "log.suppressed", [("event", name), ("dropped", resumedAfter)]);
            }

            Append(line, now, level, name, fields);
            WriteRaw(line.ToString());
        }
    }

    private static void Append(StringBuilder line, DateTimeOffset now, LogLevel level, string name, ReadOnlySpan<(string Key, LogValue Value)> fields)
    {
        line.Append(CultureInfo.InvariantCulture, $"{{\"t\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"level\":\"{LevelToken(level)}\",\"event\":\"{name}\"");
        foreach (var (key, value) in fields)
        {
            var k = IsToken(key) ? key : Redacted;
            if (value.Quoted)
            {
                line.Append(CultureInfo.InvariantCulture, $",\"{k}\":\"{value.Text}\"");
            }
            else
            {
                line.Append(CultureInfo.InvariantCulture, $",\"{k}\":{value.Text}");
            }
        }

        line.Append("}\n");
    }

    private static string LevelToken(LogLevel level) => level switch
    {
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        _ => "info",
    };

    /// <summary>Per-event rate limit in one-minute windows; reports how many were dropped in the previous window.</summary>
    private bool Admit(string name, DateTimeOffset now, out int resumedAfter)
    {
        resumedAfter = 0;
        if (!_rates.TryGetValue(name, out var r) || now - r.Window >= TimeSpan.FromMinutes(1))
        {
            resumedAfter = r.Dropped;
            _rates[name] = (now, 1, 0);
            return true;
        }

        if (r.Count >= MaxPerMinute)
        {
            _rates[name] = (r.Window, r.Count, r.Dropped + 1);
            return false;
        }

        _rates[name] = (r.Window, r.Count + 1, r.Dropped);
        return true;
    }

    private void WriteRaw(string text)
    {
        try
        {
            _ = System.IO.Directory.CreateDirectory(Directory);
            var bytes = Encoding.UTF8.GetBytes(text);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length + bytes.Length > MaxFileBytes)
            {
                Roll();
            }

            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FailedWrites++;
        }
    }

    private void Roll()
    {
        var oldest = Path.Combine(Directory, $"halo.{MaxFiles - 1}.log");
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = MaxFiles - 2; i >= 1; i--)
        {
            var from = Path.Combine(Directory, $"halo.{i}.log");
            if (File.Exists(from))
            {
                File.Move(from, Path.Combine(Directory, $"halo.{i + 1}.log"));
            }
        }

        File.Move(FilePath, Path.Combine(Directory, "halo.1.log"));
    }
}
