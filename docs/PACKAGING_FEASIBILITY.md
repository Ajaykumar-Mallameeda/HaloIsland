# Packaging Feasibility (L-F01)

| Field | Value |
|---|---|
| Status | Report only, 2026-10-29. No package was built, installed or signed, and the installation was not changed (owner decision L-1) |
| Sources | RESEARCH_EXPANSION_CAMPAIGN §1 S6, S10; ADR-001 §11; SECURITY_AND_PRIVACY_DESIGN §4, §8 |

## 1. What packaging would unlock

| Goal | Unpackaged (today) | Needs package identity |
|---|---|---|
| Island, tray, settings, timers, reminders, tasks, shelf, themes, shortcut | **Works** | — |
| Halo's own in-island notifications and UIA announcements | **Works** | — |
| Windows toast notifications sent by Halo | Possible with the Windows App SDK `AppNotificationManager` (unpackaged support) plus a COM activator registration in the user's registry | Simpler: activator declared in the manifest |
| Reading **other apps'** notifications (`UserNotificationListener`, O-F01) | **Not possible** | Required, with the `userNotificationListener` capability and user consent |
| Start with Windows | HKCU `Run` value (FR-APP-03 dev path; not built) | `StartupTask`, which the user controls in Settings |
| Clean install, upgrade and uninstall with data rules | Copy the folder; data in `%LOCALAPPDATA%\ProjectHalo` | MSIX: data in the package's `LocalFolder`, removed on uninstall |

## 2. Options compared

| Option | Identity | Install / uninstall | Signing | Data migration | Admin rights | Effort |
|---|---|---|---|---|---|---|
| **A. Unpackaged** (today) | None | Copy or delete a folder | Optional (SmartScreen reputation) | None | No | None |
| **B. MSIX (full package)** | Yes | Clean; per-user; upgrade keeps `LocalFolder` | **Required** (Store re-signs; sideloading needs a trusted certificate) | `%LOCALAPPDATA%\ProjectHalo` → `LocalFolder` on first run | No | Medium: packaging project, manifest, icons, `runFullTrust` |
| **C. Packaged with external location** (sparse package) | Yes | The app stays an ordinary folder; a small signed package gives identity | Required for the sparse package | None | No | Medium: identity manifest, signing, registration at install time |

## 3. Recommendation

1. **Keep A** for development. Everything built in this campaign works unpackaged.
2. **When notification mirroring (O-F01) or Store distribution is wanted, choose B (MSIX).** It is the only
   option that gives a clean install, upgrade and uninstall together with the listener capability. C adds
   identity, but signing is just as hard and uninstall is less clean.
3. **Before B:**
   - decide on a code-signing route (Store, or an OV certificate on hardware or Azure Artifact Signing). This is an
     owner decision and may cost money;
   - choose the public name (trademark check, SECURITY_AND_PRIVACY_DESIGN §7);
   - build the migration from `%LOCALAPPDATA%\ProjectHalo`. The data files are versioned JSON
     (`settings.json`, `state.json`, `tasks.json`, `shelf.json`), so it is a copy.

## 4. L-F02 / L-F03 status

- **L-F02 (implement the chosen path): BLOCKED.** It needs decision L-1 (package or not) and a signing route. No
  packaging project was added, because one without a decided identity and certificate would be a placeholder.
- **L-F03 (clean install and uninstall test): BLOCKED.** It needs a package (L-F02) and a clean machine or VM
  (owner decision D7).
- **What was verified for the unpackaged build in this campaign:** launch and exit, single instance, tray icon
  removed on exit, no orphaned process after the desktop suites (final verification in PROJECT_STATUS §9).
