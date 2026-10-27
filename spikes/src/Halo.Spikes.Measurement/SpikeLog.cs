using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Halo.Spikes.Measurement;

/// <summary>
/// One structured log field. Values are numbers or short tokens (identifier-like text), never free text,
/// so a log line cannot carry personal content such as titles, paths or window names
/// (SECURITY_AND_PRIVACY_DESIGN §6).
/// </summary>
public readonly partial record struct LogField
{
    private LogField(string key, double? number, string? token)
    {
        Key = key;
        Number = number;
        Token = token;
    }

    public string Key { get; }

    public double? Number { get; }

    public string? Token { get; }

    public static LogField Num(string key, double value)
    {
        ValidateKey(key);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Logged numbers must be finite.");
        }

        return new LogField(key, value, null);
    }

    public static LogField Tok(string key, string token)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(token);
        if (!TokenPattern().IsMatch(token))
        {
            throw new ArgumentException("Tokens are limited to 1-64 characters from [A-Za-z0-9_.:+-].", nameof(token));
        }

        return new LogField(key, null, token);
    }

    internal static void ValidateKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!NamePattern().IsMatch(key) || key is "t" or "event")
        {
            throw new ArgumentException($"Invalid or reserved log key '{key}'.", nameof(key));
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    internal static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z0-9_.:+-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

/// <summary>Writes one JSON object per line: <c>{"t":"...","event":"name",...fields}</c>. Thread-safe.</summary>
public sealed class SpikeLog
{
    private readonly TextWriter _sink;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();

    public SpikeLog(TextWriter sink, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(time);
        _sink = sink;
        _time = time;
    }

    public void Write(string eventName, params ReadOnlySpan<LogField> fields)
    {
        ValidateEventName(eventName);
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("t", _time.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("event", eventName);
            foreach (var field in fields)
            {
                if (field.Key is null)
                {
                    throw new ArgumentException("Uninitialised log field.", nameof(fields));
                }

                if (field.Number is { } number)
                {
                    json.WriteNumber(field.Key, number);
                }
                else
                {
                    json.WriteString(field.Key, field.Token);
                }
            }

            json.WriteEndObject();
        }

        var line = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        lock (_gate)
        {
            _sink.WriteLine(line);
            _sink.Flush();
        }
    }

    /// <summary>
    /// Logs an exception by type name and HRESULT only. The message and stack trace are deliberately
    /// omitted because they can contain paths or other content.
    /// </summary>
    public void WriteError(string eventName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Write(
            eventName,
            LogField.Tok("error_type", SanitizeTypeName(exception.GetType().FullName ?? "unknown")),
            LogField.Num("hresult", exception.HResult));
    }

    private static void ValidateEventName(string eventName)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        if (!LogField.NamePattern().IsMatch(eventName))
        {
            throw new ArgumentException($"Invalid event name '{eventName}'.", nameof(eventName));
        }
    }

    private static string SanitizeTypeName(string name)
    {
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '+' ? c : '_').ToArray();
        var cleaned = new string(chars);
        return cleaned.Length <= 64 ? cleaned : cleaned[^64..];
    }
}
