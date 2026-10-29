using Halo.Core.Activities;
using Halo.Core.Shelf;
using Halo.Core.Timers;

namespace Halo.App.Island;

/// <summary>
/// The controls inside the pages (Visual Experience 2.0): Halo's own features live in the island itself, not only in
/// the tray menu. One list (<see cref="PageControls"/>) draws the chips, hit-tests the pointer and builds the UI
/// Automation buttons, and every command goes through the same paths as the tray menu.
/// </summary>
internal sealed partial class IslandWindow
{
    /// <summary>The state the page controls are built from. Task text is only included while previews are allowed.</summary>
    private PageContext PageContextNow() => new()
    {
        Activity = AccessibleActivity(),
        QuietOn = _quietMode.IsActive(DateTimeOffset.UtcNow),
        TaskRows = ShowPrivate ? [.. _tasks.Pending.Take(PageControls.MaxTaskRows).Select(t => (t.Id, t.Text))] : [],
        ShelfTiles = [.. _shelf.Items.Take(PageControls.MaxShelfTiles).Select(i => (i.Id, ShelfName(i)))],
        PageCount = Pages.Count,
    };

    /// <summary>The page's controls for the current state.</summary>
    private IReadOnlyList<PageControl> CurrentControls() => PageControls.For(_layout, CurrentPage, PageContextNow());

    /// <summary>A short name for a shelf tile: the file name, or the first line of text.</summary>
    private static string ShelfName(ShelfItem item) => item.Kind == ShelfKind.File
        ? Path.GetFileName(item.Value.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : item.Value
        : item.Label;

    /// <summary>Tasks done today (local date) and still pending, for the to-do page's readout.</summary>
    private int TasksDoneToday()
    {
        var today = DateTimeOffset.Now.Date;
        return _tasks.Items.Count(t => t.Done && t.CompletedUtc is { } done && done.ToLocalTime().Date == today);
    }

    /// <summary>
    /// Puts the current tasks, shelf and page controls on screen. Called whenever the page or what it shows changes;
    /// each part redraws only what differs.
    /// </summary>
    private void RefreshPageContent()
    {
        if (_visuals is null || _layout is null)
        {
            return;
        }

        var rows = ShowPrivate ? _tasks.Pending.Take(PageControls.MaxTaskRows).Select(t => (t.Id, t.Text)).ToList() : [];
        _visuals.ShowTasks(new TasksView(TasksDoneToday(), _tasks.PendingCount, !ShowPrivate && _tasks.PendingCount > 0, rows));
        _visuals.ShowShelf(new ShelfView(_shelf.Items.Count, [.. _shelf.Items.Take(PageControls.MaxShelfTiles).Select(i => (i.Id, ShelfName(i), i.Kind == ShelfKind.File))]));
        _visuals.ShowControls(CurrentControls(), IslandPages.ToneOf(CurrentPage, AccessibleActivity()));
    }

    /// <summary>The page control under a client point while the island is open, or null.</summary>
    private PageControl? PageControlAt(int x, int y) =>
        State.IsExpanded && State.Visible ? PageControls.HitTest(CurrentControls(), x, y) : null;

    /// <summary>Runs a page control (pointer or UI Automation). Every command uses the same path as the tray menu.</summary>
    private void RunPageControl(PageControl control, string source)
    {
        _log?.Write("page.control", ("command", control.Command.ToString().ToLowerInvariant()), ("source", source));
        switch (control.Command)
        {
            case PageCommand.PreviousPage:
                StepPage(-1, source);
                return;
            case PageCommand.NextPage:
                StepPage(1, source);
                return;
            case PageCommand.ActivityAction:
                RunPrimaryAction(control.Argument, source);
                return;
            case PageCommand.StartFocus:
                StartFocus(FocusPlan.Default, source);
                return;
            case PageCommand.StartBreak or PageCommand.StartTimer:
                StartTimer(TimeSpan.FromMinutes(control.Argument), source);
                return;
            case PageCommand.ToggleQuiet:
                SetQuiet(_quietMode.IsActive(DateTimeOffset.UtcNow) ? QuietMode.Off : QuietMode.Start(QuietKind.UntilTurnedOff, DateTimeOffset.UtcNow, TimeZoneInfo.Local), source);
                return;
            case PageCommand.OpenSettings:
                _settingsWindow = SettingsWindow.Open(this, _settingsWindow);
                _log?.Write("settings.window", ("state", "open"));
                return;
            case PageCommand.AddTask or PageCommand.OpenTasks:
                // Typing needs a focusable window; the island itself never takes focus (FR-ISL-06).
                OpenTasksWindow();
                return;
            case PageCommand.CompleteTask when control.Key is { } taskId:
                _ = CompleteTask(taskId);
                RefreshPageContent();
                PublishAutomation();
                return;
            case PageCommand.PinClipboard:
                _ = PinClipboard(_hwnd);
                RefreshPageContent();
                PublishAutomation();
                return;
            case PageCommand.OpenShelf:
                OpenShelfWindow();
                return;
            case PageCommand.CopyShelfItem when control.Key is { } itemId && _shelf.Find(itemId) is { } item:
                var ok = item.Kind == ShelfKind.File ? Interop.Clipboard.SetFiles(_hwnd, [item.Value]) : Interop.Clipboard.SetText(_hwnd, item.Value);
                LogShelf("copy", ok, item.Kind);
                RaiseAlert(new Halo.Core.Island.IslandAlert("shelf.copy", Halo.Core.Island.AlertKind.Halo, Halo.Core.Island.AlertPriority.Normal, new Halo.Core.Island.AlertContent(ok ? "" : "", ok ? "Copied" : "The clipboard is busy") { Tone = Halo.Core.Theming.Tone.Shelf }, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3)), source);
                return;
        }
    }

    /// <summary>A page control requested through UI Automation, by its index for the current state (stale ones are dropped).</summary>
    private void RunPageControlAt(int index, string source)
    {
        var controls = CurrentControls();
        if (!State.IsExpanded || !State.Visible || index < 0 || index >= controls.Count)
        {
            _log?.Write("uia.request", ("request", "page_control"), ("result", "stale"));
            return;
        }

        RunPageControl(controls[index], source);
    }
}
