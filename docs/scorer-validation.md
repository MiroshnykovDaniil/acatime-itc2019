# Scorer validation

The paper reports official ITC 2019 costs. The scorer must therefore be
demonstrably consistent with the official validator.

## What is needed

- The independent scorer source and its tests.
- A set of known input/solution/score triples covering every cost component:
  time, room, distribution and student (where applicable).
- Parity checks against the official ITC 2019 validator for the four certified
  solutions.
- Saved official validator responses or logs, with timestamps and checksums.
- A command that recomputes a score from an input XML and a solution XML.

## Required output fields

Each score record should contain:

- instance name;
- hard feasibility flag and violation list;
- time penalty;
- room penalty;
- distribution penalty;
- student penalty;
- total cost;
- scorer version/commit.

## Acceptance rule

For every certified solution, the independent scorer's total and component
costs must match the official validator, or any difference must be explained and
documented.

The paper must never use AcaTime's internal objective proxy as a reported
result. It is a search signal only; the scorer is the referee.
