# Live Activity Engine 2.0 and Island Silhouette 2.0: Design

Status: implemented on top of `8c67db0` (Visual Experience 2.0). This document describes the code as built. The
phase-by-phase results, evidence and PASS/PARTIAL/BLOCKED table are in
[LIVE_ACTIVITY_ENGINE_2_REPORT.md](LIVE_ACTIVITY_ENGINE_2_REPORT.md).

Sources for the Apple behaviour are Apple's own pages, read on 2026-10-29: the Human Interface Guidelines
"Live Activities" page (updated 2026-10-13) and the ActivityKit article "Displaying live data with Live Activities".
Only design rules and numbers are used; no Apple assets or code.

## 1. Architecture before this milestone (audit of `8c67db0`)

| Area | What existed |
|---|---|
| Window | One `WS_POPUP` with `WS_EX_NOREDIRECTIONBITMAP \| WS_EX_TOPMOST \| WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`. Drawn only by a `DesktopWindowTarget` (Windows.UI.Composition), so pixels outside the visuals are truly transparent. Input is limited by `SetWindowRgn`. |
| Geometry | A fixed canvas (expanded size + 14 DIP margin), top centre of the work area. VX 2.0 shapes were flush with the top edge: the drawn rectangle started above the window by its radius, with two concave "shoulders". That is the notch look this milestone replaces. Compact 236 × 36, alert 320 × 42, expanded 460 × 196 (r 34). |
| State | `IslandStateMachine`, a pure function: phases Collapsed / ExpandPending / ExpandedByHover / CollapsePending / ExpandedPinned, with Suppressed / ShowNow for full screen. `IslandController` owns the two one-shot timers (hover intent, alert life) and the alert queue. Every input already went through it: pointer, click, menu, tray, UIA, second launch, full screen. |
| Presentations | Compact, Alert, Expanded. There was no idle state: the clock pill was always visible. |
| Activities | `ActivityBoard`: bounded, coalescing upserts, expiry, explicit selection, quiet-mode and full-screen filters. The primary was chosen by priority and then **most recently changed**, so a countdown tick took the island from an equal-priority activity every second. Media, charging and network were not activities: media sat in a separate pill slot, and battery and network were transient alerts only. |
| Motion | `MotionVocabulary` / `MotionPlanner` / `MotionTracker`: one plan per channel, a plan ID so stale completion callbacks are ignored, reduced-motion variants, a 2 s ceiling. Morph 350 ms with an ease-out. |
| Threading | Single UI thread. Watchers (media, volume, network, brightness, camera) post window messages. Media is debounced 150 ms, network 2 s. |
| Accessibility | A UIA provider on the window (Button, ExpandCollapse, Invoke, page controls), with live-region announcements for alerts. |

Deficiencies against this milestone: no idle state; a notch silhouette; only one activity visible; ordering by update
time, which caused churn; no lifecycle beyond upsert, remove and expire, so a dismissed or expired activity came back
when its provider published again; media, charging and network outside the activity model; and no deterministic
tie-break.

## 2. Silhouette 2.0

### 2.1 Presentations

`IslandPresentation` gained `Line`. The precedence is computed in `IslandState.Presentation`, one place:

1. **Expanded**: the phase is open (hover-opened, collapse pending, or pinned).
2. **Alert**: a transient alert is shown.
3. **Compact**: thin-line mode is off, or a live activity asks for the pill (`Live`), or the pointer is on the island,
   or it is pressed, or a hover expansion is pending.
4. **Line**: otherwise.

