using System.IO;
using FlaUI.UIA3;
using Windows.Media;
using Windows.Media.Control;
using Windows.Media.Core;
using Windows.Media.Playback;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// Media sessions against the real Windows session manager (M3-F01). The test process itself becomes a media
/// session: a Windows MediaPlayer at volume 0 loops a sound file that ships with Windows, with a synthetic title and
/// artist. No music is downloaded and nothing is heard.
/// </summary>
public sealed partial class IslandAppTests
{
    private const string TestTitle = "Halo Test Track";
    private const string TestArtist = "Halo Tests";

    [Fact]
    public void ALiveMediaSessionIsShownAndItsEndIsNoticed()
    {
        RequireNoForeignMediaSession();
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);

        var player = StartTestSession();
        try
        {
            // The test player sets its title just after the session appears; the island must catch up with it.
            WaitForMedia(island, "playing", titleLength: TestTitle.Length);
            Assert.True(WaitUntil(() => element.Name.Contains($"Playing: {TestTitle} by {TestArtist}", StringComparison.Ordinal), 5000), $"UIA name: {element.Name}");

            // The expanded island shows the media card (text pixels in the panel).
            element.Patterns.ExpandCollapse.Pattern.Expand();
            island.WaitFor("transition.end");
            Thread.Sleep(200);
            AssertDrawn(layout, layout.Expanded, minBright: 150);

            player.Pause();
            WaitForMedia(island, "paused");
            Assert.True(WaitUntil(() => element.Name.Contains($"Paused: {TestTitle}", StringComparison.Ordinal), 5000), $"UIA name: {element.Name}");
        }
        finally
        {
            player.Dispose();
        }

