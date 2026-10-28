# Project Status — Project Halo

Last updated: 2026-10-28 (evening: hover-intent and tray icon)

## Current phase

**Phase 4 — M2 island shell in progress on option C.** The S1/S2 gate is closed provisionally. The
island runs as a native app and now has hover-intent expansion and a tray icon.

- The S1/S2 matrix ran at 120 Hz (2 of 3 sessions per technique), then the owner stopped it.
- Calibration is VALID at 120 Hz and INVALID at 60 Hz.
- Both WPF techniques **FAIL PB-05** (frame pacing).
- Option C (Win32 + `Windows.UI.Composition`) passes PB-05 and every automated S2 item except the
  expected UI Automation gap.
- ADR-001 §13.1 records **C as PROVISIONALLY SELECTED**. It is not SELECTED, because PB-02 and the
  60 Hz results are INVALID and some sessions and manual checks were not run (open items below).

S3–S8 (Execution 2) have not started.

## Open items that stay visible until closed

| Item | State |
|---|---|
| PB-02 (wake-ups) | **Unresolved**: the PDH method cannot see short-lived threads; every session was INVALID. Needs the WPR `CSwitch` trace (E3). Not rerun in this milestone |
| 60 Hz validation | **Unresolved**: 60 Hz calibration INVALID, so no formal 60 Hz result exists |
| Third S1-C-120 session | **Incomplete** (owner stopped the batch) |
| Product-island PB-05 | **Not shown**: smoke runs capture only 9 of 20 transitions validly at 120 Hz |
| PB-03 with a visible clock | **Owner decision E2** (§5) |
| Narrator / Axe.Windows review | **Not done** |
| Manual DPI check S2-11M (100/150/175 %) | **Not done** |
| Multi-monitor and mixed DPI | **Blocked**: one display (D7) |

## 1. S1/S2 outcome (details: PROTOTYPE_S1_S2_REPORT §9)

| Criterion | A | B | C |
|---|---|---|---|
| PB-01, PB-03, PB-10, PB-12 (120 Hz) | PASS | PASS | PASS |
| PB-05 (120 Hz) | **FAIL** | **FAIL** | PASS |
| PB-02 | INVALID | INVALID | INVALID |
| 120 Hz session 3 | STOPPED/INCOMPLETE | NOT RUN | NOT RUN |
| 60 Hz (calibration INVALID) | INVALID | INVALID | INVALID |
| S2 automated items | PASS (S2-07 NOT RUN) | PASS (S2-07 NOT RUN) | PASS except S2-12 FAIL (expected) |
| S2-11M manual DPI | NOT RUN | NOT RUN | NOT RUN |
| Multi-monitor, mixed DPI | BLOCKED | BLOCKED | BLOCKED |

## 2. What the island does now (verified 2026-10-28)

`src/Halo.App` (`Halo.exe`) and `src/Halo.Core`. Commits: `2d4c3d4` (slice), `35dc882` (hover),
`e49c299` (tray), `62a7d90` (test hardening).

- **Placement:** a black pill centred at the top of the primary monitor's work area, 6 DIP below the
  top edge. It scales with DPI and re-places itself after DPI, display, work-area or Explorer changes.
- **Compact view:** the short date on the left, the time on the right, with a smaller AM/PM.
- **Expanded view:** the time in large type, the full date, a day-progress bar and a
  "Day N · X% of today has passed" line.
- **Hover-intent (FR-ISL-03, new):**
  - resting on the pill for **120 ms** opens it;
  - moving from the pill into the panel keeps it open (the expanded shape contains the pill);
  - leaving the expanded shape for **400 ms** closes it; coming back within that time cancels;
  - values are FR-ISL-03's defaults, set in one place (`IslandTiming.Default`). The dwell is short next
    to the 350 ms morph, so opening feels immediate, but long enough that crossing the pill does not
    open it. The grace is longer than the morph, so overshooting the edge does not start a collapse
    that is reversed mid-animation;
  - only one Win32 timer exists, and only while an action is pending; nothing polls.
- **Click and explicit actions (FR-ISL-04):** a click, the menus, the tray, UI Automation or a second
  launch **pins** the island open; pointer movement and hover timers never close a pinned island.
  A click on a hover-opened island pins it, and a click on a pinned island closes it. Pinning rather
  than closing matters because the dwell usually expires while the user is still moving in to click.
