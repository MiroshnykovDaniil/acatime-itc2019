#!/usr/bin/env python3
"""Targeted LNS: move a penalized class onto a cheap candidate by evicting
exactly the classes that block it, then reinserting the evicted ones.

Iteration:
  1. pick a class with positive time penalty (or soft cost);
  2. for each cheaper candidate (up to Targets): compute blockers =
     violated_partners(class, candidate) against the current state;
  3. if few blockers (<= MaxEvict), unassign {class} u blockers, pin the class
     to the candidate, greedily reinsert the blockers (cheapest feasible);
  4. accept if the proxy cost improves (or sideways with small probability).

Hard feasibility is preserved at every accepted state; rooms excluded.

Usage:
  python3 tools/targeted.py <meta.json> <sol-in.json> <sol-out.json> [params]
Params: Seed=1 TimeLimit=300 MaxEvict=20 Targets=10 Noise=0.02 Verbose=1
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


def main():
    args = sys.argv[1:]
    meta_path, sol_in, sol_out = args[0], args[1], args[2]
    params = {}
    for a in args[3:]:
        k, v = a.split("=", 1)
        params[k] = v
    seed = int(params.get("Seed", 1))
    time_limit = float(params.get("TimeLimit", 300))
    max_evict = int(params.get("MaxEvict", 20))
    targets = int(params.get("Targets", 10))
    noise = float(params.get("Noise", 0.02))
    verbose = params.get("Verbose", "1") == "1"

    meta = json.load(open(meta_path))
    sol = json.load(open(sol_in))
    pb = LS.Problem(meta)
    w_time = meta["weights"]["time"]
    w_dist = meta["weights"]["distribution"]
    n = len(pb.ids)
    tpen = [[int(t.get("penalty", 0)) for t in c["times"]] for c in meta["classes"]]
    rel = norm_soft(meta)

    for sc in sol["classes"]:
        pb.set_candidate(i := pb.idx[sc["classId"]], int(sc["candidate"]))
    assert not pb.all_violating_pairs(), "starting solution must be hard-feasible"

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
            kj = int(pb.chosen[j])
            if kj >= 0 and soft_viol(i, k, j, kj, code, par):
                c += pen
        return c

    def total_cost():
        t = sum(tpen[i][int(pb.chosen[i])] for i in range(n)) * w_time
        d = 0
        for i in range(n):
            for (j, code, par, pen) in rel[i]:
                if j > i and pb.chosen[j] >= 0:
                    if soft_viol(i, int(pb.chosen[i]), j, int(pb.chosen[j]), code, par):
                        d += pen
        return t + d * w_dist

    def unassign(i):
        pb.chosen[i] = -1
        pb.cur_start[i] = 0
        pb.cur_len[i] = 0
        pb.cur_dmask[i] = 0
        pb.cur_wmask[i] = 0

    def insert_best(i, cap=40):
        best_k, best_key = None, None
        nc = len(pb.c_start[i])
        order = list(range(nc))
        if nc > cap:
            rng.shuffle(order)
            order = order[:cap]
        for k in order:
            if pb.violated_partners(i, k):
                continue
            key = (tpen[i][k] * w_time + soft_of(i, k) * w_dist,
                   int(pb.block_pen[i][k]))
            if best_key is None or key < best_key:
                best_k, best_key = k, key
        if best_k is None:
            return False
        pb.set_candidate(i, best_k)
        return True

    rng = random.Random(seed)
    cur_cost = total_cost()
    best_cost = cur_cost
    best = pb.chosen.copy()
    print(f"initial: hard=0 proxy={cur_cost}")
    t0 = time.time()
    it = 0
    accepted = 0
    improved = 0

    penalized = [i for i in range(n) if tpen[i][int(pb.chosen[i])] > 0]

    while time.time() - t0 < time_limit:
        it += 1
        if it % 500 == 0:
            penalized = [i for i in range(n)
                         if tpen[i][int(pb.chosen[i])] > 0 or soft_of(i, int(pb.chosen[i])) > 0]
        if not penalized:
            break
        i = rng.choice(penalized)
        cur_k = int(pb.chosen[i])
        cur_local = tpen[i][cur_k] * w_time + soft_of(i, cur_k) * w_dist

        # cheaper candidates by the COMBINED local cost (time + soft)
        cheaper = [k for k in range(len(pb.c_start[i]))
                   if k != cur_k and tpen[i][k] * w_time + soft_of(i, k) * w_dist < cur_local]
        if not cheaper:
            # sideways plateaus (equal local cost) allow soft/time trade-offs
            cheaper = [k for k in range(len(pb.c_start[i]))
                       if k != cur_k and tpen[i][k] * w_time + soft_of(i, k) * w_dist == cur_local]
        if not cheaper:
            continue
        rng.shuffle(cheaper)
        cheaper = cheaper[:targets]

        for k in cheaper:
            blockers = pb.violated_partners(i, k)
            if len(blockers) > max_evict:
                continue
            ruin = list({i} | set(int(b) for b in blockers))
            old = {x: int(pb.chosen[x]) for x in ruin}
            old_cost = cur_cost

            for x in ruin:
                unassign(x)
            pb.set_candidate(i, k)  # pinned target
            ok = True
            rest = [x for x in ruin if x != i]
            rng.shuffle(rest)
            for x in rest:
                if not insert_best(x):
                    ok = False
                    break
            if not ok:
                for x in ruin:
                    unassign(x)
                for x, kk in old.items():
                    pb.set_candidate(x, kk)
                continue

            new_cost = total_cost()
            if new_cost < old_cost or rng.random() < noise:
                cur_cost = new_cost
                accepted += 1
                if new_cost < best_cost:
                    best_cost = new_cost
                    best = pb.chosen.copy()
                    improved += 1
                    if verbose:
                        print(f"  it={it} cost={new_cost} evicted={len(blockers)} "
                              f"t={time.time()-t0:.1f}s", flush=True)
                break
            else:
                for x in ruin:
                    unassign(x)
                for x, kk in old.items():
                    pb.set_candidate(x, kk)

    for x in range(n):
        pb.set_candidate(x, int(best[x]))
    assert not pb.all_violating_pairs()
    print(f"final: hard=0 proxy={best_cost} iterations={it} accepted={accepted} "
          f"improved={improved} seconds={time.time()-t0:.1f}")

    out = dict(classes=[])
    for i, cid in enumerate(pb.ids):
        k = int(pb.chosen[i])
        t = meta["classes"][i]["times"][k]
        out["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=None))
    with open(sol_out, "w") as f:
        json.dump(out, f)
    print("wrote", sol_out)


if __name__ == "__main__":
    main()
