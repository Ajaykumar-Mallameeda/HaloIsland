# Prototype Report — Spikes S1 and S2 (architecture gate)

| Field | Value |
|---|---|
| Status | **NOT RUN — protocol PROPOSED, awaiting owner review** |
| Date | 2026-10-27 |
| Related | ADR-001 §4, §9, §13; `TESTING_STRATEGY.md` §4.2, §4.4, §4.8, §5; `PROJECT_STATUS.md` |
| Gate result | **None yet.** S1 and S2 have not been implemented or measured. Nothing in this document is a pass |

## 1. Summary

Phase 4 Execution 1 completed M0: the .NET 10 SDK is installed and a minimal spike solution builds
and tests green. It stopped **before implementing S1/S2**. The plan says: *"If S1/S2 are not
precisely specified in the documents, define the missing test protocol in a proposed documentation
change and stop for review before implementing the prototypes."* They are not precisely
specified (§2), so §5 proposes the missing protocol for review.

## 2. Why the existing definitions are not enough

The only definitions are ADR-001 §13 (one row each) and the budgets in ADR-001 §9.

| # | Source text | Gap |
|---|---|---|
| G1 | S2 pass criteria: "FlaUI test + manual checklist pass" | **No checklist exists** in any document. TESTING_STRATEGY §4.4 lists related focus/input cases but not S2's items (topmost, transparency, below full screen) |
| G2 | S2: "below fullscreen apps when suppressed" | Suppression needs a full-screen detector, which is spike S7 (out of scope). The S2 boundary is undefined |
| G3 | S1 must meet PB-02 (method "WPR (CSwitch) / WPA"), PB-03 and PB-05 (method "PresentMon") | PresentMon and WPA are **not installed**. Installing them needs approval (D8, not given), and this execution forbids third-party analysis tools. PresentMon also cannot see technique A's frames: layered windows update through `UpdateLayeredWindow`, not a swap-chain Present |
| G4 | S1 must meet PB-04 ("CPU, music playing, island compact with static indicator") | Needs a media session. Media is excluded from this execution (S3/M3). No surrogate is defined |
| G5 | S1 "at 60 and 120 Hz" | The panel runs at 120 Hz. 60 Hz exists but means changing a user display setting. Who switches it, and how, is undefined |
| G6 | S1 "which technique (A/B/C)" | Undefined: the test surface (sizes, content, animation), run count, warm-up/idle durations per technique, how 50 transitions are driven, and how a winner is chosen if several pass. ADR §4.5 calls C a fallback, but the execution plan asks for a comparison with C |
| G7 | Execution plan asks for warm startup, input responsiveness and CPU/memory during animation | Not part of S1's pass criteria (PB-06 and PB-09 are M2 budgets). Gating or informational? |

## 3. Conflicts between the Phase 3 documents and the execution plan

| # | Phase 3 documents | Execution plan | Resolution applied / proposed |
|---|---|---|---|
| C1 | M0 = solution with `Halo.Core`, `Halo.Platform.Windows`, `Halo.Infrastructure`, `Halo.App` (PROJECT_STATUS, ADR §5) | "Create only the minimum solution structure needed to build and test S1/S2"; no app shell | **Applied the execution plan** (newer, more specific). M0 created spike-only projects under `spikes/`. The production projects are deferred to M2 |
| C2 | M1 = spikes S1–S8 | Execution 1 = S1/S2 only; S3–S8 in Execution 2 after review | Applied the execution plan |
| C3 | PB-02/03/05 methods name WPR+WPA / PresentMon | No third-party analysis tools; D8 not approved | **Proposed** built-in methods (§5.5) with a calibration step (§5.6); ADR §9 method column to be amended on approval |
| C4 | ADR §5: Platform adapters use CsWin32 | Minimal skeleton | M0 uses .NET `LibraryImport` (no package) for 6 functions. CsWin32 stays the plan for production code (M2) |
| C5 | TESTING_STRATEGY §2: xUnit v3 via `dotnet test` | — | xUnit v3 4.0.2 only runs on Microsoft.Testing.Platform with the .NET 10 SDK. `global.json` opts in. That platform includes Microsoft usage telemetry, disabled only by an environment variable (recorded, owner decision Q8) |
| C6 | ADR §13 "pass criteria: all listed budgets met at 60 and 120 Hz" | "Do not adjust the acceptance targets after seeing the results" | No budget value is changed. Only *methods* and PB-04's placement are proposed, **before** any result exists |

