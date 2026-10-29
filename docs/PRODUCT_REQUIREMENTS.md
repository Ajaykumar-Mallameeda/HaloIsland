# Product Requirements — Project Halo

| Field | Value |
|---|---|
| Status | **PROPOSED** (Phase 3) — awaiting owner approval |
| Date | 2026-10-27 |
| Architecture | [`ARCHITECTURE_DECISION_RECORD.md`](ARCHITECTURE_DECISION_RECORD.md) (ADR-001) |
| Reference evidence | Behavioural notes on reference products (kept private, not published) |

This document defines **our** product. Reference behaviour is cited only to explain a choice; it is
never a requirement in itself. Everything here is PROPOSED.

## 1. Product summary

An original, privacy-first Windows overlay at the top edge of the screen that shows glanceable live
activities (media, timers, tasks, system changes) in a compact island and expands on demand for
interaction — without stealing focus, without background cost when idle, and accessible by keyboard
and screen reader.

### Goals

1. Glanceable status with zero interaction cost; interaction available in one hover or keystroke.
2. Imperceptible when idle (ADR §9.3 budgets).
3. Never disrupt the user: no focus stealing, steps aside for full-screen use.
4. Accessible: Narrator, keyboard-only, high contrast, reduced motion.
5. Local-first: no account, no telemetry, network only for opt-in features.

### Non-goals (v1)

Calendar integration; reading other apps' notifications in unpackaged builds; third-party plugins;
cloud sync; macOS/Linux; multiple simultaneous islands across monitors; mascot/character features
modelled on the reference.

## 2. Milestone scope

| Milestone | Contents |
|---|---|
| **Core (v1.0)** | Island shell (FR-ISL), media (FR-MED), volume HUD, battery, network (FR-SYS-01…04), timer (FR-TIM), basic to-do with reminders (FR-TSK), tray/settings/persistence/startup (FR-APP), accessibility (FR-A11Y) |
| **Advanced** | Brightness HUD (FR-SYS-05, if spike S5 passes), keyboard indicators (FR-SYS-06/07, opt-in), Bluetooth audio (FR-SYS-08), water tracker (FR-WAT), file shelf (FR-SHF), lyrics (FR-LYR, conditional), notification mirroring (FR-NTF, packaged + consent), volume slider |
| **Release readiness** | MSIX/Store packaging, signing, accessibility verification report, performance report, privacy policy, third-party notices |

## 3. Functional requirements

Each table lists: requirement · Windows APIs · permissions/identity · dependencies · failure modes ·
tests (U = unit, I = integration, UI = UI automation, M = manual, P = performance).

### 3.1 Island shell (FR-ISL)

