using System.Globalization;
using System.Text;

namespace Halo.Core.Settings;

/// <summary>How the last load went (diagnostics token in parentheses).</summary>
public enum SettingsLoadStatus
{
    /// <summary>No file yet: defaults (missing).</summary>
    Missing,

    /// <summary>The file was read (loaded). Individual bad fields may still have fallen back.</summary>
    Loaded,

    /// <summary>An older schema was upgraded in memory and is written back on the next save (migrated).</summary>
    Migrated,

    /// <summary>The file was unreadable; it was set aside and the backup was used (recovered_backup).</summary>
    RecoveredFromBackup,

    /// <summary>The file and its backup were unreadable; defaults are used (recovered_defaults).</summary>
    RecoveredDefaults,
}

/// <summary>
/// Settings persistence (ADR-001 §8, FR-APP-05, NFR-07) in one small JSON file, by default in
/// <c>%LOCALAPPDATA%\ProjectHalo\settings.json</c>. No roaming, no cloud, no secrets.
/// <list type="bullet">
/// <item>Writes are atomic: <c>settings.json.tmp</c> is written and flushed, then swapped in with
/// <see cref="File.Replace(string, string, string?)"/>, which keeps the previous file as <c>settings.json.bak</c>.</item>
/// <item>A save that would write the same bytes is skipped (no needless disk writes); the caller debounces.</item>
/// <item>An unreadable file is renamed <c>settings.json.corrupt-yyyyMMddHHmmss</c> (kept for support), then the backup
/// is tried, then defaults. Files larger than 64 KB are treated as unreadable.</item>
/// <item>A file from a newer version is read for the fields this version knows, and is copied to
/// <c>settings.json.v{N}.bak</c> before it is first overwritten (downgrade keeps a backup).</item>
/// </list>
/// </summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";
    private const int MaxBytes = 64 * 1024;

    private string? _lastWritten;
    private int _loadedSchemaVersion = HaloSettings.CurrentSchemaVersion;

    public string Directory { get; } = directory;

    public string FilePath => Path.Combine(Directory, FileName);

    public string BackupPath => FilePath + ".bak";

    /// <summary>Fields that had invalid values and fell back to their defaults in the last load.</summary>
    public IReadOnlyList<string> LastIssues { get; private set; } = [];

    /// <summary>The per-user default location for the unpackaged app.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProjectHalo");

    public HaloSettings Load(out SettingsLoadStatus status)
    {
        LastIssues = [];
        if (!File.Exists(FilePath))
        {
            status = SettingsLoadStatus.Missing;
            return HaloSettings.Defaults;
        }

        if (TryRead(FilePath, out var settings, out var version, out var issues))
        {
            LastIssues = issues;
            _loadedSchemaVersion = version;
            status = version < HaloSettings.CurrentSchemaVersion ? SettingsLoadStatus.Migrated : SettingsLoadStatus.Loaded;
            _lastWritten = status == SettingsLoadStatus.Loaded && version == HaloSettings.CurrentSchemaVersion ? settings.ToJson() : null;
            return settings;
        }

        SetAside();
        if (File.Exists(BackupPath) && TryRead(BackupPath, out settings, out version, out issues))
        {
            LastIssues = issues;
            _loadedSchemaVersion = version;
            status = SettingsLoadStatus.RecoveredFromBackup;
            return settings;
        }

        status = SettingsLoadStatus.RecoveredDefaults;
        return HaloSettings.Defaults;
    }

    /// <summary>Writes the settings atomically. Returns false when nothing needed writing.</summary>
    public bool Save(HaloSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var json = settings.ToJson();
        if (string.Equals(json, _lastWritten, StringComparison.Ordinal))
        {
            return false;
        }

        _ = System.IO.Directory.CreateDirectory(Directory);
        if (_loadedSchemaVersion > HaloSettings.CurrentSchemaVersion && File.Exists(FilePath))
        {
            File.Copy(FilePath, $"{FilePath}.v{_loadedSchemaVersion.ToString(CultureInfo.InvariantCulture)}.bak", overwrite: true);
            _loadedSchemaVersion = HaloSettings.CurrentSchemaVersion;
        }

        var tmp = FilePath + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(FilePath))
        {
            File.Replace(tmp, FilePath, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, FilePath);
        }

        _lastWritten = json;
        return true;
    }

    private static bool TryRead(string path, out HaloSettings settings, out int version, out List<string> issues)
    {
        settings = HaloSettings.Defaults;
        version = 0;
        issues = [];
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
            {
                return false;
            }

            return HaloSettings.TryParse(File.ReadAllText(path, Encoding.UTF8), out settings, out version, out issues);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SetAside()
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(FilePath, $"{FilePath}.corrupt-{stamp}", overwrite: true);
        }
        catch (IOException)
        {
            // Leave it in place; the next save replaces it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