## 4. Test setup (captured 2026-10-27; repeated automatically at the start of every run)

| Item | Value | Source |
|---|---|---|
| OS | Windows 11 Home Single Language 10.0.26300.9711 (26H2) | `EnvironmentSnapshot` |
| SDK / runtime | .NET SDK 10.0.403; runtime .NET 10.0.14 | `dotnet --info`; smoke log |
| Display | One internal panel, 1920×1080, **120 Hz** active; 60 Hz mode available; 125 % scaling (120 DPI) | `EnumDisplaySettings`, `GetDpiForMonitor` |
| GPU | Intel UHD (drives the panel) + RTX 3050 Laptop | ENVIRONMENT_AUDIT |
| Power | AC, battery 100 %, plan Balanced, overlay GUID `ded574b5-…` | `GetSystemPowerStatus`, `PowerGetEffectiveOverlayScheme` |
| Elevation | Non-elevated shell | `WindowsPrincipal` |
| Tools available | Performance counters (`GPU Engine`, `Thread`, `Process`), DWM timing API, WPR + `tracerpt` (need elevation to record), `QueryProcessCycleTime` | ENVIRONMENT_AUDIT |
| Tools not available | PresentMon, WPA, Accessibility Insights, PerfView | not installed |

## 5. Proposed S1/S2 protocol (PROPOSED — needs approval before implementation)

### 5.1 Techniques under test

| ID | Technique (ADR §4.5) | Built in S1 |
|---|---|---|
| A | WPF window, `AllowsTransparency=True` (layered) | Full |
| B | WPF window without layering; DWM-extended frame; transparent `HwndTarget` background; `SetWindowRgn` input region | Full |
| C | Win32 host window with `WS_EX_NOREDIRECTIONBITMAP` + `Windows.UI.Composition` visuals via `ICompositorDesktopInterop` | **Minimal baseline** (Q3): same shape and animation, solid fill, no text or UIA. It measures the cost of the composition path, not a finished island |

Each technique is selected with `Halo.Spikes.Overlay --technique A|B|C`. The spike is throw-away and
not reused as production code.

### 5.2 Test surface (test parameters, not product design)

| Parameter | Proposed value |
|---|---|
| Window ("fixed canvas", ADR §4.3) | 468 × 208 DIP (Expanded + 24 DIP margin each side), centred under the top edge of the primary work area; never resized during a run |
| Compact shape | 220 × 40 DIP rounded rectangle, radius 20, black fill, one 13 pt Segoe UI text line (fixed text "12:34") and one 24 DIP circle glyph (A/B only) |
| Expanded shape | 420 × 160 DIP, radius 32, same content plus a second text line |
| Transition | Width, height and radius animated together, 350 ms, cubic ease-out. Content cross-fades. Same curve in A, B and C |
| Shadow | None in gating runs. Drop shadow optional as an informational run |

### 5.3 Process structure and timing

- Two processes:
  - **Overlay** (the technique under test).
  - **Harness** (`Halo.Spikes.Harness`, console, new in S1). It launches the overlay, sends commands over the overlay's standard input (`expand`, `collapse`, `suppress`, `restore`, `quit`), and samples counters from outside the overlay process.
- Both processes timestamp with `QueryPerformanceCounter`, which is system-wide, so their events line up on one timeline.
- The overlay logs `transition.start`/`transition.end` with QPC through `SpikeLog`. Logs contain no content.
- No network, no files outside `perf-results/`.

### 5.4 Run conditions

