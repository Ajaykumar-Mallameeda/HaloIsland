# Packaging Feasibility (L-F01)

| Field | Value |
|---|---|
| Status | 2026-10-29: owner approved a **local development MSIX** signed with a self-signed certificate (§5). Release signing and the public name are still open (L-1) |
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

- **L-F02 (implement the chosen path): PARTIAL.** A development MSIX is built and signed (§5). Release identity and
  signing remain owner decision L-1.
- **L-F03 (clean install and uninstall test): WAITING ON OWNER.** Install needs one elevated step (§5); a clean machine
  or VM is still decision D7.
- **What was verified for the unpackaged build in this campaign:** launch and exit, single instance, tray icon
  removed on exit, no orphaned process after the desktop suites (final verification in PROJECT_STATUS §9).

## 5. Development MSIX (owner-approved 2026-10-29)

Scripts in `packaging/`; all output goes to `artifacts/msix/` (ignored by Git; no package, certificate or key is
committed).

| Script | What it does |
|---|---|
| `build-msix.ps1` | `dotnet publish` (Release, framework-dependent with the x64 apphost, no runtime identifier so the lock files stay valid: needs the .NET 10 Desktop Runtime); draws the logo PNGs at build time; fills `Package.appxmanifest`; `makeappx pack`; signs with a self-signed code-signing certificate `CN=ProjectHalo Dev` kept in `Cert:\CurrentUser\My` (private key never exported); exports the public `.cer` |
| `install-dev.ps1` | **Elevated.** Imports the `.cer` into `Cert:\LocalMachine\TrustedPeople`, then `Add-AppxPackage` |
| `uninstall-dev.ps1` | `Remove-AppxPackage`; with `-RemoveCertificate` (elevated) also removes the dev certificate from all three stores. Leaves `%LOCALAPPDATA%\ProjectHalo` alone |

- **Manifest:** identity `ProjectHalo.Island.Dev`, publisher `CN=ProjectHalo Dev`, `Windows.FullTrustApplication`,
  only `runFullTrust`. No startup task, notification listener or other capability yet.
- **Verified here:** package built (14 files) and signed (SHA-256); the packaged payload starts, reaches `ready`
  and exits with no process left; `signtool verify /pa` reports the chain ends in an untrusted root, as expected
  before trust.
- **Not yet verified: install.** Importing the certificate into the *user's* Trusted People store is not enough:
  `Add-AppxPackage` failed with 0x800B0109 ("root certificate … must be trusted"). The machine store needs admin
  rights, which this session does not have, so the owner runs `install-dev.ps1` once from an elevated PowerShell.
  The per-user entry was removed again.
- **To check after install (L-F03):** launch from Start, single instance, tray icon, settings persistence (a packaged
  full-trust app's new writes under AppData may be redirected to the package's private copy), uninstall leaves no
  process. A clean machine or VM (D7) is still needed for a true clean-install test.
