# `lums-spr18` result

Certified seed/repair result: **826**. This is **not** a GA improvement; the GA
produced no positive moves on this instance.

Files:

- `sol-seed-826.json` — solution input for the scorer;
- `score-seed-826.json` — official score;
- `solution-seed-826.xml` — canonical ITC XML.

Verify:

```bash
python3 src/scorer/score.py data/lums-spr18_reduced.xml results/lums-spr18/sol-seed-826.json
```

Reference: best known 95; this result is a feasibility/repair seed, not a
headline GA result.