- Release build.
- AC power; the power-mode overlay is recorded.
- 2 min warm-up after the overlay first renders (ADR §9).
- During runs:
  - Display kept on with `SetThreadExecutionState(ES_DISPLAY_REQUIRED)` (harness only).
  - The owner does not use the machine.
  - No animating windows are open.
  - Windows "Do not disturb" is on, an owner action, so toasts don't add compositions.
- Every run writes a CSV + JSON-lines file to `perf-results/<date>/<technique>-<hz>-<run>/` with the environment snapshot. A summary is copied into §6 by hand.

### 5.5 Measurement methods per criterion

| Criterion | Budget | Proposed method (built into Windows, no admin) | Optional cross-check (needs approval) |
|---|---|---|---|
| PB-01 settled CPU | ≤ 0.5 s / 10 min | Harness reads overlay `Process.TotalProcessorTime` at the start and end of a **10 min** settled window (and every 60 s for the trend); `QueryProcessCycleTime` recorded alongside | WPR CPU profile |
| PB-02 wake-ups | ≤ 2 context switches/s | `\Thread(<overlay>/*)\Context Switches/sec` raw cumulative values, every 10 s, keyed by `ID Thread`, positive deltas summed over the 10 min window ÷ 600 s. Limitation: switches on threads that start and end between samples are missed | WPR CSwitch + `tracerpt` (elevated) |
| PB-03 settled frames | 0 / s | (a) Overlay's per-process `\GPU Engine(pid_<n>*)\Running Time` delta over a 60 s settled window = 0. (b) DWM composition timeline (§5.6) over the same window: no compositions above the no-overlay baseline. Both must hold | PresentMon (B/C only; cannot see A) |
| PB-05 frame pacing | ≤ 1 long frame (> 1.5× refresh) per transition; p95 interval ≤ 1.5× refresh; 50 transitions | Harness polls `DwmGetCompositionTimingInfo(NULL)` (system-wide; NULL handle required since Windows 8.1) in a tight loop **only during transitions** and records each new `cFrame` with its `qpcCompose`. The intervals between compositions in each transition window are evaluated by `FramePacing` (M0, unit-tested). 50 transitions = 25 expand + 25 collapse, 2 s apart | PresentMon |
| PB-10 memory | ≤ 120 MB private bytes after 10 min idle | `Process.PrivateMemorySize64` at the end of the PB-01 window. `WorkingSet64` reported separately (not judged) | — |
| PB-12 settled GPU | ≤ 0.5 % 3D engine | `\GPU Engine(pid_<n>*engtype_3D)\Utilization Percentage`, sampled every 1 s over the settled window; mean and max reported, mean judged | — |
| PB-04 | ≤ 1.0 s / 10 min, music playing | **Proposed: defer to M3** (needs media, S3). S1 reports a surrogate (compact surface with a static glyph, no media) as **informational only** | — |

### 5.6 Calibration — a measurement must be able to fail

Before any technique is judged, the harness validates the DWM-timeline method on this machine at
each refresh rate:

1. **Baseline:** quiet desktop, no overlay, 60 s. Record the composition rate (expected near 0; recorded as found).
2. **Positive control:** technique C running a continuous animation for 10 s. Expect intervals ≈ 1 refresh period.
3. **Negative control:** technique A with a deliberate 50 ms UI-thread stall injected in the middle of every transition (calibration build only). The method must report a long frame in **each** stalled transition.

If 2 or 3 fails, PB-03 and PB-05 are recorded **INCONCLUSIVE** for every technique and the owner decides on PresentMon (Q5). Calibration results go into §6.

### 5.7 Run matrix

| Run | Per technique | Refresh | Duration (approx.) |
|---|---|---|---|
| Idle (PB-01/02/03/10/12) | 3 runs; worst value judged | 120 Hz | 3 × (2 + 10) min |
| Idle | 1 run | 60 Hz | 12 min |
| Transitions (PB-05) | 50 transitions | 120 Hz and 60 Hz | ~3 min each |
| Informational | warm startup p50 of 10 (after 1 discarded run); input→first composition (synthetic mouse move → first composition after `transition.start`), 20 samples; CPU time and private bytes during the 50 transitions | 120 Hz | ~5 min |

