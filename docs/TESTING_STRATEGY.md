# Testing Strategy — Project Halo

| Field | Value |
|---|---|
| Status | **PROPOSED** (Phase 3) |
| Date | 2026-10-27 |
| Related | ADR-001 §10, `PRODUCT_REQUIREMENTS.md`, `DEVELOPMENT_WORKFLOW.md` §4 |

No tests exist yet. Results quoted anywhere in the project must come from actual runs; skipped
checks are reported as "NOT RUN — reason" (DEVELOPMENT_WORKFLOW).

## 1. Test levels

| Level | What | Where it runs | Gate |
|---|---|---|---|
| Unit | `Halo.Core`: state machine, arbitration, alert coalescing, timers/reminders with virtual time, settings validation/migrations, geometry (DPI, work area, monitor selection), LRC parser (Adv), log redaction | Any machine, `dotnet test` | Every commit touching code |
| Integration | `Halo.Platform.Windows` adapters and `Halo.Infrastructure` against the real OS/filesystem | Dev machine; tests tagged `Category=Integration`, some require manual setup (player running, charger) | Before merging a module milestone |
| UI automation | App launched as a process; driven through UIA (FlaUI) | Dev machine, interactive desktop | Before milestone completion |
| Accessibility | Automated rules (Axe.Windows) + scripted manual checks | Dev machine | Before milestone completion and release |
| Performance | PB-01…PB-13 scripted measurements | Target machine, Release build | Spike S1, each milestone, release |
| Packaging | MSIX install/upgrade/uninstall/startup | Clean machine (spare PC or VM — **not available yet**) | Release |
| Soak | 8 h run with scripted activity | Target machine | Release |

## 2. Tooling (PROPOSED; installation in Phase 4 needs approval where noted)

| Purpose | Tool | Licence | Install? |
|---|---|---|---|
| Test framework | xUnit v3 | Apache-2.0 | NuGet (project-local) |
| Virtual time | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) | MIT | NuGet |
| UI automation | FlaUI (UIA3) | MIT | NuGet |
| Accessibility rules | Axe.Windows | MIT | NuGet |
| Accessibility manual | Accessibility Insights for Windows; Narrator | MIT; built-in | Accessibility Insights: **approval** |
| CPU/wake-ups | Windows Performance Recorder/Analyzer | Built into Windows (WPR); WPA via ADK | WPA: **approval** |
| Frame pacing | PresentMon | MIT | **approval** |
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

Each PB-xx has a PowerShell or C# harness that records machine state (power source, refresh rate,
DPI, build), runs N iterations and writes a CSV to `perf-results/` (to be added to `.gitignore` in
M0; summaries are copied into a dated report under `docs/` by hand). Budgets are pass/fail; results are never
estimated.

### 4.9 Packaging (manual until a clean machine exists)

Install from MSIX; first-run; enable startup task and reboot; upgrade to a newer build with data
kept; uninstall removes app and data; reinstall clean. Store certification pre-check with the
Windows App Certification Kit (WACK).

## 5. Phase 4 acceptance per milestone

| Milestone | Acceptance |
|---|---|
| M0 Toolchain & skeleton | Solution builds with zero warnings-as-errors violations; one passing unit test per project; `dotnet test` green; analyzers enabled |
| M1 Spikes S1–S8 | Each spike has a written result in ADR §13 with raw numbers; S1/S2 pass or the owner approves a fallback |
| M2 Island shell | FR-ISL-01…13, FR-APP-01…05/07/08 tests green; §4.1, §4.4 pass; PB-01–PB-03, PB-05, PB-06, PB-09, PB-10 measured |
| M3 Media + system indicators | FR-MED-01…06, FR-SYS-01…04 tests green; §4.5 pass; PB-04, PB-07, PB-08 measured |
| M4 Timer + tasks | FR-TIM, FR-TSK-01/02 tests green; sleep/resume reminder test passes |
| M5 Accessibility + packaging | §4.7 all pass; MSIX §4.9 on a clean machine; PB-11 soak; privacy checklist |