`Live` and `LineIdle` are fields of `IslandState`. They are set only through `IslandController.SetLive` (from the
activity board's primary) and `SetLineIdle` (from the setting). Providers, timers, the renderer and pointer handlers
never set a presentation directly.

Mapping to the conceptual states in the brief:

| Brief | Implementation |
|---|---|
| ThinLineIdle | `Presentation == Line` (Collapsed, not hovered, not live, LineIdle on) |
| CompactVisible | `Presentation == Compact` |
| ExpandPending | `Phase == ExpandPending`, shown compact |
| Expanded | `ExpandedByHover` |
| CollapsePending | `CollapsePending`, still expanded |
| PinnedExpanded | `ExpandedPinned` |
| SuppressedFullscreen | `Hidden` (Suppressed without ShowNow); the window is hidden and nothing renders |

### 2.2 Geometry (DIP; the layout scales by dpi/96 and rounds to whole pixels)

| Shape | Size | Radius | Position |
|---|---|---|---|
| Line | 76 × 2 (clamped to 2–4 physical px) | half the thickness | top 4, centred |
| Line pointer zone | 168 × 12 | – | from the top edge, centred |
| Compact pill | 236 × 38 | 19 (pill) | top 8 (floating gap) |
| Detached bubble | 38 × 38 | 19 (circle) | 6 right of the pill |
| Alert pill | 320 × 44 | 22 | top 8 |
| Expanded | 460 × 196 (+40 volume row) | 40 | top 8 |

Apple's numbers for comparison: the compact island is 36.67 pt tall, minimal bubbles are 36.67–45 × 36.67 pt, and the
expanded island is 371–408 × 84–160 pt. Halo is a little taller than the compact island so that desktop text fits.
The expanded island stays at its VX 2.0 size because it hosts the pages.

### 2.3 Transparency, input region and hit testing

- The host window is still the fixed canvas, and only composition visuals draw into it. There is no redirection
  bitmap, so there is no black rectangle; transparent pixels are transparent.
- Input is the window region:
  - Line: the 168 × 12 DIP pointer zone.
  - Other presentations: the shape extended up to the top edge (`RoundedBox.Reach`). The gap above a floating shape
    counts as island, so a pointer pushed against the top of the screen lands on it, and the gap never "leaves" the
    island (no flicker).
  - The detached bubble is added to the region while it is shown.
  - During a transition the region is the expanded shape, widened by the opening overshoot, plus the bubble. When
    the latest plan completes, the target region is set.
- Hover is decided on the target shape (`IslandLayout.Hit`), not on the region. The line zone lies inside the pill's
  zone, so line → pill can never turn into "left" → line → "entered".
- Everything below the 12 DIP zone, beside it, and where the pill would be stays the application's (desktop test
  `TheIdleIslandIsAThinLineWhoseSmallZoneLetsClicksBelowThrough`).
- No global hooks, no capture except during a press, `WS_EX_NOACTIVATE` unchanged: no focus theft (desktop tests
  assert the foreground window).

### 2.4 Rendering

One `CompositionRoundedRectangleGeometry` is the line, the pills and the open island, so every change is one
continuous morph. In the line state the gradient stops and the rim animate to a neutral grey capsule,
`rgba(128,128,128,0.78)`, readable on dark and light wallpapers. In high contrast the line uses the system text
colour. Leaving the line, they animate back to the dark surface. The VX 2.0 shoulders and the "drawn above the
window" offset are removed. Nothing animates or redraws while the line rests. The minute clock timer keeps the text
current; it redraws only lines whose text changed, and the line itself has none.

## 3. Activity model (Halo.Core/Activities)

`IslandActivity` is an immutable record; the board stores snapshots. Domain fields:

