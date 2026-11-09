#!/usr/bin/env python3
"""Post-process a runner slot-dump into an ITC-canonical solution.

Steps:
 1. Per class: derive the chosen candidate time = the candidate whose meeting set
    EXACTLY equals the slot (week,dow,pair) set (first match in XML order).
    Classes whose slots don't match any candidate are 'mixed' (invalid) and reported.
 2. Room consolidation: one room per class from its candidate list, respecting
    per-room unavailable windows and cross-class occupancy (per week,dow,time cells),
    greedy: hardest classes first (fewest feasible rooms, then more meetings, then
    bigger limit), choosing minimal room penalty.
 3. Export solution.json (input for tools/score.py) + stats.

Usage: python3 tools/post.py out/tg-fal17/meta.json out/tg-fal17/run-default-....json
"""

import json
import sys
from collections import defaultdict


def cells_of(cand):
    """[(week, dow, start, length)] from days/start/weeks/length."""
    ms = []
    for wi, chw in enumerate(cand["weeks"]):
        if chw == "1":
            for di, chd in enumerate(cand["days"]):
                if chd == "1":
                    ms.append((wi, di, cand["start"], cand["length"]))
    return ms


def room_blocked(room_unavail, w, dow, start, length):
    for u in room_unavail:
        if isinstance(u, dict):
            ud, us, ul, uw = u["days"], u["start"], u["length"], u["weeks"]
        else:
            ud, us, ul, uw = u
        if dow < len(ud) and w < len(uw) and ud[dow] == "1" and uw[w] == "1" \
                and start < us + ul and us < start + length:
            return True
    return False


