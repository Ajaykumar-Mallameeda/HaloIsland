using Halo.Core.Island;

namespace Halo.Core.Notifications;

/// <summary>
/// Why Halo can or cannot read other apps' Windows notifications (Live Activity Engine 2.0, Phase 5). Windows exposes
/// them only through <c>Windows.UI.Notifications.Management.UserNotificationListener</c>, which needs package identity,
/// the <c>userNotificationListener</c> capability in the package manifest and the user's consent (RequestAccessAsync,
/// UI thread); the user can revoke it in Windows Settings at any time.
/// </summary>
public enum NotificationAccess
{
    /// <summary>The user has not opted in inside Halo (the default): nothing is probed or requested.</summary>
    Off,

    /// <summary>Halo runs without package identity (not from the MSIX), so the listener cannot be used.</summary>
    NoPackageIdentity,

    /// <summary>The package does not declare the userNotificationListener capability.</summary>
    NotDeclared,

    /// <summary>Windows has not been asked yet, or the prompt was closed without an answer.</summary>
    NotRequested,

    /// <summary>The user denied access (only Windows Settings can change it).</summary>
    Denied,

    /// <summary>Access granted.</summary>
    Allowed,
}

/// <summary>
/// The adapter boundary between Windows notifications and <see cref="NotificationCenter"/>. Pure: the window reads
/// the listener (when allowed) and hands plain values here; dedup, previews, priority, rate limits, quiet mode and
/// expiry are the center's existing rules, so Windows notifications follow the same privacy model as every provider.
/// </summary>
public static class WindowsNotifications
{
    public const string ProviderId = "windows.notifications";

    /// <summary>Generic text shown while previews are off: neither the app nor the content is revealed.</summary>
    public const string Redacted = "New notification";

    /// <summary>The conservative default policy: previews off, short lifetime, never over full screen or in quiet mode.</summary>
    public static ProviderPolicy DefaultPolicy { get; } = new()
    {
        ShowPreview = false,
        Lifetime = TimeSpan.FromMinutes(5),
        AlertDuration = TimeSpan.FromSeconds(4),
        AllowedOverFullscreen = false,
        AllowedInQuietMode = false,
        MaxPerMinute = 4,
    };

    /// <summary>
    /// Decides the access state from what can be checked without side effects, in order: the user's opt-in, package
    /// identity, the declared capability, then Windows' answer (null when not yet asked).
    /// </summary>
    public static NotificationAccess Evaluate(bool optedIn, bool hasPackageIdentity, bool capabilityDeclared, NotificationAccess? windowsStatus) =>
        !optedIn ? NotificationAccess.Off
        : !hasPackageIdentity ? NotificationAccess.NoPackageIdentity
        : !capabilityDeclared ? NotificationAccess.NotDeclared
        : windowsStatus is NotificationAccess.Allowed or NotificationAccess.Denied ? windowsStatus.Value
        : NotificationAccess.NotRequested;

    /// <summary>True only when the listener may be read.</summary>
    public static bool CanRead(NotificationAccess access) => access == NotificationAccess.Allowed;

    /// <summary>
    /// One Windows notification as a Halo notification. The app name and the first text line are private (shown only
    /// with previews on); the source ID is the listener's numeric ID, so a re-read does not show it twice. Returns null
    /// for Halo's own notifications and for notifications without any text.
    /// </summary>
    public static HaloNotification? From(uint id, string appName, IReadOnlyList<string> texts, DateTimeOffset createdAt, string ownAppName = "Halo")
    {
        ArgumentNullException.ThrowIfNull(texts);
        appName = Clean(appName);
        if (appName.Length == 0 || string.Equals(appName, ownAppName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lines = texts.Select(Clean).Where(t => t.Length > 0).ToList();
        if (lines.Count == 0)
        {
            return null;
        }

        return new HaloNotification(ProviderId, id.ToString(System.Globalization.CultureInfo.InvariantCulture), AlertPriority.Low)
        {
            Title = appName,
            Preview = lines[0],
            Redacted = Redacted,
            Glyph = "", // bell
            OccurredAt = createdAt,
        };
    }

    /// <summary>Single line, no control characters, at most 120 characters (text from other apps is untrusted).</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var chars = text.Where(c => !char.IsControl(c)).ToArray();
        var line = new string(chars).Trim();
        return line.Length <= 120 ? line : line[..119] + "…";
    }
}