The total is about 1 h per technique (3 × 12 + 12 + 2 × 3 + 5 min) plus about 10 min of calibration: roughly 3–3.5 h for A, B and C, unattended. The 60 Hz runs need the
display switched (Q4). If 60 Hz runs are not done, every "at 60 Hz" item is **NOT RUN** and S1 cannot pass.

### 5.8 S1 decision rules

1. A technique **passes** only if PB-01, PB-02, PB-03, PB-05, PB-10 and PB-12 are all `Pass` at
   both 120 Hz and 60 Hz. Any `Fail` fails it. Any `NotRun`/`Inconclusive` means it does not pass.
2. **S1 passes for WPF** if A or B passes. Prefer A (simplest, ADR §4.5) unless B passes and A
   does not.
3. If only C passes, S1 = "WPF path failed", the fallback is triggered and the decision goes to the owner (ADR R-01).
4. If none passes, S1 fails. Report the failure mechanism and propose a revised approach.
5. PB-04 (if deferred) and the informational measurements never decide S1.

### 5.9 S2 checklist (proposed definition of "FlaUI test + manual checklist")

S2 runs against the technique selected by S1. If S1 has no winner, it runs on A and B for information.

**Probe window.** A plain WPF window in the **harness process**, not the overlay's thread, because
`HTTRANSPARENT` only forwards input within one thread. It shows a solid known colour and a text box,
and counts clicks with their coordinates.

**Input.** Generated with FlaUI (MIT, NuGet, listed in TESTING_STRATEGY §2) via `SendInput`. This
moves the real cursor, so the owner must not use the machine during the run.

| ID | Check | Method | Pass |
|---|---|---|---|
| S2-01 | Transparent outside the shape | Screen capture of the overlay window rectangle over the probe colour, settled Compact and Expanded | Every sampled pixel outside the shape (≥ 2 px from the edge, to allow anti-aliasing) equals the probe colour |
| S2-02 | Click-through outside the shape | 100 clicks on a 10×10 grid inside the window but outside the shape, Compact and Expanded | 100 % reach the probe |
| S2-03 | Clicks inside the shape | 50 clicks inside the shape | 100 % reach the overlay; 0 reach the probe |
| S2-04 | Click routing during transitions (B/C region) | Clicks outside the target shape during transitions | Misrouting only inside the documented union region (ADR §4.4) |
| S2-05 | No activation | Probe text box focused. 100 hover/expand/collapse cycles plus 20 clicks on the shape | `GetForegroundWindow` is unchanged after every step; typed keys still reach the probe (TESTING_STRATEGY §4.4) |
| S2-06 | Not in taskbar or Alt-Tab | `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE`/`WS_EX_TOPMOST` styles read back; **manual**: Alt-Tab and taskbar inspection | Styles present; owner confirms it is absent |
| S2-07 | Topmost over normal windows | Probe maximised and activated over the overlay; `WindowFromPoint` at the shape centre and a Z-order walk | Overlay is above the probe |
| S2-08 | Fixed canvas | `GetWindowRect` sampled during 20 transitions | Rectangle never changes |
| S2-09 | Placement and DPI at 125 % | Shape centre and top edge vs `MONITORINFO.rcWork` in physical pixels | Centred within 1 px; top within 1 px of the work-area top |
| S2-10 | Suppress mechanism (detection itself is S7) | Harness sends `suppress` while a monitor-sized borderless probe window (topmost and non-topmost variants) is in front | No overlay pixels visible in a screen capture after `suppress`; `restore` brings it back. Hide latency is measured and reported but **not judged** (no budget exists for it) |
| S2-11 | DPI change (single monitor) | **Manual, owner-run**: switch scaling 100 → 150 → 175 → 125 % with the overlay running; re-run S2-09 after each | Placement correct after `WM_DPICHANGED`; no crash. Mixed-DPI and multi-monitor: **NOT RUN** (one display) |
| S2-12 | Display change | Harness simulates a work-area change (`WM_SETTINGCHANGE`) | Overlay re-centres |

