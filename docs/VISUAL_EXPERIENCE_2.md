# Halo Visual Experience 2.0

> **Superseded in part (2026-10-29):** the flush-top silhouette with concave shoulders was replaced by the floating pill, thin-line idle and detached bubble of [Live Activity Engine 2.0](LIVE_ACTIVITY_ENGINE_2_DESIGN.md). The surface, tones, page frame and in-island pages described here still apply.

| Field | Value |
|---|---|
| Status | Implemented 2026-10-29 on `main` (local commits only) |
| Renderer | Unchanged: option C, Win32 + `Windows.UI.Composition` (ADR-001 §13.1). No new framework or dependency |
| Inputs | The owner's reference screenshots of a Dynamic Island–style notch (used for design principles only; no assets, code or exact layouts were taken), the `design:design-critique` plugin's framework, the existing MOTION_SYSTEM.md |
| Related | [MOTION_SYSTEM.md](MOTION_SYSTEM.md), [PROJECT_STATUS.md](PROJECT_STATUS.md) §"Visual Experience 2.0" |

The owner's feedback was that the features work, but the island looked "too naive" next to an iOS-style Dynamic Island:
there was no reactivity, no colour and no depth. The owner also asked that the features be reachable **inside Halo
itself**, not only from the tray menu.

## 1. Baseline (before)

Measured on `b1712b1`, before any change. These are SMOKE results from `harness app-check`, not budget verdicts.

| Measurement | `APP-SMOKE-120-161942` (valid, 120 Hz) |
|---|---|
| Start → first frame p50 | 284.6 ms |
| Idle 90 s | 15.6 ms CPU, 1 attributed frame (the minute clock) |
| Private / working set | 21.3 / 82.3 MB |
| Transitions | 20/20 valid; 2 with one long frame; p95 8.8 ms; max 16.8 ms |
| Exit p50 | 77.9 ms; 0 processes left |

Screenshots from the harness: a floating black pill ("Thu 29 · 2:17 PM") 6 DIP below the screen edge, and a 420 × 172 card
with a large clock, the long date, a day-progress bar, a Wi-Fi/battery cluster and a volume row.

## 2. Critique and gap analysis

Critique using the design-critique framework (first impression, usability, hierarchy, consistency, accessibility):

| Finding | Severity | Change made |
|---|---|---|
| The pill floats below the screen edge, so it reads as a widget rather than part of the display | 🔴 | Flush-top silhouette with concave shoulders (§3.2) |
| The open island has no header and no visible navigation: pages hid behind three small dots at the bottom right | 🔴 | A header row on every page (title, date and time, status), chevrons, and centred page dots (§3.3) |
| Everything is white on black, so no activity has its own personality | 🔴 | Semantic tones and one accent per activity (§3.1) |
| Activity actions were 36 DIP glyph-only squares with no labels | 🟡 | Labelled chips with one filled primary action; icon-only only when the label does not fit (§3.4) |
| The timer is a flat bar | 🟡 | A progress ring, in the pill and in the open island (§4) |
| Timer, Do Not Disturb, tasks and the shelf could only be reached from the tray | 🔴 | Clock (with Do Not Disturb and Settings), Pomodoro, To-do and Shelf pages inside the island (§5) |
| Flat surface, uniform 40-alpha outline on every side | 🟢 | A subtle vertical gradient and a rim light on the lower edge only |

What already worked and was kept: the motion engine (morph, fades, settle, swap, level, emphasis, arrival), UIA for every
control, theme and high-contrast support, and no rendering while settled.

## 3. Design system

All tokens live in `Halo.Core/Theming/HaloDesign.cs` and `IslandPalette`, so drawing, hit-testing and UI Automation share
one set of values. Tests: `DesignTokenTests`, `PageControlsTests`.

### 3.1 Colour

- **Surface:** near-black vertical gradient (`#000000` → `#0E0E11`), so there is depth without a visible band (the stops
  differ by less than 1.25:1). The light theme uses `#FAFAFC` → `#EEEEF2`.
