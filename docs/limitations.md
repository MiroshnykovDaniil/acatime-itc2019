# Limitations and negative results

The artifact is deliberately bounded. The paper should not be read as a claim
that AcaTime solves all ITC 2019 instances or beats the competition solvers.

## Scope

- 30 ITC 2019 competition instances total.
- 6 are student-free and directly comparable with AcaTime's current model.
- 4 are certified here.
- 24 are student-bearing and require student sectioning; they are outside this
  paper.

## Negative results

- `iku-fal17` is time-feasible but room-infeasible: 70 room collisions.
- `iku-spr18` is untried.
- `lums-fal17` (1,602) and `lums-spr18` (826) are certified seed/repair results,
  not GA improvements. The GA produced no positive moves on those instances.
- The old 7,310 result is superseded. It used a non-room-aware GA plus a
  best-of-312 repair; its median was 7,925, only 31.4% of runs beat 7,800, and
  only one beat 7,358. It is an ablation/negative result, not a headline.
- The exact one-room-per-class ablation is weaker (7,053 / 7,220 at 600 s) than
  the headline per-meeting relaxation + final pass.

## Budget caveats

- The production GA budget is 60 s.
- The main ITC runs use 600 s; the 6,889 result is chained 600 + 300 s.
- The published UniTime reference uses 2 h.
- These differences must be stated whenever scores are compared.

## What is not claimed

- No claim that AcaTime beats UniTime on score.
- No claim that the four student-free instances represent all 30 ITC instances.
- No claim that `lums` results are GA wins.
- No claim that the bridge replaces specialised competition solvers.