- **Tray icon (FR-APP-01, new; Settings, Start with Windows and Pause alerts are not built):**
  - `Shell_NotifyIconW`, version 4; the image is drawn in code (a dark pill with a light outline);
  - left click toggles; Space/Enter opens; the menu has **Open Halo**, **Collapse Halo**, **Exit Halo**;
  - every tray action goes through the same controller as the island, so it cancels pending hover
    timers;
  - re-added on `TaskbarCreated`, redrawn on DPI changes, deleted on exit;
  - if the shell refuses the icon, the island keeps running and logs `tray=failed`.
- **Animation:** 350 ms shape morph (size, position and corner radius) run by DWM. Content cross-fades
  inside a clip that follows the shape.
- **Feedback:** hover lightens the fill and edge; press scales the island to 96.5 %.
- **Reduced motion:** the system "Show animations" setting gives instant shape changes and cross-fades.
- **Window:**
  - topmost, non-activating tool window, not in the taskbar or Alt+Tab;
  - input only inside the visible shape (window region), so everything else passes clicks through;
  - never takes the foreground on click or hover.
- **Clock:** one-shot timer to the next minute boundary. It also refreshes after a time change and on
  resume.
- **Accessibility:** a UI Automation provider exposes a button named "Halo, <time>, <date>" with Invoke
  and ExpandCollapse. The day progress is exposed as the full description when expanded.
- **Lifecycle:**
  - right-click menu with Expand or Collapse and **Exit Halo**; focus returns to the previous window;
  - `Halo.exe --exit` closes a running island;
  - a second launch expands the running island (single instance; no second process stays);
  - exit stops the hover timer and clock, deletes the tray icon and disconnects UIA. A start-up
    failure runs the same clean-up and releases the single-instance mutex;
  - `--diagnostics` prints privacy-safe JSON events for tests (`phase`, `tray`, `transition.*`, …).

Not built yet (by scope): media, volume, battery, network, notifications, keyboard indicators, timers,
to-dos, settings, persistence, Start with Windows, logging to disk, full-screen detection (S7),
keyboard focus path (FR-ISL-07), packaging.

## 3. Measured on the product island (SMOKE, not budget verdicts — report §9.5)

| Measurement | 60 Hz (slice) | 120 Hz (slice) | 120 Hz (hover + tray build) |
|---|---|---|---|
| Run | APP-SMOKE-60-161618 | APP-SMOKE-120-162208 | APP-SMOKE-120-172126 |
| Start → first frame, p50 of 5 warm launches | 150.5 ms | 156.9 ms | 158.8 ms |
| Idle 90 s: CPU / attributed frames | 0 ms / 2 (minute clock) | 15.6 ms / 2 (minute clock) | 15.6 ms / 2 (2 clock updates) |
| Private memory / working set | 17.3 / 58.6 MB | 17.4 MB | 17.1 / 58.6 MB |
| Transitions (20, UIA-driven) | 20 valid; 3 with one long frame; max 35.7 ms | 9 valid (11 capture-coalesced); 0 long frames; max 10.0 ms | 9 valid (11 capture-coalesced); 0 long frames; max 10.0 ms |
| Exit p50; processes left | 64 ms; 0 | 70 ms; 0 | 65 ms; 0 |

The 15.6 ms of idle CPU is one scheduler tick, spent on the two clock redraws.

**Hover timing (manual probe, 8 cycles, 120 Hz, diagnostics timestamps):** dwell median 130.5 ms
(115–140); grace median 408 ms (388–413); dwell expiry → transition start 1–3 ms; 16 transitions for
8 cycles, so no restarted animations. Win32 timers tick about every 15.6 ms, which explains the spread.
PB-06 (dwell → first *frame* ≤ 50 ms) was **not** measured: the first frame needs Desktop Duplication.

**Text device choice (measured with `Get-Process`, 2026-10-28):** private memory was about 62 MB when
Direct3D picked the NVIDIA GPU (its driver loaded) and 68 MB on the Intel GPU. With WARP it was 15.5 MB.
The island therefore rasterises text with WARP; shape animation still runs on the GPU in DWM.

## 4. Tests (2026-10-28, clean Release build: 0 warnings, 0 errors)

