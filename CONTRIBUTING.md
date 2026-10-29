# Contributing to Halo

Thank you for helping improve Halo. This guide covers how to set up, build, test and submit a change.

By taking part you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). By submitting a contribution you agree
that it is licensed under the project's [MIT License](LICENSE).

## Before you start

- For anything larger than a small fix, please open an issue first, so we can agree on the approach before you
  invest time.
- Security problems must **not** be reported in public issues. See [`SECURITY.md`](SECURITY.md).

## Prerequisites

- Windows 11 (Windows 10 version 2004 or later should work but is untested), x64.
- .NET SDK **10.0.403** or a later 10.0.4xx patch (pinned in [`global.json`](global.json)).
- Git.
- Optional, only for the development MSIX: Windows SDK 10.0.26100.

## Setup and build

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'

git clone https://github.com/Ajaykumar-Mallameeda/HaloIsland.git
cd HaloIsland
dotnet restore Halo.slnx --locked-mode
dotnet build Halo.slnx -c Release --no-restore
```

- The build treats **warnings as errors** and enforces the code style in [`.editorconfig`](.editorconfig).
- Package versions are managed centrally in [`Directory.Packages.props`](Directory.Packages.props), and restores are
  locked (`packages.lock.json`). If you change a dependency, run `dotnet restore Halo.slnx --force-evaluate` and commit
  the updated lock files.
- New dependencies must be permissively licensed (MIT, Apache-2.0, BSD or similar) and come from nuget.org. Explain
  why the dependency is needed in the pull request.

## Testing

```powershell
# Unit tests: required for every change
dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"

# Desktop tests: required for changes to Halo.App behaviour. Interactive desktop only;
# they move the mouse, so do not touch the machine while they run. Run the suites one at a time.
dotnet test --project tests/Halo.App.Tests/Halo.App.Tests.csproj -c Release -- --filter-trait "Category=Integration"
dotnet test --project spikes/tests/Halo.Spikes.Tests/Halo.Spikes.Tests.csproj -c Release -- --filter-trait "Category=Integration"
```

- Put logic in `Halo.Core` where possible, behind injectable clocks and interfaces, and cover it with unit tests that
  use virtual time.
- Tests that need a real desktop must carry `[Trait("Category", "Integration")]`.
- Do not weaken, skip or delete a test to make a change pass. If a test is wrong, explain why in the pull request.
- Report test results honestly in the pull request, including anything you could not run.

## Coding conventions

- C# with nullable reference types enabled; follow `.editorconfig` and the style of the surrounding code.
- `Halo.Core` must stay free of Windows-specific APIs, P/Invoke and UI code.
- Nothing may poll or render continuously while Halo is idle. Use events and one-shot timers.
- Respect Reduce Motion for every new animation.
- New UI must be reachable through UI Automation, with a meaningful name.
- Keep comments short and useful.

## Commit messages

Use [Conventional Commits](https://www.conventionalcommits.org/) (template: [`.gitmessage`](.gitmessage)):

```
feat: add a countdown preset to the Pomodoro page
fix: keep the pill on the primary monitor after a DPI change
docs: explain the shelf storage limits
test: cover reminder snooze across a DST change
```

Keep each commit focused on one change, and write the body to explain *why*.

## Pull requests

- Describe what changed and why, and link the related issue.
- List the test commands you ran and their results.
- The build must have zero warnings and all unit tests must pass. CI checks this.
- Update the documentation when behaviour, settings or commands change.
- Keep pull requests small enough to review.

### Screenshots for UI changes

Any change that affects what Halo looks like must include **before and after screenshots** (or a short recording for
motion changes). Check both the dark and light themes when relevant.

Capture only the island. Screenshots must not show personal information: crop away the desktop, other apps, file
names, notification text, email addresses, account names and similar content. Use made-up sample data for tasks and
reminders.

## Privacy and security requirements

Halo is local-first. Contributions must keep it that way:

- No telemetry, analytics, crash upload or other network calls without a prior design discussion and an explicit,
  off-by-default opt-in.
- Never log user content: task or reminder text, media titles, file names, notification content, device or app
  names. Diagnostics carry event names, counts and fixed tokens only.
- Store data only under `%LOCALAPPDATA%\ProjectHalo`.
- Privacy-sensitive indicators (such as the camera indicator) must be off by default and use documented Windows APIs.
  No process-name heuristics or undocumented registry keys.

### Never commit

- Secrets of any kind: API keys, tokens, passwords, connection strings, `.env` files.
- Signing material: `.pfx`, `.p12`, `.snk`, `.pem`, `.key` files, or exported certificates.
- Built packages or binaries: `.msix`, `.appx`, `.exe`, `.dll`, installers, or the `artifacts/` folder.
- Personal data: diagnostic logs, your `settings.json`, `tasks.json` or `shelf.json`, and screenshots or recordings that
  show personal content.
- Code, assets or icons copied from other products, or anything you do not have the right to license under MIT.

`.gitignore` blocks most of these, but please check `git status` and the staged diff before every commit. If you
accidentally commit a secret, report it privately as described in [`SECURITY.md`](SECURITY.md) and revoke it right
away. Deleting it in a later commit does not remove it from history.
