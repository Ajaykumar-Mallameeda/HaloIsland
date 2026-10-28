using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Halo.Core.Accounts;

/// <summary>Connection state of an account provider, shown to the user and logged as a token.</summary>
public enum ProviderStatus
{
    /// <summary>No account connected (the default; nothing is requested).</summary>
    NotConnected,

    Connected,

    /// <summary>The sign-in expired or was revoked: the user must sign in again.</summary>
    SignInRequired,

    /// <summary>Consent for the needed permission was denied or withdrawn.</summary>
    PermissionDenied,

    /// <summary>The service asked Halo to slow down (HTTP 429); <see cref="ProviderResult{T}.RetryAfter"/> says when.</summary>
    Throttled,

    /// <summary>Network or service failure; retried with backoff.</summary>
    Unavailable,
}

/// <summary>A provider call's outcome: a value, or a status explaining why there is none.</summary>
public sealed record ProviderResult<T>(ProviderStatus Status, T? Value, TimeSpan? RetryAfter = null)
{
    public bool Ok => Status == ProviderStatus.Connected && Value is not null;
}

/// <summary>
/// Supplies an OAuth access token for a delegated, least-privilege scope set, or null when no account is connected.
/// Implementations own interactive sign-in and secure token storage (Windows Credential Manager), and must never
/// write tokens to logs or files in the repository. Halo ships none that signs in unattended.
/// </summary>
public interface IAccessTokenSource
{
    /// <summary>Scopes this source was consented for (shown in settings).</summary>
    IReadOnlyList<string> Scopes { get; }

    Task<string?> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Disconnect: forget cached tokens. Revoking consent is done by the user in their account settings.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);
}

/// <summary>The token source until the owner connects an account: always "not connected", never touches the network.</summary>
public sealed class NotConnectedTokenSource : IAccessTokenSource
{
    public IReadOnlyList<string> Scopes => [];

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>One unread message, with only the fields Halo shows (E-F02). No body, preview or attachments.</summary>
public sealed record MailSummary(string Id, string SenderName, string Subject, DateTimeOffset ReceivedUtc, Uri? WebLink);

/// <summary>Inbox state: unread count and the newest unread messages.</summary>
public sealed record MailSnapshot(int UnreadCount, IReadOnlyList<MailSummary> Unread);

/// <summary>An email provider (E-F01). Vendor-neutral: the island only sees snapshots and statuses.</summary>
public interface IMailProvider
{
    string ProviderId { get; }

    Task<ProviderResult<MailSnapshot>> GetInboxAsync(CancellationToken cancellationToken);
}

/// <summary>One calendar event with only what the glance and reminders need (F-F01). No body or attendees.</summary>
public sealed record CalendarEvent(string Id, string Subject, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public bool IsAllDay { get; init; }

    public bool IsCancelled { get; init; }

    /// <summary>Private or confidential at the source: the subject is never shown.</summary>
    public bool IsPrivate { get; init; }

    public string Location { get; init; } = string.Empty;

    /// <summary>Online-meeting join link, when the event has one and it passed <see cref="LinkPolicy"/>.</summary>
    public Uri? JoinUrl { get; init; }

    /// <summary>Link to open the event in the provider's own app or site.</summary>
    public Uri? WebLink { get; init; }
}

/// <summary>A calendar provider (F-F01).</summary>
public interface ICalendarProvider
{
    string ProviderId { get; }

    Task<ProviderResult<IReadOnlyList<CalendarEvent>>> GetEventsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
}

/// <summary>
/// Which links Halo may open for an action: HTTPS only, on the provider's own hosts. Anything else in content is
/// ignored, so a crafted notification cannot make Halo open an arbitrary address.
/// </summary>
public static class LinkPolicy
{
    private static readonly string[] AllowedHostSuffixes =
    [
        "outlook.office.com",
        "outlook.office365.com",
        "outlook.live.com",
        "teams.microsoft.com",
        "teams.live.com",
    ];

