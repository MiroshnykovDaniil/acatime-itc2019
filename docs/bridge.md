# Bridge: ITC 2019 → AcaTime

This document will describe the semantics-preserving transformation used to run
AcaTime on ITC 2019 instances. The code itself will be imported from the
internal `itc2019-benchmark` workspace.

## Mapping

- ITC class → AcaTime `GroupSubject`.
- ITC meeting → one-lesson AcaTime series.
- Candidate `(days, start, length, weeks)` patterns → slot domains and
  generated validation scripts.
- ITC rooms, room unavailability, travel and distribution constraints → AcaTime
  hard/soft rule scripts.
- ITC solution → canonical ITC XML, scored by the independent scorer.

## Important deviations

1. Search-side hard checks are conservative: a candidate is rejected if any
   matching pattern pair violates the formula; the official check uses the
   finally chosen pattern. This may forbid valid solutions but never admits an
   invalid one.
2. SameAttendees travel uses the maximum candidate-room travel gap because rooms
   are not visible to AcaTime scripts; the scorer applies the exact
   room-dependent rule.
3. Rooms are assigned in a post-pass. The room-aware GA adds a room proxy to the
   objective; the official room assignment remains a separate step.
4. Student sectioning is not modeled. Only student-free instances are directly
   comparable.
5. Room capacity is not rechecked against class limits where the ITC data
   already filtered rooms as suitable; this follows the ITC FAQ and is required
   for official comparability.

## Scorer

`score.py` is an independent implementation of the official ITC 2019 cost
function. It emits the official XML and reports the cost components. The
scorer, not AcaTime's internal estimator, is the referee for every reported
number.

## Open work

- Import the exact converter/scorer/runner versions used for the paper.
- Add round-trip fixtures and expected scores.
- Add scorer parity tests against the official validator.
