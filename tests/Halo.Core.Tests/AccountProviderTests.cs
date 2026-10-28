using System.Net;
using System.Text;
using Halo.Core.Accounts;
using Halo.Core.Activities;
using Halo.Core.Notifications;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>
/// E-F01–E-F04 and F-F01–F-F03 with test doubles: the Graph adapter against fixtures shaped like the documented
/// responses (served by a fake HTTP handler — no network), error mapping, mail deduplication and backoff, and the
/// calendar glance and reminders. Live use needs an app registration and the owner's sign-in (BLOCKED).
/// </summary>
public class AccountProviderTests
{
    private const string Inbox = """{ "unreadItemCount": 3 }""";
    private const string Messages = """
        { "value": [
          { "id": "AAMk1", "subject": "Quarterly notes", "receivedDateTime": "2026-10-29T08:15:00Z",
            "from": { "emailAddress": { "name": "Ada Lovelace", "address": "ada@example.com" } },
            "webLink": "https://outlook.office365.com/owa/?ItemID=AAMk1" },
          { "id": "AAMk2", "subject": "Lunch?\u0007", "receivedDateTime": "2026-10-29T08:20:00Z",
            "from": { "emailAddress": { "address": "bob@example.com" } },
            "webLink": "https://evil.example.com/phish" },
          { "subject": "no id" }
        ] }
        """;

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 8, 50, 0, TimeSpan.Zero);

    private static string Events(string extra = "") => $$"""
        { "value": [
          { "id": "E1", "subject": "Design review", "isAllDay": false, "isCancelled": false, "sensitivity": "normal",
            "start": { "dateTime": "2026-10-29T09:00:00.0000000", "timeZone": "UTC" },
            "end": { "dateTime": "2026-10-29T09:30:00.0000000", "timeZone": "UTC" },
            "location": { "displayName": "Room 4" },
            "isOnlineMeeting": true, "onlineMeeting": { "joinUrl": "https://teams.microsoft.com/l/meetup-join/abc" },
            "webLink": "https://outlook.office365.com/owa/?itemid=E1" },
          { "id": "E2", "subject": "Doctor", "sensitivity": "private",
            "start": { "dateTime": "2026-10-29T12:00:00.0000000", "timeZone": "UTC" },
            "end": { "dateTime": "2026-10-29T13:00:00.0000000", "timeZone": "UTC" } },
          { "id": "E3", "subject": "Holiday", "isAllDay": true,
            "start": { "dateTime": "2026-10-29T00:00:00.0000000", "timeZone": "UTC" },
            "end": { "dateTime": "2026-10-30T00:00:00.0000000", "timeZone": "UTC" } }{{extra}}
        ] }
        """;

    [Fact]
    public async Task TheGraphAdapterReadsOnlyTheNeededFieldsWithLeastPrivilege()
    {
        var http = new FakeHttp(req => req.RequestUri!.AbsolutePath.EndsWith("/inbox", StringComparison.Ordinal) ? (HttpStatusCode.OK, Inbox) : (HttpStatusCode.OK, Messages));
        using var invoker = new HttpMessageInvoker(http);
        var graph = new GraphClient(invoker, new FixedToken("t0ken"));

        var result = await graph.GetInboxAsync(CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal(3, result.Value!.UnreadCount);
        Assert.Equal(2, result.Value.Unread.Count); // the entry without an ID is skipped
        var ada = result.Value.Unread[0];
        Assert.Equal(("Ada Lovelace", "Quarterly notes"), (ada.SenderName, ada.Subject));
        Assert.NotNull(ada.WebLink);
        var bob = result.Value.Unread[1];
        Assert.Equal(("bob@example.com", "Lunch?"), (bob.SenderName, bob.Subject)); // control character removed
        Assert.Null(bob.WebLink); // not an Outlook host: never opened

        Assert.All(http.Requests, r =>
        {
            Assert.Equal("graph.microsoft.com", r.RequestUri!.Host);
            Assert.Equal(HttpMethod.Get, r.Method); // read-only
            Assert.Equal("Bearer t0ken", r.Headers.Authorization!.ToString());
        });
        Assert.Contains("$select=id,subject,from,receivedDateTime,webLink", Uri.UnescapeDataString(http.Requests[1].RequestUri!.Query), StringComparison.Ordinal);
        Assert.DoesNotContain("body", http.Requests[1].RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["User.Read", "Mail.ReadBasic", "Calendars.ReadBasic", "offline_access"], GraphClient.RequiredScopes);
    }

    [Fact]
    public async Task WithoutAnAccountNothingIsRequested()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.OK, Inbox));
        using var invoker = new HttpMessageInvoker(http);
        var graph = new GraphClient(invoker, new NotConnectedTokenSource());
        var result = await graph.GetInboxAsync(CancellationToken.None);
        Assert.Equal(ProviderStatus.NotConnected, result.Status);
        Assert.Empty(http.Requests);
        Assert.Null(PollSchedule.Next(result.Status, 0, null)); // and nothing is scheduled
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderStatus.SignInRequired)]
    [InlineData(HttpStatusCode.Forbidden, ProviderStatus.PermissionDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderStatus.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderStatus.Unavailable)]
    public async Task ErrorsBecomeStatusesNotExceptions(HttpStatusCode code, ProviderStatus expected)
    {
        using var invoker = new HttpMessageInvoker(new FakeHttp(_ => (code, "{ \"error\": { \"message\": \"secret detail\" } }")));
        var result = await new GraphClient(invoker, new FixedToken("t")).GetInboxAsync(CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task NetworkFailureIsUnavailableAndBacksOff()
    {
        using var invoker = new HttpMessageInvoker(new FakeHttp(_ => throw new HttpRequestException("offline")));
        var result = await new GraphClient(invoker, new FixedToken("t")).GetInboxAsync(CancellationToken.None);
        Assert.Equal(ProviderStatus.Unavailable, result.Status);
        Assert.Equal(TimeSpan.FromMinutes(1), PollSchedule.Next(ProviderStatus.Unavailable, 0, null));
        Assert.Equal(TimeSpan.FromMinutes(8), PollSchedule.Next(ProviderStatus.Unavailable, 3, null));
        Assert.Equal(PollSchedule.MaxBackoff, PollSchedule.Next(ProviderStatus.Unavailable, 9, null));
        Assert.Equal(TimeSpan.FromSeconds(90), PollSchedule.Next(ProviderStatus.Throttled, 0, TimeSpan.FromSeconds(90)));
        Assert.Equal(PollSchedule.Normal, PollSchedule.Next(ProviderStatus.Connected, 0, null));
        Assert.Null(PollSchedule.Next(ProviderStatus.PermissionDenied, 0, null));
    }

    [Fact]
    public void ConnectingDoesNotFloodAndEachNewEmailIsShownOnce()
    {
        var center = new NotificationCenter(new ManualClock());
        center.Register(GraphClient.MailProviderId, ProviderPolicy.Default);
        var watcher = new MailWatcher(center);
        var first = Mail(("m1", 0), ("m2", 1));

        Assert.Empty(watcher.Apply(first)); // baseline: already unread, counted not announced
        Assert.Equal("2 unread", watcher.UnreadActivity()!.CompactText);

        var second = Mail(("m1", 0), ("m2", 1), ("m3", 2));
        var shown = watcher.Apply(second);
        var note = Assert.Single(shown);
        Assert.Equal(SubmitResult.Shown, note.Result);
        Assert.Equal("New email", note.Alert!.Content.Text); // previews off by default
        Assert.Equal("open", note.Activity!.Actions[0].Id);

        Assert.Empty(watcher.Apply(second)); // the same poll again: nothing new
        Assert.Empty(watcher.Apply(new ProviderResult<MailSnapshot>(ProviderStatus.Unavailable, null)));
        Assert.Equal(1, watcher.ConsecutiveFailures);
        Assert.Null(new MailWatcher(center).UnreadActivity());
    }

    [Fact]
    public async Task CalendarEventsAreReadInUtcAndPrivateOnesNeverShowTheirSubject()
    {
        var http = new FakeHttp(_ => (HttpStatusCode.OK, Events()));
        using var invoker = new HttpMessageInvoker(http);
        var result = await new GraphClient(invoker, new FixedToken("t")).GetEventsAsync(Now, Now.AddHours(12), CancellationToken.None);
        Assert.True(result.Ok);
        Assert.Equal("outlook.timezone=\"UTC\"", http.Requests[0].Headers.GetValues("Prefer").Single());
        var events = result.Value!;
        Assert.Equal(3, events.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero), events[0].StartUtc);
        Assert.NotNull(events[0].JoinUrl);
        Assert.True(events[1].IsPrivate);
        Assert.True(events[2].IsAllDay);

        var watcher = new CalendarWatcher(new NotificationCenter());
        watcher.Apply(result);
        var glance = watcher.GlanceActivity(Now, TimeZoneInfo.Utc, showPreview: true)!;
        Assert.Equal(("In 10 min", "Design review"), (glance.CompactText, glance.Title));
        Assert.Contains("Room 4", glance.Subtitle, StringComparison.Ordinal);
        Assert.Equal(["join", "open"], glance.Actions.Select(a => a.Id));
        Assert.Equal("Next meeting", watcher.GlanceActivity(Now, TimeZoneInfo.Utc, showPreview: false)!.Title);

        var later = watcher.GlanceActivity(Now.AddHours(2.5), TimeZoneInfo.Utc, showPreview: true)!; // 11:20, before the private 12:00
        Assert.Equal("Next meeting", later.Title); // the private appointment: never its subject
        Assert.Contains("private appointment", later.Accessible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Doctor", later.Accessible, StringComparison.Ordinal);

        Assert.Equal("Meeting now", watcher.GlanceActivity(Now.AddMinutes(15), TimeZoneInfo.Utc, true)!.CompactText);
    }

    [Fact]
    public void MeetingRemindersComeOnceAndFollowMovesAndCancellations()
    {
        var center = new NotificationCenter(new ManualClock());
        center.Register(GraphClient.CalendarProviderId, ProviderPolicy.Default);
        var watcher = new CalendarWatcher(center) { Lead = TimeSpan.FromMinutes(5) };
        var start = Now.AddMinutes(10);
        watcher.Apply(Cal(new CalendarEvent("E1", "Standup", start, start.AddMinutes(15))));

        Assert.Empty(watcher.DueReminders(Now)); // 10 minutes before: not yet
        Assert.Equal(start.AddMinutes(-5), watcher.NextChange(Now));
        var due = Assert.Single(watcher.DueReminders(Now.AddMinutes(5)));
        Assert.Equal("Meeting in 5 min", due.Alert!.Content.Text);
        Assert.Empty(watcher.DueReminders(Now.AddMinutes(6))); // once

        // Moved by 30 minutes: the old reminder is withdrawn and a new one comes at the new time.
        var moved = new CalendarEvent("E1", "Standup", start.AddMinutes(30), start.AddMinutes(45));
        var withdrawn = watcher.Apply(Cal(moved));
        Assert.Single(withdrawn);
        Assert.Single(watcher.DueReminders(moved.StartUtc.AddMinutes(-5)));

        // Cancelled: withdrawn, never reminded again.
        Assert.Single(watcher.Apply(Cal(moved with { IsCancelled = true })));
        Assert.Empty(watcher.DueReminders(moved.StartUtc));
        Assert.Null(watcher.GlanceActivity(moved.StartUtc, TimeZoneInfo.Utc, true));
    }

    [Fact]
    public void OnlyHttpsLinksOnMicrosoftHostsAreAllowed()
    {
        Assert.NotNull(LinkPolicy.Allow("https://teams.microsoft.com/l/meetup-join/x"));
        Assert.NotNull(LinkPolicy.Allow("https://outlook.office.com/mail/inbox"));
        Assert.Null(LinkPolicy.Allow("http://teams.microsoft.com/x"));
        Assert.Null(LinkPolicy.Allow("https://teams.microsoft.com.evil.example/x"));
        Assert.Null(LinkPolicy.Allow("https://user:pw@outlook.office.com/"));
        Assert.Null(LinkPolicy.Allow("file:///C:/Windows/system32/calc.exe"));
        Assert.Null(LinkPolicy.Allow("javascript:alert(1)"));
        Assert.Null(LinkPolicy.Allow(null));
    }

    private static ProviderResult<MailSnapshot> Mail(params (string Id, int Minute)[] items) =>
        new(ProviderStatus.Connected, new MailSnapshot(items.Length, items.Select(i => new MailSummary(i.Id, "Sender", "Subject", Now.AddMinutes(i.Minute), LinkPolicy.Allow("https://outlook.office.com/mail/" + i.Id))).ToList()));

    private static ProviderResult<IReadOnlyList<CalendarEvent>> Cal(params CalendarEvent[] events) => new(ProviderStatus.Connected, events);

    private sealed class FixedToken(string token) : IAccessTokenSource
    {
        public IReadOnlyList<string> Scopes => GraphClient.RequiredScopes;

        public Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(token);

        public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeHttp(Func<HttpRequestMessage, (HttpStatusCode Code, string Body)> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (code, body) = respond(request);
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
