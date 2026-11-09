# Data provenance

## Source

The input files are the **competition-reduced DTU versions** of the ITC 2019
instances:

- DOI: <https://doi.org/10.11583/DTU.20014127>
- Title: *ITC 2019 competition reduced datasets*
- Creator: Dennis Søren Holm, Technical University of Denmark

The files are downloaded from the DTU/figshare record by `data/download.sh` and
verified against `data/CHECKSUMS.sha256`.

## What “reduced” means here

These are not the raw competition XML. The reduction removes redundant
variables while retaining the official ITC 2019 objective and hard structure.
They are the files used in the published ITC 2019 comparisons, including the
UniTime reference runs.

Do **not** substitute the post-competition reduction
(`10.11583/DTU.20014070`). Its soft objective/search space is not identical, so
scores are not directly comparable with the published competition results.

## Files

| File | Role |
|---|---|
| `tg-fal17_reduced.xml` | GA success, chained/independent results |
| `tg-spr18_reduced.xml` | GA success, hard SameRoom + Precedence |
| `lums-fal17_reduced.xml` | certified seed/repair, no GA improvement |
| `lums-spr18_reduced.xml` | certified seed/repair, no GA improvement |
| `iku-fal17_reduced.xml` | negative/scope result, room-infeasible |

## Scope of the data

Only six of the 30 ITC 2019 instances are student-free. The current AcaTime
model does not include student sectioning, so the paper reports only the
student-free subset. The 24 student-bearing instances are outside the paper's
scope and must not be presented as covered by these results.

## Production data

No production AcaTime data (student groups, teachers, institutional rules or
real faculty schedules) is part of this artifact. The artifact is limited to
public ITC 2019 data and derived benchmark outputs.