| Suite | Command | Result |
|---|---|---|
| Unit (all projects) | `dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"` | **132/132** passed (spikes 79, Halo.Core 46, Halo.App 7) |
| Island desktop tests | `dotnet test --project tests/Halo.App.Tests/Halo.App.Tests.csproj -c Release -- --filter-trait "Category=Integration"` | **14/14** passed at 120 Hz in 4 consecutive runs of the final build (`62a7d90`); one earlier focus failure is described below |
| Spike desktop tests | `dotnet test --project spikes/tests/Halo.Spikes.Tests/Halo.Spikes.Tests.csproj -c Release -- --filter-trait "Category=Integration"` | **5/5** passed |
| Locked restore | `dotnet restore Halo.slnx --locked-mode` | passed |

Run the two desktop suites one at a time, and do not touch the mouse: they move the pointer, open the
tray overflow and put windows at the top of the screen.

Intermittent failures seen during this milestone (causes not proven):

- Two development runs of the hover-only build failed in hover tests (3 failures, extra
  "pill entered and left" phases with no matching move). The next 10 runs passed. The hover tests now
  check that the pointer is where the test put it and print the island's phase trace on failure.
- In the final verification, `ClicksToggleTheIslandWithoutTakingFocus` (a pre-existing test) once
  saw the foreground change from window 0x10172 to 0x1059E. The next 8 runs passed. The handles point
  to short-lived windows, which fits the tray test's flyout or menu still closing. Since `62a7d90` the
  baseline is taken once the foreground is stable, the tray test waits for its flyout to close, and
  focus failures name both windows. The FR-ISL-06 check itself is unchanged. If this recurs, the
  message shows whether Halo took the foreground.

What the evidence covers:

- **Unit (deterministic, virtual time):** dwell, early leave, re-entry, pill → panel, grace, return in
  grace, click before dwell, click pins, explicit expansion vs. stale timers, tray expand/collapse
  during pending timers, menu cancel, shutdown with a pending timer, timer armed only while pending.
- **Desktop (real Windows):** hover dwell and grace timing in the running app, early leave, cancel on
  return, exit with each timer pending, tray icon registered while running and deleted by the shell on
  exit, **Explorer's own notification-area button** driving toggle and the Open/Collapse/Exit menu,
  tray failure, start-up failure, no duplicate process.
- **Limits:** on this Windows build the icon sits in the overflow flyout, and the test opens it. After
  a process exits, Windows drops its icon even if the app never deleted it, so the proof of clean-up is
  the shell's reply to the delete call. Explorer restart (`TaskbarCreated`) was not tested.

## 5. Decisions required from the owner

| # | Decision | Recommendation |
|---|---|---|
| **E1** | Accept option C as the provisional rendering path (ADR §13.1) | Accept |
| **E2** | PB-03 says "Frames presented, settled: 0 per second" with no exception, and FR-ISL-12 says "no rendering while settled". A visible minute clock must draw once a minute, so the island can never meet PB-03 literally while it shows the time. The budget's purpose (report §8.6) is to prove there is **no continuous or unexplained rendering** | Keep PB-03's threshold for unexplained frames and add one precise allowance: *"0 unexplained frames in the settled window; at most one attributed frame per minute boundary, each within 250 ms after a logged `clock` event (as calibration TICK)."* Alternative: keep PB-03 literal and measure it with the clock hidden (needs a test switch). PB-03 is **not** changed until the owner decides |
| **E3** | Close the outstanding gate items (ADR §13.1): WPR CSwitch trace for PB-02, a C-based 60 Hz negative control, S1-C-120 session 3, manual S2-11M | Schedule together: about 1 h of machine time and 10 min attended |
| D1 | Approve ADR-001 | After E1–E3 |
| D7 | External monitor; spare PC or VM | Still needed for multi-monitor, mixed-DPI and clean-install tests |
| D9 | Project licence | Before any public release |

Earlier decisions Q1–Q8 were answered on 2026-10-27, and the PB-02 rule change (option b) on 2026-10-28.

## 6. Known defects and limits

- At 120 Hz, 11 of 20 smoke transitions had one capture-coalesced frame (Desktop Duplication merged
  two presents). This affects the measurement's validity in a smoke run and is not a visible defect,
  but it means PB-05 is **not** shown for the product island yet.
