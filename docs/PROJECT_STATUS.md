# Project Status — Project Halo

Last updated: 2026-10-29 (Live Activity Engine 2.0 done, see the section before "Next recommended action"; Visual Experience 2.0 before it; see the section before "Next recommended action"; §9 for the expansion campaign, §8 for M1–M3)

## Current phase

**Phase 4 — M1, M2 and M3 built on option C.** The island is a working daily app:
- the clock;
- hover and click expansion, the tray icon, settings and full-screen suppression;
- now playing with playback controls, and a volume pill and slider;
- battery and network status and alerts, all through one alert controller.

Every feature's real status, tests and commit are in the feature register (§8). Not done: optional features (§8,
"Optional features"), release work (packaging, Start with Windows, installer), and the owner decisions in §5.

The S1/S2 rendering gate is closed provisionally:

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
| UIA events from the island provider | **Fixed** (A-F01, §9): received by a real UIA client; Narrator by ear still **not done** |
| PB-04 (music playing) | **Not measured**: media now exists, but no PB-04 measurement method has been run |
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

Built since this section was written (details in §8):
- full-screen suppression (M1-F01) and transient alerts (M1-F02);
- settings with a settings window (M2-F01/F02) and the local diagnostic log (M2-F03);
- now playing and playback controls (M3-F01/F02);
- the volume pill and the volume row in the open island (M3-F03);
- battery and network status and alerts (M3-F04/F05).

Not built yet:
- notifications (O-F01, blocked: package identity);
- keyboard indicators (O-F02, deferred);
- timers, to-dos and Start with Windows;
- the keyboard focus path (FR-ISL-07) and packaging.

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

## 4. Tests (updated 2026-10-29 at the M3 gate; clean Release build: 0 warnings, 0 errors)

| Suite | Command | Result |
|---|---|---|
| Unit (all projects) | `dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"` | **319/319** passed (M3 gate; was 132 on 2026-10-28) |
| Island desktop tests | `dotnet test --project tests/Halo.App.Tests/Halo.App.Tests.csproj -c Release -- --filter-trait "Category=Integration"` | **52/52** passed in 2 consecutive full runs at the M3 gate (§8); intermittent failures and their fixes are in the gate entry |
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
- Primary monitor only; multi-monitor untested (one display).
- Battery, charger and network transitions were verified with synthetic readings, never by unplugging or
  disconnecting (§8, M3-F04/F05).
- Status icons (network, battery) show only in the open clock view, not in the media view.

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
- **Commit:** `eea7c2b`.

### M3-F05 Network connectivity — PASS (transitions verified with synthetic readings)

- **Requirements:** FR-SYS-04 (lost / restored / limited alert, debounced 2 s), the M3-F05 contract (state and
  connection type, no activity collected, startup while disconnected, API failures, event bursts).
- **API:** `Windows.Networking.Connectivity.NetworkInformation` from the Windows SDK projection already referenced.
  No new package.
  - `NetworkStatusChanged` only posts a message. The island restarts a 2 s timer on each one, so it reads once
    after 2 s of quiet;
  - the read runs off the UI thread: `GetInternetConnectionProfile()`, then its connectivity level and adapter
    type (WLAN, WWAN, or IANA type 6 for Ethernet);
  - the start-up read is immediate.
