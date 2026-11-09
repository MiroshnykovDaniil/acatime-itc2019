#!/usr/bin/env python3
"""Build a pin list for incremental solving with a consistency fixpoint.

Given the extended instance's meta and the previous (smaller) solution, select
which solved classes to pin so that EVERY unpinned (new) class keeps at least
one candidate compatible (conservatively) with the pinned assignment. Classes
whose pin would zero-out a new class are unpinned (the DFS re-solves them).

Usage: tools/make_pin.py out/tg-fal17-sub600/meta.json out/tg-fal17-sub550/xxx-sol.json out/pin600.json
"""

import json
import sys


def main():
    meta_path, sol_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
    meta = json.load(open(meta_path))
    sol = json.load(open(sol_path))
    cls = {c["classId"]: c for c in meta["classes"]}
    full_pin = {c["classId"]: c["candidate"] for c in sol["classes"]
                if c["classId"] in cls and c["candidate"] < len(cls[c["classId"]]["times"])}

    def dmask(s):
        return sum(1 << i for i, ch in enumerate(s) if ch == "1")

    def blockers_for(cid, pin_map):
        """Partners whose pin blocks ALL candidates of cid (returns None if some candidate fits)."""
        c = cls[cid]
        blockers = set()
        fits_any = False
        for cand_t in c["times"]:
            dm, wm = dmask(cand_t["days"]), dmask(cand_t["weeks"])
            sx, ex = cand_t["start"], cand_t["start"] + cand_t["length"]
            ok = True
            checks = [("sa", c["saPartners"]), ("no", c["noPartners"]),
                      ("st", c["sameTimePartners"]), ("sd", c["sameDaysPartners"])]
            for kind, partners in checks:
                for pid in partners:
                    if pid not in pin_map:
                        continue
                    pt = cls[pid]["times"][pin_map[pid]]
                    pd, pw = dmask(pt["days"]), dmask(pt["weeks"])
                    ps, pe = pt["start"], pt["start"] + pt["length"]
                    violated = False
                    if kind == "sa":
                        tv = 0
                        if c["rooms"] and cls[pid]["rooms"]:
                            tv = max(meta["rooms"][str(ra)]["travel"].get(rb, 0)
                                     for ra in [r["id"] for r in c["rooms"]]
                                     for rb in [r["id"] for r in cls[pid]["rooms"]])
                        if dm & pd and wm & pw and sx < pe and ps < ex:
                            violated = not (sx + cand_t["length"] + tv <= ps or pe + tv <= sx)
                    elif kind == "no":
                        violated = dm & pd and wm & pw and sx < pe and ps < ex
                    elif kind == "st":
                        violated = not ((sx <= ps and pe <= ex) or (ps <= sx and ex <= pe))
                    elif kind == "sd":
                        o = dm | pd
                        violated = not (o == dm or o == pd)
                    if violated:
                        blockers.add(pid)
                        ok = False
            if ok:
                fits_any = True
                break
        return (fits_any, blockers)

    pin_map = dict(full_pin)
    for it in range(12):
        new_classes = [cid for cid in cls if cid not in pin_map and cls[cid]["times"]]
        zero = []
        for cid in new_classes:
            fits, blockers = blockers_for(cid, pin_map)
            if not fits:
                zero.append((cid, blockers))
        if not zero:
            break
        for cid, blockers in zero:
            print(f"iteration {it}: class {cid} has no compatible candidate; unpinning {sorted(blockers)}")
            for pid in blockers:
                pin_map.pop(pid, None)
    dropped = set(full_pin) - set(pin_map)
    rooms = {c["classId"]: c.get("room") for c in sol["classes"]}
    out = {"instance": meta["instance"], "classes": [
        {"classId": cid, "candidate": k, "room": rooms.get(cid)}
        for cid, k in sorted(pin_map.items())],
        "dropped": sorted(dropped)}
    with open(out_path, "w") as f:
        json.dump(out, f)
    print(f"pin list: {len(pin_map)} pinned, {len(dropped)} dropped -> {out_path}")


if __name__ == "__main__":
    main()
