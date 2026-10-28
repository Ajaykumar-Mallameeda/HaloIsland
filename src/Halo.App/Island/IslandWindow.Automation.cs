using Halo.App.Interop;
using Halo.Core.Accessibility;
using Halo.Core.Island;
using Halo.Core.Media;

namespace Halo.App.Island;

/// <summary>
/// UI Automation for the island (A-F01): the window builds one immutable snapshot of what is on screen and publishes it
/// to the provider, which raises the matching events. Requests from UIA threads arrive as posted messages and go through
/// the same paths as pointer input.
/// </summary>
internal sealed partial class IslandWindow
{
    /// <summary>The live summary of the island (FR-A11Y-01): what is playing, then the time and date.</summary>
    public string AccessibleName => _mediaSnapshot is { } m
        ? $"Halo, {m.AccessibleSummary}, {_clock.TimeWithDesignator}, {_clock.LongDate}"
        : $"Halo, {_clock.TimeWithDesignator}, {_clock.LongDate}";

    /// <summary>The alert text while an alert is shown; the open island's summary while expanded; otherwise null.</summary>
    public string? AccessibleDescription => State.Alert is { } alert
        ? alert.Content.Spoken
        : State.IsExpanded ? ExpandedDescription() : null;

    /// <summary>Builds and publishes the UIA snapshot. Cheap, and silent when nothing visible changed.</summary>
    private void PublishAutomation()
    {
        if (_automation is null || _layout is null || _clock is null)
        {
            return;
        }

        var state = State;
        var input = new IslandAccessibilityInput(_layout, state, _clock)
        {
            Media = _mediaSnapshot,
            Volume = _volume?.Current,
            NoAudioDevice = _volume is { HasDevice: false },
            Power = _batteryWatching ? _battery.Current : null,
            Network = _network is not null ? _networkMonitor.Current : null,
            Now = DateTimeOffset.Now,
        };
        var snapshot = new AutomationSnapshot(
            state.IsExpanded,
            AccessibleName,
            AccessibleDescription,
            _layout.ShapeFor(state.Presentation),
            _layout.Canvas.Left,
            _layout.Canvas.Top,
            state.Visible,
            IslandAccessibility.Children(input));
        var changes = _automation.Publish(snapshot);
        if (changes != default)
        {
            _log?.Write(
                "uia",
                ("expand", changes.ExpandState ? 1 : 0),
                ("name", changes.Name ? 1 : 0),
                ("structure", changes.Structure ? 1 : 0),
                ("live", changes.LiveRegion ? 1 : 0),
                ("children", snapshot.Children.Count));
        }
    }

    /// <summary>
    /// A request from a UIA client, on the UI thread. Each one is checked against the current state, so a request for a
    /// control that has since gone (the session changed, the island collapsed) is dropped as stale.
    /// </summary>
    private void OnAutomationRequest(AutomationRequest request, int value)
    {
        switch (request)
        {
            case AutomationRequest.Expand:
                Apply(IslandInput.Expand, "automation");
                return;
            case AutomationRequest.Collapse:
                Apply(IslandInput.Collapse, "automation");
                return;
            case AutomationRequest.Toggle:
                Apply(IslandInput.Toggle, "automation");
                return;
        }

        var token = request switch
        {
            AutomationRequest.MediaPrevious => "previous",
            AutomationRequest.MediaPlayPause => "play_pause",
            AutomationRequest.MediaNext => "next",
            AutomationRequest.ToggleMute => "mute",
            AutomationRequest.SetVolume => "set",
            _ => "unknown",
        };
        if (!State.IsExpanded || !State.Visible)
        {
            _log?.Write("uia.request", ("request", token), ("result", "stale"));
            return;
        }

        switch (request)
        {
            case AutomationRequest.MediaPrevious when _mediaSnapshot?.CanPrevious == true:
                RunMediaCommand(MediaCommand.Previous, _media?.Selected.Id);
                break;
            case AutomationRequest.MediaPlayPause when _mediaSnapshot?.CanPlayPause == true:
                RunMediaCommand(MediaCommand.PlayPause, _media?.Selected.Id);
                break;
            case AutomationRequest.MediaNext when _mediaSnapshot?.CanNext == true:
                RunMediaCommand(MediaCommand.Next, _media?.Selected.Id);
                break;
            case AutomationRequest.ToggleMute when _volume?.Current is { } now:
                var muted = _volume.SetMuted(!now.Muted);
                _log?.Write("volume.command", ("command", now.Muted ? "unmute" : "mute"), ("result", muted ? "accepted" : "failed"), ("source", "automation"));
                break;
            case AutomationRequest.SetVolume when _volume?.HasDevice == true && value is >= 0 and <= 100:
                var ok = _volume.SetLevel(value / 100f);
                _log?.Write("volume.command", ("command", "set"), ("percent", value), ("result", ok ? "accepted" : "failed"), ("source", "automation"));
                break;
            default:
                _log?.Write("uia.request", ("request", token), ("result", "stale"));
                return;
        }

        _log?.Write("uia.request", ("request", token), ("result", "sent"));
    }
}
