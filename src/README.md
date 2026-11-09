# Source code

The exact code used for the paper has been imported from the internal AcaTime
workspace. It is not the full production backend: it contains the GA projects,
the ITC runner, the conversion/scoring pipeline and the scorer entry point.

| Directory | Source | Purpose |
|---|---|---|
| `ga/` | `AcaTime.Common/AcaTime.Algorithm.Genetic` + required projects | GA optimizer, supporting models and tests |
| `runner/` | `itc2019-benchmark/runner` | local solver host |
| `bridge/tools/` | `itc2019-benchmark/tools` | ITC XML → AcaTime conversion and post-processing |
| `scorer/score.py` | `itc2019-benchmark/tools/score.py` | independent official-cost scorer |

Source snapshot: `AcaTime.Common` commit `8669726` (2026-11-09). Build with:

```bash
dotnet build src/AcaTime.ItcArtifact.sln -c Release
```

The GA's production defaults are unchanged; the ITC adaptation remains opt-in.
See `THIRD_PARTY_NOTICES.md` for dependency licenses and `docs/reproducing.md`
for the exact commands.
