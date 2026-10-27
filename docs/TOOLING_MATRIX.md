# Tooling Matrix

| Purpose | Tool | Status | Used in Phase 1? | Notes |
|---|---|---|---|---|
| Hashing | `sha256sum`, `md5sum` (Git Bash) | Available | Yes | |
| File identification | `file` | Available | Yes | |
| PE parsing | Python `pefile` 2024.8.26 (session venv) | Available | Yes | headers, imports, resources, Rich header |
| Authenticode | PowerShell `Get-AuthenticodeSignature` | Available | Yes | both binaries `NotSigned` |
| Strings | Python regex extractor; MSYS2 `strings` | Available | Yes | ASCII + UTF-16LE |
| Inno Setup unpacking | Own Python parser (`zlib` CRC, `lzma`) | Available | Yes | handles 6.5+ encryption header, 64-bit block sizes, x86 call transform; output verified by SHA-256 |
| innoextract | — | Not installed | No | 1.9 likely predates Inno 6.7 format |
| GUID/COM scan | Own Python script | Available | Yes | |
| Disassembly | `objdump` (MSYS2) | Available | No | not required for this phase |
| Decompiler | Ghidra / IDA / Hopper | Not installed | No | optional; requires owner approval |
| REA (MCP investigation workflow) | `rea-agents` | **Blocked** | No | Node.js 24.11.1 < required 24.22.1 |
| Runtime sandbox | VM / Windows Sandbox | Not available | No | Home edition; no hypervisor software |
| Process / registry monitoring | Procmon, Process Explorer | Not installed | No | for future VM runtime analysis |
| Network capture | Wireshark / Fiddler | Not installed | No | for future VM runtime analysis |
| Web inspection | `curl` + Python HTML text extraction; WebFetch | Available | Yes | text-only; Chrome DevTools MCP failed to connect this session |
| .NET build/test | .NET SDK 9.0.317, `dotnet test` | Available | No | .NET 10 SDK not installed |
| Windows SDK | 10.0.22621.0 | Available | No | |
| IDE | Visual Studio Community 2022 | Available | No | |

Analysis scripts used in Phase 1 are versioned in `tools/re-analysis/` (our own code; they read but
never execute target files).
