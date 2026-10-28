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
- **Commit:** `daab0bf`.

### M1-F03 Clock and idle-rendering contract — PASS (PB-03 wording still an owner decision)

- **Requirements:** FR-ISL-12 (no rendering while hidden, locked or display off), PB-03 (unchanged), clock
  correctness.
- **Behaviour:**
  - the clock stays event-driven: one one-shot timer to the next minute boundary, and only changed text lines are
    redrawn;
  - while the session is locked (`WTSRegisterSessionNotification`) or the display is off
    (`GUID_SESSION_DISPLAY_STATUS`), the clock timer is stopped entirely. It refreshes at once on unlock or
    display-on;
  - while full screen hides the island, text is not redrawn until it is shown again;
  - `WM_TIMECHANGE` clears .NET's cached time zone before re-reading (a time-zone change was not picked up before);
  - `WM_SETTINGCHANGE("intl")` clears the cached culture, so a 12/24-hour or date-order change applies;
  - day progress is clamped to [0, 1), so the label reads 0–99 % (including 23- and 25-hour DST days, which use
    the wall clock).
- **Unit tests:** 7 new (`ClockBoundaryTests.cs`: midnight/new-year rollover, 0/50/99 %, DST day, leap year,
  the minute timer is the only periodic cadence). Unit 184/184.
- **Desktop test:** 1 new. The window messages Windows sends for lock, unlock, time change and `intl` are sent to
  the running island: pause, resume, refresh. A real lock or display-off was not performed (it would disturb the
  owner).
- **Idle evidence (SMOKE, not a verdict):** `APP-SMOKE-120-192017`, valid. 90 s idle: 0 ms CPU, 2 attributed
  frames, 2 clock updates, 17.8 MB private.
- **PB-03:** unchanged. Its literal wording ("0 frames/s settled") still conflicts with a visible minute clock;
  decision E2 (§5) remains open with the precise proposal.

### M1 completion gate — PASS (2026-10-28, idle machine)

Locked restore passed; Release build 0 warnings; unit 184/184; island desktop 23/23 in 2 consecutive runs; spike
desktop 5/5. Verified in those suites: full-screen suppression and override, tray, hover and click, single
instance, no focus change, clean shutdown, clock lifecycle, no orphaned processes.

Smoke run `APP-SMOKE-120-192017` (valid, 120 Hz):

| Measurement | Value |
|---|---|
| Start → first frame p50 | 175.2 ms (158.8 ms before M1; start-up now also registers the tray, app bar, WinEvent hook and session notifications) |
| Idle 90 s | 0 ms CPU, 2 frames, 2 clock updates |
| Private / working set | 17.8 / 62.3 MB |
| Transitions | 20/20 valid; 1 with one long frame; max 17.2 ms |
| Exit p50 | 69 ms; 0 processes left |

Commit: `bcd588d`.

The earlier gate attempt (`APP-SMOKE-120-181627`) was INVALID (`user_input_during_run`), and one hover test in the
same session failed with "the pointer was moved during the test". Both were caused by real mouse use and are kept
here as the audit trail.

### M2-F01 Persistent settings — PASS

- **Requirements:** FR-APP-05, NFR-07, ADR §8, FR-ISL-03/04/09/13 settings.
- **Storage:** `%LOCALAPPDATA%\ProjectHalo\settings.json` (per user, no roaming, no cloud, no secrets),
  `schemaVersion` 1.
- **Settings (only ones with an effect today):**

  | Key | Default | Range / meaning |
  |---|---|---|
  | `hoverToExpand` | true | off = click-only mode |
  | `hoverDwellMs` | 120 | 0–500 |
  | `leaveGraceMs` | 400 | 100–2000 |
  | `startExpanded` | false | open (pinned) at start |
  | `reduceMotion` | false | instant transitions, on top of the Windows setting |
  | `hideInFullscreen` | true | off removes the full-screen hooks |

