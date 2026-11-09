#!/usr/bin/env python3
"""Min-conflicts feasibility search over class -> candidate-time assignments.

The hard constraints are evaluated with the exact official PATAT-2018
pairwise formulas (imported from score.py — the scorer remains the referee).
Rooms are NOT handled here: the output is a runner-style JSON that goes
through tools/post.py (room consolidation) and then tools/score.py.

Idea: constructive DFS thrashes on the dense SameAttendees/NotOverlap
kernel (measured depth wall ~200/550). This tool works the other way round:
always complete assignments, repair violated hard pairs via min-conflicts.

Usage:
  python3 tools/localsearch.py <meta.json> <out-run.json> [key=value ...]

Params (key=value):
  Seed=1           RNG seed
  TimeLimit=180    seconds
  Iterations=5000000
  Noise=0.03       probability of a random (non-greedy) candidate move
  RestartAfter=1500  stagnation iterations before a perturbation restart
  Verbose=1
"""

import json
import os
import random
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score as SC  # official formulas (pair_violated, first_bit, ...)


def norm_type(t):
    if "(" in t:
        base, rest = t.split("(", 1)
        param = int(rest.split(")", 1)[0])
        return base, param
    return t, 0


SPECIAL_TYPES = {"SameStart", "SameTime", "DifferentTime", "SameDays",
                 "DifferentDays", "SameWeeks", "DifferentWeeks",
                 "NotOverlap", "Overlap", "SameAttendees", "Precedence",
                 "WorkDay", "MinGap"}


