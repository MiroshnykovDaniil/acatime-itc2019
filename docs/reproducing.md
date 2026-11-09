# Reproducing the results

This document will be completed when the code and curated result files are
imported. It should let a reviewer go from a paper number to a verified score.

## Environment

- .NET SDK version: to be pinned.
- Python version and packages: to be pinned.
- OS/architecture and CPU: to be recorded for timing claims.
- Docker/devcontainer: to be added.
- Exact artifact commit/tag: to be recorded.

## Budgets

| Context | Budget |
|---|---|
| AcaTime production GA | 60 s |
| Main ITC experiments | 600 s |
| Chained `tg-fal17` result | 600 s + 300 s |
| Published UniTime reference | 2 h |

## Planned commands

```bash
make data
make check-data
make build
make smoke
make verify
```

## Stochasticity

The GA is stochastic. Every reported run must include its seed and parameters.
The artifact should include all curated runs, not only the best one, plus the
median/percentile summary used in the paper.

## Claim verification

`docs/claims.md` maps paper claims to planned result paths. Once the result
import is complete, `make verify` should recompute the scores and compare them
with the expected values.
