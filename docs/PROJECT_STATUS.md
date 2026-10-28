# Project Status — Project Halo

Last updated: 2026-10-28

## Current phase

**Phase 4 — M1a S1/S2 gate closed provisionally; first island vertical slice built and running.**

- The S1/S2 matrix ran at 120 Hz (2 of 3 sessions per technique), then the owner stopped it.
- Calibration is VALID at 120 Hz and INVALID at 60 Hz.
- Both WPF techniques **FAIL PB-05** (frame pacing).
- Option C (Win32 + `Windows.UI.Composition`) passes PB-05 and every automated S2 item except the
  expected UI Automation gap.
- ADR-001 §13.1 records **C as PROVISIONALLY SELECTED**. It is not SELECTED, because PB-02 and the
  60 Hz results are INVALID and some sessions and manual checks were not run.

The product island (`src/Halo.App`) is the first vertical slice on option C.

S3–S8 (Execution 2) have not started.

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

## 2. What the island does now (vertical slice, 2026-10-28)

`src/Halo.App` (`Halo.exe`) and `src/Halo.Core`:

- **Placement:** a black pill centred at the top of the primary monitor's work area, 6 DIP below the
  top edge. It scales with DPI and re-places itself after DPI, display, work-area or Explorer changes.
- **Compact view:** the short date on the left, the time on the right, with a smaller AM/PM.
- **Expanded view:** opens and closes on click (click mode, FR-ISL-04). It shows the time in large
  type, the full date, a day-progress bar and a "Day N · X% of today has passed" line.
- **Animation:** 350 ms shape morph (size, position and corner radius) run by DWM. Content cross-fades
  inside a clip that follows the shape.
- **Feedback:** hover lightens the fill and edge; press scales the island to 96.5 %.
- **Reduced motion:** the system "Show animations" setting gives instant shape changes and cross-fades.
- **Window:**
  - topmost, non-activating tool window, not in the taskbar or Alt+Tab;
  - input only inside the visible shape (window region), so everything else passes clicks through;
  - never takes the foreground on click.
- **Clock:** one-shot timer to the next minute boundary. It also refreshes after a time change and on
  resume.
- **Accessibility:** a UI Automation provider exposes a button named "Halo, <time>, <date>" with Invoke
  and ExpandCollapse. The day progress is exposed as the full description when expanded.
- **Lifecycle:**
  - right-click menu with Expand/Collapse and **Exit Halo**; focus returns to the previous window;
  - `Halo.exe --exit` closes a running island;
  - a second launch expands the running island (single instance);
  - `--diagnostics` prints privacy-safe JSON events for tests.

Not in this slice (by scope): media, volume, battery, network, notifications, keyboard indicators,
timers, to-dos, tray icon, settings, persistence, logging to disk, full-screen detection (S7),
hover-intent expansion (FR-ISL-03), keyboard focus path (FR-ISL-07), packaging.

## 3. Measured on the product island (SMOKE, not budget verdicts — report §9.5)

| Measurement | 60 Hz | 120 Hz |
|---|---|---|
| Start → first frame, p50 of 5 warm launches | 150.5 ms | 156.9 ms |
| Idle 90 s: CPU / attributed frames | 0 ms / 2 (minute clock) | 15.6 ms / 2 (minute clock) |
| Private memory / working set | 17.3 / 58.6 MB | 17.4 MB |
| Transitions (20, UIA-driven) | 20 valid; 3 with one long frame; max 35.7 ms | 9 valid (11 with one capture-coalesced frame); 0 long frames; max 10.0 ms |
| Exit p50; processes left | 64 ms; 0 | 70 ms; 0 |

**Text device choice (measured with `Get-Process`, 2026-10-28):** private memory was about 62 MB when
Direct3D picked the NVIDIA GPU (its driver loaded) and 68 MB on the Intel GPU. With WARP it was 15.5 MB.
The island therefore rasterises text with WARP; shape animation still runs on the GPU in DWM.

## 4. Tests (2026-10-28, clean Release build: 0 warnings, 0 errors)

| Suite | Command | Result |
|---|---|---|
| Unit (all projects) | `dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"` | **108/108** passed (spikes 79, Halo.Core 24, Halo.App 5) |
| Island desktop tests | `dotnet test --project tests/Halo.App.Tests/Halo.App.Tests.csproj -c Release -- --filter-trait "Category=Integration"` | **6/6** passed at 120 Hz (5/5 at 60 Hz, before the menu test was added) |
| Spike desktop tests | `dotnet test --project spikes/tests/Halo.Spikes.Tests/Halo.Spikes.Tests.csproj -c Release -- --filter-trait "Category=Integration"` | **5/5** passed |
| Locked restore | `dotnet restore Halo.slnx --locked-mode` | passed |

Run the two desktop suites one at a time: both put windows at the top of the screen.

## 5. Decisions required from the owner

| # | Decision | Recommendation |
|---|---|---|
| **E1** | Accept option C as the provisional rendering path (ADR §13.1) | Accept |
| **E2** | Does a once-a-minute clock redraw count as an activity under PB-03 ("0 frames/s settled")? | Yes: a visible clock is an activity; keep PB-03 for a truly idle island (no clock) |
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
- Hover does not expand the island (click mode only); FR-ISL-03 hover-intent is not implemented.
- No tray icon: exit is through the right-click menu or `Halo.exe --exit`.
- Primary monitor only; multi-monitor untested (one display).

## 7. Session notes

- 2026-10-28: the owner asked for the 60 Hz switch to be done for them. A one-off script outside the
  repository changed the refresh rate dynamically (not saved), and 120 Hz was restored afterwards.
- Two background runs were stopped by the host for low memory. The 120 Hz batch was then restarted as
  a separate process, and the owner stopped it during session 3.
- The canonical repository is `C:\dev\Notch\advanced-windows-island`; the OneDrive copy was not touched.

## Next recommended action

1. Owner reviews E1–E3.
2. Next feature (M2): **hover-intent expansion with leave grace (FR-ISL-03)**, built on the existing
   state machine, with unit tests for the 120 ms dwell and the 400 ms grace.
3. Then the tray icon with Show/Hide and Exit (FR-APP-01).
