# ADR-001 — Application stack and architecture for Project Halo

| Field | Value |
|---|---|
| Status | **PROPOSED** — awaiting owner approval at the Phase 3 gate |
| Date | 2026-10-27 |
| Decision confidence | **Medium** overall. High for runtime/platform choices (§2, §5–§8); Medium for the overlay rendering path: option C provisionally selected after S1/S2 (§13.1) |
| Inputs | `NUSA_EVIDENCE_REGISTER.md`, `NUSA_FEATURE_MATRIX.md`, `ENVIRONMENT_AUDIT.md`, `EXTERNAL_REFERENCES.md` |
| Related | `PRODUCT_REQUIREMENTS.md`, `SECURITY_AND_PRIVACY_DESIGN.md`, `TESTING_STRATEGY.md` |

Everything in this document is **PROPOSED** design for our product unless it cites evidence. No
benchmark in this document has been run; all performance numbers are targets to be validated.

## 1. Context

Project Halo is an original top-edge "island" overlay for Windows: compact when idle, expandable on
hover or click, hosting live activities (media, timers, tasks, system indicators). Requirements that
dominate the architecture:

1. **Always-on overlay** with per-pixel transparency, topmost, never activating or stealing focus,
   input limited to its visible shape (E-031, E-032, E-125).
2. **Idle cost near zero**: no rendering while settled, event-driven integrations (E-036, E-091).
3. **Smooth morphing animation** at 60–120 Hz on a hybrid-GPU laptop (E-039; environment: 120 Hz
   panel, Intel UHD + RTX 3050).
4. **Accessibility** (UIA, keyboard, high contrast, reduced motion) — a gap in the reference (E-130, E-131).
5. **Windows integrations** spanning Win32 (power, displays, hooks), COM (Core Audio, OLE drag-drop)
   and WinRT (media sessions, network, Bluetooth, notifications).
6. **Solo-developer maintainability**, testability and a credible signed distribution path.

Environment constraints (verified 2026-10-27): .NET SDK 9.0.317 only; .NET 10.0.11 runtimes present;
**.NET 8 and .NET 9 reach end of support on 2026-11-10**, .NET 10 LTS is supported to 2028-11-14;
Windows App SDK 2.0 is current (servicing to 2027-04-29); Visual Studio 2022 Community without the
.NET desktop workload registered; Rust 1.97 and MSVC 14.43 present; single 125 % display.

## 2. Decision

**Recommended stack (PROPOSED):**

| Layer | Choice |
|---|---|
| Language / runtime | C# on **.NET 10 LTS**, TFM `net10.0-windows10.0.19041.0` (WinRT projections via CsWinRT) |
| UI framework | **WPF** for the settings window. Island content: **amended 2026-10-28** to Windows.UI.Composition with a custom UIA provider (option C, provisional, §13.1) |
| Win32 / COM interop | Isolated adapters using `Microsoft.Windows.CsWin32` source-generated P/Invoke (no hand-written signatures where avoidable) |
| WinRT | Built-in .NET Windows SDK projections (GSMTC, NetworkInformation, DeviceWatcher, UserNotificationListener) |
| Overlay window | Dedicated topmost, non-activating, tool window; transparency technique chosen by spike S1 between **A** (WPF layered window) and **B** (non-layered window with DWM per-pixel alpha); **C** (Win32 + `Windows.UI.Composition` desktop target) is the fallback if neither A nor B meets the budgets. **2026-10-28: C provisionally selected (§13.1)** |
| Persistence | Versioned JSON files via `System.Text.Json` (in-box), atomic replace |
| Logging | `Microsoft.Extensions.Logging` abstractions + a small in-repo rolling file sink |
| Packaging | **MSIX** (package identity), distributed via the Microsoft Store as the primary channel; signed direct download only if the owner funds a certificate (§11) |
| Tests | xUnit v3, FlaUI (UIA), Axe.Windows (automated accessibility rules), WPR/WPA + PresentMon (performance) |

**Prerequisite owner decision:** install the **.NET 10 SDK** at the start of Phase 4. The installed
.NET 9 SDK cannot target `net10.0`, and targeting .NET 9 would start the project on a runtime that
leaves support 14 days from now. This applies to every .NET-based option, including WinUI 3.

### Rationale in one paragraph

WPF on .NET 10 gives the fastest route to a correct, accessible, testable product: mature UIA
automation peers, high-contrast and keyboard support, data binding for MVVM unit testing, first-class
WinRT and COM access from C#, and no extra runtime beyond .NET. Its known weakness is the cost and
behaviour of transparent top-level windows. The island is small (≈ 0.1 megapixel at its largest), the
weakness is measurable, and it is isolated behind an `IIslandSurface` boundary so the renderer can be
replaced without touching the activity engine, modules or settings. WinUI 3's fully transparent
window path appears to be new with Windows App SDK 2.0 (April 2026; secondary sources, see
`EXTERNAL_REFERENCES.md` §3), the SDK ships new majors up to every six months with 12-month
servicing, and XAML views are harder to unit test. A fully native renderer would win on footprint and idle cost
but multiplies the work for accessibility, settings UI and testing — exactly where the reference
product is weakest.