- **Privacy:** the profile name (the Wi-Fi network's name), addresses, traffic, usage and history are never read.
  Diagnostics carry the state and type tokens only.
- **Rules (`Halo.Core/Network/NetworkMonitor`):**
  - the first known reading never alerts, even when offline;
  - lost → "Offline" (3 s); limited → "Limited connectivity" (3 s); restored or type switched → "Connected to
    Wi-Fi" or "Connected to Ethernet" (2.5 s). All are Normal priority with one key, so changes update a single
    pill, and none appear over full screen. A waiting pill is dropped after 10 s (5 s at first; changed in
    M3-F06);
  - an unchanged or unknown reading (API failure) never alerts. After a failure, the next reading is compared with
    the last known one, so a failed read cannot fake a "Connected".
- **User-visible:** a network glyph before the battery glyph in the open clock view's status cluster: Wi-Fi,
  Ethernet, globe (other), Wi-Fi-warning or Ethernet-error when limited, globe-blocked when offline. The UIA
  description adds "Connected to Wi-Fi" or "Offline".
- **Setting:** "Show network status and connection alerts" (`showNetwork`, default on). Off unsubscribes.
- **Failure:** a failed read gives `unknown` (no icon, no alert), a `network.unavailable` warning once per run, and
  a retry on the next change event. `HALO_TEST_FAULT=network` injects it.
- **Files:** `Halo.Core/Network/Network.cs`, `Halo.App/Island/NetworkWatcher.cs`, `IslandWindow.cs`,
  `HaloSettings.cs`, `SettingsWindow.cs`, `Program.cs`.
- **Unit tests (13 new, `NetworkTests`):** glyphs for 7 state/type pairs; spoken text with no network name; no
  alert at start-up, even offline; lost, limited, reconnect and Wi-Fi → Ethernet each alert once; unchanged
  readings are silent; a failed read is ignored and does not fake a reconnect; an unknown start; a burst of
  changes through the controller is one pill holding the newest text. Unit 313/313.
- **Desktop tests (4 new):**
  - **Real:** the start-up reading matches the test's own read of the same API (this machine: Wi-Fi, connected),
    with no pill, and the UIA description has "Connected to Wi-Fi";
  - **Real debounce:** ten change events 100 ms apart give exactly one read, 1.9–3 s after the last event;
  - **SYNTHETIC** (`WmApp+52`, diagnostics only): Wi-Fi → offline → (repeat ignored) → limited → Ethernet gives
    one network pill updated in place and never queued;
  - **Fault and off:** with the injected fault the state is `unknown`, the read reports its HRESULT, there is no
    pill, a later event retries, and the warning is logged exactly once. With `showNetwork` off there are no
    reads.
  Island desktop 48/48.
- **Runtime check:** screenshot of the open island: Wi-Fi glyph, battery glyph, "100%".
- **Not verified with real hardware changes:** disconnecting Wi-Fi, a cable change, a captive portal or a metered
  or cellular link (they would disrupt the owner's connection; covered by synthetic readings and unit tests).
- **Commit:** `da8cb4e`.

### M3-F06 Event presentation integration — PASS

- **Requirements:** the M3-F06 contract (priorities, transient vs. pinned, no interruption of the user,
  coalescing, expiry, full-screen suppression, feature settings, no stray timers, return to compact), ADR §6.1.
- **What was already in place:** every M3 source raises alerts through the one `IslandController` (M1-F02), so no
  feature has its own window, overlay or alert timer. The rules are now written down in one table, in ADR §6.1
  "As built, M3".
- **New:**
  - `IslandController.DropAlerts(kinds)`: turning volume, battery or network off in settings removes that feature's
    pill at once, on screen or queued, and cancels its timer;
  - the network alert's stale time is now 10 s (was 5 s). With 5 s, "Offline" raised together with a 5 s
    low-battery alert expired exactly when its turn came, a race found while writing this feature's test.
- **Unit tests (6 new, `EventIntegrationTests`, using the real alert factories):** volume, low battery and offline
  at once → battery, then "Offline", with the volume pill gone; equal priority replaces; over full screen only
  critical battery shows and the island hides again after it; nothing interrupts an island the user opened, and
  what waited expires unseen; `DropAlerts` on screen and queued, with no timer left; nothing after shutdown.
  Unit 319/319.
- **Desktop tests (4 new):**
  - **Simultaneous:** a real volume change plus synthetic low battery and offline. The battery alert is on
    screen first, "Offline" follows when it ends, the volume pill never returns, the island ends collapsed, and
    focus is unchanged;
  - **Full screen:** with a full-screen test window in front, real volume, offline and low battery stay hidden;
    critical battery appears without taking focus and leaves again;
  - **Setting off:** with the real settings window opened from Explorer's tray button, unticking the battery box
    ends a critical-battery pill at once (`source=settings`);
  - **Exit with everything busy:** a pill on screen, another queued and a network debounce pending. Exit takes
    under 5 s with code 0, no window is left, and the pending debounce never fires.
  Island desktop 52/52; the system volume was back at its starting 28 % afterwards.
- **Commit:** `9162c4c`.

### M3 completion gate — PASS (functional); performance smoke partly unusable (see below)

**Results on this machine (2026-10-29):**
- locked restore passed;
- clean Release build (`--no-incremental`): 0 warnings, 0 errors;
- unit tests 319/319;
- island desktop tests 52/52 in 2 consecutive full runs (after the fixes below);
- spike desktop tests 5/5;
- no Halo process left, and the system volume back at its starting 28 %, not muted.

**Real Windows state vs. synthetic inputs:**

| API | Tested against real Windows | Synthetic only |
|---|---|---|
| Media sessions (GSMTC) | session appears and ends, title, pause, play/pause, Next, setting off | several sessions from different apps (unit-tested selector) |
| Core Audio endpoint | external change → pill, mute, slider click and drag, mute button, re-bind, setting off | a real device switch or unplug; hardware volume keys |
| Power (`GetSystemPowerStatus` + notifications) | the start-up reading (AC, 100 %) | charger plug/unplug, discharge through 20 % and 10 % |
| Network (`NetworkInformation`) | the start-up reading (Wi-Fi, connected), the 2 s debounce on ten real-path events | lost, limited, restored, Wi-Fi → Ethernet, API failure (injected) |

**Test defects found during the gate and fixed (audit trail):**

1. **Tray test failures:** `TrayIconTogglesAndItsMenuOpensCollapsesAndExits` failed in 4 of 7 full runs and passed
   alone.
   - **Evidence:** a new failure message showed the right click landing on a 50 × 15 px button, so the overflow
     flyout was mid-animation, and the flyout was gone when the menu should have appeared.
   - **Why it was more likely now:** M3-F06's settings test left the flyout open behind the settings window, and
     clicking an icon closes the flyout with an animation.
   - **Fix:** `FindTrayButton` accepts the button only when its rectangle is unchanged 120 ms later.
     `CloseTrayOverflow` closes a flyout that is not in the foreground with its own chevron button. The new test
     closes the settings window and the flyout.
   - **Result:** the triggering trio passed 6/6 (3 of 5 failed before); then two full runs passed 52/52. The test's
     assertions are unchanged.
2. **Media tests, once:** all three failed together in one full run, with no media events at all in the island.
   - The cause was not found, and it did not recur in the following 6 full runs.
   - The failure message now lists the sessions Windows reports to the test process, so a repeat shows whether
     the test's session was registered.
   - Kept open as an intermittent with unknown cause.

**Performance smoke (`harness app-check`, SMOKE, not budget verdicts):**
- **Harness fix:** the harness computed the island's canvas without the volume row, so its grey backdrop and
  capture region were 50 px shorter than the island. `APP-SMOKE-120-013037` ran that way and is superseded.
- **Run `APP-SMOKE-120-013355`** (valid by the harness's own checks, 120 Hz, canvas 545 × 283):

  | Measurement | Value |
  |---|---|
  | Start → first frame p50 | 230.5 ms (M2: 185.2 ms; start-up now also opens the media manager, the audio endpoint, power notifications and a network read) |
  | Idle 90 s | 15.6 ms CPU (one scheduler tick), 1 clock update. Frames: **160, not attributable to Halo**, see below |
  | Private / working set | 19.3 / 73.6 MB (M2: 17.4 / 60.7 MB) |
  | Transitions | 17/20 valid (3 capture-coalesced); 1 with one long frame; p95 8.9 ms; max 15.4 ms |
  | Exit p50 | 75.7 ms; 0 processes left |

- **Idle frames:** the harness baseline calibration (`dev-cal --kind base`, no Halo running) **failed** with 102
  attributed frames, so the screen region was changing without Halo. A 30 s diagnostics run of the island logged
  no periodic event: only start-up, one clock tick and one reading per feed. The 160 idle frames are therefore
  **not** evidence about Halo. The idle-frame smoke must be repeated on a quiet desktop (no terminal or editor
  output on screen). PB-03 is unchanged and still an owner decision (E2).
- **Not measured:** PB-04 (music playing), PB-07 as a frame time (only the 5.3–6.1 ms change → pill state time),
  PB-08.

### Optional features

- **O-F01 Notification previews — BLOCKED.** The supported API (`UserNotificationListener`) needs package
  identity and the `userNotificationListener` capability, so it needs an MSIX-packaged Halo (SECURITY_AND_PRIVACY
  §4). Halo is unpackaged, and packaging is release work that was not authorised. No scraper or workaround was
  built, and the feature stays off.
- **O-F02 Keyboard and input indicators — DEFERRED (owner decision).**
  - Caps Lock has no hook-free change notification. Watching it needs a global low-level keyboard hook or
    raw-input sink, which observes every key press system-wide, or polling, which the project avoids;
  - the input-language indicator needs per-foreground-thread layout reads (spike S6, not run);
  - both fall under the prompt's "broad global keyboard hook / unresolved privacy decision" rule. Nothing was
    built.

## 9. Expansion campaign feature register (started 2026-10-29)

Campaign brief: accessibility repair, motion system, activities, notifications, productivity, customisation.
Research: [`RESEARCH_EXPANSION_CAMPAIGN.md`](RESEARCH_EXPANSION_CAMPAIGN.md). Motion: [`MOTION_SYSTEM.md`](MOTION_SYSTEM.md).
Starting point: `4f26bc9`, clean tree; baseline unit 319/319, island desktop 52/52.

### A-F01 Screen-reader announcements and the island's UIA tree — PASS (Narrator by ear: NOT RUN)

- **Root cause of M2-F05:** `ProviderOptions.ServerSideProvider` was declared as `0x1`. In UIAutomationCore.h,
  `0x1` is `ClientSideProvider` and server-side is `0x2`. UIA therefore treated the island as a client-side proxy
  and delivered none of the events it raised, although each call returned `S_OK`.
- **How it was found:** bisection with a real out-of-process UIA client (RESEARCH_EXPANSION_CAMPAIGN §3):
  - Calculator, WinForms and WPF notifications arrived (13/13 each);
  - a plain Win32 window using Halo's provider got none;
  - in one process, WPF's managed provider worked and Halo's declarations did not.
- **Second defect, from the same trace:** UIA calls providers on its own threads. The old provider read window
  fields and drove the controller on those threads.
- **New design:**
  - `Halo.Core/Accessibility/IslandAccessibility` builds the tree from one immutable snapshot of what is on
    screen. It is a pure function and is unit-tested;
  - `IslandAutomationProvider` is now a fragment root. It keeps Button, Invoke and ExpandCollapse, and adds the
    Window pattern, which UiaRaiseNotificationEvent's remarks require for `WS_POPUP` windows;
  - the island's children are:
    - while compact: a live status element only while an alert is shown (`LiveSetting` polite, or assertive when
      critical);
    - while expanded: time, date and day progress (a progress bar), network and battery text, media summary,
      track position, and Previous / Play–Pause / Next buttons (only those the session enables);
    - with the volume row: a mute toggle button and a volume slider (RangeValue, 0–100);
  - reads come only from the published snapshot. Commands are posted to the UI thread and re-checked there, so a
    stale request is dropped;
  - events are raised only for real changes: expand state, name, structure (children invalidated), live region,
    and toggle/value of controls. Clock ticks change only the island's name; no notification is ever raised for
    them;
  - an element that has left the screen returns `UIA_E_ELEMENTNOTAVAILABLE`. After shutdown, nothing is exposed
    and nothing is posted;
  - not keyboard-focusable and no `SetForegroundWindow`, so there is no focus stealing (FR-ISL-06).
- **Tests:**
  - unit, +19 (11 Core tree, 8 provider rewrites and additions): unit 338/338;
  - desktop, +7, all with a real UIA3 client:
    - an alert notification is **received** on a desktop-subtree listener, with text and MostRecent processing;
    - a critical alert arrives as ImportantMostRecent;
    - the ExpandCollapseState change is received;
    - expanding and collapsing raise no notifications;
    - the expanded tree has the expected roles, the slider equals the real endpoint volume, bounds lie inside the
      window, and the children are gone after collapse;
    - an alert is a live child while shown and is gone when it ends;
    - Invoke through UIA does not change the foreground;
  - island desktop 59/59.
- **Not done:** Narrator by ear (needs a person); Axe.Windows (not installed); a high-contrast review.
- **Reduced motion:** announcements and the tree do not depend on animation (existing reduce-motion test unchanged).

### B-F01 Motion system — PASS (specification)

- [`MOTION_SYSTEM.md`](MOTION_SYSTEM.md) defines nine motions: morph, fade-out, fade-in, settle, swap, level,
  emphasis, arrival and feedback.
- **Timing:** durations stay ≤ 450 ms, against Apple's 2 s ceiling.
- **Rules:**
  - state owns motion;
  - one plan per channel;
  - an interrupted animation restarts from its current value;
  - nothing animates while idle;
  - suppression wins;
  - each motion has a reduced-motion alternative;
  - nothing is conveyed by colour alone;
  - durations are times, not frame counts (60 and 120 Hz).
- The 350 ms morph is unchanged, because it is the one with PB-05 evidence.

### B-F02 Shared motion engine — PASS

- `Halo.Core/Motion`:
  - `MotionVocabulary` holds the durations, delays and reduced-motion alternatives;
  - `MotionPlanner` (pure) turns a change into steps: transition, swap, level, arrival and emphasis. Equal values
    plan nothing;
  - `MotionTracker` keeps one plan per channel. A superseded plan's completion is ignored, and after `Shutdown`
    nothing begins or completes.
- `IslandVisuals` executes plans and no longer hard-codes durations. New motions:
  - **swap:** when one alert replaces another in place, the content dips to 35 % and back, never to zero;
  - **level:** the alert bar animates between values, for example volume 30 → 35;
  - **arrival:** a 4 % grow-and-return, used by the timer.
- `IslandWindow` gates `WmTransitionDone` through the tracker, which replaces the transition counter, and shuts it
  down on exit.
- **Tests:**
  - unit, +12 (`MotionTests`): expand and collapse plans, reduced motion, the ceiling for every pair of
    presentations, coalescing, the emphasis window, 50 rapid transitions (bounded, 49 stale completions ignored),
    independent channels, cancel and shutdown;
  - island desktop: 59/59, including the existing transition-timing and reduce-motion tests.
- **Not measured:** PB-05 for the new swap, level and arrival motions. They are DWM animations of 90–260 ms, but
  no frame evidence was collected.

### B-F03 Activity model and lifecycle — PASS (model; first consumer is G-F01)

- **`Halo.Core/Activities`: `IslandActivity` fields:**
  - stable ID, kind and priority;
  - glyph, compact and redacted text;
  - title, subtitle and a 0–1 level;
  - ongoing or transient, plus an expiry;
  - action descriptors (fixed tokens, never content);
  - privacy class and accessible text;
  - whether it is allowed over full screen and in quiet mode;
  - an emphasis flag.
- **`ActivityBoard`:**
  - **one primary:** the user's explicit choice while it exists and is eligible; otherwise the highest priority,
    then the most recently changed;
  - **coalescing:** an equal update changes nothing and does not reorder;
  - **bounded:** at most 8; the lowest-priority, oldest activity is dropped first;
  - **expiry:** `NextExpiry` arms one timer;
  - **explicit switching:** `Select` and `SelectNext`, in a stable order;
  - **quiet mode and full-screen suppression:** filter eligibility without discarding anything;
  - private text is redacted unless previews are allowed.
- **Relation to alerts:** transient system alerts keep their tested `IslandController` path (M1-F02/M3-F06).
  Activities are the ongoing layer. One `IslandController` still owns presentation state.
- **Tests:** unit +10 (`ActivityBoardTests`, on a manual clock with separate wall and monotonic time); Core 266/266.

### G-F01 Focus timer and C-F02 timer animation — PASS

- **Engine (`Halo.Core/Timers/CountdownTimer`):**
  - remaining time comes from `TimeProvider.GetTimestamp` (monotonic), so a wall-clock change (by hand, DST, network time) neither shortens nor lengthens it;
  - start (1 s to 24 h), pause, resume, reset, +1 minute and snooze;
  - completion fires exactly once.
- **Persistence (`state.json`, FR-TIM-02):**
  - a running countdown stores its wall-clock end and a paused one stores its remaining time;
  - a countdown that ended while Halo was closed restores as completed ("ended while Halo was closed");
  - a clock moved back while closed is capped at the duration.
- **Sleep:** the state is captured on `PBT_APMSUSPEND` and restored on resume from wall-clock end times, so the countdown is right whatever the sleep length.
- **Waking (no idle cost):**
  - one one-shot Win32 timer fires at the next whole second only while the island can be seen;
  - while hidden, locked or with the display off, it fires only at completion.
- **Shared store (`Halo.Core/Storage/AtomicJsonFile`):**
  - size cap, `*.tmp` + `File.Replace` + `*.bak`, corrupt file set aside, unchanged content not rewritten;
  - used by `StateStore` and by later stores.
- **Visuals (C-F02):**
  - **compact:** glyph, `m:ss`, a thin remaining-time bar, and "+N" when other activities exist;
  - **expanded:** an activity page with large digits, "Timer · 1 min 35 s · ends 3:03 AM", a progress bar and up to three action buttons (pause/resume, +1 min/reset, cancel; dismiss and snooze once done);
  - **last 10 s:** one gentle brightness step per second (Emphasis), with no flashing and no colour-only state;
  - **completion:** a "Time's up" alert with the Arrival motion, then "Time's up" stays in the pill until dismissed, or for 10 minutes;
  - reduced motion turns off Level, Emphasis and Arrival.
- **Pages (FR-ISL-08):**
  - the open island has an activity, a now-playing and a clock page;
  - opening shows the activity page first;
  - page dots are 24 DIP targets, and the mouse wheel also steps pages.
- **Ways to start:** tray › Timer (1/5/10/15/25/45 min, pause/resume, cancel or dismiss) and the island's action buttons.
- **UIA:**
  - the activity is a progress bar named e.g. "Timer, 4 minutes left". The name is coarse (minutes, then tens of seconds, then seconds), so it is not a new value every second;
  - the actions and page dots are buttons;
  - "Timer finished" is announced;
  - the island's name includes the activity.
- **Screens checked:** compact countdown, expanded card, clock page with dots, completion alert and compact "Time's up". The screenshots were taken during the run and not committed.

### G-F02 Focus sessions — PASS

- `FocusSession` runs on the same countdown: work, short break, and a long break after each cycle. It defaults to 25/5/15 minutes with 4 cycles, within bounds.
- A phase ending starts the next one, with an alert ("Short break time", "Back to focus").
- Controls: pause/resume, skip and stop, in the tray and as island buttons.
- A phase that ended while Halo was closed resumes at the next phase, paused, so nothing is replayed.
- No gamification and no accounts.

### G-F03 Do Not Disturb (Halo quiet mode) — PASS

- **Modes:** for 1 hour, until tomorrow (the next local midnight, DST-aware), and until turned off. The choice is persisted and its end is re-checked after a time or zone change.
- **Windows settings:** the Windows Focus / Do Not Disturb setting is **not** touched.
- **Precedence**, highest first:
  1. the user opening the island;
  2. full-screen suppression;
  3. quiet mode.
- **What quiet mode does:**
  - critical alerts and the completion of a timer or focus session the user started still appear;
  - other alerts are counted, not queued, and what was already queued is dropped;
  - activities not allowed in quiet mode cannot be primary.
- **When it ends:** one summary alert ("1 alert while Do Not Disturb was on") replaces the missed items.
- **Indicators:** the tray tooltip and the island's UIA name say when it is on.

**Tests (G-F01/G-F02/G-F03/C-F02):**
- unit, +40:
  - countdown: run, pause, resume and complete once; wall-clock jumps; add, snooze and reset; bounds; once-a-second `NextChange`;
  - format and spoken text; restart, missed and capped restore;
  - activity states, actions and emphasis window; focus cycle with the long break; skip, stop and restore;
  - quiet durations, including a custom DST zone; quiet gate; the controller holding alerts in quiet mode;
  - state round trip and strictness; corrupt-file recovery from the backup; duration labels;
- unit total 398/398;
- desktop, +7:
  - countdown in the pill, completion alert and the UIA notification **received**, dismiss through the UIA button;
  - pause holds the time and resume works;
  - a running timer survives a real exit and restart (`state.json` holds `endUtc`);
  - pages and page buttons, and reopening starts on the activity page;
  - the focus phase change with its alert;
  - quiet mode holds an alert, lets the timer through and sends the summary;
  - the tray submenu starts a timer.
- **Test change:** the tray Exit step now selects the last item with Up instead of counting Down presses. The assertion is unchanged.
- **Not done:** real sleep and resume (needs the machine to sleep), and a real wall-clock change on the desktop (needs administrator rights). Both are covered by unit tests on a manual clock.

### D-F01 Notification framework — PASS

- **`Halo.Core/Notifications/NotificationCenter`** is provider-neutral. A `HaloNotification` holds:
  - the provider token and the source ID, which is the deduplication key and is never shown;
  - priority;
  - title and preview, which are private;
  - a redacted text, a glyph and the time it occurred;
  - the actions the provider supports.
- **Rules:**
  - **deduplication:** one provider and source ID is shown once. A push and a poll of the same item, or a
    re-submission after a dismissal, show nothing. The memory is bounded at 512 entries and pruned in arrival order;
  - **per-provider policy:** enabled, preview, minimum priority, alert duration and lifetime, over full screen, in
    quiet mode;
  - **rate limit:** at most 6 arrivals a minute per provider by default. More are accepted into the island
    without an alert, so a burst never floods;
  - **expiry**, dismissal, and dropping a provider's live items when it is turned off.
- **Output:** an island activity (private unless previews are allowed; actions plus Dismiss) and an arrival alert.
  Quiet mode and full screen apply through the board and the controller (G-F03).
- **Content:** kept in memory only. The center never logs or persists it.

### D-F02 Windows app notification compatibility — INVESTIGATED; no Windows toast path added (owner decision L-1)

- Windows App SDK `AppNotificationManager` works for unpackaged apps but needs:
  - the Windows App SDK runtime, a new framework dependency;
  - a COM activator registration, so a click can relaunch the app;
  - it does not work in elevated apps.
- Reading other apps' notifications (`UserNotificationListener`) needs package identity, so it stays **BLOCKED** (O-F01).
- **What was built instead:** Halo's own notifications (reminders now; later providers) appear in the island and
  are announced through UIA. Nothing new was installed or registered.
- Evidence: RESEARCH_EXPANSION_CAMPAIGN §1 S6, §2.2.

### D-F03 Notification presentation settings — PASS (for the providers that exist)

- **Settings:**
  - "Show due reminders and tasks" (`showReminders`, default on) turns the reminder provider on or off. Turning
    it off removes its notifications from the island at once;
  - "Show private text" (`showPreviews`, **default off**) applies to task and reminder text and to provider
    previews: pill, card, alert and UIA alike;
  - both are in the settings window, which now has 11 check boxes, and both apply live.
- **Fixed in code, not exposed:**
  - **per-provider durations, priority threshold and quiet/full-screen behaviour:** they are set by the provider's
    policy, because no second provider exists yet that the user could tell apart;
  - **reminders over full screen:** allowed, per the ADR-001 §6.2 default;
  - **reminders in quiet mode:** held and summarised.

### D-F04 Notification actions — PASS

- Actions are fixed tokens. Dismiss is handled by the center; anything else goes to the provider's handler.
- A provider's **Rejected** is reported as "That didn't work" and is never shown as success.
- An action the notification does not offer is never sent, and text from content is never executed.
- Reminders offer snooze (10 min), mark done and dismiss.

### F-F04 Local reminders and F-F05 To-do list — PASS

- **`Halo.Core/Tasks/TaskList`:**
  - create, edit, complete, reopen and delete; an optional due time; daily or weekly recurrence;
  - snooze and dismiss;
  - pending count and next due;
  - text is cleaned (control characters removed, at most 200 characters), with at most 500 items; completed
    items older than 30 days are dropped on load.
- **Recurrence** keeps the local time of day across DST, tested with a custom zone. A long gap gives the next
  future occurrence, not a replay of each missed day.
- **Persistence:** `tasks.json` through `AtomicJsonFile`. The text is stored only on this machine and never logged.
- **In the island:**
  - a due item is a notification from `halo.reminders` (see D-F01);
  - reminders missed while Halo was closed are shown at start, once each;
  - a snoozed reminder at a new time is a new notification;
  - editing, completing or deleting a task removes its reminder from the island;
  - one one-shot timer waits for the next alert. Time changes and resume re-check it.
- **Tasks window:** tray › "Tasks and reminders…" opens a native window with a list, a text field, "Remind me on"
  date and time pickers, Repeat, and Add, Save changes, Complete, Delete and New buttons.
- **Tests:**
  - unit, +17 (`NotificationCenterTests` 9, `TaskListTests` 8). They found two real bugs, both fixed:
    - `Clean` trimmed before removing control characters;
    - deduplication pruning could forget the newest item when timestamps were equal;
  - unit total 415/415;
  - desktop, +4:
    - a due reminder is shown and announced as "Reminder" with no task text in the name, announcement or log; it
      is snoozed through UIA and `tasks.json` records the snooze;
    - with previews on, the text is shown, and dismiss stops it;
    - a reminder missed while closed is shown at start;
    - the tasks window adds and completes a task through UIA.

### E-F01–E-F04 Email and F-F01–F-F03 Calendar — PASS with test doubles; live use BLOCKED (owner sign-in)

- **Provider layer (`Halo.Core/Accounts`), vendor-neutral:**
  - `IMailProvider` and `ICalendarProvider` return snapshots plus a status: not connected, connected, sign-in
    required, permission denied, throttled or unavailable;
  - `IAccessTokenSource` owns sign-in and token storage. The shipped `NotConnectedTokenSource` never touches the
    network;
  - `LinkPolicy` allows only HTTPS links on Outlook and Teams hosts, never `file:`, `javascript:`, look-alike hosts
    or user-info URLs.
- **Microsoft Graph adapter (`GraphClient`):**
  - read-only GET requests to graph.microsoft.com only;
  - delegated least privilege: `User.Read Mail.ReadBasic Calendars.ReadBasic offline_access`. `ReadBasic` excludes
    body, preview and attachments (RESEARCH S7);
  - `$select` asks only for the fields shown;
  - calendar times are requested in UTC;
  - 10 s timeout and a 1 MB cap;
  - HTTP 401/403/429/5xx map to statuses, and nothing is read from error bodies.
  - It never sends, deletes, accepts, declines or modifies.
- **`MailWatcher` (E-F03):**
  - the first read after connecting is a baseline: existing unread mail is counted, not announced;
  - each new message is submitted once, and the center deduplicates a push and a poll;
  - "N unread" is an activity;
  - "Open in Outlook" appears only with an allowed link;
  - polling every 2 min, exponential backoff to 30 min on failure, `Retry-After` honoured on 429, and **no retry
    loop** when not connected, sign-in is required or permission is denied.
- **`CalendarWatcher` (F-F02/F-F03):**
  - the glance shows the meeting in progress, else the next one within 12 h. Cancelled and all-day events are
    skipped;
  - the reminder comes 5 min before, once per start time; a moved event reminds again at its new time and a
    cancelled one is withdrawn;
  - join and open links are offered when they are allowed;
  - private or confidential events never show their subject, whatever the preview setting;
  - nothing is modified.
- **E-F04 other providers:** the extension points exist; no second live provider was built (prompt §8 rule).
- **Tests:** unit +11 (`AccountProviderTests`) with a fake HTTP handler and fixtures shaped like the documented
  Graph responses. Core 334/334. **No real account was used.**
- **BLOCKED, owner action needed for live use:**
  1. register an Entra ID app as a public client with a loopback redirect;
  2. grant the delegated scopes above;
  3. sign in interactively.
  Then add a token source using the system browser with PKCE, and Windows Credential Manager for token storage.
  No secret is needed for a public client.
- **Not wired into the island yet:** without a token source there is nothing to show, and no placeholder
  settings were added.

### I-F05 Keyboard shortcut — PASS

- **Setting:** Settings › "Shortcut to open or close", **Off by default**. The choices are Ctrl+Alt+H,
  Ctrl+Alt+Space and Ctrl+Shift+H. Each needs Ctrl, so no bare key is ever taken from typing.
- **Mechanism:**
  - `RegisterHotKey` with `MOD_NOREPEAT`. There is no keyboard hook, so Halo never sees other keystrokes;
  - it is unregistered when changed and on exit.
- **Behaviour:** the shortcut toggles the island through the same controller as the tray, and never takes focus.
- **Conflict:** when another app already owns the shortcut, Windows refuses it (error 1409). Halo logs this,
  shows "In use by another app" next to the setting, and does not retry in a loop. The tray icon stays the
  fallback.
- **Not built:** moving keyboard focus into the island (the rest of FR-ISL-07). The island is non-activating by
  design (FR-ISL-06).
- **Tests:**
  - unit, +3: token round trip, modifiers, defaults and invalid values;
  - desktop, +3:
    - off by default;
    - a real Ctrl+Alt+H chord sent with `SendInput` opens and closes the island, and focus is unchanged;
    - the shortcut already held by the test process is reported with error 1409.

### J-F01 Manual media-session switching — PASS

- **Switching:**
  - with two or more sessions, a switch button (36 DIP, clear of Next) appears at the end of the transport row;
  - the artist line adds a short app name taken from the AppUserModelID, for example "Spotify" or "ZuneMusic",
    only when it is needed to tell sessions apart.
- **Rules:**
  - the user's choice holds while its session exists, even if another session is the one playing;
  - when the chosen session ends, the island falls back to Windows' current session (FR-MED-03);
  - a command pressed before a switch cannot reach the new session (the existing stale-command check);
  - app IDs and names are never logged; diagnostics carry the session count and index only.
- **UIA:** "Switch media source (1 of 2)" button.
- **Tests:**
  - unit (`MediaSwitchSeekTests`): the preference wins and falls back, next wraps, app names are shortened, and
    the switch button appears only with several sessions and does not overlap Next;
  - desktop: two **real** sessions, the test's silent MediaPlayer and a silent MediaPlayer hosted by Windows
    PowerShell. The UIA switch button changes the selected session, and the choice holds.

### J-F02 Seeking — PASS

- **When it is offered:** only when the session reports `IsPlaybackPositionEnabled` and has a timeline.
- **How it works:**
  - a click on the progress bar seeks there, on release, so slipping off the bar cancels;
  - through UIA, the position becomes a slider, and `SetValue` seeks;
  - a session that cannot seek keeps a read-only progress bar, and nothing offers to seek.
- **Mechanism:** `TryChangePlaybackPositionAsync` from the session's own start and end times. The result token is
  logged: accepted, refused or failed.
- **Tests:**
  - unit: seek band only when seekable, fraction clamping, slider only when seekable;
  - desktop, against a **real** session: the test MediaPlayer reported seeking enabled, UIA `SetValue(50)` was
    accepted, and the player's own position moved to the middle of the file (the test did **not** take its
    "not seekable" skip path).

### I-F01 Theme system — PASS

- **Model:** `Halo.Core/Theming/IslandPalette` gives every colour a role: fill, hover, edge, three text levels,
  track and bar.
- **Themes:**
  - **Dark**, the original look and the default;
  - **Light**;
  - **Match Windows**, which follows the app mode through `UISettings`, the documented Win32 approach, and updates
    live on `ImmersiveColorSet`;
  - an optional **accent colour for bars**, adjusted until it stands out from the fill by 3:1;
  - **high contrast** overrides everything with the system colours from `GetSysColor` and updates live.
- **Contrast:** every text colour meets **4.5:1** against both the fill and the hover fill. This is tested.
- **Renderer:** brushes are registered by role. A theme change recolours them and redraws each text line once,
  with no animation.
- **Unchanged:** dimensions and corner radii stay at the safe values of MOTION_SYSTEM.md (no free-form sizes), and
  the animation preference is the existing reduce-motion setting.

### I-F02 Compact-pill content — PASS

- **Options:** Settings › "The compact pill shows": activities, what is playing, the date, and battery percent
  beside the time.
- **One left slot:** the first enabled item that exists wins, in a fixed priority (activity > media > date). The
  pill never rotates or reorders by itself.
- **Battery percent** appears only on machines with a battery.
- **Hiding something from the pill hides it there only.** The expanded island and UIA still show it: a running
  timer hidden from the pill is still in the island's UIA name.
- **Settings window:** now has a right-hand "Appearance" column, 800 × 600 DIP, so it does not grow taller than
  small screens. It has 16 check boxes, 3 combo boxes and 2 sliders.

**Tests (I-F01/I-F02):**
- unit, +17: palette selection, contrast in both themes, accent visibility, high contrast, the WCAG maths
  (#777 on white = 4.48), pill priority, settings round trip and bad values;
- unit total 457/457;
- desktop, +3:
  - the light theme captures as a light pill with dark text;
  - dark is the default;
  - with the date and activities hidden from the pill, its left half stays empty while a timer runs.

### C-F01–C-F07 Activity animations — PASS (C-F08 camera/microphone: see K)

All of these use the shared vocabulary (B-F01/B-F02), and none adds a motion of its own.

| Feature | What happens | Where it was verified |
|---|---|---|
| C-F01 Event arrival | The pill morphs into the alert shape and the content fades in. One alert replacing another in place uses **Swap**, a dip to 35 % with no flash. The island returns to its previous state when the alert ends. Repeated events coalesce, priority and the user-open rules apply, and suppression and reduced motion are respected | Existing alert desktop tests plus motion unit tests |
| C-F02 Timer | Pill, card, Emphasis and Arrival | G-F01 |
| C-F03 Battery | The status cluster swaps when the level, charging or charger state changes, and the battery alert's level bar animates (**Level**). No battery state is invented on machines without a battery (M3-F04) | Battery desktop tests, which use synthetic readings |
| C-F04 Volume | The volume pill's bar and the open island's slider fill and thumb glide (**Level**) on changes made elsewhere. A drag stays direct. Rapid changes coalesce into one pill (M3-F03) | Volume desktop tests against the real endpoint |
| C-F05 Media | A new track, artwork or session swaps the card and the pill's title together. Play and pause and progress do not animate. Stale metadata is replaced on a session change (watcher, M3-F01) | Media desktop tests against real sessions |
| C-F06 Network | The status cluster swaps on a change. A transient alert comes only for lost, limited or restored, after a 2 s debounce, with no repeats during reconnection (M3-F05) | Network desktop tests, which use synthetic readings |
| C-F07 Notification | The arrival pill appears, expanding shows the card with its actions, dismissing removes it, a higher priority replaces it, and bursts are summarised. Quiet mode holds it. Redacted text is used when previews are off | D-F01 and F-F04 desktop tests |

- **Reduced motion:** Swap, Level, Arrival and Emphasis are all off; the shape changes instantly.
- **Not measured:** PB-05 for the new motions. They run in DWM, last 90–260 ms and are finite, but no frame
  evidence was collected.

### H-F01 Clipboard and file shelf research — PASS

- **APIs:**
  - the standard Win32 clipboard formats are enough: `CF_UNICODETEXT` for text, and `CF_HDROP` with `DROPFILES`
    for file lists;
  - watching the clipboard would need `AddClipboardFormatListener`. Halo does **not** register one, so nothing is
    captured in the background;
  - dragging out uses the shell's own data object for the file (`IShellItem` → `BHID_DataObject`) with
    `DoDragDrop`. Halo implements only a small `IDropSource`.

### H-F02 Local shelf — PASS (drag-out added after the first gate)

- **Pinning:** tray › "Shelf…" opens a native window. **"Pin copied text or files"** reads the clipboard once,
  only when pressed.
- **Actions:**
  - Copy puts the text, or the file as a drop list, back on the clipboard;
  - Show in folder runs `explorer.exe /select,` and never opens or runs the file;
  - Remove;
  - **Clear all** deletes `shelf.json` and its backup.
- **Storage:**
  - file items are path references; files are never copied, moved or deleted;
  - a moved or deleted file shows "missing", and checking never recreates it;
  - limits: 30 items, with the oldest dropped; text up to 10 000 characters;
  - Unicode is kept and control characters are dropped;
  - the same item pinned again moves to the top;
  - `shelf.json` uses the atomic store and stays on this PC;
  - diagnostics hold the kind and counts only, never content or paths.
- **Drag-out:**
  - press on a pinned file in the list and move past the system drag threshold (`DragDetect`);
  - only copy and link are offered, **never move**, so a drop cannot move or delete the original;
  - a click still just selects the item;
  - the effect is logged as a token.
- **Tests:**
  - unit +5 (`ShelfTests`): order and duplicates, long and Unicode text, the bound and clear, missing files, and
    persistence that skips invalid or relative entries;
  - desktop +1, on the **real clipboard**:
    - copying alone captures nothing, and no file is written;
    - pin text, then Copy puts it back;
    - pin a file; after it is deleted it shows as missing and is not recreated;
    - Clear all removes `shelf.json`;
    - the log and the diagnostics contain neither the text nor the file name;
  - desktop drag test, +1: a real `SendInput` drag from the shelf's list into a WPF drop target in the test process
    delivered `[path]` with effect `copy`, and the original file was still there. Island desktop 82/82.
- **Harness note (audit trail, corrected):**
  - **symptom:** `TheTasksWindowAddsAndCompletesTasks` failed once, after a tray-menu test.
  - **First diagnosis, wrong:** a stale window handle. The harness was changed to take the island window owned by
    the new process ID. The change is harmless and was kept.
  - **Actual cause, found later:** the failing line is `AllowSetForegroundWindow` in `OpenTrayMenuDirect`. When a
    tray-menu test runs alone or in a small batch, the test process does not hold the foreground rights it passes
    on to Halo for the menu. In the full suite, an earlier test window gives it those rights. Reproduced twice with
    the shelf test run on its own; the full suites pass.
  - **This is a harness limitation, not a Halo defect.** Tray-menu tests should be run inside the full suite.

### I-F03 Per-activity customisation — PARTIAL

- **Built:**
  - each source can be turned on or off: media, volume, battery, network, reminders;
  - preview visibility (`showPreviews`);
  - pill eligibility (I-F02);
  - quiet-mode and full-screen eligibility, which are fixed per source in code (G-F03, D-F03).
- **Not built:** user-editable display duration and priority per activity type. The defaults are those of ADR-001
  §6.1 and D-F01. There is one priority table, and exposing it as free settings would let users bypass the
  quiet-mode and full-screen rules, which the brief forbids.

### I-F04 Position and display choice — PASS for the primary display; display choice BLOCKED (D7)

- **Setting:** Settings › "Horizontal position" is a slider from 800 DIP left to 800 DIP right in steps of 20,
  centred by default, and applied live.
- **Clamping:** `IslandLayout.Compute(..., offsetDip)` clamps the canvas to the work area, so the island never
  leaves the screen, including on a smaller display or one at negative coordinates.
- **Input:** shapes are canvas-relative, so hit-testing, the input region and the UIA bounds move with it.
- **Choosing a display:** the layout already takes any monitor's work area. Picking a display is **not built**,
  because it cannot be verified with one monitor (D7).
- **Tests:**
  - unit, +5: centred at zero, the offset scales with DPI, clamping on large values and on a monitor at negative
    coordinates, bounds and default;
  - desktop, +1: at −240 DIP the window sits at the expected pixel, takes clicks there and not at the old centre,
    and toggles without taking focus;
  - the settings window now has 3 sliders.

### J-F03 Brightness indicator — PASS (built-in screen, read-only)

- **Dependency:** `System.Management` 10.0.14 (MIT, Microsoft; pulls `System.CodeDom` 10.0.14), approved by the owner
  on 2026-10-29. Halo.App only; Halo.Core stays BCL-only.
- **How it works:** `BrightnessWatcher` reads `WmiMonitorBrightness.CurrentBrightness` (root\wmi) at start-up and
  subscribes to `WmiMonitorBrightnessEvent`; each change is posted to the UI thread (`WmApp + 11`).
  `BrightnessMonitor` (Core) keeps the start-up reading silent and shows a level pill on each change
  ("Brightness N percent" for UIA, 1.5 s).
- **Never writes:** Halo does not call `WmiSetBrightness`; the level only changes when the user changes it.
- External monitors expose no supported brightness API, so they show nothing (`brightness.watch` = `not_supported`).
- Setting "Show brightness changes (built-in screen)", default on.
- **Tests:** unit +2 (silent start, clamping, reset); desktop +2 (real WMI read of this PC's panel, synthetic change
  shows a pill, the setting turns the watcher off). Full desktop suite 86/86.

### J-F04 Bluetooth headphone status — PASS (connection only; battery never shown)

- **How it works:** when the default output device is bound, Halo reads the endpoint's documented
  `PKEY_Device_EnumeratorName` and `PKEY_Device_FriendlyName` from its property store. The bus is BTHENUM,
  BTHLEDEVICE or BTHHFENUM for Bluetooth.
- **Pills:**
  - "Connected · <name>" when the default output becomes a Bluetooth device;
  - "Disconnected · <name>" when it moves away from one, or there is no output at all;
  - nothing at start-up, on a re-bind of the same device, or for wired-to-wired switches.
- **Battery:** **never** shown or inferred, because Windows documents no battery API for classic Bluetooth audio.
- **Privacy:** the device name appears on screen only. Diagnostics carry a bus token (`intelaudio` on this machine)
  and 0 or 1, never the name.
- **Setting:** part of "Show volume changes"; turning it off resets the state.
- **Tests:**
  - unit, +11: classification, the connect/disconnect sequence, silent first reading, reset, name cleaning, no
    level bar;
  - desktop, +1: the **real** endpoint's bus is read (`intelaudio`, not "unknown"). Connect and disconnect are
    **synthetic**, because no Bluetooth headset is available to the tests; the pills go through the real alert
    path, and the name never reaches the log.
  - island desktop total 81/81.

### K / C-F08 Camera and microphone indicators — camera: PASS, **off by default**; microphone: BLOCKED

- **Camera source:** Media Foundation's documented sensor activity monitor (`MFCreateSensorActivityMonitor`,
  mfsensorgroup.dll; interface IDs and vtables taken from the installed Windows SDK 10.0.26100 `mfidl.h`).
  - It is a passive report of which processes stream a camera. Starting it never opens or starts a camera.
  - Media Foundation runs in lite mode (`MFStartup` / `MFShutdown`) only while the setting is on.
- **What is kept:** only "another app is streaming a camera" and the number of streams.
  - Process IDs are used once, to leave Halo itself out, then dropped.
  - Friendly names and symbolic links are never read.
  - No history is recorded.
- **Island:**
  - a "Camera on" pill when use starts, allowed in quiet mode because it is a privacy signal;
  - a "Camera in use" activity while it lasts, also in the island's UIA name;
  - it ends when streaming stops.
  - Windows' own privacy indicator remains the authority.
- **Setting:** "Show when an app uses the camera", **off by default**, with the explanation next to the box. Owner
  decision K-1 is now only whether to turn it on.
- **Microphone: BLOCKED.**
  - No documented API reports microphone use.
  - `CapabilityAccessManager` registry keys are not a documented interface.
  - Process-name heuristics are not allowed.
- **Tests:**
  - unit, +4: on once, ends when streaming stops, Halo excluded, no app identity in any text, off by default;
  - App unit, +1: every island window message ID is distinct (guards the collision found while building this);
  - desktop, +2:
    - off by default starts nothing;
    - when enabled, the **real** monitor starts. The first attempt failed with `MF_E_SHUTDOWN` until `MFStartup`
      was added;
    - the in-use transitions are **synthetic** reports. The camera was never activated.

### Expansion campaign gate — PASS (functional); Narrator by ear NOT RUN

**Results on this machine (2026-10-29):**
- locked restore passed;
- clean Release build (`--no-incremental`): 0 warnings, 0 errors;
- unit tests **462/462** (Core, App and spikes);
- island desktop **79/79**;
- spike desktop **5/5**;
- no Halo process left afterwards.

**Manual run** (temporary data folder):
- the second launch expanded the running island and exited 0, with one process running;
- `--exit` closed it, and the shell confirmed the tray icon was removed;
- no process remained;
- the log contains no content.

**Smoke `APP-SMOKE-120-034653`** (valid; SMOKE only, not budget verdicts):

| Measurement | Value |
|---|---|
| Start → first frame p50 | 266.0 ms |
| Idle 90 s | 15.6 ms CPU, **1** attributed frame (the minute clock) |
| Private / working set | 20.1 / 77.5 MB |
| Transitions | 20/20 valid; 1 with one long frame; p95 8.9 ms; max 16.9 ms |
| Exit p50 | 76.2 ms; 0 processes left |

The idle result is now clean: the M3 run's 160 frames came from other activity on the screen. PB-03 is still owner
decision E2.

**Other checks:**
- no secrets or binaries are tracked;
- 0 broken relative links in the docs;
- no remotes, and nothing was pushed;
- the OneDrive copy is clean at `9f7d5fb`, untouched.

### Second gate after the "continue" features — PASS

On the final HEAD:
- locked restore passed;
- clean Release build: 0 warnings;
- unit **480/480**;
- island desktop **84/84**;
- spike desktop **5/5**.

Smoke `APP-SMOKE-120-130250` (valid; SMOKE only):

| Measurement | Value |
|---|---|
| Start → first frame p50 | 277.1 ms |
| Idle 90 s | 31.3 ms CPU (2 ticks), 1 attributed frame |
| Private memory | 20.4 MB |
| Transitions | 20/20 valid; p95 8.8 ms; max 16.5 ms |
| Exit p50 | 73.2 ms; 0 processes left |

### Third gate after the owner's approval round — PASS

On the final code (`19a2c1a` plus the packaging commit):
- locked restore passed;
- clean `--no-incremental` Release build: 0 warnings;
- unit **482/482**;
- island desktop **86/86**;
- spike desktop **5/5**.

Smoke `APP-SMOKE-120-141648` (valid; SMOKE only):

| Measurement | Value |
|---|---|
| Start → first frame p50 | 287.2 ms. It was 337.0 ms in `APP-SMOKE-120-140922`, while the WMI brightness start-up ran on the UI thread (fixed in `19a2c1a`) |
| Idle 90 s | 0 ms CPU, 1 attributed frame |
| Private memory | 20.9 MB |
| Transitions | 20/20 valid; p95 8.8 ms; max 17.0 ms |
| Exit p50 | 78.2 ms; 0 processes left |

Also done in this round:
- the camera indicator was turned on in the owner's settings;
- a development MSIX was built and signed ([`PACKAGING_FEASIBILITY.md`](PACKAGING_FEASIBILITY.md) §5). Installing
  it needs one elevated step.

### Visual Experience 2.0 — PASS (functional and visual); high contrast and a live media session not checked by eye

Design and evidence: [`VISUAL_EXPERIENCE_2.md`](VISUAL_EXPERIENCE_2.md). Commits `6526551` (design tokens and tones) and
`d2432eb` (flush-top surface, page frame, activity visuals, in-island feature pages).

- **What changed:**
  - a flush-top silhouette with concave shoulders, a surface gradient and a rim light;
  - a header row, chevrons and centred, animated page dots on every page;
  - tone accents for every activity and alert;
  - timer and focus rings;
  - a media card with a cover accent;
  - labelled chips;
  - the Clock (Do Not Disturb, Settings), Pomodoro, To-do and Shelf pages **inside the island**. The tray menu is unchanged.
- **Tests:** unit 533/533, island desktop 86/86, spike desktop 5/5, clean Release build with 0 warnings.
- **Expectations changed:** the geometry (flush top, 460 × 196 expanded, 320 × 42 alert, the region includes the
  shoulders) and the always-present feature pages. All are documented in VISUAL_EXPERIENCE_2 §2–§3.
- **Smoke:** before `APP-SMOKE-120-161942`, after `APP-SMOKE-120-165727`, both valid (VISUAL_EXPERIENCE_2 §8).
  First frame 285 → 310 ms, idle CPU unchanged, private memory 21.3 → 24.2 MB. No budget changed.
- **Environment note:** the desktop suites need to run outside the command sandbox, because `SendInput` is blocked
  inside it. This was proven on the unchanged `b1712b1`.
- **Open:**
  - water reminder (not built: it is not an existing feature; owner decision);
  - high contrast and a live media session checked by eye;
  - page-slide frame pacing (no harness marker);
  - 60 Hz.

### Live Activity Engine 2.0 and Island Silhouette 2.0 — PASS; Windows-wide notifications BLOCKED (owner opt-in)

Report: [`LIVE_ACTIVITY_ENGINE_2_REPORT.md`](LIVE_ACTIVITY_ENGINE_2_REPORT.md). Design:
[`LIVE_ACTIVITY_ENGINE_2_DESIGN.md`](LIVE_ACTIVITY_ENGINE_2_DESIGN.md).

- **What changed:**
  - idle is a thin line, and clicks below or beside it reach the app underneath;
  - live activities grow the line into a floating pill;
  - a second activity shows in a detached bubble, and clicking it swaps the two without opening the island or taking
    focus;
  - one deterministic scheduler: critical → urgent → picked → priority → running → relevance → first started;
  - lifecycle: end, dismiss, expiry, provider stop and history;
  - new charging and offline activities;
  - Reduce Motion is respected;
  - new settings: `idleLine` and `rotateActivities`.
- **Superseded:** the VX 2.0 flush-top silhouette and its shoulders. The surface, page frame and feature pages stay.
- **Tests:** Core 479/479, App unit 14/14, spike unit 79/79, island desktop 90/90, spike desktop 5/5, Release build with
  0 warnings.
- **Smoke:** `APP-SMOKE-120-180643`, valid. First frame 319 ms, idle CPU 0 ms, private memory 24.1 MB, transition p95
  9.0 ms. More transitions contain one long frame (8 of 20); a formal PB-05 run is still needed.
- **Blocked:** reading other apps' notifications. The adapter is built, but the capability, opt-in and consent prompt
  wait for the owner's decision.
- **Not installed:** the installed MSIX is still 0.1.0.0.

## Next recommended action

The expansion campaign's report is in [`EXPANSION_CAMPAIGN_REPORT.md`](EXPANSION_CAMPAIGN_REPORT.md).

1. **A person listens with Narrator** (about 10 minutes):
   - alerts, timer completion, reminders and Do Not Disturb summaries are announced;
   - the open island's buttons and sliders can be reached.
   UIA delivery is proven by tests; speech is not.
2. **Owner decisions:**
   - E1–E3 (§5);
   - L-1, the release name and signing route (a dev MSIX is built: run `packaging\install-dev.ps1` elevated once to
     install it, [`PACKAGING_FEASIBILITY.md`](PACKAGING_FEASIBILITY.md) §5);
   - E-1, an Entra app registration and sign-in for Outlook mail and calendar;
   - O-F02, the keyboard indicators.
3. **Next engineering items:**
   - Outlook token source (system browser + PKCE, Credential Manager) once E-1 is done, then wire
     `MailWatcher`/`CalendarWatcher` into the island;
4. **Measurements on a quiet desktop:** idle smoke, PB-04 with music, PB-07 as a frame time, and PB-05 for the new
   motions.
