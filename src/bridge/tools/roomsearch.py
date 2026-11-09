#!/usr/bin/env python3
"""Min-conflicts room assignment for a fixed time assignment.

Input: meta.json + a runner-style run JSON (localsearch output or a DFS run).
Class times are pinned exactly like tools/post.py does; then every class that
needs a room gets one from its candidate list such that:
  - room unavailability is respected (class limit is NOT compared with room
    capacity; ITC FAQ I1 says the listed room domain is already suitable),
  - a room is never double-booked (same week/day, overlapping intervals),
  - hard SameRoom groups receive one common room (GroupRoomSolver).
The official scorer (tools/score.py) remains the referee.

Usage:
  python3 tools/roomsearch.py <meta.json> <run.json> <out-sol.json> [key=value ...]

Params: Seed=1 TimeLimit=60 Noise=0.1 RestartAfter=2000 Verbose=1
"""

import datetime
import json
import os
import random
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def overlap(a_start, a_len, b_start, b_len):
    return a_start < b_start + b_len and b_start < a_start + a_len


def room_blocked(unavail, w, dow, start, length):
    for u in unavail:
        ud, us, ul, uw = u["days"], u["start"], u["length"], u["weeks"]
        if dow < len(ud) and w < len(uw) and ud[dow] == "1" and uw[w] == "1" \
                and overlap(start, length, us, ul):
            return True
    return False


