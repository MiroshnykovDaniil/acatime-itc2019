# `tg-spr18` results

Certified result: **22,180** (600 s GA + room optimisation).

The canonical files are in `certified/`:

- `sol.json` — solution input for the scorer;
- `score.json` — official score, total 22,180;
- `solution.xml` — canonical ITC XML.

`raw/` contains the intermediate runner outputs and the pre-roomopt score
(which is infeasible). Do not use `raw/score.json` as a result; the certified
score is `certified/score.json`.

Verify:

```bash
python3 src/scorer/score.py data/tg-spr18_reduced.xml results/tg-spr18/certified/sol.json
```

Hard constraints: SameRoom and Precedence are present. The result is 1.75x the
optimum, 1.37x UniTime's best and 1.09x UniTime's average.
