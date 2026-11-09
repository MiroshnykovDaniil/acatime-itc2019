# Experiments

This directory contains the seed files and the run documentation needed to
re-run the reported results. The converted `api-scheduledata*.json` inputs are
generated from the public DTU instances and are not committed; regenerate them
with `src/bridge/tools/convert.py`.

## `tg-fal17`

Seeds:

- `seeds/seed-7800-run.json` — initial feasible/LNS seed;
- `seeds/seed-6930-run.json` — seed reconstructed from the 6,930 solution;
- `seeds/seed-6889-run.json` — seed reconstructed from the 6,889 solution;
- `seeds/seed-11450-run.json` — weaker optimised seed.

The full room-aware run log is in `roomaware-summary.md`.

Regenerate the input and run a 300 s room-aware experiment:

```bash
python3 src/bridge/tools/convert.py \
  data/tg-fal17_reduced.xml /tmp/tg-fal17 --room-aware

dotnet run --project src/runner -c Release -- \
  /tmp/tg-fal17/api-scheduledata-roomaware.json /tmp/tg-fal17/run \
  genetic MaxIterations=1 TimeoutInSeconds=300 ResultsCount=1 \
  InitialPopulationSize=1 GeneticIterations=1000000 Adapter=itc \
  ExtraItcOperators=3 IgnoreClassrooms=true \
  SeedSchedule=experiments/tg-fal17/seeds/seed-7800-run.json
```

Then materialise rooms and score:

```bash
python3 src/bridge/tools/roomsearch.py \
  /tmp/tg-fal17/meta.json /tmp/tg-fal17/run/run-genetic-*.json \
  /tmp/tg-fal17/sol.json Seed=1 TimeLimit=60

python3 src/scorer/score.py \
  data/tg-fal17_reduced.xml /tmp/tg-fal17/sol.json \
  --emit-xml /tmp/tg-fal17/solution.xml
```

## `tg-spr18`

Seed: `seeds/seed-23148-run.json`. The certified result is 22,180 after the
room optimisation pass. The exact run sequence is documented in
`results/tg-spr18/README.md` and the raw run JSON.

## `lums`

The `lums` results are seed/repair results; their solution and score files are
in `results/lums-fal17/` and `results/lums-spr18/`. The GA produced no positive
moves on these instances.

## Missing for full replay

- the converted `api-scheduledata*.json` inputs (regenerable);
- exact per-run seeds for the independent 300/600 s runs (the runner output
  JSON records parameters, but the seed reconstruction files still need to be
  collected);
- hardware and wall-clock metadata.