def pin_candidates(meta, run):
    """classId -> (candidate index, time dict), exactly like post.py."""
    classes = {c["classId"]: c for c in meta["classes"]}
    starts = meta["starts"]
    spair = {s: i + 1 for i, s in enumerate(starts)}
    base = tuple(meta["baseDate"])

    results = run.get("results", [])
    if not results:
        raise SystemExit("no results in run file")
    best = max(results, key=lambda r: r.get("totalEstimation", -10**18))
    per_class = defaultdict(list)
    for s in best["slots"]:
        per_class[s["classId"]].append(s)

    chosen, missing = {}, []
    for cid, c in classes.items():
        ss = per_class.get(cid, [])
        if len(ss) != c["meetings"]:
            missing.append(cid)
            continue
        cells = set()
        ok = True
        for s in ss:
            y, m, d = map(int, s["date"].split("-"))
            day_ix = (datetime.date(y, m, d) - datetime.date(*base)).days
            cells.add((day_ix // 7, day_ix % 7, s["pair"]))
        pick = None
        for k, t in enumerate(c["times"]):
            cand_cells = set()
            for wi, chw in enumerate(t["weeks"]):
                if chw == "1":
                    for di, chd in enumerate(t["days"]):
                        if chd == "1":
                            cand_cells.add((wi, di, spair[t["start"]]))
            if cand_cells == cells:
                pick = k
                break
        if pick is None:
            missing.append(cid)
        else:
            chosen[cid] = (pick, c["times"][pick])
    if missing:
        print(f"WARNING: {len(missing)} classes not pinnable: {missing[:8]}")
    return classes, chosen


class RoomSolver:
    def __init__(self, meta, classes, chosen, seed=1, init_rooms=None):
        self.init_rooms = init_rooms or {}
        self.meta = meta
        self.rooms = meta["rooms"]
        self.classes = classes
        self.chosen = chosen
        self.rng = random.Random(seed)

        self.ids = [cid for cid in classes if cid in chosen and classes[cid]["hasRoom"]]
        self.idx = {cid: i for i, cid in enumerate(self.ids)}
        n = len(self.ids)

        # allowed rooms per class (capacity + unavailability at its meetings)
        self.allowed = []
        self.meetings = []
        for cid in self.ids:
            c = classes[cid]
            k, t = chosen[cid]
            ms = []
            for wi, chw in enumerate(t["weeks"]):
                if chw == "1":
                    for di, chd in enumerate(t["days"]):
                        if chd == "1":
                            ms.append((wi, di, t["start"], t["length"]))
            self.meetings.append(ms)
            ok_rooms = []
            for r in c["rooms"]:
                rid = r["id"]
                rt = self.rooms[str(rid)] if str(rid) in self.rooms else self.rooms.get(rid)
                if rt is None:
                    continue
                if any(room_blocked(rt["unavailable"], w, d, s, l) for (w, d, s, l) in ms):
                    continue
                ok_rooms.append((rid, r.get("penalty", 0)))
            if not ok_rooms:
                # fall back to the raw list minus nothing (will be flagged)
                ok_rooms = [(r["id"], r.get("penalty", 0)) for r in c["rooms"]]
            self.allowed.append(ok_rooms)

        self.room = [-1] * n
        # occupancy: rid -> (week,dow) -> list of (classIdx, start, len)
        self.occ = defaultdict(lambda: defaultdict(list))

    def conflicts(self, i, rid):
        """Count conflicts if class i is placed in rid."""
        cnt = 0
        for (w, d, s, l) in self.meetings[i]:
            bucket = self.occ[rid].get((w, d))
            if not bucket:
                continue
            for (j, s2, l2) in bucket:
                if j != i and overlap(s, l, s2, l2):
                    cnt += 1
        return cnt

    def place(self, i, rid):
        self.unplace(i)
        self.room[i] = rid
        for (w, d, s, l) in self.meetings[i]:
            self.occ[rid][(w, d)].append((i, s, l))

    def unplace(self, i):
        rid = self.room[i]
        if rid < 0:
            return
        for (w, d, s, l) in self.meetings[i]:
            bucket = self.occ[rid][(w, d)]
            for x in range(len(bucket) - 1, -1, -1):
                if bucket[x][0] == i:
                    del bucket[x]
                    break
        self.room[i] = -1

    def total_conflicts(self):
        cnt = 0
        for rid, cells in self.occ.items():
            for cell, bucket in cells.items():
                for a in range(len(bucket)):
                    for b in range(a + 1, len(bucket)):
                        if overlap(bucket[a][1], bucket[a][2], bucket[b][1], bucket[b][2]):
                            cnt += 1
        return cnt

    def solve(self, time_limit=60.0, noise=0.1, restart_after=2000, verbose=True,
              reinit=True):
        import time
        t0 = time.time()
        n = len(self.ids)

        # greedy: fewest allowed rooms first
        order = sorted(range(n), key=lambda i: len(self.allowed[i]))
        for i in (order if reinit else []):
            best_rid, best_key = None, None
            for (rid, pen) in self.allowed[i]:
                key = (self.conflicts(i, rid), pen)
                if best_key is None or key < best_key:
                    best_rid, best_key = rid, key
                    if key == (0, 0):
                        break
            self.place(i, best_rid)

        total = self.total_conflicts()
        best_total = total
        best_state = list(self.room)
        stagnation = 0
        it = 0

        while total > 0 and time.time() - t0 < time_limit:
            it += 1
            if stagnation > restart_after:
                for i in range(n):
                    self.place(i, best_state[i])
                for i in self.rng.sample(range(n), max(3, n // 16)):
                    rid = self.rng.choice(self.allowed[i])[0]
                    self.place(i, rid)
                total = self.total_conflicts()
                stagnation = 0
                continue

            # pick a conflicted class: find a conflicting pair quickly
            bad_classes = []
            for rid, cells in self.occ.items():
                for cell, bucket in cells.items():
                    for a in range(len(bucket)):
                        for b in range(a + 1, len(bucket)):
                            if overlap(bucket[a][1], bucket[a][2], bucket[b][1], bucket[b][2]):
                                bad_classes.extend([bucket[a][0], bucket[b][0]])
            if not bad_classes:
                break
            i = self.rng.choice(bad_classes)

            if self.rng.random() < noise:
                rid = self.rng.choice(self.allowed[i])[0]
            else:
                best_rid, best_key = None, None
                for (rid2, pen) in self.allowed[i]:
                    key = (self.conflicts(i, rid2), pen)
                    if best_key is None or key < best_key:
                        best_rid, best_key = rid2, key
                rid = best_rid
            self.place(i, rid)
            total = self.total_conflicts()

            if total < best_total:
                best_total = total
                best_state = list(self.room)
                stagnation = 0
                if verbose:
                    print(f"  it={it} conflicts={total} t={time.time()-t0:.1f}s", flush=True)
            else:
                stagnation += 1

        for i in range(n):
            if self.room[i] != best_state[i]:
                self.place(i, best_state[i])
        return dict(iterations=it, conflicts=best_total, seconds=time.time() - t0)


class GroupRoomSolver(RoomSolver):
    """Room solver that keeps hard SameRoom groups in one room.

    A group is a union of classes connected by required SameRoom
    distributions. All members must receive the same room; the room must be
    allowed (capacity + unavailability) for every member. The solver assigns
    one room per group and counts collisions between groups; overlapping
    meetings inside a group are also counted (they would violate SameRoom).
    """

    def __init__(self, meta, classes, chosen, seed=1, init_rooms=None):
        super().__init__(meta, classes, chosen, seed=seed, init_rooms=init_rooms)
        n = len(self.ids)
        parent = list(range(n))

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        def union(a, b):
            ra, rb = find(a), find(b)
            if ra != rb:
                parent[rb] = ra

        groups_declared = 0
        for d in meta.get("distributions", []):
            if not d.get("required"):
                continue
            if not d["type"].startswith("SameRoom"):
                continue
            members = [self.idx[cid] for cid in d["classes"] if cid in self.idx]
            if len(members) < 2:
                continue
            groups_declared += 1
            for x in members[1:]:
                union(members[0], x)

        buckets = defaultdict(list)
        for i in range(n):
            buckets[find(i)].append(i)
        self.units = list(buckets.values())
        self.unit_of_class = {}
        for u, members in enumerate(self.units):
            for i in members:
                self.unit_of_class[self.ids[i]] = u

        self.unit_allowed = []
        self.unit_pen = []
        self.unit_meetings = []
        self.bad_units = []
        for u, members in enumerate(self.units):
            maps = [{rid: pen for rid, pen in self.allowed[i]} for i in members]
            common = set(maps[0])
            for mp in maps[1:]:
                common &= set(mp)
            if common:
                allowed = [(rid, sum(mp[rid] for mp in maps)) for rid in sorted(common)]
            else:
                # keep the group placeable for diagnostics; scorer will reject
                allowed = []
                self.bad_units.append(u)
            self.unit_allowed.append(allowed)
            self.unit_pen.append({rid: pen for rid, pen in allowed})
            ms = []
            for i in members:
                ms.extend((u, i, s, l) for (_w, _d, s, l) in self.meetings[i])
            self.unit_meetings.append(ms)

        # occupancy: rid -> (week,dow) -> [(unit, member, start, len)]
        self.unit_occ = defaultdict(lambda: defaultdict(list))
        self.unit_room = [-1] * len(self.units)
        self.n_groups = groups_declared
        self.n_units = len(self.units)

    def _unit_conflicts(self, u, rid):
        cnt = 0
        # conflicts against other units
        for (w, d, s, l) in self._meeting_cells(u):
            for (u2, i2, s2, l2) in self.unit_occ[rid].get((w, d), []):
                if u2 != u and overlap(s, l, s2, l2):
                    cnt += 1
        # conflicts inside the group (same room, overlapping meetings)
        members = self.units[u]
        for a in range(len(members)):
            ia = members[a]
            for b in range(a + 1, len(members)):
                ib = members[b]
                for (wa, da, sa, la) in self.meetings[ia]:
                    for (wb, db, sb, lb) in self.meetings[ib]:
                        if (wa, da) == (wb, db) and overlap(sa, la, sb, lb):
                            cnt += 1
        return cnt

    def _meeting_cells(self, u):
        for i in self.units[u]:
            for (w, d, s, l) in self.meetings[i]:
                yield (w, d, s, l)

    def _place_unit(self, u, rid):
        self._unplace_unit(u)
        self.unit_room[u] = rid
        for i in self.units[u]:
            for (w, d, s, l) in self.meetings[i]:
                self.unit_occ[rid][(w, d)].append((u, i, s, l))

    def _unplace_unit(self, u):
        rid = self.unit_room[u]
        if rid < 0:
            return
        for cell, bucket in list(self.unit_occ[rid].items()):
            bucket[:] = [x for x in bucket if x[0] != u]
            if not bucket:
                del self.unit_occ[rid][cell]
        self.unit_room[u] = -1

    def total_conflicts(self):
        cnt = 0
        for rid, cells in self.unit_occ.items():
            for cell, bucket in cells.items():
                for a in range(len(bucket)):
                    for b in range(a + 1, len(bucket)):
                        if bucket[a][0] == bucket[b][0]:
                            continue  # intra-unit conflicts counted separately
                        if overlap(bucket[a][2], bucket[a][3],
                                   bucket[b][2], bucket[b][3]):
                            cnt += 1
        for u, members in enumerate(self.units):
            for a in range(len(members)):
                for b in range(a + 1, len(members)):
                    for (wa, da, sa, la) in self.meetings[members[a]]:
                        for (wb, db, sb, lb) in self.meetings[members[b]]:
                            if (wa, da) == (wb, db) and overlap(sa, la, sb, lb):
                                cnt += 1
        return cnt

    def solve(self, time_limit=60.0, noise=0.1, restart_after=2000,
              verbose=True, reinit=True):
        import time
        t0 = time.time()
        n = len(self.units)

        def room_choices(u):
            if self.unit_allowed[u]:
                return self.unit_allowed[u]
            # fallback for an infeasible group: use first member's raw list
            i = self.units[u][0]
            return self.allowed[i]

        order = sorted(range(n), key=lambda u: (len(room_choices(u)),
                                                -len(self.units[u])))
        if reinit:
            for u in order:
                choices = room_choices(u)
                rid0 = self.init_rooms.get(self.ids[self.units[u][0]])
                if rid0 is not None and any(rid == rid0 for (rid, _pen) in choices):
                    self._place_unit(u, rid0)
                    continue
                best_rid, best_key = None, None
                for (rid, pen) in choices:
                    key = (self._unit_conflicts(u, rid), pen)
                    if best_key is None or key < best_key:
                        best_rid, best_key = rid, key
                        if key == (0, 0):
                            break
                self._place_unit(u, best_rid if best_rid is not None else -1)

        total = self.total_conflicts()
        best_total = total
        best_state = list(self.unit_room)
        stagnation = 0
        it = 0

        while total > 0 and time.time() - t0 < time_limit:
            it += 1
            if stagnation > restart_after:
                for u in range(n):
                    self._place_unit(u, best_state[u])
                for u in self.rng.sample(range(n), max(3, n // 16)):
                    choices = room_choices(u)
                    self._place_unit(u, self.rng.choice(choices)[0])
                total = self.total_conflicts()
                stagnation = 0
                continue

            bad = []
            for rid, cells in self.unit_occ.items():
                for cell, bucket in cells.items():
                    for a in range(len(bucket)):
                        for b in range(a + 1, len(bucket)):
                            if bucket[a][0] != bucket[b][0] and \
                                    overlap(bucket[a][2], bucket[a][3],
                                            bucket[b][2], bucket[b][3]):
                                bad.extend([bucket[a][0], bucket[b][0]])
            # intra-unit conflicts
            for u, members in enumerate(self.units):
                for a in range(len(members)):
                    for b in range(a + 1, len(members)):
                        for (wa, da, sa, la) in self.meetings[members[a]]:
                            for (wb, db, sb, lb) in self.meetings[members[b]]:
                                if (wa, da) == (wb, db) and overlap(sa, la, sb, lb):
                                    bad.append(u)
            if not bad:
                break
            u = self.rng.choice(bad)
            choices = room_choices(u)
            if self.rng.random() < noise:
                rid = self.rng.choice(choices)[0]
            else:
                best_rid, best_key = None, None
                for (rid2, pen) in choices:
                    key = (self._unit_conflicts(u, rid2), pen)
                    if best_key is None or key < best_key:
                        best_rid, best_key = rid2, key
                rid = best_rid
            self._place_unit(u, rid)
            total = self.total_conflicts()

            if total < best_total:
                best_total = total
                best_state = list(self.unit_room)
                stagnation = 0
                if verbose:
                    print(f"  it={it} conflicts={total} t={time.time()-t0:.1f}s", flush=True)
            else:
                stagnation += 1

        for u in range(n):
            if self.unit_room[u] != best_state[u]:
                self._place_unit(u, best_state[u])
        # expose the class-level room array used by the writer
        for u, members in enumerate(self.units):
            for i in members:
                self.room[i] = self.unit_room[u]
        return dict(iterations=it, conflicts=best_total,
                    seconds=time.time() - t0,
                    same_room_groups=self.n_groups,
                    bad_groups=len(self.bad_units))


def main():
    args = sys.argv[1:]
    meta_path, run_path, out_path = args[0], args[1], args[2]
    params = {}
    for a in args[3:]:
        k, v = a.split("=", 1)
        params[k] = v

    meta = json.load(open(meta_path))
    run = json.load(open(run_path))
    classes, chosen = pin_candidates(meta, run)

    init_rooms = {}
    init_path = params.get("InitSol")
    if init_path:
        init_sol = json.load(open(init_path))
        init_rooms = {sc["classId"]: sc.get("room")
                      for sc in init_sol.get("classes", [])
                      if sc.get("room") is not None}
    has_hard_same_room = any(
        d.get("required") and d.get("type", "").startswith("SameRoom")
        for d in meta.get("distributions", []))
    solver_cls = GroupRoomSolver if has_hard_same_room else RoomSolver
    solver = solver_cls(meta, classes, chosen, seed=int(params.get("Seed", 1)),
                        init_rooms=init_rooms)
    stats = solver.solve(
        time_limit=float(params.get("TimeLimit", 60)),
        noise=float(params.get("Noise", 0.1)),
        restart_after=int(params.get("RestartAfter", 2000)),
        verbose=params.get("Verbose", "1") == "1",
    )
    print("roomsearch:", stats, "| classes needing rooms:", len(solver.ids))

    sol = dict(classes=[])
    for cid, (k, t) in chosen.items():
        rid = None
        if cid in solver.idx:
            rid = solver.room[solver.idx[cid]]
            if rid < 0:
                rid = None
        sol["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=rid))
    with open(out_path, "w") as f:
        json.dump(sol, f)
    print("wrote", out_path)


if __name__ == "__main__":
    main()
