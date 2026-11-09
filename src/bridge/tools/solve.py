#!/usr/bin/env python3
"""Feasibility orchestrator: time min-conflicts -> room min-conflicts -> joint repair.

Pipeline:
  1. tools/localsearch min-conflicts over class time candidates (hard ITC
     constraints incl. forced-room pairs for singleton-room classes).
  2. tools/roomsearch min-conflicts over room choices.
  3. If room conflicts remain (cell oversubscription), joint moves: change a
     conflicted class's TIME to a candidate with fewer time/room conflicts,
     repair the induced time violations, continue room repair.
Referee stays tools/score.py.

Usage:
  python3 tools/solve.py <meta.json> <out-sol.json> [key=value ...]
Params:
  Seed=1 TimeLimit=300 TimeLimitTime=120 TimeLimitRoom=60
"""

import json
import random
import sys
import time

import localsearch as LS
import roomsearch as RS


def meetings_of_cand(t):
    ms = []
    for wi, chw in enumerate(t["weeks"]):
        if chw == "1":
            for di, chd in enumerate(t["days"]):
                if chd == "1":
                    ms.append((wi, di, t["start"], t["length"]))
    return ms


def allowed_rooms(c, t, rooms):
    ms = meetings_of_cand(t)
    ok = []
    for r in c["rooms"]:
        rt = rooms.get(str(r["id"])) or rooms.get(r["id"])
        if rt is None:
            continue
        if any(RS.room_blocked(rt["unavailable"], w, d, s, l) for (w, d, s, l) in ms):
            continue
        ok.append((r["id"], r.get("penalty", 0)))
    if not ok:
        ok = [(r["id"], r.get("penalty", 0)) for r in c["rooms"]]
    return ok


def conflicted_details(solver):
    """[(i, j, rid, (w,d), start, len)] conflict pairs."""
    out = []
    for rid, cells in solver.occ.items():
        for cell, bucket in cells.items():
            for a in range(len(bucket)):
                for b in range(a + 1, len(bucket)):
                    if RS.overlap(bucket[a][1], bucket[a][2],
                                  bucket[b][1], bucket[b][2]):
                        out.append((bucket[a][0], bucket[b][0], rid, cell,
                                    bucket[a][1], bucket[a][2]))
    return out


def mover_candidates(solver, conflicts, classes, rng):
    """Conflict classes plus the occupants of the rooms they compete for
    (those are often the classes that must actually move)."""
    movers = set()
    for (i, j, rid, cell, s, l) in conflicts:
        movers.add(i)
        movers.add(j)
    # rooms wanted by the conflicted classes
    want_rooms = set()
    for i in list(movers):
        for (rid2, pen) in solver.allowed[i]:
            want_rooms.add(rid2)
    for (i, j, rid, cell, s, l) in conflicts:
        for rid2 in want_rooms:
            bucket = solver.occ.get(rid2, {}).get(cell)
            if not bucket:
                continue
            for (j2, s2, l2) in bucket:
                if RS.overlap(s, l, s2, l2):
                    movers.add(j2)
    movable = [i for i in movers if len(classes[solver.ids[i]]["times"]) > 1]
    pool = movable or list(movers)
    pool = sorted(pool)
    return pool


