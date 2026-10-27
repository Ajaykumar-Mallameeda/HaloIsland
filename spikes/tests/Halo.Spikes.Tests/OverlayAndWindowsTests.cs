using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using Halo.Spikes.Overlay;
using Halo.Spikes.Windows;
using Xunit;

namespace Halo.Spikes.Tests;

public class SpikeOptionsTests
{
    [Fact]
    public void ParsesSmokeWithOptionalLog()
    {
        Assert.Equal(new SpikeOptions(SpikeMode.Smoke, null), SpikeOptions.Parse(["--smoke"]));
        Assert.Equal(new SpikeOptions(SpikeMode.Smoke, "out.jsonl"), SpikeOptions.Parse(["--log", "out.jsonl", "--smoke"]));
    }

    [Theory]
    [InlineData]
    [InlineData("--unknown")]
    [InlineData("--smoke", "--smoke")]
    [InlineData("--smoke", "--log")]
    [InlineData("--smoke", "--log", "--smoke")]
    public void RejectsInvalidArguments(params string[] args)
    {
        Assert.Equal(SpikeMode.Invalid, SpikeOptions.Parse(args).Mode);
    }
}

public class NativeLayoutTests
{
    [Fact]
    public void DevModeMatchesWin32Size()
    {
        // sizeof(DEVMODEW) in the Windows SDK headers.
        Assert.Equal(220, Unsafe.SizeOf<NativeMethods.DevModeW>());
    }
}

[Trait("Category", "Integration")]
public class DesktopIntegrationTests
{
    [Fact]
    public void EnvironmentSnapshotReadsDisplayAndPower()
    {
        var env = EnvironmentSnapshot.Capture();

        Assert.True(env.PrimaryWidthPx > 0 && env.PrimaryHeightPx > 0, "display mode");
        Assert.True(env.PrimaryRefreshHz > 0, "refresh rate");
        Assert.True(env.PrimaryDpi >= 96, "dpi");
        Assert.StartsWith("10.0.", env.OsVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlaySmokeModeRendersAndExits()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "Halo.Spikes.Overlay.dll");
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--smoke");

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "smoke process did not exit");

        Assert.Equal(Program.ExitOk, process.ExitCode);
        Assert.Contains("\"event\":\"env.captured\"", stderr, StringComparison.Ordinal);
        Assert.Contains("\"event\":\"smoke.rendered\"", stderr, StringComparison.Ordinal);
    }
}
