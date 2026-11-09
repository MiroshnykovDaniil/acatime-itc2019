# AcaTime × ITC 2019 — reproducibility artifact

This repository is the public artifact for the paper:

> **Bridging AcaTime and ITC 2019: Certified Multi-Instance Results for a General Timetabling GA**

**Status:** initial scaffold (2026-11-09). Code and curated result files are being imported. The manuscript is intentionally **not** included here; the paper DOI/citation will be added after submission.

The artifact is being prepared for the IT&I(s)-2026 short paper and the follow-up journal paper. It is not yet archived with a DOI.

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
src/             GA, bridge, scorer and runner source (import in progress)
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

The full reproduction commands will be added with the code import. Planned entry points:

```bash
make build
make smoke       # small verification run
make verify      # recompute scorer results from curated solutions
```

Requirements and expected runtimes will be documented in [`docs/reproducing.md`](docs/reproducing.md).

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
