# Project Status — Project Halo

Last updated: 2026-10-27

## Current phase

**Phase 3 (architecture evaluation and proposal) — complete. Stopped at the Phase 3 approval gate.**
Phase 2 (evidence inventory) complete. No application code exists. Phase 4 has not started.

## Completed

| Item | Result |
|---|---|
| Bootstrap, Phase 0, Phase 1 | Repository, workflow, environment audit, website audit, installer static analysis, first feature matrix |
| Node.js upgrade; workspace relocation | 24.22.1; `C:\dev\Notch\` |
| **Phase 2.1 — REA verification** | `doctor` healthy for Claude Code; MCP connected; skill v49 discoverable; target-free PE tools and browser capture work; target-bound analysis unavailable without an engine (TOOLING_MATRIX) |
| **Phase 2.2 — evidence inventory** | NUSA_EVIDENCE_REGISTER (new, 89 entries); NUSA_FEATURE_MATRIX rebuilt (11 categories, 59 rows) |
| **Phase 2.3 — research docs** | NUSA_WEBSITE_AUDIT §9–10, NUSA_INSTALLER_STATIC_ANALYSIS §10, NUSA_RUNTIME_ANALYSIS (16 open runtime questions), EXTERNAL_REFERENCES (new) |
| **Phase 2 — environment capture** | Display/DPI/GPU/brightness/battery; Windows SDK 10.0.26100; Rust 1.97; MSVC 14.43 |
| **Phase 3 — architecture** | ARCHITECTURE_DECISION_RECORD (ADR-001, proposed), PRODUCT_REQUIREMENTS, SECURITY_AND_PRIVACY_DESIGN, TESTING_STRATEGY |

## Key findings

1. The supplied installer matches the hash in the vendor's GitHub **v1.1.0** pre-release notes; the
   Phase 1 mismatch came from the website still describing v1.0.0.
2. No evidence of UI Automation, system reduced-motion or high-contrast support in the reference.
3. `UserNotificationListener` needs package identity + user consent → MSIX packaging is strategic.
4. Windows sends no notification when a full-screen app starts or stops → event-driven heuristics.
5. **.NET 8 and .NET 9 reach end of support on 2026-11-10**; only .NET 10 LTS is viable.

## Architectural decisions

| ADR | Decision | Status |
|---|---|---|
| ADR-001 | C# on .NET 10 LTS + WPF, isolated Win32/WinRT adapters, overlay transparency chosen by spike S1 (fallback: Win32 + Windows.UI.Composition), versioned JSON storage, MSIX via Microsoft Store | **PROPOSED — awaiting owner approval** |

## Decisions required from the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | Approve ADR-001 | Approve, with spikes S1–S2 as a hard gate before feature work |
| D2 | Install the **.NET 10 SDK** (and optionally the VS ".NET desktop development" workload) at Phase 4 start | Approve — required by every .NET option; .NET 9 support ends 2026-11-10 |
| D3 | Distribution channel | Microsoft Store (free signing); decide on a paid certificate only if direct download is wanted (Artifact Signing for individuals is USA/Canada only) |
| D4 | Include synced lyrics? | Defer; requires licensing review of lyrics/LRCLIB terms |
| D5 | Include keyboard indicators (input monitoring)? | Defer to Advanced, opt-in only, after spike S6 |
| D6 | Accept memory budget PB-10 ≤ 120 MB | Accept for v1; re-evaluate after spike S1 numbers |
| D7 | Test hardware: external monitor; spare PC or VM for clean installs | Provide before milestone M5 |
| D8 | Install test tools in Phase 4 (PresentMon, WPA, Accessibility Insights, PerfView) | Approve when M1 starts |
| D9 | Project licence | Decide before any public repository or release |
| D10 | Old OneDrive copy; pre-existing Nusa install | Unchanged; separate decisions |
| D11 | Runtime analysis of Nusa | Keep skipped; reconsider only if a spare PC is available and a runtime question becomes decision-relevant |

## Proposed Phase 4 plan (not started)

| Milestone | Scope | Exit criteria (see TESTING_STRATEGY §5) |
|---|---|---|
| M0 Toolchain & skeleton | .NET 10 SDK (after D2); solution with `Halo.Core`, `Halo.Platform.Windows`, `Halo.Infrastructure`, `Halo.App`, test projects; analyzers; lock files; `.gitignore` additions | Build + `dotnet test` green |
| M1 Spikes S1–S8 | Overlay transparency A/B/C, hit-testing/focus, GSMTC packaged/unpackaged, Core Audio callbacks, brightness, keyboard indicators, full-screen matrix, MSIX size/startup | Results with raw numbers recorded in ADR §13; S1/S2 pass or approved fallback |
| M2 Island shell | Geometry, window, state machine, hover/click/keyboard, full-screen suppression, tray, settings, persistence, single instance, logging, module supervisor | FR-ISL/FR-APP tests; PB-01–03, 05, 06, 09, 10 measured |
| M3 Media + indicators | GSMTC module, volume HUD, battery, network | FR-MED/FR-SYS-01…04; PB-04, 07, 08 |
| M4 Timer + tasks | Timer/Pomodoro, tasks with reminders | FR-TIM/FR-TSK tests incl. sleep/resume |
| M5 Accessibility + packaging | UIA polish, Narrator/keyboard/HC/reduced-motion scripts, MSIX, soak | TESTING_STRATEGY §4.7, §4.9; privacy checklist |
| Advanced (later approval) | Brightness, keyboard indicators, Bluetooth, water, shelf, lyrics, notifications | Per PRODUCT_REQUIREMENTS §3.7 |

## Blocked / pending

| Item | Blocker | Needs |
|---|---|---|
| Phase 4 start | Phase 3 approval gate | Owner decisions D1–D2 |
| Multi-monitor / mixed-DPI testing | One display attached | External monitor (D7) |
| Clean-install packaging tests | No VM, Home edition | Spare PC or VM (D7) |
| Old workspace copy at OneDrive Desktop | Removal needs separate approval | Owner confirmation |

## Session note

The Phase 2/3 Claude Code session was started from the old OneDrive copy; all work used absolute
paths in the canonical repository `C:\dev\Notch\advanced-windows-island`. The OneDrive copy
(2 commits behind) was not modified. Start future sessions from the canonical path.

## Tests

No code yet — no automated tests exist. Documentation validation is listed in each commit message.

## Known defects

None (no code).

## Next recommended action

Owner reviews ADR-001 and decisions D1–D11. On approval: Phase 4 milestone M0, then spikes S1–S2.
