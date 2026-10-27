# Environment Audit

Captured: 2026-10-27 on the owner's primary workstation. All values come from commands executed in
this session unless marked otherwise.

## 1. Host

| Item | Value | Source |
|---|---|---|
| OS | Windows 11 Home Single Language, 10.0.26300, UBR 9711 (registry `ProductName` still reads "Windows 10 …", `DisplayVersion` 26H2) | session environment; spike `EnvironmentSnapshot` (Phase 4, 2026-10-27) |
| Architecture | x64 | `dotnet`/PE tooling, session |
| PowerShell | Windows PowerShell 5.1 (also Git Bash / MSYS2) | session |
| Desktop session | Interactive Windows desktop available (GUI apps can run) | session |
| Displays / DPI | **One display** (internal laptop panel `LGD0738`), 1920×1080 @ 120 Hz, scaling 125 % (AppliedDPI 120; a DPI-unaware query reports 1536×864), taskbar at the bottom (work area 816 of 864 logical px) | `Screen.AllScreens`, `Win32_VideoController`, registry (captured 2026-10-27, Phase 3) |
| GPU | Hybrid: Intel UHD Graphics (driver 32.0.101.7088, drives the panel) + NVIDIA GeForce RTX 3050 6 GB Laptop GPU (driver 32.0.16.1088) | `Win32_VideoController` |
| Brightness | WMI `WmiMonitorBrightness` available on the internal panel (current 50) | `root\wmi` query |
| Battery | Present (Dell `P8P1P41`) — laptop power events testable | `Win32_Battery` |
| Display modes (1920×1080) | **120 Hz and 60 Hz** both offered by the driver (read-only `EnumDisplaySettings` enumeration; refresh rate not changed) | Phase 4, 2026-10-27 |
| Power at Phase 4 start | AC online, battery 100 %, power plan Balanced, power-mode overlay `ded574b5-45a0-4f42-8737-46345c09c238` (commonly documented as "Best performance"; name not verified against Settings) | `GetSystemPowerStatus`, `PowerGetEffectiveOverlayScheme`, `powercfg` |
| Virtualization software | None found: no VirtualBox, VMware Workstation, Hyper-V Manager (`vmms.exe`) or Windows Sandbox. `vmcompute.exe` present (host compute service, typically WSL2). | file checks |
| Windows Sandbox / Hyper-V | Not available on the Home edition | OS edition |

## 2. Developer tooling

