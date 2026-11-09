# Results

Result import is in progress. The public artifact will contain:

```text
results/
  summary.csv
  tg-fal17/
    chained/
    independent-a/
    independent-b/
    best-300/
    exact-room-ablation/
  tg-spr18/
  lums-fal17/
  lums-spr18/
  negative/
```

Each run directory should contain:

- `config.json` — instance, seed, budget, parameters;
- `solution.xml` — canonical ITC solution;
- `score.json` — scorer output with component costs;
- `run.log` — solver log;
- `README.md` — command and expected score.

Do not commit only the best run. Include the distribution used for the
median/percentage claims and the negative results.

Large raw logs and solution bundles may be stored in a Zenodo release rather
than Git history. If so, link them from this directory and record checksums.