        // Session closed: the island falls back to the clock (FR-MED-01 "activity ends").
        WaitForMedia(island, "none", timeoutMs: 10_000);
        Assert.DoesNotContain(TestTitle, element.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaCanBeTurnedOffAndTitlesNeverReachTheLog()
    {
        RequireNoForeignMediaSession();
        var player = StartTestSession();
        try
        {
            using (var off = IslandProcess.Start(settingsJson: "{ \"schemaVersion\": 1, \"showMedia\": false }"))
            {
                Thread.Sleep(1500);
                Assert.DoesNotContain(off.Events("media"), e => e["state"]?.GetValue<string>() != "none");
            }

            using var on = IslandProcess.Start();
            WaitForMedia(on, "playing");
            on.Exit();
            var log = File.ReadAllText(Path.Combine(on.DataDir, "logs", "halo.log"));
            Assert.DoesNotContain(TestTitle, log, StringComparison.Ordinal);
            Assert.DoesNotContain(TestArtist, log, StringComparison.Ordinal);
            Assert.DoesNotContain(on.Events("media").Select(e => e.ToJsonString()), j => j.Contains(TestTitle, StringComparison.Ordinal));
        }
        finally
        {
            player.Dispose();
        }
    }

    [Fact]
    public void TransportButtonsControlTheLiveSessionAndUnsupportedOnesAreHidden()
    {
        RequireNoForeignMediaSession();
        var layout = ExpectedLayout();
        using var pointer = new PointerScope(layout);
        using var island = IslandProcess.Start();
        var player = StartTestSession();
        try
        {
            var playing = WaitForMedia(island, "playing");
            Assert.Equal(0, playing["next"]!.GetValue<int>()); // the test player does not enable skipping yet
            Assert.Equal(0, playing["previous"]!.GetValue<int>());
            Assert.Equal(1, playing["play_pause"]!.GetValue<int>());

            using var automation = new UIA3Automation();
            automation.FromHandle(island.Hwnd).Patterns.ExpandCollapse.Pattern.Expand(); // pinned open
            island.WaitFor("transition.end");
            var buttons = Halo.Core.Media.MediaControls.Layout(layout, new Halo.Core.Media.MediaSnapshot { Title = "x", Artist = "y", CanPrevious = true, CanPlayPause = true, CanNext = true });
            var play = buttons.Single(b => b.Command == Halo.Core.Media.MediaCommand.PlayPause);
            var next = buttons.Single(b => b.Command == Halo.Core.Media.MediaCommand.Next);
            (int X, int Y) At(Halo.Core.Media.MediaButton b) => (layout.Canvas.Left + (int)b.CenterX, layout.Canvas.Top + (int)b.CenterY);

            // Play/pause: the player really pauses, and the island follows the session's own report.
            var (px, py) = At(play);
            Click(px, py);
            Assert.True(WaitUntil(() => player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused, 5000), $"the player did not pause; commands: {string.Join(",", island.Events("media.command").Select(e => $"{e["command"]}={e["result"]}"))}; trace: {island.Trace()}; click at {px},{py}");
            Assert.Contains(island.Events("media.command"), e => e["command"]?.GetValue<string>() == "play_pause" && e["result"]?.GetValue<string>() == "accepted");
            WaitForMedia(island, "paused");
            Click(px, py);
            Assert.True(WaitUntil(() => player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing, 5000), "the player did not resume");
            Assert.True(island.State() == "expanded_pinned", island.Trace()); // a button click never collapses the island

            // Hidden Next is empty panel space: the click sends nothing and, like any panel click, collapses the
            // island. Once the session enables Next, the same spot (re-expanded) sends Next.
            var (nx, ny) = At(next);
            var beforeHidden = island.Events("phase").Last();
            var endsBeforeCollapse = island.Events("transition.end").Count;
            Click(nx, ny);
            island.WaitForPhase("collapsed", after: beforeHidden);
            island.WaitFor("transition.end", skip: endsBeforeCollapse);
            Assert.DoesNotContain(island.Events("media.command"), e => e["command"]?.GetValue<string>() == "next");
            var received = 0;
            player.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
            player.CommandManager.NextReceived += (_, _) => Interlocked.Increment(ref received);
            Assert.True(WaitUntil(() => island.Events("media").LastOrDefault()?["next"]?.GetValue<int>() == 1, 5000), "the island did not notice Next became available");
            var ends = island.Events("transition.end").Count;
            automation.FromHandle(island.Hwnd).Patterns.ExpandCollapse.Pattern.Expand();
            island.WaitFor("transition.end", skip: ends);
            Click(nx, ny);
            Assert.True(WaitUntil(() => Volatile.Read(ref received) > 0, 5000), $"Next did not reach the player; commands: {string.Join(",", island.Events("media.command").Select(e => $"{e["command"]}={e["result"]}"))}; trace: {island.Trace()}");
        }
        finally
        {
            player.Dispose();
        }
    }

    /// <summary>
    /// A MediaPlayer whose command manager drives the system media controls, so its title and artist are the playback
    /// item's display properties (manual DisplayUpdater changes are ignored in this mode).
    /// </summary>
    private static MediaPlayer StartTestSession()
    {
        var item = new MediaPlaybackItem(MediaSource.CreateFromUri(new Uri(@"C:\Windows\Media\Alarm01.wav")));
        var display = item.GetDisplayProperties();
        display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = TestTitle;
        display.MusicProperties.Artist = TestArtist;
        item.ApplyDisplayProperties(display);
        var player = new MediaPlayer { Volume = 0, IsLoopingEnabled = true, Source = item };
        player.Play();
        return player;
    }

    /// <summary>
    /// The island follows Windows' current session (FR-MED-03), so another app's session (a browser tab, a music
    /// app) would legitimately take over mid-test. These tests need the machine's media to be idle: wait briefly for
    /// foreign sessions to end, then fail with a clear message instead of a misleading one later.
    /// </summary>
    private static void RequireNoForeignMediaSession()
    {
        string[] foreign = [];
        var quiet = WaitUntil(() => (foreign = ForeignMediaSessions()).Length == 0, 15_000);
        Assert.True(quiet, $"another app holds a media session ({string.Join(", ", foreign)}); stop its playback or close it before running the media tests");
    }

    private static string[] AllMediaSessions()
    {
        var manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
        return [.. manager.GetSessions().Select(s => $"{s.SourceAppUserModelId}:{s.GetPlaybackInfo().PlaybackStatus}")];
    }

    private static string[] ForeignMediaSessions()
    {
        var own = Path.GetFileName(Environment.ProcessPath) ?? string.Empty;
        var manager = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
        return [.. manager.GetSessions().Select(s => s.SourceAppUserModelId ?? "?").Where(id => !string.Equals(id, own, StringComparison.OrdinalIgnoreCase))];
    }

    private static System.Text.Json.Nodes.JsonObject WaitForMedia(IslandProcess island, string state, int timeoutMs = 8000, int? titleLength = null)
    {
        System.Text.Json.Nodes.JsonObject? found = null;
        var ok = WaitUntil(() => (found = island.Events("media").LastOrDefault(e => e["state"]?.GetValue<string>() == state && (titleLength is null || e["title_length"]?.GetValue<int>() == titleLength))) is not null, timeoutMs);
        Assert.True(ok, $"no media state '{state}'; seen: {string.Join(",", island.Events("media").Select(e => $"{e["state"]}/{e["title_length"]}/s{e["sessions"]}"))}; events: {string.Join(",", island.Events("media.event").Select(e => e["kind"]?.GetValue<string>()))}; failures: {string.Join(",", island.Events("media.refresh_failed").Select(e => $"{e["type"]}:{e["hresult"]}"))}; status: {string.Join(",", island.Events("media.status").Select(e => e["status"]?.GetValue<string>()))}; sessions seen by the test: {string.Join(",", AllMediaSessions())}");
        return found!;
    }
}
