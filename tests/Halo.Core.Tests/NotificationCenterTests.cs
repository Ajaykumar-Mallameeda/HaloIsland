using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Notifications;
using Xunit;

namespace Halo.Core.Tests;

/// <summary>D-F01/D-F03/D-F04: ingestion, deduplication, policy, rate limiting, expiry, dismissal and actions.</summary>
public class NotificationCenterTests
{
    private const string Mail = "test.mail";
    private readonly ManualClock _clock = new();

    [Fact]
    public void ANotificationBecomesAPrivateActivityAndAnArrivalAlert()
    {
        var center = Center();
        var outcome = center.Submit(N("m1"));

        Assert.Equal(SubmitResult.Shown, outcome.Result);
        var a = outcome.Activity!;
        Assert.Equal("note:test.mail/m1", a.Id);
        Assert.Equal((ActivityKind.Notification, ActivityPrivacy.Private, false), (a.Kind, a.Privacy, a.Ongoing));
        Assert.Equal("New email", a.CompactFor(showPrivate: false));
        Assert.Equal("New email", a.Accessible); // previews off: nothing private reaches the screen reader either
        Assert.Equal(string.Empty, a.Subtitle);
        Assert.Equal(_clock.GetUtcNow() + ProviderPolicy.Default.Lifetime, a.ExpiresAt);
        Assert.Equal("dismiss", a.Actions[^1].Id);

        var alert = outcome.Alert!;
        Assert.Equal(("New email", AlertKind.Notification, "note.test.mail"), (alert.Content.Text, alert.Kind, alert.Key));
        Assert.False(alert.AllowedWhenSuppressed);
        Assert.False(alert.AllowedInQuietMode);
    }

    [Fact]
    public void PreviewsShowOnlyWhenTheProviderAllowsThem()
    {
        var center = Center(ProviderPolicy.Default with { ShowPreview = true });
        var outcome = center.Submit(N("m1"));
        Assert.Equal("Ada Lovelace", outcome.Alert!.Content.Text);
        Assert.Equal("Quarterly notes", outcome.Activity!.Subtitle);
        Assert.Equal(ActivityPrivacy.Public, outcome.Activity.Privacy);
        Assert.Contains("Quarterly notes", outcome.Alert.Content.Spoken, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameItemIsShownOnceEvenIfReportedAgain()
    {
        var center = Center();
        Assert.Equal(SubmitResult.Shown, center.Submit(N("m1")).Result);
        Assert.Equal(SubmitResult.Duplicate, center.Submit(N("m1")).Result); // e.g. a push and a poll of the same email
        Assert.Equal(SubmitResult.Shown, center.Submit(N("m2")).Result);
        center.Dismiss(Mail, "m1");
        Assert.Equal(SubmitResult.Duplicate, center.Submit(N("m1")).Result); // dismissed stays dismissed
    }

    [Fact]
    public void DisabledUnknownAndLowPriorityAreNotShown()
    {
        var center = Center(ProviderPolicy.Default with { MinPriority = AlertPriority.High });
        Assert.Equal(SubmitResult.Filtered, center.Submit(N("m1")).Result);
        Assert.Equal(SubmitResult.Disabled, center.Submit(N("x") with { ProviderId = "unknown" }).Result);
        center.Register(Mail, ProviderPolicy.Default with { Enabled = false });
        Assert.Equal(SubmitResult.Disabled, center.Submit(N("m3")).Result);
        Assert.Equal(0, center.LiveCount);
    }

    [Fact]
    public void ABurstIsRateLimitedIntoTheIslandWithoutAFloodOfAlerts()
    {
        var center = Center(ProviderPolicy.Default with { MaxPerMinute = 3 });
        var results = Enumerable.Range(0, 20).Select(i => center.Submit(N($"b{i}")).Result).ToList();
        Assert.Equal(3, results.Count(r => r == SubmitResult.Shown));
        Assert.Equal(17, results.Count(r => r == SubmitResult.Summarised));
        Assert.Equal(20, center.LiveCount);

        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(SubmitResult.Shown, center.Submit(N("later")).Result);
    }

    [Fact]
    public void ActionsAreRoutedToTheProviderAndARejectionIsNotReportedAsDone()
    {
        var calls = new List<(string, string)>();
        var center = new NotificationCenter(_clock);
        center.Register(Mail, ProviderPolicy.Default, (source, action) =>
        {
            calls.Add((source, action));
            return source == "ok" ? ActionOutcome.Done : ActionOutcome.Rejected;
        });
        var open = new ActivityAction("open", "Open in Outlook", "o");
        var ok = center.Submit(N("ok") with { Actions = [open] }).Activity!;
        var bad = center.Submit(N("bad") with { Actions = [open] }).Activity!;

        Assert.Equal(["open", "dismiss"], ok.Actions.Select(a => a.Id));
        Assert.Equal(ActionOutcome.Done, center.RunAction(ok.Id, "open"));
        Assert.Equal(ActionOutcome.Rejected, center.RunAction(bad.Id, "open"));
        Assert.Equal(ActionOutcome.Unsupported, center.RunAction(bad.Id, "delete")); // not offered: never sent
        Assert.Equal([("ok", "open"), ("bad", "open")], calls);
        Assert.Equal(ActionOutcome.Gone, center.RunAction(ok.Id, "open")); // done removes it
        Assert.Equal(ActionOutcome.Done, center.RunAction(bad.Id, "dismiss"));
        Assert.Equal(0, center.LiveCount);
        Assert.Equal(ActionOutcome.Gone, center.RunAction("timer", "dismiss"));
    }

    [Fact]
    public void ExpiryAndDisconnectForgetLiveNotifications()
    {
        var center = Center();
        var a = center.Submit(N("a")).Activity!;
        center.Submit(N("b"));
        center.Expire([a.Id, "timer"]);
        Assert.Equal(1, center.LiveCount);
        Assert.Equal(["note:test.mail/b"], center.DropProvider(Mail));
        Assert.Equal(0, center.LiveCount);
    }

    [Fact]
    public void PolicyDecidesFullScreenAndQuietModeEligibility()
    {
        var center = Center(ProviderPolicy.Default with { AllowedOverFullscreen = true, AllowedInQuietMode = true });
        var o = center.Submit(N("r1"));
        Assert.True(o.Alert!.AllowedWhenSuppressed);
        Assert.True(o.Alert.AllowedInQuietMode);
        Assert.True(o.Activity!.AllowedWhenSuppressed);
        Assert.True(o.Activity.AllowedInQuietMode);
    }

    [Fact]
    public void TheDeduplicationMemoryIsBounded()
    {
        var center = Center(ProviderPolicy.Default with { MaxPerMinute = 10_000 });
        for (var i = 0; i < NotificationCenter.MaxRemembered * 3; i++)
        {
            center.Submit(N($"n{i}"));
            center.Dismiss(Mail, $"n{i}");
        }

        // Bounded: recent items are still remembered, so the last one is a duplicate.
        Assert.Equal(SubmitResult.Duplicate, center.Submit(N($"n{(NotificationCenter.MaxRemembered * 3) - 1}")).Result);
    }

    private NotificationCenter Center(ProviderPolicy? policy = null)
    {
        var center = new NotificationCenter(_clock);
        center.Register(Mail, policy ?? ProviderPolicy.Default);
        return center;
    }

    private static HaloNotification N(string id) => new(Mail, id, AlertPriority.Normal)
    {
        Title = "Ada Lovelace",
        Preview = "Quarterly notes",
        Redacted = "New email",
    };
}
