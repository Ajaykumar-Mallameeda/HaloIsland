# Security and Privacy Design — Project Halo

| Field | Value |
|---|---|
| Status | **PROPOSED** (Phase 3) |
| Date | 2026-10-27 |
| Related | ADR-001 (`ARCHITECTURE_DECISION_RECORD.md`), `PRODUCT_REQUIREMENTS.md`, `SAFETY_AND_SCOPE.md` |

## 1. Principles

1. **Local-first:** all data stays on the device; no accounts, telemetry, analytics or crash upload.
2. **Least privilege:** runs as the standard user (`asInvoker`), no elevation, no services, no drivers.
3. **Opt-in for anything sensitive:** network access, input monitoring and notification reading are
   off by default and explained before enabling.
4. **Observe, don't interfere:** integrations read system state through documented APIs and never
   modify other apps (cf. reference defects E-098, E-125).
5. **Untrusted input is handled defensively:** media metadata, notification text, dragged data and
   network responses come from other software.

## 2. Assets and data inventory

| Data | Source | Stored? | Location | Retention | Sensitivity |
|---|---|---|---|---|---|
| Settings | User | Yes | `settings.json` | Until deleted | Low |
| Tasks (text, due times) | User | Yes | `tasks.json` | Until deleted | Medium (personal) |
| Timers | User | Yes | `timers.json` | Until completed + 7 days | Low |
| Water log (Adv) | User | Yes | `water.json` | 90 days rolling (configurable) | Low–medium (health-adjacent) |
| Shelf items (Adv) | User drag-drop | Paths/metadata only | `shelf.json` | Until removed | Medium (file names/paths) |
| Media metadata and art | Other apps via GSMTC | **Memory only** | — | While session active | Medium (listening habits) |
| Notification content (Adv, Cond) | Other apps via listener | **Memory only** | — | While displayed | **High** |
| Lyrics (Adv, Cond) | LRCLIB | Memory; optional session cache | — | Session | Low (third-party copyright) |
| Logs | App | Yes | `logs/` | 5 × 1 MB rolling; debug logs expire after 24 h | Low by design (§6) |
| Crash records | App | Yes | `crash/` | Last 5 | Low (no memory dumps by default) |

