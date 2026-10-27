# Spikes (throw-away prototypes)

Code in this folder answers the Phase 4 entry questions in ADR-001 §13. It is **not shipped** and is
**not** the start of the production application; production projects will live elsewhere from M2.

| Project | Target | Purpose |
|---|---|---|
| `src/Halo.Spikes.Measurement` | `net10.0` | Pure logic: budgets (ADR §9), outcome vocabulary, nearest-rank percentiles, CPU normalisation, PB-02 aggregation, PB-03/PB-05 frame windowing, predeclared calibration rules, test-surface geometry, overlay options and commands, privacy-safe JSON-lines log |
| `src/Halo.Spikes.Windows` | `net10.0-windows` | Win32 interop: environment snapshot, Desktop Duplication frame metadata (no pixels), PDH counters, GDI capture of the test region, window helpers |
| `src/Halo.Spikes.Overlay` | `net10.0-windows`, WPF | Techniques **A** (layered) and **B** (DWM-extended frame + `SetWindowRgn`), `--technique A` or `--technique B`; `--smoke` kept from M0 |
| `src/Halo.Spikes.CompositionBaseline` | `net10.0-windows10.0.19041.0` | Technique **C** minimal baseline: Win32 + `Windows.UI.Composition` (no text, no UIA; report §6) |
| `src/Halo.Spikes.Harness` | `net10.0-windows`, WPF | Runs the S1/S2 protocol: backdrop and probe windows, overlay control, Desktop Duplication, counters, calibration, S2 checklist (FlaUI UIA3), artifacts in `perf-results/` |
| `tests/Halo.Spikes.Tests` | `net10.0-windows` | xUnit v3 tests (Microsoft.Testing.Platform) |

Status: S1/S2 prototypes and harness implemented for protocol v1.3 (`docs/PROTOTYPE_S1_S2_REPORT.md`).

## Build and test

The .NET SDK and the test runner (Microsoft.Testing.Platform, via xUnit v3) send usage telemetry to
Microsoft by default. On the development machine the opt-out is set permanently for the user
(`DEVELOPMENT_WORKFLOW.md` §6). Elsewhere, set it first:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'   # also honoured by Microsoft.Testing.Platform
dotnet build Halo.slnx -c Release
# Unit tests (no desktop needed)
dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"
# Integration tests (need an interactive desktop; briefly show the test surface at the top of the screen)
dotnet test --solution Halo.slnx -c Release -- --filter-trait "Category=Integration"
```

## Running the protocol (machine idle, Do Not Disturb on, AC power)

```powershell
$h = 'spikes\src\Halo.Spikes.Harness\bin\Release\net10.0-windows\Halo.Spikes.Harness.exe'
& $h env                       # check run conditions only
& $h batch --hz 120            # calibration, startup, 3 S1 sessions per technique, S2 (about 2.6 h)
& $h batch --hz 60             # after the owner switches the display to 60 Hz (about 50 min)
& $h s2-dpi --technique A      # manual S2-11M: owner changes scaling 100/150/175/125 %
& $h report --date 2026-10-27  # writes perf-results/<date>/REPORT.md
```

`dev-s1`, `dev-s2`, `dev-cal` and `dev-alttab` are shake-down commands. Their `DEV-` runs are never
results. The harness never changes display settings; a refresh-rate mismatch makes a run INVALID.

Measurement output goes to `perf-results/` (ignored by Git). Summaries are copied into `docs/` by hand.

## Rules

- Logs contain only event names, numbers and short tokens. Exceptions are logged by type and HRESULT,
  never by message (`SpikeLog`).
- Desktop Duplication reads metadata only (timestamps and dirty rectangles). S2 screen captures
  cover only the test region over a probe window. UI Automation names are compared in memory and never logged.
- No network calls, telemetry or auto-update code.
- Budgets in `Budgets.cs` mirror ADR-001 §9 and change only through an approved ADR update.
