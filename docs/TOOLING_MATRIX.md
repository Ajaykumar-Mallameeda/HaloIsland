# Tooling Matrix

Last updated: 2026-10-27 (Phase 2).

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
| .NET build/test | .NET SDK 9.0.317 | Available | — | **.NET 9 SDK targets go out of support 2026-11-10.** .NET 10 SDK not installed (deferred; Phase 4 decision) |
| Windows SDK | 10.0.22621.0, 10.0.26100.0 | Available | — | |
| C/C++ compiler | MSVC 14.43 (`cl.exe` present under VS 2022 Community; only the Core Editor workload is registered) | Available (unverified build) | — | Relevant only to a native option |
| Rust | rustc / cargo 1.97.0, `stable-x86_64-pc-windows-msvc` | Available | — | Found in Phase 3 environment check |
| IDE | Visual Studio Community 2022 | Available | — | .NET desktop workload **not** registered (vswhere) |
| Performance tooling | WPR/WPA (built into Windows); PresentMon | WPR present with Windows; PresentMon not installed | — | Needed in Phase 4 for measured budgets |
| Accessibility tooling | Accessibility Insights for Windows; Narrator | Narrator built in; Accessibility Insights not installed | — | Phase 4 |

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
