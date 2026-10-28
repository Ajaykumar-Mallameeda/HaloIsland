using Halo.Core.Island;
using Halo.Core.Shell;
using Xunit;

namespace Halo.Core.Tests;

public class FullscreenRuleTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void OrdinaryWindowIsNotFullscreen() =>
        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(App(new PixelRect(200, 150, 800, 600))));

    [Fact]
    public void MaximisedWindowWithTitleBarIsNotFullscreenEvenWhenItCoversTheMonitor()
    {
        // With an auto-hide taskbar a maximised window spills past every monitor edge.
        var probe = App(new PixelRect(-8, -8, 1936, 1096)) with { MaximizedWithCaption = true };

        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(probe));
    }

    [Fact]
    public void MaximisedWindowAboveTheTaskbarIsNotFullscreen() =>
        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(App(new PixelRect(-8, -8, 1936, 1036))));

    [Theory]
    [InlineData(0, 0, 1920, 1080)] // borderless window exactly on the monitor (browser F11, video, borderless game)
    [InlineData(-1, -1, 1922, 1082)] // slightly larger
    public void WindowCoveringTheWholeMonitorIsFullscreen(int x, int y, int w, int h) =>
        Assert.Equal(FullscreenVerdict.Fullscreen, FullscreenRule.Evaluate(App(new PixelRect(x, y, w, h))));

    [Fact]
    public void WindowOnAnotherMonitorDoesNotSuppress() =>
        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(App(new PixelRect(0, 0, 1920, 1080)) with { OnIslandMonitor = false }));

    [Fact]
    public void MinimisedOrCloakedWindowsDoNotSuppress()
    {
        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(App(Monitor) with { Minimized = true }));
        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(App(Monitor) with { Cloaked = true }));
    }

    [Theory]
    [InlineData(FullscreenRule.QunsRunningD3DFullScreen)]
    [InlineData(FullscreenRule.QunsPresentationMode)]
    public void ExclusiveFullscreenAndPresentationModeSuppressWhateverTheGeometry(int quns)
    {
        var probe = App(new PixelRect(10, 10, 100, 100)) with { NotificationState = quns, Kind = ForegroundKind.TransientShell };

        Assert.Equal(FullscreenVerdict.Fullscreen, FullscreenRule.Evaluate(probe));
    }

    [Theory]
    [InlineData(ForegroundKind.TransientShell)]
    [InlineData(ForegroundKind.OwnProcess)]
    [InlineData(ForegroundKind.None)]
    public void ShellOverlaysAndHalosOwnWindowsKeepThePreviousDecision(ForegroundKind kind) =>
        Assert.Equal(FullscreenVerdict.Unchanged, FullscreenRule.Evaluate(App(Monitor) with { Kind = kind }));

    [Fact]
    public void TheDesktopEndsFullscreen() =>
        Assert.Equal(FullscreenVerdict.NotFullscreen, FullscreenRule.Evaluate(App(Monitor) with { Kind = ForegroundKind.Desktop }));

    [Theory]
    [InlineData("Progman", ForegroundKind.Desktop)]
    [InlineData("WorkerW", ForegroundKind.Desktop)]
    [InlineData("Shell_TrayWnd", ForegroundKind.TransientShell)]
    [InlineData("MultitaskingViewFrame", ForegroundKind.TransientShell)]
    [InlineData("#32768", ForegroundKind.TransientShell)]
    [InlineData("Chrome_WidgetWin_1", ForegroundKind.Application)]
    public void ClassifiesShellWindowsByClass(string className, ForegroundKind expected) =>
        Assert.Equal(expected, ForegroundClassifier.Classify(42, ownProcess: false, className));

    [Fact]
    public void OwnProcessAndNoWindowWinOverTheClassName()
    {
        Assert.Equal(ForegroundKind.OwnProcess, ForegroundClassifier.Classify(42, ownProcess: true, "Progman"));
        Assert.Equal(ForegroundKind.None, ForegroundClassifier.Classify(0, ownProcess: false, "Progman"));
    }

    private static FullscreenProbe App(PixelRect window) =>
        new(5, ForegroundKind.Application, window, Monitor, OnIslandMonitor: true, Minimized: false, MaximizedWithCaption: false, Cloaked: false);
}

