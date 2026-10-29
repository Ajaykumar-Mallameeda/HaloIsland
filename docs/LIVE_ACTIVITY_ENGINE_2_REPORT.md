# Live Activity Engine 2.0 and Island Silhouette 2.0: Report

Date: 2026-10-29. Base `8c67db0` (Visual Experience 2.0). Design: [`LIVE_ACTIVITY_ENGINE_2_DESIGN.md`](LIVE_ACTIVITY_ENGINE_2_DESIGN.md).
Motion values: [`MOTION_SYSTEM.md`](MOTION_SYSTEM.md). Screenshots: [`visual/live-activity-engine-2/`](visual/live-activity-engine-2/).

Apple's Dynamic Island guidance was read from developer.apple.com: the Human Interface Guidelines for Live Activities
and the ActivityKit documentation. The sizes, the one-attached-and-one-detached rule, alerts showing the expanded view
briefly, "highest relevance wins, and the first started wins a tie", immediate removal at the end, and the 8-hour limit
come from those pages. Apple publishes no spring curve or timing table for the island. Halo's timings are therefore its
own values inside Apple's "at most 2 s" limit, and are not claimed to be Apple's.

## 1. Outcome by phase

| Phase | Result | Evidence |
|---|---|---|
| 0 Audit and design | PASS | design doc §1–§10 |
| 1 Silhouette: thin-line idle, floating pill, expanded, state machine | PASS | `IslandPresentation.Line`; `IslandTests`; desktop `IslandAppTests.Line` (4 tests); `visual/.../phase-1/` |
| 2 Activity model and lifecycle | PASS | `ActivityBoard` (upsert, end, dismiss, expire, provider stop, history); `LiveActivityEngineTests` |
| 3 Scheduler: deterministic, fair, switchable | PASS | rank order (design §4); the 720-permutation determinism test; explicit selection; opt-in rotation every 8 s |
| 4 Motion: interruption, Reduce Motion | PASS | `MotionTiming`; an interrupted transition ends only the newest plan (desktop line test); Reduce Motion is an instant morph with 150 ms fades (`reduced-*.png`) |
| 5 Windows-wide notifications | **BLOCKED** (adapter PARTIAL) | adapter, access evaluation and redaction built and tested; live reading needs the owner's opt-in (§4) |
| 6 Providers | PASS for media, timer, focus, charging, offline, camera and reminders | §3 |
| 7 Presentation contract | PASS | lead in the pill, second activity in the detached bubble, "+N" for the rest, line when nothing is live |
| 8 Settings | PASS | `idleLine` (default on) and `rotateActivities` (default off): saved, parsed and shown in the Settings window |
| 9 Tests | PASS | §5 |
| 10 Visual and performance | PASS (smoke) | §6, §7 |
| 11 Docs, report, commit | PASS | this file, `PROJECT_STATUS.md`, local commit |

## 2. What changed for the user

- **Idle is a thin line:** a 76 × 2 DIP grey line at the top centre. Clicks just below or beside it reach the app
  underneath. Its pointer zone is 168 × 12 DIP. Resting the pointer on it grows it into the pill, then opens the island
  after the hover delay. Leaving returns it to the line.
- **Something live grows the line into the floating pill:** a timer, focus session, playing media, an offline network,
  a charger just plugged in (shown for 45 s), or the camera in use. When the activity ends, the pill returns to the line.
- **Two activities:** the second one sits in a round detached bubble to the right of the pill, like Apple's minimal
  presentation. Clicking the bubble swaps the two. It does not open the island or take focus. Any further activities
  show as "+N" in the pill.
- **Order:**
  1. critical;
  2. urgent;
  3. the one you picked;
  4. priority;
  5. running before paused;
  6. relevance (camera 30, timers 20, media 10);
  7. first started.
  Updates never reorder activities.
- **Dismiss:** the island's menu offers "Dismiss *title*" for dismissible activities. Camera use cannot be dismissed.
- **Settings:** "Idle as a thin line" and "Rotate the pill between live activities (every 8 s)".

## 3. Providers

| Provider | Source | Shown | Privacy |
|---|---|---|---|
| Timer, Pomodoro, focus | Halo's own timers | pill with ring; paused stays listed but leaves the pill | — |
| Media | GSMTC (already used) | playing: pill; paused: page only | titles are never logged |
| Charging | power broadcasts (already used) | 45 s after plugging in | level only |
| Offline | `NetworkInformation` (already used) | urgent pill until connected | no network names |
| Camera | consent-store activity reports (already used, off by default) | pill or bubble, not dismissible | only "in use or not"; no app names; the camera is never opened |
| Reminders | Halo's reminders and tasks | as before, now ranked by the scheduler | private text hidden unless previews are on |

The diagnostics log records only provider tokens (`timer`, `media`, `network` …), counts and reasons. It never
records titles or text.

## 4. Windows-wide notifications (Phase 5): BLOCKED

`UserNotificationListener` is the only supported API for reading other apps' notifications. It needs three things:
package identity, the `userNotificationListener` capability in the MSIX manifest, and the user's consent through
`RequestAccessAsync`.

