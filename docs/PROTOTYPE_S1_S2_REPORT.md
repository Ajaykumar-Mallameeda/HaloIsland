# Prototype Report — Spikes S1 and S2 (architecture gate)

| Field | Value |
|---|---|
| Status | **Protocol v1.3 — v1.2 was fixed before any gated measurement; v1.3 changes one PB-02 rule after a partial gated batch (§11). Results: see §9** |
| Date | 2026-10-27 |
| Related | ADR-001 §4, §9, §13; `TESTING_STRATEGY.md` §4.2, §4.4, §4.8, §5; `PROJECT_STATUS.md` |
| Protocol history | v0 (proposal, commit `b6228b7`) → v1.0 (commit `5853eba`) → v1.1 → v1.2 → **v1.3** (this document; changes in §11) |
| Outcome vocabulary | **PASS** measured and met · **FAIL** valid measurement, not met · **INVALID** method or run failed its validity rules · **BLOCKED** tooling, hardware, permission or condition unavailable · **NOT RUN** no valid attempt |

## 1. Summary

Phase 4 Execution 1 built M0 and stopped because S1/S2 were under-specified. The owner then
approved the open decisions (§5) and asked for the gaps to be resolved, a protocol to be fixed,
the prototypes to be built and measured, and an evidence-based architecture decision.

Before writing this protocol, a feasibility probe found that the v0 frame-timing method
(`DwmGetCompositionTimingInfo`) returns **synthetic counters** on this Windows build (new gap G8). v1.0
replaces it with DXGI Desktop Duplication, attributed to the overlay's screen region. The method
must pass a predeclared calibration before any PB-03/PB-05 result counts.

## 2. Baseline verification (Phase A, 2026-10-27)

| Item | Result |
|---|---|
| Repository root | `C:\dev\Notch\advanced-windows-island` (`git rev-parse --show-toplevel`) |
| Branch / HEAD | `main` / `b6228b73ba623200d43425b8f22d0b956b20129f`; tree clean; no remotes; no diff |
| OS | 10.0.26300.9711 (UBR 9711) |
| SDKs | 9.0.317, 10.0.403; `global.json` pins 10.0.403 (`latestPatch`) |
| Runtime | .NET 10.0.14 (smoke log) |
| Display | 1920×1080, **120 Hz**, 120 DPI (125 %) |
| Power | AC; power-mode overlay `ded574b5-45a0-4f42-8737-46345c09c238` |
| Shell | Non-elevated. The account is a member of **Performance Log Users** (enabled group), so user-mode ETW sessions can be created without elevation (not used by v1.0; recorded for the fallback rule, §8.14) |
| Telemetry variables before Phase E | Not set (user, machine, process) |

| Command (repository root, `DOTNET_CLI_TELEMETRY_OPTOUT=1`) | Exit | Result |
|---|---|---|
| delete all `spikes/**/bin` and `obj` | 0 | clean state |
| `dotnet restore Halo.slnx --locked-mode` | 0 | restore OK against lock files |
| `dotnet build Halo.slnx -c Release --no-restore` | 0 | **0 warnings, 0 errors** |
| `dotnet test --solution Halo.slnx -c Release --no-build -- --filter-not-trait "Category=Integration"` | 0 | **35/35 passed** |
| `dotnet test --solution Halo.slnx -c Release --no-build -- --filter-trait "Category=Integration"` | 0 | **2/2 passed** |
| `Halo.Spikes.Overlay.exe --smoke --log <scratch>` | 0 | `env.captured` + `smoke.rendered` |

All M0 baseline facts reported by Execution 1 were reproduced.

## 3. Original gap and conflict inventory (unchanged from v0)

### 3.1 Gaps

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

New gap found while preparing v1.0 (kept separate, not merged into G3):