def main():
    args = sys.argv[1:]
    meta_path, out_path = args[0], args[1]
    params = {}
    for a in args[2:]:
        k, v = a.split("=", 1)
        params[k] = v
    seed = int(params.get("Seed", 1))
    t_total = float(params.get("TimeLimit", 300))
    t_time = float(params.get("TimeLimitTime", 120))
    t_room = float(params.get("TimeLimitRoom", 60))
    verbose = params.get("Verbose", "1") == "1"

    t0 = time.time()
    meta = json.load(open(meta_path))
    classes = {c["classId"]: c for c in meta["classes"]}
    rooms = meta["rooms"]
    rng = random.Random(seed)

    pb = LS.Problem(meta)
    init_sol = params.get("InitSol")
    if init_sol:
        sol0 = json.load(open(init_sol))
        for sc in sol0["classes"]:
            if sc["classId"] in pb.idx:
                pb.set_candidate(pb.idx[sc["classId"]], int(sc["candidate"]))
        hard0 = pb.all_violating_pairs()
        print(f"phase1: loaded init solution, hard violations={len(hard0)}", flush=True)
        st = {"violations": len(hard0)}
        if hard0:
            st = LS.solve(pb, seed=seed, time_limit=30, noise=0.12,
                          restart_after=2500, verbose=False, reinit=False)
            print("phase1 repair:", st, flush=True)
    else:
        st = LS.solve(pb, seed=seed, time_limit=t_time, noise=0.12,
                      restart_after=2500, verbose=verbose)
        print("phase1 time:", st, flush=True)
    if st["violations"] != 0:
        print("WARNING: time constraints not fully satisfied; continuing anyway")

    chosen = {}
    for cid in classes:
        if cid in pb.idx:
            k = int(pb.chosen[pb.idx[cid]])
            chosen[cid] = (k, classes[cid]["times"][k])

    solver = RS.RoomSolver(meta, classes, chosen, seed=seed)
    st = solver.solve(time_limit=t_room, noise=0.1, restart_after=2000,
                      verbose=verbose)
    print("phase2 rooms:", st, flush=True)

    rounds = 0
    tabu = []  # recent (cid, k) joint moves
    while solver.total_conflicts() > 0 and time.time() - t0 < t_total:
        rounds += 1
        conflicts = conflicted_details(solver)
        if not conflicts:
            break
        i = mover_candidates(solver, conflicts, classes, rng)
        i = rng.choice(i)
        cid = solver.ids[i]
        c = classes[cid]
        pi = pb.idx[cid]
        solver.unplace(i)

        best_key, best_list = None, []
        attempts = 0
        while best_key is None and attempts < 2:
            attempts += 1
            for k in range(len(c["times"])):
                if attempts == 1 and (cid, k) in tabu and len(c["times"]) > 1:
                    continue
                t = c["times"][k]
                tv = len(pb.violated_partners(pi, k))
                ms = meetings_of_cand(t)
                solver.meetings[i] = ms
                ok = allowed_rooms(c, t, rooms)
                for (rid, pen) in ok:
                    rc = solver.conflicts(i, rid)
                    key = (rc, tv, pen)
                    if best_key is None or key < best_key:
                        best_key, best_list = key, [(k, rid, ms, ok)]
                    elif key == best_key:
                        best_list.append((k, rid, ms, ok))
            if best_key is None:
                tabu.clear()
        best = None
        if best_list:
            k, rid, ms, ok = rng.choice(best_list)
            best = (best_key, k, rid, ms, ok)
            tabu.append((cid, k))
            if len(tabu) > 8:
                tabu.pop(0)
        if best is None:
            # no feasible room at any candidate: restore previous placement
            solver.meetings[i] = meetings_of_cand(chosen[cid][1])
            solver.allowed[i] = allowed_rooms(c, chosen[cid][1], rooms)
            prev = chosen[cid][0]
            best_r, best_k = None, None
            for (rid2, pen2) in solver.allowed[i]:
                key2 = (solver.conflicts(i, rid2), pen2)
                if best_k is None or key2 < best_k:
                    best_r, best_k = rid2, key2
            solver.place(i, best_r)
            break
        key, k, rid, ms, ok = best
        pb.set_candidate(pi, k)
        solver.meetings[i] = ms
        solver.allowed[i] = ok
        solver.place(i, rid)
        if verbose:
            print(f"  joint move round {rounds}: class {cid} -> cand {k} room {rid} "
                  f"(tv={key[0]} rc={key[1]})", flush=True)

        # repair induced time violations, then continue room repair
        LS.solve(pb, seed=seed + rounds, time_limit=15, max_iters=4000,
                 noise=0.12, restart_after=2000, verbose=False, reinit=False)
        # refresh room meetings for any classes whose time changed during repair
        changed = False
        for cid2, (k2, t2) in chosen.items():
            pi2 = pb.idx[cid2]
            k_now = int(pb.chosen[pi2])
            if k_now != k2:
                changed = True
                chosen[cid2] = (k_now, classes[cid2]["times"][k_now])
                i2 = solver.idx.get(cid2)
                if i2 is not None:
                    solver.unplace(i2)
                    solver.meetings[i2] = meetings_of_cand(chosen[cid2][1])
                    solver.allowed[i2] = allowed_rooms(classes[cid2], chosen[cid2][1], rooms)
                    best_r, best_k = None, None
                    for (rid2, pen2) in solver.allowed[i2]:
                        key2 = (solver.conflicts(i2, rid2), pen2)
                        if best_k is None or key2 < best_k:
                            best_r, best_k = rid2, key2
                    solver.place(i2, best_r)
        st = solver.solve(time_limit=10, noise=0.1, restart_after=1500,
                          verbose=False, reinit=False)

    # --- final phase: fresh consistent room solver + exhaustive depth-1 moves
    def rebuild():
        ch = {}
        for cid2 in classes:
            if cid2 in pb.idx:
                k2 = int(pb.chosen[pb.idx[cid2]])
                ch[cid2] = (k2, classes[cid2]["times"][k2])
        return RS.RoomSolver(meta, classes, ch, seed=seed)

    solver = rebuild()
    solver.solve(time_limit=20, noise=0.1, restart_after=2000,
                 verbose=False, reinit=True)
    def trial(idx, ms, rid):
        old_ms = solver.meetings[idx]
        old_room = solver.room[idx]
        solver.unplace(idx)
        solver.meetings[idx] = ms
        solver.place(idx, rid)
        v = solver.total_conflicts()
        solver.unplace(idx)
        solver.meetings[idx] = old_ms
        if old_room >= 0:
            solver.place(idx, old_room)
        return v

    visited = set()
    for step in range(60):
        conf = solver.total_conflicts()
        if conf == 0:
            break
        conflicts = conflicted_details(solver)
        movers = mover_candidates(solver, conflicts, classes, rng)
        best = (conf, None)
        for idx in movers:
            cid2 = solver.ids[idx]
            c2 = classes[cid2]
            pi2 = pb.idx[cid2]
            for k2 in range(len(c2["times"])):
                if len(pb.violated_partners(pi2, k2)) > 0:
                    continue
                t2 = c2["times"][k2]
                ms2 = meetings_of_cand(t2)
                ok2 = allowed_rooms(c2, t2, rooms)
                for (rid2, pen2) in ok2:
                    if (cid2, k2, rid2) in visited:
                        continue
                    v = trial(idx, ms2, rid2)
                    # plateau acceptance: v may equal the current count; the
                    # visited set prevents cycling while enabling prerequisite
                    # moves (free a room, then place the blocked class there)
                    if v < best[0] or (v == best[0] and best[1] is None):
                        best = (v, (idx, cid2, k2, rid2, ms2, ok2, pen2))
        if best[1] is None:
            break
        visited.add((best[1][1], best[1][2], best[1][3]))
        idx, cid2, k2, rid2, ms2, ok2, pen2 = best[1]
        solver.unplace(idx)
        solver.meetings[idx] = ms2
        solver.allowed[idx] = ok2
        solver.place(idx, rid2)
        pb.set_candidate(pb.idx[cid2], k2)
        if verbose:
            print(f"  exhaustive step {step+1}: class {cid2} -> cand {k2} room {rid2} "
                  f"conflicts {best[0]}", flush=True)

    # --- targeted deadlock breaker: place a conflicted class into a blocked
    # room by explicitly relocating the blocker(s) to non-overlapping times.
    def break_deadlock():
        confs = conflicted_details(solver)
        if verbose:
            print("  breaker conflicts:", [(solver.ids[a], solver.ids[b], rr, cc)
                                           for (a, b, rr, cc, s0, l0) in confs])
        for (i, j, rid, cell, s, l) in confs:
            wd = cell
            for idx in (i, j):
                cid = solver.ids[idx]
                c = classes[cid]
                ms_idx = solver.meetings[idx]
                old_room_idx = solver.room[idx]
                for (rid2, pen) in solver.allowed[idx]:
                    if rid2 == rid:
                        continue
                    # blockers of idx in rid2 at its conflict meeting(s)
                    blockers = set()
                    for (w, d, s1, l1) in ms_idx:
                        for (j2, s2, l2) in solver.occ.get(rid2, {}).get((w, d), []):
                            if j2 != idx and RS.overlap(s1, l1, s2, l2):
                                blockers.add(j2)
                    if verbose:
                        print(f"  breaker idx={solver.ids[idx]} room={rid2} blockers={[solver.ids[x] for x in blockers]}")
                    if not blockers:
                        # room is free for idx right now: just move
                        old_room = solver.room[idx]
                        solver.unplace(idx)
                        solver.place(idx, rid2)
                        if solver.total_conflicts() == 0:
                            return True
                        solver.unplace(idx)
                        solver.place(idx, old_room)
                        continue
                    for bi in blockers:
                        bid = solver.ids[bi]
                        bc = classes[bid]
                        bpi = pb.idx[bid]
                        b_old_room = solver.room[bi]
                        b_old_ms = solver.meetings[bi]
                        b_old_allowed = solver.allowed[bi]
                        k_order = sorted(
                            range(len(bc["times"])),
                            key=lambda kk: (len(pb.violated_partners(bpi, kk)),
                                            int(pb.block_pen[bpi][kk])))
                        for k in k_order:
                            t2 = bc["times"][k]
                            ms2 = meetings_of_cand(t2)
                            ok2 = allowed_rooms(bc, t2, rooms)
                            # does the new time still overlap idx's meetings in rid2?
                            still = False
                            for (w, d, s1, l1) in ms_idx:
                                for (w2, d2, s2, l2) in ms2:
                                    if (w, d) == (w2, d2) and RS.overlap(s1, l1, s2, l2):
                                        still = True
                            if still:
                                continue
                            solver.unplace(bi)
                            solver.meetings[bi] = ms2
                            solver.allowed[bi] = ok2
                            for (rid3, pen3) in ok2:
                                solver.place(bi, rid3)
                                solver.unplace(idx)
                                solver.place(idx, rid2)
                                if solver.total_conflicts() == 0:
                                    pb.set_candidate(bpi, k)
                                    return True
                                solver.unplace(idx)
                                solver.place(idx, old_room_idx)
                                solver.unplace(bi)
                            solver.meetings[bi] = b_old_ms
                            solver.allowed[bi] = b_old_allowed
                            solver.place(bi, b_old_room)
        return False

    if solver.total_conflicts() > 0 and break_deadlock():
        print("deadlock broken", flush=True)

    conf = solver.total_conflicts()

    print(f"final: room conflicts={conf} joint_rounds={rounds} "
          f"time_violations={len(pb.all_violating_pairs())} "
          f"total {time.time()-t0:.1f}s")

    sol = dict(classes=[])
    for cid, c in classes.items():
        if cid in pb.idx:
            k = int(pb.chosen[pb.idx[cid]])
        else:
            k = 0
        t = c["times"][k]
        rid = None
        i = solver.idx.get(cid)
        if i is not None and solver.room[i] >= 0:
            rid = solver.room[i]
        sol["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=rid))
    with open(out_path, "w") as f:
        json.dump(sol, f)
    print("wrote", out_path)


if __name__ == "__main__":
    main()
