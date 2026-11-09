#!/usr/bin/env python3
"""Cost-aware local search on top of a feasible solution.

Starts from a feasible solution produced by tools/solve.py (or any solution
JSON) and performs moves that PRESERVE hard-constraint feasibility while
reducing the official cost proxy: time penalties (weight time), soft
distribution penalties (weight distribution). Room penalties are handled by
the room assignment pass; rooms are otherwise not part of this search.

Usage:
  python3 tools/optimize.py <meta.json> <sol-in.json> <sol-out.json> [params]
Params: Seed=1 TimeLimit=120 Noise=0.02 Verbose=1
"""

import json
import random
import sys
import time

import localsearch as LS
import score as SC


def soft_relations(meta):
    """Per class: list of (other_idx, type, param, penalty)."""
    idx = {}
    order = [c["classId"] for c in meta["classes"]]
    for i, cid in enumerate(order):
        idx[cid] = i
    rel = [[] for _ in order]
    for d in meta["distributions"]:
        if d.get("required"):
            continue
        base, param = LS.norm_type(d["type"])
        if base not in LS.SPECIAL_TYPES:
            continue
        members = [idx[cid] for cid in d["classes"] if cid in idx]
        for a in range(len(members)):
            for b in range(a + 1, len(members)):
                i, j = members[a], members[b]
                rel[i].append((j, base, param, d["penalty"]))
                rel[j].append((i, base, param, d["penalty"]))
    return rel


def main():
    args = sys.argv[1:]
    meta_path, sol_in, sol_out = args[0], args[1], args[2]
    params = {}
    for a in args[3:]:
        k, v = a.split("=", 1)
        params[k] = v
    seed = int(params.get("Seed", 1))
    time_limit = float(params.get("TimeLimit", 120))
    noise = float(params.get("Noise", 0.02))
    verbose = params.get("Verbose", "1") == "1"

    meta = json.load(open(meta_path))
    sol = json.load(open(sol_in))
    pb = LS.Problem(meta)
    w_time = meta["weights"]["time"]
    w_dist = meta["weights"]["distribution"]

    # load state from solution
    for sc in sol["classes"]:
        i = pb.idx[sc["classId"]]
        pb.set_candidate(i, int(sc["candidate"]))
    hard = pb.all_violating_pairs()
    if hard:
        print(f"ERROR: input solution has {len(hard)} hard violations; aborting")
        return

    rel = soft_relations(meta)
    n = len(pb.ids)
    tpen = [list(c["times"]) for c in meta["classes"]]
    tpen = [[int(t.get("penalty", 0)) for t in ts] for ts in tpen]

    def soft_cost_of(i, k):
        """soft distribution cost if class i uses candidate k (current others)."""
        c = 0
        for (j, tc, par, pen) in rel[i]:
            A = pb.c_dicts[i][k]
            B = pb.c_dicts[j][pb.chosen[j]]
            if SC.pair_violated(tc, A, B, par, {}, None):
                c += pen
        return c

    def total_cost(state):
        t = sum(tpen[i][int(state[i])] for i in range(n)) * w_time
        d = 0
        for i in range(n):
            for (j, tc, par, pen) in rel[i]:
                if j > i:
                    A = pb.c_dicts[i][int(state[i])]
                    B = pb.c_dicts[j][int(state[j])]
                    if SC.pair_violated(tc, A, B, par, {}, None):
                        d += pen
        return t + d * w_dist

    cur_cost = total_cost(pb.chosen)
    best_cost = cur_cost
    best = pb.chosen.copy()
    print(f"initial: hard=0 proxy_cost={cur_cost} (time*w={w_time}, dist*w={w_dist})")

    rng = random.Random(seed)
    t0 = time.time()
    it = 0
    stagnation = 0
    while time.time() - t0 < time_limit:
        it += 1
        i = rng.randrange(n)
        cur = int(pb.chosen[i])
        base = tpen[i][cur] * w_time + soft_cost_of(i, cur) * w_dist

        best_k, best_delta, n_ties = cur, 0, 1
        nc = len(pb.c_start[i])
        if nc < 2:
            continue
        for k in range(nc):
            if k == cur:
                continue
            if pb.violated_partners(i, k):
                continue
            delta = (tpen[i][k] * w_time + soft_cost_of(i, k) * w_dist) - base
            if delta < best_delta:
                best_k, best_delta, n_ties = k, delta, 1
            elif delta == best_delta and rng.random() < 1.0 / (n_ties + 1):
                best_k, n_ties = k, n_ties + 1

        if best_k != cur and (best_delta < 0 or rng.random() < noise):
            pb.set_candidate(i, best_k)
            cur_cost += best_delta
            if cur_cost < best_cost:
                best_cost = cur_cost
                best = pb.chosen.copy()
                stagnation = 0
                if verbose:
                    print(f"  it={it} cost={cur_cost} t={time.time()-t0:.1f}s")
            else:
                stagnation += 1
        else:
            stagnation += 1

        if stagnation > 40000:
            # restart from best + random perturbation preserving hard feasibility
            for j in range(n):
                pb.set_candidate(j, int(best[j]))
            cur_cost = best_cost
            moved = 0
            for _ in range(30):
                j = rng.randrange(n)
                ks = [k for k in range(len(pb.c_start[j]))
                      if not pb.violated_partners(j, k)]
                if ks:
                    pb.set_candidate(j, rng.choice(ks))
                    moved += 1
            cur_cost = total_cost(pb.chosen)
            stagnation = 0

    for j in range(n):
        pb.set_candidate(j, int(best[j]))
    hard = pb.all_violating_pairs()
    print(f"final: hard={len(hard)} proxy_cost={best_cost} iterations={it} "
          f"seconds={time.time()-t0:.1f}")
    assert not hard

    out = dict(classes=[])
    for i, cid in enumerate(pb.ids):
        k = int(pb.chosen[i])
        t = meta["classes"][i]["times"][k]
        # keep the room if the input had one for this candidate; the room pass
        # in solve.py/roomsearch.py is authoritative
        out["classes"].append(dict(classId=cid, candidate=k,
                                   days=t["days"], start=t["start"],
                                   length=t["length"], weeks=t["weeks"],
                                   room=None))
    with open(sol_out, "w") as f:
        json.dump(out, f)
    print("wrote", sol_out)


if __name__ == "__main__":
    main()