| Tool | Version / status | Notes |
|---|---|---|
| Node.js | **v24.22.1** (upgraded 2026-10-27 from v24.11.1 via `winget upgrade --id OpenJS.NodeJS.LTS --exact --version 24.22.1`; installer `node-v24.22.1-x64.msi` from nodejs.org, winget hash verified) | Meets REA minimum — see §4 |
| npm | 11.6.1 | same version before and after the Node upgrade (`C:\Program Files\nodejs\npm.cmd -v` → 11.6.1); a user-global npm also exists in `%APPDATA%\npm` |
| Git | 2.55.0.windows.2 | global `user.name`/`user.email` configured |
| Claude Code | 2.1.311 | |
| .NET SDK | **10.0.403** (installed 2026-10-27, Phase 4) and 9.0.317 | Installed from the official Microsoft installer `dotnet-sdk-10.0.403-win-x64.exe` (builds.dotnet.microsoft.com; SHA-512 matched the 10.0 `releases.json`, SHA-256 matched the winget manifest, Authenticode valid, signer CN=.NET, O=Microsoft Corporation). Release 10.0.14 (2026-10-25, security release) was the latest stable at install time. Added runtimes NETCore/AspNetCore/WindowsDesktop **10.0.14**; host now 10.0.14. Existing SDKs/runtimes kept (6/7/8/9, 10.0.11). No workloads installed. The repository pins the SDK in `global.json` (`latestPatch`). .NET 8 and .NET 9 reach end of support on **2026-11-10**; .NET 10 (LTS) is supported to 2028-11-14 |
| .NET first-run side effects | The first .NET 10 CLI command created an **untrusted** ASP.NET Core HTTPS development certificate in the user certificate store (not trusted, not used). The first command (`dotnet new sln`) ran before telemetry opt-out, so one SDK usage event may have been sent | Phase 4 session output |
| Telemetry opt-out | Not set machine- or user-wide. Phase 4 commands set `DOTNET_CLI_TELEMETRY_OPTOUT=1` per process (also honoured by Microsoft.Testing.Platform). Setting it permanently is an owner decision (see `spikes/README.md`) | |
| Visual Studio | Community 2022 and Build Tools 2022, both 17.14.37531.7 | Not needed for spike builds (the .NET SDK builds WPF). `vswhere` registers only the Core Editor workload (no ".NET desktop" or "Desktop C++" workload); MSVC toolsets 14.43/14.44 folders exist and `cl.exe` 14.43 is present |
| Windows SDK | 10.0.22621.0 and 10.0.26100.0 | `Program Files (x86)\Windows Kits\10\Include` |
| Rust | rustc/cargo 1.97.0, default toolchain `stable-x86_64-pc-windows-msvc` | `%USERPROFILE%\.cargo\bin` (found 2026-10-27) |
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
| Build/run .NET desktop apps | **Yes, .NET 10** — `net10.0-windows` WPF builds with 0 warnings and a WPF window renders (spike `--smoke`, Phase 4 M0) |
| Automated unit tests (xUnit/MSTest via `dotnet test`) | Yes — xUnit v3 4.0.2 on Microsoft.Testing.Platform (`global.json` opts in; the VSTest path is not supported for it on the .NET 10 SDK) |
| GUI/manual UI testing | Yes — interactive desktop |
| Multi-monitor testing | **Not possible on this machine as captured (single display).** Needs an external monitor; multi-monitor *logic* can be unit tested with simulated geometry |
| Mixed-DPI testing | Not possible with one display; single-display DPI changes (100–175 %) can be tested by changing scaling |
| High-refresh testing | Yes — 120 Hz panel; 60 Hz mode available (switching it changes a user display setting → owner decision, see `PROTOTYPE_S1_S2_REPORT.md`) |
| Elevation | Claude Code shell runs **non-elevated**; elevated steps (installer) needed a UAC prompt approved by the owner |
| Frame-timing tools | **None installed**: no PresentMon, no WPA/ADK. Built in: WPR, `tracerpt`, performance counters, DWM timing API |
| Isolated runtime analysis of reference binaries | **No** — no VM |
| Network capture | Not set up |

## 4. REA prerequisites

| Requirement (rea.tools/get-started) | Host | Status |
|---|---|---|
| Node 22.23.4+, 24.22.1+ or 26.12.1+ | 24.22.1 | Met (after upgrade; was 24.11.1) |
| npm | 11.6.1 | Met |
| Claude Code installed | 2.1.311 | Met |
| Optional Ghidra provider: Ghidra 12.1.4 + JDK 21+ | JDK 24 present, Ghidra absent | Optional — not installed |

## 5. Project directories

| Path | Purpose |
|---|---|
| `C:\dev\Notch\NusaSetup.exe` | Supplied reference installer (unmodified) |
| `C:\dev\Notch\advanced-windows-island\` | This Git repository |
| `C:\dev\Notch\re-workspace\` | Extraction and raw tool output (not version controlled) |

Relocation (2026-10-27): the workspace was originally created under the OneDrive-synced Desktop
(`%USERPROFILE%\OneDrive\Desktop\Notch`). It was copied to the non-synced `C:\dev\Notch` with
`robocopy /E /COPY:DAT /DCOPY:DAT` (87 files, 0 failures). Validation: all 87 files matched a
pre-move SHA-256 manifest; `git fsck --full` clean; branch `main` and both commits
(`bd31bc3`, `9f7d5fb`) preserved; reference-artifact hashes unchanged; all six analysis scripts
re-run from the new location reproduced their original outputs (two differed only by BOM/line
endings introduced when the originals were captured). C: had 92 GB free.
