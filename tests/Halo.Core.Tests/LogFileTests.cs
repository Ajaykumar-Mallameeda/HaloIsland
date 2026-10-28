using System.Text.Json;
using Halo.Core.Diagnostics;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Local diagnostic log (M2-F03, FR-APP-07): privacy by construction, bounded size and rate, never fatal.</summary>
public sealed class LogFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "halo-log-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void WritesOneJsonObjectPerLineWithLevelTimeAndFields()
    {
        var log = new LogFile(_dir);

        log.Write(LogLevel.Warning, "tray.failed", ("reason", "initial"), ("hresult", -2147467259), ("ok", false), ("ratio", 0.5));

        var line = Assert.Single(File.ReadAllLines(log.FilePath));
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal("warning", root.GetProperty("level").GetString());
        Assert.Equal("tray.failed", root.GetProperty("event").GetString());
        Assert.Equal("initial", root.GetProperty("reason").GetString());
        Assert.Equal(-2147467259, root.GetProperty("hresult").GetInt32());
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0.5, root.GetProperty("ratio").GetDouble());
        Assert.True(root.TryGetProperty("t", out _));
    }

    [Theory]
    [InlineData("Bohemian Rhapsody")] // a song title (spaces)
    [InlineData("C:\\Users\\someone\\secret.txt")] // a path
    [InlineData("Buy milk; call mum")] // task text
    [InlineData("\"injected\":1,\"x")] // JSON injection attempt
    [InlineData("")]
    public void FreeTextCannotReachTheLog(string text)
    {
        var log = new LogFile(_dir);

        log.Write(LogLevel.Information, "media.title", ("title", text));
        log.Write(LogLevel.Information, text.Length == 0 ? "x y" : text);

        var content = File.ReadAllText(log.FilePath);
        if (text.Length > 0)
        {
            Assert.DoesNotContain(text, content, StringComparison.Ordinal);
        }

        Assert.Equal(2, content.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        foreach (var l in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var doc = JsonDocument.Parse(l); // still valid JSON
        }

        Assert.Contains("\"redacted\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TokensAreShortAsciiIdentifiers()
    {
        Assert.True(LogFile.IsToken("InvalidOperationException"));
        Assert.True(LogFile.IsToken("recovered_defaults"));
        Assert.True(LogFile.IsToken("app.start-2"));
        Assert.False(LogFile.IsToken("two words"));
        Assert.False(LogFile.IsToken("é"));
        Assert.False(LogFile.IsToken(new string('a', 65)));
        Assert.False(LogFile.IsToken(null));
    }

    [Fact]
    public void RollsAtTheSizeLimitAndKeepsAtMostFiveFiles()
    {
        var clock = new VirtualTimer();
        var log = new LogFile(_dir, clock);
        var pad = new string('a', 60);
        for (var i = 0; i < 70_000; i++)
        {
            // Distinct event names per minute window keep the rate limit out of this test.
            log.Write(LogLevel.Information, $"e{i % 19}", ("pad", pad));
            if (i % 380 == 0)
            {
                clock.Advance(TimeSpan.FromMinutes(1));
            }
        }

        var files = Directory.GetFiles(_dir, "halo*.log");
        Assert.Equal(LogFile.MaxFiles, files.Length);
        Assert.All(files, f => Assert.True(new FileInfo(f).Length <= LogFile.MaxFileBytes, $"{f} exceeds the limit"));
        Assert.True(File.Exists(Path.Combine(_dir, "halo.4.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "halo.5.log")));
    }

    [Fact]
    public void ARepeatedEventIsLimitedPerMinuteAndTheDropIsReported()
    {
        var clock = new VirtualTimer();
        var log = new LogFile(_dir, clock);

        for (var i = 0; i < 100; i++)
        {
            log.Write(LogLevel.Error, "window.error", ("hresult", 1));
        }

        Assert.Equal(LogFile.MaxPerMinute, File.ReadAllLines(log.FilePath).Length);

        clock.Advance(TimeSpan.FromMinutes(1));
        log.Write(LogLevel.Error, "window.error", ("hresult", 1));

        var lines = File.ReadAllLines(log.FilePath);
        Assert.Contains("\"event\":\"log.suppressed\"", lines[^2], StringComparison.Ordinal);
        Assert.Contains("\"dropped\":80", lines[^2], StringComparison.Ordinal);
    }

    [Fact]
    public void WriteFailuresAreCountedNotThrown()
    {
        // A file where the log directory should be: every write fails.
        Directory.CreateDirectory(Path.GetDirectoryName(_dir)!);
        File.WriteAllText(_dir, "not a directory");
        var log = new LogFile(_dir);

        log.Write(LogLevel.Error, "app.crash", ("type", "X"));
        log.Write(LogLevel.Error, "app.crash", ("type", "X"));

        Assert.Equal(2, log.FailedWrites);
        File.Delete(_dir);
    }
}