- **Robustness:**
  - writes are atomic (`.tmp`, flush, `File.Replace` keeping `.bak`), and an identical save is skipped. Runtime
    changes are saved at most once a second and flushed on exit; a failed save is logged and the island keeps
    running;
  - each field is checked on its own: a wrong type or an out-of-range value takes its default and the rest is kept;
  - an unreadable or oversized (> 64 KB) file is renamed `.corrupt-<time>`, then the backup is used, else defaults;
  - a file without `schemaVersion` is migrated and written back as v1. A file from a newer version is read for
    known fields and copied to `.v{N}.bak` before it is first overwritten.
- **Applied live:** hover timing and click-only mode through `IslandController.SetTiming` (from the next pending
  action), reduce motion, full-screen hooks on/off (turning it off ends a current suppression), start expanded.
- **Test isolation:** `--data-dir <path>` (honoured only with `--diagnostics`); every desktop test island uses its
  own temporary folder.
- **Unit tests:** 21 new (`SettingsTests.cs`: defaults, missing file, persistence across restarts, no rewrite of
  identical content, backup and no temp leftovers, 6 invalid-field cases, 3 corrupt cases with backup, corrupt
  without backup, oversized, v0 migration, newer-version backup, documented keys only, click-only mode, timing
  change). Unit total 205/205.
- **Desktop tests:** 4 new: click-only mode (hover does not open, click does); start expanded plus custom dwell;
  full-screen suppression off; corrupt file set aside and defaults used. Island desktop 27/27.
- **Commit:** `9cb6191`.

### M2-F02 Settings window — PASS

- **Requirements:** FR-APP-02 (keyboard accessible, changes apply live), FR-A11Y-01 for the settings surface.
- **What it is:** "Halo settings", a small native window of standard Win32 controls (Common Controls v6, now in
  the manifest; no new package, no WPF). Opened from the tray menu item **Settings…**. It has 4 check boxes
  (expand on hover, hide in full screen, reduce motion, open at start), 2 sliders (hover delay 0–500 ms in steps
  of 10, collapse delay 100–2000 ms in steps of 50) with their values in ms, and **Restore defaults** and
  **Close** buttons. The hover-delay slider is disabled in click-only mode. Every change applies at once through
  `IslandWindow.UpdateSettings` and is saved within 1 s.
