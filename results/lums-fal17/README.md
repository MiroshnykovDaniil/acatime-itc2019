# `lums-fal17` result

Certified seed/repair result: **1,602**. This is **not** a GA improvement; the
GA produced no positive moves on this instance.

Files:

- `sol-seed-1602.json` — solution input for the scorer;
- `score-seed-1602.json` — official score;
- `solution-seed-1602.xml` — canonical ITC XML.

Verify:

```bash
python3 src/scorer/score.py data/lums-fal17_reduced.xml results/lums-fal17/sol-seed-1602.json
```

Reference: best known 349; this result is a feasibility/repair seed, not a
headline GA result.
