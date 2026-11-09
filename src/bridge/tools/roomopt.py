#!/usr/bin/env python3
"""Room-cost optimisation for a fixed, room-feasible time schedule.

The time search and roomsearch assign rooms to avoid collisions, but they do
not optimise *soft* SameRoom distributions.  This tool takes a feasible
solution (0 room collisions) and improves the room assignment for:

  room penalty  +  weight_distribution * (soft SameRoom violations)

It only changes rooms, never times.  Hard SameRoom groups are left untouched;
moving a class that belongs to a hard group would require moving the whole
group and is not attempted here.  Room availability and collisions are always
re-checked with the same rules as roomsearch.py; score.py remains the referee.

The main move is a group LNS: try to place a soft-SameRoom group in one room,
evict the classes blocking that room, and greedily reinsert the evicted
classes.  The move is accepted only when it keeps the schedule feasible and
reduces the room-soft objective.

Usage:
  python3 tools/roomopt.py <meta.json> <run.json> <sol-in.json> <sol-out.json>
      [Seed=1] [TimeLimit=120] [Verbose=1]
"""

import json
import random
import sys
import time

sys.path.insert(0, ".")
import roomsearch as RS


def main():
    args = sys.argv[1:]
    if len(args) < 4:
        raise SystemExit(__doc__)
    meta_path, run_path, sol_in_path, sol_out_path = args[:4]
    params = {}
    for a in args[4:]:
        k, v = a.split("=", 1)
        params[k] = v
    seed = int(params.get("Seed", 1))
    time_limit = float(params.get("TimeLimit", 120))
    verbose = params.get("Verbose", "1") == "1"

    meta = json.load(open(meta_path))
    run = json.load(open(run_path))
    sol = json.load(open(sol_in_path))
    classes, chosen = RS.pin_candidates(meta, run)

    solver = RS.RoomSolver(meta, classes, chosen, seed=seed)
    init_rooms = {sc["classId"]: sc.get("room") for sc in sol.get("classes", [])}
    for i, cid in enumerate(solver.ids):
        rid = init_rooms.get(cid)
        if rid is not None:
            solver.place(i, rid)
    if solver.total_conflicts() != 0:
        raise SystemExit("roomopt: input solution is not room-feasible")

    hard = set()
    for d in meta.get("distributions", []):
        if d.get("required") and d.get("type", "").startswith("SameRoom"):
            for cid in d["classes"]:
                if cid in solver.idx:
                    hard.add(solver.idx[cid])

    soft = []
    for d in meta.get("distributions", []):
        if d.get("required") or not d.get("type", "").startswith("SameRoom"):
            continue
        members = [solver.idx[cid] for cid in d["classes"] if cid in solver.idx]
        if len(members) >= 2:
            soft.append((members, int(d.get("penalty", 0))))

    room_pen = {i: {rid: pen for rid, pen in solver.allowed[i]}
                for i in range(len(solver.ids))}
    w_dist = meta["weights"]["distribution"]

    def cost():
        rp = sum(room_pen[i][solver.room[i]]
                 for i in range(len(solver.ids)) if solver.room[i] >= 0)
        sp = 0
        for members, pen in soft:
            for a in range(len(members)):
                for b in range(a + 1, len(members)):
                    if solver.room[members[a]] != solver.room[members[b]]:
                        sp += pen
        return rp + w_dist * sp, rp, sp

    def group_overlap(members):
        for a in range(len(members)):
            for b in range(a + 1, len(members)):
                for (wa, da, sa, la) in solver.meetings[members[a]]:
                    for (wb, db, sb, lb) in solver.meetings[members[b]]:
                        if (wa, da) == (wb, db) and RS.overlap(sa, la, sb, lb):
                            return True
        return False

    def blockers_for(members, rid):
        blockers = set()
        for i in members:
            for (w, d, s, l) in solver.meetings[i]:
                for (j, s2, l2) in solver.occ[rid].get((w, d), []):
                    if j not in members and RS.overlap(s, l, s2, l2):
                        blockers.add(j)
        return blockers

    def unassign(members):
        for i in members:
            solver.unplace(i)

    def reinsert(members):
        order = sorted(members, key=lambda i: (len(solver.allowed[i]),
                                               -len(solver.meetings[i])))
        for i in order:
            best = None
            for rid, pen in solver.allowed[i]:
                if solver.conflicts(i, rid) != 0:
                    continue
                key = (pen, rid)
                if best is None or key < best[0]:
                    best = (key, rid)
            if best is None:
                return False
            solver.place(i, best[1])
        return True

    base_cost, base_room, base_soft = cost()
    if verbose:
        print(f"roomopt: base={base_cost} (room={base_room}, sameRoom={base_soft}) "
              f"classes={len(solver.ids)} soft_groups={len(soft)}")

    t0 = time.time()
    rng = random.Random(seed)
    improved = 0
    for _round in range(100):
        if time.time() - t0 > time_limit:
            break
        changed = False
        order = list(range(len(soft)))
        rng.shuffle(order)
        for gi in order:
            if time.time() - t0 > time_limit:
                break
            members, _pen = soft[gi]
            if any(i in hard for i in members) or group_overlap(members):
                continue
            common = set(room_pen[members[0]])
            for i in members[1:]:
                common &= set(room_pen[i])
            common = list(common)
            rng.shuffle(common)

            old_cost = cost()[0]
            best = None
            for rid in common[:12]:
                blockers = blockers_for(members, rid)
                if any(i in hard for i in blockers):
                    continue
                old_rooms = {i: solver.room[i] for i in list(members) + list(blockers)}
                unassign(list(members) + list(blockers))
                for i in members:
                    solver.place(i, rid)
                if solver.total_conflicts() == 0 and reinsert(list(blockers)):
                    new_cost = cost()[0]
                    if new_cost < old_cost and (best is None or new_cost < best[0]):
                        best = (new_cost, rid, list(blockers))
                unassign(list(members) + list(blockers))
                for i, rr in old_rooms.items():
                    if rr >= 0:
                        solver.place(i, rr)

            if best is None:
                continue
            new_cost, rid, blockers = best
            old_rooms = {i: solver.room[i] for i in list(members) + blockers}
            unassign(list(members) + blockers)
            for i in members:
                solver.place(i, rid)
            if not reinsert(blockers):
                unassign(list(members) + blockers)
                for i, rr in old_rooms.items():
                    if rr >= 0:
                        solver.place(i, rr)
                continue
            changed = True
            improved += 1
            if verbose:
                print(f"  move group {gi} -> room {rid} cost={new_cost} "
                      f"blockers={len(blockers)} t={time.time()-t0:.2f}s")
        if not changed:
            break

    if solver.total_conflicts() != 0:
        raise SystemExit("roomopt: internal error, output is not room-feasible")
    final_cost, final_room, final_soft = cost()
    if verbose:
        print(f"roomopt: final={final_cost} (room={final_room}, "
              f"sameRoom={final_soft}), moves={improved}, "
              f"seconds={time.time()-t0:.2f}")

    out = {"classes": []}
    for cid, (k, t) in chosen.items():
        rid = None
        if cid in solver.idx:
            rid = solver.room[solver.idx[cid]]
            if rid < 0:
                rid = None
        out["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=rid))
    with open(sol_out_path, "w") as f:
        json.dump(out, f)
    print("wrote", sol_out_path)


if __name__ == "__main__":
    main()