- Device-loss recovery for the text device is implemented but untested.
- Hover timers fire on the 15.6 ms system tick: the measured dwell was 115–140 ms for a 120 ms setting.
- Windows 11 puts a new tray icon in the overflow flyout; the user must drag it to the taskbar to keep
  it visible. Halo does not change that setting.
- The keyboard on the tray icon opens the island (Space/Enter) but does not close it, because Enter
  can deliver the select event twice.
- No settings yet: dwell, grace and click-only mode cannot be changed without rebuilding.
- Primary monitor only; multi-monitor untested (one display).

## 7. Session notes

- 2026-10-28: the owner asked for the 60 Hz switch to be done for them. A one-off script outside the
  repository changed the refresh rate dynamically (not saved), and 120 Hz was restored afterwards.
- Two background runs were stopped by the host for low memory. The 120 Hz batch was then restarted as
  a separate process, and the owner stopped it during session 3.
- 2026-10-28 evening: an island left running from the earlier session (PID 17140) locked the build
  output. It was closed with `Halo.exe --exit`.
- Commit message errata (commits are not amended without approval): `35dc882` says "22 deterministic
  controller tests"; it adds 18. `62a7d90` gives the foreground handles as 0x101E2 → 0x105AE; they were
  0x10172 → 0x1059E (65906 → 66974).
- The canonical repository is `C:\dev\Notch\advanced-windows-island`; the OneDrive copy was not touched.

## 8. Feature execution register (campaign M1 → M3, started 2026-10-28)

One record per feature. Status is PASS only when the behaviour is implemented and its tests passed on this
machine (1920×1080 @ 120 Hz, 125 %, one display). "Desktop" = `Category=Integration` tests against real Windows.

### M1-F01 Full-screen suppression and "Show island now" — PASS

- **Requirements:** FR-ISL-09, FR-ISL-06 (no focus change), FR-ISL-11 (Explorer restart), FR-ISL-12 (no drawing
  while hidden).
- **Rule (ADR §4.9, `Halo.Core/Shell/FullscreenRule`):** suppress when the foreground application window is on the
  island's monitor, visible, not minimised or cloaked, and its rectangle covers the whole monitor, except an
  ordinary maximised window that still has a title bar. Exclusive Direct3D full screen and presentation mode
  (`SHQueryUserNotificationState`) always suppress. The desktop ends suppression. Taskbar, Start, Alt+Tab,
  flyouts, menus and Halo's own windows keep the previous decision, so they do not flash the island over a game.
- **Triggers, no polling:** `EVENT_SYSTEM_FOREGROUND` (all processes); `EVENT_OBJECT_LOCATIONCHANGE` scoped to the
  foreground application's process, filtered to its top-level window (F11 in the same window); `ABN_FULLSCREENAPP`
  through an app-bar registration that reserves no space; display, DPI, work-area changes and `TaskbarCreated`.
  Every trigger schedules one probe 100 ms later (debounce: one probe per burst, no flicker during resizes).
- **Behaviour:** suppression hides the window (`SW_HIDE`), collapses it and cancels pending hover timers. Pointer
  input is ignored while hidden, and the minute clock is not redrawn until the island is shown again. The tray
  menu gains **Show island now** (checked while active) only while suppressed. Choosing it shows the island over
  the full-screen app without taking the foreground. Choosing it again returns to automatic suppression, and the
  override also ends when the full-screen condition ends. Tray *Open Halo* or a second launch while hidden counts
  as the override and pins the island open. Precedence: user override > full-screen suppression > normal.
- **Also fixed:** `WM_MOUSELEAVE` while the pointer is still inside the shape (seen once as a spurious
  leave/re-enter in a hover test, likely after the window region is replaced) is now checked against the real
  pointer position, and tracking is re-armed. The cause was not proven.
- **Files:** `Halo.Core/Shell/FullscreenRule.cs` (rule + class classifier), `IslandStateMachine.cs` (suppressed,
  show-now, visibility), `Halo.App/Island/FullscreenWatcher.cs`, `IslandWindow.cs`, `Interop/Native.cs`.
- **Unit tests:** 29 new (`FullscreenTests.cs`: ordinary, maximised with/without auto-hide taskbar, borderless on
  the monitor, other monitor, minimised/cloaked, exclusive D3D and presentation mode, shell/own/none keep state,
  desktop, classifier, hide/collapse/cancel, show again, pointer ignored while hidden, override on/off/auto-end,
  tray open while hidden, controller cancels dwell). Total unit 161/161.
