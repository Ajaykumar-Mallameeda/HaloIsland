using Halo.Core.Island;
using Halo.Core.Settings;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>Settings model and store (M2-F01, ADR-001 §8), each test in its own temporary folder.</summary>
public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "halo-settings-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void DefaultsMatchTheShippedBehaviour()
    {
        var d = HaloSettings.Defaults;

        Assert.True(d.HoverToExpand);
        Assert.Equal(120, d.HoverDwellMs);
        Assert.Equal(400, d.LeaveGraceMs);
        Assert.False(d.StartExpanded);
        Assert.False(d.ReduceMotion);
        Assert.True(d.HideInFullscreen);
        Assert.True(d.AnnounceAlerts);
    }

    [Fact]
    public void MissingFileGivesDefaultsAndWritesNothing()
    {
        var store = new SettingsStore(_dir);

        var s = store.Load(out var status);

        Assert.Equal(SettingsLoadStatus.Missing, status);
        Assert.Equal(HaloSettings.Defaults, s);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void SettingsPersistAcrossRestarts()
    {
        var changed = HaloSettings.Defaults with { HoverToExpand = false, HoverDwellMs = 250, LeaveGraceMs = 900, StartExpanded = true, ReduceMotion = true, HideInFullscreen = false, AnnounceAlerts = false };
        Assert.True(new SettingsStore(_dir).Save(changed));

        var reloaded = new SettingsStore(_dir).Load(out var status);

        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(changed, reloaded);
    }

    [Fact]
    public void SavingUnchangedSettingsDoesNotWrite()
    {
        var store = new SettingsStore(_dir);
        Assert.True(store.Save(HaloSettings.Defaults));
        var written = File.GetLastWriteTimeUtc(store.FilePath);

        Assert.False(store.Save(HaloSettings.Defaults));
        Assert.Equal(written, File.GetLastWriteTimeUtc(store.FilePath));

        // A freshly loaded store also knows what is on disk.
        var again = new SettingsStore(_dir);
        again.Load(out _);
        Assert.False(again.Save(HaloSettings.Defaults));
    }

    [Fact]
    public void SecondSaveKeepsTheFirstAsBackupAndLeavesNoTempFile()
    {
        var store = new SettingsStore(_dir);
        store.Save(HaloSettings.Defaults);
        store.Save(HaloSettings.Defaults with { HoverDwellMs = 300 });

        Assert.True(File.Exists(store.BackupPath));
        Assert.Contains("\"hoverDwellMs\": 120", File.ReadAllText(store.BackupPath), StringComparison.Ordinal);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("\"hoverDwellMs\": 9000")]
    [InlineData("\"hoverDwellMs\": -5")]
    [InlineData("\"hoverDwellMs\": \"fast\"")]
    [InlineData("\"hoverDwellMs\": 1.5")]
    [InlineData("\"leaveGraceMs\": 20")]
    [InlineData("\"hoverToExpand\": \"yes\"")]
    public void InvalidFieldsFallBackToTheirDefaultAndKeepTheRest(string bad)
    {
        Write($"{{ \"schemaVersion\": 1, \"reduceMotion\": true, {bad} }}");
        var store = new SettingsStore(_dir);

        var s = store.Load(out var status);

        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.True(s.ReduceMotion); // the valid field survives
        Assert.Equal(120, s.HoverDwellMs);
        Assert.Equal(400, s.LeaveGraceMs);
        Assert.True(s.HoverToExpand);
        Assert.Single(store.LastIssues);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("")]
    public void CorruptFileIsSetAsideAndTheBackupIsUsed(string corrupt)
    {
        var store = new SettingsStore(_dir);
        store.Save(HaloSettings.Defaults with { HoverDwellMs = 200 });
        store.Save(HaloSettings.Defaults with { HoverDwellMs = 300 }); // backup now holds 200
        Write(corrupt);

        var s = new SettingsStore(_dir).Load(out var status);

        Assert.Equal(SettingsLoadStatus.RecoveredFromBackup, status);
        Assert.Equal(200, s.HoverDwellMs);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
        Assert.False(File.Exists(Path.Combine(_dir, SettingsStore.FileName))); // set aside, not deleted
    }

    [Fact]
    public void CorruptFileWithoutBackupGivesDefaults()
    {
        Write("\u0000\u0001garbage");

        var s = new SettingsStore(_dir).Load(out var status);

        Assert.Equal(SettingsLoadStatus.RecoveredDefaults, status);
        Assert.Equal(HaloSettings.Defaults, s);
    }

    [Fact]
    public void OversizedFileIsTreatedAsCorrupt()
    {
        Write("{ \"pad\": \"" + new string('x', 70_000) + "\" }");

        new SettingsStore(_dir).Load(out var status);

        Assert.Equal(SettingsLoadStatus.RecoveredDefaults, status);
    }

    [Fact]
    public void FileWithoutSchemaVersionIsMigratedAndRewrittenAsVersionOne()
    {
        Write("{ \"hoverDwellMs\": 222 }");
        var store = new SettingsStore(_dir);

        var s = store.Load(out var status);
        Assert.Equal(SettingsLoadStatus.Migrated, status);
        Assert.Equal(222, s.HoverDwellMs);

        Assert.True(store.Save(s)); // a migrated file is always written back
        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void NewerVersionIsReadAndBackedUpBeforeItIsOverwritten()
    {
        Write("{ \"schemaVersion\": 7, \"hoverDwellMs\": 333, \"futureSetting\": 42 }");
        var store = new SettingsStore(_dir);

        var s = store.Load(out var status);
        Assert.Equal(SettingsLoadStatus.Loaded, status);
        Assert.Equal(333, s.HoverDwellMs);

        store.Save(s with { HoverDwellMs = 100 });
        var kept = Path.Combine(_dir, "settings.json.v7.bak");
        Assert.True(File.Exists(kept));
        Assert.Contains("futureSetting", File.ReadAllText(kept), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonContainsOnlyTheDocumentedKeys()
    {
        var json = HaloSettings.Defaults.ToJson();

        foreach (var key in new[] { "schemaVersion", "hoverToExpand", "hoverDwellMs", "leaveGraceMs", "startExpanded", "reduceMotion", "hideInFullscreen", "announceAlerts" })
        {
            Assert.Contains($"\"{key}\"", json, StringComparison.Ordinal);
        }

        Assert.Equal(8, json.Count(c => c == ':'));
    }

    [Fact]
    public void ClickOnlyModeNeverStartsTheDwell()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default with { HoverEnabled = false }, clock);
        clock.Attach(island);

        island.Apply(IslandInput.PointerEntered);
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(IslandPhase.Collapsed, island.State.Phase);
        Assert.True(island.State.Hovered); // feedback still works
        island.Apply(IslandInput.PointerPressed);
        island.Apply(IslandInput.PointerReleasedInside);
        Assert.Equal(IslandPhase.ExpandedPinned, island.State.Phase); // click still opens
    }

    [Fact]
    public void NewTimingAppliesToTheNextPendingAction()
    {
        var clock = new VirtualTimer();
        var island = new IslandController(clock, IslandTiming.Default, clock);
        clock.Attach(island);

        island.SetTiming(new IslandTiming(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(400)));
        island.Apply(IslandInput.PointerEntered);
        clock.Advance(TimeSpan.FromMilliseconds(299));
        Assert.False(island.State.IsExpanded);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(island.State.IsExpanded);
    }

    private void Write(string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), text);
    }
}