public class SuppressionStateTests
{
    [Fact]
    public void FullscreenHidesCollapsesAndCancelsPendingWork()
    {
        var s = Run(IslandState.Initial, IslandInput.Expand);
        var t = IslandStateMachine.Apply(s, IslandInput.FullscreenStarted);

        Assert.True(t.VisibilityChanged);
        Assert.False(t.Next.Visible);
        Assert.Equal(IslandPhase.Collapsed, t.Next.Phase);
        Assert.True(t.PresentationChanged);

        var pending = Run(IslandState.Initial, IslandInput.PointerEntered);
        var hidden = IslandStateMachine.Apply(pending, IslandInput.FullscreenStarted);
        Assert.True(hidden.CancelsTimer);
        Assert.False(hidden.Next.Hovered);
    }

    [Fact]
    public void FullscreenEndingShowsTheIslandAgainCompact()
    {
        var s = Run(IslandState.Initial, IslandInput.FullscreenStarted, IslandInput.FullscreenEnded);

        Assert.True(s.Visible);
        Assert.Equal(IslandState.Initial, s);
    }

    [Fact]
    public void PointerInputIsIgnoredWhileHidden()
    {
        var hidden = Run(IslandState.Initial, IslandInput.FullscreenStarted);

        Assert.False(IslandStateMachine.Apply(hidden, IslandInput.PointerEntered).Changed);
        Assert.False(IslandStateMachine.Apply(hidden, IslandInput.PointerPressed).Changed);
    }

    [Fact]
    public void ShowNowOverridesSuppressionUntilTheUserClearsIt()
    {
        var shown = Run(IslandState.Initial, IslandInput.FullscreenStarted, IslandInput.ShowNowRequested);
        Assert.True(shown.Visible);
        Assert.True(shown.Suppressed);

        // Normal interaction works while overridden.
        var hovered = Run(shown, IslandInput.PointerEntered);
        Assert.Equal(IslandPhase.ExpandPending, hovered.Phase);

        var back = IslandStateMachine.Apply(hovered, IslandInput.ShowNowCleared);
        Assert.False(back.Next.Visible);
        Assert.True(back.CancelsTimer);
    }

    [Fact]
    public void ShowNowEndsWhenTheFullscreenConditionEnds()
    {
        var s = Run(IslandState.Initial, IslandInput.FullscreenStarted, IslandInput.ShowNowRequested, IslandInput.FullscreenEnded);

        Assert.False(s.ShowNow);
        Assert.False(s.Suppressed);

        // So the next full-screen app is suppressed again.
        Assert.False(Run(s, IslandInput.FullscreenStarted).Visible);
    }

    [Fact]
    public void ShowNowWithoutFullscreenDoesNothing() =>
        Assert.False(IslandStateMachine.Apply(IslandState.Initial, IslandInput.ShowNowRequested).Changed);

    [Fact]
    public void ARepeatedFullscreenStartKeepsAnActiveOverride()
    {
        var s = Run(IslandState.Initial, IslandInput.FullscreenStarted, IslandInput.ShowNowRequested, IslandInput.FullscreenStarted);

        Assert.True(s.Visible);
    }

    [Fact]
    public void OpeningFromTheTrayWhileHiddenShowsAndPinsTheIsland()
    {
        var hidden = Run(IslandState.Initial, IslandInput.FullscreenStarted);
        var t = IslandStateMachine.Apply(hidden, IslandInput.Expand);

        Assert.True(t.Next.Visible);
        Assert.True(t.Next.ShowNow);
        Assert.Equal(IslandPhase.ExpandedPinned, t.Next.Phase);
        Assert.True(t.VisibilityChanged); // the window must be shown: Previous is the hidden state
        Assert.False(t.Previous.Visible);
    }

    [Fact]
    public void ControllerCancelsTheHoverTimerWhenFullscreenStarts()
    {
        var timer = new CountingTimer();
        var island = new IslandController(timer, IslandTiming.Default);
        island.Apply(IslandInput.PointerEntered);
        Assert.Equal(1, timer.Running);

        island.Apply(IslandInput.FullscreenStarted);

        Assert.Equal(0, timer.Running);
        Assert.False(island.TimerElapsed().Changed); // a late dwell callback cannot open a hidden island
    }

    private static IslandState Run(IslandState state, params IslandInput[] inputs)
    {
        foreach (var input in inputs)
        {
            state = IslandStateMachine.Apply(state, input).Next;
        }

        return state;
    }

    private sealed class CountingTimer : IIslandTimer
    {
        public int Running { get; private set; }

        public void Start(IslandTimerSlot slot, TimeSpan delay) => Running = slot == IslandTimerSlot.Intent ? 1 : Running;

        public void Cancel(IslandTimerSlot slot) => Running = slot == IslandTimerSlot.Intent ? 0 : Running;
    }
}