| ID | Requirement | APIs | Perm. | Deps | Failure modes → behaviour | Tests |
|---|---|---|---|---|---|---|
| FR-ISL-01 | Show the island centred at the top of the chosen monitor's work area, inside the work area | `EnumDisplayMonitors`, `GetMonitorInfoW`, `GetDpiForMonitor`, `SHAppBarMessage` | — | — | Monitor removed → primary; top taskbar → below it | U geometry, I, M |
| FR-ISL-02 | Presentations: Minimal, Compact, Expanded, Alert, Hidden (ADR §6.1) | — (Core) | — | — | — | U state machine |
| FR-ISL-03 | Expand on hover-intent (default 120 ms dwell, 0–500 ms configurable); collapse after leave grace (default 400 ms) | `TrackMouseEvent` / WPF mouse events | — | — | Pointer jitter at edge → hysteresis | U timings, UI |
| FR-ISL-04 | Click-to-open mode as an alternative | — | — | — | — | UI |
| FR-ISL-05 | Only the visible shape receives input; everything else passes through | Layered alpha or `SetWindowRgn` | — | Spike S1/S2 | Region stale during animation → union of current and target | UI, M |
| FR-ISL-06 | Never take foreground activation from pointer interaction; never minimise or disturb full-screen apps | `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` | — | — | — | UI asserts foreground window unchanged |
| FR-ISL-07 | Opt-in global shortcut moves keyboard focus to the island; `Esc` returns focus to the previous window | `RegisterHotKey`, `SetForegroundWindow` (user-initiated only) | — | — | Shortcut conflict → report, rebind | UI, M |
| FR-ISL-08 | Page switching by page dots, mouse wheel, Left/Right keys | — | — | — | — | UI |
| FR-ISL-09 | Hide automatically when a full-screen app is in the foreground on the island's monitor; user can disable | `SetWinEventHook`, `SHQueryUserNotificationState`, `MonitorFromWindow` | — | Spike S7 | Misdetection → tray "Show island now" override | U heuristics, I, M matrix |
| FR-ISL-10 | Size presets (Small/Default/Large) applied live; auto-scale with DPI | `WM_DPICHANGED` | — | — | — | U, M at 100–175 % |
| FR-ISL-11 | Correct after DPI change, resolution change, monitor hot-plug, sleep/resume, Explorer restart | `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_POWERBROADCAST`, `TaskbarCreated` | — | — | GPU device lost → recreate surface | I, M |
| FR-ISL-12 | No rendering while settled, hidden, display off or session locked | `GUID_SESSION_DISPLAY_STATUS`, `WTSRegisterSessionNotification` | — | — | — | P (PB-03) |
| FR-ISL-13 | Respect system reduced motion (cross-fade/instant transitions) and an in-app motion setting | `SystemParametersInfo(SPI_GETCLIENTAREAANIMATION)`, `UISettings.AnimationsEnabled` | — | — | — | U, UI |

### 3.2 Media (FR-MED)

| ID | Requirement | APIs | Perm. | Deps | Failure modes → behaviour | Tests |
|---|---|---|---|---|---|---|
| FR-MED-01 | Show current session: title, artist, art, play state, progress | GSMTC `RequestAsync`, `GetCurrentSession`, `TryGetMediaPropertiesAsync`, `GetTimelineProperties`, `GetPlaybackInfo`, events | Docs list `globalMediaControl` capability — spike S3 | — | Manager fails → `Unavailable` + retry; no session → activity ends | U mapping, I with local player |
| FR-MED-02 | Play/pause, next, previous, seek — only when the session reports the control as enabled | `TryTogglePlayPauseAsync`, `TrySkipNext/PreviousAsync`, `TryChangePlaybackPositionAsync` | as above | — | Command returns false → transient "not supported" | I, UI |
| FR-MED-03 | Switch between multiple sessions | `GetSessions`, `SessionsChanged` | as above | — | Session vanishes mid-switch → fall back to current | U, I |
| FR-MED-04 | Album art decoded off the UI thread, cached per session, size-capped | `Thumbnail.OpenReadAsync`, WPF `BitmapDecoder` | — | — | Corrupt/huge image → placeholder | U, I |
| FR-MED-05 | Progress updates without a running timer when paused; when playing, at ≤ 1 Hz while compact | `TimelinePropertiesChanged` + interpolation | — | — | Player reports bad timeline → hide progress | U, P (PB-04) |
| FR-MED-06 | Optional animated "now playing" indicator, off by default unless it meets PB-04 | — | — | — | — | P |
| FR-MED-07 (Adv) | Volume slider for the default endpoint | `IAudioEndpointVolume::SetMasterVolumeLevelScalar` | — | — | Device change mid-drag → re-acquire | I, UI |

### 3.3 System indicators (FR-SYS)

