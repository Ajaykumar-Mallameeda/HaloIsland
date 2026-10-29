# Project Halo — Product Expansion Campaign Report

| Field | Value |
|---|---|
| Date | 2026-10-29 |
| Repository | `C:\dev\Notch\advanced-windows-island` (canonical). The OneDrive copy was not touched: clean, still at `9f7d5fb` |
| Starting point | `4f26bc9` (M3 gate), branch `main`, clean tree, no remotes |
| End point | the last commit in §14 (`docs: record the post-gate features and the final numbers`), clean tree, no remotes, **nothing pushed** |
| Machine | 1920×1080 @ 120 Hz, 125 %, one display, Windows 11 (build 26300) |
| Detail | Per-feature records: [`PROJECT_STATUS.md`](PROJECT_STATUS.md) §9 · research: [`RESEARCH_EXPANSION_CAMPAIGN.md`](RESEARCH_EXPANSION_CAMPAIGN.md) · motion: [`MOTION_SYSTEM.md`](MOTION_SYSTEM.md) · packaging: [`PACKAGING_FEASIBILITY.md`](PACKAGING_FEASIBILITY.md) |

## Summary

- **Accessibility:** screen-reader announcements now work. The root cause of the old M2-F05 defect was one wrong
  constant (§3).
- **Built:** a shared motion system, an activity model, and these working features:
  - a focus timer, focus sessions, and Do Not Disturb;
  - a notification framework, local reminders, and a to-do list;
  - Outlook mail and calendar providers, behind test doubles;
  - a keyboard shortcut;
  - media session switching and seeking;
  - light, dark and system themes, with pill content choices;
  - a clipboard and file shelf;
  - activity animations.
- **Tests:** they went from 319 unit and 52 desktop tests to **480 unit** and **84 desktop** tests,
  all passing at the final gate.
