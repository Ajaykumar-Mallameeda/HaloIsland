using System.IO;
using System.Text.Json;
using Xunit;

namespace Halo.App.Tests;

/// <summary>The local diagnostic log written by the running island (M2-F03).</summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void TheIslandWritesAPrivacySafeLocalLog()
    {
        string logPath;
        using (var island = IslandProcess.Start(fault: "tray", settingsJson: "{ broken"))
        {
            island.WaitFor("tray");
            logPath = Path.Combine(island.DataDir, "logs", "halo.log");
            island.Exit();
            Assert.Equal(0, island.ExitCode);

            var lines = File.ReadAllLines(logPath);
            var events = lines.Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
            var names = events.Select(e => e.GetProperty("event").GetString()).ToList();

            Assert.Equal("app.start", names[0]);
            Assert.Equal("warning", events[0].GetProperty("level").GetString()); // settings had to be recovered
            Assert.Equal("recovered_defaults", events[0].GetProperty("settings").GetString());
            Assert.Contains("tray.failed", names);
            Assert.Equal("app.exit", names[^1]);

            // Nothing but tokens and numbers: no spaces, no paths, no user content.
            foreach (var e in events)
            {
                foreach (var p in e.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String && p.Name != "t"))
                {
                    Assert.Matches("^[A-Za-z0-9_.-]{1,64}$", p.Value.GetString()!);
                }
            }

            Assert.DoesNotContain(island.DataDir, File.ReadAllText(logPath), StringComparison.OrdinalIgnoreCase);
            Assert.True(lines.Length < 20, $"{lines.Length} lines for one short run: too chatty");
        }
    }
}