- **Rim:** the outline is transparent along the top half and fades in to 46 alpha along the lower curve, so it lights the
  edge without framing the island against the screen edge.
- **Elevated:** chips, panels, the art placeholder and the chevrons sit on white at 30 alpha (black at 16 in the light theme).
- **Tones:** each alert and activity carries a `Tone` (focus, short break, long break, timer, charging, warning, critical,
  connected, offline, Bluetooth, camera, reminder, notification, quiet, brightness, tasks, shelf, media). `HaloDesign.Accent`
  turns a tone into a colour:
  - neutral uses the palette's bar colour, which still follows the user's "accent bars" setting;
  - other tones use one restrained system-like hue each, adjusted until it reaches 3:1 against the fill (WCAG 1.4.11);
  - high contrast uses the system highlight for every tone and flat surfaces with a full outline.
- **Text on an accent** is black or white, whichever has more contrast.
- **Colour is never the only signal.** Every state is also in the glyph and the text, and UIA names it.

### 3.2 Silhouette

- Flush with the top of the work area (`TopGapDip` 0). The drawn rounded rectangle starts above the window by its own corner
  radius (`RoundedBox.Drawn()`), so only the lower corners show and the gradient stays continuous through every morph.
- Two concave shoulders (10 DIP) join it to the screen edge. Each one is a square of the surface colour masked by a radial
  gradient. Expression animations bind them to the shape, so they follow every morph in DWM with no managed code per frame.
- Hit-testing treats the top corners as square (`RoundedBox.ContainsSquareTop`). The window region adds a strip for the
  shoulders.

| Shape | DIP (was) | Radius |
|---|---|---|
| Compact | 236 × 36 (236 × 38) | pill |
| Alert | 320 × 42 (300 × 44) | pill |
| Expanded | 460 × 196, +40 with the volume row (420 × 172) | 34 |

### 3.3 Frame of the expanded island

