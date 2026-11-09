# Scorer module

Planned source:

- `itc2019-benchmark/tools/score.py` — independent ITC 2019 scorer.

Required additions:

- tests for every cost component;
- parity checks against the official ITC 2019 validator;
- saved official validator outputs or logs;
- a command that prints component costs and total cost.

See `docs/scorer-validation.md`. The scorer is the referee; AcaTime's internal
objective proxy is never a reported result.
