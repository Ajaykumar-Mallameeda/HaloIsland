# Testing Strategy — Project Halo

| Field | Value |
|---|---|
| Status | **PROPOSED** (Phase 3) |
| Date | 2026-10-27 |
| Related | ADR-001 §10, `PRODUCT_REQUIREMENTS.md`, `DEVELOPMENT_WORKFLOW.md` §4 |

Phase 4 (2026-10-27): the spike solution `Halo.slnx` has 82 tests in `spikes/tests/Halo.Spikes.Tests`
(77 unit, 5 desktop integration), all passing at commit `ad291ee`. Results quoted anywhere in the project must come from
actual runs; skipped checks are reported as "NOT RUN — reason" (DEVELOPMENT_WORKFLOW).

## 1. Test levels

| Level | What | Where it runs | Gate |
|---|---|---|---|
| Unit | Spikes (M0): `Halo.Spikes.Measurement` budgets, percentiles, CPU normalisation, frame pacing, log privacy. Production: `Halo.Core`: state machine, arbitration, alert coalescing, timers/reminders with virtual time, settings validation/migrations, geometry (DPI, work area, monitor selection), LRC parser (Adv), log redaction | Any machine, `dotnet test` | Every commit touching code |
| Integration | `Halo.Platform.Windows` adapters and `Halo.Infrastructure` against the real OS/filesystem | Dev machine; tests tagged `Category=Integration`, some require manual setup (player running, charger) | Before merging a module milestone |
| UI automation | App launched as a process; driven through UIA (FlaUI) | Dev machine, interactive desktop | Before milestone completion |
| Accessibility | Automated rules (Axe.Windows) + scripted manual checks | Dev machine | Before milestone completion and release |
| Performance | PB-01…PB-13 scripted measurements | Target machine, Release build | Spike S1, each milestone, release |
| Packaging | MSIX install/upgrade/uninstall/startup | Clean machine (spare PC or VM — **not available yet**) | Release |
| Soak | 8 h run with scripted activity | Target machine | Release |

## 2. Tooling (PROPOSED; installation in Phase 4 needs approval where noted)

| Purpose | Tool | Licence | Install? |
|---|---|---|---|
| Test framework | xUnit v3 (4.0.2 in M0) on Microsoft.Testing.Platform (`global.json` `test.runner`; required on the .NET 10 SDK). The platform sends usage telemetry unless `DOTNET_CLI_TELEMETRY_OPTOUT=1` or `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` is set | Apache-2.0 | NuGet (project-local) |
| Virtual time | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) | MIT | NuGet |
| UI automation | FlaUI (UIA3) 5.0.0, validated (report §8.13); spike harness only so far | MIT | NuGet |
| Accessibility rules | Axe.Windows | MIT | NuGet |
| Accessibility manual | Accessibility Insights for Windows; Narrator | MIT; built-in | Accessibility Insights: **approval** |
| CPU/wake-ups | Windows Performance Recorder/Analyzer | Built into Windows (WPR); WPA via ADK | WPA: **approval** |
| Frame pacing | S1: DXGI Desktop Duplication frame metadata, region-attributed, calibrated (built in). PresentMon only under PROTOTYPE_S1_S2_REPORT §8.14 | Built in; PresentMon MIT | PresentMon: **approval** |
| Memory | PerfView / `dotnet-counters` | MIT | approval |
| Coverage | `coverlet` / Microsoft Code Coverage | MIT | NuGet |

## 3. Design for testability

- All OS access behind Core ports (ADR §5); unit tests use fakes, never P/Invoke.
- Time through `TimeProvider`; randomness through an injected source.
- The state machine is pure: `(state, event, now) → (state, effects)`; effects are asserted, not executed.
- View models contain no Win32 calls; UI tests target UIA `AutomationId`s defined for every
  interactive element.
- Adapters emit structured `EventSource` events (timestamps) used by performance tests (PB-06–PB-08).

## 4. Key test catalogues

### 4.1 State machine and arbitration (unit)

Hover dwell below/above threshold; leave and re-enter within grace; click mode; alert while
expanded (no page change under the pointer); alert coalescing by key; priority pre-emption; stale
activity drop-out; pinned activity; suppression during full screen with `AllowedWhenSuppressed`;
reduced-motion transitions; keyboard focus and `Esc` restore.

### 4.2 Geometry and displays (unit + manual)

Monitor sets as data: 1–3 monitors, negative coordinates, top/left/bottom taskbars, auto-hide,
mixed DPI 100/125/150/175/200 %, portrait. Manual on this machine: 100/125/150/175 % on one display,
60 and 120 Hz. **Multi-monitor and mixed-DPI manual tests need an external monitor (not available).**

### 4.3 Full-screen matrix (integration/manual, spike S7)

