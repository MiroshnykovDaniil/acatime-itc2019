# Scorer validation

The paper reports official ITC 2019 costs. The artifact's referee is the
independent scorer in `src/scorer/score.py` (backed by
`src/bridge/tools/score.py`), which implements the PATAT-2018 formulas for time,
room, distribution and student costs.

## Local validation

`make verify` re-scores every committed solution and compares the total against
the expected value in `results/claims.csv`. This is the required validator for
the artifact; it does not depend on the external ITC website or an account
token.

The scorer emits:

- hard feasibility and violation list;
- time, room, distribution and student penalties;
- total cost;
- unsupported constraint types.

## What is checked

- `tg-fal17`: 6,889 chained, 6,919 / 6,930 independent, 7,042 best 300 s,
  7,325 median, 7,220 / 7,053 exact-room ablation.
- `tg-spr18`: 22,180.
- `lums-fal17`: 1,602.
- `lums-spr18`: 826.
- `iku-fal17`: 70 room collisions (negative result).

## Scope

The external ITC web validator is not required and is not called by the
artifact. If a token is available, an optional cross-check can be added later,
but the reproducible claim is based on the committed solutions and the local
scorer.

## Remaining work

- Add a small fixture set with hand-checked component costs.
- Keep the scorer and bridge scoring formulas in sync if the pipeline changes.
