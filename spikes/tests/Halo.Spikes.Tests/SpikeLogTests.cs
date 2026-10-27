using System.IO;
using System.Text.Json;
using Halo.Spikes.Measurement;
using Xunit;

namespace Halo.Spikes.Tests;

public class SpikeLogTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (SpikeLog Log, StringWriter Sink) Create()
    {
        var sink = new StringWriter();
        return (new SpikeLog(sink, new FixedTime(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero))), sink);
    }

    [Fact]
    public void WritesOneJsonObjectPerLine()
    {
        var (log, sink) = Create();

        log.Write("s1.settled", LogField.Num("cpu_s", 0.25), LogField.Tok("technique", "A"));
        log.Write("s1.done");

        var lines = sink.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("2026-10-27T12:00:00.0000000Z", first.RootElement.GetProperty("t").GetString());
        Assert.Equal("s1.settled", first.RootElement.GetProperty("event").GetString());
        Assert.Equal(0.25, first.RootElement.GetProperty("cpu_s").GetDouble());
        Assert.Equal("A", first.RootElement.GetProperty("technique").GetString());
    }

    [Theory]
    [InlineData("Song Title by Artist")]
    [InlineData(@"C:\Users\someone\file.txt")]
    [InlineData("")]
    public void RejectsFreeTextTokens(string text)
    {
        Assert.Throws<ArgumentException>(() => LogField.Tok("value", text));
    }

    [Theory]
    [InlineData("Event")]
    [InlineData("has space")]
    [InlineData("t")]
    [InlineData("event")]
    public void RejectsInvalidOrReservedKeys(string key)
    {
        Assert.Throws<ArgumentException>(() => LogField.Num(key, 1));
    }

    [Fact]
    public void RejectsNonFiniteNumbers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogField.Num("x", double.PositiveInfinity));
    }

    [Fact]
    public void ErrorsOmitMessageAndStackTrace()
    {
        var (log, sink) = Create();
        Exception captured;
        try
        {
            throw new InvalidOperationException(@"failed to open C:\Users\someone\Music\Private Song.mp3");
        }
        catch (InvalidOperationException ex)
        {
            captured = ex;
        }

        log.WriteError("spike.failed", captured);

        var text = sink.ToString();
        Assert.Contains("System.InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("hresult", text, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Song", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ErrorsOmitMessageAndStackTrace", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorTypeNamesOfGenericExceptionsAreSanitised()
    {
        var (log, sink) = Create();

        log.WriteError("spike.failed", new GenericFailure<int>());

        Assert.DoesNotContain("`", sink.ToString(), StringComparison.Ordinal);
    }

    private sealed class GenericFailure<T> : Exception;
}
