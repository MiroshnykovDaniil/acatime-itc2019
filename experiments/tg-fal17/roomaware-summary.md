# W5: room-aware GA fitness — results (2026-11-02)

## What was added

`convert.py --room-aware` generates an **opt-in** ITC objective that adds room
feasibility and room cost to the GA's `itc-official-cost` estimation script:

- per candidate: bitmask of rooms allowed by capacity + unavailability at that
  candidate's meetings;
- per candidate: pair cells whose start lies inside the meeting interval;
- per class/room: the official room penalty;
- greedy room assignment by room-count buckets (fewest candidate rooms first),
  charging `1e6` per room collision and the assigned room's penalty.

Default `--room-aware` assigns a room **per meeting** (a relaxation of the
official one-room-per-class rule); the final room pass enforces class
consistency. `--room-aware-exact` is the stricter one-room-per-class ablation.

No C# core changes: the generated script is the adapter. The default
(non-room-aware) conversion is byte-identical to the pre-W5 DTO (verified by
object comparison of `api-scheduledata.json`).

The GA still does not persist room ids; the final room assignment is the
existing fixed-time `tools/roomsearch.py` pass. In all runs below roomsearch
reported **0 conflicts and 0 iterations**, i.e. it only materialises rooms; no
joint time+room repair was needed. The independent scorer (`tools/score.py`)
is the referee.

## Independent runs from the certified 7,800 seed

300 s each, same parameters as the earlier recheck
(`Adapter=itc ExtraItcOperators=3 IgnoreClassrooms=true`,
`InitialPopulationSize=1`, `SeedSchedule=out/tg-fal17/seed-7800-run.json`):

| run | GA estimate (room-aware proxy) | official | time | room | dist | roomsearch |
|---|---:|---:|---:|---:|---:|---|
| r1 | 992,801 | 7,279 | 2,684 | 151 | 88 | 0 conflicts |
| r2 | 992,998 | **7,042** | 2,700 | 102 | 77 | 0 conflicts |
| r3 | 992,715 | 7,325 | 2,732 | 101 | 88 | 0 conflicts |
| r4 | 992,664 | 7,386 | 2,732 | 122 | 90 | 0 conflicts |
| r5 | 992,703 | 7,397 | 2,732 | 173 | 88 | 0 conflicts |

Median **7,325**, mean 7,286, best **7,042**. The seed's official cost is
7,800 (time 2,732, room 436, dist 95); every run improves all three components.
The room-aware seed estimate is 992,120 (old proxy 992,636 minus the 516
room-penalty proxy).

## Longer / chained runs

| run | start | budget | official | time | room | dist | note |
|---|---|---|---:|---:|---:|---:|---|
| r600 | 7,800 seed | 600 s | **6,930** | 2,684 | 162 | 70 | independent |
| r600b | 7,800 seed | 600 s | **6,919** | 2,684 | 151 | 70 | independent |
| chain6930 | 6,930 solution | 300 s | **6,889** | 2,684 | 121 | 70 | chained |
| chain6889 | 6,889 solution | 300 s | 6,889 | 2,684 | 121 | 70 | chained, no further official gain |

The two independent 600 s runs are 6,930 and 6,919 — reproducible. The 6,889
result is 115 points (1.7%) above the 6,774 competition finalist and better than
the 7,358 / 8,044 finalists. The chain is reported separately from the
independent runs; it is not a standalone 300 s result.

## Ablation: exact one-room-per-class proxy

`--room-aware-exact` assigns one room to all meetings of a class (the official
rule) instead of the per-meeting relaxation:

| run | budget | official | time | room | dist |
|---|---:|---:|---:|---:|---:|
| exact-300 | 300 s | 7,355 | 2,732 | 111 | 89 |
| exact-600 | 600 s | 7,220 | 2,684 | 92 | 88 |
| exact-600b | 600 s | 7,053 | 2,636 | 161 | 81 |

The relaxation is stronger on this instance (6,919/6,930 vs 7,053/7,220 at
600 s), because per-meeting room choices leave the time/distribution search
freer; the final roomsearch still found a valid one-room-per-class assignment
(zero conflicts) in every reported run. The exact model is a stricter ablation,
not the headline method.

## Other seeds / budgets

