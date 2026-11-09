# Source code

Code import is in progress. The public artifact will contain the exact source
used for the paper, not the full production AcaTime backend.

## Planned modules

| Directory | Source | Purpose |
|---|---|---|
| `ga/` | `AcaTime.Common/AcaTime.Algorithm.Genetic` + required projects | GA optimizer and supporting models |
| `bridge/` | `itc2019-benchmark/tools/` | ITC XML → AcaTime conversion and post-processing |
| `scorer/` | `itc2019-benchmark/tools/score.py` | independent official-cost scorer |
| `runner/` | `itc2019-benchmark/runner/` | local solver host |

## Import rules

- Do not commit `bin/`, `obj/`, `.DS_Store`, logs or secrets.
- Pin the exact source commit in `docs/reproducing.md`.
- Keep the GA's production defaults unchanged; ITC adaptation remains opt-in.
- Add third-party license notices when dependencies are imported.
- Include the tests needed to verify the scorer and bridge behavior.