## 3. Alternatives evaluated

Ratings: ++ strong, + good, 0 neutral, − weak, −− poor. **E** = supported by cited evidence or
documentation; **X** = engineering expectation to be measured.

| Criterion | WPF + Win32 interop | WinUI 3 (WinAppSDK 2.x) | Native Win32 + DirectComposition/Direct2D (C++ or Rust) |
|---|---|---|---|
| Animation smoothness / latency | + UI-thread animations; must keep UI thread free; desired frame rate must be raised for 120 Hz (X) | ++ compositor-thread animations (implicit/spring) (X) | ++ DWM-composited animations, full frame control (E-021 shows the reference uses this) |
| Idle CPU / memory / GPU | 0/− render thread idles when nothing animates (X); .NET + WPF baseline memory expected well above the reference's ~30–47 MB (E-140, E-141) (X) | − larger runtime/framework footprint (X) | ++ smallest footprint (X) |
| Transparency, topmost, hit testing, taskbar | − layered windows (`AllowsTransparency`) cost a readback per frame; non-layered DWM alternative needs spike; topmost/no-activate via Win32 styles (X) | − transparent windows new in 2.0 with reported light/dark artefacts (secondary sources); Win32 style edits still needed | ++ `WS_EX_NOREDIRECTIONBITMAP` + composition, regions (E) |
| Multi-monitor, Per-Monitor V2 DPI, display changes | + PMv2 supported since .NET Core 3; per-monitor scaling events | + | + all manual |
| Fullscreen and focus detection | Same Win32 APIs in all options (E-124) | same | same |
| Media sessions and notifications | ++ WinRT projections in .NET | ++ | + C++/WinRT or `windows` crate; more boilerplate |
| System indicators (audio, power, BT) | + COM via CsWin32/ComWrappers; WinRT direct | + | ++ |
| Accessibility, keyboard, high contrast | ++ mature UIA peers, keyboard, HC themes | + good UIA; fewer mature patterns for custom overlays | −− UIA providers, focus model and HC all hand-written |
| Security and privacy boundaries | + managed memory safety; same OS boundaries | + | − C++ memory safety risk; Rust mitigates |
| Maintainability, testability, debugging, speed | ++ MVVM unit tests, FlaUI, VS debugging; fastest for C# | 0 XAML testing needs app context; SDK churn | − slowest; UI testing hardest |
| Packaging, signing, updates, uninstall | + MSIX or unpackaged; self-contained adds size | + MSIX natural; WinAppSDK runtime dependency or self-contained | ++ tiny binary; any packaging |
| Dependency size, long-term compatibility | + .NET 10 LTS to 2028; WPF stable, in-box with .NET | − WinAppSDK majors ≤ 6 months, 12-month servicing (E: release-channels page) | ++ OS APIs only |
| Fit with available environment | + needs .NET 10 SDK (approval) and VS ".NET desktop" workload or CLI only | − same SDK plus WinAppSDK tooling | + MSVC 14.43 and Rust 1.97 already present |

**Why not WinUI 3:** its main advantage (compositor-driven animation) can be obtained from a Win32
window through `Windows.UI.Composition` (option C) without adopting WinAppSDK's release cadence and
the newer, less-proven transparent-window path. Re-evaluate if spike S1 fails for both A and C.

**Why not fully native:** the reference product already demonstrates the native approach's
footprint (E-020, E-021) — and its gaps (no UIA, no system reduced-motion, defects in frame pacing
and focus handling: E-039, E-125, E-130, E-131). Our differentiators are accessibility, robustness
and testability, which a native UI makes most expensive. A native (C++ or Rust) renderer remains the
documented fallback **for the overlay surface only** if the .NET memory budget proves unacceptable.

**Alternatives also considered:** Avalonia (cross-platform, own renderer; adds a large dependency
and its own windowing layer for no Windows-specific gain); WinForms (no modern composition or
accessibility advantage); Electron/WebView2 (contradicts the idle and memory goals).

## 4. Windowing and rendering strategy

1. **Process DPI awareness:** Per-Monitor V2 declared in the application manifest (not at runtime).
2. **Island window:** one per active monitor (initially one), `WS_POPUP` with `WS_EX_TOOLWINDOW`
   (no taskbar/Alt-Tab), `WS_EX_NOACTIVATE`, `WS_EX_TOPMOST`; WPF `ShowActivated=false`,
   `ShowInTaskbar=false`. The window never calls `SetForegroundWindow` except when the user
   explicitly invokes keyboard focus (§6, FR-ISL-07).
