# Tooling Matrix

Last updated: 2026-10-27 (Phase 4, S1/S2 gate).

| Purpose | Tool | Status | Used in | Notes |
|---|---|---|---|---|
| Hashing | `sha256sum`, `md5sum` (Git Bash), `Get-FileHash` | Available | P1, P2 | |
| File identification | `file` | Available | P1 | |
| PE parsing | Python `pefile` 2024.8.26 (session venv) | Available | P1 | headers, imports, resources, Rich header |
| PE parsing (independent) | REA `inspect_pe_imports/resources/certificates/load_config`, `inspect_native_exports`, `inspect_rust_binary` | **Working** | P2 | Target-free; no engine needed. Agrees with `pefile` |
| Authenticode | PowerShell `Get-AuthenticodeSignature` | Available | P1 | both binaries `NotSigned` |
| Strings | Python regex extractor; MSYS2 `strings` | Available | P1, P2 (re-read of P1 output) | ASCII + UTF-16LE |
| Inno Setup unpacking | Own Python parser (`zlib` CRC, `lzma`) | Available | P1 | output verified by SHA-256 |
| innoextract | — | Not installed | — | 1.9 likely predates Inno 6.7 format |
| GUID/COM scan | Own Python script | Available | P1 | |
| Disassembly | `objdump` (MSYS2); REA `inspect_with_objdump` advertised | Available, not used | — | not required |
| Decompiler / deep analysis | Ghidra / IDA / JADX / Kuna / Hopper | **Not installed / not configured** (by decision) | — | REA reports `provider_unavailable` for strings, xrefs, decompilation. Kuna does not admit PE targets in REA 6.8.0. Hopper is macOS-only |
| REA (MCP investigation workflow) | `rea-agents` 6.8.0 (MIT, `github.com/morluto/rea`) | **Configured and connected** for Claude Code | P2 | `doctor --client claude_code`: `healthy: true`, registration aligned (`npx -y rea-agents@6.8.0 mcp` in `~/.claude.json`, backup `~/.claude.json.rea.backup`), skill `reverse-engineer-anything` v49 aligned, 229 MCP tools. Registrations for other clients are absent (`config_drift`) — out of scope, untouched |
| Website observation | REA `capture_browser_scenario` (launch mode, headless Edge, temp profile) | **Working** | P2 | Chrome DevTools MCP failed to connect (`CONNECTION_CLOSED`) |
| Runtime sandbox | VM / Windows Sandbox | Not available | — | Home edition; no hypervisor software |
| Process / registry monitoring | Procmon, Process Explorer | Not installed | — | for future isolated runtime analysis |
| Network capture | Wireshark / Fiddler | Not installed | — | for future isolated runtime analysis |
| Web fetch | `curl`; WebFetch | Available | P1, P2 | GitHub REST API (unauthenticated) |
| .NET build/test | .NET SDK **10.0.403** (pinned in `global.json`) and 9.0.317 | Available | P4 | xUnit v3 4.0.2 on Microsoft.Testing.Platform. Telemetry opt-out set for the user (DEVELOPMENT_WORKFLOW §6) |
| Windows SDK | 10.0.22621.0, 10.0.26100.0 | Available | — | |
| C/C++ compiler | MSVC 14.43 (`cl.exe` present under VS 2022 Community; only the Core Editor workload is registered) | Available (unverified build) | — | Relevant only to a native option |
| Rust | rustc / cargo 1.97.0, `stable-x86_64-pc-windows-msvc` | Available | — | Found in Phase 3 environment check |
| IDE | Visual Studio Community 2022 | Available | — | .NET desktop workload **not** registered (vswhere) |
| Frame timing (PB-03/PB-05) | DXGI Desktop Duplication metadata (built into Windows), region-attributed, in `Halo.Spikes.Harness` | **Used**, valid only after calibration | P4 S1 | Observes the composed desktop (what DWM presented), not app presents; needs the static backdrop and contamination checks (PROTOTYPE_S1_S2_REPORT §8.5). Reads timestamps and dirty rectangles only, never pixels |
| Composition timing API | `DwmGetCompositionTimingInfo` | **Unsuitable** | — | Returns synthetic per-call counters on 10.0.26300 (gap G8); never used for results |
| CPU, wake-ups, GPU (PB-01/02/10/12) | `Process` APIs, `QueryProcessCycleTime`, PDH raw counters (`Thread`, `GPU Engine`) | **Used** | P4 S1 | Non-elevated. PB-02 sees threads alive at 10 s sample times; a new thread invalidates the run, ended threads are reported (v1.3) |
| WPR / tracerpt / logman | Built into Windows | Available, not used | — | WPR needs elevation. The account is in Performance Log Users, so `logman` user-mode ETW sessions work without elevation; next built-in candidate if a method fails calibration |
| PresentMon / WPA | — | **Not installed, not approved** | — | Considered only if a calibration fails or a method is unsuitable, after documenting provenance, licence, data collected and owner approval (report §8.14). PresentMon cannot observe layered-window (technique A) updates |
| UI automation (S2) | FlaUI.Core / FlaUI.UIA3 5.0.0 (MIT), Interop.UIAutomationClient 10.19041.0 (MIT) | **Validated and used** (harness only) | P4 S2 | Licence read from each package's `LICENSE.txt`; SHA-256 recorded (report §8.13). On this build the Alt-Tab switcher exposes no list items to UIA (manual S2-07M) |
| Accessibility tooling | Accessibility Insights for Windows; Narrator; Axe.Windows | Narrator built in; Accessibility Insights and Axe.Windows not installed | — | M5. S2-12 uses FlaUI UIA3 only |

Analysis scripts used in Phase 1 are versioned in `tools/re-analysis/` (our own code; they read but
never execute target files).

## REA verification record (2026-10-27)

| Step | Result |
|---|---|
| `npx -y rea-agents@6.8.0 doctor --client claude_code` | `healthy: true`; `environment_healthy: false` only because optional engines and other clients are absent |
| MCP server connection (`claude mcp list`) | `rea … ✔ Connected` |
| Live server identity (`binary_session`) | rea 6.8.0, MCP protocol 2025-11-25, client claude-code 2.1.311, skill expected v49 |
| Tool inventory | 229 tools; 4 provider candidates (ghidra, ida, kuna: `not_configured`; hopper: `unsupported_host`) |
| Skill discovery | `reverse-engineer-anything` listed and loaded in-session |
| `open_binary` on `nusa.exe.bin` | Succeeded (format pe, x86_64, SHA-256 matches) |
| `search_strings` (target-bound) | **Failed** `provider_unavailable` — needs Ghidra/IDA |
| Target-free PE tools | Succeeded (see `NUSA_INSTALLER_STATIC_ANALYSIS.md` §10) |
| `capture_browser_scenario` | Succeeded (2 runs) |
| `close_binary` | Succeeded; reference artifact hashes unchanged |

Capabilities that require an external engine (not installed by decision): disassembly listings,
pseudocode/decompilation, function/call graphs, cross-references, string-reference tracing,
native value tracing. Runtime tools (Frida, GDB, rizin debug, process capture) are advertised but
were not used — runtime analysis is out of scope.
