# Advanced Windows Island

Internal codename: **Project Halo**. The public product name is not chosen yet; it will be selected
after a name and trademark availability check.

An original Windows desktop application that turns the top edge of the screen into an adaptive
"island": a compact, non-intrusive surface for live activities such as media playback, timers,
reminders, file staging and system indicators.

> **Status:** Pre-implementation. The repository currently contains research, environment audits
> and engineering process documents only. No application code exists yet.
> See [`docs/PROJECT_STATUS.md`](docs/PROJECT_STATUS.md).

## Goals

- Native-feeling Windows desktop app (planned stack: C# / .NET + WPF with isolated Win32 interop,
  pending the architecture decision record in Phase 3).
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
tools/re-analysis/        Read-only Python scripts used for reference static analysis
.gitignore                Excludes binaries, extraction output, build output, secrets
.gitmessage               Commit message template (Conventional Commits)
```

Source, tests and build tooling will be added from Phase 4 onward.

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
| [REFERENCE_ARCHITECTURE_HYPOTHESES](docs/REFERENCE_ARCHITECTURE_HYPOTHESES.md) | Inferred architecture, clearly labelled |

## License

Not yet chosen. A license will be selected before any public release (tracked in
`docs/PROJECT_STATUS.md`). Until then, all rights are reserved by the author.
