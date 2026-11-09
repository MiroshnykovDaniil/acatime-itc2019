#!/usr/bin/env python3
"""Large Neighborhood Search on top of the feasibility layer (fast version).

State: one candidate-time pattern per class (hard-feasible w.r.t. the exact
official hard formulas incl. forced-room pairs). Objective: official cost proxy
= w_time * sum(time penalties) + w_dist * sum(soft distribution penalties).
Rooms are excluded (assigned afterwards by roomsearch/solve).

Ruin: a high-cost seed + its hard neighbours + random fill; recreate greedily
with the cheapest hard-feasible pattern. Cost is maintained incrementally and
soft constraints are evaluated with bitmasks (days/weeks/starts), which makes
iterations ~100x cheaper than the naive version.

Usage:
  python3 tools/lns.py <meta.json> <sol-in.json> <sol-out.json> [params]
Params: Seed=1 TimeLimit=300 RuinMin=4 RuinMax=10 Noise=0.02 CandCap=40
        GroupRuin=0 GroupProb=0.5 Verbose=1
"""

import json
import random
import sys
import time

import localsearch as LS


# soft constraint codes
C_SA = 0     # SameAttendees / NotOverlap (time overlap + shared day + shared week)
C_SD = 1     # SameDays subset
C_DD = 2     # DifferentDays disjoint
C_WD = 3     # WorkDay(param): same day+week, span > param
C_SW = 4     # SameWeeks subset
C_DW = 5     # DifferentWeeks disjoint
C_ST = 6     # SameTime containment (equal or nested interval)
C_PR = 7     # Precedence (asymmetric)


