#!/usr/bin/env python3
"""Cost-weighted min-conflicts / annealing over class -> candidate patterns.

Unlike lns.py this search MAY leave hard-feasible space temporarily: hard
violations are penalized with a large weight P inside the move score, so the
search can cross plateaus and find globally better cost configurations, while
the best hard-feasible state seen is tracked and returned.

Objective: official cost proxy = w_time*timePen + w_dist*softPen
(+ P per hard-violating partner pair).

Usage:
  python3 tools/sa.py <meta.json> <sol-in.json> <sol-out.json> [params]
Params: Seed=1 TimeLimit=300 HardPenalty=60 Noise=0.06 CandCap=60 Verbose=1
"""

import json
import random
import sys
import time

import localsearch as LS

C_SA, C_SD, C_DD, C_WD, C_SW, C_DW = range(6)


def norm_soft(meta):
    idx = {c["classId"]: i for i, c in enumerate(meta["classes"])}
    rel = [[] for _ in meta["classes"]]
    for d in meta["distributions"]:
        if d.get("required"):
            continue
        base, par = LS.norm_type(d["type"])
        if base in ("SameAttendees", "NotOverlap"):
            code = C_SA
        elif base == "SameDays":
            code = C_SD
        elif base == "DifferentDays":
            code = C_DD
        elif base == "WorkDay":
            code = C_WD
        elif base == "SameWeeks":
            code = C_SW
        elif base == "DifferentWeeks":
            code = C_DW
        else:
            continue
        ids = [idx[cid] for cid in d["classes"] if cid in idx]
        for a in range(len(ids)):
            for b in range(a + 1, len(ids)):
                i, j = ids[a], ids[b]
                rel[i].append((j, code, par, d["penalty"]))
                rel[j].append((i, code, par, d["penalty"]))
    return rel


def run(meta, sol, seed=1, time_limit=300, hard_pen=60, noise=0.06,
        cand_cap=60, verbose=True):
    pb = LS.Problem(meta)
    w_time = meta["weights"]["time"]
    w_dist = meta["weights"]["distribution"]
    n = len(pb.ids)
    tpen = [[int(t.get("penalty", 0)) for t in c["times"]] for c in meta["classes"]]
    rel = norm_soft(meta)

    for sc in sol["classes"]:
        i = pb.idx[sc["classId"]]
        pb.set_candidate(i, int(sc["candidate"]))

    def soft_viol(i, k, j, kj, code, par):
        dm = pb.c_dmask[i][k] & pb.c_dmask[j][kj]
        wm = pb.c_wmask[i][k] & pb.c_wmask[j][kj]
        if code == C_SA:
            if dm == 0 or wm == 0:
                return False
            s, l = pb.c_start[i][k], pb.c_len[i][k]
            sj, lj = pb.c_start[j][kj], pb.c_len[j][kj]
            return bool(s < sj + lj and sj < s + l)
        if code == C_SD:
            a, b = pb.c_dmask[i][k], pb.c_dmask[j][kj]
            return not ((a & ~b) == 0 or (b & ~a) == 0)
        if code == C_DD:
            return dm != 0
        if code == C_WD:
            if dm == 0 or wm == 0:
                return False
            s = min(pb.c_start[i][k], pb.c_start[j][kj])
            e = max(pb.c_start[i][k] + pb.c_len[i][k],
                    pb.c_start[j][kj] + pb.c_len[j][kj])
            return bool(e - s > par)
        if code == C_SW:
            a, b = pb.c_wmask[i][k], pb.c_wmask[j][kj]
            return not ((a & ~b) == 0 or (b & ~a) == 0)
        if code == C_DW:
            return wm != 0
        return False

    def soft_of(i, k):
        c = 0
        for (j, code, par, pen) in rel[i]:
            if soft_viol(i, k, j, int(pb.chosen[j]), code, par):
                c += pen
        return c

    def score(i, k):
        hp = len(pb.violated_partners(i, k))
        return (hard_pen * hp + w_time * tpen[i][k] + w_dist * soft_of(i, k))

    def proxy():
        t = sum(tpen[i][int(pb.chosen[i])] for i in range(n))
        d = 0
        for i in range(n):
            for (j, code, par, pen) in rel[i]:
                if j > i and soft_viol(i, int(pb.chosen[i]), j, int(pb.chosen[j]), code, par):
                    d += pen
        return t * w_time + d * w_dist

    t0 = time.time()
    rng = random.Random(seed)
    best_proxy = None
    best = pb.chosen.copy()
    it = 0
    last_improve = 0

    # initial feasibility / cost
    hard0 = pb.all_violating_pairs()
    cur_proxy = proxy() if not hard0 else 10**9
    if not hard0:
        best_proxy = cur_proxy
    print(f"initial: hard={len(hard0)} proxy={cur_proxy if hard0 == [] else 'infeasible'}")

    while time.time() - t0 < time_limit:
        it += 1
        # bias to classes involved in soft cost or hard violations
        i = rng.randrange(n)
        nc = len(pb.c_start[i])
        order = list(range(nc))
        if nc > cand_cap:
            rng.shuffle(order)
            order = order[:cand_cap]
        best_k, best_s = None, None
        for k in order:
            s = score(i, k)
            if best_s is None or s < best_s:
                best_k, best_s = k, s
        if rng.random() < noise:
            best_k = rng.randrange(nc)
        pb.set_candidate(i, best_k)

        if it % 200 == 0:
            hard = pb.all_violating_pairs()
            if not hard:
                p = proxy()
                if best_proxy is None or p < best_proxy - 0.5:
                    best_proxy = p
                    best = pb.chosen.copy()
                    last_improve = it
                    if verbose:
                        print(f"  it={it} proxy={p} t={time.time()-t0:.1f}s", flush=True)

    # restore best feasible
    for i in range(n):
        pb.set_candidate(i, int(best[i]))
    hard = pb.all_violating_pairs()
    final = proxy()
    print(f"final: hard={len(hard)} proxy={final} iterations={it} "
          f"seconds={time.time()-t0:.1f}")
    assert not hard

    out = dict(classes=[])
    for i, cid in enumerate(pb.ids):
        k = int(pb.chosen[i])
        t = meta["classes"][i]["times"][k]
        out["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=None))
    return out


def main():
    args = sys.argv[1:]
    meta_path, sol_in, sol_out = args[0], args[1], args[2]
    params = {}
    for a in args[3:]:
        k, v = a.split("=", 1)
        params[k] = v
    meta = json.load(open(meta_path))
    sol = json.load(open(sol_in))
    out = run(meta, sol,
              seed=int(params.get("Seed", 1)),
              time_limit=float(params.get("TimeLimit", 300)),
              hard_pen=int(params.get("HardPenalty", 60)),
              noise=float(params.get("Noise", 0.06)),
              cand_cap=int(params.get("CandCap", 60)),
              verbose=params.get("Verbose", "1") == "1")
    with open(sol_out, "w") as f:
        json.dump(out, f)
    print("wrote", sol_out)


if __name__ == "__main__":
    main()
