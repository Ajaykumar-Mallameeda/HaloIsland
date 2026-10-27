# Spikes (throw-away prototypes)

Code in this folder answers the Phase 4 entry questions in ADR-001 §13. It is **not shipped** and is
**not** the start of the production application; production projects will live elsewhere from M2.

| Project | Target | Purpose |
|---|---|---|
| `src/Halo.Spikes.Measurement` | `net10.0` | Pure measurement logic: budgets from ADR §9, nearest-rank percentiles, CPU-time normalisation, PB-05 frame-pacing evaluation, privacy-safe JSON-lines log |
| `src/Halo.Spikes.Windows` | `net10.0-windows` | Win32 interop: records measurement conditions (OS build, display mode, refresh rate, DPI, power source, power mode) |
| `src/Halo.Spikes.Overlay` | `net10.0-windows`, WPF | Host for the S1/S2 overlay spikes. **M0 contains only `--smoke`** (shows a window, waits for the first render, exits 0) |
| `tests/Halo.Spikes.Tests` | `net10.0-windows` | xUnit v3 tests (Microsoft.Testing.Platform) |

Status: M0 skeleton only. S1/S2 are **not implemented** until the proposed protocol in
`docs/PROTOTYPE_S1_S2_REPORT.md` is approved.

## Build and test

The .NET SDK and the test runner (Microsoft.Testing.Platform, via xUnit v3) send usage telemetry to
Microsoft by default. The only documented switch is an environment variable, so set it first
(recommended permanently at user level; the project cannot disable it from a file):

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'   # also honoured by Microsoft.Testing.Platform
dotnet build Halo.slnx -c Release
# Unit tests (no desktop needed)
dotnet test --solution Halo.slnx -c Release -- --filter-not-trait "Category=Integration"
# Integration tests (need an interactive desktop; briefly show a small window)
dotnet test --solution Halo.slnx -c Release -- --filter-trait "Category=Integration"
# Smoke run with a log file
spikes\src\Halo.Spikes.Overlay\bin\Release\net10.0-windows\Halo.Spikes.Overlay.exe --smoke --log perf-results\smoke.jsonl
```

Measurement output goes to `perf-results/` (ignored by Git). Summaries are copied into `docs/` by hand.

## Rules

- Logs contain only event names, numbers and short tokens. Exceptions are logged by type and HRESULT,
  never by message (`SpikeLog`).
- No network calls, telemetry or auto-update code.
- Budgets in `Budgets.cs` mirror ADR-001 §9 and change only through an approved ADR update.
