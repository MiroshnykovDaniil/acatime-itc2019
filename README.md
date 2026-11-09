# AcaTime × ITC 2019 — reproducibility artifact

[![DOI](https://zenodo.org/badge/DOI/10.5281/zenodo.24083720.svg)](https://doi.org/10.5281/zenodo.24083720)

This repository is the public artifact for the paper:

> **Bridging AcaTime and ITC 2019: Certified Multi-Instance Results for a General Timetabling GA**

**Archived version:** `v0.1.0` — DOI [10.5281/zenodo.24083720](https://doi.org/10.5281/zenodo.24083720)  
**Concept DOI (all versions):** [10.5281/zenodo.24083719](https://doi.org/10.5281/zenodo.24083719)

**Status:** code imported and verified against the certified result files (2026-11-09). The manuscript is intentionally **not** included here; the paper DOI/citation will be added after submission.

The artifact is being prepared for the IT&I(s)-2026 short paper and the follow-up journal paper. It is not yet archived with a DOI.

## Current verification

```bash
make check-data   # SHA-256 of the five DTU instances
make build        # .NET solution build
make test         # .NET unit tests
make verify       # re-score the curated solutions
```

`make verify` currently checks the 6,889 chained result, the 6,919 / 6,930
independent runs, the 7,042 best / 7,325 median 300 s runs, the 22,180
`tg-spr18` result, the 1,602 / 826 LUMS seeds, the 7,220 / 7,053 exact-room
ablation, and the 70-collision `iku-fal17` negative result.


## What the paper claims

The paper tests the AcaTime genetic algorithm on the **student-free, competition-reduced DTU instances** of ITC 2019:

| Instance | Certified result | Role |
|---|---:|---|
| `tg-fal17` | 6,889 chained (600 + 300 s); 6,919 / 6,930 independent 600 s | GA success |
| `tg-spr18` | 22,180 (600 s + room optimisation) | GA success |
| `lums-fal17` | 1,602 | certified seed / repair, no GA improvement |
| `lums-spr18` | 826 | certified seed / repair, no GA improvement |

Scope: of 30 ITC 2019 instances, only six are student-free and directly comparable with AcaTime's current curriculum-based model. Four were certified. `iku-fal17` is time-feasible but room-infeasible (70 collisions); `iku-spr18` is untried; 24 student-bearing instances require student sectioning and are outside this paper.

The full claim-to-artifact map is in [`docs/claims.md`](docs/claims.md).

## Why AcaTime, not UniTime

AcaTime is the production solver for our faculty because its hard and soft rules are per-season user scripts, and its GA is the post-optimizer that improves the Default constructive schedule against those scripts. UniTime is a mature system, but its solver rules are Java constraint classes, not user scripts; adapting it to our curriculum model and rule layer would require re-encoding the model and writing Java extensions.

The ITC 2019 comparison is the neutral external check: AcaTime is within the finalist range at a much shorter budget while matching the rules we actually use. The paper does **not** claim that AcaTime beats UniTime on score.

## Repository layout

```text
data/            ITC 2019 reduced-instance provenance, download script and checksums
src/ga/          exact AcaTime GA projects (GA, Default, ScheduleCommon, ScriptModels)
src/runner/      local ITC runner
src/bridge/tools/ Python conversion, post-processing and scoring pipeline
src/scorer/      scorer entry point (score.py)
experiments/     run configurations, seeds and budgets
results/         certified solutions, scores, logs and summary tables
docs/            bridge semantics, data provenance, limitations, claims
paper/           paper metadata only; no manuscript PDF
scripts/         helper scripts for data verification and result checks
```

## Data

The input files are the **competition-reduced DTU versions** of the ITC 2019 instances:

- dataset DOI: <https://doi.org/10.11583/DTU.20014127>
- these are not the raw competition XML;
- these are not the post-competition reduction (`10.11583/DTU.20014070`);
- use the competition-reduced files for comparison with published ITC 2019 results.

Run:

```bash
./data/download.sh
./scripts/check_data.sh
```

The data is stored outside Git history by default; the download script fetches the official DTU files and verifies SHA-256 checksums. See [`data/README.md`](data/README.md) and [`docs/data-provenance.md`](docs/data-provenance.md).

## Reproduction

The code and the curated certified results are in the repository. From a fresh
checkout:

```bash
./data/download.sh       # fetch and verify the DTU reduced instances
make build               # dotnet build src/AcaTime.ItcArtifact.sln -c Release
make test                # dotnet unit tests
make verify              # re-score every curated solution
```

`make smoke` runs the single 6,889 check. The full verification is fast because
it only re-runs the scorer; it does not re-run the GA. Full experiment
reproduction requires the runner and the configurations to be completed in
[`experiments/`](experiments/README.md) and [`docs/reproducing.md`](docs/reproducing.md).

Requirements:

- .NET SDK 9 (`global.json`);
- Python 3.10+;
- `numpy` for the optional local-search/room pipeline (the scorer and verifier
  are standard-library only).


## Docker

Build and verify without installing .NET or Python locally:

```bash
make docker-build
make docker-verify
```

The image builds the .NET solution, runs the unit tests, downloads the DTU
instances, and re-scores every curated solution. The default container command
is the same as `make verify`. To use an existing local data directory instead of
downloading again:

```bash
docker run --rm -v "$PWD/data:/artifact/data" acatime-itc2019:local
```

## Licensing

- Code: Apache-2.0 (see `LICENSE`).
- Documentation: CC BY 4.0, unless stated otherwise.
- Data: the DTU dataset retains its own license and citation requirements. Do not relicense the data.
- Third-party components: see `THIRD_PARTY_NOTICES.md`.

## Citation

The software citation metadata is in [`CITATION.cff`](CITATION.cff). A DOI will be reserved through Zenodo before the paper is submitted. Until then, cite this repository URL and the exact commit/tag.

## Contact

Daniil Miroshnykov, Taras Shevchenko National University of Kyiv  
`daniilmiroshnykov@knu.ua`