| ID | Requirement | APIs | Perm. | Deps | Failure modes → behaviour | Tests |
|---|---|---|---|---|---|---|
| FR-SYS-01 | Volume/mute HUD on any change source (keys, mixer, apps), coalesced | `IAudioEndpointVolumeCallback`, `IMMNotificationClient` | — | Spike S4 | No device → no HUD; callback storm → coalesce | U coalescing, I, P (PB-07) |
| FR-SYS-02 | Charger connected/disconnected alert | `GUID_ACDC_POWER_SOURCE` | — | — | Desktop (no battery) → disabled | I (plug/unplug), U |
| FR-SYS-03 | Low (20 %) and critical (10 %) battery alerts, thresholds configurable; "fully charged" optional | `GUID_BATTERY_PERCENTAGE_REMAINING`, `GetSystemPowerStatus` | — | — | Unknown capacity (255) → no alert | U thresholds, M |
| FR-SYS-04 | Network lost/restored/limited alert, debounced 2 s | `NetworkInformation.NetworkStatusChanged`, `GetInternetConnectionProfile` | — | — | Flapping → debounce | U, M |
| FR-SYS-05 (Adv) | Brightness HUD on internal panels | Spike S5 (WMI `WmiMonitorBrightnessEvent` or power-setting GUID) | — | Spike S5 | External monitor → `NotSupported` documented | I, M |
| FR-SYS-06 (Adv, opt-in) | Caps Lock indicator | Spike S6 (hook-free preferred) | Input-monitoring disclosure | Spike S6 | Not feasible hook-free → LL hook only with explicit consent | M, security review |
| FR-SYS-07 (Adv, opt-in) | Input-language change indicator | Spike S6 (`GetKeyboardLayout` of foreground thread on foreground/change events) | as above | Spike S6 | — | M |
| FR-SYS-08 (Adv) | Bluetooth audio device connected, battery when exposed | WinRT `DeviceWatcher`, `BluetoothDevice` | — | — | No battery property → name only; must not disturb other devices (cf. E-098) | I with headset, M |

### 3.3a As built: media and system indicators (M3, 2026-10-29)

Requirements are unchanged. This table records what exists; evidence is in PROJECT_STATUS §8.

| ID | State | Notes |
|---|---|---|
| FR-MED-01 | Built | Title, artist, art, play state and progress; the island falls back to the clock when the session ends |
| FR-MED-02 | Built **except seek** | Previous, play/pause and next, each only when the session enables it. Seek was left out: GSMTC position changes are unreliable across players |
| FR-MED-03 | Partly built | Follows Windows' current session (else the playing one, else the first). Switching between sessions by hand is **not built** |
| FR-MED-04 | Built | Art from the session's own thumbnail only: 4 MB cap, 96 px decode off the UI thread, 2 s timeout, placeholder otherwise. Decoded with `Windows.Graphics.Imaging`, not WPF |
| FR-MED-05 | Built | Progress interpolated; 1 Hz redraw only while the open card is visible and playing; none when paused |
| FR-MED-06 | Not built | Optional animated indicator (needs PB-04 first) |
| FR-MED-07 (Adv) | Built | Volume row in the open island (mute button and slider) |
| FR-SYS-01 | Built | Coalesced volume pill for changes made elsewhere; no device → "No audio output" and no pill. PB-07 **not measured** (a state-change smoke time only) |
| FR-SYS-02 | Built | Charger pill; machines without a battery show nothing. Verified with synthetic readings |
| FR-SYS-03 | Built | Low 20 % / critical 10 %, configurable in the settings file; once per discharge; unknown level → no alert. "Fully charged" is not built |
| FR-SYS-04 | Built | Lost / limited / restored pill after 2 s of quiet; startup never alerts |
| FR-SYS-05…08 | Not built | Advanced; FR-SYS-06/07 deferred for privacy (PROJECT_STATUS §8, O-F02) |
| FR-NTF-01 | Not built | Blocked: needs package identity (O-F01) |

### 3.3b As built: expansion campaign (2026-10-29)

Requirements are unchanged. Evidence for each row is in PROJECT_STATUS §9.