3. **Fixed canvas, animated shape:** the window is sized once to the largest presentation for the
   monitor (expanded width × height plus shadow margin, in physical pixels after DPI scaling) and
   centred under the top edge of the monitor's work area. Animations change the shape inside the
   window; the window is **not** resized per frame (avoids `SetWindowPos` jank).
4. **Input region:** clicks outside the visible shape must reach the windows underneath.
   - Layered window (A): fully transparent pixels are click-through by the OS.
   - Non-layered (B/C): `SetWindowRgn` with the union of current and target shapes, updated at
     transition start and end. `WM_NCHITTEST`→`HTTRANSPARENT` is **not** sufficient because it only
     forwards to windows of the same thread.
5. **Transparency techniques under test (spike S1):**
   - **A — WPF `AllowsTransparency=True`** (layered). Simplest. Risk: per-frame readback cost.
   - **B — non-layered WPF window with DWM-extended frame and transparent `HwndTarget` background.**
     Hardware path without readback; risk: frame/shadow artefacts, behaviour across Windows builds.
   - **C — Win32 host window (`WS_EX_NOREDIRECTIONBITMAP`) with a `Windows.UI.Composition` visual
     tree** via `ICompositorDesktopInterop`. Best animation path; text must be drawn with
     Direct2D/DirectWrite into composition surfaces and UIA must be provided by a custom provider —
     a large cost, hence fallback only.
6. **Render only when needed:** no `CompositionTarget.Rendering` subscriptions while settled;
   animations are finite; continuous visuals (e.g., a sound-wave) are optional and capped (FR-MED-06).
   Rendering pauses entirely while the island is hidden, the display is off
   (`GUID_SESSION_DISPLAY_STATUS`) or the session is locked.
7. **Refresh rate:** WPF animations default to about 60 fps (`Timeline.DesiredFrameRate`; to be
   confirmed in S1); the desired frame rate is raised to the monitor refresh rate for transitions,
   and frame pacing is measured at 60 and 120 Hz (E-039 shows this was a real defect in the
   reference).
8. **Device loss and display changes:** handle `WM_DISPLAYCHANGE`, `WM_DPICHANGED`,
   `WM_SETTINGCHANGE` (work area), sleep/resume and GPU device-removed by re-computing geometry and
   re-creating the window if needed.
9. **Fullscreen suppression:** Windows sends no full-screen start/stop notification (E-124), so the
   detector reacts to `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_LOCATIONCHANGE` (filtered to the
   foreground window) via `SetWinEventHook`, plus `SHQueryUserNotificationState` and a comparison of
   the foreground window rectangle with its monitor rectangle; it ignores the shell and our own
   windows. Re-evaluated on display changes.
10. **Taskbar awareness:** read the work area (`MONITORINFO.rcWork`) and `SHAppBarMessage` for a
    top-docked or auto-hide taskbar; the island stays inside the work area.

## 5. Component boundaries and dependency direction

```
Halo.App (WPF, composition root)
  ├── depends on → Halo.Platform.Windows (adapters)
  ├── depends on → Halo.Infrastructure (persistence, logging, HTTP)
  └── depends on → Halo.Core
Halo.Platform.Windows ──→ Halo.Core (implements its ports)
Halo.Infrastructure  ──→ Halo.Core (implements its ports)
Halo.Core            ──→ nothing Windows-specific (net10.0, no UI, no P/Invoke)
```

| Project (PROPOSED) | Responsibility | May reference |
|---|---|---|
| `Halo.Core` | Activity model, island state machine, arbitration, module contracts, domain logic (timer, tasks, indicator coalescing), settings schema, ports (interfaces) for every OS service, `TimeProvider`-based scheduling | BCL only |
| `Halo.Platform.Windows` | Adapters: media sessions, Core Audio, power/battery, network, displays/DPI, fullscreen detector, foreground events, tray icon, startup registration, single instance, hotkeys, notification listener (packaged only) | Core, CsWin32, Windows SDK projections |
| `Halo.Infrastructure` | JSON stores with migrations, file logger, `HttpClient` factory with allowlist (lyrics, Adv) | Core |
| `Halo.App` | WPF views and view models, island surface(s), settings window, DI wiring, lifecycle | All of the above |
| `Halo.Core.Tests` | Unit tests | Core |
| `Halo.Platform.Windows.Tests` | Integration tests against real OS APIs (local, tagged) | Platform, Core |
| `Halo.App.UiTests` | FlaUI + Axe.Windows UI and accessibility tests | App (as a process) |

Rules: Core never references Platform or App; adapters expose only Core types; UI never calls
P/Invoke directly. **No third-party plugin loading** in v1: the extension point is the in-process
`IActivityModule` contract, which keeps modules isolated and testable without the security and
compatibility burden of loading external code. A plugin system will only be reconsidered if a
concrete need appears after release.