- **Desktop tests:** 5 new, using a window the test owns, switched between ordinary, maximised and borderless
  full screen: no false suppression for ordinary or maximised windows; hide on full screen; show again when full
  screen ends in the same window; foreground switching both ways; Show island now on/off/auto-end through the
  real menu, with the foreground kept by the full-screen window; `TaskbarCreated` re-registration; exit while
  suppressed; CPU < 100 ms in 5 s while suppressed. Island desktop suite 19/19 in 3 consecutive runs.
- **Evidence limits:** the "Show island now" test sends the tray callback itself, because the taskbar is covered
  by the full-screen window; the real Explorer tray path is covered by the existing tray test. A real Explorer
  restart was not performed (it would disrupt the owner's session); `TaskbarCreated` was posted instead. Not
  verified with games, exclusive full-screen D3D, video players or PowerPoint: the §4.3 full-screen matrix
  remains manual.
- **Privacy:** only the foreground window's class, rectangle and state are read; no titles or process names are
  logged (diagnostic tokens: desktop/shell/own/application).
- **Commit:** `ea894a2`.
- **Defect found afterwards and fixed in M1-F02:** an explicit open while hidden computed the transition from an
  already-modified state, so `VisibilityChanged` was false and the window would not have been shown. The unit
  test now asserts `VisibilityChanged`.

### M1-F02 Unified event panel and prioritisation — PASS

- **Requirements:** ADR §6.1/§6.2 (alerts, interaction lock, `AllowedWhenSuppressed`), FR-SYS-01 coalescing
  (groundwork for M3).
- **Design:** an `Alert` presentation (300 × 44 DIP pill: icon glyph, one line, optional level bar and value)
  managed by the existing `IslandController`; no second overlay or controller. `IslandAlert` has key, kind,
  priority (Low/Normal/High/Critical), content, duration, stale-after and allowed-over-full-screen. Rules are in
  ADR §6.1 "As built, M1". Timers are keyed (`Intent`, `Alert`). `TimeProvider` is injected for staleness.
- **Event sources and their place:**

  | Source | Path |
  |---|---|
  | Click, hover, tray, UIA, second launch | phase inputs (pin/expand/collapse) |
  | Full screen | suppression inputs |
  | Volume, battery, network, media (M3) | `Raise(alert)` |

- **Files:** `Halo.Core/Island/IslandAlert.cs`, `IslandController.cs` (rewritten timer interface and
  arbitration), `IslandStateMachine.cs`, `IslandLayout.cs` (alert shape), `Halo.App/Island/IslandVisuals.cs`
  (alert content), `IslandWindow.cs` (alert timer, `alert` diagnostics, diagnostics-only test message).
- **Unit tests:** 16 new (`AlertTests.cs`): show and expire; coalesce and restart; priority replace and queue;
  stale drop; wait while expanded and show on collapse; expired not shown after collapse; wait while dwell is
  pending or pressed; hover expands and clears; click pins and alerts never pin; stale alert timer; full-screen
  allow-list; full screen clears an ordinary alert; bounded queue; shutdown; level clamp; alert shape geometry.
  The hover and full-screen test doubles were moved to the keyed timer interface without changing their
  assertions. Unit total 177/177.
- **Desktop tests:** 3 new. A real alert pill with drawn content that expires back to compact, foreground
  unchanged; an alert waits while expanded and shows after collapse; only allowed alerts appear over a
  full-screen window. Island desktop 22/22.
- **Runtime:** screenshot of a test alert (glyph, text, bar, value) checked by eye.
- **Accessibility:** alerts are not yet announced to UI Automation (FR-A11Y-02; planned in M2-F05).
- **Privacy:** alert text is never logged; diagnostics carry kind, priority and state tokens only.

## Next recommended action

1. Owner reviews E1–E3 (E2 now has a precise proposal).
2. Next feature: **full-screen suppression (FR-ISL-09, spike S7)**. The island is topmost, so it now
   covers full-screen video, games and slideshows. Hide it while a full-screen app is in the foreground
   on its monitor, and add the tray item "Show island now" as the override that FR-ISL-09 requires.
   This builds on the tray menu and the controller, starting with the §4.3 full-screen matrix.
3. Then the keyboard focus path (FR-ISL-07) and a first Settings surface for dwell, grace and
   click-only mode.