| Concept | Field |
|---|---|
| Stable ID | `Id` (same ID = same activity) |
| Type / provider | `Kind` (Timer, Focus, Reminder, Task, Notification, Calendar, System, Media, Power, Network), `Provider` token |
| Lifecycle | `Lifecycle` (Active, Paused); ending is leaving the board |
| Priority / urgency | `Priority` (Low, Normal, High, Critical), `Urgent`, `Relevance` (Apple's relevanceScore) |
| Timestamps | `CreatedAt`, `UpdatedAt` (set by the board), `ExpiresAt` |
| Persistence | `Ongoing` (persistent) vs transient with `ExpiresAt` |
| Dismissible | `Dismissible` (camera: false) |
| Compact support | `ShowsCompact` (paused media: false, "background") |
| DND / full screen | `AllowedInQuietMode`, `AllowedWhenSuppressed` |
| Presentation metadata | `Glyph`, `Tone`, `CompactText`, `RedactedText`, `Title`, `Subtitle`, `Level`, `Actions`, `Emphasis`, `Accessible`, `Privacy` |
| Safe diagnostics identity | `ActivityBoard.ProviderOf(a)`, a fixed token, plus the ID. Diagnostics never log content. |

No renderer objects, handles or controls are in the model.

### 3.1 Lifecycle operations

| Operation | API |
|---|---|
| Register / start, update, promote / demote, pause / resume, reconnect after a provider restart | `Upsert`. The same ID keeps `CreatedAt`; equal content is a no-op; priority, relevance and lifecycle are fields. |
| Complete / remove | `End(id)` (alias `Remove`): leaves the board; the ID may be reused. |
| Dismiss | `Dismiss(id)`: leaves the board and the ID is **retired**. Further identical publications are ignored until the provider calls `End`. |
| Expire | `RemoveExpired()`: the same retirement, so a finished transient activity does not reappear from a repeated publication. |
| Provider turned off | `StopProvider(token)` |

Bounded: at most 8 activities (eviction: lowest priority, oldest, then ID), 32 retired IDs and 16 history entries
(ID, provider and reason only).

## 4. Scheduler

`ActivityBoard.Primary` is the activity attached to the compact island. `Secondary` is the next one, shown detached
beside it as Apple's minimal presentation. Both come from one total order, compared key by key:

1. critical priority;
2. `Urgent` (operational: network lost);
3. the user's explicit selection (while that activity exists and is eligible);
4. priority (High > Normal > Low);
5. Active before Paused;
6. relevance, higher first (camera 30, countdowns 20, media 10, others 0);
7. start time, earlier first. This is Apple's rule: "If you don't provide a relevance score or if Live Activities
   have the same relevance score, the system shows the first Live Activity you started in the Dynamic Island";
8. ID, ordinal, so the order is total and deterministic.

Only eligible activities count (quiet mode: `AllowedInQuietMode` or Critical; full screen: `AllowedWhenSuppressed`),
and only those with `ShowsCompact` can be primary or secondary. Quiet mode and full screen filter; they never delete.
The order does not depend on `UpdatedAt` or arrival order. Equivalent event sets give identical decisions whatever
the order: a unit test checks all 720 orders of six activities.

### 4.1 Fairness and switching

- **No churn**: updates never reorder. A countdown tick or a media position change cannot take the island.
- **Nothing hidden**: the second activity is always visible as the detached bubble, which is Apple's answer to
  several Live Activities. Clicking the bubble selects that activity. It swaps into the pill, with no provider
  restart and no expansion.
- **Selection** lasts while the activity exists and is eligible. It ends with the activity and outranks everything
  except critical and urgent activities. It stops rotation.
- **Rotation** (setting `rotateActivities`, off by default): every 8 s it steps through activities that tie on
  criticality, urgency, priority and lifecycle. The timer is armed only while that tie exists and the island is
  visible and closed.
- Urgent and critical activities take the island immediately. Lower-ranked arrivals go to the bubble.

### 4.2 Reconciliation with the existing alert system

Transient alerts (`IslandController.Raise`) are unchanged: priority, coalescing by key, a queue of four, stale drops,
quiet mode and full-screen allowances. An alert is shown in the alert pill and returns to whatever the board decides
(line or pill). This mirrors Apple, where an alert briefly shows the expanded presentation and the activity then
continues compact.

### 4.3 Threading

Single-threaded by design: the board and controller are touched only on the UI thread. Providers post Win32
messages; WinRT callbacks marshal to the window before touching state. This is the one serialisation mechanism, and
it makes concurrent mutation impossible.

Coalescing:
- media: 150 ms debounce;
- network: 2 s debounce;
- volume device: 200 ms;
- equal upserts are no-ops.

Countdowns use one tick timer, armed only while something is visible. Shutdown: `IslandController.Shutdown` and
`MotionTracker` ignore late callbacks, and every Win32 timer is killed in `WM_DESTROY`.

## 5. Providers (real data sources only)

| Provider | Source | Activity |
|---|---|---|
| Timer, focus | Halo's own countdowns | Normal, relevance 20. Paused = `Paused` lifecycle, still compact. Completion is an alert plus a High transient activity, emitted once. |
| Media | `GlobalSystemMediaTransportControlsSessionManager` (existing watcher) | ID `media`, stable across tracks. Playing: Active and compact. Paused: background only. No session: ended. The position stays out of the activity, so progress never touches the board. Controls and art are unchanged. |
| Charging | `GetSystemPowerStatus` and power broadcasts | Only when the charger is **connected** while Halo runs: Low priority, expires after 45 s, then retired until unplugged. An always-plugged laptop still rests as a line. Low and critical battery stay alerts with their existing priorities. |
| Network | `Windows.Networking.Connectivity.NetworkInformation` (existing watcher; never the Wi-Fi name) | Offline: High and `Urgent`, dismissible. Connected: passive (header icon only). Unknown readings never claim offline. |
| Camera | Media Foundation's passive sensor-activity reports (existing `CameraWatcher`, opt-in setting; never opens a camera) | Normal, relevance 30, not dismissible. Microphone use is not detected. |
| Reminders, notifications | `NotificationCenter` (existing) | Unchanged activities and alerts, with private text redacted unless previews are on. |

Providers do not touch geometry or visuals. The window's `PublishBoard` is the only place board decisions reach the
island: lead (media or activity), page activity, secondary bubble, and `SetLive`.

## 6. Motion

All transitions run through `MotionPlanner` and `MotionTiming`, as one plan per change:

| Change | Morph | Content | Notes |
|---|---|---|---|
| Line ↔ pill / alert / expanded | 220 ms | out 90 ms; in 150 ms after 90 ms | colour morphs with the shape |
| Pill / alert → expanded | 300 ms, about 3 % overshoot | out 110 ms; in 180 ms after 120 ms; settle 260 ms after 40 ms | the "springy" open |
| Expanded → pill / alert / line | 260 ms, no overshoot | in 180 ms after 110 ms | |
| Pill ↔ alert | 240 ms | out 100 ms; in 160 ms after 90 ms | |
| Bubble in / out | 240 / 160 ms scale 0.55 ↔ 1 plus fade | | grows out of the island's edge |
| Activity swap | 260 ms arrival pulse | | |

Apple publishes no curve for the Dynamic Island. What it documents is a 2 s maximum, layout changes that keep and
move existing elements, and content-replace (fade) transitions. Halo follows those rules. The specific durations
are the brief's targets.

Interruption:
- Composition animations start from the current value, so an interrupted morph continues from where it is.
- A newer `StartAnimation` replaces the older one per property.
- `MotionTracker` plan IDs make stale completion messages no-ops: only the latest plan sets the final region.

Reduced motion: an instant morph, 150 ms fades only, and no overshoot, settle, pulse or bubble scale.

## 7. Windows-wide notification ingestion (Phase 5)

| Candidate | Official API | Requirements | Data | Decision |
|---|---|---|---|---|
| Other apps' notifications | `UserNotificationListener` (Windows.UI.Notifications.Management), Windows 10 1607+ | Package identity, the `userNotificationListener` capability in the manifest, and the user's consent via `RequestAccessAsync` on the UI thread; revocable in Settings, after which calls silently return nothing | App display name and logo, creation time, toast text elements | **Adapter built, live ingestion BLOCKED** (below) |
| Notification changes | `NotificationChanged` event (foreground), or `UserNotificationChangedTrigger` background task | as above | change signal only, then re-read | not used yet |
| Media | `GlobalSystemMediaTransportControlsSessionManager` | none (already used) | title, artist, art, timeline, controls | integrated |
| System status | power, network, consent store, Core Audio | none (already used) | as listed in §5 | integrated |
| Downloads, file transfer, navigation, calls | No universal Windows API exposes other apps' progress or route state | per-app integration | – | deferred: extension point only |

Built:
- `WindowsNotifications`: a pure adapter into the existing `NotificationCenter`, so dedup, previews (off by
  default), priority, rate limits, quiet mode and expiry are the same as for every other provider;
- `NotificationAccess` with a side-effect-free `Evaluate`, in the order opt-in → package identity → declared
  capability → Windows' answer;
- text cleaning: control characters removed, a single line of at most 120 characters, and Halo's own notifications
  never echoed.

At startup the window logs the access state as a token. Today that state is `off`: there is no opt-in, and the
dev package does not declare the capability.

Blocker: reading other apps' notifications exposes the content of every message, so it requires the owner's
explicit decision. The steps are:
1. add `<uap3:Capability Name="userNotificationListener"/>` to `packaging/Package.appxmanifest`;
2. add an opt-in setting;
3. call `RequestAccessAsync` and answer the Windows consent prompt;
4. install the MSIX.

None of these were done without that decision. No notification is read and no activity is fabricated.

## 8. Settings

| Key | Default | Effect |
|---|---|---|
| `idleLine` | true | Thin-line idle. Off = the clock pill as the idle island (the VX 2.0 behaviour, floating). |
| `rotateActivities` | false | Rotation among tied activities (§4.1). |

Both are persisted by the existing atomic JSON store. Missing keys fall back to defaults, existing settings are
kept on upgrade, and the existing "pill shows media" option also decides whether playing media brings up the pill.

## 9. Testing strategy

- **Core unit tests** (`LiveActivityEngineTests`, updated `ActivityBoardTests`, `IslandTests`, `MotionTests`):
  - lifecycle, retirement, bounds and invalid input;
  - every scheduler scenario in the brief, plus the 720-permutation determinism test;
  - presentation-state transitions (line, pill, open, alert, pin, full screen, click-only, shutdown);
  - line and bubble geometry at 96, 120, 144 and 192 DPI;
  - motion timings;
  - provider adapters;
  - notification access and privacy.
- **Desktop tests** (`IslandAppTests.Line`, on the real Halo.exe):
  - the line region and clicks passing through;
  - line → pill → open → line with exactly three transitions and no focus theft;
  - a live activity growing the line and its end returning it;
  - a bubble click swapping without opening or taking focus.
- The existing 86 desktop tests keep covering the clock-pill idle mode (`idleLine: false`, injected by the harness
  unless a test sets it).
- **Visual verification**: real Release builds, screenshots in `docs/visual/live-activity-engine-2/`.

## 10. Migration risks and compatibility decisions

- **Ordering change**: "first started wins" replaces "most recently changed wins". Two board tests were updated with
  the reason. A newly started equal-rank activity now waits in the bubble instead of taking the pill.
- **Idle look**: upgraded installs get the line by default (no settings are reset); `idleLine: false` restores the
  pill.
- **Silhouette**: VX 2.0's flush top and shoulders are removed (the brief asks for a floating pill). Canvas height
  grows by the 8 DIP gap.
- **Motion**: the open morph drops from 350 to 300 ms. The tests that pinned 350 ms were updated.
- **Pages**: the media activity never becomes the "Activity" page (media has its own page). The page activity is the
  primary when it is not media, else the first eligible non-media activity.
