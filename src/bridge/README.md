# Bridge module

Imported from `itc2019-benchmark/tools`:

- `convert.py` — ITC XML → AcaTime input;
- `scripts_gen.py` — generated rule/constraint scripts;
- `post.py` — candidate pinning and room consolidation;
- `run_from_solution.py` — scored solution → runner seed;
- the local-search and room-search scripts used by the experimental pipeline.

The scorer itself lives at `src/scorer/score.py` (a link to
`bridge/tools/score.py` for compatibility with the pipeline imports).

See `docs/bridge.md` for the mapping and its conservative deviations. The
Python pipeline requires `numpy`; the scorer and verifier do not.
