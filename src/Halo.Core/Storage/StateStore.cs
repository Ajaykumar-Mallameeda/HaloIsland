using System.Globalization;
using System.Text;
using System.Text.Json;
using Halo.Core.Timers;

namespace Halo.Core.Storage;

/// <summary>
/// Runtime state that must survive a restart (FR-TIM-02): the countdown, the focus session and the quiet-mode schedule.
/// No user text is stored here.
/// </summary>
public sealed record HaloState
{
    public const int CurrentSchemaVersion = 1;

    public static HaloState Empty { get; } = new();

    public CountdownRecord? Timer { get; init; }

    public FocusRecord? Focus { get; init; }

    public QuietMode Quiet { get; init; } = QuietMode.Off;

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schemaVersion", CurrentSchemaVersion);
            if (Timer is { } t)
            {
                w.WriteStartObject("timer");
                WriteCountdown(w, t);
                w.WriteEndObject();
            }

            if (Focus is { } f)
            {
                w.WriteStartObject("focus");
                w.WriteString("phase", f.Phase.ToString());
                w.WriteNumber("completedWork", f.CompletedWork);
                w.WriteNumber("workMinutes", f.Plan.Work.TotalMinutes);
                w.WriteNumber("shortBreakMinutes", f.Plan.ShortBreak.TotalMinutes);
                w.WriteNumber("longBreakMinutes", f.Plan.LongBreak.TotalMinutes);
                w.WriteNumber("cycleLength", f.Plan.CycleLength);
                w.WriteStartObject("countdown");
                WriteCountdown(w, f.Countdown);
                w.WriteEndObject();
                w.WriteEndObject();
            }

            if (Quiet.Kind != QuietKind.Off)
            {
                w.WriteStartObject("quiet");
                w.WriteString("kind", Quiet.Kind.ToString());
                if (Quiet.UntilUtc is { } until)
                {
                    w.WriteString("untilUtc", until.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                }

                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Strict parse: unknown or invalid parts are dropped individually; a malformed document fails.</summary>
    public static (bool Ok, HaloState Value) Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (false, Empty);
        }

        var state = Empty;
        if (root.TryGetProperty("timer", out var timer) && ReadCountdown(timer) is { } countdown)
        {
            state = state with { Timer = countdown };
        }

        if (root.TryGetProperty("focus", out var focus) && focus.ValueKind == JsonValueKind.Object
            && Enum.TryParse<FocusPhase>(Text(focus, "phase"), out var phase)
            && focus.TryGetProperty("countdown", out var fc) && ReadCountdown(fc) is { } focusCountdown
            && FocusPlan.TryCreate(Number(focus, "workMinutes"), Number(focus, "shortBreakMinutes"), Number(focus, "longBreakMinutes"), (int)Number(focus, "cycleLength")) is { } plan)
        {
            state = state with { Focus = new FocusRecord(phase, (int)Math.Clamp(Number(focus, "completedWork"), 0, 1000), plan, focusCountdown) };
        }

        if (root.TryGetProperty("quiet", out var quiet) && quiet.ValueKind == JsonValueKind.Object
            && Enum.TryParse<QuietKind>(Text(quiet, "kind"), out var kind) && kind != QuietKind.Off)
        {
            DateTimeOffset? until = DateTimeOffset.TryParse(Text(quiet, "untilUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var u) ? u : null;
            if (kind == QuietKind.UntilTurnedOff || until is not null)
            {
                state = state with { Quiet = new QuietMode(kind, until) };
            }
        }

        return (true, state);
    }

    private static void WriteCountdown(Utf8JsonWriter w, CountdownRecord t)
    {
        w.WriteString("state", t.State.ToString());
        w.WriteNumber("durationMs", (long)t.Duration.TotalMilliseconds);
        if (t.EndUtc is { } end)
        {
            w.WriteString("endUtc", end.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        if (t.Remaining is { } r)
        {
            w.WriteNumber("remainingMs", (long)r.TotalMilliseconds);
        }
    }

    private static CountdownRecord? ReadCountdown(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !Enum.TryParse<CountdownState>(Text(e, "state"), out var s))
        {
            return null;
        }

        var duration = TimeSpan.FromMilliseconds(Number(e, "durationMs"));
        DateTimeOffset? end = DateTimeOffset.TryParse(Text(e, "endUtc"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        TimeSpan? remaining = e.TryGetProperty("remainingMs", out var r) && r.TryGetInt64(out var ms) && ms > 0 ? TimeSpan.FromMilliseconds(ms) : null;
        return new CountdownRecord(s, duration, end, remaining);
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : 0;
}

/// <summary>Loads and saves <see cref="HaloState"/> as <c>state.json</c> in the data folder.</summary>
public sealed class StateStore(string directory)
{
    public const string FileName = "state.json";

    private readonly AtomicJsonFile _file = new(System.IO.Path.Combine(directory, FileName), 64 * 1024);

    public string FilePath => _file.Path;

    public HaloState Load(out DataLoadStatus status)
    {
        status = _file.Load(HaloState.Parse, out var state);
        return state ?? HaloState.Empty;
    }

    public bool Save(HaloState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _file.Save(state.ToJson());
    }
}