| # | Source text | Gap |
|---|---|---|
| G8 | v0 §5.5/§5.6: PB-03/PB-05 via polling `DwmGetCompositionTimingInfo(NULL)` `cFrame`/`qpcCompose` | Probe 2026-10-27 (scratch C# via PowerShell `Add-Type`, layout checked: `cbSize` 292): `cFrame` and `cRefresh` increase by exactly **one per call** (22,083,779 calls → 22,083,779 "frames" in 10 s), they start at 1 in a fresh process, and `qpcCompose` equals `qpcVBlank`, which tracks the current time. The values are synthesized per caller, not real composition counts. **The v0 method cannot measure compositions** |

### 3.2 Conflicts

| # | Phase 3 documents | Execution plan | Resolution applied / proposed (v0 text) |
|---|---|---|---|
| C1 | M0 = solution with `Halo.Core`, `Halo.Platform.Windows`, `Halo.Infrastructure`, `Halo.App` (PROJECT_STATUS, ADR §5) | "Create only the minimum solution structure needed to build and test S1/S2"; no app shell | **Applied the execution plan** (newer, more specific). M0 created spike-only projects under `spikes/`. The production projects are deferred to M2 |
| C2 | M1 = spikes S1–S8 | Execution 1 = S1/S2 only; S3–S8 in Execution 2 after review | Applied the execution plan |
| C3 | PB-02/03/05 methods name WPR+WPA / PresentMon | No third-party analysis tools; D8 not approved | **Proposed** built-in methods (§5.5) with a calibration step (§5.6); ADR §9 method column to be amended on approval |
| C4 | ADR §5: Platform adapters use CsWin32 | Minimal skeleton | M0 uses .NET `LibraryImport` (no package) for 6 functions. CsWin32 stays the plan for production code (M2) |
| C5 | TESTING_STRATEGY §2: xUnit v3 via `dotnet test` | — | xUnit v3 4.0.2 only runs on Microsoft.Testing.Platform with the .NET 10 SDK. `global.json` opts in. That platform includes Microsoft usage telemetry, disabled only by an environment variable (recorded, owner decision Q8) |
| C6 | ADR §13 "pass criteria: all listed budgets met at 60 and 120 Hz" | "Do not adjust the acceptance targets after seeing the results" | No budget value is changed. Only *methods* and PB-04's placement are proposed, **before** any result exists |

## 4. Resolution register (v1.0)

**Precedence used when documents disagree:** (1) owner decisions recorded in §5 and the current
execution plan; (2) ADR-001 (owns budgets, techniques and S1/S2 definitions; values never change
here); (3) PRODUCT_REQUIREMENTS (owns behaviour the S2 checklist derives from); (4)
TESTING_STRATEGY and other documents follow. Dependent documents are updated to match.

| ID | Original issue | Affected artifacts | Resolution | Evidence / rationale | Budget impact | Status | Validation |
|---|---|---|---|---|---|---|---|
| G1 | No S2 checklist | ADR §13 S2; TESTING_STRATEGY §4.4; PRODUCT_REQUIREMENTS FR-ISL-01/05/06/09/10/11, FR-A11Y-01 | 12-item checklist defined in §8.11, each item traced to a requirement, with setup, action, evidence, pass rule, automation level, severity and gating | Items derive from the ADR §13 S2 question and the FR rows it depends on; nothing is added that no requirement asks for | None | **Resolved** | Each item produces an artifact in `perf-results/`; §9.3 reports per item |
| G2 | Suppression depends on S7 | ADR §13 S2/S7; FR-ISL-09 | S2-10 tests only the **mechanism** (overlay hides on a `suppress` command and is not visible over a full-screen window; `restore` brings it back). Detection stays in S7 | ADR §4.9 separates detector from presentation state (`Hidden(Fullscreen)`, §6.1) | None | **Resolved** | S2-10 screen-region check (§8.11) |
| G3 | PB-02/03/05 tools (PresentMon/WPA) unavailable; PresentMon blind to layered windows | ADR §9 method column; TESTING_STRATEGY §2 | PB-02: per-thread `Context Switches/sec` raw counts via PDH. PB-03/PB-05: DXGI Desktop Duplication frame metadata (QPC present time + dirty rectangles) attributed to the overlay's screen region over a static backdrop (§8.5). Valid only if calibration passes (§8.7). PresentMon only under §8.14 | Probe 2026-10-27: duplication works non-elevated on the Intel adapter that drives the panel (183 updates in 10 s with dirty rectangles; animated content showed 8.2–8.6 ms intervals at 120 Hz). Desktop Duplication captures the composed desktop, so layered windows (A) are visible to it, unlike PresentMon | None (methods only; ADR §9 *Method* column amended) | **Resolved, conditional on calibration** | §8.7 calibration at each refresh rate; result §9.1 |
| G4 | PB-04 needs media | ADR §9 PB-04; ADR §13 S1; TESTING_STRATEGY §5 (M3) | **PB-04 deferred to M3** (owner Q2). Identifier, target (≤ 1.0 s CPU per 10 min) and intent (compact island with a static indicator while music plays) unchanged. S1 does not play media and reports PB-04 as **DEFERRED (M3)** — not passed, failed or deleted | S1 cannot play media without starting S3/M3 work; owner approved | None (moved, not changed) | **Resolved** | ADR §13 S1 row, TESTING_STRATEGY §5 M3 row and PROJECT_STATUS updated consistently |
| G5 | Who switches 60 Hz | ADR §13 S1; ENVIRONMENT_AUDIT | **Owner switches manually** in Settings (owner Q4a). The harness never changes display settings (no `ChangeDisplaySettings*` import; checked by a unit test). Every run reads the refresh rate at start and end; a mismatch with the declared rate makes the run INVALID | Owner decision; avoids automated changes to user settings | None | **Resolved** | Run metadata `refresh_hz_start/end`; unit test on the interop surface |
| G6 | Test surface, runs, warm-up, transition driving, winner rule; C role | ADR §4.5, §13 S1 | §8.2 surface; §8.8 run matrix (3 sessions per technique at 120 Hz, 1 at 60 Hz, interleaved A→B→C); §8.10 decision rules (prefer A, then B; C is a minimal comparison baseline per owner Q3, §6) | ADR §4.5 already defines A/B/C and ranks A as simplest | None | **Resolved** | Matrix executed as declared; any deviation recorded in §9 |
| G7 | Startup, input responsiveness, animation CPU/memory: gating? | ADR §9 PB-06/PB-09 (M2 budgets) | **Informational, never gating for S1/S2** (§8.12). Reported next to PB-06/PB-09 for orientation only | PB-06/PB-09 are assigned to M2 in TESTING_STRATEGY §5; changing that would change the gate after the fact | None | **Resolved** | §9.4 labels every such number "informational" |
| G8 | DWM timing API synthetic (§3.1) | v0 §5.5/§5.6 | v0 method withdrawn; replaced by the G3 resolution | Probe evidence in §3.1 | None | **Resolved** (v0 method INVALID by inspection, never used for results) | Calibration of the replacement (§8.7) |
| C1 | M0 projects vs spike-only plan | ADR §5; PROJECT_STATUS | Keep the execution plan: spike projects only; production projects in M2 | Execution plan is newer and narrower; ADR §13 says spikes are throw-away | None | **Resolved** | No `Halo.Core/App/...` projects created |
| C2 | M1 = S1–S8 vs S1/S2 only | ADR §13; TESTING_STRATEGY §5 | S1/S2 only in this execution; S3–S8 remain NOT STARTED | Execution plan stop condition | None | **Resolved** | No S3–S8 code |
| C3 | ADR §9 methods vs no third-party tools | ADR §9 method column | ADR §9 *Method* column amended for PB-02/03/05 to the built-in methods of §8.6, with calibration; WPR/WPA/PresentMon stay as optional cross-checks under §8.14 | Owner Q1 + G3/G8 evidence | None | **Resolved** | ADR §9 diff contains only the Method column for PB-02/03/05 and the PB-04 placement note |
| C4 | CsWin32 vs `LibraryImport` | ADR §5 | Spikes keep `LibraryImport` and hand-declared COM vtables (throw-away code, no package). CsWin32 remains the production plan for M2 | Avoids adding a source-generator dependency to throw-away code | None | **Resolved** | No CsWin32 reference in `spikes/` |
| C5 | xUnit v3 needs Microsoft.Testing.Platform, which sends telemetry | TESTING_STRATEGY §2; DEVELOPMENT_WORKFLOW | Owner Q8: `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `TESTINGPLATFORM_TELEMETRY_OPTOUT=1` set permanently for the user (Phase E, DEVELOPMENT_WORKFLOW §6) | Microsoft documents only the environment-variable opt-out | None | **Resolved** | Variables read back from `HKCU\Environment` and from a process started by Explorer |
| C6 | Budgets fixed vs method changes | ADR §9, §13 | All budget IDs and values unchanged; only methods (C3) and PB-04 placement (G4) change, both before any result exists | Change log §11 | None | **Resolved** | `Budgets.cs` values unchanged (diff); ADR §9 Target column unchanged |

No unresolved issue affects measurement validity. G3 remains conditional: if calibration fails,
PB-03/PB-05 become INVALID and the gate stops (§8.14).

## 5. Owner decisions applied (2026-10-27)

| # | Decision |
|---|---|
| Q1 | Resolve the gaps and fix a reproducible protocol before implementing (this document) |
| Q2 | PB-04 moved to M3 |
| Q3 | C built only as a minimal comparison baseline |
| Q4 | 60 Hz switched manually by the owner; the harness must not change display settings |
| Q5 | PresentMon only if the built-in method fails calibration or is unsuitable, with approval first |
| Q6 | About 3–3.5 h unattended machine time plus about 30 min owner time |
| Q7 | FlaUI approved in principle; package validated before use (§8.13) |
| Q8 | Permanent telemetry opt-out for the user |

## 6. Option C definition (Phase D)

ADR-001 §4.5 defines C exactly: *"Win32 host window (`WS_EX_NOREDIRECTIONBITMAP`) with a
`Windows.UI.Composition` visual tree via `ICompositorDesktopInterop`. Best animation path; text must be
drawn with Direct2D/DirectWrite into composition surfaces and UIA must be provided by a custom
provider — a large cost, hence fallback only."* The definition is unambiguous; no clarification is needed.

The baseline is a separate executable (`Halo.Spikes.CompositionBaseline`) so that the Windows SDK
projection (`net10.0-windows10.0.19041.0`) stays out of the WPF overlay and the tests.

| Aspect | A / B (WPF) | C baseline | Reason for any difference |
|---|---|---|---|
| Window placement and size | 468×208 DIP canvas, top-centre of the work area | Same, in physical pixels | — |
| Shape | Black rounded rectangle, Compact ↔ Expanded | Same (`CompositionRoundedRectangleGeometry`) | — |
| Glyph | 24 DIP white circle | Same (`CompositionEllipseGeometry`) | — |
| Text ("12:34", second line in Expanded) | Yes | **No** | ADR §4.5: text needs Direct2D/DirectWrite surfaces; out of a minimal baseline. C does less raster work, so its numbers are a **lower bound** for a full C implementation |
| Animation | WPF `DoubleAnimation` (Width, Height, RadiusX/Y), `CubicEase` EaseOut, 350 ms, `DesiredFrameRate` = refresh rate (ADR §4.7) | Composition key-frame animations (Size, CornerRadius, Offset), cubic Bézier (0.33, 1, 0.68, 1) ≈ ease-out cubic, 350 ms, run by DWM | Each technique's native animation path is what S1 compares |
| Cross-fade | Second text line `Opacity` | A white placeholder bar (160 × 6 DIP) fades in with the same timing | No text in C; the bar keeps one element fading in, as in A/B |
| Input region | A: per-pixel alpha; B: `SetWindowRgn` union (ADR §4.4) | `SetWindowRgn` union | ADR §4.4 |
| UI Automation | WPF peers | **None** | ADR §4.5: custom provider is out of scope; S2-12 is expected to FAIL for C and is recorded as a cost |
| Calibration modes | stall injection, stray-frame tick | continuous animation, stray-frame tick | C's animations run in DWM, so a UI-thread stall cannot stutter them |

## 7. Test setup

| Item | Value | Source |
|---|---|---|
| OS | Windows 11 Home Single Language 10.0.26300.9711 (26H2) | `EnvironmentSnapshot` |
| SDK / runtime | .NET SDK 10.0.403; runtime .NET 10.0.14 | `dotnet --list-sdks`; smoke log |
| Display | One internal panel, 1920×1080, 120 Hz (60 Hz available), 125 % (120 DPI) | `EnumDisplaySettings`, `GetDpiForMonitor` |
| GPU | Intel UHD drives the panel (Desktop Duplication output 0 is on DXGI adapter 0); RTX 3050 Laptop present | DXGI probe; ENVIRONMENT_AUDIT |
| Power | AC; "Best performance" overlay `ded574b5-…` | `GetSystemPowerStatus`, `PowerGetEffectiveOverlayScheme` |
| Elevation | Non-elevated | `WindowsPrincipal` |

## 8. Protocol v1.3

### 8.1 Techniques

A, B and C as defined in ADR §4.5 and §6 above. A and B share one WPF executable
(`Halo.Spikes.Overlay --technique A|B`); C is `Halo.Spikes.CompositionBaseline`. Both accept the
same command-line options and standard-input commands and write the same log events.

### 8.2 Test surface (test parameters, not product design)

| Parameter | Value |
|---|---|
| Canvas (window) | 468 × 208 DIP = 585 × 260 px at 120 DPI. Horizontally centred on the primary monitor's work area; window top = work-area top. Never resized or moved during a run (ADR §4.3) |
| Compact shape | 220 × 40 DIP, corner radius 20 DIP, black, top edge flush with the canvas top, horizontally centred. Content: text "12:34" (Segoe UI 13 pt, white) and a 24 DIP white circle (A/B); circle only (C) |
| Expanded shape | 420 × 160 DIP, radius 32 DIP, same anchor; adds a second text line (A/B) |
| Transition | Width, height and radius animate together over 350 ms with ease-out cubic; content cross-fades |
| Shadow | None |
| Frame rate | A/B: `Timeline.DesiredFrameRate` = current refresh rate. C: DWM (always refresh rate) |

### 8.3 Processes

- **Harness** (`Halo.Spikes.Harness`, console + WPF windows on an STA thread, Per-Monitor V2):
  - starts and stops the overlay;
  - shows a static **backdrop** window (solid grey, topmost, no activation) exactly under the canvas, so nothing else can change that screen region;
  - runs Desktop Duplication on a dedicated high-priority thread;
  - samples counters every 10 s;
  - drives transitions over the overlay's standard input;
  - writes all artifacts.
- **Overlay** (A/B/C):
  - reads commands from standard input: `expand`, `collapse`, `suppress`, `restore`, `hover-on`, `hover-off`, `ping`, `quit`;
  - writes `SpikeLog` JSON lines to standard output: config, `overlay.ready`, `cmd.received`, `transition.start`/`transition.end`, `stall.injected`, `tick`, `click.inside`, `hover.enter`/`hover.leave`, `placement`;
  - logs QPC timestamps as numbers. No titles, paths or user content.
- The harness and overlay use the same system-wide QPC clock, which Desktop Duplication also reports.

### 8.4 Run conditions (checked, not assumed)

| Condition | How it is checked | Violation |
|---|---|---|
| Declared refresh rate (120 or 60 Hz), 1920×1080, 120 DPI | `EnumDisplaySettings` and `GetDpiForMonitor` at run start and end | INVALID |
| AC power and "Best performance" overlay | `GetSystemPowerStatus`, `PowerGetEffectiveOverlayScheme` at start and end | INVALID |
| Non-elevated harness | token check | INVALID |
| No user input during S1/calibration runs | `GetLastInputInfo` at every 10 s sample (S1 runs inject no input) | INVALID (contaminated) |
| No foreign window over the attribution region | Z-order walk at every sample: no visible top-level window above the backdrop intersects the canvas, other than the overlay | INVALID (contaminated) |
| Cursor outside the canvas | Harness parks the cursor below the canvas at run start; checked at every sample | INVALID |
| Desktop Duplication session alive for the whole measured window | no `DXGI_ERROR_ACCESS_LOST`, no acquire error | INVALID |
| Calibration features off in gated runs | overlay config log: `stall_ms = 0`, `tick_ms = 0`, `continuous = 0`, `calibration = 0` | INVALID |
| Build identity | SHA-256 of the overlay assembly recorded per run; must equal the value recorded at matrix start | INVALID |
| Overlay on top of the backdrop | `WindowFromPoint` at the shape centre resolves to the overlay at run start | INVALID |
| Sampling regularity | no gap > 15 s between counter samples in the measured window | INVALID |
| Display kept on | `SetThreadExecutionState(ES_CONTINUOUS \| ES_DISPLAY_REQUIRED \| ES_SYSTEM_REQUIRED)` by the harness during runs, cleared at exit | — |
| Reference app not running | Read-only process-list check for `nusa` at run start and end (the app is never started, stopped or touched) | INVALID |
| Do Not Disturb | Owner action; not machine-verifiable. Toasts appear bottom-right, outside the attribution region, and foreign windows over the region are caught by the Z-order check | recorded only |

### 8.5 Frame attribution method (PB-03, PB-05, calibration)

1. The harness creates a D3D11 device on the DXGI adapter that owns the panel's output and calls `IDXGIOutput1::DuplicateOutput`.
2. A dedicated thread at `THREAD_PRIORITY_HIGHEST` loops on `AcquireNextFrame`. For each frame with `LastPresentTime ≠ 0` (pointer-only updates are ignored), it reads `GetFrameDirtyRects` and `GetFrameMoveRects` and releases the frame immediately. **Desktop pixels are never mapped, copied or stored.**
3. A frame is **attributed** to the overlay when any dirty or move rectangle intersects the canvas rectangle. Because a static topmost backdrop fills that rectangle under the overlay, an update there can only come from the overlay or from a foreign window caught by the Z-order check.
4. A frame with `AccumulatedFrames > 1` is **coalesced**: at least one intermediate timestamp was lost. A coalesced frame can hide only short intervals, never create a long one.
5. Recorded per frame: QPC present time, attributed flag, coalesced flag, `RectsCoalesced`.

**Limitations (stated prominently):**
- The method observes the **composed desktop**, that is, what DWM presented. It does not observe the application's own Present calls. That is closer to what the user sees than app-side timing, but DWM-side delays are attributed to the technique under test.
- Region attribution relies on the backdrop and the contamination checks.
- Duplication adds DWM work (copying dirty regions), equally for all techniques.

### 8.6 Measurement definitions per budget

Common to the S1 session (§8.8):
- **Warm-up:** 120 s from `overlay.ready` (first frame rendered).
- **Settled window:** the next 600 s, no commands sent.
- **Transitions phase:** follows in the same process, 60 transitions alternating expand/collapse, one command every 2.0 s.
- Counter samples are taken every 10 s; the raw values go into `samples.csv`.

| Budget | Target (ADR §9, unchanged) | Meaning and type | Raw data | Aggregation and pass formula | Direct or approximate | Invalid when | Limitations |
|---|---|---|---|---|---|---|---|
| PB-01 | ≤ 0.5 s process CPU per 10 min | Absolute threshold on overlay CPU (user + kernel) while settled | `Process.TotalProcessorTime` and `QueryProcessCycleTime` at each sample | `ΔCPU(600 s window) × 600/window_s`; PASS iff ≤ 0.5 s | Direct | §8.4 violation | — |
| PB-02 | ≤ 2 context switches/s into our threads, average | Absolute threshold, settled | PDH `\Thread(*)\Context Switches/sec` **raw** cumulative values with `ID Thread`/`ID Process`, overlay threads only | Σ over consecutive sample pairs of `max(0, c₂−c₁)` per thread ID present in both, plus `c₂` for threads first seen in the later sample, ÷ window seconds; PASS iff ≤ 2.0 | Direct for threads alive at sample times | a thread ID not present in the first sample appears later, or a known ID's count decreases (ID reuse) — a new thread could also start and end between samples unseen, so the bound is not proven (v1.3) | Threads that **end** during the window are counted up to their last sample; switches between that sample and their exit are not seen. Reported as `pb02_threads_ended` and as a limitation, not INVALID (v1.3). Switches on threads that live entirely between samples are not seen; a new thread makes the run INVALID instead of passing |
| PB-03 | 0 frames per second | Absolute zero, settled | Desktop Duplication frames (§8.5) | attributed frames in the 600 s window; PASS iff 0 | Direct observation of presented desktop updates in the overlay region | §8.4 violation | Updates that DWM does not present (no visual change) are not frames by definition. App-side rendering that changes no pixels is visible only in PB-01/PB-12 |
| PB-05 | ≤ 1 frame over 1.5× refresh per transition **and** p95 interval ≤ 1.5× refresh, over 50 transitions, at 60 and 120 Hz | Count per transition + nearest-rank p95 over all intervals | Attributed frames; overlay `transition.start`/`.end` QPC | Frames in `(start, end + 3 × refresh]`; intervals between consecutive frames (the gap from `start` to the first frame is latency, excluded). Long frame: interval > 1.5 × (1000/Hz) ms. A transition is **valid** if it has ≥ 2 attributed frames and no coalesced attributed frame. The first 50 valid transitions are evaluated with `FramePacing.Evaluate`. PASS iff worst long-frame count ≤ 1 and p95 ≤ 1.5 × refresh | Direct (presented desktop timeline) | fewer than 50 valid of 60; §8.4 violation | Same as §8.5 |
| PB-10 | ≤ 120 MB private bytes after 10 min idle | Absolute threshold | `Process.PrivateMemorySize64`, `WorkingSet64` | private bytes at window end in **decimal MB** (10⁶ bytes, the stricter reading); PASS iff ≤ 120. Working set and MiB reported, not judged | Direct | §8.4 violation | — |
| PB-12 | ≤ 0.5 % 3D engine, settled | Absolute threshold on mean utilisation | PDH `\GPU Engine(pid_<pid>_*engtype_3D)\Running Time` raw (100 ns units), all adapters; also `dwm.exe` 3D (informational) | `Σ ΔRunningTime / window × 100`; PASS iff ≤ 0.5 %. No GPU-engine instance for the pid = no GPU work submitted = 0 % | Direct for in-process GPU work | §8.4 violation | Work DWM does on the overlay's behalf (all of C's rendering) is attributed to `dwm.exe`; mitigated because PB-03 = 0 means DWM presented no overlay updates |
| PB-04 | ≤ 1.0 s per 10 min, music playing | — | — | **DEFERRED to M3** (G4). Not run, not passed, not failed | — | — | — |

Outputs per run in `perf-results/<date>/<run-id>/`:
- `env-start.json` and `env-end.json`;
- `overlay.jsonl`;
- `samples.csv`;
- `frames.csv` (attributed and coalesced frames, QPC only);
- `summary.json` (metrics, outcomes, validity flags, build hash).

### 8.7 Calibration (predeclared; run at each refresh rate before any technique run)

| Run | Setup | Rule (fixed before running) |
|---|---|---|
| CAL-<hz>-BASE | Backdrop only, no overlay, 60 s | 0 attributed frames (the region is quiet). System-wide update rate recorded |
| CAL-<hz>-POS | C with `--calibration --continuous` (composition animation alternating forever), 30 s warm-up, 10 s measured | Attributed rate ≥ 0.9 × Hz frames/s **and** median interval within [0.9, 1.1] × refresh **and** ≤ 1 % coalesced. Shows the method sees frames at the refresh cadence |
| CAL-<hz>-NEG-A | A with `--calibration --stall-ms 50` (one 50 ms UI-thread sleep 175 ms after each `transition.start`), 30 s warm-up, 20 transitions | **Every** stalled transition contains an interval ≥ 40 ms |
| CAL-<hz>-NEGCTL-A | A with `--calibration` and no stall, same procedure | ≤ 2 of 20 transitions contain an interval ≥ 40 ms |
| (fallback) CAL-<hz>-NEG-B / NEGCTL-B | Same with B | Run only if NEGCTL-A fails; the method is valid if either pair passes. Both pairs are reported |
| CAL-<hz>-TICK-A/B/C | `--calibration --tick-ms 5000` (one minimal repaint every 5 s while settled), 30 s warm-up, 60 s measured | Every logged `tick` has an attributed frame within 250 ms after it, **and** every attributed frame lies within 250 ms after a tick (no unexplained frame). Shows PB-03 detects a single stray frame for each technique (v1.1: window widened from 50 ms, see §11) |

The method is **VALID at a refresh rate** only if BASE, POS, one NEG/NEGCTL pair and all three
TICK runs pass. Otherwise PB-03 and PB-05 are **INVALID** at that rate for every technique, and the gate
stops at §8.14.

**Containment:**
- Stall, tick and continuous modes exist only behind `--calibration`; option parsing rejects them otherwise (unit-tested).
- Every gated run verifies the overlay's config log and is INVALID if any calibration feature is on (§8.4).
- No calibration state persists between processes.

### 8.8 Run matrix (IDs fixed before execution)

Batch 1 runs at 120 Hz, unattended (~2.6 h). Techniques are interleaved to spread thermal and
background drift:

| Order | Run IDs | Content |
|---|---|---|
| 1 | CAL-120-BASE, CAL-120-POS, CAL-120-NEG-A, CAL-120-NEGCTL-A, (CAL-120-NEG-B, CAL-120-NEGCTL-B), CAL-120-TICK-A, CAL-120-TICK-B, CAL-120-TICK-C | Calibration |
| 2 | STU-A-120, STU-B-120, STU-C-120 | Warm startup, 11 launches each, first discarded (informational) |
| 3 | S1-A-120-1, S1-B-120-1, S1-C-120-1, S1-A-120-2, S1-B-120-2, S1-C-120-2, S1-A-120-3, S1-B-120-3, S1-C-120-3 | S1 sessions: 120 s warm-up, 600 s settled (PB-01/02/03/10/12), 60 transitions (PB-05 + informational) |
| 4 | S2-A, S2-B, S2-C | Automated S2 items (§8.11) |

Batch 2 runs at 60 Hz (~50 min), after the owner switches the refresh rate:

| Order | Run IDs |
|---|---|
| 5 | CAL-60-BASE, CAL-60-POS, CAL-60-NEG-A, CAL-60-NEGCTL-A, (NEG-B pair), CAL-60-TICK-A/B/C |
| 6 | S1-A-60-1, S1-B-60-1, S1-C-60-1 |

Batch 3 is manual and needs the owner (~15–30 min, back at 120 Hz):

| Order | Run IDs |
|---|---|
| 7 | S2-11M-<technique> (DPI 125 → 100 → 150 → 175 → 125 % with the overlay running; the harness checks placement automatically after each change) for each WPF technique still eligible after S1, and S2-07M (Alt-Tab) only if its automation is unavailable |

### 8.9 Validity and rerun rules

- An INVALID run may be repeated once, as `<run-id>-r1`. Both results stay in the audit trail. If the repeat is also INVALID, the affected criteria are INVALID for that technique and rate.
- A valid FAIL is final. It is never repeated to look for a pass.
- Contamination (user input, foreign window, Desktop Duplication loss, environment drift) makes the run INVALID. Contaminated data is never re-used.
- An implementation defect found after a run is fixed. Every affected run is then repeated, the original results are kept and labelled "superseded (defect: …)", and the change log records the fix.

### 8.10 Decision rules

**S1, per technique:**
- PASS only if PB-01, PB-02, PB-03, PB-05, PB-10 and PB-12 are PASS in **every** S1 session at 120 Hz (3) **and** 60 Hz (1).
- Any FAIL fails the technique.
- Any INVALID, BLOCKED or NOT RUN means the technique does not pass.
- PB-04 is DEFERRED and never decides S1.

**S2, per technique:** PASS only if every gating item in §8.11 is PASS.

**Architecture (ADR §13, Phase H):**
1. **WPF confirmed** (provisionally, pending ADR approval) only if A or B passes S1 and S2 with valid calibration at both rates. Prefer A; choose B only if B passes and A does not (ADR §4.5).
2. **Prefer C** only if the comparison is valid and either (a) every WPF technique fails a mandatory criterion that C passes, or (b) C passes all of them while no WPF technique has a valid pass. Budgets are pass/fail, so when a WPF technique passes everything, C's larger margins are reported but do not overturn the decision. C's known costs (text, UIA) are part of the comparison.
3. **UNRESOLVED** if calibration fails, a mandatory measurement is INVALID, BLOCKED or NOT RUN, or a critical S2 item is blocked. Name the smallest next experiment.

### 8.11 S2 checklist (12 items)

**Common setup:**
- Probe window: a maximised, normal (non-topmost) WPF window in the harness process, colour `#FF00FF`. It records every click position and holds a text box for keystrokes.
- The probe is activated by a real click on its text box, then the overlay is started with hover disabled.
- Clicks are injected with `SendInput`, one atomic batch per click (absolute move, button down, button up), which Windows processes in order. Keys go through FlaUI's keyboard API, and hover moves use `SetCursorPos`. The real cursor moves, so the owner must not use the machine. FlaUI UIA3 reads UI Automation (S2-07, S2-12).
- Screen captures are GDI `BitBlt` + `CAPTUREBLT` of the canvas rectangle only. Over the probe, that region contains only the probe colour and the overlay's own test content. They are saved as PNG evidence.

| ID | Objective (requirement) | Exact setup → action | Expected behaviour and observable evidence | Pass rule | Automation | Severity | Blocks gate |
|---|---|---|---|---|---|---|---|
| S2-01 | Window lifecycle: created, visible, responsive, exits cleanly (FR-ISL-01/02, ADR §4.2) | Start overlay → wait `overlay.ready` → `IsWindowVisible`, `GetWindowRect`, `SendMessageTimeout(WM_NULL, 1 s)` → at the end `quit` | Visible at the §8.2 rectangle; responds; exits with code 0 within 5 s; `IsWindow` false afterwards; no process left | All checks true | Automated | Critical | Yes |
| S2-02 | Transparent outside the shape (ADR §1.1, FR-ISL-05) | Capture canvas over the probe in settled Compact and Expanded | Pixels ≥ 2 px outside the shape equal the probe colour; the shape interior is the shape colour | 100 % of outside pixels within ±2 per channel of `#FF00FF`; ≥ 1 interior pixel within ±2 of black | Automated | Critical | Yes |
| S2-03 | Click-through outside the shape (FR-ISL-05) | Settled Compact and Expanded: 10×10 grid of points inside the canvas but ≥ 4 px outside the shape (100 per state) → click each | Each click reaches the probe at the clicked position; the overlay logs no `click.inside` | 200/200 reach the probe | Automated | Critical | Yes |
| S2-04 | Clicks inside the shape reach the overlay (FR-ISL-05) | 50 points ≥ 4 px inside the Compact shape (and 50 in Expanded) → click | Overlay logs `click.inside`; probe records nothing | 100/100 to the overlay, 0 to the probe | Automated | Critical | Yes |
| S2-05 | Input routing during transitions (FR-ISL-05 failure mode, ADR §4.4) | 20 expand transitions; during each, click 2 points outside the union region and 2 inside the Compact shape | Outside-union clicks reach the probe; inside-Compact clicks reach the overlay. Clicks between current and target shape are "documented union" and not judged | 40/40 outside-union to the probe and 40/40 inside-Compact to the overlay | Automated | Major | Yes |
| S2-06 | No activation; focus and keystrokes stay with the probe (FR-ISL-06, TESTING_STRATEGY §4.4) | Probe text box focused; `hover-on`; 100 cycles of moving the cursor into the shape (expand) and out (collapse), waiting for each `transition.end`; then 20 clicks on the shape; then type a fixed test string | `GetForegroundWindow` = probe after every step; the probe's text box contains the test string (compared in memory, never logged) | 0 foreground changes in all checks; string received | Automated | Critical | Yes |
| S2-07 | Not in taskbar or Alt-Tab (FR-ISL-06, ADR §4.2) | Read `GWL_EXSTYLE`; enumerate taskbar buttons via UIA; open Alt-Tab (Alt down, Tab), enumerate its items via UIA, cancel with Esc | `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WS_EX_TOPMOST` set and no `WS_EX_APPWINDOW`; the overlay is absent from both lists while the probe (positive control) is present. Names are compared in memory; only booleans are logged | All true; foreground unchanged afterwards | Styles and taskbar automated. The Alt-Tab list is **manual S2-07M**: in DEV runs on this build the switcher window exposed no list items to UI Automation, so the positive control cannot be met | Major | Yes |
| S2-08 | Topmost over normal windows (ADR §13 S2) | Probe maximised and active | `WindowFromPoint(shape centre)` = overlay; overlay above probe in a Z-order walk | Both true | Automated | Critical | Yes |
| S2-09 | Placement at the top edge and fixed canvas (FR-ISL-01, ADR §4.3) | Read window rectangle and `MONITORINFO.rcWork`; sample `GetWindowRect` every 10 ms during 20 transitions | Centred; top at the work-area top; never moves or resizes | Centre within 1 px, top within 1 px, rectangle constant | Automated | Major | Yes |
| S2-10 | Suppression mechanism: below full-screen apps when suppressed (ADR §13 S2; detection is S7) | Monitor-sized borderless probe (`#00FF00`), (i) non-topmost: overlay visible over it → `suppress` → capture → `restore` → capture; (ii) topmost probe shown → `suppress` → capture → probe closed → `restore` → capture | No overlay pixel in the canvas while suppressed; overlay visible again after `restore`. Hide latency (command → first attributed frame) reported, **not judged** (no budget exists) | All captures as expected | Automated | Critical | Yes |
| S2-11 | DPI and display configuration (FR-ISL-10/11) | (a) Automated: send (`SendMessageTimeout`, as the system broadcasts them) `WM_SETTINGCHANGE`(`SPI_SETWORKAREA`) and `WM_DISPLAYCHANGE` to the overlay → placement re-checked. (b) **Manual S2-11M:** owner changes scaling 125 → 100 → 150 → 175 → 125 % while the overlay runs; the harness re-checks after each `WM_DPICHANGED` | Placement rule of S2-09 holds after each change; canvas size = 468×208 DIP at the new DPI; no crash | All checks PASS | (a) automated, (b) human-triggered, automatically checked | Major | Yes (single display). **Multi-monitor and mixed-DPI: BLOCKED** (one display, R-09) — not gating, never claimed |
| S2-12 | UI Automation exposure (FR-A11Y-01) | FlaUI UIA3 `FromHandle(overlay)`; find descendants | Root element has a non-empty name; the compact text element is exposed; inspection does not change the foreground | All true | Automated | Major | Yes, for the technique selected. Expected FAIL for C (ADR §4.5), recorded as C's cost |

### 8.12 Informational measurements (G7; never gating)

| Measurement | Method | Reported next to |
|---|---|---|
| Warm startup | STU runs: `Process.Start` QPC → first attributed frame in the canvas; p50 of launches 2–11 | PB-09 (M2) |
| Command → first frame | overlay `cmd.received` → first attributed frame of the transition; p50/p95 over the session's transitions | PB-06 (M2) |
| Hover → transition start | S2-06: cursor move QPC → overlay `hover.enter` → `transition.start` | PB-06 (M2) |
| Animation CPU and memory | overlay ΔCPU during the 60-transition phase (s per minute) and peak private bytes | — |
| Settled working set | `WorkingSet64` at window end | PB-10 (context) |
| DWM 3D utilisation while settled | `dwm.exe` 3D Running Time delta | PB-12 (context) |

### 8.13 Dependencies validated

| Package | Version | Licence (read from the package's `LICENSE.txt`) | Provenance | Target fit | Transitive | SHA-256 (nupkg) |
|---|---|---|---|---|---|---|
| FlaUI.Core | 5.0.0 | MIT | nuget.org, repository `github.com/FlaUI/FlaUI` commit `c5e7eb7a`, published 2025-02-25 | `lib/net8.0-windows7.0` → runs on net10.0-windows | Declares Microsoft.Win32.Registry, System.Diagnostics.PerformanceCounter, System.Drawing.Common, System.Management, System.Security.Permissions. **Resolved on .NET 10: only System.Management 8.0.0** (Microsoft, MIT); the others are provided by the Windows Desktop runtime and pruned by the SDK (lock file) | `191cc65e…f568a7` |
| FlaUI.UIA3 | 5.0.0 | MIT | same repository and commit | `lib/net8.0-windows7.0` | FlaUI.Core, Interop.UIAutomationClient | `d5d2e083…f711f2` |
| Interop.UIAutomationClient | 10.19041.0 | MIT | nuget.org, `github.com/Roemer/UIAutomation-Interop` | `lib/netcoreapp3.0` | none | `0d2ed17d…ff3b1` |
| Microsoft.Windows.SDK.NET.Ref | selected by the .NET SDK for `net10.0-windows10.0.19041.0` | MIT (Microsoft) | targeting pack restored from nuget.org | C baseline only | — | — |

- FlaUI is referenced only by the harness, a test tool that is never shipped.
- Versions are pinned in `Directory.Packages.props`, with lock files.
- UI Automation is used only for in-memory comparisons. No element names or texts are logged.

### 8.14 PresentMon / other-tool fallback

PresentMon is not installed or approved.

**When it is considered:** only if a §8.7 calibration fails, or the method is shown unsuitable for a required metric.

**Before any install, the following must be documented:**
- the failed measurement;
- the metric;
- the official source and package hash;
- the licence (MIT);
- the data collected (ETW present events for all processes) and the privacy handling;
- compatibility;
- whether it can observe the technique (it cannot see layered-window updates, so it could never validate A).

**Then:** request owner approval. Until approved, the affected criteria are **BLOCKED** and the
architecture verdict is UNRESOLVED. An absence of PresentMon data is never treated as a pass. User-mode
ETW via `logman` (Performance Log Users) is the next built-in candidate and is subject to the same
documentation step.

## 9. Results

### 9.1 Calibration

NOT RUN yet.

### 9.2 S1

NOT RUN yet.

### 9.3 S2

NOT RUN yet.

### 9.4 Informational

NOT RUN yet.

## 10. Limitations known in advance

- **One display:** no multi-monitor or mixed-DPI results are possible (R-09). They are BLOCKED, not gating, and never claimed.
- **Composed-desktop observation:** PB-03/PB-05 observe the composed desktop, not the application's own presents (§8.5). They are system-level evidence attributed by screen region. They are application-specific only to the degree that the backdrop and contamination checks hold.
- **PB-02 thread visibility:** PB-02 sees threads alive at sample times. A new thread invalidates the run instead of passing it. Threads that end during the window lose at most the switches between their last sample (≤ 10 s) and their exit, so PB-02 is a slight under-count when `pb02_threads_ended` > 0 (v1.3).
- **C's GPU attribution:** C's GPU work is attributed to DWM (PB-12 limitation).
- **C is a baseline:** C has no text and no UIA, so it is a lower bound for a full implementation.
- **Hybrid GPU:** the Intel UHD drives the panel. Results apply to that adapter.
- **Power mode:** "Best performance" raises clocks; other power modes or battery power may differ.
- **Do Not Disturb:** not machine-verifiable (mitigated, §8.4).

## 11. Protocol change log

| Version | Date | Change | Before any result? |
|---|---|---|---|
| v0 | 2026-10-27 | Proposal (commit `b6228b7`) | Yes |
| v1.0 | 2026-10-27 | G8 found. The DWM timing method was replaced by Desktop Duplication with region attribution and a backdrop. Calibration rules are now numeric. S2 checklist rewritten to 12 items traced to requirements, adding lifecycle (S2-01) and UIA (S2-12). The work-area simulation merged into S2-11. S1 sessions combine idle + transitions per process. PB-02 churn rule, contamination checks, decimal MB for PB-10, run IDs and the C definition table added. PB-04 DEFERRED (Q2) | Yes |
| v1.1 | 2026-10-27 | TICK match window 50 → 250 ms. The overlay logs `tick` when it changes a property; the frame appears after the render tick, the render-thread present and the next DWM composition, about 3 refresh periods (≈ 50 ms at 60 Hz). 50 ms would fail a working method at 60 Hz. Ticks are 5000 ms apart, so a 250 ms window cannot pair a frame with the wrong tick. Changed while writing the code, before any calibration run | Yes |
| v1.2 | 2026-10-27 | Harness defects and implementation details found in DEV shake-down runs (IDs `DEV-…`, never results), before any gated run: (1) clicks are injected as one atomic `SendInput` batch each, because moving with `SetCursorPos` and clicking with a separate `SendInput` raced (S2-05 clicks landed at the next point); (2) S2-11a sends rather than posts `WM_SETTINGCHANGE` (Windows refuses to post it); (3) S2-06 gives the probe's text box focus with a click first; (4) the Alt-Tab list cannot be read through UI Automation on this build, so it becomes manual S2-07M; (5) C's cross-fade uses a placeholder bar; (6) a read-only reference-app check was added to §8.4; (7) §8.13 records the packages that actually resolve. No rule, threshold or budget changed | Yes |
| v1.3 | 2026-10-28 | **Changed after seeing gated data — owner-approved (option b).** The first gated 120 Hz batch (`perf-results/2026-10-27`) was stopped by the host for low system memory after calibration (VALID), startup and 5 of 9 S1 sessions. In all 5 sessions PB-02 was INVALID under the v1.0 churn rule only because 1–2 startup threads present in the first sample **ended** within the first 20 s of the settled window; no new thread appeared (A: 0.15 switches/s, C: 0.013/s, budget ≤ 2). The rule now invalidates PB-02 only for a **new** thread (or ID reuse); ended threads are counted to their last sample and reported (`pb02_threads_ended`) as a limitation. The PB-02 threshold, aggregation and all other rules are unchanged. The aborted batch is kept as an audit trail (§9) and the whole 120 Hz batch, including calibration, is rerun under v1.3 | **No** — after 5 gated S1 sessions |