| ID | State | Notes |
|---|---|---|
| FR-ISL-07 | Partly built | Opt-in shortcut (Off by default) toggles the island. Moving keyboard focus into the island is not built (non-activating by design) |
| FR-ISL-08 | Built | Activity, now-playing and clock pages; page dots (24 DIP) and mouse wheel |
| FR-ISL-13 / FR-A11Y-05 | Built | Shared motion system (MOTION_SYSTEM.md) with reduced-motion alternatives |
| FR-MED-02 | Built **with seek** | Seek only when the session allows it |
| FR-MED-03 | Built | Manual session switching |
| FR-TIM-01/02/03 | Built | Countdown and focus sessions (Pomodoro preset); restart and sleep safe; completion alert in the island (no sound) |
| FR-TSK-01/02 | Built | Tasks window; due reminders with snooze (10 min), done and dismiss; missed reminders shown at the next start |
| FR-TSK-03 | Not built | Needs package identity (L-1) |
| FR-APP-01 | Built except Start with Windows | Tray: Timer, Focus session, Do Not Disturb (the "pause alerts" requirement), Tasks, Shelf |
| FR-A11Y-01/02 | Built | UIA tree with content and controls; notifications delivered to clients (Narrator by ear not run) |
| FR-A11Y-04 | Partly built | High contrast uses system colours. A manual review of all four HC themes was not run |
| FR-SHF-01 | Built except drag | Explicit pin from the clipboard; references only; missing flagged; never opens anything |
| FR-NTF-01 | Not built | BLOCKED (package identity) |

### 3.4 Timer (FR-TIM) and tasks (FR-TSK)

| ID | Requirement | APIs | Perm. | Deps | Failure modes → behaviour | Tests |
|---|---|---|---|---|---|---|
| FR-TIM-01 | Countdown timer and stopwatch; Pomodoro preset (durations configurable) | `TimeProvider`; `CreateWaitableTimerEx` for wake-accurate expiry | — | — | Sleep across expiry → fire on resume as "missed" | U virtual time, I sleep/resume |
| FR-TIM-02 | Timer survives app restart (persisted end time, not remaining time) | — | — | JSON store | Clock change → recompute from UTC | U |
| FR-TIM-03 | Completion alert in-island; optional sound respecting quiet hours | `PlaySound`/`MediaPlayer`, `SHQueryUserNotificationState` | — | — | Muted → visual only | U, M |
| FR-TSK-01 | Add, edit, complete, delete tasks; optional due time | — | — | JSON store | Corrupt file → recovery (ADR §8) | U, UI |
| FR-TSK-02 | Due reminder in-island with snooze (5/10/15 min) | Scheduler on `TimeProvider` | — | — | Missed while off → shown on next start | U, I |
| FR-TSK-03 | Optional Windows app notification for due tasks (packaged only) | `AppNotificationManager` | Package identity | MSIX | Notifications disabled → in-island only | I, M |

### 3.5 App shell (FR-APP)

| ID | Requirement | APIs | Perm. | Deps | Failure modes → behaviour | Tests |
|---|---|---|---|---|---|---|
| FR-APP-01 | Tray icon with menu: Show/Hide island, Settings, Start with Windows, Pause alerts 1 h, Exit | `Shell_NotifyIconW`, `TaskbarCreated` | — | — | Explorer restart → re-add | I, UI |
| FR-APP-02 | Settings window (keyboard accessible), changes apply live | WPF | — | — | Invalid value → validation message | UI, A11Y |
| FR-APP-03 | Start with Windows toggle | `StartupTask` (packaged) / HKCU `Run` (unpackaged dev) | User may disable in Windows Settings | — | `DisabledByUser`/`DisabledByPolicy` → show state | I |
| FR-APP-04 | Single instance; second launch opens Settings | Named mutex + IPC message | — | — | — | I |
| FR-APP-05 | Persistence with schema versions, atomic writes, corruption recovery | `File.Replace` | — | — | ADR §8 | U fixtures, I |
| FR-APP-06 | Data export (JSON zip) and "delete all my data" | — | — | — | — | I |
| FR-APP-07 | Local logs with privacy rules; "open log folder" | — | — | — | Disk full → drop logs silently | U redaction |
| FR-APP-08 | Module status page: each feature shows Running / Unavailable(reason) / NotSupported / Disabled | — | — | — | — | U, UI |