Built and tested:
- `WindowsNotifications.Evaluate`, which reports `Off`, `NoPackageIdentity`, `NotDeclared`, `NotRequested`, `Denied` or
  `Allowed`;
- `From`, which strips control characters, trims to 120 characters, and skips Halo's own and empty toasts;
- a policy with previews off, a 5-minute lifetime, at most 4 per minute, and never shown over full screen or in quiet
  mode.

At start-up Halo logs `notifications.listener` with the access state and whether it has package identity.

Not done, on purpose:
- The capability has not been added to the manifest.
- There is no opt-in switch and no consent prompt.

Reading other apps' notifications is a privacy decision for the owner, and the new MSIX is not installed. To enable it:
1. The owner opts in.
2. Add the capability, the setting and the consent request.
3. Build and install the MSIX.

## 5. Tests (final code, Release)

| Suite | Result |
|---|---|
| Core unit | **479/479** (was 440) |
| App unit | **14/14** |
| Spike unit | **79/79** |
| Island desktop (outside the sandbox) | **90/90** (was 86; 4 new line and bubble tests) |
| Spike desktop | **5/5** |
| Release build | 0 warnings, 0 errors |

The following test expectations were updated because the design changed. No test was removed or loosened:

- **Floating geometry:**
  - canvas height 273;
  - the region now covers the gap above the shape (`Reach`);
  - shoulders are gone.
- **Ordering:** a tie is won by the first-started activity, as in Apple's documentation, where the old board used the
  newest. Updates do not reorder.
- **Motion:** opening takes 300 ms with content fading in at 120 ms; collapsing takes 260 ms with content at 110 ms.
- **Settings:** 30 keys; 20 check boxes in the Settings window.
- **Existing desktop tests keep the pill idle** by writing `idleLine: false` unless a test sets it. The new line tests
  cover the line idle.

During this work, three desktop failures came from the tests themselves:
- the check-box count was not updated;
- the "Offline" alert was not allowed its 3 s;
- the test counted the end of a transition that was interrupted on purpose.

All three were fixed in the tests, and the full suite then passed 90/90.

The two failing runs also printed "Halo failed to start: InvalidOperationException" once. The final passing run did not
print it. The cause was not traced. One possibility is a test starting Halo while the process from a failed test had
not yet exited.

## 6. Performance (smoke, `harness app-check`, 120 Hz)

| Measurement | VX 2.0 `APP-SMOKE-120-165727` | Now `APP-SMOKE-120-180643` | Budget |
|---|---|---|---|
| Validity | valid | valid | — |
| Start → first frame p50 | 310 ms | 319 ms | < 1 s |
| Idle 90 s CPU | 15.6 ms | **0 ms** | PB-01 |
| Idle attributed frames | 2 | **0** (the line has no clock to redraw) | PB-03 |
| Private / working set | 24.2 / 87.1 MB | 24.1 / 88.0 MB | < 120 MB private |
| Transitions | 20/20 valid, p95 8.9 ms, max 16.5 ms, 1 with a long frame | 20/20 valid, p95 9.0 ms, max 19.0 ms, 8 with a long frame | p95 < 16.7 ms |
| Exit p50 | 74.9 ms | 75.8 ms | — |

The first attempt (`app-check` at about 18:05) was **invalid** because of user input during the run. It was repeated,
not counted.

More transitions now contain one long frame. A likely cause is that the open now animates the surface gradient and rim
together with the shape. This is an observation: p95 still meets the budget, but a formal PB-05 run is still needed.

## 7. Screenshots

Taken on the real Release build over a plain backdrop window, so no desktop content is in the files.

| Folder / prefix | Shows |
|---|---|
| `phase-1/` | line, hover pill, expanded Today and back to the line, in dark and light |
| `dark-04…06` | expanded Pomodoro, To-do and Shelf pages |
| `dark-08` | line-to-pill morph caught mid-transition |
| `dark-09`, `light-09` | timer pill |
| `dark-10…12` | focus arrives in the detached bubble; a click swaps them (midway, done) |
| `dark-13`, `dark-14` | the urgent "Offline" alert, then the offline pill with the bubble and "+1" |
| `dark-15` | charging in the bubble |
| `reduced-*` | Reduce Motion: line, pill, and the timer pill 40 ms after it started (instant shape, content fading) |

## 8. Limitations and open items

- Windows-wide notifications are BLOCKED on the owner's opt-in (§4).
- Downloads, transfers, calls and navigation have no universal Windows API. Only the extension point exists.
- Microphone use is not detected and not claimed.
- Not checked by eye: high contrast, a live media session, Narrator. The tests use synthetic media and power events.
- Not measured: page-slide and bubble frame pacing (the harness has no marker for them), and 60 Hz.
- Multi-monitor is not tested: one display.
- **The installed app is still MSIX 0.1.0.0.** This work is in the Release build and the repository only. Installing a
  new MSIX is the owner's decision.
