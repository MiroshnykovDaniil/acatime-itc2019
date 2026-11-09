# iku-fal17: W6 status (2026-11-02)

## Instance / reference

Official **competition-reduced** file `data/iku-fal17_reduced.xml`
(DTU 10.11583/DTU.20014127, MD5 `a91541757cedc71f29013855e9fe98d1`):

- 2,641 classes, 214 rooms, 14 weeks, 0 students
- 37,044 meetings, 84,100 candidate patterns
- 23,229 hard class pairs (1,099 SameAttendees, 475 NotOverlap, 159 SameDays,
  102 SameRoom, 27 Precedence, 18 SameTime, 9 Overlap, 2 DifferentDays, plus
  WorkDay variants)
- 1,070 soft pairs

Reference results:

| method | cost |
|---|---:|
| lower bound (DSUM) | 18,109 |
| best known (Holm/Mikkelsen) | 18,968 |
| ITC milestone M2 | 19,080 |
| UniTime 2020, best of 50 runs (2 h) | 27,867 |
| UniTime 2020, average of 50 runs | 29,553 |
| UniTime updated 2-h result (journal) | 28,178 |
| Rappos et al. | 26,840 |
| Er-rhaimini | 44,482 |
| Gashi | 50,613 |

The UniTime 2020 paper explicitly notes that UniTime did worst on the two
no-student instances (`iku-fal17`, `tg-fal17`).

## What is implemented

### Capacity correction (important)

The ITC FAQ (I1) states: *a room listed in the class domain is already suitable;
do not compare class limit with room capacity*. Our scorer and room tools were
filtering by capacity, which is wrong. Capacity filters were removed from:

- `tools/score.py`
- `tools/roomsearch.py`
- `tools/solve.py`
- `tools/roomrepair.py`
- `tools/post.py`
- `tools/scripts_gen.py` (room-aware masks)

No tg-fal17 score changed (verified: 7,800 and 7,042 remain feasible with the
same costs).

### W6a: hard SameRoom groups

`tools/roomsearch.py` now has a `GroupRoomSolver` used when the instance has
required SameRoom distributions:

- union-find groups of classes that must share one room;
- one room per group, from the intersection of allowed rooms;
- group meeting occupancy and intra-group overlap checks;
- class-level output (all members receive the same room id).

On iku it recognizes **102 hard SameRoom groups**; SameRoom violations are no
longer in the output. Room collisions remain (see below).

### W6b: room-aware time repair (prototype)

`tools/roomrepair.py` keeps the time assignment hard-feasible and hill-climbs:

- peak concurrency above the number of rooms;
- classes with no allowed room;
- hard SameRoom groups without a common room;
- (added) per-cell bipartite matching deficiency between active classes and
  their allowed rooms.

On the 44 s time-feasible solution it removes the peak overload (263 -> 0),
but the matching deficiency is still ~24k and roomsearch still reports ~1.8k
room collisions. The fixed time structure is not room-feasible. CP-SAT (with
one-room-per-class and allowed-room constraints) returns `INFEASIBLE` in
<1 s, confirming this.

### GA room-aware path

`convert.py --room-aware` cannot currently encode iku: the generated room mask
is a 32-bit `int`, and iku has 214 rooms. A clear guard now stops the conversion
instead of producing an uncompilable script. Extending the generated arrays to
room lists (or using a Python/CP-SAT room-aware search) is required.

## Current certified status

**No official iku-fal17 solution yet.** Time feasibility is solved; room
feasibility is not. This is the correct place to stop before claiming a score.

## Next steps

1. Integrate room availability into the **time search** (not a post-hoc pass):
   add a matching/Hall-deficiency term per (week, day) cell to localsearch, or
   use CP-SAT/LNS to co-optimize time + room.
2. Extend the generated room-aware objective to >31 rooms (room-list arrays
   instead of bitmasks), then use the GA as a strong-seed room-aware repair.
3. Re-run:
   - localsearch with room-aware objective;
   - `roomsearch.py` (GroupRoomSolver);
   - `score.py`.
4. Target: feasible first, then compare with UniTime 27,867/29,553 and the
   best known 18,968.

## Artifacts