- **Keyboard:** Tab and Shift+Tab move through the controls (the island's message loop calls `IsDialogMessage`),
  Space toggles, arrow keys move the sliders, Alt+underlined letters jump, Esc closes.
- **Defects found and fixed while testing:**
  - a default Close button would have closed the window when the Enter that picked "Settings…" in the tray menu
    reached it, so Close is no longer the default button;
  - the test helper that dismisses the tray flyout sent Esc to whatever window was in front (here the settings
    window). It now presses Esc only when the flyout itself has the foreground.
- **Changed test:** the tray menu gained an item, so the existing tray test reaches "Exit Halo" with Down ×4
  instead of ×3 (same real menu, same assertions).
- **Desktop test (1 new, end to end through Explorer's real tray button):**
  - opens the window from the tray and checks that every control has an accessible name and role (4 check boxes,
    2 sliders, 2 buttons by control ID);
  - toggles click-only mode through UI Automation, sees it applied and saved, then sees that hovering no longer
    opens the island;
  - Tab moves focus to a slider, and Right ×2 sets the collapse delay to 500 ms, which is saved to the file;
  - Esc closes the window and the island keeps running;
  - a new island started from the saved file comes up with both changes.
  Island desktop 28/28 in 2 consecutive runs; unit 205/205.
- **Not done:** high-contrast and text-scaling review (FR-A11Y-04/06), and a Narrator pass, which needs a person
  listening; Axe.Windows is not installed.
- **Commit:** `7ce9c9d`.

### M2-F03 Error handling and local diagnostic logging — PASS

- **Requirements:** FR-APP-07, ADR §9.1/§9.2, SECURITY_AND_PRIVACY_DESIGN §6 (as-built note there), NFR-05.
- **Log:** `<data folder>\logs\halo.log`, JSON lines, rolled at 1 MB, 5 files at most. Values can only be
  numbers, booleans or tokens; anything else becomes `redacted`. Each event name is limited to 20 lines a minute,
  with the overflow counted. Write failures are dropped and counted.
- **Recorded:**
  - `app.start` (settings status, invalid-field count, DPI; warning level when settings had to be recovered);
  - `app.exit`;
  - `tray.failed`, `fullscreen.unavailable`, `settings.save_failed` (warnings);
  - `window.error` (exception type, HRESULT and message ID from the window procedure);
  - `app.start_failed` and `app.crash` (an `AppDomain.UnhandledException` handler; no auto-restart).
  - Nothing per tick or per pointer move.
- **Failure handling, confirmed or added:** a failing tray, full-screen hooks or settings save keeps the island
  running; start-up failure cleans up (existing test); window-procedure exceptions are logged and the next event
  retries.
- **Settings:** an **Open log folder** button (Explorer, nothing uploaded).
- **Unit tests:** 9 new (`LogFileTests.cs`: JSON shape and types; 5 free-text and injection cases redacted;
  token rules; rolling at 1 MB with 5 files at most; per-minute limit with the drop count reported; write
  failures counted, not thrown). Unit 215/215.
- **Desktop test:** 1 new. A real run with an injected tray failure and a corrupt settings file writes
  `app.start` (warning, recovered_defaults), `tray.failed` and `app.exit`. Every string value is a token, the data
  path never appears, and a short run writes fewer than 20 lines. Island desktop 29/29.
- **Not tested:** the "Open log folder" button itself (it would open an Explorer window on the owner's desktop).
- **Commit:** `753ff50`.

### M2-F04 Display and DPI behaviour — PASS for this machine (multi-monitor and mixed DPI: BLOCKED, D7)

- **Requirements:** FR-ISL-01, FR-ISL-10 (DPI auto-scale), FR-ISL-11.
- **Policy (unchanged):** the island sits at the top centre of the **primary** monitor's work area.
- **Behaviour:**
  - placement is recomputed from the live work area and DPI on DPI, display, work-area, Explorer-restart and
    resume events, with no cached geometry;
  - new: when the result equals the current layout, nothing is moved, rebuilt or redrawn (`placement.unchanged`).
    Before, every work-area notification rebuilt the text surfaces;
  - new: resume from sleep re-places the island and re-checks full screen, besides refreshing the clock.
  - Transparent rendering and hit testing follow the new layout (region checked in the test).
- **Unit tests:** 10 new (`DisplayGeometryTests.cs`: 100/125/150/175/200 % shapes inside the canvas and centred;
  left taskbar; top taskbar; negative-coordinate monitor; equal inputs give equal layouts, which the no-op relies
  on; tiny work area rejected). Unit 225/225.
- **Desktop tests:** 2 new.
  - **Real change:** a test window registers a 60 px top app bar, so the shell really shrinks the work area. The
    island moves below it, its region follows, and it returns when the app bar is removed.
  - **Synthetic messages:** `WM_DISPLAYCHANGE` and resume messages give `placement.unchanged` with no rebuild.
  - Island desktop 31/31.
- **Not verified (stated, not claimed):** a real resolution or DPI change (display settings are reserved for the
  owner), a real sleep/resume, multi-monitor, mixed DPI and monitor hot-plug (one display, D7).
- **Commit:** `80b6384`.

### M2-F05 Accessibility foundation — PARTIAL (known defect: UIA events not delivered)

- **Requirements:** FR-A11Y-01, FR-A11Y-02, FR-A11Y-05 (reduced motion), FR-APP-02.
- **Surface today:**
  - the island is one UIA button named "Halo, <time>, <date>" (live), with Invoke and ExpandCollapse. Its full
    description is the day progress when expanded and the alert text while an alert is shown (new);
  - the settings window uses standard controls with names, roles, states and keyboard operation (M2-F02);
  - alerts carry a glyph **and** text (no information by colour alone).
- **New:**
  - each shown alert calls `UiaRaiseNotificationEvent`: critical uses ImportantMostRecent, everything else uses
    MostRecent, so repeated volume changes replace each other. The activity ID is `Halo.Alert.<kind>`;
  - a setting, "Announce alerts to screen readers" (`announceAlerts`, default on), satisfies FR-A11Y-02
    "announcements can be turned off". The settings window now has 5 check boxes.
- **Known defect, found while testing (OPEN):** events raised by the island's UIA provider reach no UIA client.
  Raising notifications and property changes returns `S_OK` while a client listens, but nothing arrives, whether
  the client subscribes on the island element or on the desktop subtree. This includes the expand/collapse and
  name changes raised since the first slice, which were never verified before. Removing `UseComThreading` made no
  difference.
  - **Likely next step:** implement the provider as a fragment root (`IRawElementProviderFragmentRoot`, with a
    runtime ID and bounding rectangle) and re-test with Narrator.
  - **Effect:** screen-reader users get the live name and description on demand, but not spontaneous
    announcements.
- **Tests:**
  - **desktop, 3 new:**
    - while a UIA client is attached, a shown alert is raised (S_OK) and appears in the island's description,
      which clears when the alert ends;
    - with announcements off, nothing is raised;
    - reduce motion: the transition completes in under 200 ms, against more than 250 ms in a normal control run;
  - unit settings tests extended for the new key. Unit 225/225; island desktop 34/34.
- **Not done:** Narrator pass (needs a person listening), Axe.Windows (not installed), high contrast, text
  scaling, the keyboard focus path to the island (FR-ISL-07, opt-in shortcut).
- **Commit:** `43f4040`.

### M2 completion gate — PASS (with M2-F05 PARTIAL)

Locked restore passed; clean Release build (`--no-incremental`) 0 warnings; unit 225/225; island desktop 34/34
in 2 consecutive runs; spike desktop 5/5; no Halo process left. Verified in those suites: persistence and
recovery, every settings-window control and its effect, log privacy and bounds, work-area changes, the UIA
surface the island exposes, tray lifecycle and single instance.

Smoke run `APP-SMOKE-120-195953` (valid, 120 Hz), SMOKE and not a verdict:

| Measurement | Value |
|---|---|
| Start → first frame p50 | 185.2 ms (M1 175.2 ms; settings load and log file added) |
| Idle 90 s | 0 ms CPU, 1 frame, 1 clock update |
| Private / working set | 17.4 / 60.7 MB |
| Transitions | 20/20 valid; 1 with one long frame; max 16.7 ms |
| Exit p50 | 71 ms; 0 processes left |

### M3-F01 Media session and now playing — PASS

- **Requirements:** FR-MED-01 (show what is playing, fall back when it ends), FR-MED-03 (session choice), FR-MED-04
  (art from the session only), SECURITY_AND_PRIVACY_DESIGN §2, §3 and §6.
- **API:** `GlobalSystemMediaTransportControlsSessionManager` (GSMTC) from the Windows SDK projection that is
  already referenced. No new package.
- **Session choice (`MediaSessionSelector`):** Windows' current session while it is listed, else the first one
  that is playing, else the first one.
- **Events, no polling:** sessions, current session, properties, playback and timeline events post one message.
  The island debounces bursts (150 ms) into one refresh, built off the UI thread. Timeline and playback events
  reuse the cached metadata, and art is re-decoded only when the title, artist or album changes.
- **User-visible:**
  - compact pill: a play or pause glyph and the title while a session exists, otherwise the clock;
  - expanded card: 96 px art (or a placeholder), title, artist, a progress bar and "1:23 / 3:45" when the player
    reports a valid timeline;
  - the UIA name becomes "Halo, Playing: <title> by <artist>, <time>, <date>";
  - when the session closes, the island returns to the clock.
- **Setting:** "Show what is playing (title, artist, artwork)", `showMedia`, default on. Off releases the
  manager.
- **Failure handling:** if the manager fails to start, media is marked unavailable (logged once as a warning)
  and the island runs without it. A session that disappears mid-read is retried on the next event
  (`media.refresh_failed`, type and HRESULT only).
- **Files:** `Halo.Core/Media/MediaModel.cs`, `Halo.App/Island/MediaWatcher.cs`, `IslandVisuals.cs` (media card),
  `Interop/TextRenderer.cs` (art and glyphs), `IslandWindow.cs`, `SettingsWindow.cs`, `HaloSettings.cs`.
- **Unit tests:** `MediaModelTests` covers:
  - no session, Windows' current session wins, playing session, first session;
  - text cleaning: whitespace, control and zero-width characters, null, a 512-character cap;
  - timeline: interpolation, pause, hours, invalid timelines hidden, non-zero start;
  - missing title or artist in the summaries.
  Settings tests cover the new key.
- **Desktop tests (real GSMTC):** the test process is the media session: a Windows `MediaPlayer` at volume 0
  looping `C:\Windows\Media\Alarm01.wav`, with a synthetic title and artist. Nothing is downloaded or heard.
  - **Shown and ended:** the title arrives, the UIA name follows, the expanded card is drawn, pause is noticed,
    and closing the session returns the island to the clock;
  - **Off and private:** with `showMedia` off there are no media states; with it on, the title and artist appear
    in neither the log file nor the diagnostics.
- **Test defect found and fixed (audit trail, 2026-10-28):** two runs failed because another app's session became
  Windows' current session mid-test, and the island correctly followed it. The tests now check for media
  sessions from other apps first, wait up to 15 s, and fail with an explicit message if one is still present.
  The `media` diagnostic gained a `sessions` count. The product's session choice did not change.
- **Not covered:** artwork was checked with the placeholder path only (the test session has no thumbnail);
  multiple real sessions from different apps were covered only by unit tests; no browser or Spotify session was
  tested.
- **Commit:** see M3-F02 (one commit for both, because they share the media card).

### M3-F02 Playback controls — PASS

- **Requirements:** FR-MED-02 (previous, play/pause, next), FR-MED-05 (progress), M3-F02 contract.
- **Behaviour:**
  - three buttons on the media card (`MediaControls.Layout`). A button appears only when the session enables
    that command, and play/pause shows the action it will take;
  - a press captures the pointer, and the command runs on release over the same button. Button clicks never
    collapse or toggle the island; empty panel space keeps the ordinary click-to-collapse;
  - the command goes to the session that was selected **at press time**. If another session is selected by
    release, it is dropped as `stale`;
  - commands are sent off the UI thread. The island does not assume success: it shows what the session reports
    next. Results are logged as `accepted`, `refused` or `failed` (a vanished session; logged as a warning with
    its HRESULT);
  - progress is redrawn at 1 Hz only while the card is visible and playing. There is no timer when paused,
    collapsed or hidden.
- **Seeking:** not built. GSMTC playback-position changes are not reliable across players, and the contract
  allows leaving it out.
- **Unit tests:** `MediaControlsTests`: no session, no buttons; only enabled commands are offered; buttons sit
  inside the expanded shape and are large enough; hit testing, including the gaps; the play/pause glyph.
- **Desktop test (real session and pointer):** clicking play/pause really pauses and resumes the player, and the
  island follows the session's own report and stays pinned. A click on hidden Next sends nothing and collapses
  the island like any panel click. Once the player enables Next, the island notices; after re-expanding, the
  same spot sends Next and the player receives it.
- **Test defect found and fixed:** the test expected the island to stay open after a click on hidden Next. That
  click lands on empty panel space, which collapses the island by design, so the test now waits for the
  collapse and expands again.
- **Results:** media desktop tests 3/3 in 3 consecutive runs; unit 250/250; island desktop 37/37.
- **Commit:** `0503baf` (M3-F01 and M3-F02 together).

### M3-F03 Volume pill and volume row — PASS

- **Requirements:** FR-SYS-01 (volume and mute pill for any change source, coalesced, no device → no pill), the
  M3-F03 contract (adjust the volume in the expanded panel), FR-ISL-06 (no focus change), FR-A11Y-07 (target size).
- **API:** Core Audio through classic COM interop (`Interop/CoreAudio.cs`). No new package.
  - Endpoint: the **default console render device**, the device the volume keys and the taskbar flyout control;
  - `IAudioEndpointVolumeCallback` for changes;
  - `IMMNotificationClient` for default-device changes, removals and state changes.
- **Events, no polling:**
  - the volume callback runs on an audio thread. It keeps the newest reading and posts one message until the UI
    thread has taken it, so a burst becomes one update;
  - device notifications are debounced (200 ms) into one re-bind;
  - Halo's own changes carry a private event context, so they update the slider but never raise the pill.
- **User-visible:**
  - **pill:** a change made elsewhere while the island is collapsed shows the 300 DIP alert pill: speaker glyph
    (mute or 0–3 waves), "Volume" or "Muted", a level bar and the percent. It lasts 1.5 s from the last change.
    Every volume change uses one alert key, so a burst updates one pill. Priority Normal; never shown over full
    screen; dropped if it could not be shown within 1 s (for example while the island was open);
  - **volume row:** the expanded island gains a 40 DIP row: a mute button (32 DIP), a slider (28 DIP hit band; a
    click jumps, a drag follows the pointer in whole percent) and the value. Moving the slider while muted unmutes,
    as Windows' own slider does. Muted shows a dimmed bar. Without a device the row says "No audio output" and has
    no controls;
  - while the island is open, the slider follows external changes and no pill is raised;
  - screen readers: the pill is announced as "Volume 64 percent" or "Volume muted, 64 percent" (new
    `AlertContent.Accessible`), and the open island's description ends with the same text.
- **Setting:** "Show volume changes and the volume slider" (`showVolume`, default on). Off releases Core Audio and
  gives back the original 172 DIP expanded height (`IslandLayout.VolumeRow`).
- **Failure and recovery:** if Core Audio is unavailable, the island logs `volume.unavailable` (warning) once and
  carries on. A failed set call (the device went away) triggers a re-bind. With no default device, the island
  shows the "No audio output" state. Device IDs and names are never logged; diagnostics carry percent, mute,
  source (`external`, `halo`, `bind`) and binding status only.
- **Files:** `Halo.Core/Audio/Volume.cs`, `Halo.Core/Island/IslandAlert.cs` (`Accessible`, `Spoken`),
  `IslandLayout.cs` (volume row), `Halo.App/Interop/CoreAudio.cs`, `Island/VolumeWatcher.cs`, `IslandVisuals.cs`,
  `IslandWindow.cs`, `SettingsWindow.cs`, `HaloSettings.cs`, `Program.cs` (`HALO_TEST_FAULT=audio`).
- **Unit tests (23 new, `VolumeTests`):** percent rounding and clamping (including NaN); glyph thresholds; spoken
  text; the pill's key, priority and full-screen rule; a burst of 20 changes gives one pill and one expiry; a pill
  that waited more than 1 s is dropped; no row means the old layout; the row inside the shape at 100/125/150/200 %
  and below the media buttons; ≥ 24 DIP targets; hit tests, including the gap and the value; level from the
  pointer, clamped past both ends. Settings tests cover the new key. Unit 273/273.
- **Desktop tests (4 new, real default endpoint):** the test opens the endpoint with its own event context. It
  moves the level only 2–8 % from where it was and restores the original level and mute state on dispose, even
  after a failure.
  - **External change:** the pill appears, the island stays collapsed, mute and unmute update the same pill, the
    pill expires, and focus never moves;
  - **Row:** a click on the track sets the endpoint to the level under the pointer; a real drag sets a second
    level; the mute button toggles both ways; no pill; the island stays pinned; UIA description has the volume;
    focus unchanged;
  - **Device change and no device:** a burst of device messages gives exactly one re-bind, and later changes
    still arrive. With `HALO_TEST_FAULT=audio` there are no volume events and no pill, and a click where the
    slider would be leaves the endpoint unchanged;
  - **Setting off:** original canvas height, no endpoint opened, no pill.
  Volume tests 4/4 in 3 consecutive runs; island desktop 41/41. Afterwards the system volume was confirmed back at
  its starting level (28 %, not muted).
- **Smoke measurement (not a PB verdict):** external change → `alert shown` diagnostic, 5.3–6.1 ms across 3 runs
  (`HALO_TEST_METRICS`). This is the time to the island's state change, not to a presented frame, so the < 100 ms
  response budget is **not** claimed as measured.
- **Runtime check:** screenshots of the expanded island with the volume row (28 %) and of the volume pill, checked
  by eye.
- **Not verified:** a real device switch or unplug (it would disturb the owner's audio; the re-bind path was driven
  by the device message instead); hardware volume keys (the test changes the endpoint the same way an app does);
  per-app volume (out of scope).
- **Commit:** `6bc1a28`.

### M3-F04 Battery and charger — PASS (transitions verified with synthetic readings)

- **Requirements:** FR-SYS-02 (charger connected or disconnected; no battery → disabled), FR-SYS-03 (low 20 % and
  critical 10 %, configurable; unknown level → no alert), the M3-F04 contract (show level and charging state).
- **API:** `GetSystemPowerStatus` for every reading. Notifications only, with no timer:
  `RegisterPowerSettingNotification` for `GUID_ACDC_POWER_SOURCE` and `GUID_BATTERY_PERCENTAGE_REMAINING`, plus
  `PBT_APMPOWERSTATUSCHANGE`. Several notifications for one change are collapsed: an identical reading is ignored.
- **Rules (`Halo.Core/Power/BatteryMonitor`):**

  | Event | Alert |
  |---|---|
  | Charger connected / disconnected | "Charging" (or "Plugged in" when full) / "On battery". Normal, 2.5 s, key `power` |
  | On battery at or below the low threshold | "Battery low". High, 5 s, once per discharge |
  | On battery at or below critical | "Battery critical". Critical, 8 s, **allowed over full screen**, once per discharge |
  | Unplugged below a threshold | the battery alert instead of "On battery" |
  | Start-up | an alert only when already low or critical on battery |
  | No battery, unknown level | no level alerts |

  An alert is re-armed only by charging or by the level climbing 2 points above its threshold. A level wobbling
  around the line does not repeat it.
- **User-visible:** the open clock view shows a battery glyph (0–10 bars, the charging set while plugged in) and
  "82%" at the top right, in place of the "Halo" wordmark. The UIA description adds "Battery 82 percent,
  charging". The media view does not show it (its top row is the title).
- **Settings:**
  - "Show battery level and charger or low-battery alerts" (`showBattery`, default on);
  - `batteryLowPercent` (5–50, default 20) and `batteryCriticalPercent` (2–25, default 10) are in the settings
    file only. If critical is not below low, critical falls back. Off unregisters the notifications.
- **Files:** `Halo.Core/Power/Battery.cs`, `HaloSettings.cs`, `Halo.App/Interop/Native.cs`, `IslandWindow.cs`,
  `IslandVisuals.cs` (status cluster), `SettingsWindow.cs`.
- **Unit tests (27 new, `BatteryTests`):** mapping of 7 raw status combinations (charging, battery, full, no
  battery, unknown, unknown level, invalid level); glyphs; spoken text; start-up rules; one alert per threshold
  per discharge, including exactly at 20 % and 10 %; no repeat while the level wobbles, and a repeat after climbing
  above the hysteresis; charger alerts and "Plugged in"; unplugging while low; no battery; unknown level (no bar);
  thresholds changed while running; threshold validation, including critical ≥ low. Unit 300/300.
- **Desktop tests (3 new):**
  - **Real:** the start-up reading equals the test's own `GetSystemPowerStatus` (this machine: AC, 100 %, not
    charging), with no alert, and the open island's UIA description has "Battery 100 percent, plugged in";
  - **SYNTHETIC** (`WmApp+51`, diagnostics only, raw status values through the real path): unplug → power pill;
    re-plug updates the same pill; 21 → 20 % gives one High battery alert replacing it; 19 % adds nothing; 10 %
    gives Critical; an identical notification is ignored;
  - **Off:** no reading and no alerts, even for a synthetic 5 %.
  Island desktop 44/44.
- **Runtime check:** screenshot of the open island showing the plugged-in glyph and "100%".
- **Hardware-dependent and not verified:** a real unplug or plug, a real discharge through the thresholds, a
  desktop without a battery (unit-tested mapping only), and battery saver.
- **Commit:** see the next entry.

## Next recommended action

1. Owner reviews E1–E3 (E2 now has a precise proposal).
2. Next feature: **full-screen suppression (FR-ISL-09, spike S7)**. The island is topmost, so it now
   covers full-screen video, games and slideshows. Hide it while a full-screen app is in the foreground
   on its monitor, and add the tray item "Show island now" as the override that FR-ISL-09 requires.
   This builds on the tray menu and the controller, starting with the §4.3 full-screen matrix.
3. Then the keyboard focus path (FR-ISL-07) and a first Settings surface for dwell, grace and
   click-only mode.
