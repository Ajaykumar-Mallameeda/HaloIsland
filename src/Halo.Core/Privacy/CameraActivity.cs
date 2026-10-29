using Halo.Core.Activities;
using Halo.Core.Island;

namespace Halo.Core.Privacy;

/// <summary>One process streaming from one camera, as Windows' sensor activity report lists it.</summary>
/// <param name="ProcessId">Used only to leave Halo itself out; never stored, shown or logged.</param>
/// <param name="Streaming">The process is receiving frames now.</param>
public readonly record struct CameraUse(uint ProcessId, bool Streaming);

/// <summary>
/// The camera-in-use indicator (K / C-F08), off by default. It keeps only whether some other app is streaming a camera
/// and how many streams there are: no app names, no process IDs, no history. Windows' own privacy indicator stays the
/// authority; Halo only mirrors it in the island.
/// </summary>
public sealed class CameraActivityMonitor(uint ownProcessId)
{
    public const string ActivityId = "privacy.camera";
    public const string AlertKey = "privacy.camera";
    private const string CameraGlyph = "";

    public bool InUse { get; private set; }

    /// <summary>Streams by other apps in the latest report.</summary>
    public int StreamCount { get; private set; }

    /// <summary>Applies a report. Returns the pill to show when the camera goes from unused to in use.</summary>
    public IslandAlert? Update(IReadOnlyList<CameraUse> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);
        var was = InUse;
        StreamCount = uses.Count(u => u.Streaming && u.ProcessId != ownProcessId);
        InUse = StreamCount > 0;
        return InUse && !was
            ? new IslandAlert(AlertKey, AlertKind.Halo, AlertPriority.High, new AlertContent(CameraGlyph, "Camera on") { Accessible = "An app started using the camera", Tone = Theming.Tone.Camera }, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10))
            {
                AllowedInQuietMode = true, // a privacy signal, not a distraction
            }
            : null;
    }

    /// <summary>The ongoing activity while a camera is in use, else null.</summary>
    public IslandActivity? Activity() => InUse
        ? new IslandActivity(ActivityId, ActivityKind.System, AlertPriority.Normal)
        {
            Provider = LiveProviders.Camera,
            Relevance = LiveProviders.CameraRelevance,
            Dismissible = false, // a privacy indicator stays while the camera is in use
            Glyph = CameraGlyph,
            Tone = Theming.Tone.Camera,
            CompactText = "Camera on",
            Title = "Camera in use",
            Subtitle = StreamCount == 1 ? "An app is using a camera" : $"{StreamCount} camera streams in use",
            Accessible = "Camera in use",
            AllowedInQuietMode = true,
        }
        : null;

    public void Reset()
    {
        InUse = false;
        StreamCount = 0;
    }
}
