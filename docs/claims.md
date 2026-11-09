# Claim-to-artifact map

This file maps every quantitative claim in the paper to the artifact that
verifies it. `make verify` runs the independent scorer over the imported
solutions and regenerates `results/summary.csv`.

Status meanings:

- **verified** — score recomputed from the committed solution and matches;
- **imported** — files are present but a human-readable note is the evidence;
- **planned** — not yet imported.

## Certified results

| Claim | Value | Budget | Artifact | Status |
|---|---:|---|---|---|
| `tg-fal17` best chained | **6,889** | 600 + 300 s | `results/tg-fal17/chained/` | verified |
| `tg-fal17` independent run A | **6,919** | 600 s | `results/tg-fal17/independent-600-a/` | verified |
| `tg-fal17` independent run B | **6,930** | 600 s | `results/tg-fal17/independent-600-b/` | verified |
| `tg-fal17` best 300 s | 7,042 | 300 s | `results/tg-fal17/best-300/` | verified |
| `tg-fal17` 300 s median | 7,325 | 300 s | `results/tg-fal17/300s/summary.csv` | verified |
| `tg-spr18` certified | **22,180** | 600 s + roomopt | `results/tg-spr18/certified/` | verified |
| `lums-fal17` certified seed | **1,602** | local search seed | `results/lums-fal17/` | verified |
| `lums-spr18` certified seed | **826** | local search seed | `results/lums-spr18/` | verified |

## Ablations and negative results

| Claim | Value | Artifact | Status |
|---|---:|---|---|
| Exact one-room-per-class, 600 s | 7,220 / 7,053 | `results/tg-fal17/exact-ablation/` | verified |
| Initial LNS solution | 7,800 | `results/tg-fal17/initial-lns/` | planned |
| Old non-room-aware + repair headline | 7,310 | `results/negative/tg-fal17-old-7310/` | planned |
| Old repair distribution: median | 7,925 | `results/negative/tg-fal17-old-7310/summary.csv` | planned |
| Old repair distribution: beat 7,800 | 31.4% | `results/negative/tg-fal17-old-7310/summary.csv` | planned |
| `tg-spr18` initial solution | 23,148 | `results/tg-spr18/raw/` | planned |
| `iku-fal17` room collisions | 70 | `results/negative/iku-fal17/STATUS.md` | imported |
| `iku-spr18` | untried | — | n/a |

## Reference values

These are not claims produced by AcaTime; they are external comparison points
and should be cited from the published sources.

| Reference | `tg-fal17` | `tg-spr18` |
|---|---:|---:|
| Proven optimum | 4,215 | 12,704 |
| UniTime, 2 h best / average | 5,817 / 6,644 | 16,140 / 20,418 |
| Competition finalists | 6,774 / 7,358 / 8,044 | 12,704–31,900 |

## Scope facts

- 30 ITC 2019 competition instances total.
- 6 are student-free and directly comparable with the current AcaTime model.
- 4 are certified in this paper.
- 24 are student-bearing and require student sectioning; outside this paper.
- `lums` results are seed/repair results, not GA improvements.
- `6,889` is chained (600 s + 300 s), not a standalone 300 s run.
- The paper does not claim that AcaTime beats UniTime on score.

## Verification rule

Every row marked `planned` must eventually have:

1. an input instance path/checksum;
2. a config and seed;
3. the exact command;
4. the solution file;
5. the scorer output;
6. the expected value in a machine-readable summary.

Run:

```bash
make verify
```

The resulting table is `results/summary.csv`.