def main():
    argv = [a for a in sys.argv[1:] if a != "--prior"]
    prior_path = sys.argv[sys.argv.index("--prior") + 1] if "--prior" in sys.argv else None
    meta_path, run_path = argv[0], argv[1]
    meta = json.load(open(meta_path))
    run = json.load(open(run_path))
    classes = {c["classId"]: c for c in meta["classes"]}
    starts = meta["starts"]
    spair = {s: i + 1 for i, s in enumerate(starts)}
    base = tuple(meta["baseDate"])  # (y,m,d)

    results = run.get("results", [])
    if not results:
        print("no results in run file; nothing to do")
        return
    best = max(results, key=lambda r: r.get("totalEstimation", -10**18))
    slots = best["slots"]

    # regroup slots by class
    per_class = defaultdict(list)
    for s in slots:
        per_class[s["classId"]].append(s)

    # 1) candidate pinning
    chosen = {}
    mixed = []
    for cid, c in classes.items():
        ss = per_class.get(cid, [])
        if len(ss) != c["meetings"]:
            mixed.append((cid, "slot-count", len(ss), c["meetings"]))
            continue
        cells = set()
        ok = True
        for s in ss:
            # date -> (week,dow) via dayIndex from base date
            y, m, d = map(int, s["date"].split("-"))
            import datetime
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
            mixed.append((cid, "no-exact-candidate", len(cells), 0))
        else:
            chosen[cid] = dict(cand=pick, t=c["times"][pick])

    print(f"classes={len(classes)} pinned={len(chosen)} mixed={len(mixed)}")
    if mixed:
        print("mixed examples:", mixed[:5])

    # 2) room consolidation (attempted per result, with randomized greedy restarts)
    import random

    def consolidate(res_slots, seed=42, tries=24):
        per_class_r = defaultdict(list)
        for s in res_slots:
            per_class_r[s["classId"]].append(s)
        ch = {}
        mixed_r = []
        for cid, c in classes.items():
            ss = per_class_r.get(cid, [])
            if len(ss) != c["meetings"]:
                mixed_r.append((cid, "slot-count", len(ss), c["meetings"]))
                continue
            cells = set()
            for s in ss:
                y, m, dd = map(int, s["date"].split("-"))
                import datetime
                day_ix = (datetime.date(y, m, dd) - datetime.date(*base)).days
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
                mixed_r.append((cid, "no-exact-candidate", len(cells), 0))
            else:
                ch[cid] = dict(cand=pick, t=c["times"][pick])
        if mixed_r:
            return None, mixed_r, {}

        info = []
        for cid, c in ch.items():
            cc = classes[cid]
            cells = cells_of(c["t"])
            feas = []
            for r in cc["rooms"]:
                rinfo = meta["rooms"][str(r["id"])]
                if all(not room_blocked(rinfo["unavailable"], w, dow, st, ln)
                       for (w, dow, st, ln) in cells):
                    feas.append(r)
            info.append(dict(cid=cid, cells=cells, feas=feas, limit=cc["limit"],
                             meetings=len(cells), noroom=not cc["hasRoom"]))

        rnd = random.Random(seed)
        base_order = sorted([x for x in info if not x["noroom"]],
                            key=lambda x: (len(x["feas"]), -x["meetings"], -x["limit"]))
        best_partial = (None, {})
        for t in range(tries):
            order = list(base_order)
            if t > 0:
                # shuffle the tail, keep the scarcest third in front
                cut = max(1, len(order) // 3)
                tail = order[cut:]
                rnd.shuffle(tail)
                order = order[:cut] + tail
            room_cells_r = defaultdict(list)
            ra = {}
            infeas = []
            # prior rooms for pinned classes: fixed by construction
            free_order = []
            for ci in order:
                pr = prior_rooms.get(ci["cid"])
                if pr is not None:
                    ra[ci["cid"]] = pr
                    for (w, dow, st, ln) in ci["cells"]:
                        room_cells_r[pr].append((w, dow, st, ln, ci["cid"]))
                else:
                    free_order.append(ci)
            order = free_order
            for ci in order:
                ok = False
                for r in sorted(ci["feas"], key=lambda x: (x["penalty"], x["id"])):
                    rid = r["id"]
                    if not any(w == w2 and dow == dow2 and st < s2 + l2 and s2 < st + ln
                               for (w, dow, st, ln) in ci["cells"]
                               for (w2, dow2, s2, l2, _) in room_cells_r.get(rid, [])):
                        ra[ci["cid"]] = rid
                        for (w, dow, st, ln) in ci["cells"]:
                            room_cells_r[rid].append((w, dow, st, ln, ci["cid"]))
                        ok = True
                        break
                if not ok:
                    infeas.append(ci["cid"])
            if not infeas:
                return ch, [], ra
            if best_partial[0] is None or len(infeas) < len(best_partial[0]):
                best_partial = (infeas, ra)
        return ch, best_partial[0], best_partial[1]

    prior_rooms = {}
    if prior_path:
        prior = json.load(open(prior_path))
        prior_rooms = {c["classId"]: c["room"] for c in prior["classes"] if c.get("room") is not None}
        print(f"prior rooms for {len(prior_rooms)} pinned classes")

    results_sorted = sorted(results, key=lambda r: -r.get("totalEstimation", -10**18))
    chosen, mixed, room_assignment = {}, [], {}
    for res in results_sorted:
        ch, mix, ra = consolidate(res["slots"])
        print(f"result est={res.get('totalEstimation')}: pinned={len(ch)} mixed={len(mix)} room_infeasible={len(mix) if mix else 0}")
        if ch and not mix:
            # count room failures among roomed classes
            n_room_fail = sum(1 for cid in ch if classes[cid]["hasRoom"] and cid not in ra)
            if n_room_fail == 0:
                chosen, mixed, room_assignment = ch, [], ra
                best = res
                break
        if not chosen and ch:
            chosen, mixed, room_assignment = ch, mix, ra
            best = res

    # 2b) repair: re-pick alternative candidate times for room-infeasible classes,
    #     re-checking hard constraints conservatively (final referee = score.py)
    def hard_ok_with(cid, cand_t, ch_map):
        c = classes[cid]
        dmask = sum(1 << i for i, ch_ in enumerate(cand_t["days"]) if ch_ == "1")
        wmask = sum(1 << i for i, ch_ in enumerate(cand_t["weeks"]) if ch_ == "1")
        sx, ex = cand_t["start"], cand_t["start"] + cand_t["length"]
        checks = [("sa", c["saPartners"]), ("no", c["noPartners"]),
                  ("st", c["sameTimePartners"]), ("sd", c["sameDaysPartners"])]
        for kind, partners in checks:
            for pid in partners:
                if pid not in ch_map:
                    continue
                pt = ch_map[pid]["t"]
                pd = sum(1 << i for i, ch_ in enumerate(pt["days"]) if ch_ == "1")
                pw = sum(1 << i for i, ch_ in enumerate(pt["weeks"]) if ch_ == "1")
                ps, pe = pt["start"], pt["start"] + pt["length"]
                if kind == "sa":
                    tv = c["travelMax"].get(pid, 0)
                    if dmask & pd and wmask & pw and sx < pe and ps < ex:
                        if not (ex + tv <= ps or pe + tv <= sx):
                            return False
                elif kind == "no":
                    if dmask & pd and wmask & pw and sx < pe and ps < ex:
                        return False
                elif kind == "st":
                    if not ((sx <= ps and pe <= ex) or (ps <= sx and ex <= pe)):
                        return False
                elif kind == "sd":
                    o = dmask | pd
                    if not (o == dmask or o == pd):
                        return False
        return True

    def room_free_for(cid, cand_t, taken):
        cc = classes[cid]
        cells = cells_of(cand_t)
        for r in cc["rooms"]:
            rinfo = meta["rooms"][str(r["id"])]
            if any(room_blocked(rinfo["unavailable"], w, dow, st, ln) for (w, dow, st, ln) in cells):
                continue
            if not any(w == w2 and dow == dow2 and st < s2 + l2 and s2 < st + ln
                       for (w, dow, st, ln) in cells
                       for (w2, dow2, s2, l2, _) in taken.get(r["id"], [])):
                return r
        return None

    def place_simple(cid, t, room_assignment, room_cells_map):
        r = room_free_for(cid, t, room_cells_map)
        if r is None:
            return False
        room_assignment[cid] = r["id"]
        for (w, dow, st, ln) in cells_of(t):
            room_cells_map[r["id"]].append((w, dow, st, ln, cid))
        return True

    def repair_round(ch_map, infeas_ids, room_assignment, room_cells_map):
        changed = 0
        for cid in list(infeas_ids):
            cc = classes[cid]
            order = sorted(range(len(cc["times"])),
                           key=lambda k: (cc["times"][k]["penalty"], k))
            done = False
            for k in order:
                t = cc["times"][k]
                if k == ch_map[cid]["cand"] and cid in room_assignment:
                    continue  # same time and already placed elsewhere: nothing to do
                if not hard_ok_with(cid, t, ch_map):
                    continue
                if place_simple(cid, t, room_assignment, room_cells_map):
                    ch_map[cid] = dict(cand=k, t=t)
                    changed += 1
                    done = True
                    break
                # eviction repair: free a needed room-cell by moving its occupant
                cells = cells_of(t)
                for r in cc["rooms"]:
                    rinfo = meta["rooms"][str(r["id"])]
                    if any(room_blocked(rinfo["unavailable"], w, dow, st, ln)
                           for (w, dow, st, ln) in cells):
                        continue
                    victims = {e[4] for e in room_cells_map.get(r["id"], [])
                               if any(w == w2 and dow == dow2 and st < s2 + l2 and s2 < st + ln
                                      for (w, dow, st, ln) in cells
                                      for (w2, dow2, s2, l2, _) in [e])}
                    for vid in victims:
                        vinfo_c = classes[vid]
                        if len(vinfo_c["rooms"]) <= 1 or vid in infeas_ids:
                            continue
                        v_t = ch_map[vid]["t"]
                        old_room = room_assignment[vid]
                        # evict victim
                        room_cells_map[old_room] = [e for e in room_cells_map.get(old_room, [])
                                                    if e[4] != vid]
                        del room_assignment[vid]
                        # try: class in this room, victim elsewhere
                        if not any(w == w2 and dow == dow2 and st < s2 + l2 and s2 < st + ln
                                   for (w, dow, st, ln) in cells
                                   for (w2, dow2, s2, l2, _) in room_cells_map.get(r["id"], [])):
                            room_assignment[cid] = r["id"]
                            for (w, dow, st, ln) in cells:
                                room_cells_map[r["id"]].append((w, dow, st, ln, cid))
                            v_alt = [t2 for t2 in vinfo_c["times"] if t2 is not v_t]
                            placed_v = False
                            for t2 in sorted(v_alt, key=lambda x: (x["penalty"])):
                                if not hard_ok_with(vid, t2, ch_map):
                                    continue
                                if place_simple(vid, t2, room_assignment, room_cells_map):
                                    ch_map[vid] = dict(cand=vinfo_c["times"].index(t2), t=t2)
                                    placed_v = True
                                    break
                            if placed_v:
                                ch_map[cid] = dict(cand=k, t=t)
                                changed += 1
                                done = True
                                break
                            # victim failed: undo
                            del room_assignment[cid]
                            for e in [x for x in room_cells_map.get(r["id"], []) if x[4] == cid]:
                                room_cells_map[r["id"]].remove(e)
                        # restore victim
                        if vid not in room_assignment:
                            room_assignment[vid] = old_room
                            for (w, dow, st, ln) in cells_of(v_t):
                                room_cells_map[old_room].append((w, dow, st, ln, vid))
                    if done:
                        break
                if done:
                    break
        return changed

    infeas_ids = [cid for cid in chosen
                  if classes[cid]["hasRoom"] and cid not in room_assignment]
    if chosen and (mixed or infeas_ids):
        # rebuild room occupancy from the current assignment
        room_cells = defaultdict(list)
        for cid, rid in room_assignment.items():
            for (w, dow, st, ln) in cells_of(chosen[cid]["t"]):
                room_cells[rid].append((w, dow, st, ln, cid))
        print(f"repair: {len(infeas_ids)} room-infeasible classes")
        for rounds in range(8):
            nch = repair_round(chosen, infeas_ids, room_assignment, room_cells)
            infeas_ids = [cid for cid in chosen
                          if classes[cid]["hasRoom"] and cid not in room_assignment]
            print(f"  round {rounds}: repaired={nch} remaining={len(infeas_ids)}")
            if not infeas_ids or nch == 0:
                break

    # 3) export
    sol = dict(
        instance=meta["instance"],
        classes=[dict(classId=cid, candidate=ch["cand"],
                      days=ch["t"]["days"], start=ch["t"]["start"],
                      length=ch["t"]["length"], weeks=ch["t"]["weeks"],
                      room=room_assignment.get(cid),
                      roomPenalty=next((r["penalty"] for r in classes[cid]["rooms"]
                                        if r["id"] == room_assignment.get(cid)), 0))
                 for cid, ch in sorted(chosen.items())],
        runtime=dict(seconds=run.get("runSeconds", 0), technique="AcaTime DFS"),
    )
    out_path = run_path.replace(".json", "-sol.json")
    with open(out_path, "w") as f:
        json.dump(sol, f, indent=1)
    print("wrote", out_path)
    print("estimation reported by solver:", best.get("totalEstimation"))


if __name__ == "__main__":
    main()