    public static Uri? Allow(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        var host = uri.IdnHost;
        return AllowedHostSuffixes.Any(s => host.Equals(s, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + s, StringComparison.OrdinalIgnoreCase)) ? uri : null;
    }
}

/// <summary>
/// Microsoft Graph mail and calendar adapter (E-F02, F-F01), read-only and least privilege: delegated
/// <c>Mail.ReadBasic</c> (no body, preview or attachments) and <c>Calendars.ReadBasic</c> (no body or attachments).
/// It never sends, deletes, accepts, declines or modifies anything. HTTPS to graph.microsoft.com only; 10 s timeout;
/// 1 MB response cap; strict JSON reading of a few fields. Tokens come from <see cref="IAccessTokenSource"/>.
/// </summary>
public sealed class GraphClient(HttpMessageInvoker http, IAccessTokenSource tokens) : IMailProvider, ICalendarProvider
{
    public const string MailProviderId = "outlook.mail";
    public const string CalendarProviderId = "outlook.calendar";
    public static readonly Uri BaseUri = new("https://graph.microsoft.com/v1.0/");
    public static readonly IReadOnlyList<string> RequiredScopes = ["User.Read", "Mail.ReadBasic", "Calendars.ReadBasic", "offline_access"];
    private const int MaxResponseBytes = 1024 * 1024;
    private const int MaxText = 256;

    string IMailProvider.ProviderId => MailProviderId;

    string ICalendarProvider.ProviderId => CalendarProviderId;

    public static Uri InboxUri() => new(BaseUri, "me/mailFolders/inbox?$select=unreadItemCount");

    public static Uri UnreadUri(int top = 10) => new(
        BaseUri,
        "me/mailFolders/inbox/messages?$select=id,subject,from,receivedDateTime,webLink&$filter=isRead%20eq%20false&$orderby=receivedDateTime%20desc&$top=" + top.ToString(CultureInfo.InvariantCulture));

