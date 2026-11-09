# Scorer module

Imported from `itc2019-benchmark/tools/score.py`.

Usage:

```bash
python3 src/scorer/score.py data/tg-fal17_reduced.xml results/tg-fal17/chained/sol.json
python3 src/scorer/score.py data/tg-fal17_reduced.xml results/tg-fal17/chained/sol.json --emit-xml /tmp/solution.xml
```

It uses only the Python standard library. The scorer is the referee for every
reported score; AcaTime's internal objective proxy is never used as a result.

Remaining work: add explicit component tests and saved official-validator
parity evidence (see `docs/scorer-validation.md`).