- **Blocked:** live Outlook use (needs the owner's app registration and sign-in), MSIX packaging and
  notification mirroring (owner decision), and the microphone indicator (no documented API).
- **Not done:** a Narrator check by ear (needs a person).

## Added after the first gate ("continue")

| Feature | Commit | State |
|---|---|---|
| I-F04 Horizontal position (primary display) | `9631a75` | PASS |
| J-F04 Bluetooth audio connect/disconnect pills (no battery) | `2de698c` | PASS (real bus read; transitions synthetic) |
| H-F02 Shelf drag-out (copy or link, never move) | `a4b03d7` | PASS (real drag) |
| K / C-F08 Camera-in-use indicator (off by default) | `53046ef` | PASS (real passive monitor; transitions synthetic) |
| J-F03 Brightness | — | Re-checked: no documented power-setting notification for brightness; still needs WMI (a new dependency). NOT BUILT |

**Second gate on the final HEAD:**
- locked restore OK;
- clean `--no-incremental` Release build: 0 warnings, 0 errors;
- unit **480/480**;
- island desktop **84/84**;
- spike desktop **5/5**;
- smoke `APP-SMOKE-120-130250` valid.

## 1. Starting state

- `4f26bc9` was HEAD on `main`, the working tree was clean, and there were no remotes.
- Baseline, rerun at the start: locked restore OK, Release build with 0 warnings, unit 319/319, island desktop 52/52.

## 2. Research and the decisions taken from it

Primary sources only. The source table is in RESEARCH_EXPANSION_CAMPAIGN §1.

| Source | Decision |
|---|---|
| Apple HIG, Live Activities (compact, minimal, expanded; animations "maximum duration of two seconds"; update only on new content; alert only when essential; rotate events in one activity; avoid sensitive information) | One primary activity, plus "+N" for the others. All motions ≤ 450 ms. Coalescing everywhere. Private text is redacted by default. Halo's own motion vocabulary; no iOS animations are copied |
| Microsoft, `UiaRaiseNotificationEvent` remarks (WS_POPUP windows need the Window pattern) and UIAutomationCore.h `ProviderOptions` | Window pattern added. The real fault was the ProviderOptions constant (§3) |
| Microsoft, App notifications quickstart (Windows App SDK; COM activator; not for elevated apps) | No toast path was added: Halo's own island is the surface. Recorded as owner decision L-1 |
| Microsoft Graph permissions reference (`Mail.ReadBasic`, `Calendars.ReadBasic`: no body, preview or attachments; delegated; no admin consent) | Least-privilege read-only Graph adapter |
| Microsoft, `MFCreateSensorActivityMonitor` (camera sensor activity) | Camera indicator is possible but held for owner consent (K-1). No documented microphone API, so the microphone is BLOCKED |

## 3. Accessibility (A-F01) — fixed

- **Root cause:** Halo declared `ProviderOptions.ServerSideProvider = 0x1`, which is **ClientSideProvider** in
  UIAutomationCore.h. UIA dropped every event the island raised, although each call returned `S_OK`.
- **How it was found:** by bisection with a real out-of-process UIA client.
  - Calculator, WinForms and WPF all delivered notifications.
  - A plain Win32 window using Halo's provider delivered none.
  - In the same process, WPF's provider worked and Halo's declarations did not.
  - Changing the one value made Halo deliver 12 of 12.
- **Second fix:** UIA calls providers on its own threads. The provider now reads an immutable snapshot and posts
  commands to the UI thread.
- **New UIA tree:**
  - the island is a fragment root (button, Invoke, ExpandCollapse, Window pattern);
  - its children are the time, date and day progress, network and battery, the media summary and track position
    (a slider when the track can be seeked), the media buttons, volume mute and slider, the activity with its
    action buttons, the page buttons, and a live-region status element for alerts;
  - an element that has gone reports `UIA_E_ELEMENTNOTAVAILABLE`;
  - nothing is focusable and nothing takes focus;
  - clock ticks raise no notifications.
- **Automated evidence (desktop, real UIA3 client):**
  - notifications are received, critical ones as ImportantMostRecent;
  - expand/collapse and structure events are received;
  - roles and values are right, and the slider equals the real endpoint volume;
  - "Timer finished" and "Reminder" are received.
- **Manual verification:** **Narrator by ear: NOT RUN** (needs a person). Axe.Windows and a high-contrast review:
  not run.

## 4. Animation engine and activity animations

- **Motion system:** `MOTION_SYSTEM.md` defines nine motions and their reduced-motion alternatives.
  `Halo.Core/Motion` has a pure planner and a tracker that keeps one plan per channel and ignores stale
  completions. Durations are times, not frame counts.
- **Activity model:** `ActivityBoard` keeps one primary activity, chosen by explicit choice, then priority, then
  recency. It coalesces equal updates, holds at most 8, expires activities, and filters for quiet mode and full
  screen.
- **Pages (FR-ISL-08):** the open island has an activity page, a now-playing page and a clock page, with 24 DIP
  page dots and mouse-wheel paging.
- **Animations:**
  - event arrival, and Swap when one alert replaces another;
  - timer: pill, card, Emphasis in the last 10 s, Arrival on completion;
  - battery and network: the status cluster swaps;
  - volume: the slider glides (Level);
  - media: a swap on a track or session change;
  - notification arrival and dismissal.
- **Not measured:** PB-05 frame pacing for the new motions.

## 5. Notification framework and the Windows notification findings

- **`NotificationCenter`:**
  - deduplicates by provider and source ID, with bounded memory;
  - applies per-provider enable, preview, minimum-priority, full-screen and quiet-mode policy;
  - rate-limits arrivals (6 a minute) into the island without alerts;
  - handles expiry and dismissal;
  - routes actions to the provider, and never reports a rejected action as done.
- **Windows findings:**
  - Windows App SDK toasts could work unpackaged, but need a new runtime dependency and a COM activator, so they
    were not added (owner decision L-1);
  - reading other apps' notifications needs package identity, so it stays **BLOCKED** (O-F01).
- **Settings:** "Show due reminders and tasks", and "Show private text" (off by default).

## 6. Email and calendar

- **Built and unit-tested against Graph-shaped fixtures** through a fake HTTP handler, with no network:
  - vendor-neutral provider interfaces and a read-only Graph adapter. It uses delegated `User.Read Mail.ReadBasic
    Calendars.ReadBasic offline_access`, GET only with `$select`, a 10 s timeout and a 1 MB cap, and maps 401, 403,
    429 and 5xx to statuses;
  - `MailWatcher`: baseline on connect (no flood), each new message once, bounded polling with backoff, no retry
    loop when signed out;
  - `CalendarWatcher`:
    - the next-meeting glance;
    - one reminder per start time, following moves and cancellations;
    - private subjects never shown;
    - join and open links only over HTTPS to Outlook or Teams hosts.
- **Live use: BLOCKED.**
  - It needs an Entra app registration (a public client, no secret) and the owner's interactive sign-in.
  - No sign-in code that could not be verified was shipped, and nothing touches the network.
- **Other providers (E-F04):** extension points only; no second live provider.

## 7. Productivity

- **Focus timer (G-F01):**
  - runs on monotonic time, so wall-clock changes don't affect it;
  - survives restart through its wall-clock end; one that ended while closed restores as "missed";
  - survives sleep through a snapshot taken at suspend;
  - wakes once a second only while visible, and otherwise only at completion;
  - controls: pause, resume, reset, +1 min, cancel, snooze and dismiss.
- **Focus sessions (G-F02):** work, short break and long break on the same engine, with pause, skip and stop.
- **Do Not Disturb (G-F03):**
  - for 1 hour, until tomorrow (DST-aware) or until turned off, and persisted;
  - Windows' own setting is never changed;
  - precedence: opening the island first, then full screen, then quiet mode;
  - critical alerts and user-started completions still show;
  - the rest are counted, then shown as one summary.
- **Local reminders and to-do list (F-F04/F-F05):**
  - due times, daily and weekly recurrence at the same local time across DST, snooze, dismiss and complete;
  - reminders missed while Halo was closed show once at start;
  - a native tasks window;
  - text is private by default and never logged.

## 8. Customisation

- **Themes (I-F01):** dark (the default), light and match-Windows (`UISettings`, live). An optional accent for
  bars is kept at 3:1 against the fill. High contrast uses the system colours. Every text colour meets 4.5:1, which
  is tested.
- **Pill content (I-F02):** activity, media, date and battery %. The left slot follows a fixed priority and never
  rotates.
- **Keyboard shortcut (I-F05):**
  - off by default; Ctrl+Alt+H, Ctrl+Alt+Space or Ctrl+Shift+H;
  - registered with `RegisterHotKey`: no keyboard hook, no keystrokes observed;
  - a conflict is detected and shown.
- **Partial or not built:**
  - per-activity customisation is partial (I-F03): sources on or off and previews, but no free priority or
    duration settings, because those would bypass the safety rules;
  - **position (I-F04, added after the first gate):** a horizontal position slider for the primary display,
    clamped to the work area. Choosing a display is not built, because it needs a second monitor.

## 9. Advanced media and hardware

| Item | State |
|---|---|
| J-F01 Manual session switching | **PASS**: two real sessions (test MediaPlayer + one hosted by Windows PowerShell), switched through UIA |
| J-F02 Seeking | **PASS**: only when the session allows it. A real UIA seek to 50 % moved the player's position |
| J-F03 Brightness | **NOT BUILT**: needs `System.Management` (a new dependency) or hand-written WMI COM |
| J-F04 Bluetooth headphones | **PASS, connection only** (added after the first gate): "Connected/Disconnected · name" pills from the endpoint's documented device properties. The real bus was read; the transitions were synthetic. **Battery never shown**: no documented API |
| K / C-F08 Camera and microphone | Camera (added after the first gate): **PASS, off by default**. Media Foundation's passive sensor activity reports; only "in use or not" is kept. The real monitor was started; transitions were synthetic; no camera was opened. Turning it on is the owner's choice (K-1). Microphone: **BLOCKED**, no documented API |
| H-F02 Shelf | **PASS**: pin on request only, copy back to the real clipboard, file references, "missing" flag, clear-all deletes the file. **Drag-out** (added after the first gate): copy or link only, never move. A real drag into a drop target delivered the file, and the original stayed |

## 10. Tests

| Suite | Start | End |
|---|---|---|
| Unit (Core + App + spikes) | 319 | **480** passed, 0 failed |
| Island desktop (`Category=Integration`) | 52 | **84** passed, 0 failed |
| Spike desktop | 5 | **5/5** |
| Locked restore / clean Release build | OK / 0 warnings | locked restore OK / `--no-incremental` Release build 0 warnings, 0 errors |

**Tests added, by feature:**

| Feature | Unit | Desktop |
|---|---|---|
| A-F01 accessibility | 19 | 7 |
| B motion and activities | 22 | — |
| G timer, focus, quiet | 40 | 7 |
| D/F notifications, reminders, tasks | 17 | 4 |
| E/F accounts | 11 | — |
| I-F05 shortcut | 3 | 3 |
| J media | ≈11 | 2 |
| I-F01/I-F02 themes and pill | 17 | 3 |
| H shelf (pin, copy, drag-out) | 5 | 2 |
| I-F04 position (after the first gate) | 5 | 1 |
| J-F04 Bluetooth audio (after the first gate) | 11 | 1 |
| K camera indicator, message IDs (after the first gate) | 5 | 2 |

**Intermittents (audit trail):**
- One tasks-window failure. It was first blamed on a stale window handle, which was wrong. The failing call is
  `AllowSetForegroundWindow`: a tray-menu test run alone lacks the foreground rights that earlier tests give it in
  the full suite. This is a harness limitation, not a Halo defect, and full suites pass.
- The historical media-test failure from M3 did not recur in this campaign's runs.

## 11. Real versus simulated coverage

| Area | Real Windows | Simulated or synthetic |
|---|---|---|
| UIA events | Received by a real UIA3 client in another process | — |
| Timer, focus | Real process, real exit and restart, real tray menu | Wall-clock jumps, sleep and resume, and DST, on a manual clock (unit) |
| Reminders and tasks | Real tasks window, real `tasks.json`, a missed reminder at start | DST recurrence (unit) |
| Media | Two real GSMTC sessions; a real seek | Several apps with odd names (unit) |
| Clipboard shelf | The real clipboard (text and file drop list) | — |
| Shortcut | A real `SendInput` chord; a real conflict | — |
| Themes | Captured pixels of the light theme | High contrast (unit only) |
| Outlook | — | **All of it** (fake HTTP, fixtures) |
| Battery, network | Start-up readings | Transitions (synthetic, as in M3) |
| Bluetooth audio | The real endpoint's bus (`intelaudio`) read from its property store | Connect and disconnect (no headset available) |
| Camera indicator | The real Media Foundation monitor started (passive) | In-use transitions (a camera is never opened) |
| Shelf drag-out | A real `SendInput` drag into a WPF drop target (copy; the original stays) | — |

## 12. Performance

`Halo.Spikes.Harness app-check` runs at 120 Hz; each is valid by the harness's own checks:

| Measurement | M3 gate (`…-013355`) | First gate (`…-034653`) | Final (`APP-SMOKE-120-130250`) |
|---|---|---|---|
| Start → first frame, p50 of 5 | 230.5 ms | 266.0 ms | **277.1 ms** (it now also loads `state.json`, `tasks.json` and `shelf.json` and reads the theme) |
| Idle 90 s: CPU / attributed frames | 15.6 ms / 160 (baseline failed; not attributable) | 15.6 ms / 1 | **31.3 ms / 1** (two scheduler ticks; the one frame is the minute clock) |
| Private memory / working set | 19.3 / 73.6 MB | 20.1 / 77.5 MB | **20.4 / 77.7 MB** |
| Transitions (20, UIA-driven) | 17/20 valid; 1 long frame; p95 8.9 ms | 20/20; 1 long; p95 8.9 ms | **20/20 valid**; 1 with one long frame; p95 8.8 ms; max 16.5 ms |
| Exit p50; Halo processes left | 75.7 ms; 0 | 76.2 ms; 0 | **73.2 ms; 0** |

- **Budgets:** no budget was changed, and these are **SMOKE observations, not verdicts**.
- **Not measured** (still open): PB-02, the PB-03 interpretation (owner decision E2), formal PB-05 (including the
  new motions), 60 Hz, multi-monitor and mixed DPI, PB-04 with music, and PB-07 as a frame time.

## 13. Privacy, authentication and dependencies

- **Dependencies:** **none added.** There are no new NuGet packages, the Windows App SDK was not installed, and no
  cloud resources were created.
- **Network:** the default build still makes no network connections. The Graph code is unreachable without a
  token source.
- **New local files, all in `%LOCALAPPDATA%\ProjectHalo` and versioned JSON written atomically:**
  - `state.json`: timer, focus session and quiet-mode schedule; no user text;
  - `tasks.json`: the user's tasks;
  - `shelf.json`: pinned items.
- **Logs:** they carry tokens and counts only. Desktop tests checked that task text, shelf text, file names and
  media titles never reach `halo.log`.
- **Never added:**
  - clipboard listeners or keyboard hooks;
  - any camera or microphone **access**. The optional, off-by-default camera indicator only reads Windows'
    activity reports and never opens a camera;
  - changes to Windows settings;
  - telemetry;
  - automatic sending, deleting, accepting or modifying of anything.
- **Secret scan:** no secrets or binaries are tracked.

## 14. Commits (local only, in order)

| Commit | Message |
|---|---|
| `8c2d5f9` | docs: add expansion campaign research on platform APIs and interaction patterns |
| `fd8b926` | fix: deliver UI Automation events and expose the island's content tree |
| `9dbd89f` | feat: add the shared motion engine and the Halo motion system |
| `804fb91` | feat: add the activity model with one primary activity per island |
| `950b89c` | feat: add the focus timer, focus sessions and Do Not Disturb |
| `e7dedea` | feat: add the notification framework, local reminders and a to-do list |
| `ae91a3e` | feat: add read-only Outlook mail and calendar providers behind test doubles |
| `265aeb9` | feat: add an optional shortcut that opens or closes the island |
| `d789044` | feat: switch between media sessions and seek where the session allows |
| `f20c7bb` | feat: add light, dark and system themes and pill content choices |
| `92ca359` | feat: animate media, volume, battery and network changes |
| `74cd792` | feat: add a user-controlled clipboard and file shelf |
| `5ed31aa` | docs: record the expansion campaign report and final gate |
| `9631a75` | feat: let the user move the island along the top of the screen |
| `2de698c` | feat: show when Bluetooth audio connects or disconnects |
| `a4b03d7` | feat: drag pinned files out of the shelf as copies |
| `53046ef` | feat: add an opt-in camera-in-use indicator from Windows' activity reports |
| (this update) | `docs: record the post-gate features and the final numbers` |

## 15. Remaining blockers and owner decisions

| # | Decision or blocker | Unblocks |
|---|---|---|
| — | A person runs a Narrator check by ear (~10 min) | A11y sign-off |
| E1–E3 | Earlier owner decisions (PROJECT_STATUS §5) | ADR approval, PB-03 wording, outstanding perf runs |
| L-1 | Package Halo as MSIX, and choose a signing route | Notification mirroring (O-F01), toasts, Start with Windows, clean install tests (L-F02/L-F03) |
| E-1 | Entra app registration and owner sign-in | Live Outlook mail and calendar |
| K-1 | Turn on the camera-in-use indicator (built, off by default) | Camera indicator in daily use |
| O-F02 | Keyboard-state indicators | Caps Lock and input-language pills |
| — | Approve `System.Management` | Brightness indicator (J-F03) |
| D7 | Second monitor; clean PC or VM | Multi-monitor and mixed DPI; install tests |

## 16. Final working-tree state

- `git status`: clean after the report commit. No untracked files remain, and the scratch screenshots stayed outside
  the repository.
- `git remote -v`: no remotes; nothing was pushed.
- No Halo process is running after the suites.
- OneDrive copy: `git status` clean, HEAD `9f7d5fb`, unchanged.

## 17. Next concrete step

Run a 10-minute Narrator check of the island:
- alerts;
- timer completion;
- a reminder;
- a Do Not Disturb summary;
- the expanded island's buttons and sliders.

Then decide L-1 and E-1, which unlock the next largest features: notification mirroring and live Outlook.