Every page uses the same frame (`ActivityLayout`):
- a header row at 21 DIP: page glyph (tinted with the page's tone) and title on the left, date and **bold time** in the
  centre (hidden on the clock page, which shows the time large), status glyphs and battery on the right;
- chevrons, 28 DIP targets, centred 17 DIP from the sides;
- page dots, centred at the bottom: the current page is a 16 × 5 capsule and the others are 5 DIP dots. They are
  rounded-rectangle shapes, so the capsule animates its width without a clip;
- a body between the chevrons, inset 40 DIP.

### 3.4 Type and controls

| Token | Value |
|---|---|
| Readout (timer, task count) | Segoe UI Variable Display 40 semibold |
| Clock | Display 50 semibold, designator 18 |
| Headline (reminder, notification) | Display 20 semibold |
| Title / body / caption | Text 15.5 / 13 / 11.5 |
| Header | Text 13 semibold; time 12.5 semibold |
| Chip | 30 DIP high capsule, label Text 12 semibold, glyph 11.5 |

Chips are sized to their labels (`ActivityLayout.ChipRow`). If a row does not fit, the widest secondary chip becomes
icon-only (44 DIP) until it does. The first chip (the page's main action) keeps its label and is filled with the page's
accent. Labels stay the accessible names.

## 4. Activity personalities

| Activity | Compact | Expanded | Motion |
|---|---|---|---|
| Timer | orange ring + "4:59" | 78 DIP ring, readout, subtitle with the end time, chips Pause / + / Cancel timer | Ring end glides each second (Level, 240 ms, in DWM); Emphasis in the last 10 s; Arrival on completion |
| Focus session | ring in red (work), green (short break) or blue (long break) | same layout, Pause / Skip / Stop | as timer; the phase change shows a tinted alert |
| Media | cover thumbnail (or tinted glyph) + title | 72 DIP cover, title and artist, elapsed and remaining times around an accent progress bar with a thumb, transport row with a filled play disc | track change: Swap dip on card, title and thumbnail; play/pause: one 260 ms pulse of the play disc |
| Volume | — (alert pill) | volume row; muted dims the bar | Level glide for outside changes; the mute glyph dips on mute/unmute; dragging stays direct |
| Battery / power | — | status in the header | alert tinted green (charging), yellow (low), red (critical) |
| Network | — | status in the header | alert tinted blue (connected), orange (offline), yellow (limited) |
| Bluetooth audio | — | — | blue alert |
| Camera (opt-in) | green glyph | green disc | green alert, like a privacy indicator |
| Reminders, notifications | tinted glyph | tinted disc, headline, subtitle, chips | existing priorities and Swap; no repeat flashing |

**Artwork accent:** `HaloDesign.ArtAccent` averages the colourful pixels of the cover on a grid of at most 32 × 32
samples. It runs once per new picture, and a grey cover falls back to the media tone. The result is adjusted to 3:1
against the fill. High contrast skips it.

## 5. Features inside the island

The open island now has these pages, in order: **Activity** (when one is live), **Media** (when something plays),
**Clock**, **Pomodoro**, **To-do**, **Shelf**. It still opens on the activity, else media, else the clock.

| Page | Shows | Controls (`PageControls`) |
|---|---|---|
| Clock ("Today") | large time, long date, day progress | **Do Not Disturb** (toggle, purple when on), **Settings** |
| Pomodoro | static red ring, "25:00", "25 min focus · 5 min break" | **Focus** (starts the default session), **Break** (5-minute timer), **15 min** (timer) |
| To-do | "done today / total" readout; up to three pending tasks with check buttons | **Add task**, **All** (open the tasks window), one **Mark done** per row |
| Shelf | up to four tiles (file or text) and the item count; a dashed empty state | **Pin clipboard**, **Open shelf**, a tile copies its item back to the clipboard and confirms with a short "Copied" alert |

Rules kept:
- Typing (a new task's text) still happens in the tasks window: the island never takes focus (FR-ISL-06).
- Task text appears only while previews are allowed (D-F03). Otherwise the page shows "N pending · previews off", and UIA
  exposes the counts only.
- Every command goes through the same path as the tray menu, and the tray menu is unchanged.
- One list (`PageControls.For`) draws the chips, hit-tests the pointer and builds UIA buttons
  (`AccessibleCommand.PageControl`, index checked against the current state, so a stale request is dropped).

## 6. Motion added

All of it is built on the existing engine: no new timers and no per-frame managed code. See MOTION_SYSTEM.md §1.

| Motion | Where | Spec |
|---|---|---|
| Page slide | body of the open island on a page change | from ±28 DIP and transparent to in place: 250 ms offset, 160 ms fade; direction follows the step; the header title and glyph Swap; reduced motion: none |
| Dot morph | page dots | 220 ms size and offset |
| Press | chips, chevrons, tiles, checks | 94 % scale, 90 ms down and 180 ms up; reduced motion: off |
| Ring level | timer and focus rings | `TrimEnd` glide 240 ms (Level) |
| Play pulse | media play disc on play/pause | 90 % and back in 260 ms, once |
| Mute dip | volume glyph | Swap dip |

## 7. Visual acceptance checklist

Checked on the running app (Debug and Release builds, 1920 × 1080 at 125 %, 120 Hz) with screenshots taken through
`CopyFromScreen`:

| Check | Result |
|---|---|
| Compact: flush top, shoulders, date and time baseline, timer ring + text | PASS (dark and light) |
| Expanded clock, Pomodoro, To-do (empty and with tasks), Shelf (empty and with items), Timer | PASS |
| Alerts: charging (green), offline (orange), critical battery (red); camera held behind the critical alert | PASS |
| Page slide captured mid-transition (90 ms) | PASS |
| Dots: capsule and dots crisp (a clipping defect was found and fixed) | PASS after fix |
| Chip labels: "Add 1 minute" / "Cancel timer" were truncated; now sized to fit, with icon-only fallback | PASS after fix |
| Light theme | PASS |
| High contrast | NOT RUN visually: turning on high contrast is a machine-wide accessibility change. Covered by `DesignTokenTests` (system highlight for every tone, flat surface, full outline) |
| Media page with a real session | NOT RUN visually: no media was playing during the check. Covered by desktop tests and unit tests |
| Pointer clicks on chips | Covered by the desktop suite (FlaUI). Input injected from the measurement shell did not reach the window, so it was not used |

Representative screenshots are in [`visual/`](visual/). They are masked to the island's shape, so nothing from the desktop
behind it is kept.

## 8. Performance

Both runs are SMOKE results from `harness app-check` (120 Hz, same machine and method), not budget verdicts. No budget
in ADR-001 §9 was changed.

| Measurement | Before `APP-SMOKE-120-161942` | After `APP-SMOKE-120-165727` | Budget (ADR §9.3) |
|---|---|---|---|
| Validity | valid | valid | — |
| Start → first frame p50 | 284.6 ms | 310.4 ms | PB-09 ≤ 1.0 s warm |
| Idle 90 s CPU | 15.6 ms | 15.6 ms | PB-01 ≤ 0.5 s / 10 min |
| Idle attributed frames | 1 (1 clock update) | 2 (2 clock updates: the window spanned two minute boundaries) | PB-03: owner decision E2 |
| Private / working set | 21.3 / 82.3 MB | 24.2 / 87.1 MB | PB-10 ≤ 120 MB |
| Transitions (expand/collapse) | 20/20 valid; 2 with one long frame; p95 8.8 ms; max 16.8 ms | 20/20 valid; 1 with one long frame; p95 8.9 ms; max 16.5 ms | PB-05 (formal run needed) |
| Exit p50 | 77.9 ms | 74.9 ms | — |

Findings:
- Start-up is about 26 ms slower: the island now creates its page groups, rings and control slots up front, about
  60 composition objects and their text surfaces. It is still far under PB-09.
- Memory grew by about 3 MB private. That is the extra text surfaces, and they are allocated once.
- Idle behaviour is unchanged. Nothing new runs while settled: no timers, no polling, no per-frame managed code. The
  shoulders and rings animate in DWM.
- **Not measured:** frame pacing of the page slide and the ring glide. The harness only marks expand/collapse
  transitions, so their smoothness is only a visual observation, not a pass. 60 Hz has no valid result.

Tests on the final code: unit **533/533** (Core 440, App 14, spikes), island desktop **86/86**, spike desktop **5/5**,
clean Release build with 0 warnings. The desktop suites must run outside the command sandbox: inside it, `SendInput`
moves no pointer. The same test fails on the unchanged `b1712b1` inside the sandbox and passes outside it.

## 10. Screenshots

Files in [`visual/`](visual/), masked to the island's shape at 125 % scaling:

| File | Shows |
|---|---|
| `before-compact.png`, `before-expanded.png` | baseline (harness backdrop) |
| `after-harness-compact.png`, `after-harness-expanded.png` | the same harness views after the change |
| `compact-dark.png`, `compact-light.png`, `compact-timer.png`, `compact-focus.png` | compact pill: date, light theme, timer ring, focus ring |
| `page-clock.png`, `page-pomodoro.png`, `page-todo.png`, `page-todo-light.png`, `page-shelf-empty.png`, `page-shelf-light.png` | feature pages |
| `activity-timer.png`, `activity-focus.png` | live activities with rings and chips |
| `alert-charging.png`, `alert-offline.png`, `alert-battery-critical.png` | tone-tinted alerts |
| `motion-page-slide.png` | a page change 80 ms in: the body sliding and the header title dipping |

## 9. Limitations and deferred work

- **Water reminder:** shown in the reference screenshots, but Halo has no such feature. Not built, because the campaign
  rule is not to invent functionality. **Owner decision.**
- **Microphone indicator:** still no supported API (unchanged).
- **High contrast** and a **real media session** were not checked by eye (§7).
- **Chip width** is a conservative estimate in Core (DirectWrite measures only on the UI thread). Very wide localised
  labels fall back to icon-only rather than being measured exactly.
- **Light theme accents** are darkened to reach 3:1 on the light surface, so they are less vivid than on black.
- **60 Hz:** no valid formal result (the display runs at 120 Hz; display modes are reserved for the owner).
