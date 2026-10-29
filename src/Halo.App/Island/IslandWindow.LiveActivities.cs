using Halo.Core.Activities;
using Halo.Core.Island;
using Halo.Core.Media;
using Halo.Core.Network;
using Halo.Core.Notifications;
using Halo.Core.Power;
using static Halo.App.Interop.Native;

namespace Halo.App.Island;

/// <summary>
/// Live Activity Engine 2.0 in the window: the board's decisions reach the island here, and only here.
/// <list type="bullet">
/// <item>Providers (timer, focus, media, charging, network, camera, reminders) publish activities to the board on the UI
/// thread; none of them touches geometry or visuals.</item>
/// <item><see cref="PublishBoard"/> turns the board into presentation: whether something is live (line or pill), what
/// leads the pill, the activity page, and the detached bubble for the second activity.</item>
/// <item>The bubble is a real control: a click makes its activity the user's selection (it swaps with the pill).</item>
/// </list>
/// </summary>
internal sealed partial class IslandWindow
{
    private const nuint RotationTimerId = 20;

    /// <summary>Rotation step between tied activities when the user turned rotation on.</summary>
    private static readonly TimeSpan RotationInterval = TimeSpan.FromSeconds(8);

    private DateTimeOffset? _pluggedAt;
    private bool _bubblePressed;
    private string? _publishedLead;
    private string? _publishedSecondary;

    /// <summary>The activity shown on the activity page and offering actions: the primary unless it is media (media has its own page).</summary>
    private IslandActivity? PageActivity => _board.Primary is { Kind: not ActivityKind.Media } primary ? primary : _board.First(a => a.Kind != ActivityKind.Media);

    /// <summary>Further activities counted as "+N" in the pill: the one in the detached bubble is already visible.</summary>
    private int PillOthers => Math.Max(0, _board.OthersCount - (_board.Secondary is null ? 0 : 1));

    /// <summary>The board, for tests.</summary>
    internal ActivityBoard Board => _board;

    /// <summary>
    /// Shows what the board decided. Called after every board change; cheap when nothing changed (the visuals compare
    /// what they already show).
    /// </summary>
    private void PublishBoard(string reason)
    {
        var lead = _board.Primary;
        var secondary = _board.Secondary;
        if (!string.Equals(lead?.Id, _publishedLead, StringComparison.Ordinal) || !string.Equals(secondary?.Id, _publishedSecondary, StringComparison.Ordinal))
        {
            (_publishedLead, _publishedSecondary) = (lead?.Id, secondary?.Id);

            // IDs and provider tokens only (never content).
            _log?.Write(
                "live",
                ("lead", lead is null ? "none" : ActivityBoard.ProviderOf(lead)),
                ("secondary", secondary is null ? "none" : ActivityBoard.ProviderOf(secondary)),
                ("count", _board.Count),
                ("selected", _board.SelectedId is null ? 0 : 1),
                ("reason", reason));
        }

        _visuals?.ShowActivity(PageActivity, PillOthers, ShowPrivate, _reducedMotion);
        _visuals?.ShowLead(lead is null ? CompactLead.None : lead.Kind == ActivityKind.Media ? CompactLead.Media : CompactLead.Activity);
        Present(_controller.SetLive(lead is not null), "live");
        ShowSecondary();
        ArmRotation();
    }

    /// <summary>The detached bubble follows the compact presentation; the input region follows the bubble.</summary>
    private void ShowSecondary()
    {
        if (_visuals is null)
        {
            return;
        }

        var was = _visuals.SecondaryShown;
        _visuals.ShowSecondary(_board.Secondary, State.Presentation == IslandPresentation.Compact && State.Visible, _reducedMotion);
        if (was != _visuals.SecondaryShown && _motion.ActiveCount == 0)
        {
            ApplyRegion(union: false);
        }
    }

    /// <summary>The input region: the current presentation (or, during a transition, everything it may pass through) and the bubble.</summary>
    private void ApplyRegion(bool union)
    {
        static (int, int, int, int, int) Box(RoundedBox r) =>
            ((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom), (int)Math.Round(r.Radius));
        if (union)
        {
            // The open island contains every other shape; it is widened by the opening overshoot so that never clips.
            var e = _layout.Expanded.Reach();
            var grow = Math.Ceiling(_layout.Expanded.Width * Halo.Core.Motion.MotionTiming.OpenOvershoot);
            SetRoundedRegion(_hwnd, Box(e with { Left = e.Left - grow, Width = e.Width + (2 * grow), Height = e.Height + grow }), Box(_layout.Minimal));
            return;
        }

        var shape = Box(_layout.RegionFor(State.Presentation));
        if (_visuals?.SecondaryShown == true)
        {
            SetRoundedRegion(_hwnd, shape, Box(_layout.Minimal));
        }
        else
        {
            SetRoundedRegion(_hwnd, shape);
        }
    }

