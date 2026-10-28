using System.Diagnostics;
using System.IO;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace Halo.App.Tests;

/// <summary>
/// J-F01 and J-F02 against real Windows media sessions: the test process's silent MediaPlayer, plus a second silent
/// MediaPlayer hosted by Windows PowerShell for switching. Nothing is heard (volume 0) and nothing is downloaded.
/// </summary>
public sealed partial class IslandAppTests
{
    [Fact]
    public void ASeekableSessionCanBeSeekedAndANonSeekableOneIsReadOnly()
    {
        RequireNoForeignMediaSession();
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        var player = StartTestSession();
        try
        {
            var media = WaitForMedia(island, "playing", titleLength: TestTitle.Length);
            var seekable = media["seek"]!.GetValue<int>() == 1;
            element.Patterns.ExpandCollapse.Pattern.Expand();
            island.WaitFor("transition.end");
            var progress = WaitForChild(element, "Halo.MediaProgress");
            if (!seekable)
            {
                // The session does not allow seeking: the position is read-only and nothing offers to seek.
                Assert.Equal(ControlType.ProgressBar, progress.ControlType);
                Assert.True(progress.Patterns.RangeValue.Pattern.IsReadOnly.Value);
                Assert.Skip("The test session reports no seeking; only the read-only path was checked.");
            }

            Assert.Equal(ControlType.Slider, progress.ControlType);
            player.Pause();
            WaitForMedia(island, "paused");
            progress = WaitForChild(element, "Halo.MediaProgress");
            progress.Patterns.RangeValue.Pattern.SetValue(50);
            var command = island.WaitFor("media.command");
            Assert.Equal(("seek", 50), (command["command"]!.GetValue<string>(), command["percent"]!.GetValue<int>()));
            Assert.Equal("accepted", command["result"]!.GetValue<string>());
            var duration = player.PlaybackSession.NaturalDuration;
            Assert.True(WaitUntil(() => Math.Abs(player.PlaybackSession.Position.TotalSeconds - (duration.TotalSeconds / 2)) < Math.Max(0.6, duration.TotalSeconds * 0.1), 5000), $"position {player.PlaybackSession.Position} of {duration}");
        }
        finally
        {
            player.Dispose();
        }
    }

    [Fact]
    public void WithTwoSessionsTheUserCanSwitchBetweenThem()
    {
        RequireNoForeignMediaSession();
        using var island = IslandProcess.Start();
        using var automation = new UIA3Automation();
        var element = automation.FromHandle(island.Hwnd);
        var player = StartTestSession();
        using var second = StartPowerShellSession();
        try
        {
            Assert.True(WaitUntil(() => island.Events("media").Any(e => e["sessions"]!.GetValue<int>() == 2), 15_000), $"two sessions never seen: {string.Join(",", AllMediaSessions())}");
            element.Patterns.ExpandCollapse.Pattern.Expand();
            island.WaitFor("transition.end");

            var before = island.Events("media").Last()["index"]!.GetValue<int>();
            var button = WaitForChild(element, "Halo.Media.Switch");
            Assert.StartsWith("Switch media source (", button.Name, StringComparison.Ordinal);
            button.Patterns.Invoke.Pattern.Invoke();
            var switched = island.WaitFor("media.command");
            Assert.Equal(("switch", "accepted"), (switched["command"]!.GetValue<string>(), switched["result"]!.GetValue<string>()));
            Assert.True(WaitUntil(() => island.Events("media").Last()["index"]!.GetValue<int>() != before, 5000), "the selected session did not change");

            // The choice holds although the other session is the one playing.
            Thread.Sleep(1500);
            Assert.NotEqual(before, island.Events("media").Last()["index"]!.GetValue<int>());
        }
        finally
        {
            player.Dispose();
            if (!second.HasExited)
            {
                second.Kill();
            }
        }

        WaitForMedia(island, "none", timeoutMs: 15_000);
    }

    /// <summary>A second, silent media session in another process (Windows PowerShell hosting a WinRT MediaPlayer).</summary>
    private static Process StartPowerShellSession()
    {
        var script = string.Join(
            "; ",
            "$null = [Windows.Media.Playback.MediaPlayer,Windows.Media.Playback,ContentType=WindowsRuntime]",
            "$null = [Windows.Media.Core.MediaSource,Windows.Media.Core,ContentType=WindowsRuntime]",
            "$p = New-Object Windows.Media.Playback.MediaPlayer",
            "$p.Volume = 0",
            "$p.IsLoopingEnabled = $true",
            "$p.Source = [Windows.Media.Core.MediaSource]::CreateFromUri([Uri]'C:\\Windows\\Media\\Alarm02.wav')",
            "$p.Play()",
            "Start-Sleep -Seconds 60");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
        {
            info.ArgumentList.Add(arg);
        }

        return Process.Start(info)!;
    }
}