class Problem:
    def __init__(self, meta):
        self.base = tuple(meta["baseDate"])
        self.starts = meta["starts"]
        self.spair = {s: i + 1 for i, s in enumerate(self.starts)}

        classes = meta["classes"]
        self.ids = [c["classId"] for c in classes]
        self.idx = {cid: i for i, cid in enumerate(self.ids)}
        n = len(classes)

        # per-class candidate arrays
        self.c_start = []
        self.c_len = []
        self.c_dmask = []
        self.c_wmask = []
        for c in classes:
            st = np.array([t["start"] for t in c["times"]], dtype=np.int64)
            ln = np.array([t["length"] for t in c["times"]], dtype=np.int64)
            dm = np.array([int(t["days"][::-1], 2) for t in c["times"]], dtype=np.int64)
            wm = np.array([int(t["weeks"][::-1], 2) for t in c["times"]], dtype=np.int64)
            self.c_start.append(st)
            self.c_len.append(ln)
            self.c_dmask.append(dm)
            self.c_wmask.append(wm)

        # candidate dicts for special-formula checks
        self.c_dicts = []
        for c in classes:
            self.c_dicts.append([
                dict(days=t["days"], start=t["start"], length=t["length"],
                     weeks=t["weeks"], room=None)
                for t in c["times"]])

        # hard relations
        sa_pairs = set()
        special_pairs = []  # (i, j, base_type, param)
        skipped = set()
        for d in meta["distributions"]:
            if not d.get("required"):
                continue
            base, param = norm_type(d["type"])
            members = [self.idx[cid] for cid in d["classes"] if cid in self.idx]
            if len(members) < 2:
                continue
            if base in ("SameAttendees", "NotOverlap"):
                for a in range(len(members)):
                    for b in range(a + 1, len(members)):
                        i, j = members[a], members[b]
                        sa_pairs.add((min(i, j), max(i, j)))
            elif base in SPECIAL_TYPES:
                for a in range(len(members)):
                    for b in range(a + 1, len(members)):
                        # keep distribution order: Precedence is asymmetric
                        special_pairs.append((members[a], members[b], base, param))
            else:
                skipped.add(base)

        if skipped:
            print("WARNING: unsupported hard distribution types skipped:",
                  sorted(skipped), file=sys.stderr)

        # forced room conflicts: classes whose candidate-room list is a singleton
        # share THE SAME room => their meetings must never overlap in time (the
        # official room-collision predicate is exactly overlap & share days &
        # share weeks). Add them to the must-not-overlap graph so the time search
        # avoids room-impossible time assignments.
        by_room = {}
        for c in classes:
            if c.get("hasRoom") and len(c["rooms"]) == 1:
                by_room.setdefault(c["rooms"][0]["id"], []).append(self.idx[c["classId"]])
        n_forced = 0
        for rid, members in by_room.items():
            for a in range(len(members)):
                for b in range(a + 1, len(members)):
                    i, j = members[a], members[b]
                    sa_pairs.add((min(i, j), max(i, j)))
                    n_forced += 1
        if n_forced:
            print(f"forced-room pairs (singleton rooms): {n_forced}")

        # adjacency (SA/NO deduped)
        nbr = [[] for _ in range(n)]
        for (i, j) in sa_pairs:
            nbr[i].append(j)
            nbr[j].append(i)
        self.nbr = [np.array(x, dtype=np.int64) for x in nbr]

        # conservative SameAttendees travel: max travel between candidate rooms,
        # matching the generated estimator/validator.  Only nonzero pairs are
        # stored; NotOverlap pairs have travel 0.
        self.travel_pair = {}
        for i, c in enumerate(meta["classes"]):
            for pid, val in c.get("travelMax", {}).items():
                j = self.idx.get(int(pid))
                if j is not None and val > 0:
                    self.travel_pair[(i, j)] = int(val)

        # special adjacency
        spc = [[] for _ in range(n)]
        for (i, j, tc, par) in special_pairs:
            # reverse=False: i is the first class of the constraint;
            # reverse=True: j is first and i is second (needed for Precedence).
            spc[i].append((j, tc, par, False))
            spc[j].append((i, tc, par, True))
        self.spc = spc
        self.special_pairs = special_pairs

        # blocking penalty (soft tie-break): singleton-room classes should avoid
        # time slots occupied by forced-time classes whose room pool contains
        # their only room (otherwise the room complement can deadlock).
        self.block_pen = []
        forced = []
        for ci, c in enumerate(classes):
            if c.get("hasRoom") and len(c["times"]) == 1:
                t = c["times"][0]
                cells = []
                for wi, chw in enumerate(t["weeks"]):
                    if chw == "1":
                        for di, chd in enumerate(t["days"]):
                            if chd == "1":
                                cells.append((wi, di, t["start"], t["length"]))
                forced.append((set(r["id"] for r in c["rooms"]), cells))
        for ci, c in enumerate(classes):
            pens = np.zeros(len(c["times"]), dtype=np.int64)
            if c.get("hasRoom") and len(c["rooms"]) == 1:
                r = c["rooms"][0]["id"]
                rel = [f for f in forced if r in f[0]]
                for k, t in enumerate(c["times"]):
                    cs = []
                    for wi, chw in enumerate(t["weeks"]):
                        if chw == "1":
                            for di, chd in enumerate(t["days"]):
                                if chd == "1":
                                    cs.append((wi, di, t["start"], t["length"]))
                    cnt = 0
                    for (_rooms_f, fcells) in rel:
                        for (fw, fd, fs, fl) in fcells:
                            for (w, d, s, l) in cs:
                                if (w, d) == (fw, fd) and s < fl and fs < l:
                                    cnt += 1
                    pens[k] = cnt
            self.block_pen.append(pens)

        # current state
        self.chosen = np.full(n, -1, dtype=np.int64)
        self.cur_start = np.zeros(n, dtype=np.int64)
        self.cur_len = np.zeros(n, dtype=np.int64)
        self.cur_dmask = np.zeros(n, dtype=np.int64)
        self.cur_wmask = np.zeros(n, dtype=np.int64)

    # -- state helpers ---------------------------------------------------
    def set_candidate(self, i, k):
        self.chosen[i] = k
        self.cur_start[i] = self.c_start[i][k]
        self.cur_len[i] = self.c_len[i][k]
        self.cur_dmask[i] = self.c_dmask[i][k]
        self.cur_wmask[i] = self.c_wmask[i][k]

    def violated_partners(self, i, k):
        """Indexes of hard partners violated if class i uses candidate k
        (with the rest of the state as is)."""
        bad = []
        nbrs = self.nbr[i]
        if len(nbrs):
            s = self.c_start[i][k]
            l = self.c_len[i][k]
            ps = self.cur_start[nbrs]
            pl = self.cur_len[nbrs]
            ov = (s < ps + pl) & (ps < s + l)
            if ov.any():
                dm = self.c_dmask[i][k] & self.cur_dmask[nbrs]
                wm = self.c_wmask[i][k] & self.cur_wmask[nbrs]
                bad_mask = ov & (dm != 0) & (wm != 0)
                if bad_mask.any():
                    for ix in np.where(bad_mask)[0]:
                        j = int(nbrs[ix])
                        tv = self.travel_pair.get((i, j), 0)
                        if tv > 0:
                            s2 = int(ps[ix]); l2 = int(pl[ix])
                            if (s + l + tv <= s2) or (s2 + l2 + tv <= s):
                                continue
                        bad.append(j)
        for (j, tc, par, reverse) in self.spc[i]:
            if self.chosen[j] < 0:
                continue
            if reverse:
                A = self.c_dicts[j][self.chosen[j]]
                B = self.c_dicts[i][k]
            else:
                A = self.c_dicts[i][k]
                B = self.c_dicts[j][self.chosen[j]]
            res = SC.pair_violated(tc, A, B, par, {}, None)
            if res:
                bad.append(j)
        return bad

    def all_violating_pairs(self):
        vp = set()
        for i in range(len(self.ids)):
            for j in self.violated_partners(i, self.chosen[i]):
                vp.add((min(i, j), max(i, j)))
        return vp


