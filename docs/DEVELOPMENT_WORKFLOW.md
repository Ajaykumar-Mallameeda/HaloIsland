# Development Workflow

## 1. Repository policy

- The repository is **local only**. No GitHub repository, remote, push or public release is created
  without the owner's explicit approval.
- Before any future public push: audit tracked files, scan for secrets, confirm licensing of all
  dependencies and assets, remove generated artifacts, and review the reverse-engineering reports
  for content that should not be published.
- Existing commits are never amended, rebased, rewritten or deleted without explicit approval.
- Reference binaries (`NusaSetup.exe`, extracted payloads) live **outside** the repository in
  `../re-workspace/` and are additionally blocked by `.gitignore`.

## 2. Commit convention — Conventional Commits

Format: `<type>: <imperative summary>` (optionally `<type>(<scope>): ...`). Summary ≤ 72 characters,
lowercase type, no trailing period. Add a body when the *why* is not obvious.

| Type | Use for |
|---|---|
| `docs:` | Documentation and research |
| `chore:` | Tooling and repository configuration |
| `feat:` | New functionality |
| `fix:` | Bug fixes |
| `test:` | Tests and test infrastructure |
| `refactor:` | Behaviour-preserving code improvements |
| `perf:` | Verified performance improvements (include measurement) |
| `build:` | Build and packaging changes |
| `ci:` | Continuous integration changes |

Examples:

```
chore: initialize Windows island project
docs: add Nusa reverse engineering audit
feat: implement adaptive island state machine
test: cover activity prioritization
fix: prevent overlay focus stealing
```

A commit message template is configured locally (`git config commit.template .gitmessage`).

## 3. Milestone workflow (every meaningful change)

1. Inspect the working-tree state (`git status`).
2. Define the scope of the change — one logical unit.
3. Implement a small, logically complete change.
4. Run the relevant validation (see §4).
5. Inspect the diff and the staged file list (`git diff --staged --stat`, `git diff --staged`).
6. Remove accidental generated files and any sensitive content from staging.
7. Commit only after review.
8. Report: commit hash, message, changed files, validation results (actual output).
9. Identify any uncommitted work that remains.

Rules:

- Never write a commit message or report that implies tests passed when they were not run.
- Never bundle unrelated features into one commit.
- No empty commits to inflate history.
- No secrets, credentials, tokens or proprietary reference binaries in any commit.

## 4. Validation requirements

| Change type | Minimum validation before commit |
|---|---|
| Documentation only | Links/paths resolve; staged file review; no secrets |
| Repository config | `git status` clean of unintended files; `.gitignore` checked with `git check-ignore -v` for sensitive paths |
| Code (from Phase 4) | `dotnet build` with zero errors; `dotnet test` for affected projects; analyzers/warnings reviewed |
| Performance claims | Reproducible measurement script + recorded conditions |
| Packaging | Clean install / uninstall on a clean environment |

Test results quoted in commits, reports and `docs/PROJECT_STATUS.md` must come from actual execution.
If a check could not be run, say so explicitly ("NOT RUN — reason").

## 5. Sensitive-file checklist before staging

- [ ] No `*.exe`, `*.dll`, `*.msi`, `*.msix` or extraction directories
- [ ] No `.env`, keys, certificates (`*.pfx`, `*.pem`, `*.key`), tokens or `ida-mcp.json`
- [ ] No local databases (`*.db`, `*.sqlite`) or logs
- [ ] No IDE/user-specific files (`.vs/`, `*.user`, `.claude/settings.local.json`)
- [ ] No verbatim proprietary content copied from reference products (strings dumps of
      proprietary binaries are summarised, not committed wholesale)

## 6. .NET telemetry opt-out (owner decision Q8, 2026-10-27)

The .NET SDK and Microsoft.Testing.Platform (the xUnit v3 runner) send usage telemetry by default.
Microsoft documents only environment variables as the opt-out, so they are set **permanently for
the Windows user** (not machine-wide, no other privacy setting changed):

| Variable | Value | Location |
|---|---|---|
| `DOTNET_CLI_TELEMETRY_OPTOUT` | `1` | `HKCU\Environment` (user environment) |
| `TESTINGPLATFORM_TELEMETRY_OPTOUT` | `1` | `HKCU\Environment` (user environment) |

Set with `[Environment]::SetEnvironmentVariable(<name>, '1', 'User')`, which writes the user
environment and broadcasts `WM_SETTINGCHANGE` so newly started programs pick it up. Neither
variable existed before (user, machine or process scope).

**Verification (without sending telemetry):**

```powershell
Get-ItemProperty HKCU:\Environment | Select-Object DOTNET_CLI_TELEMETRY_OPTOUT, TESTINGPLATFORM_TELEMETRY_OPTOUT
```

On 2026-10-27, a `.cmd` script started through Explorer, which behaves like a newly opened terminal,
printed both variables as `1`.

**Limitations:**
- Terminals that were already open keep their old environment until restarted.
- The opt-out cannot undo the single SDK usage event that may have been sent by the first .NET 10
  command in Phase 4 (ENVIRONMENT_AUDIT §2).
- Machine-wide or other-user settings are unchanged.
- The untrusted ASP.NET development certificate and the installed SDKs were left as they are.
