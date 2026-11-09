#!/usr/bin/env python3
"""Room-feedback repair of the time seed (W6b).

Given a time-feasible run and a room assignment (sol.json from roomsearch),
move classes to alternative candidate times where one of their allowed rooms is
free. Time hard constraints are checked with localsearch.Problem; the final
room assignment and scoring are still done by roomsearch.py / score.py.

Unlike a generic time search, this targets the classes that actually collide in
the current room assignment. It is intended as a seed improver before the GA.

Usage:
  python3 tools/roomfeedback.py <meta.json> <run.json> <sol.json> <out-run.json>
Params: Seed=1 TimeLimit=120 Verbose=1
"""

import datetime
import json
import random
import sys
import time
from collections import defaultdict

sys.path.insert(0, ".")
import localsearch as LS
import roomsearch as RS


def main():
    args = sys.argv[1:]
    meta_path, run_path, sol_path, out_path = args[0], args[1], args[2], args[3]
    params = {}
    for a in args[4:]:
        k, v = a.split("=", 1)
        params[k] = v
    seed = int(params.get("Seed", 1))
    t_limit = float(params.get("TimeLimit", 120))
    verbose = params.get("Verbose", "1") == "1"

    meta = json.load(open(meta_path))
    run = json.load(open(run_path))
    sol = json.load(open(sol_path))
    classes, chosen = RS.pin_candidates(meta, run)
    rooms = meta["rooms"]
    rng = random.Random(seed)

    pb = LS.Problem(meta)
    for cid, (k, _t) in chosen.items():
        pb.set_candidate(pb.idx[cid], k)

    # hard SameRoom groups; do not move a single member of a group in this
    # pass (the group room would become inconsistent).
    parent = {c["classId"]: c["classId"] for c in meta["classes"]}

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
        if d.get("required") and d["type"].startswith("SameRoom"):
            for x in d["classes"][1:]:
                union(d["classes"][0], x)
    grouped = set()
    for c in meta["classes"]:
        if find(c["classId"]) != c["classId"]:
            grouped.add(c["classId"])
        else:
            # a representative with at least one other member is grouped too
            pass
    rep_count = defaultdict(int)
    for c in meta["classes"]:
        rep_count[find(c["classId"])] += 1
    for c in meta["classes"]:
        if rep_count[find(c["classId"])] > 1:
            grouped.add(c["classId"])

    room = {sc["classId"]: sc.get("room") for sc in sol["classes"]}

    def mt(cid, t=None):
        if t is None:
            t = chosen[cid][1]
        return [(wi, di, t["start"], t["length"])
                for wi, chw in enumerate(t["weeks"]) if chw == "1"
                for di, chd in enumerate(t["days"]) if chd == "1"]

    def allowed(cid, t):
        c = classes[cid]
        out = []
        for r in c["rooms"]:
            rid = r["id"]
            rt = rooms.get(str(rid)) or rooms.get(rid)
            if rt is None:
                continue
            if any(RS.room_blocked(rt["unavailable"], w, d, s, l)
                   for (w, d, s, l) in mt(cid, t)):
                continue
            out.append(rid)
        return out

    # occupancy from the fixed room assignment
    occ = defaultdict(list)
    for cid, rid in room.items():
        if rid is None or cid not in chosen:
            continue
        for (w, d, s, l) in mt(cid):
            occ[(w, d, rid)].append((cid, s, l))

    def conflicts(cid, rid, t=None):
        cnt = 0
        for (w, d, s, l) in mt(cid, t):
            for (cid2, s2, l2) in occ.get((w, d, rid), []):
                if cid2 != cid and RS.overlap(s, l, s2, l2):
                    cnt += 1
        return cnt

    def bad_classes():
        bad = set()
        for (w, d, rid), bucket in occ.items():
            for a in range(len(bucket)):
                for b in range(a + 1, len(bucket)):
                    if RS.overlap(bucket[a][1], bucket[a][2],
                                  bucket[b][1], bucket[b][2]):
                        bad.add(bucket[a][0])
                        bad.add(bucket[b][0])
        return bad

    def total_conflicts():
        cnt = 0
        for (w, d, rid), bucket in occ.items():
            for a in range(len(bucket)):
                for b in range(a + 1, len(bucket)):
                    if RS.overlap(bucket[a][1], bucket[a][2],
                                  bucket[b][1], bucket[b][2]):
                        cnt += 1
        return cnt

    t0 = time.time()
    start_conf = total_conflicts()
    moved = 0
    while time.time() - t0 < t_limit:
        bad = [cid for cid in bad_classes() if cid not in grouped]
        if not bad:
            break
        cid = rng.choice(bad)
        i = pb.idx[cid]
        c = classes[cid]
        cur_room = room.get(cid)
        cur_conf = conflicts(cid, cur_room) if cur_room is not None else 10**9
        best = None  # (conf, penalty, k, rid)
        old_k = int(pb.chosen[i])
        old_t = c["times"][old_k]
        # same-time room change (only for singleton units)
        if cid not in grouped and cur_room is not None:
            room_best = None
            for rid in allowed(cid, old_t):
                cf = conflicts(cid, rid)
                pen = next((r.get("penalty", 0) for r in c["rooms"]
                            if r["id"] == rid), 0)
                key = (cf, pen)
                if room_best is None or key < (room_best[0], room_best[1]):
                    room_best = (cf, pen, rid)
            if room_best is not None and room_best[0] < cur_conf:
                cf, pen, rid = room_best
                # update occupancy in place
                for (w, d, s, l) in mt(cid):
                    bucket = occ[(w, d, cur_room)]
                    for x in range(len(bucket) - 1, -1, -1):
                        if bucket[x][0] == cid:
                            del bucket[x]
                            break
                room[cid] = rid
                for (w, d, s, l) in mt(cid):
                    occ[(w, d, rid)].append((cid, s, l))
                moved += 1
                if verbose:
                    print(f"room move class {cid}: {cur_conf} -> {cf} "
                          f"(total {total_conflicts()})", flush=True)
                continue
        for k, t in enumerate(c["times"]):
            if k == old_k:
                continue
            if pb.violated_partners(i, k):
                continue
            for rid in allowed(cid, t):
                cf = conflicts(cid, rid, t)
                pen = next((r.get("penalty", 0) for r in c["rooms"]
                            if r["id"] == rid), 0)
                key = (cf, pen)
                if best is None or key < (best[0], best[1]):
                    best = (cf, pen, k, rid)
                    if cf == 0 and pen == 0:
                        break
            if best is not None and best[0] == 0:
                break
        if best is not None and best[0] < cur_conf:
            cf, pen, k, rid = best
            before_total = total_conflicts()
            old_ms = mt(cid)
            # remove old occupancy
            for (w, d, s, l) in old_ms:
                bucket = occ[(w, d, cur_room)]
                for x in range(len(bucket) - 1, -1, -1):
                    if bucket[x][0] == cid:
                        del bucket[x]
                        break
            pb.set_candidate(i, k)
            chosen[cid] = (k, c["times"][k])
            room[cid] = rid
            for (w, d, s, l) in mt(cid):
                occ[(w, d, rid)].append((cid, s, l))
            after_total = total_conflicts()
            if after_total >= before_total:
                # revert: not a global improvement
                for (w, d, s, l) in mt(cid):
                    bucket = occ[(w, d, rid)]
                    for x in range(len(bucket) - 1, -1, -1):
                        if bucket[x][0] == cid:
                            del bucket[x]
                            break
                pb.set_candidate(i, old_k)
                chosen[cid] = (old_k, c["times"][old_k])
                room[cid] = cur_room
                for (w, d, s, l) in old_ms:
                    occ[(w, d, cur_room)].append((cid, s, l))
                if verbose:
                    print(f"reject class {cid}: no global gain "
                          f"({before_total}->{after_total})", flush=True)
                continue
            moved += 1
            if verbose:
                print(f"move class {cid}: {cur_conf} -> {cf} conflicts "
                      f"(total {before_total}->{after_total})", flush=True)
        else:
            # no improving move; perturb a bad class to a random feasible time
            for _ in range(10):
                cid = rng.choice(bad)
                i = pb.idx[cid]
                c = classes[cid]
                ks = list(range(len(c["times"])))
                rng.shuffle(ks)
                for k in ks:
                    if pb.violated_partners(i, k):
                        continue
                    allowed_rooms = allowed(cid, c["times"][k])
                    if not allowed_rooms:
                        continue
                    rid = rng.choice(allowed_rooms)
                    old_room = room.get(cid)
                    for (w, d, s, l) in mt(cid):
                        bucket = occ[(w, d, old_room)]
                        for x in range(len(bucket) - 1, -1, -1):
                            if bucket[x][0] == cid:
                                del bucket[x]
                                break
                    pb.set_candidate(i, k)
                    chosen[cid] = (k, c["times"][k])
                    room[cid] = rid
                    for (w, d, s, l) in mt(cid):
                        occ[(w, d, rid)].append((cid, s, l))
                    moved += 1
                    break
                break

    end_conf = total_conflicts()
    print(f"roomfeedback: conflicts {start_conf} -> {end_conf}, moved={moved}, "
          f"seconds={time.time()-t0:.1f}")

    base = datetime.date(*meta["baseDate"])
    spair = {s: i + 1 for i, s in enumerate(meta["starts"])}
    slots = []
    for c in meta["classes"]:
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
    out = dict(algorithm="roomfeedback",
               results=[dict(name="roomfeedback", totalEstimation=0,
                             error=None, slots=slots)])
    with open(out_path, "w") as f:
        json.dump(out, f, separators=(",", ":"))
    sol_out = out_path + ".sol.json"
    sol = dict(classes=[])
    for c in meta["classes"]:
        cid = c["classId"]
        k = int(pb.chosen[pb.idx[cid]])
        t = c["times"][k]
        sol["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=room.get(cid)))
    with open(sol_out, "w") as f:
        json.dump(sol, f)
    print("wrote", out_path, "and", sol_out)


if __name__ == "__main__":
    main()