def greedy_init(pb, rng, order=None):
    n = len(pb.ids)
    order = order or list(range(n))
    rng.shuffle(order)
    for i in order:
        best_k, best_key = 0, None
        for k in range(len(pb.c_start[i])):
            key = (len(pb.violated_partners(i, k)), int(pb.block_pen[i][k]))
            if best_key is None or key < best_key:
                best_k, best_key = k, key
                if key == (0, 0):
                    break
        pb.set_candidate(i, best_k)


def solve(pb, seed=1, time_limit=180.0, max_iters=5_000_000, noise=0.03,
          restart_after=1500, verbose=True, reinit=True):
    rng = random.Random(seed)
    t0 = time.time()

    if reinit:
        greedy_init(pb, rng)
    vp = pb.all_violating_pairs()
    total = len(vp)

    best_total = total
    best_state = pb.chosen.copy()
    stagnation = 0

    it = 0
    while it < max_iters:
        it += 1
        if total == 0:
            break
        if (it & 255) == 0 and time.time() - t0 > time_limit:
            break
        if stagnation > restart_after:
            # perturbation restart FROM the best state: randomize a slice
            for i in range(len(pb.ids)):
                pb.set_candidate(i, int(best_state[i]))
            k_pert = max(6, len(pb.ids) // 16)
            for i in rng.sample(range(len(pb.ids)), k_pert):
                pb.set_candidate(i, rng.randrange(len(pb.c_start[i])))
            vp = pb.all_violating_pairs()
            total = len(vp)
            stagnation = 0
            if verbose:
                print(f"  perturb at it={it} total={total}", flush=True)
        else:
            # pick a violated pair, then one of its classes
            a, b = rng.choice(tuple(vp))
            i = a if rng.random() < 0.5 else b
            ncand = len(pb.c_start[i])
            if rng.random() < noise:
                k = rng.randrange(ncand)
            else:
                best_k, best_key = 0, None
                for k in range(ncand):
                    key = (len(pb.violated_partners(i, k)), int(pb.block_pen[i][k]))
                    if best_key is None or key < best_key:
                        best_k, best_key = k, key
                k = best_k
            pb.set_candidate(i, k)
            for (x, y) in [p for p in vp if p[0] == i or p[1] == i]:
                vp.discard((x, y))
            for j in pb.violated_partners(i, k):
                vp.add((min(i, j), max(i, j)))
            total = len(vp)

        if total < best_total:
            best_total = total
            best_state = pb.chosen.copy()
            stagnation = 0
            if verbose:
                print(f"  it={it} total={total} t={time.time()-t0:.1f}s", flush=True)
        else:
            stagnation += 1

    # restore best
    for i in range(len(pb.ids)):
        pb.set_candidate(i, int(best_state[i]))

    return dict(iterations=it, violations=best_total, seconds=time.time() - t0)


def emit_run(pb, path):
    slots = []
    from datetime import date, timedelta
    base = date(*pb.base)
    allset = {int(pb.chosen[i]) for i in range(len(pb.ids))}
    for i, cid in enumerate(pb.ids):
        k = int(pb.chosen[i])
        t = pb.c_dicts[i][k]
        for wi, chw in enumerate(t["weeks"]):
            if chw != "1":
                continue
            for di, chd in enumerate(t["days"]):
                if chd != "1":
                    continue
                d = base + timedelta(days=wi * 7 + di)
                slots.append(dict(classId=cid, date=d.strftime("%Y-%m-%d"),
                                  pair=pb.spair[t["start"]]))
    run = dict(algorithm="localsearch", totalSeconds=0.0,
               results=[dict(totalEstimation=0, slots=slots)])
    with open(path, "w") as f:
        json.dump(run, f)


def main():
    args = [a for a in sys.argv[1:]]
    meta_path, out_path = args[0], args[1]
    params = {}
    for a in args[2:]:
        k, v = a.split("=", 1)
        params[k] = v

    meta = json.load(open(meta_path))
    pb = Problem(meta)
    print(f"classes={len(pb.ids)} cands={sum(len(x) for x in pb.c_start)} "
          f"sa_pairs={sum(len(x) for x in pb.nbr)//2} special={len(pb.special_pairs)}")

    stats = solve(
        pb,
        seed=int(params.get("Seed", 1)),
        time_limit=float(params.get("TimeLimit", 180)),
        max_iters=int(params.get("Iterations", 5_000_000)),
        noise=float(params.get("Noise", 0.03)),
        restart_after=int(params.get("RestartAfter", 1500)),
        verbose=params.get("Verbose", "1") == "1",
    )
    print("localsearch:", stats)
    emit_run(pb, out_path)
    print("wrote", out_path)


if __name__ == "__main__":
    main()
