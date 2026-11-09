# `tg-fal17` results

All scores are official ITC 2019 costs from `src/scorer/score.py`.

| Directory | Result | Budget | Role |
|---|---:|---|---|
| `chained/` | **6,889** | 600 + 300 s | best chained result |
| `independent-600-a/` | **6,919** | 600 s | independent run |
| `independent-600-b/` | **6,930** | 600 s | independent run |
| `300s/r1..r5/` | 7,042–7,397, median **7,325** | 300 s each | five independent runs |
| `best-300/` | 7,042 | 300 s | best of the five (same run as `300s/r2`) |
| `exact-ablation/exact-600/` | 7,220 | 600 s | exact one-room-per-class ablation |
| `exact-ablation/exact-600b/` | 7,053 | 600 s | exact one-room-per-class ablation |

Each directory contains the runner output JSON, the solution JSON, the official
score JSON and the emitted solution XML.

Verify a solution:

```bash
python3 src/scorer/score.py data/tg-fal17_reduced.xml results/tg-fal17/chained/sol.json
```

The chained result is not a standalone 300 s run. The independent 600 s results
are 6,919 / 6,930; the 6,889 result starts from the 6,930 solution and adds a
300 s run.
