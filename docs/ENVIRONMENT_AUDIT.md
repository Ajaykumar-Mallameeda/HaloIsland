# Environment Audit

Captured: 2026-10-27 on the owner's primary workstation. All values come from commands executed in
this session unless marked otherwise.

## 1. Host

| Item | Value | Source |
|---|---|---|
| OS | Windows 11 Home Single Language, 10.0.26300 | session environment |
| Architecture | x64 | `dotnet`/PE tooling, session |
| PowerShell | Windows PowerShell 5.1 (also Git Bash / MSYS2) | session |
| Desktop session | Interactive Windows desktop available (GUI apps can run) | session |
| Displays / DPI | NOT CAPTURED — command check unavailable during this session; re-run `[System.Windows.Forms.Screen]::AllScreens` | — |
| Virtualization software | None found: no VirtualBox, VMware Workstation, Hyper-V Manager (`vmms.exe`) or Windows Sandbox. `vmcompute.exe` present (host compute service, typically WSL2). | file checks |
| Windows Sandbox / Hyper-V | Not available on the Home edition | OS edition |

## 2. Developer tooling

| Tool | Version / status | Notes |
|---|---|---|
| Node.js | **v24.11.1** | Below REA minimum for the 24.x line (24.22.1+) — see §4 |
| npm | 11.6.1 | |
| Git | 2.55.0.windows.2 | global `user.name`/`user.email` configured |
| Claude Code | 2.1.311 | |
| .NET SDK | **9.0.317 only** | **No .NET 10 SDK.** Runtimes present: .NET 6/7/8/9/10.0.11 incl. WindowsDesktop 10.0.11 |
| Visual Studio | Community 2022 | workloads not enumerated |
| Windows SDK | 10.0.22621.0 | `Program Files (x86)\Windows Kits\10` |
| Python | 3.11.9 (+ per-session venv with `pefile` 2024.8.26 for analysis) | |
| Java | JDK 24 (`JAVA_HOME=C:\Program Files\Java\jdk-24`) | satisfies Ghidra's "JDK 21+" requirement if Ghidra is ever approved |
| winget / Chocolatey | available | not used |
| GNU binutils (MSYS2) | `strings`, `objdump` | |
| `file` | available (Git Bash) | |
| innoextract / 7-Zip / sigcheck | **not installed** | Not needed — Inno payload extracted with our own verified Python parser |
| Ghidra / IDA / Hopper | not installed | Optional; not installed (requires approval) |

## 3. Verification capabilities

| Capability | Available? |
|---|---|
| Build/run .NET desktop apps | Yes for .NET 9; .NET 10 requires SDK install (decision in Phase 3 ADR) |
| Automated unit tests (xUnit/MSTest via `dotnet test`) | Yes (packages restored from NuGet when first used) |
| GUI/manual UI testing | Yes — interactive desktop |
| Multi-monitor testing | Unknown until displays are captured; multi-monitor *logic* can be unit tested with simulated geometry |
| Isolated runtime analysis of reference binaries | **No** — no VM |
| Network capture | Not set up |

## 4. REA prerequisites

| Requirement (rea.tools/get-started) | Host | Status |
|---|---|---|
| Node 22.23.4+, 24.22.1+ or 26.12.1+ | 24.11.1 | **Not met** |
| npm | 11.6.1 | Met |
| Claude Code installed | 2.1.311 | Met |
| Optional Ghidra provider: Ghidra 12.1.4 + JDK 21+ | JDK 24 present, Ghidra absent | Optional — not installed |

## 5. Project directories

| Path | Purpose |
|---|---|
| `Notch/NusaSetup.exe` | Supplied reference installer (unmodified) |
| `Notch/advanced-windows-island/` | This Git repository |
| `Notch/re-workspace/` | Extraction and raw tool output (not version controlled) |

Note: the project lives under OneDrive (`Desktop` is OneDrive-synced). The reference installer and
`re-workspace/` will therefore sync to the owner's OneDrive account. Consider moving the project to a
non-synced path (e.g. `C:\dev\`) before builds start, to avoid sync churn on `bin/`/`obj/` and
uploading reverse-engineering artifacts to cloud storage.