def norm_soft(meta):
    """Per class: list of (other_idx, code, param, penalty)."""
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
        elif base == "SameTime":
            code = C_ST
        elif base == "Precedence":
            code = C_PR
        else:
            # SameRoom/DifferentRoom depend on the room assignment;
            # MaxDayLoad is a group-level constraint, not pairwise.
            continue
        ids = [idx[cid] for cid in d["classes"] if cid in idx]
        for a in range(len(ids)):
            for b in range(a + 1, len(ids)):
                i, j = ids[a], ids[b]
                rel[i].append((j, code, par, d["penalty"], False))
                rel[j].append((i, code, par, d["penalty"], True))
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
    ruin_min = int(params.get("RuinMin", 4))
    ruin_max = int(params.get("RuinMax", 10))
    noise = float(params.get("Noise", 0.02))
    cand_cap = int(params.get("CandCap", 40))
    group_ruin = int(params.get("GroupRuin", 0))
    group_prob = float(params.get("GroupProb", 0.5))
    verbose = params.get("Verbose", "1") == "1"

    meta = json.load(open(meta_path))
    sol = json.load(open(sol_in))
    pb = LS.Problem(meta)
    w_time = meta["weights"]["time"]
    w_dist = meta["weights"]["distribution"]
    n = len(pb.ids)

    tpen = [[int(t.get("penalty", 0)) for t in c["times"]] for c in meta["classes"]]
    rel = norm_soft(meta)
    # Whole-distribution ruin targets (especially the 15-class SameDays/
    # SameTime groups): random 4-14 class ruins usually cannot rebuild them.
    soft_groups = []
    group_by_class = [[] for _ in range(n)]
    for d in meta["distributions"]:
        if d.get("required"):
            continue
        base, _par = LS.norm_type(d["type"])
        if base not in ("SameAttendees", "NotOverlap", "SameDays",
                        "DifferentDays", "SameWeeks", "DifferentWeeks",
                        "SameTime", "WorkDay", "Precedence"):
            continue
        ids = [pb.idx[cid] for cid in d["classes"] if cid in pb.idx]
        if len(ids) >= 3:
            gi = len(soft_groups)
            soft_groups.append(ids)
            for i in ids:
                group_by_class[i].append(gi)

    for sc in sol["classes"]:
        i = pb.idx[sc["classId"]]
        pb.set_candidate(i, int(sc["candidate"]))
    hard = pb.all_violating_pairs()
    if hard:
        print(f"ERROR: input has {len(hard)} hard violations; aborting")
        return

    # fast soft-pair predicate from candidate arrays
    def soft_viol(i, k, j, kj, code, par, reverse=False):
        if reverse:
            i, j, k, kj = j, i, kj, k
        dm = pb.c_dmask[i][k] & pb.c_dmask[j][kj]
        wm = pb.c_wmask[i][k] & pb.c_wmask[j][kj]
        if code == C_SA:
            if dm == 0 or wm == 0:
                return False
            s = pb.c_start[i][k]
            l = pb.c_len[i][k]
            sj = pb.c_start[j][kj]
            lj = pb.c_len[j][kj]
            return bool(s < sj + lj and sj < s + l)
        if code == C_SD:
            a = pb.c_dmask[i][k]
            b = pb.c_dmask[j][kj]
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
            a = pb.c_wmask[i][k]
            b = pb.c_wmask[j][kj]
            return not ((a & ~b) == 0 or (b & ~a) == 0)
        if code == C_DW:
            return wm != 0
        if code == C_ST:
            sa, ea = int(pb.c_start[i][k]), int(pb.c_start[i][k] + pb.c_len[i][k])
            sb, eb = int(pb.c_start[j][kj]), int(pb.c_start[j][kj] + pb.c_len[j][kj])
            return not ((sa <= sb and eb <= ea) or (sb <= sa and ea <= eb))
        if code == C_PR:
            return bool(LS.SC.pair_violated("Precedence", pb.c_dicts[i][k],
                                            pb.c_dicts[j][kj], par, {}, None))
        return False

    def soft_of(i, k):
        c = 0
        for (j, code, par, pen, reverse) in rel[i]:
            kj = pb.chosen[j]
            if kj < 0:
                continue
            if soft_viol(i, k, j, int(kj), code, par, reverse):
                c += pen
        return c

    # incremental state
    sc = [soft_of(i, int(pb.chosen[i])) for i in range(n)]
    soft_total = 0.5 * sum(sc)
    time_total = sum(tpen[i][int(pb.chosen[i])] for i in range(n))
    cur_cost = time_total * w_time + soft_total * w_dist
    best_cost = cur_cost
    best = pb.chosen.copy()
    print(f"initial: hard=0 proxy={cur_cost}")
    t0 = time.time()
    rng = random.Random(seed)
    it = 0
    accepted = 0
    improved = 0
    active = [i for i in range(n)
              if tpen[i][int(pb.chosen[i])] > 0 or sc[i] > 0]

    while time.time() - t0 < time_limit:
        it += 1
        if it % 300 == 0:
            active = [i for i in range(n)
                      if tpen[i][int(pb.chosen[i])] > 0 or sc[i] > 0]

        seed_i = rng.choice(active) if (active and rng.random() < 0.85) else rng.randrange(n)

        k_ruin = rng.randint(ruin_min, ruin_max)
        ruin = {seed_i}
        if group_ruin and soft_groups and rng.random() < group_prob:
            # Prefer a still-expensive whole group; sum(sc) is a cheap proxy.
            weights = [max(1, sum(sc[i] for i in g)) for g in soft_groups]
            gi = rng.choices(range(len(soft_groups)), weights=weights, k=1)[0]
            members = soft_groups[gi]
            seed_i = rng.choice(members)
            ruin = set(members)
            k_ruin = max(k_ruin, len(ruin))
        nbrs = pb.nbr[seed_i]
        if len(nbrs):
            take = min(len(nbrs), max(1, k_ruin // 2))
            for j in rng.sample(list(nbrs), take):
                ruin.add(int(j))
        while len(ruin) < k_ruin:
            ruin.add(rng.randrange(n))
        ruin = list(ruin)

        old = {i: int(pb.chosen[i]) for i in ruin}
        affected = set(ruin)
        for i in ruin:
            for (j, code, par, pen, reverse) in rel[i]:
                affected.add(j)
        old_sc = {i: sc[i] for i in affected}
        old_tt = time_total

        for i in ruin:
            pb.chosen[i] = -1
            pb.cur_start[i] = 0
            pb.cur_len[i] = 0
            pb.cur_dmask[i] = 0
            pb.cur_wmask[i] = 0

        rng.shuffle(ruin)
        ok = True
        for i in ruin:
            best_k, best_key = None, None
            nc = len(pb.c_start[i])
            order = list(range(nc))
            if nc > cand_cap:
                rng.shuffle(order)
                order = order[:cand_cap]
            for k in order:
                if pb.violated_partners(i, k):
                    continue
                key = (tpen[i][k] * w_time + soft_of(i, k) * w_dist,
                       int(pb.block_pen[i][k]))
                if best_key is None or key < best_key:
                    best_k, best_key = k, key
            if best_k is None:
                ok = False
                break
            pb.set_candidate(i, best_k)

        if not ok:
            for i in ruin:
                pb.chosen[i] = -1
                pb.cur_start[i] = 0
                pb.cur_len[i] = 0
                pb.cur_dmask[i] = 0
                pb.cur_wmask[i] = 0
            for i, k in old.items():
                pb.set_candidate(i, k)
            continue

        # incremental recompute
        time_total = old_tt - sum(tpen[i][old[i]] for i in old) \
                    + sum(tpen[i][int(pb.chosen[i])] for i in ruin)
        for i in affected:
            sc[i] = soft_of(i, int(pb.chosen[i])) if pb.chosen[i] >= 0 else 0
        soft_total += 0.5 * sum(sc[i] - old_sc[i] for i in affected)
        new_cost = time_total * w_time + soft_total * w_dist
        delta = new_cost - cur_cost

        if delta < 0 or rng.random() < noise:
            cur_cost = new_cost
            accepted += 1
            if new_cost < best_cost:
                best_cost = new_cost
                best = pb.chosen.copy()
                improved += 1
                if verbose:
                    print(f"  it={it} cost={new_cost} t={time.time()-t0:.1f}s", flush=True)
        else:
            time_total = old_tt
            for i in affected:
                sc[i] = old_sc[i]
            soft_total = 0.5 * sum(sc)
            for i in range(n):
                pass
            for i in ruin:
                pb.chosen[i] = -1
                pb.cur_start[i] = 0
                pb.cur_len[i] = 0
                pb.cur_dmask[i] = 0
                pb.cur_wmask[i] = 0
            for i, k in old.items():
                pb.set_candidate(i, k)

    for i in range(n):
        pb.set_candidate(i, int(best[i]))
    hard = pb.all_violating_pairs()
    print(f"final: hard={len(hard)} proxy={best_cost} iterations={it} "
          f"accepted={accepted} improved={improved} seconds={time.time()-t0:.1f}")
    assert not hard

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
