# Reproducing the results

The code and the curated certified results are in the repository. This document
covers the verification path; full GA re-runs still need the experiment
configuration import.

## Environment

- .NET SDK: **9.0.201** (`global.json`, roll-forward enabled).
- Python: **3.10+**.
- Python packages: the scorer and verifier use only the standard library;
  `numpy==2.4.6` is required for the optional local-search/room pipeline.
- Source snapshot: `AcaTime.Common` commit `8669726` (2026-11-09).
- Hardware/OS for the reported runs: not yet recorded; add before making
  wall-clock claims on new hardware.
- Docker/devcontainer: not yet provided.

## Commands

```bash
./data/download.sh       # fetch and verify the DTU reduced instances
make check-data          # SHA-256 verification
make build               # dotnet build src/AcaTime.ItcArtifact.sln -c Release
make test                # dotnet unit tests
make verify              # re-score all curated solutions
make smoke               # only the 6,889 check
```

`make verify` regenerates `results/summary.csv` and should print `PASS` for
every imported claim. It does not re-run the GA; it verifies the committed
solutions with the independent scorer.

## Budgets

| Context | Budget |
|---|---|
| AcaTime production GA | 60 s |
| Main ITC experiments | 600 s |
| Chained `tg-fal17` result | 600 s + 300 s |
| Published UniTime reference | 2 h |

The 6,889 result is chained (600 + 300 s); the independent 600 s results are
6,919 and 6,930. Do not present 6,889 as a standalone 300 s score.

## Stochasticity

The GA is stochastic. The curated `run-genetic-*.json` files contain the runner
parameters and output metadata. The seed reconstruction files and the full
per-run seed list still need to be imported into `experiments/`; until then,
the artifact verifies the scores of the committed solutions, not a fresh GA
replay.

## Docker

```bash
make docker-build
make docker-verify
```

The Docker build stage runs `dotnet test`; the runtime stage downloads the DTU
instances and runs `scripts/verify_results.py`. To mount existing data:

```bash
docker run --rm -v "$PWD/data:/artifact/data" acatime-itc2019:local
```

## Claim verification

`docs/claims.md` maps the paper claims to result paths. `make verify` checks the
imported rows and writes `results/summary.csv`. The remaining `planned` rows
are the initial 7,800 LNS seed and the superseded 7,310 result; they are not
headline claims.

## Full reproduction

Full GA re-runs require:

1. the converted `api-scheduledata*.json` inputs;
2. the runner configuration and seed for each reported run;
3. the room-aware post-pass and scorer;
4. the recorded hardware/time budget.

Those configurations are the next artifact milestone; the certified solution
files and scorer already make the reported numbers independently checkable.