Data location: package `LocalFolder` (MSIX) or `%LOCALAPPDATA%\ProjectHalo\` (unpackaged dev).
Data is not encrypted at rest: it is no more sensitive than other per-user app data, and the user
profile is the protection boundary (BitLocker/device encryption is the user's control). This choice
is revisited if notification content is ever persisted (it is not, by design).

## 3. Trust boundaries and threat model (STRIDE-lite)

| Boundary | Threat | Mitigation |
|---|---|---|
| Other apps → media metadata/art | Malformed or huge strings/images (DoS), spoofed content | Length caps (title/artist ≤ 512 chars), image size cap (e.g., 4 MB, ≤ 1024 px decode), decode off UI thread with timeout, never interpret as markup |
| Other apps → notification text (Adv) | Phishing-style content shown in a trusted-looking surface; privacy leak on screen share | Clear source attribution (app name + icon from `AppInfo`); per-app allowlist; auto-hide during presentation mode/full screen; never actionable links in v1 |
| Dragged data → shelf (Adv) | Malicious files, path tricks, URL shortcuts | Store references only; never execute or open automatically; open only on explicit click via `ShellExecute` default verb with the user's normal SmartScreen/Mark-of-the-Web protections; display full path on hover |
| Network → lyrics (Adv) | MITM, malformed responses, large payloads | HTTPS only with default certificate validation; host allowlist (`lrclib.net`); 5 s timeout; 256 KB response cap; strict JSON parsing; LRC parser fuzz-tested |
| Local files → app | Corrupt or crafted JSON (same-user tampering is not a security boundary) | Schema validation, size caps, recovery (ADR §8); never deserialise types polymorphically |
| Process → OS | DLL search-order hijacking | MSIX install location is protected; call `SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)` at startup for unpackaged runs; no `LoadLibrary` of relative names |
| Process → input (Adv, opt-in) | Keylogging perception/risk, AV flags (E-094) | Prefer hook-free techniques (spike S6); if a low-level hook is ever used: opt-in with explanation, only virtual-key codes for Caps Lock/IME-related keys inspected, nothing stored or logged, hook removed when the feature is off |
| IPC (single instance) | Another local process sends commands | Accept only a fixed "activate/open settings" message; ignore payloads |
| Updates/distribution | Tampered installer | MSIX signed by Store (or our certificate); hashes published by the release script; no unsigned public builds |
| Supply chain | Malicious/compromised NuGet packages | Minimal dependencies; `packages.lock.json` with locked mode in release builds; NuGet package source mapping to nuget.org; review licence and maintainer for each new package; SBOM at release |

## 4. Permissions and capabilities

| Capability | Needed by | Default | User control |
|---|---|---|---|
| `runFullTrust` (MSIX desktop app) | Whole app | Required | — |
| Startup task | FR-APP-03 | Off until user enables (or first-run prompt) | Windows Settings › Startup apps |
| Internet (client) | Lyrics (Adv) only | Feature off | In-app toggle |
| User Notification Listener | FR-NTF-01 (Adv) | Feature off | In-app toggle + Windows consent prompt + Windows privacy settings |
| Media session access (`globalMediaControl` listed in docs) | FR-MED | On | In-app toggle |
| Input monitoring (no capability exists) | FR-SYS-06/07 (Adv) | Off | In-app toggle with explanation |

## 5. Network policy

- Default build makes **no** network connections (PB-13 verifies).
- Allowed hosts are compile-time constants per feature; no redirects to other hosts.
- User-Agent identifies the app name and version only; no device identifiers.
- Lyrics disclosure (exact fields, e.g., title, artist, album, duration) is shown next to the toggle
  and in the privacy policy; the request format is decided in the lyrics milestone.

## 6. Logging and diagnostics privacy

- Information level: lifecycle, module states, error codes, durations. **Never**: media titles,
  artists, task text, file names/paths, notification content, window titles of other apps.
- Debug level (opt-in, auto-expires in 24 h, labelled in UI) may include content to diagnose issues.
- Crash records contain exception type, message, stack trace and module states; exception messages
  are passed through the same redaction filter. Full memory dumps only on explicit user action.
- Nothing is uploaded; the user may attach logs to a bug report manually.

**As built (M2-F03, 2026-10-28):**

- The log is **stricter than the design above**. `Halo.Core/Diagnostics/LogFile` accepts only numbers, booleans
  and tokens (1–64 characters of `A–Z a–z 0–9 _ . -`). Any other string, including exception messages and paths,
  is written as `redacted`, so free text cannot reach the file by construction.
- Crash records hold the exception type name and HRESULT only, with no message and no stack trace.
- There is no debug level yet.
- Location: `<data folder>\logs\halo.log`, rolled at 1 MB into 5 files at most.
- Each event name is limited to 20 lines a minute, and the overflow is counted in one `log.suppressed` line.
- Write failures are dropped and counted, and never stop the island.
- Settings › **Open log folder** shows the folder in File Explorer.

**As built, media (M3-F01/F02, 2026-10-29):**

- Title, artist and album art stay in memory for as long as the session is shown. They are not saved, logged or
  sent anywhere. Art comes only from the session's own thumbnail and is never downloaded: at most 4 MB, decoded
  off the UI thread to 96 × 96 px, with a 2 s timeout.
- Session text is cleaned (`MediaText.Clean`): control and format characters are dropped, whitespace is
  collapsed and the length is capped at 512 characters. It is drawn as plain text.
- Diagnostics carry tokens and counts only: state, which controls exist, title *length*, session count and
  command results. Source app IDs are never logged.
- Settings › "Show what is playing" (`showMedia`) turns the integration off completely. The session manager is
  released and no events are received.

**As built, volume (M3-F03, 2026-10-29):** Halo reads and sets only the default output device's master volume and
mute. Device IDs and names are not logged or stored; diagnostics carry percent, mute and binding status. Nothing
about audio content or other apps' sessions is read. `showVolume` off releases Core Audio entirely.

**As built, battery and network (M3-F04/F05, 2026-10-29):** battery readings are the charge level, power source
and charging flag from `GetSystemPowerStatus`. Network readings are the connectivity level and adapter type of the
internet profile. The Wi-Fi network name (profile name), addresses, traffic, DNS and browsing activity are never
read. Nothing is stored or sent; diagnostics hold state tokens only. `showBattery` and `showNetwork` turn each feed
off.

**As built, expansion campaign (2026-10-29):**

- **Data files, all versioned JSON in the data folder, written atomically with a `.bak`:**
  - `state.json`: countdown, focus session and quiet-mode schedule. No user text;
  - `tasks.json`: the user's tasks and reminders. Their text is shown in the island only with "Show private text";
  - `shelf.json`: items the user pinned. This is **text snippets as well as file paths**, which differs from §2 and
    is recorded here. Pinning happens only on an explicit button press; no clipboard listener exists. Clear all
    deletes the file and its backup.
- **Logs:** they never contain task text, shelf content, file names, media app IDs or notification content; desktop
  tests check this.
- **Graph (Outlook) adapter:**
  - delegated `User.Read Mail.ReadBasic Calendars.ReadBasic offline_access`;
  - HTTPS GET to `graph.microsoft.com` only, 10 s timeout, 1 MB cap;
  - links opened only over HTTPS to Outlook or Teams hosts.
  It is **inactive**: no token source signs in, so the default build still makes no network connection (PB-13
  unchanged). Tokens, when added, belong in Windows Credential Manager, never in files or logs (ADR-001 §8).
- **Keyboard:** the shortcut uses `RegisterHotKey` only. No hook exists and no keystrokes are seen.
- **Windows settings:** quiet mode is Halo's own. Windows' Do Not Disturb and Focus settings are never changed.
- **Camera indicator (opt-in, off by default):**
  - reads Media Foundation's passive sensor activity reports. It never opens a camera, captures frames or bypasses
    Windows' own indicator;
  - keeps only "another app is streaming" and the stream count;
  - process IDs are used once to exclude Halo and then dropped; app names are never read;
  - no history is kept.
- **Microphone use:** not monitored. No documented API exists.

## 7. Licensing, intellectual property and reverse-engineering boundary

| Topic | Position |
|---|---|
| Reference product (Nusa) | No license is published (E-009) → all rights reserved. **No code, assets, icons, sounds, text, settings vocabulary or file formats are copied.** Static findings are used only to understand behaviour and Windows API choices; our implementation is derived from Microsoft documentation and our own design |
| Trademarks | Do not use "Dynamic Island", "Live Activities", "Nusa" or Apple marks in the product name, store listing or marketing; the public name requires a trademark search (owner decision #5) |
| Lyrics | Lyrics are copyrighted by rights holders. LRCLIB's server is MIT-licensed, but data/API terms were **not** verifiable (client-rendered docs). Public release of a lyrics feature requires the owner's legal review |
| Dependencies | Permissive licences only (MIT/Apache-2.0/BSD) by default; `THIRD_PARTY_NOTICES` generated at release |
| Our licence | Not chosen (README). Must be decided before any public repository or release |
| Fonts/icons | Segoe Fluent Icons / system fonts used via the OS only; no redistribution of font files |

## 8. Distribution security

- Store MSIX: Microsoft re-signs; Store certification acts as an additional review.
- Direct download (optional): signed with Azure Artifact Signing (if eligible) or an OV certificate
  on a hardware token/HSM; never instruct users to bypass SmartScreen (contrast E-146).
- Release script produces SHA-256 for every artifact and updates release notes and any website from
  the same source (prevents the drift seen in E-003).
- Dev certificates and any `.pfx` stay outside the repository (`.gitignore`).

## 9. Privacy review checklist (release gate)

- [ ] Data inventory (§2) matches the implementation (automated test enumerates files written)
- [ ] PB-13: no network with optional features off
- [ ] Log redaction tests pass (`FR-APP-07`)
- [ ] Every opt-in feature has explanation text and a working off switch
- [ ] Notification and input-monitoring features absent from builds where not approved
- [ ] Export and delete-all-data work (`FR-APP-06`)
- [ ] Privacy policy text written (required for Store listing)
- [ ] Third-party notices complete; no GPL/LGPL code unless approved
- [ ] No reference-product material in the repository (`REVERSE_ENGINEERING_WORKFLOW.md` §6)
