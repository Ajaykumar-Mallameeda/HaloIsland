# Project Status — Project Halo

Last updated: 2026-10-27

## Current phase

**Phase 2 (reverse-engineering research and evidence collection) — complete.**
Phase 3 (architecture evaluation) — in progress. No application code exists.

## Completed

| Item | Result |
|---|---|
| Bootstrap Task 2 — local Git repository | `C:\dev\Notch\advanced-windows-island\`, branch `main`, commit template configured |
| Bootstrap Task 5 — initial documents | README, `.gitignore`, PROJECT_STATUS, DEVELOPMENT_WORKFLOW, REVERSE_ENGINEERING_WORKFLOW |
| Phase 0 — environment discovery | ENVIRONMENT_AUDIT, SAFETY_AND_SCOPE, TOOLING_MATRIX |
| Phase 1.1 — website audit (text-only) | NUSA_WEBSITE_AUDIT |
| Phase 1.2 — installer static analysis | NUSA_INSTALLER_STATIC_ANALYSIS (payload extracted, SHA-256 verified) |
| Phase 1.4 — reports | NUSA_FEATURE_MATRIX, REFERENCE_ARCHITECTURE_HYPOTHESES |
| Node.js upgrade | 24.11.1 → 24.22.1 |
| Workspace relocation | OneDrive Desktop → `C:\dev\Notch\` |
| **Phase 2.1 — REA verification** | `doctor` healthy for Claude Code; MCP connected; skill v49 discoverable; target-free PE tools and browser capture work; target-bound analysis unavailable without an engine (TOOLING_MATRIX) |
| **Phase 2.2 — evidence inventory** | NUSA_EVIDENCE_REGISTER (new, ~90 entries), NUSA_FEATURE_MATRIX (rebuilt, 11 categories / 59 rows) |
| **Phase 2.3 — research docs** | NUSA_WEBSITE_AUDIT §9–10 (live interaction, GitHub releases), NUSA_INSTALLER_STATIC_ANALYSIS §10 (REA corroboration), NUSA_RUNTIME_ANALYSIS (16 runtime-only questions), EXTERNAL_REFERENCES (new) |
| **Phase 2 — environment capture** | Displays/DPI/GPU/brightness/battery captured; Windows SDK 10.0.26100, Rust 1.97, MSVC 14.43 found (ENVIRONMENT_AUDIT) |

## Key Phase 2 findings

1. The supplied installer's SHA-256 equals the hash published in the vendor's GitHub **v1.1.0**
   pre-release notes; the Phase 1 mismatch came from the website still describing v1.0.0.
2. No evidence of UI Automation, system reduced-motion or high-contrast support in the reference.
3. `UserNotificationListener` requires package identity and user consent (Microsoft docs).
4. Windows sends no notification when a full-screen app starts or stops (Microsoft docs).
5. .NET 8 and .NET 9 reach end of support on **2026-11-10**; only .NET 10 (LTS) is viable for new work.

## Owner decisions (2026-10-27)

| # | Decision | Status |
|---|---|---|
| 1 | Upgrade Node.js and configure REA | Done. REA registration in `~/.claude.json` was present at session start (backup `~/.claude.json.rea.backup`, 15:10); Phase 2 verified it and changed no configuration |
| 2 | Skip runtime analysis | Still in force; provenance precondition now partly met (hash match), isolation precondition not met |
| 3 | Move workspace to `C:\dev\Notch\` | Done. **The Phase 2/3 Claude Code session was started from the old OneDrive copy**; all work used absolute paths in the canonical repo. The OneDrive copy (2 commits behind) is unchanged |
| 4 | Defer .NET 10 SDK until Phase 3 ADR | Recorded; ADR will request a decision |
| 5 | Internal codename "Project Halo" | Recorded |

## Blocked / pending

| Item | Blocker | Needs |
|---|---|---|
| Old workspace copy at OneDrive Desktop | Removal needs separate approval | Owner confirmation |
| Pre-existing Nusa install on workstation | Owner's choice | Keep or uninstall (project will not touch it) |
| Multi-monitor testing | Only one display attached | External monitor for Phase 4 tests |

## Tests

No code yet — no automated tests exist. Documentation validation is listed in each commit message.

## Known defects

None (no code).

## Architectural decisions

None accepted yet (Phase 3 in progress).

## Next recommended action

Complete Phase 3 (ADR, product requirements, security/privacy design, testing strategy) and stop
at the Phase 3 approval gate.