- `out/iku-fal17/test/run-ls-60.json` — time-feasible, 0 time violations, 44 s
- `out/iku-fal17/test/run-roomrepair-120b.json` — peak 0, matching deficiency
- `out/iku-fal17/test/sol-roomrepair-120b.json` — group room assignment,
  ~1.8k room collisions, not feasible
- `out/iku-fal17/ga-smoke/` — failed GA compile for 214-room mask (diagnostic)
- `out/iku-fal17/STATUS.md` — this file

## GA pilot after >31-room support (2026-11-03)

The generated room-aware objective now supports >31 rooms using per-candidate
room-index lists plus word-packed occupancy (no 32-bit mask). iku converts to a
60 MB DTO and the runner compiles it.

Pilot runs from the 44 s time-feasible seed:

| run | budget | seed estimate | outcome |
|---|---|---:|---|
| initial wide path | 60 s | -2,147,483,648 | clamped; 1e6 collision penalty overflowed |
| scaled collision penalty | 10 s | -100,042,202 | not clamped, but **0 accepted moves** |
| scaled collision penalty | 60 s | -100,042,202 | no improvement |

The seed estimate `-100,042,202` is dominated by the wide room-collision term:
dividing by the 1e4 collision penalty gives roughly **10,000 collision units**
(the seed is a room-blind time structure). It is not a hard-violation clamp;
the time search now also enforces conservative SameAttendees travel, but the
room objective still rejects every mutation. The GA has not produced a
room-feasible or scoreable iku solution.

This is now a **room-aware seed** issue: the time search must produce a
structure with far fewer room collisions, or the GA must be able to improve the
collision term. The current operators accept zero moves from this seed. After a
better seed, the group room solver + scorer can decide.

## Next (updated)

1. Make `localsearch.py` enforce conservative SameAttendees travel (or generate
   a seed with the AcaTime validator) so the GA seed has zero hard violations.
2. Re-run GA 60/300 s; then `GroupRoomSolver` + `score.py`.
3. If travel makes the seed too constrained, assign rooms jointly with time
   (CP-SAT/LNS) rather than separately.

## Better seed via room-feedback (2026-11-03)

`tools/roomfeedback.py` takes the time seed + room assignment and moves classes
to candidate times where their room is free; it accepts only global room-conflict
reductions.

Result on the 120 s roomrepair seed:

| stage | room conflicts |
|---|---:|
| roomrepair seed | 1,834 |
| after roomfeedback | **70** |

The 70 remaining collisions are only **5 unique class pairs × 14 weeks**
(10 classes; four of the classes are in hard SameRoom groups). CP-SAT on this
fixed time structure still returns `INFEASIBLE`, so the last pairs need a
multi-class/time-room move, not a single-class repair.

GA from the 70-conflict seed:

- estimate `-72,547,214` (down from `-100,042,202`), dominated by the remaining
  room-collision term;
- **0 accepted moves** in 60 s.

So the seed is much better, but still not room-feasible and not a successful
GA starting point. Next: resolve the last five pairs with a small CP-SAT/LNS
neighborhood over time + rooms, or a room-feedback chain that can move both
sides of a pair.

## Blocker-aware neighborhood attempt (2026-11-03)

Starting from the 70-conflict seed (5 unique pairs):

- a targeted SA over the 10 conflict classes: best stayed **70**;
- a SA over 125 classes including blockers of candidate moves: best stayed
  **70** (it could increase to 98, but did not find a 0-conflict state);
- CP-SAT on the fixed time structure: `INFEASIBLE`.

So the remaining five pairs need either an exact time+room neighborhood solver
(CP-SAT/MIP with both candidate times and room choices) or a different time
seed; simple local/SA moves over the conflict classes are not sufficient.

The GA from the 70-conflict seed still accepts zero moves (estimate
-72,547,214). No official iku score.

## Targeted pair/group repair attempt (2026-11-06)

Rechecked the 70-collision seed (5 unique pairs x 14 weeks):

- (1130,1769), (1102,1776), (1154,1879), (1596,2510), (1597,2514)
- exhaustive single-class/group moves for the movable sides found **no
  improving move**; several conflict classes are in hard SameRoom groups
  (1102, 1154, 1879, 1596, 1597), and 2510/2514 have a single candidate/room.
- the seed remains at 70 collisions; no official score.

The next viable path is an exact CP-SAT/LNS neighborhood that changes time and
room together for the five conflict groups, rather than another greedy
room-feedback pass.