## 6. Activity and state model

### 6.1 Island presentation states

| State | Meaning | Entered when | Leaves when |
|---|---|---|---|
| `Hidden` | Not shown (reason: `Fullscreen`, `UserHidden`, `NoDisplay`, `Locked`) | Suppression rule true | Rule false |
| `Minimal` | Smallest idle form (no live activity) | No active activity, minimal mode on | Activity starts / hover |
| `Compact` | Summary of the top activity | Any activity active | Hover-intent, click, alert, end |
| `Expanded` | Interactive pages for active activities | Hover-intent (default 120 ms dwell) or click (click mode) or keyboard focus | Pointer leaves + grace (default 400 ms), `Esc`, focus loss, or 8 s without interaction when opened by an alert |
| `Alert` | Transient banner (HUD or popup) layered over Minimal/Compact | Alert raised | Timeout (HUD 1.5 s after last update; popup 3.5 s) or user dismiss |

The state machine is a pure function `(state, event, now) → (state, effects)`, driven by an injected
`TimeProvider`, so every transition is unit-testable without a window.

**As built (M2, 2026-10-28; Compact/Expanded only):** `IslandStateMachine` is a pure function of
`(state, input)`. Timer expiries are inputs, and `IslandController` owns one injectable one-shot
timer, so tests run on virtual time without a `TimeProvider` package. The window implements the timer
with a Win32 timer on the UI thread. Phases: `Collapsed`, `ExpandPending`, `ExpandedByHover`,
`CollapsePending`, `ExpandedPinned`. Refinement of the table above: the "pointer leaves + grace" exit
applies only to a hover-opened island. Click, menu, tray, UIA and second-launch expansion **pin** it
until an explicit collapse, and a click on a hover-opened island pins it. Otherwise a tray "Open"
would close 400 ms later, because the pointer is at the tray.

**As built, M1 (2026-10-28):**