    /// <summary>True when a client point is on the shown bubble.</summary>
    private bool BubbleAt(int x, int y) => _visuals?.SecondaryShown == true && _layout.Minimal.Contains(x, y);

    /// <summary>A completed click on the bubble: its activity becomes the user's selection and moves into the island.</summary>
    private void SwapToSecondary(string source)
    {
        if (_board.Secondary is not { } next)
        {
            return;
        }

        var update = _board.Select(next.Id);
        _log?.Write("live.select", ("provider", ActivityBoard.ProviderOf(next)), ("changed", update.PrimaryChanged ? 1 : 0), ("source", source));
        _visuals?.Arrive(Halo.Core.Motion.MotionPlanner.Arrival(_reducedMotion));
        PublishBoard("select");
        ApplyPage(animate: false);
        PublishAutomation();
    }

    /// <summary>Dismisses the activity in the pill (island menu). Non-dismissible ones (camera) are not offered.</summary>
    private void DismissPrimary(string source)
    {
        if (_board.Primary is not { Dismissible: true } primary)
        {
            return;
        }

        var provider = ActivityBoard.ProviderOf(primary);
        _ = _board.Dismiss(primary.Id);
        _log?.Write("live.dismiss", ("provider", provider), ("source", source));
        PublishBoard("dismiss");
        ApplyPage(animate: false);
        PublishAutomation();
    }

    /// <summary>The playing media session as an activity (media page and art stay with the media watcher).</summary>
    private void PublishMedia(MediaSnapshot? snapshot)
    {
        Put(MediaActivities.ActivityId, MediaActivities.For(snapshot, _settings.PillShowsMedia));
        PublishBoard("media");
    }

    /// <summary>Charging: a short live activity from the moment the charger is connected.</summary>
    private void PublishPower(PowerReading? previous, PowerReading reading)
    {
        if (!_settings.ShowBattery)
        {
            _pluggedAt = null;
        }
        else if (reading.Charging && previous is { Charging: false })
        {
            _pluggedAt = DateTimeOffset.UtcNow;
        }
        else if (!reading.Charging)
        {
            _pluggedAt = null;
        }

        Put(PowerActivities.ChargingId, PowerActivities.Charging(reading, _pluggedAt));
        _ = _board.RemoveExpired();
        PublishBoard("power");
        ArmActivityTimers();
    }

    /// <summary>Lost connectivity: an urgent, dismissible activity until the connection returns.</summary>
    private void PublishNetwork(NetworkReading reading)
    {
        Put(NetworkActivities.OfflineId, _settings.ShowNetwork ? NetworkActivities.For(reading) : null);
        PublishBoard("network");
    }

    /// <summary>The rotation timer: armed only while rotation is on and activities tie for the island.</summary>
    private void ArmRotation()
    {
        _board.RotationEnabled = _settings.RotateActivities;
        if (_board.CanRotate && State.Visible && !State.IsExpanded)
        {
            _ = SetTimer(_hwnd, RotationTimerId, (uint)RotationInterval.TotalMilliseconds, 0);
        }
        else
        {
            _ = KillTimer(_hwnd, RotationTimerId);
        }
    }

    /// <summary>The state of Windows notification ingestion, for the settings window and diagnostics.</summary>
    internal NotificationAccess NotificationAccess { get; private set; } = NotificationAccess.Off;

    /// <summary>
    /// Phase 5: Windows notification ingestion is not opted in (no setting yet) and the package does not declare the
    /// userNotificationListener capability, so nothing is requested or read. Only the side-effect-free checks run, and
    /// the result is logged as a token. See docs/LIVE_ACTIVITY_ENGINE_2_DESIGN.md §7.
    /// </summary>
    private void ProbeNotificationAccess()
    {
        var identity = HasPackageIdentity();
        NotificationAccess = WindowsNotifications.Evaluate(optedIn: false, identity, capabilityDeclared: false, windowsStatus: null);
        _log?.Write("notifications.listener", ("access", NotificationAccess.ToString().ToLowerInvariant()), ("package_identity", identity ? 1 : 0));
    }

    /// <summary>Window messages of the live-activity engine; null when not one of them.</summary>
    private nint? HandleLiveMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WmTimer when (nuint)wParam == RotationTimerId:
                if (_board.Rotate().PrimaryChanged)
                {
                    PublishBoard("rotate");
                }
                else
                {
                    ArmRotation();
                }

                return 0;
            case WmLButtonDown when BubbleAt(LowWord(lParam), HighWord(lParam)):
                _bubblePressed = true;
                _ = SetCapture(_hwnd);
                return 0;
            case WmLButtonUp when _bubblePressed:
                _bubblePressed = false;
                _ = ReleaseCapture();
                if (BubbleAt(LowWord(lParam), HighWord(lParam)))
                {
                    SwapToSecondary("pointer");
                }

                return 0;
            case WmCaptureChanged when _bubblePressed:
                _bubblePressed = false;
                return 0;
            default:
                return null;
        }
    }
}
