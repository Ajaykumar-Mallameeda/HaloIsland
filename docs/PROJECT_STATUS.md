# Project Status — Project Halo

Last updated: 2026-10-27

## Current phase

**Phase 4, Execution 1 — M0 complete; stopped before S1/S2 for protocol review.**
S1 and S2 were not precise enough to implement, so the execution plan's rule applied: propose the
missing protocol and stop. The protocol is in [`PROTOTYPE_S1_S2_REPORT.md`](PROTOTYPE_S1_S2_REPORT.md) §5.
**The S1/S2 architecture gate has not been run. No pass is claimed.**

Phase 4 Execution 2 (S3–S8) has not started. It requires the S1/S2 gate to pass first.

## Completed

| Item | Result |
|---|---|
| Bootstrap, Phase 0, Phase 1 | Repository, workflow, environment audit, website audit, installer static analysis, first feature matrix |
| Node.js upgrade; workspace relocation | 24.22.1; `C:\dev\Notch\` |
| Phase 2 — REA verification and evidence inventory | NUSA_EVIDENCE_REGISTER (89 entries), NUSA_FEATURE_MATRIX (59 rows), research docs |
| Phase 3 — architecture | ADR-001 (proposed), PRODUCT_REQUIREMENTS, SECURITY_AND_PRIVACY_DESIGN, TESTING_STRATEGY |
| **Phase 4 — .NET 10 SDK** | **10.0.403** installed from the official Microsoft installer (SHA-512 matched release metadata, Authenticode valid); runtime 10.0.14; existing SDKs kept (ENVIRONMENT_AUDIT §2) |
| **Phase 4 — M0 skeleton** | `Halo.slnx` with `spikes/` projects: Measurement (net10.0), Windows (interop), Overlay (WPF, `--smoke` only), Tests. Clean Release build 0 warnings; 35/35 unit + 2/2 desktop integration tests pass |
| **Phase 4 — S1/S2 protocol** | Gaps G1–G7 and conflicts C1–C6 recorded; protocol PROPOSED (PROTOTYPE_S1_S2_REPORT) |

## Key findings

1. The supplied installer matches the vendor's GitHub **v1.1.0** pre-release hash.
2. `UserNotificationListener` needs package identity → MSIX is strategic.
3. .NET 8/9 end of support 2026-11-10 → **resolved**: the .NET 10 SDK is installed and pinned in `global.json`.
4. **S2 has no written checklist, and S1's PB-02/03/05 methods depend on PresentMon/WPA**, which are
   not installed and not approved. PresentMon also cannot see layered-window (technique A) frames.
   The proposed replacement uses the system-wide DWM timing API, performance counters and a
   calibration step that must be able to fail.
5. The .NET SDK and the xUnit v3 test platform send usage telemetry by default. Phase 4 commands
   opted out per process. One SDK event may have been sent by the first command, before opt-out.

## Architectural decisions

| ADR | Decision | Status |
|---|---|---|
| ADR-001 | C# on .NET 10 LTS + WPF, isolated Win32/WinRT adapters, overlay technique chosen by S1 (fallback C), JSON storage, MSIX | **PROPOSED** — rendering path unresolved until S1/S2 run |

## Decisions required from the owner

| # | Decision | Recommendation |
|---|---|---|
| **Q1** | Approve the S1/S2 protocol (PROTOTYPE_S1_S2_REPORT §5), including built-in methods for PB-02/03/05 | Approve |
| **Q2** | Move PB-04 (music playing) from S1 to M3 | Approve |
| **Q3** | Build technique C as a minimal composition baseline | Approve |
| **Q4** | 60 Hz runs: owner switches the refresh rate manually (a) or harness switches temporarily (b) | (a) |
| **Q5** | PresentMon only if calibration fails | Approve that condition |
| **Q6** | About 3–3.5 h unattended machine time plus about 30 min attended for S2 manual items | Schedule |
| **Q7** | FlaUI (MIT, NuGet) for S2 automation | Approve |
| **Q8** | Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` permanently at user level | Recommend |
| D1 | Approve ADR-001 | After S1/S2 results |
| D3 | Distribution channel | Microsoft Store (unchanged) |
| D4/D5 | Lyrics; keyboard indicators | Defer (unchanged) |
| D6 | PB-10 ≤ 120 MB | Provisional ceiling; S1 will measure |
| D7 | External monitor; spare PC or VM | Needed for multi-monitor, mixed-DPI and clean-install tests |
| D8 | PresentMon, WPA, Accessibility Insights, PerfView | Not installed; see Q5 |
| D9 | Project licence | Before any public release |
| D10 | Old OneDrive copy; installed Nusa | Unchanged |
| D11 | Nusa runtime analysis | Keep skipped |

D2 (.NET 10 SDK) is done.

## Phase 4 plan

| Milestone | Scope | Status |
|---|---|---|
| M0 Toolchain & skeleton | SDK, spike solution, tests, analyzers, lock files | **Done** (production projects deferred to M2 — conflict C1) |
| M1a S1/S2 | Overlay techniques A/B/C, harness, calibration, S2 checklist | **Protocol proposed — awaiting Q1–Q7** |
| M1b S3–S8 | Remaining spikes | Not started; requires the S1/S2 gate |
| M2–M5 | Island shell, media and indicators, timer and tasks, accessibility and packaging | Not started |

## Blocked / pending

| Item | Blocker | Needs |
|---|---|---|
| S1/S2 implementation | Protocol review | Q1–Q7 |
| S1 at 60 Hz | Display setting change | Q4 |
| Multi-monitor / mixed-DPI | One display | D7 |
| Clean-install packaging tests | No VM, Home edition | D7 |
| Old OneDrive copy | Separate approval | Owner |

## Tests

| Suite | Command | Last result (2026-10-27, clean Release build) |
|---|---|---|
| Unit | `dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"` | 35/35 passed |
| Desktop integration | `... -- --filter-trait "Category=Integration"` | 2/2 passed |
| S1/S2 performance and interaction | — | NOT RUN — protocol awaiting approval |

## Known defects

None known in the M0 skeleton.

## Session note

The Phase 4 session started from the old OneDrive copy. All work used absolute paths in the canonical
repository `C:\dev\Notch\advanced-windows-island`; the OneDrive copy was not modified. Start future
sessions from the canonical path.

## Next recommended action

Owner reviews PROTOTYPE_S1_S2_REPORT §2–§5 and answers Q1–Q8. On approval: implement S1 (A, B, C,
harness, calibration), run the matrix, then S2, and stop at the gate with results.