- **Presentations:** `Compact`, `Expanded` and `Alert`, a 300 × 44 DIP transient pill inside the expanded shape.
- **State:** `Suppressed` (full screen) and `ShowNow` (the user's override) sit beside the phase.
- **Precedence:** user override > full-screen suppression > normal. While suppressed without the override, only
  alerts marked `AllowedWhenSuppressed` (critical battery) are shown.
- **Arbitration (`IslandController`):**
  - an alert with the same key coalesces and restarts its time;
  - higher or equal priority replaces the alert on screen (the replaced one is dropped);
  - lower priority waits in a queue of at most 4, and entries older than their `StaleAfter` are dropped;
  - alerts are shown only while the island is collapsed and not pressed. While the island is hovered pending,
    expanded or pressed they wait, so a system event never interrupts the user;
  - expanding removes the alert, and alerts never pin the island.
- **Timers:** keyed (`Intent` for dwell/grace, `Alert`). A late callback for an unarmed slot is ignored.
- **Not built:** activity arbitration and pages (§6.2); the island shows the clock, plus M3 content.

### 6.2 Activities

```text
Activity { Id, ModuleId, Kind, Priority (Critical | High | Normal | Low | Ambient),
           Lifecycle (Active | Paused | Ended), StartedAt, UpdatedAt, StaleAfter?,
           Pinned, CompactModel, ExpandedModel, AccessibleSummary }
Alert    { Key (coalescing), Priority, Model, Duration, AllowedWhenSuppressed }
```

- **Compact arbitration:** pinned first, then priority, then most recent update; stale activities
  drop out.
- **Expanded pages:** one page per active activity in a stable, user-orderable order.
- **Alerts:** queued by priority; same-key alerts coalesce (repeated volume changes update one HUD
  and extend its timer); while `Hidden(Fullscreen)` only alerts marked `AllowedWhenSuppressed`
  (user-configurable, default: critical battery and due reminders) are shown.
- **Interaction lock:** while the pointer is over the expanded island or it has keyboard focus,
  arbitration does not change the visible page.

## 7. Windows API integration boundaries

| Port (Core) | Adapter implementation | Threading | Permission / identity | Main failure modes → handling |
|---|---|---|---|---|
| `IMediaSessionSource` | `GlobalSystemMediaTransportControlsSessionManager` (`RequestAsync`, `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`) | WinRT events on thread pool → marshalled to UI | Docs list capability `globalMediaControl` (spike S3 confirms packaged and unpackaged behaviour) | Manager unavailable → module `Unavailable`, retry with backoff; player rejects command → show disabled control |
| `IAudioEndpointMonitor` | Core Audio `IMMDeviceEnumerator` + `IAudioEndpointVolumeCallback` + `IMMNotificationClient` | COM MTA callbacks → queue | None | Default device change → re-register; no device → hide volume HUD |
| `IBrightnessMonitor` | Spike S5: WMI `WmiMonitorBrightnessEvent` vs power-setting notification | Event thread → queue | None | Not supported (external monitor/desktop) → feature reports `NotSupported` |
| `IPowerMonitor` | `RegisterPowerSettingNotification` (`GUID_ACDC_POWER_SOURCE`, `GUID_BATTERY_PERCENTAGE_REMAINING`, `GUID_POWER_SAVING_STATUS`, `GUID_SESSION_DISPLAY_STATUS`), `GetSystemPowerStatus` for initial state | Window messages | None | No battery → battery features hidden |
| `INetworkMonitor` | `NetworkInformation.NetworkStatusChanged` + connection profile | WinRT event | None | Flapping → debounce 2 s |
| `IDisplayTopology` | `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetDpiForMonitor`, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `SHAppBarMessage` | UI thread | None | Chosen monitor disappears → fall back to primary, remember preference |
| `IFullscreenDetector` | §4.9 | WinEvent hook on UI thread (out-of-context) | None | Misdetection → user override in tray menu |
| `IForegroundObserver` | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` | UI thread | None | — |
| `ITrayIcon` | `Shell_NotifyIconW` with `TaskbarCreated` re-registration | UI thread | None | Explorer restart → re-add |
| `IStartupRegistration` | Packaged: `StartupTask`; unpackaged: HKCU `Run` value | UI thread | Packaged: user can disable in Settings → reflect state | Disabled by policy/user → show state, no retry loop |
| `ISingleInstance` | Named mutex in `Local\` + activation message to the first instance | Startup | None | Stale mutex impossible (kernel object) |
| `IHotkeyService` | `RegisterHotKey` for an opt-in focus shortcut | UI thread | None | Conflict → report and let user rebind |
| `IKeyboardIndicatorSource` (Adv) | Spike S6; preferred hook-free; `WH_KEYBOARD_LL` only as explicit opt-in | — | Disclosure required | AV flags (E-094) → keep opt-in |
| `INotificationSource` (Adv, Cond) | `UserNotificationListener` | WinRT | **Package identity + capability + user consent** | Denied/revoked → silent empty list (documented) → show status |
| `IBluetoothAudioMonitor` (Adv) | WinRT `DeviceWatcher` (passive) | WinRT | None | Battery level unavailable → hide |
| `ILyricsProvider` (Adv, Cond) | `HttpClient` → LRCLIB; allowlisted host | Thread pool | Network (opt-in) | Offline/timeout/not found → "not available" state |
| `IShelfService` (Adv) | OLE drag-drop (`IDropTarget` / data object), shell items | UI STA thread | None | Missing file → marked missing; never auto-open |

## 8. Configuration and local data storage

- **Location:** packaged → `ApplicationData.Current.LocalFolder`; unpackaged → `%LOCALAPPDATA%\ProjectHalo\`
  (folder name changes with the public name). No roaming data.
- **Files:** `settings.json`, `tasks.json`, `timers.json`; Adv: `water.json`, `shelf.json`.
  Each has `schemaVersion`; migrations are pure functions tested with fixtures.
- **Writes:** serialise → write `*.tmp` → flush → `File.Replace` with `*.bak`. Debounced (≤ 1 write/s
  per file).
- **Corruption:** parse failure → rename to `*.corrupt-<timestamp>`, load `*.bak`, else defaults; the
  user is told once via the settings window.
- **Shelf (Adv):** stores file paths and metadata only, never file contents (matches E-103 intent).
- **Secrets:** none are stored. If a future integration needs a token, it uses Windows Credential
  Manager / `PasswordVault`, never plain files.
- **Why JSON, not SQLite (revises the Phase 1 proposal):** data volumes are tiny, `System.Text.Json`
  is in-box, files are human-inspectable for support, and it avoids a native dependency. Revisit if
  history queries (e.g., water statistics) need indexing.

## 9. Error handling, logging and performance budgets

### 9.1 Error handling

- Expected failures (OS feature unsupported, access denied, network) are values (`Result`/status
  enums) surfaced as module state: `Running`, `Unavailable(reason)`, `NotSupported`, `Disabled`.
- A **module supervisor** starts/stops modules; an exception in a module disables that module, logs
  it, and retries with backoff (max 3 restarts per 10 min), leaving the island and other modules
  running.
- Global handlers (`AppDomain.UnhandledException`, `Dispatcher.UnhandledException`,
  `TaskScheduler.UnobservedTaskException`) write a local crash record; the app does not auto-restart
  in a loop.
- COM/WinRT event subscriptions are always unregistered on module stop (no leaks across restarts).

### 9.2 Logging

- Local rolling files (1 MB × 5) in the data folder; Information level by default.
- **No personal content at Information level**: no track titles, task text, file names or
  notification text. Debug level (off by default, user-enabled, auto-expires after 24 h) may include
  them and says so in the UI.
- No log upload, no telemetry. Users can open the log folder from Settings.

### 9.3 Performance budgets (PROPOSED — not yet measured)

Target machine: this workstation (1920×1080 @ 120 Hz, 125 %, Intel UHD + RTX 3050, battery).
Measured on a Release build, after 2 min warm-up, AC power, default settings.

| ID | Metric | Target | Method |
|---|---|---|---|
| PB-01 | CPU, settled idle (no activity) | ≤ 0.5 s process CPU per 10 min | `Get-Process` CPU delta + WPR CPU sampling |
| PB-02 | Wake-ups, settled idle | ≤ 2 context switches/s into our threads, average | PDH `Thread` raw context-switch counts per overlay thread, 10 s samples; a new overlay thread during the window ⇒ INVALID; ended threads reported (PROTOTYPE_S1_S2_REPORT §8.6, v1.3). WPR CSwitch as optional cross-check |
| PB-03 | Frames presented, settled | 0 per second | DXGI Desktop Duplication updates attributed to the island region over a static backdrop, after calibration (report §8.5, §8.7). PresentMon only under report §8.14 |
| PB-04 | CPU, music playing, island compact with static indicator | ≤ 1.0 s per 10 min | as PB-01. **Measured in M3**, not S1 (owner decision 2026-10-27; needs media) |
| PB-05 | Expand/collapse frame pacing | ≤ 1 frame over 1.5× refresh interval per transition; p95 frame interval ≤ 1.5× at 60 Hz and 120 Hz over 50 transitions | Intervals between attributed Desktop Duplication updates inside each transition, first 50 valid of 60, after calibration with stall injection (report §8.6–§8.7) |
| PB-06 | Hover-intent to first animation frame | ≤ 50 ms after the dwell delay fires | Instrumented timestamps (ETW EventSource) |
| PB-07 | Volume change to HUD visible | ≤ 100 ms p95 | Test harness changes volume via Core Audio and timestamps first frame |
| PB-08 | Media metadata change to update | ≤ 300 ms p95 | Instrumented timestamps |
| PB-09 | Startup to island visible | ≤ 1.0 s warm, ≤ 2.5 s cold (p50 of 10) | ETW process start → first present |
| PB-10 | Memory, private bytes after 10 min idle | ≤ 120 MB (stretch ≤ 80 MB) | PerfView / `Get-Process PrivateMemorySize64` |
| PB-11 | Memory growth over 8 h soak with periodic activity | ≤ 2 MB/h | Scripted soak |
| PB-12 | GPU, settled | No sustained engine utilisation (≤ 0.5 % 3D engine) | Task Manager GPU counters via `Get-Counter` |
| PB-13 | Network with optional features off | 0 outbound connections | ETW TCP/IP events or `Get-NetTCPConnection` sampling |

PB-10 is deliberately looser than the reference's claimed ~30 MB (E-140): that figure is a CLAIM,
possibly affected by working-set trimming (E-027), and a managed runtime has a higher floor. If
spike S1 shows the WPF baseline above 120 MB, escalate to the owner (fallback C or native overlay).

## 10. Test plans (summary — details in `TESTING_STRATEGY.md`)

| Level | Scope | Tooling |
|---|---|---|
| Unit | State machine, arbitration, coalescing, timers (virtual time), migrations, settings validation, geometry maths (DPI/work area) | xUnit v3, `FakeTimeProvider` |
| Integration | Each adapter against the real OS on the dev machine (media session with a local player, Core Audio callbacks, power events, display enumeration, JSON store on disk) | xUnit, tagged `Integration`, run locally |
| UI | Island and settings via UIA: expand/collapse, keyboard path, tray menu, no focus stealing | FlaUI |
| Accessibility | Automated rules + manual Narrator, keyboard-only, high contrast, reduced motion, 100–175 % scaling | Axe.Windows, Accessibility Insights, Narrator |
| Performance | PB-01…PB-13 scripted | WPR/WPA, PresentMon, PowerShell harness |
| Packaging | Install, upgrade (data kept), uninstall, startup task, clean-machine run | Spare PC or VM (not available yet) |

## 11. Packaging and distribution strategy

1. **MSIX package** from the start (single-project MSIX). Package identity enables `StartupTask`,
   Windows app notifications, the notification listener (Adv) and clean uninstall.
2. **Primary public channel: Microsoft Store (MSIX).** The Store re-signs MSIX packages for free
   worldwide and avoids SmartScreen warnings (Microsoft Learn: code signing options). Requires a
   free Store developer account in the owner's name — owner decision.
3. **Direct download (optional):** requires a trusted signature. Azure Artifact Signing (~$9.99/month)
   is available to individuals only in the USA and Canada; elsewhere an OV certificate ($150–300/year)
   is needed; EV no longer bypasses SmartScreen. Unsigned public builds are not produced (unlike the
   reference, E-006, E-146).
4. **Deployment mode:** self-contained .NET 10 inside the MSIX (no separate runtime install). Size to
   be measured in spike S8; trimming is not supported for WPF and will not be relied on.
5. **Updates:** Store-managed. For a direct channel, no auto-updater in v1; releases carry SHA-256
   hashes generated by the release script (website/version drift observed in E-003 must not recur).
6. **Uninstall:** MSIX removes package data; settings offer "export data" before uninstall.
7. **Dev builds:** local sideload with a developer certificate created on the dev machine, never
   distributed; the certificate's private key stays outside the repo (`.gitignore` covers `*.pfx`).

## 12. Risks, assumptions and open questions

### 12.1 Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| R-01 | WPF transparent overlay misses PB-01/PB-05 | Medium | High | Spike S1 first; `IIslandSurface` boundary; fallback C |
| R-02 | Memory above PB-10 | Medium | Medium | Measure early; self-contained ReadyToRun; fallback native overlay |
| R-03 | GSMTC behaviour differs packaged vs unpackaged or across players | Medium | High | Spike S3; test matrix of players |
| R-04 | Full-screen misdetection (borderless games, multi-monitor) | Medium | Medium | Heuristics + user override + test matrix |
| R-05 | Focus stealing / interference with games (E-125) | Low | High | No-activate styles, UI test asserting foreground unchanged |
| R-06 | Brightness/keyboard indicators unreliable without hooks | High | Low | Advanced, opt-in, `NotSupported` states |
| R-07 | Store certification rejects an always-on-top overlay or input monitoring | Low | High | Keep input monitoring opt-in and disclosed; early test submission |
| R-08 | Lyrics licensing / LRCLIB terms unclear | High | Medium | Feature conditional on legal review; off by default |
| R-09 | Single display limits multi-monitor/mixed-DPI testing | Certain | Medium | Unit tests on simulated geometry + borrowed external monitor |
| R-10 | No VM/spare PC for clean-install tests | Certain (today) | Medium | Owner provides spare PC or approves VM before release |
| R-11 | Solo-developer bandwidth | Medium | Medium | Narrow Core scope; spikes before features |

### 12.2 Assumptions

- The owner is comfortable working in C# (inferred from the original brief's preferred stack).
- Primary target is Windows 11 on x64; Windows 10 is not targeted because it is out of mainstream
  consumer support (Microsoft lifecycle; not re-verified in this session).
- One island on one chosen monitor is sufficient for v1.
- English-only UI for v1, with all strings in resources for later localisation.

### 12.3 Open questions (need owner input — see `PROJECT_STATUS.md`)

1. Approve the .NET 10 SDK installation for Phase 4.
2. Distribution: Microsoft Store account (recommended) and/or paid certificate for direct download.
3. Include synced lyrics at all (licensing review), and keyboard indicators (input monitoring)?
4. Accept PB-10 (≤ 120 MB) given the reference's ~30 MB claim?
5. Provide an external monitor and a spare PC (or approve a VM) for Phase 4 testing.
6. Choose a project license before any public release.

## 13. Phase 4 entry spikes (decision closure)

Each spike is a throw-away prototype in `spikes/` (not shipped), time-boxed, with a written result
appended to this ADR. Spikes S1 and S2 must pass before feature work starts.

**Phase 4 status (2026-10-27):** M0 skeleton exists (`spikes/`, .NET SDK 10.0.403). S1 and S2 are
**NOT RUN**: their definitions below were not precise enough to implement (no S2 checklist; the
PB-02/03/05 methods need tools that are not installed or approved; PB-04 needs media). A detailed
protocol is **PROPOSED** in [`PROTOTYPE_S1_S2_REPORT.md`](PROTOTYPE_S1_S2_REPORT.md) §5. It proposes
built-in measurement methods for PB-02/03/05 (with a calibration step) and moving PB-04 to M3. No
budget value in §9 is changed. On approval, the §9 *Method* column is amended accordingly.

**Update 2026-10-28:** the protocol was approved and run (v1.3); results and the provisional rendering
decision are in §13.1.

| Spike | Question | Pass criteria |
|---|---|---|
| S1 | Which transparency technique (A/B/C) meets PB-01–PB-05, PB-10, PB-12 on this machine? | All listed budgets met at 60 and 120 Hz |
| S2 | Click-through outside the shape, no activation, topmost over normal windows, below fullscreen apps when suppressed | FlaUI test + manual checklist pass |
| S3 | GSMTC from a .NET 10 app, packaged and unpackaged; Spotify, a browser, Media Player | Metadata, art and commands work in all three |
| S4 | Core Audio volume callbacks from .NET (CsWin32/ComWrappers) | PB-07 met |
| S5 | Brightness change source on this laptop | Event within 200 ms for keyboard and Settings changes |
| S6 | Caps Lock / input language without a global hook | Correct state within 500 ms, or documented "not feasible" |
| S7 | Full-screen detection matrix | Correct for F11 browser, video player fullscreen, PowerPoint slideshow, borderless and exclusive games |
| S8 | Self-contained WPF in MSIX: size, startup, StartupTask, uninstall | Recorded numbers; PB-09 met |

### 13.1 S1/S2 result and rendering decision (2026-10-28)

**Decision: option C — Win32 host window + `Windows.UI.Composition` — is PROVISIONALLY SELECTED** for
the island surface and the first vertical slice. It is **not** SELECTED: the S1/S2 gate did not pass
under its own rules (PROTOTYPE_S1_S2_REPORT §8.10), because the owner stopped the matrix early and some
mandatory measurements are INVALID. WPF (technique B, then A) stays the documented fallback; it is not
built in parallel. Decision confidence: **Medium** for the rendering path.

**Evidence** (PROTOTYPE_S1_S2_REPORT §9; calibration VALID at 120 Hz):

| Criterion | A (WPF layered) | B (WPF DWM frame) | C (composition) |
|---|---|---|---|
| PB-05 frame pacing, 120 Hz, 2 sessions | **FAIL** — long frames in 50/50 transitions, up to 4 per transition, max 74.8 ms | **FAIL** — 50/50, up to 4, max 52.7 ms | PASS — 3/50 with one long frame, max 18.0 ms |
| PB-01, PB-03, PB-10, PB-12, 120 Hz | PASS (68 MB private) | PASS (70 MB) | PASS (11 MB) |
| PB-02 | INVALID | INVALID | INVALID |
| 60 Hz | INVALID (calibration) | INVALID | INVALID |
| S2 automated items | all PASS | all PASS | all PASS except S2-12 (UIA), expected |

**Why C, under §8.10 rule 2(a):** every WPF technique has a valid FAIL of a mandatory criterion (PB-05)
that C passes in the same, calibrated comparison. Rule 3 (UNRESOLVED) also applies to the gate as a
whole because PB-02 and the 60 Hz results are INVALID, so the selection is provisional rather than
final. The owner's 2026-10-28 instruction was to make a provisional decision and start building, so
the vertical slice uses C now and the outstanding items below must close before ADR-001 is approved.

**Amendments to §2 and §4 for the island surface** (other layers unchanged):

- The island window is rendered by `Windows.UI.Composition` through `ICompositorDesktopInterop` on a
  `WS_EX_NOREDIRECTIONBITMAP` window, not by WPF. Text is drawn with Direct2D/DirectWrite into
  composition drawing surfaces on a **WARP** (software) Direct3D device: a hardware device loaded the
  GPU's user-mode driver and raised private memory from about 17 MB to 62–68 MB (PROJECT_STATUS §3).
- UI Automation comes from a custom provider (`WM_GETOBJECT` → `UiaReturnRawElementProvider`), not WPF
  automation peers. This is C's known extra cost (§4.5).
- WPF is still the proposed framework for the Settings window (FR-APP-02), which is out of the slice.
- The island code calls Win32 and COM directly in `Halo.App/Interop` (internal). §5's "UI never calls
  P/Invoke" rule is relaxed for the island surface, because the surface *is* the Win32/composition
  layer; feature adapters still belong in `Halo.Platform.Windows` when they arrive.
- The tray icon (`ITrayIcon`, §7) is in `Halo.App/Island/TrayIcon.cs` for now. It is bound to the
  island window's message loop (`Shell_NotifyIconW` callbacks, `TaskbarCreated`), and
  `Halo.Platform.Windows` does not exist yet. Move it there when that project is created. Its image is
  drawn in code (`Halo.Core/Tray/TrayGlyph`), so no binary icon file is committed.

**Outstanding before the decision can become SELECTED:**

1. PB-02 for C: one WPR `CSwitch` trace over a 10-minute settled window (smallest settling measurement).
2. 60 Hz: a negative-control design that does not depend on WPF's own pacing (for example, a stall
   injected into a C-hosted, app-driven animation), then calibration and one S1-C-60 session.
3. The third S1-C-120 session.
4. Manual S2-11M (scaling 100/150/175 %) and an Axe.Windows/Narrator pass on the product island.
5. Owner decision on whether a once-a-minute clock redraw is an "activity" under PB-03.

**Risks:** composition text and UIA are hand-written interop (mitigated by desktop tests that read the
island through FlaUI and check drawn pixels); no WPF fallback is being kept in sync; device-loss
recovery for the text device is implemented but untested.
