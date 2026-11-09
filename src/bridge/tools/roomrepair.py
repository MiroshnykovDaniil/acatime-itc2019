#!/usr/bin/env python3
"""Room-aware repair of a time-feasible schedule (W6b).

The time min-conflicts search in tools/localsearch.py is room-blind. On large
instances this produces time structures that cannot be given rooms: too many
classes overlap at the same (week, day, start), or a class has no allowed room
(capacity/unavailability), or a hard SameRoom group has no common room.

Room capacity is not compared with class limit (ITC FAQ I1: the room domain is
already suitable). This tool keeps the time assignment hard-feasible and
hill-climbs a room pressure score:

  score = sum_cells max(0, demand(cell) - N_ROOMS)
        + NOROOM_PEN * classes with no allowed room
        + GROUP_PEN * hard SameRoom groups with no common allowed room

A cell is (week, day, start) for every start covered by a meeting interval, so
overlapping intervals contribute to the same buckets. The final room assignment
is still done by tools/roomsearch.py (which enforces one room per class and
SameRoom groups); tools/score.py remains the referee.

Usage:
  python3 tools/roomrepair.py <meta.json> <run.json> <out-run.json> [key=value...]
Params:
  Seed=1 TimeLimit=120 TriesPerStep=30 NoRoomPenalty=1000 GroupPenalty=1000
"""

import datetime
import json
import random
import sys
import time
from collections import Counter, defaultdict

sys.path.insert(0, ".")
import localsearch as LS
import roomsearch as RS

NOROOM_PEN = 1000
GROUP_PEN = 1000


def meetings_of(t):
    return [(wi, di, t["start"], t["length"])
            for wi, chw in enumerate(t["weeks"]) if chw == "1"
            for di, chd in enumerate(t["days"]) if chd == "1"]


