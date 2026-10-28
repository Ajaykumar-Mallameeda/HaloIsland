# Advanced Windows Island

Internal codename: **Project Halo**. The public product name is not chosen yet; it will be selected
after a name and trademark availability check.

An original Windows desktop application that turns the top edge of the screen into an adaptive
"island": a compact, non-intrusive surface for live activities such as media playback, timers,
reminders, file staging and system indicators.

> **Status:** Phase 4. The first island vertical slice runs: `src/Halo.App` (`Halo.exe`) shows a
> black pill at the top of the screen with a live clock that expands on click. It is built on Win32 +
> `Windows.UI.Composition`, provisionally selected after spikes S1/S2. See
> [`docs/PROJECT_STATUS.md`](docs/PROJECT_STATUS.md).

## Goals

- Native-feeling Windows desktop app (C# on .NET 10 LTS; island rendered with
  `Windows.UI.Composition`, provisionally; WPF proposed for settings — see
  [ADR-001](docs/ARCHITECTURE_DECISION_RECORD.md) §13.1, awaiting approval).
- Event-driven and lightweight while idle; performance claims backed by measurements.
- Local-first and privacy-first: no accounts, telemetry or cloud backend by default.
- Modular activity engine with independently testable feature modules.
- Accessible by keyboard and screen reader; respects reduced-motion settings.
- Credible public distribution path (installer, signing plan, update verification).

## Relationship to reference products

This project studies the **behaviour** of Apple's Dynamic Island / Live Activities and of the
third-party Windows app *Nusa* as design references. It does **not** copy their source code,
assets, icons, trademarks or branding. Reverse-engineering work is limited to static inspection
of a supplied installer for interoperability and behavioural research; proprietary binaries and
extraction output are kept outside this repository and are never committed.

## Repository layout

```
docs/                     Research, audits, workflow and status documents
src/Halo.Core/            Island layout, state machine and clock logic (no Windows dependencies)
src/Halo.App/             Halo.exe: the island window, composition visuals, text, UI Automation
tests/                    Unit and desktop tests for src/
spikes/                   Throw-away Phase 4 prototypes, measurement harness and their tests (not shipped)
tools/re-analysis/        Read-only Python scripts used for reference static analysis
Halo.slnx                 .NET solution (product, tests and spikes)
global.json               Pins the .NET SDK (10.0.403) and the test runner
Directory.*.props         Shared build settings and central package versions
nuget.config              nuget.org-only package source
.gitignore                Excludes binaries, extraction output, build output, secrets
.gitmessage               Commit message template (Conventional Commits)
```

## Build, run and test

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'   # set permanently on the dev machine (DEVELOPMENT_WORKFLOW §6)
dotnet build Halo.slnx -c Release
.\src\Halo.App\bin\Release\net10.0-windows10.0.19041.0\Halo.exe   # run the island; right-click it to exit
dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"   # unit tests
dotnet test --project tests/Halo.App.Tests/Halo.App.Tests.csproj -c Release -- --filter-trait "Category=Integration"   # desktop tests
```

The desktop tests start `Halo.exe`, click it and move the mouse; do not use the machine while they run.
Spike and measurement commands: `spikes/README.md`.

## Documentation index

| Document | Purpose |
|---|---|
| [PROJECT_STATUS](docs/PROJECT_STATUS.md) | Current phase, blockers, next action |
| [DEVELOPMENT_WORKFLOW](docs/DEVELOPMENT_WORKFLOW.md) | Commit convention, validation rules, milestone workflow |
| [REVERSE_ENGINEERING_WORKFLOW](docs/REVERSE_ENGINEERING_WORKFLOW.md) | Safety rules and procedure for reference analysis |
| [ENVIRONMENT_AUDIT](docs/ENVIRONMENT_AUDIT.md) | Machine, SDKs and tools available |
| [SAFETY_AND_SCOPE](docs/SAFETY_AND_SCOPE.md) | What we will and will not do |
| [TOOLING_MATRIX](docs/TOOLING_MATRIX.md) | Analysis/build tooling status |
| [NUSA_WEBSITE_AUDIT](docs/NUSA_WEBSITE_AUDIT.md) | Advertised behaviour of the reference product |
| [NUSA_INSTALLER_STATIC_ANALYSIS](docs/NUSA_INSTALLER_STATIC_ANALYSIS.md) | Static findings from the supplied installer |
| [NUSA_RUNTIME_ANALYSIS](docs/NUSA_RUNTIME_ANALYSIS.md) | Runtime analysis status (skipped by owner decision) |
| [NUSA_FEATURE_MATRIX](docs/NUSA_FEATURE_MATRIX.md) | Feature inventory with evidence levels |
| [NUSA_EVIDENCE_REGISTER](docs/NUSA_EVIDENCE_REGISTER.md) | Per-statement evidence register (IDs `E-xxx`) |
| [REFERENCE_ARCHITECTURE_HYPOTHESES](docs/REFERENCE_ARCHITECTURE_HYPOTHESES.md) | Inferred architecture, clearly labelled |
| [EXTERNAL_REFERENCES](docs/EXTERNAL_REFERENCES.md) | Public documentation and sources consulted |
| [ARCHITECTURE_DECISION_RECORD](docs/ARCHITECTURE_DECISION_RECORD.md) | ADR-001: stack, components, state model, budgets (proposed) |
| [PRODUCT_REQUIREMENTS](docs/PRODUCT_REQUIREMENTS.md) | Functional and non-functional requirements (proposed) |
| [SECURITY_AND_PRIVACY_DESIGN](docs/SECURITY_AND_PRIVACY_DESIGN.md) | Threat model, data inventory, licensing, distribution security |
| [TESTING_STRATEGY](docs/TESTING_STRATEGY.md) | Test levels, tooling, catalogues, milestone acceptance |
| [PROTOTYPE_S1_S2_REPORT](docs/PROTOTYPE_S1_S2_REPORT.md) | S1/S2 architecture gate: protocol, setup and results (2026-10-28) |

## License

Not yet chosen. A license will be selected before any public release (tracked in
`docs/PROJECT_STATUS.md`). Until then, all rights are reserved by the author.