Browser F11, browser video full screen, Movies & TV / Media Player full screen, PowerPoint slideshow,
a borderless-windowed game, an exclusive full-screen game, maximised (not full-screen) window
(must **not** hide), full-screen app on the other monitor (must not hide our island's monitor).

### 4.4 Focus and input (UI)

100 hover/expand/collapse cycles with a text editor focused: foreground window unchanged, caret
keeps receiving keystrokes; clicks just outside the shape reach the window below; tray menu does
not leave the island activated.

### 4.5 Media (integration)

Players: Spotify, Edge/Chrome with a web video, Windows Media Player. Cases: start/stop, switch
track, seek, two sessions at once, player exit, art missing, command not supported.

### 4.6 Persistence (unit + integration)

Round-trip each schema version; migration fixtures; truncated/garbled file → `.corrupt-*` + `.bak`
recovery; simulated crash between temp write and replace; read-only folder.

### 4.7 Accessibility (automated + manual scripts)

- Axe.Windows scan of the island in each presentation and of every Settings page: 0 errors.
- Narrator script: discover island, hear compact summary, expand by keyboard, operate media
  controls, hear an alert announcement, return focus.
- Keyboard-only script for Settings and island.
- High contrast: Aquatic, Desert, Dusk, Night sky — no invisible text or focus indicators.
- Reduced motion on: no scaling/morph animations; cross-fade or instant only.

### 4.8 Performance (scripted)

Running tests: unit tests with `dotnet test --solution Halo.slnx -- --filter-not-trait "Category=Integration"`;
desktop integration tests with `--filter-trait "Category=Integration"` (see `spikes/README.md`).

**S1/S2 procedure (protocol v1.3, `PROTOTYPE_S1_S2_REPORT.md` §8):**

1. **Conditions:** Release build; machine idle; Do Not Disturb on; AC power; "Best performance" mode;
   non-elevated shell; declared refresh rate (the owner switches 120 ↔ 60 Hz manually); 125 %.
2. **Start:** run `Halo.Spikes.Harness batch --hz <rate>`. Each run records the environment at start
   and end, the overlay build hash, counter samples, Desktop Duplication frame metadata and the
   overlay's event log under `perf-results/<date>/<run-id>/` (ignored by Git).
3. **Calibration first, at each rate:**
   - **BASE:** quiet region.
   - **POS:** the refresh cadence is seen.
   - **NEG/NEGCTL:** an injected 50 ms stall is detected in every transition, and in at most 2 of 20 without it.
   - **TICK:** single stray frames are detected for A, B and C.

   The rules are fixed in code (`CalibrationRules`) and in the report. If calibration fails, PB-03/PB-05 are
   INVALID at that rate and the batch stops.
4. **S1 sessions:** 120 s warm-up, 600 s settled window (PB-01/02/03/10/12), then 60 transitions
   (PB-05, first 50 valid). Three sessions per technique at 120 Hz and one at 60 Hz, interleaved A → B → C.
5. **S2:** a 12-item checklist with automated input and UI Automation. Manual items: S2-07M (Alt-Tab) and S2-11M
   (scaling 100/150/175/125 %).
6. **Validity:**
   - **INVALID conditions:** environment drift, user input, a foreign window over the region, cursor in the region, Desktop Duplication loss, calibration features enabled in a gated run, build change, or the reference app running.
   - **Reruns:** an INVALID run is repeated once as `-r1`, and both are kept. A valid FAIL is final.
   - **Defects:** an implementation defect is fixed and the affected runs are repeated, with the originals labelled superseded.
7. **Limitations:**
   - One display, so multi-monitor and mixed-DPI tests are BLOCKED.
   - PB-03/PB-05 observe the composed desktop, not app presents.
   - PB-02 cannot see threads that live between samples (a new thread ⇒ INVALID; threads that end are counted to their last sample and reported, protocol v1.3).
   - C's GPU work is attributed to DWM.
   - Results apply to the Intel UHD adapter.

Each later PB-xx harness also records machine state (power source, refresh rate, DPI, build), runs
N iterations and writes to `perf-results/`. Summaries are copied into a dated report under `docs/` by
hand. Budgets are pass/fail; results are never estimated.

### 4.9 Packaging (manual until a clean machine exists)

Install from MSIX; first-run; enable startup task and reboot; upgrade to a newer build with data
kept; uninstall removes app and data; reinstall clean. Store certification pre-check with the
Windows App Certification Kit (WACK).

## 5. Phase 4 acceptance per milestone

| Milestone | Acceptance |
|---|---|
| M0 Toolchain & skeleton | Solution builds with zero warnings-as-errors violations; one passing unit test per project; `dotnet test` green; analyzers enabled — **met 2026-10-27** for the spike solution (production projects deferred to M2, see PROTOTYPE_S1_S2_REPORT §3 C1) |
| M1 Spikes S1–S8 | Each spike has a written result in ADR §13 with raw numbers; S1/S2 pass or the owner approves a fallback. S1/S2: protocol and results in `PROTOTYPE_S1_S2_REPORT.md` — **run 2026-10-28, incomplete by owner decision; A/B FAIL PB-05, C provisionally selected (ADR §13.1)** |
| M2 Island shell | FR-ISL-01…13, FR-APP-01…05/07/08 tests green; §4.1, §4.4 pass; PB-01–PB-03, PB-05, PB-06, PB-09, PB-10 measured. **Started 2026-10-28:** vertical slice in `src/Halo.App`; unit tests in `tests/Halo.Core.Tests` and `tests/Halo.App.Tests`; desktop tests (`Category=Integration`) in `tests/Halo.App.Tests` cover placement, styles, input region, click toggle without focus change, UIA name and patterns, drawn text, right-click exit, single instance; SMOKE performance via `harness app-check` |
| M3 Media + system indicators | FR-MED-01…06, FR-SYS-01…04 tests green; §4.5 pass; PB-04 (moved here from S1, target unchanged), PB-07, PB-08 measured |
| M4 Timer + tasks | FR-TIM, FR-TSK-01/02 tests green; sleep/resume reminder test passes |
| M5 Accessibility + packaging | §4.7 all pass; MSIX §4.9 on a clean machine; PB-11 soak; privacy checklist |