def main():
    args = sys.argv[1:]
    meta_path, run_path, out_path = args[0], args[1], args[2]
    params = {}
    for a in args[3:]:
        k, v = a.split("=", 1)
        params[a.split("=", 1)[0]] = v
    seed = int(params.get("Seed", 1))
    t_limit = float(params.get("TimeLimit", 120))
    tries = int(params.get("TriesPerStep", 30))
    no_room_pen = int(params.get("NoRoomPenalty", NOROOM_PEN))
    group_pen = int(params.get("GroupPenalty", GROUP_PEN))
    match_pen = int(params.get("MatchPenalty", 200))
    verbose = params.get("Verbose", "1") == "1"

    meta = json.load(open(meta_path))
    run = json.load(open(run_path))
    classes, chosen = RS.pin_candidates(meta, run)
    classes_list = meta["classes"]
    cid_to_i = {c["classId"]: i for i, c in enumerate(classes_list)}
    starts = meta["starts"]
    rooms = meta["rooms"]
    n_rooms = len(rooms)
    rng = random.Random(seed)

    pb = LS.Problem(meta)
    for cid, (k, _t) in chosen.items():
        if cid in pb.idx:
            pb.set_candidate(pb.idx[cid], k)

    # hard SameRoom groups (all classes, including roomless for diagnostics)
    parent = {c["classId"]: c["classId"] for c in classes_list}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    for d in meta.get("distributions", []):
        if not d.get("required") or not d["type"].startswith("SameRoom"):
            continue
        members = [cid for cid in d["classes"] if cid in parent]
        for x in members[1:]:
            union(members[0], x)

    group_members = defaultdict(list)
    for c in classes_list:
        group_members[find(c["classId"])].append(c["classId"])
    groups = [m for m in group_members.values() if len(m) > 1]
    group_of = {}
    for gi, members in enumerate(groups):
        for cid in members:
            group_of[cid] = gi

    allowed_cache = {}

    def allowed_set(cid, k):
        key = (cid, k)
        if key in allowed_cache:
            return allowed_cache[key]
        c = classes[cid]
        t = c["times"][k]
        ms = meetings_of(t)
        ok = set()
        for r in c["rooms"]:
            rid = r["id"]
            rt = rooms.get(str(rid)) or rooms.get(rid)
            if rt is None:
                continue
            if any(RS.room_blocked(rt["unavailable"], w, d, s, l)
                   for (w, d, s, l) in ms):
                continue
            ok.add(rid)
        allowed_cache[key] = ok
        return ok

    def cells_of(cid, k):
        t = classes[cid]["times"][k]
        out = []
        for (w, d, s, l) in meetings_of(t):
            for p in starts:
                if s <= p < s + l:
                    out.append((w, d, p))
        return out

    def group_bad(gi, override=None):
        members = groups[gi]
        common = None
        for cid in members:
            if override is not None and cid == override[0]:
                k = override[1]
            else:
                k = int(pb.chosen[pb.idx[cid]])
            a = allowed_set(cid, k)
            if not a:
                return True
            common = set(a) if common is None else (common & a)
            if not common:
                return True
        return common is not None and len(common) == 0

    cell_count = Counter()
    cell_classes = defaultdict(set)
    for c in classes_list:
        cid = c["classId"]
        i = pb.idx[cid]
        k = int(pb.chosen[i])
        for cell in cells_of(cid, k):
            cell_count[cell] += 1
            cell_classes[cell].add(cid)

    def deficiency_cell(cell):
        """Max-matching deficiency for classes active in this cell."""
        active = list(cell_classes[cell])
        if not active:
            return 0
        allowed = {}
        for cid in active:
            i = pb.idx[cid]
            allowed[cid] = allowed_set(cid, int(pb.chosen[i]))
        match = {}

        def dfs(cid, seen):
            for rid in allowed[cid]:
                if rid in seen:
                    continue
                seen.add(rid)
                if rid not in match or dfs(match[rid], seen):
                    match[rid] = cid
                    return True
            return False

        matched = 0
        for cid in sorted(active, key=lambda c: len(allowed[c])):
            if dfs(cid, set()):
                matched += 1
        return len(active) - matched

    def current_no_room():
        out = set()
        for c in classes_list:
            cid = c["classId"]
            if not c["hasRoom"]:
                continue
            i = pb.idx[cid]
            k = int(pb.chosen[i])
            if not allowed_set(cid, k):
                out.add(cid)
        return out

    no_room = current_no_room()
    bad_groups = {gi for gi in range(len(groups)) if group_bad(gi)}
    if match_pen:
        def_cache = {cell: deficiency_cell(cell) for cell in cell_classes}
        total_def = sum(def_cache.values())
    else:
        # A peak-only run must not pay for the expensive matching-deficiency
        # computation; it is useful when the schedule has thousands of room
        # collisions and matching is not yet the limiting term.
        def_cache = {}
        total_def = 0

    def score():
        peak = sum(max(0, n - n_rooms) for n in cell_count.values())
        return (peak + no_room_pen * len(no_room)
                + group_pen * len(bad_groups)
                + match_pen * nonlocal_total_def[0], peak, nonlocal_total_def[0])

    nonlocal_total_def = [total_def]

    def apply(cid, k):
        i = pb.idx[cid]
        old_k = int(pb.chosen[i])
        old_cells = cells_of(cid, old_k)
        new_cells = cells_of(cid, k)
        for cell in old_cells:
            cell_count[cell] -= 1
            cell_classes[cell].discard(cid)
        pb.set_candidate(i, k)
        for cell in new_cells:
            cell_count[cell] += 1
            cell_classes[cell].add(cid)
        if classes[cid]["hasRoom"]:
            if allowed_set(cid, k):
                no_room.discard(cid)
            else:
                no_room.add(cid)
        if match_pen:
            for cell in set(old_cells) | set(new_cells):
                old = def_cache.get(cell, 0)
                new = deficiency_cell(cell)
                def_cache[cell] = new
                nonlocal_total_def[0] += new - old
        gi = group_of.get(cid)
        if gi is not None:
            if group_bad(gi):
                bad_groups.add(gi)
            else:
                bad_groups.discard(gi)

    def candidate_delta(cid, k):
        i = pb.idx[cid]
        if pb.violated_partners(i, k):
            return None
        old_k = int(pb.chosen[i])
        old_cells = cells_of(cid, old_k)
        new_cells = cells_of(cid, k)
        for cell in old_cells:
            cell_count[cell] -= 1
        delta = 0
        nc = Counter(new_cells)
        for cell, add in nc.items():
            old = cell_count[cell]
            delta += max(0, old + add - n_rooms) - max(0, old - n_rooms)
        # no-room term
        old_no = cid in no_room
        new_no = bool(classes[cid]["hasRoom"]) and not allowed_set(cid, k)
        delta += no_room_pen * (int(new_no) - int(old_no))
        # group term
        gi = group_of.get(cid)
        if gi is not None:
            old_bad = gi in bad_groups
            new_bad = group_bad(gi, override=(cid, k))
            delta += group_pen * (int(new_bad) - int(old_bad))
        # matching-deficiency term
        if match_pen:
            affected = set(old_cells) | set(new_cells)
            for cell in old_cells:
                cell_classes[cell].discard(cid)
            for cell in new_cells:
                cell_classes[cell].add(cid)
            for cell in affected:
                old = def_cache.get(cell, 0)
                new = deficiency_cell(cell)
                delta += match_pen * (new - old)
            for cell in new_cells:
                cell_classes[cell].discard(cid)
            for cell in old_cells:
                cell_classes[cell].add(cid)
        for cell in old_cells:
            cell_count[cell] += 1
        return delta

    def pool():
        out = set(no_room)
        for gi in bad_groups:
            out.update(groups[gi])
        for cell, n in cell_count.items():
            if n > n_rooms or (match_pen and def_cache.get(cell, 0) > 0):
                out.update(cell_classes[cell])
        return out

    best_score, best_peak, best_def = score()
    best_state = list(pb.chosen)
    t0 = time.time()
    it = 0
    while time.time() - t0 < t_limit:
        it += 1
        cands = list(pool())
        if not cands:
            break
        improved = False
        for _ in range(min(tries, len(cands))):
            cid = rng.choice(cands)
            i = pb.idx[cid]
            old_k = int(pb.chosen[i])
            best = (0, None)
            for k in range(len(classes[cid]["times"])):
                if k == old_k:
                    continue
                d = candidate_delta(cid, k)
                if d is not None and d < best[0]:
                    best = (d, k)
            if best[1] is not None:
                apply(cid, best[1])
                improved = True
                break
        sc, peak, tdef = score()
        if sc < best_score:
            best_score, best_peak = sc, peak
            best_state = list(pb.chosen)
            if verbose:
                print(f"it={it} score={sc} peak_excess={peak} match_def={tdef} "
                      f"no_room={len(no_room)} bad_groups={len(bad_groups)} "
                      f"t={time.time()-t0:.1f}s", flush=True)
        if not improved:
            # plateau/perturbation: random feasible move from the pool
            for _ in range(20):
                cid = rng.choice(cands)
                i = pb.idx[cid]
                ks = list(range(len(classes[cid]["times"])))
                rng.shuffle(ks)
                for k in ks:
                    if not pb.violated_partners(i, k):
                        apply(cid, k)
                        break
                else:
                    continue
                break

    # restore best
    for c in classes_list:
        cid = c["classId"]
        i = pb.idx[cid]
        k = int(best_state[i])
        if int(pb.chosen[i]) != k:
            apply(cid, k)
    sc, peak, tdef = score()
    print(f"roomrepair: score={sc} peak_excess={peak} match_def={tdef} no_room={len(no_room)} "
          f"bad_groups={len(bad_groups)} iterations={it} "
          f"seconds={time.time()-t0:.1f}")

    # emit runner-style run json
    base = datetime.date(*meta["baseDate"])
    spair = {s: i + 1 for i, s in enumerate(starts)}
    slots = []
    for c in classes_list:
        cid = c["classId"]
        k = int(pb.chosen[pb.idx[cid]])
        t = c["times"][k]
        pair = spair[t["start"]]
        lesson = 0
        for wi, chw in enumerate(t["weeks"]):
            if chw != "1":
                continue
            for di, chd in enumerate(t["days"]):
                if chd != "1":
                    continue
                d = base + datetime.timedelta(days=7 * wi + di)
                lesson += 1
                slots.append(dict(classId=cid, lessonNumber=lesson,
                                  date=d.isoformat(), pair=pair))
    out = dict(algorithm="roomrepair",
               results=[dict(name="roomrepair", totalEstimation=0,
                             error=None, slots=slots)])
    with open(out_path, "w") as f:
        json.dump(out, f, separators=(",", ":"))
    print("wrote", out_path)


if __name__ == "__main__":
    main()
