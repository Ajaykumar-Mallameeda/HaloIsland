# Halo Motion System (B-F01)

| Field | Value |
|---|---|
| Status | Implemented in `Halo.Core/Motion` (planner) and `Halo.App/Island/IslandVisuals` (executor), 2026-10-29 |
| Sources | RESEARCH_EXPANSION_CAMPAIGN §2.1 (Apple HIG Live Activities, S1); PROTOTYPE_S1_S2_REPORT (PB-05 evidence for the 350 ms morph) |

An original motion vocabulary for a Windows island. It borrows the *principles* in Apple's guidance:
- motion reinforces information;
- animations stay short (Apple's ceiling is 2 s, and Halo stays far below it);
- update only when content changes;
- preserve layout while morphing.

It does not reproduce iOS system animations.

## 1. Vocabulary

Only these motions exist. A feature that needs movement uses one of them, and no feature adds its own.

| Motion | Used for | Duration | Delay | Curve | Reduced motion |
|---|---|---|---|---|---|
| **Morph** | The shape's size, position and corner radius between compact, alert and expanded | 350 ms | 0 | `ease-out` cubic (0.33, 1) (0.68, 1) | Instant (1 ms) |
| **Fade-out** | Outgoing content | 110 ms | 0 | same | 150 ms fade, no delay |
| **Fade-in** | Incoming content after a morph | 200 ms | 150 ms (expand), 170 ms (to compact/alert) | same | 150 ms fade, no delay |
| **Settle** | Expanded content drops 8 DIP into place | 320 ms | 60 ms | same | none |
| **Swap** | The same presentation with new content (a new alert replaces another, a track changes) | 90 ms out + 160 ms in | in after 90 ms | same | instant |
| **Level** | A bar or ring value changes (volume, battery, timer, media progress on a seek) | 240 ms | 0 | same | instant |
| **Emphasis** | The last 10 s of a timer: one gentle brightness step, never flashing | 400 ms, once per second | 0 | same | none (the text says "seconds left") |
| **Arrival** | A completion (timer done) or a new activity: the pill grows 4 % and returns once | 260 ms | 0 | same | none |
| **Feedback** | Hover fill/edge colour; press scale 96.5 % (island) and 94 % (chips, chevrons, tiles); one 260 ms pulse of the media play disc on play/pause | 140 ms / 90–180 ms | 0 | same | press scale and pulse off |
| **Page slide** (VX 2.0) | The open island's body on a page change: from ±28 DIP (direction of the step) and transparent to in place; the page dots morph; the header title and glyph Swap | 250 ms offset, 160 ms fade; dots 220 ms | 0 | same | none (instant) |

Longest sequence: morph plus fade-in, 370 ms. No motion repeats without a new event, except Emphasis, which runs at most 10 times (one per second).

## 2. Dimensions (DIP, scaled by monitor DPI)

| Shape | Width × height | Corner radius |
|---|---|---|
| Compact | 236 × 36 | 18 (pill) |
| Alert / activity pill | 320 × 42 | 21 (pill) |
| Expanded | 460 × 196 (+40 with the volume row) | 34 |

Radius is capped at half the height, so compact and alert shapes are exact pills at any scale.

Visual Experience 2.0 ([VISUAL_EXPERIENCE_2.md](VISUAL_EXPERIENCE_2.md)): every shape is flush with the top of the work area
(no gap). The drawn rectangle starts above the window by its radius, so only the lower corners show, and 10 DIP concave
shoulders follow the shape through every morph (expression animations, no managed code per frame).

## 3. Rules

1. **State owns motion, never the reverse.** The interaction controller decides the presentation. The planner turns
   a *change* into a plan, and completion callbacks only end the plan they belong to (transition IDs). A callback
   from an older plan is ignored.
2. **Interruption starts from where the shape is.** A new plan replaces the running animations on the same
   properties. Composition starts from the current animated value, so there are no jumps. There is at most one plan
   per channel (shape, content, level), so a burst of events cannot stack animations.
3. **Coalescing.** A plan whose target equals the current target is not started. Equal content is not redrawn.
4. **No idle rendering.** Every motion is finite. Nothing runs while settled, hidden, locked or with the display
   off (FR-ISL-12). Emphasis is the only timed repetition, and it stops at zero.
5. **Full-screen suppression wins.** While the window is hidden, nothing is planned or drawn. When the window is
   shown again, it shows the current state without replaying missed motions.
6. **Reduced motion** (the Windows "Animation effects" setting or Halo's setting): shape changes are instant, content
   uses short fades, and Level, Settle, Emphasis and Arrival are off. Information never depends on motion: every
   value is also text, and every activity is announced through UI Automation.
7. **High contrast / colour:** state is never shown by colour alone. Emphasis changes brightness and the text says
   "seconds left". The low-battery warning uses a glyph and words.
8. **60 Hz and 120 Hz:** DWM runs the animations at the display's rate. Durations are times, not frame counts.
   PB-05 was measured at 120 Hz only; 60 Hz has no valid formal result yet (PROJECT_STATUS open items).
9. **Performance limits:** at most 3 concurrent composition animations per channel. No per-frame managed code.
   Text surfaces are redrawn only when their text changes.