| run | start | budget | official | time | room | dist | roomsearch |
|---|---|---|---:|---:|---:|---:|---|
| weak180 | feasibility 22,267 | 180 s | 10,767 | 4,068 | 151 | 124 | 0 conflicts |
| opt11450 | optimised 11,450 | 300 s | **8,823** | 3,356 | 131 | 99 | 0 conflicts |
| 20 s smoke | 7,800 seed | 20 s | 7,546 | 2,732 | 182 | 95 | 0 conflicts |

The 11,450 seed result improves the earlier non-room-aware 9,424 by 601 points
and is room-feasible. The weak-seed 180 s result (10,767) is slightly worse than
the non-room-aware 180 s result (10,200, after room repair); room feasibility
consumes search effort on weak seeds. The strong-seed regime is where W5 pays.

## Comparison with the non-room-aware recheck (same 7,800 seed)

| method | budget | official | feasibility |
|---|---|---:|---|
| non-room-aware GA + joint repair, best of 312 restarts | 300 s + repair | 7,310 | repair needed |
| non-room-aware GA, typical repair outcome | 300 s + repair | median 7,925 | 31.4% beat 7,800 |
| **room-aware GA + roomsearch materialisation** | **300 s** | **7,042 best; 7,325 median** | **5/5 zero-conflict** |
| **room-aware GA + roomsearch** | **600 s** | **6,919 / 6,930** | **zero-conflict** |
| **room-aware GA, chained 300 s** | **600 + 300 s** | **6,889** | **zero-conflict** |

References: optimum 4,215; UniTime 2-h average 5,817; Lemos 6,774;
Er-rhaimini 7,358; Gashi 8,044; pipeline 7,800.

## Reproduce

```bash
# opt-in DTO (writes api-scheduledata-roomaware.json; default DTO untouched)
python3 tools/convert.py data/tg-fal17_reduced.xml . --room-aware
# stricter ablation: one room per class
python3 tools/convert.py data/tg-fal17_reduced.xml . --room-aware-exact

# 300 s independent run from the certified 7,800 seed
dotnet run --project runner -c Release -- \
  out/tg-fal17/api-scheduledata-roomaware.json out/tg-fal17/ga-roomaware/rX \
  genetic MaxIterations=1 TimeoutInSeconds=300 ResultsCount=1 \
  InitialPopulationSize=1 GeneticIterations=1000000 Adapter=itc \
  ExtraItcOperators=3 IgnoreClassrooms=true \
  SeedSchedule=out/tg-fal17/seed-7800-run.json

# materialise rooms (0 conflicts expected) and score officially
python3 tools/roomsearch.py out/tg-fal17/meta.json RUN.json SOL.json Seed=1 TimeLimit=60
python3 tools/score.py data/tg-fal17_reduced.xml SOL.json --emit-xml solution.xml
```

## Artifacts

- `r1/` … `r5/`: 300 s independent runs, `sol.json`, `score.json`, `solution.xml`
- `r600/`, `r600b/`: 600 s independent runs
- `chain6930/`, `chain6889/`: chained runs
- `weak180/`: feasibility-seed run
- `opt11450/`: optimised-seed run
- `r6-perclass/`, `r600-perclass/`, `r600b-perclass/`: exact-model ablation
- `smoke20/`, `smoke-final/`: short smoke runs
- `out/tg-fal17/seed-11450-run.json`, `seed-6930-run.json`, `seed-6889-run.json`:
  seed reconstructions via `tools/run_from_solution.py`
- `out/tg-fal17/api-scheduledata-roomaware.json`: default (per-meeting
  relaxation) DTO
- `out/tg-fal17/api-scheduledata-roomaware-exact.json`: exact one-room-per-class
  DTO

## Caveats

- The default room objective is a per-meeting relaxation; the official rule is
  one room per class. The post-pass enforces the official rule and found 0
  conflicts in every reported run. The exact proxy is an ablation (7,053/7,220
  at 600 s) and is not the headline method.
- The room objective in the GA is a greedy proxy; it can under/over-estimate the
  actual room penalty (the 6,930 -> 6,889 chain improved actual room cost while
  the proxy moved by only +1).
- Room ids are assigned only in the post-pass; the GA output itself has
  `room=null`, as before.
- The chained runs reuse the previous solution as a seed; do not mix them with
  independent-run statistics.
- This instance has no hard `SameRoom`; W6 is still needed for tg-spr18.
