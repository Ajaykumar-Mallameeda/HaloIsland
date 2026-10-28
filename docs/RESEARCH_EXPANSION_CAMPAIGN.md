# Research — Product Expansion Campaign (2026-10-29)

Time-boxed research for the expansion campaign (accessibility, motion, activities, notifications, integrations).
Primary sources only, unless a row says otherwise. "Supported" means the publisher documents it; "inferred" means
visual or behavioural inference that no source states.

## 1. Sources

| # | Source | Publisher | Feature | What it establishes |
|---|---|---|---|---|
| S1 | [Live Activities — Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/live-activities) (read through Apple's documentation JSON, the page itself is script-rendered) | Apple | Activity presentations, motion | Compact / minimal / expanded presentations; animations "with a maximum duration of two seconds"; "update a Live Activity only when new content is available"; "alert people only for essential updates"; prefer one activity that rotates through events; end immediately; avoid sensitive information; heavier text weights |
| S2 | [View Live Activities in the Dynamic Island](https://support.apple.com/en-in/guide/iphone/iph28f50d10d/ios) | Apple | User-facing behaviour | Page did not render for the fetch tool (script content); not used as evidence |
| S3 | [UiaRaiseNotificationEvent](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraisenotificationevent) | Microsoft | A-F01 | Windows 10 1709+; a `WS_POPUP` window "must also implement the Window Control Pattern and handle WM_GETOBJECT" |
| S4 | [Window control pattern](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-implementingwindow) | Microsoft | A-F01 | Required `IWindowProvider` members |
| S5 | UIAutomationCore.h `ProviderOptions` (documented values: `ClientSideProvider = 0x1`, `ServerSideProvider = 0x2`, `UseComThreading = 0x20`) | Microsoft | A-F01 | Halo declared `ServerSideProvider = 0x1`; see §3 |
| S6 | [App notifications quickstart](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart) (updated 2026-09-10) | Microsoft | D-F02, L-F01 | Windows App SDK `AppNotificationManager`; activation needs `AppNotificationManager.Register` and a COM activator (manifest `windows.toastNotificationActivation` + `windows.comServer` for packaged apps); not supported for elevated apps; when the app is not running Windows launches it by COM activation |
| S7 | [Microsoft Graph permissions reference](https://learn.microsoft.com/en-us/graph/permissions-reference) | Microsoft | E-F02, F-F01 | `Mail.ReadBasic` (delegated, no admin consent): mail "except body, previewBody, attachments and any extended properties"; `Calendars.ReadBasic` (delegated, no admin consent): events "except for properties such as body, attachments, and extensions"; `offline_access` (delegated) for refresh tokens |
| S8 | [MFCreateSensorActivityMonitor](https://learn.microsoft.com/en-us/windows/win32/api/mfidl/nf-mfidl-mfcreatesensoractivitymonitor) | Microsoft | C-F08, K | A documented Media Foundation callback API for **camera sensor** activity reports (`IMFSensorActivitiesReport`, per-process streaming state). No equivalent documented API for microphone use was found |
| S9 | Community search results on camera/microphone detection (Microsoft Q&A threads, GlassWire blog) | Various | C-F08, K | Not evidence. They show that common tools read `CapabilityAccessManager\ConsentStore` registry keys, which Microsoft does not document as an API, and that at least one vendor removed the feature after Windows changes |
| S10 | Existing project research: ADR-001 §7, PRODUCT_REQUIREMENTS §3, SECURITY_AND_PRIVACY_DESIGN §4 | Project | All | `UserNotificationListener` needs package identity (O-F01); GSMTC, Core Audio, power and network APIs already in use |

## 2. Design implications

### 2.1 Apple interaction patterns → Halo (original design, not a copy)

| Pattern (S1) | Status | Halo decision |
|---|---|---|
| Compact presentation for the one active activity | Supported | One primary activity owns the compact pill |
| Minimal presentation when several are active | Supported | Halo shows the primary activity plus a small count/indicator of the others; it does not split the pill (Windows has no camera cut-out to wrap around) |
| Expanded presentation on touch-and-hold | Supported | Hover-intent or click (existing FR-ISL-03/04); no long-press |
| Animations ≤ 2 s; use them to reinforce information | Supported | Halo's motion vocabulary stays ≤ 450 ms per transition (MOTION_SYSTEM.md) |
| Update only when content changes | Supported | Coalescing and "no redraw if equal" rules (already in IslandVisuals; extended to activities) |
| Alert only for essential updates | Supported | Alerts are priority-gated; repeated identical alerts are coalesced; quiet mode suppresses non-critical ones |
| Prefer one activity rotating through events over many | Supported | Activity switching is explicit (user) or priority-driven, never random |
| End immediately when the task ends; custom dismissal | Supported | Activities have optional expiry and are removed when their source ends |
| Avoid sensitive information; offer redaction | Supported | Per-provider preview privacy; private-by-default previews |
| Spring-like "morph" of the shape | Inferred (visual) | Halo uses its measured 350 ms cubic-bezier morph (PB-05 evidence from S1/S2); no spring physics |

### 2.2 Windows integrations

| Integration | API | Packaging needed | App must run | Consent / account | Network | Personal data | Denied → | Testable without account | Supported for Halo's architecture |
|---|---|---|---|---|---|---|---|---|---|
| Screen-reader announcements | UIA provider + `UiaRaiseNotificationEvent` (S3, S5) | No | Yes | None | No | What Halo shows | n/a | Yes (UIA client in tests) | **Yes — implemented (A-F01)** |
| In-island notifications from Halo's own features | Halo activity model | No | Yes | None | No | Timer/reminder/task text the user typed | n/a | Yes | **Yes** |
| Windows app notifications (toasts) sent by Halo | Windows App SDK `AppNotificationManager` (S6) or `Windows.UI.Notifications` with a registered AUMID | Windows App SDK: no package required, but adds the WinAppSDK runtime dependency; packaged apps declare the COM activator in the manifest | Sending: yes; activation can relaunch the app | None | No | Text Halo chooses | Notifications off in Windows → nothing shown | Partly (sending; not the user's click) | Possible, **not built**: needs a new framework dependency or AUMID/COM registration in the user's registry. Halo's own island is the primary surface, so no toast path was added (owner decision L-1) |
| Reading other apps' notifications | `UserNotificationListener` | **Yes** (package identity + capability) | Yes | User consent prompt | No | **High** (all notification text) | Silent empty list | No | **No** while Halo is unpackaged (O-F01 stays BLOCKED) |
| Outlook mail | Microsoft Graph, delegated `Mail.ReadBasic` (S7) | No | Yes (polling or change notifications while running) | Microsoft account sign-in + app registration (client ID) | **Yes** | Sender, subject, received time | 401/403 → provider shows "Not connected"/"Permission denied" | Yes, with recorded JSON fixtures and a fake token source | **Yes in design**; live use BLOCKED on an app registration and the owner's sign-in |
| Outlook calendar | Graph delegated `Calendars.ReadBasic` (S7) | No | Yes | As above | Yes | Subject, start/end, location, online-meeting URL | As above | As above | As above |
| Local reminders, tasks, timers | Halo JSON stores + monotonic scheduler | No | Yes (missed items shown on next start) | None | No | User-entered text | n/a | Yes | **Yes** |
| Media session switching / seek | GSMTC `GetSessions`, `TryChangePlaybackPositionAsync` | No | Yes | None | No | Listening activity (memory only) | Session rejects → transient "not supported" | Synthetic + local MediaPlayer session | **Yes** |
| Global shortcut | `RegisterHotKey` | No | Yes | None | No | None (no keystrokes observed) | Conflict → reported, user rebinds | Yes | **Yes**, no keyboard hook |
| Camera activity | Media Foundation sensor activity monitor (S8) | No | Yes | None | No | Which processes stream a camera | n/a | Only with a real camera streaming — Halo must never start one | **Documented; not built** (privacy gate, §4) |
| Microphone activity | No documented API found (S9 is not evidence) | — | — | — | — | — | — | — | **BLOCKED** |
| Brightness | WMI `WmiMonitorBrightness` (internal panels only) | No | Yes | None | No | None | External monitors: not supported | Only on a laptop panel | Not started in this campaign |
| Bluetooth headset battery | No general documented API for classic Bluetooth headset battery level | — | — | — | — | — | — | — | Not started; battery must never be inferred |

## 3. A-F01 root cause (evidence summary)

- **Symptom (M2-F05):** every UIA event from the island returned `S_OK` and reached no client.
- **Controls used:**
  - a real cross-process client (FlaUI UIA3, MTA) **did** receive notification events from Windows Calculator, WinForms, and WPF (13/13 each);
  - a plain Win32 window that used Halo's provider code received none, so window styles were not the cause.
- **Bisection inside one process:**
  - WPF's managed provider types worked;
  - a minimal provider written with Halo's own COM declarations failed.
- **Cause:** Halo declared `ServerSideProvider = 0x1`. That value is `ClientSideProvider` in UIAutomationCore.h, so UIA treated the island as a client-side proxy and dropped the events it raised.
- **Result of the one-value fix:** Halo's real provider delivered 12/12 notifications, including with the island's exact window style.
- **A second defect found by the same trace:** UIA calls providers on its own threads, not the island's UI thread, but the old provider read window fields and drove the controller from whichever thread called it. The new provider reads only an immutable snapshot and posts commands to the UI thread.

## 4. Unresolved questions and owner decisions

| # | Question | Recommendation |
|---|---|---|
| L-1 | Package Halo (MSIX) to unlock reading other apps' notifications, toast activation and Start-with-Windows? | Defer; Halo's in-island notifications cover Halo's own sources |
| E-1 | Register an Entra ID app (public client, delegated `User.Read Mail.ReadBasic Calendars.ReadBasic offline_access`) and sign in, so the Graph providers can run live? | Owner action; nothing in the repository needs a secret (public client + PKCE) |
| K-1 | Enable a camera-in-use indicator backed by the Media Foundation sensor activity monitor? It reveals which processes use a camera | Off by default; build only after the owner accepts the disclosure |
| — | Narrator by ear | A person must listen: automated tests prove delivery to a UIA client, not speech |
