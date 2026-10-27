# Safety and Scope

## Product scope

An original Windows "island" application. Apple Live Activities / Dynamic Island and the Nusa app
are **behavioural references**. The product will have its own name, code, visual identity, icons
and documentation.

Internal working codename: **Project Halo** (owner decision, 2026-10-27). It is used in internal
documents only. Executable names, package identifiers and public branding are unchanged until a
name and trademark availability check selects the public product name.

## Reference-artifact safety

| Rule | Status (2026-10-27) |
|---|---|
| `NusaSetup.exe` not executed on the primary machine | Complied — static analysis only |
| Extracted payload not executed | Complied — saved as `nusa.exe.bin` |
| Original artifact unmodified | Complied — SHA-256 recorded before and after analysis |
| Extraction output outside repository | Complied — `../re-workspace/` |
| No DRM / licensing / encryption bypass | Not applicable — installer encryption flag is 0 (unencrypted) |
| Runtime analysis only in isolated VM | **Skipped by owner decision (2026-10-27).** Reconsider only with a verified reference release and a suitable isolated environment |
| Provenance of supplied installer | **Not verified** — unsigned, and its SHA-256 differs from the hash published on the website (which describes v1.0.0) |

## Third-party references

- `nazimferragh/windows-dynamic-island` and `HeyyCzer/windows-dynamic-island` are architectural
  research references only. Licenses must be checked before reading for design ideas; no code or
  assets are copied without confirmed permission.
- LRCLIB (lyrics) and any other network service require documented terms and a privacy review
  before integration.

## Repository and distribution safety

- Local Git repository only; no remotes or pushes without explicit approval.
- No secrets, credentials, proprietary binaries or raw extraction output in version control.
- No telemetry, accounts or cloud backend by default.
- Unsigned builds are never described as signed.

## Workstation safety

- No paid/commercial tools are installed or activated without approval.
- No optional native-analysis engines (Ghidra, IDA, Hopper) are installed without approval.
- System-wide software changes are proposed, not performed silently. Approved so far: Node.js
  24.11.1 → 24.22.1 (2026-10-27). Deferred: .NET 10 SDK (until the Phase 3 ADR selects it).
- Agent configuration changes (e.g. REA's MCP registration in `~/.claude.json`) are shown as a
  dry-run plan and applied only after explicit owner approval.