### 3.6 Accessibility (FR-A11Y)

| ID | Requirement | Tests |
|---|---|---|
| FR-A11Y-01 | Every island element exposes UIA name, role and state; the compact island exposes a live summary (e.g., "Playing: <title> by <artist>") | Axe.Windows, FlaUI, Narrator script |
| FR-A11Y-02 | Alerts are announced via UIA notification events (`RaiseNotificationEvent`) with sensible politeness; announcements can be turned off | Narrator, FlaUI event listener |
| FR-A11Y-03 | Full keyboard path: focus shortcut, Tab/Shift+Tab, arrows for pages, Enter/Space to activate, Esc to leave | UI |
| FR-A11Y-04 | High contrast: system colours used in HC themes; no information by colour alone | M in all HC themes, Axe.Windows |
| FR-A11Y-05 | Reduced motion honoured (FR-ISL-13) | U, UI |
| FR-A11Y-06 | Text scaling (Settings › Accessibility › Text size) honoured in Settings window; island text scales within its size preset | M |
| FR-A11Y-07 | Minimum contrast 4.5:1 for text; touch/pointer targets ≥ 24×24 DIP in expanded state | Axe.Windows, design review |

### 3.7 Advanced features

| ID | Requirement | APIs | Perm. | Deps | Failure modes → behaviour | Tests |
|---|---|---|---|---|---|---|
| FR-WAT-01 | Water tracker: goal, container size, active hours, reminders, daily history | Scheduler | — | — | — | U |
| FR-SHF-01 | File shelf: drag files/text/URLs in; drag out; stores references only; missing files flagged; never opens anything automatically | OLE `RegisterDragDrop`/`DoDragDrop`, `IDataObject`, shell items | — | — | File moved/deleted → "missing"; huge drops → cap item count | U, I, UI |
| FR-LYR-01 (Cond) | Synced lyrics for the current track, opt-in, off by default; disclose fields sent | `HttpClient` (HTTPS, allowlist `lrclib.net`) | Network | LRCLIB; **owner approval + licensing review** | Offline/timeout(5 s)/not found → "not available"; malformed LRC → plain text | U parser, I with recorded fixtures |
| FR-NTF-01 (Cond) | Mirror selected app notifications in the island | `UserNotificationListener` | **Package identity + capability + user consent** | MSIX | Denied/revoked → status message (API fails silently) | I packaged, M |

## 4. Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-01 | Performance budgets PB-01…PB-13 (ADR §9.3), measured before each release |
| NFR-02 | Supported OS: Windows 11 x64 versions in Microsoft servicing at release time. ARM64: not in v1 |
| NFR-03 | No administrator rights for install or use |
| NFR-04 | No telemetry, no account, no network unless an opt-in feature is enabled (PB-13) |
| NFR-05 | A failing module never takes down the island (ADR §9.1) |
| NFR-06 | All user-visible strings in resources; English v1 |
| NFR-07 | Data formats versioned and migrated forward; downgrades keep a backup |
| NFR-08 | Dependencies: permissive licences only (MIT/Apache-2.0/BSD) unless the owner approves otherwise |

## 5. Acceptance criteria for v1.0 (Core)

1. All Core FRs implemented with their listed tests passing (unit + integration on the dev machine).
2. PB-01…PB-13 measured on the target machine and recorded; any miss has an owner-approved waiver.
3. Accessibility: zero Axe.Windows errors; Narrator script and keyboard-only script pass; all four
   high-contrast themes reviewed.
4. No focus stealing in the UI test suite (foreground window unchanged across 100 hover/expand cycles).
5. Full-screen matrix (spike S7) passes on this machine.
6. MSIX install, upgrade with data retained, uninstall, startup task verified on a clean machine.
7. Privacy review complete (`SECURITY_AND_PRIVACY_DESIGN.md` checklist).