    public static Uri CalendarViewUri(DateTimeOffset fromUtc, DateTimeOffset toUtc) => new(
        BaseUri,
        "me/calendarView?startDateTime=" + Uri.EscapeDataString(fromUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        + "&endDateTime=" + Uri.EscapeDataString(toUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        + "&$select=id,subject,start,end,isAllDay,isCancelled,sensitivity,location,isOnlineMeeting,onlineMeeting,webLink&$orderby=start/dateTime&$top=20");

    public async Task<ProviderResult<MailSnapshot>> GetInboxAsync(CancellationToken cancellationToken)
    {
        var folder = await GetJsonAsync(InboxUri(), cancellationToken).ConfigureAwait(false);
        if (folder.Value is not { } f)
        {
            return new ProviderResult<MailSnapshot>(folder.Status, null, folder.RetryAfter);
        }

        int unread;
        using (f)
        {
            unread = f.RootElement.TryGetProperty("unreadItemCount", out var c) && c.TryGetInt32(out var n) ? Math.Max(0, n) : 0;
        }

        var list = await GetJsonAsync(UnreadUri(), cancellationToken).ConfigureAwait(false);
        if (list.Value is not { } l)
        {
            return new ProviderResult<MailSnapshot>(list.Status, null, list.RetryAfter);
        }

        using (l)
        {
            return new ProviderResult<MailSnapshot>(ProviderStatus.Connected, new MailSnapshot(unread, ParseMessages(l.RootElement)));
        }
    }

    public async Task<ProviderResult<IReadOnlyList<CalendarEvent>>> GetEventsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var page = await GetJsonAsync(CalendarViewUri(fromUtc, toUtc), cancellationToken, utcCalendar: true).ConfigureAwait(false);
        if (page.Value is not { } doc)
        {
            return new ProviderResult<IReadOnlyList<CalendarEvent>>(page.Status, null, page.RetryAfter);
        }

        using (doc)
        {
            return new ProviderResult<IReadOnlyList<CalendarEvent>>(ProviderStatus.Connected, ParseEvents(doc.RootElement));
        }
    }

    /// <summary>Messages from a Graph <c>value</c> array; malformed entries are skipped.</summary>
    public static IReadOnlyList<MailSummary> ParseMessages(JsonElement root)
    {
        var list = new List<MailSummary>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var m in value.EnumerateArray())
        {
            var id = Str(m, "id");
            if (string.IsNullOrEmpty(id) || !DateTimeOffset.TryParse(Str(m, "receivedDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var received))
            {
                continue;
            }

            var sender = m.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var address) ? Str(address, "name") ?? Str(address, "address") : null;
            list.Add(new MailSummary(id, Cap(sender) is { Length: > 0 } s ? s : "Unknown sender", Cap(Str(m, "subject")), received.ToUniversalTime(), LinkPolicy.Allow(Str(m, "webLink"))));
        }

        return list;
    }

    /// <summary>Events from a Graph calendarView page requested with <c>Prefer: outlook.timezone="UTC"</c>.</summary>
    public static IReadOnlyList<CalendarEvent> ParseEvents(JsonElement root)
    {
        var list = new List<CalendarEvent>();
        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var e in value.EnumerateArray())
        {
            var id = Str(e, "id");
            if (string.IsNullOrEmpty(id) || GraphTime(e, "start") is not { } start || GraphTime(e, "end") is not { } end || end < start)
            {
                continue;
            }

            var sensitivity = Str(e, "sensitivity");
            var join = e.TryGetProperty("onlineMeeting", out var om) && om.ValueKind == JsonValueKind.Object ? LinkPolicy.Allow(Str(om, "joinUrl")) : null;
            var location = e.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object ? Cap(Str(loc, "displayName")) : string.Empty;
            list.Add(new CalendarEvent(id, Cap(Str(e, "subject")), start, end)
            {
                IsAllDay = Bool(e, "isAllDay"),
                IsCancelled = Bool(e, "isCancelled"),
                IsPrivate = sensitivity is "private" or "confidential",
                Location = location,
                JoinUrl = join,
                WebLink = LinkPolicy.Allow(Str(e, "webLink")),
            });
        }

        return list;
    }

    /// <summary>Maps an HTTP status to a provider status (and nothing else is read from an error response).</summary>
    public static ProviderStatus StatusFor(HttpStatusCode code) => code switch
    {
        HttpStatusCode.OK => ProviderStatus.Connected,
        HttpStatusCode.Unauthorized => ProviderStatus.SignInRequired,
        HttpStatusCode.Forbidden => ProviderStatus.PermissionDenied,
        HttpStatusCode.TooManyRequests => ProviderStatus.Throttled,
        _ => ProviderStatus.Unavailable,
    };

    private static DateTimeOffset? GraphTime(JsonElement e, string name)
    {
        // Graph returns {"dateTime":"2026-10-29T09:00:00.0000000","timeZone":"UTC"} when asked for UTC.
        if (!e.TryGetProperty(name, out var t) || t.ValueKind != JsonValueKind.Object || !string.Equals(Str(t, "timeZone"), "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return DateTime.TryParse(Str(t, "dateTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))
            : null;
    }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Cleans text from the service (control characters removed, length capped); it is drawn as plain text.</summary>
    private static string Cap(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var clean = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > MaxText ? clean[..MaxText] : clean;
    }

    private async Task<ProviderResult<JsonDocument>> GetJsonAsync(Uri uri, CancellationToken cancellationToken, bool utcCalendar = false)
    {
        if (uri.Host != BaseUri.Host || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Graph requests go to graph.microsoft.com over HTTPS only.");
        }

        var token = await tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            return new ProviderResult<JsonDocument>(ProviderStatus.NotConnected, null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (utcCalendar)
        {
            request.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var status = StatusFor(response.StatusCode);
            if (status != ProviderStatus.Connected)
            {
                return new ProviderResult<JsonDocument>(status, null, response.Headers.RetryAfter?.Delta ?? (status == ProviderStatus.Throttled ? TimeSpan.FromSeconds(60) : null));
            }

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                return new ProviderResult<JsonDocument>(ProviderStatus.Unavailable, null);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
            if (bytes.Length > MaxResponseBytes)
            {
                return new ProviderResult<JsonDocument>(ProviderStatus.Unavailable, null);
            }

            return new ProviderResult<JsonDocument>(ProviderStatus.Connected, JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 }));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProviderResult<JsonDocument>(ProviderStatus.Unavailable, null);
        }
    }
}