**S2 passes** when S2-01…S2-10 and S2-12 pass automatically and the owner confirms S2-06 (manual)
and S2-11. Multi-monitor and mixed-DPI items stay **NOT RUN** until an external monitor exists (D7).

### 5.10 New code this protocol needs (when approved)

- Overlay techniques A, B and C (`--technique`).
- `Halo.Spikes.Harness`: process control, counter sampling, DWM timeline, calibration, CSV writer, display keep-awake.
- S2 integration tests (FlaUI package added to the test project).
- Unit tests for every new deterministic piece: thread context-switch delta aggregation, DWM timeline windowing, union-region geometry, command parsing.

No production code, no S3–S8 work.

## 6. Results

| Criterion | A | B | C | Notes |
|---|---|---|---|---|
| Calibration (§5.6) | NOT RUN | — | — | Protocol awaiting approval |
| PB-01 settled CPU @120/@60 | NOT RUN | NOT RUN | NOT RUN | |
| PB-02 wake-ups @120/@60 | NOT RUN | NOT RUN | NOT RUN | |
| PB-03 settled frames @120/@60 | NOT RUN | NOT RUN | NOT RUN | |
| PB-05 frame pacing @120/@60 | NOT RUN | NOT RUN | NOT RUN | |
| PB-10 private bytes | NOT RUN | NOT RUN | NOT RUN | |
| PB-12 settled GPU | NOT RUN | NOT RUN | NOT RUN | |
| PB-04 | NOT RUN | NOT RUN | NOT RUN | Proposed deferral to M3 (Q2) |
| S2-01…S2-12 | NOT RUN | NOT RUN | — | |
| **S1 gate** | **NOT RUN** | | | |
| **S2 gate** | **NOT RUN** | | | |

M0 toolchain results (actual): Release build 0 warnings / 0 errors from clean; unit tests 35/35;
desktop integration tests 2/2 (environment snapshot; WPF window renders and exits 0); locked restore OK.

## 7. Limitations known in advance

- One display: no multi-monitor or mixed-DPI results are possible (R-09).
- The DWM timeline is system-wide. Any other on-screen activity adds compositions, which can
  **hide** dropped overlay frames, so a quiet desktop is required and calibration must pass.
- Process counters sample at intervals. Very short-lived threads can be missed by PB-02's method.
- Hybrid GPU: the panel is driven by the Intel UHD. Results apply to that adapter only.
- The power-mode overlay ("Best performance" GUID) raises clocks. Results under "Balanced" or battery can differ;
  a battery run is optional and informational.

## 8. Owner decisions requested

| # | Decision | Recommendation |
|---|---|---|
| Q1 | Approve this protocol (§5), including the amendment of ADR §9 *methods* for PB-02/03/05 to the built-in methods with calibration | Approve |
| Q2 | PB-04 moved from S1 to M3 (S1 keeps an informational surrogate) | Approve: S1 cannot play media without starting S3 |
| Q3 | Build C as a minimal composition baseline (vs. only when A and B fail) | Approve: gives the comparison the execution plan asks for, at moderate cost |
| Q4 | 60 Hz runs: (a) owner switches refresh rate in Settings when the harness pauses, or (b) harness switches temporarily via `ChangeDisplaySettingsEx` without saving to the registry and restores 120 Hz afterwards | (a): no automated change to user display settings |
| Q5 | PresentMon (MIT) as an optional cross-check, needing elevation per run; or only if calibration fails | Only if calibration fails |
| Q6 | Owner availability: about 3–3.5 h unattended (machine idle, Do Not Disturb on) and about 30 min attended for S2-06/S2-11 | Schedule |
| Q7 | Confirm FlaUI (MIT, NuGet, test project only) for S2 | Approve (already listed in TESTING_STRATEGY §2) |
| Q8 | Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` permanently at user level | Recommend; owner action or explicit approval |
