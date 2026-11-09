#!/usr/bin/env python3
"""Independent ITC 2019 scorer/validator (PATAT-2018 spec formulas).

Reads:  instance XML (ground truth) + solution JSON (candidate-based export)
Writes: validation report + official cost; optionally ITC-canonical solution XML.

Solution JSON format (produced by tools/post.py):
{
  "instance": "...",
  "classes": [ {"classId": int, "candidate": int,   # index into the class's time list
                "days": "1000000", "start": 164, "length": 27, "weeks": "00001000000000",
                "room": 4|null, "roomPenalty": 0} , ... ],
  "runtime": {...}
}

Cost (weighted sum, minimized):
  total = w_time*sumTimePen + w_room*sumRoomPen + w_dist*sumDistPen + w_student*studentConflicts

Usage: python3 tools/score.py <instance.xml> <solution.json> [--emit-xml out.xml]
"""

import json
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict


def parse_instance(path):
    root = ET.parse(path).getroot()
    nr_days = int(root.get("nrDays"))
    nr_weeks = int(root.get("nrWeeks"))
    inst = dict(
        name=root.get("name"),
        nrDays=int(root.get("nrDays")),
        nrWeeks=int(root.get("nrWeeks")),
        slotsPerDay=int(root.get("slotsPerDay")),
    )
    opt = root.find("optimization")
    inst["weights"] = dict(time=int(opt.get("time", "0")), room=int(opt.get("room", "0")),
                           distribution=int(opt.get("distribution", "0")),
                           student=int(opt.get("student", "0")))
    inst["rooms"] = {}
    for r in root.find("rooms").findall("room"):
        rid = int(r.get("id"))
        inst["rooms"][rid] = dict(
            capacity=int(r.get("capacity", "0")),
            unavailable=[((u.get("days") or "").ljust(nr_days, "0"), int(u.get("start")),
                          int(u.get("length")), (u.get("weeks") or "").ljust(nr_weeks, "0"))
                         for u in r.findall("unavailable")],
            travel={int(t.get("room")): int(t.get("value")) for t in r.findall("travel")},
        )
    inst["classes"] = {}
    for crs in root.find("courses").findall("course"):
        for cfg in crs.findall("config"):
            for sub in cfg.findall("subpart"):
                for c in sub.findall("class"):
                    cid = int(c.get("id"))
                    rooms_c = [(int(r.get("id")), int(r.get("penalty", "0") or 0))
                               for r in c.findall("room")]
                    if not rooms_c and c.get("room") is not None and c.get("room") != "false":
                        rooms_c = [(int(x), 0) for x in c.get("room").split(",") if x]
                    inst["classes"][cid] = dict(
                        limit=int(c.get("limit", "0")),
                        rooms=dict(rooms_c),
                        times=[dict(days=(t.get("days") or "").ljust(nr_days, "0"),
                                    start=int(t.get("start")),
                                    length=int(t.get("length")),
                                    weeks=(t.get("weeks") or "").ljust(nr_weeks, "0"),
                                    penalty=int(t.get("penalty", "0") or 0))
                               for t in c.findall("time")],
                    )
    dists = []
    for d in root.find("distributions").findall("distribution"):
        typ = d.get("type")
        param = 0
        if "(" in typ:
            typ, ps = typ.split("(", 1)
            param = int(ps.split(")", 1)[0])
        dists.append(dict(type=typ, param=param,
                          required=d.get("required", "false") == "true",
                          penalty=int(d.get("penalty", "0") or 0),
                          classes=[int(e.get("id")) for e in d.findall("class")]))
    inst["distributions"] = dists
    return inst


def meetings_of(sol_c):
    """[(week, dow)] for the assigned (days,start,weeks)."""
    ms = []
    for wi, chw in enumerate(sol_c["weeks"]):
        if chw == "1":
            for di, chd in enumerate(sol_c["days"]):
                if chd == "1":
                    ms.append((wi, di))
    return ms


def travel(a_rooms, b_rooms, rooms):
    return max((rooms[ra]["travel"].get(rb, 0) for ra in a_rooms for rb in b_rooms), default=0)


class Violations:
    def __init__(self):
        self.items = []

    def add(self, code, msg):
        self.items.append((code, msg))


def time_overlap(a, b):
    return a["start"] < b["start"] + b["length"] and b["start"] < a["start"] + a["length"]


def days_and(a, b):
    return any(x == "1" and y == "1" for x, y in zip(a, b))


def weeks_and(a, b):
    return any(x == "1" and y == "1" for x, y in zip(a, b))


def days_subset(a, b):
    return all((x == "1") <= (y == "1") for x, y in zip(a, b))


def weeks_subset(a, b):
    return all((x == "1") <= (y == "1") for x, y in zip(a, b))


def first_bit(s):
    return s.index("1") if "1" in s else len(s)


def pair_violated(tc, A, B, par, rooms, room_lookup):
    """Class-level formula check between assigned values A, B (dicts with
    days/start/length/weeks/room)."""
    d_and = days_and(A["days"], B["days"])
    w_and = weeks_and(A["weeks"], B["weeks"])
    if tc == "SameStart":
        return A["start"] != B["start"]
    if tc == "SameTime":
        sa, ea = A["start"], A["start"] + A["length"]
        sb, eb = B["start"], B["start"] + B["length"]
        return not ((sa <= sb and eb <= ea) or (sb <= sa and ea <= eb))
    if tc == "DifferentTime":
        return time_overlap(A, B)
    if tc == "SameDays":
        return not (days_subset(A["days"], B["days"]) or days_subset(B["days"], A["days"]))
    if tc == "DifferentDays":
        return d_and
    if tc == "SameWeeks":
        return not (weeks_subset(A["weeks"], B["weeks"]) or weeks_subset(B["weeks"], A["weeks"]))
    if tc == "DifferentWeeks":
        return w_and
    if tc == "SameRoom":
        return A["room"] != B["room"]
    if tc == "DifferentRoom":
        return A["room"] == B["room"]
    if tc == "NotOverlap":
        return time_overlap(A, B) and d_and and w_and
    if tc == "Overlap":
        return time_overlap(A, B) and d_and and w_and
    if tc == "SameAttendees":
        tv = 0
        if A["room"] is not None and B["room"] is not None:
            tv = rooms[A["room"]]["travel"].get(B["room"], 0)
        if not (time_overlap(A, B) and d_and and w_and):
            return False
        # rooms must be far enough apart
        if A["room"] is None or B["room"] is None:
            return True
        return not ((A["start"] + A["length"] + tv <= B["start"]) or
                    (B["start"] + B["length"] + tv <= A["start"]))
    if tc == "Precedence":
        wa, wb = first_bit(A["weeks"]), first_bit(B["weeks"])
        if wa != wb:
            return not (wa < wb)
        da, db = first_bit(A["days"]), first_bit(B["days"])
        if da != db:
            return not (da < db)
        return not (A["start"] + A["length"] <= B["start"])
    if tc == "WorkDay":
        if not (d_and and w_and):
            return False
        s = min(A["start"], B["start"])
        e = max(A["start"] + A["length"], B["start"] + B["length"])
        return e - s > par
    if tc == "MinGap":
        if not (d_and and w_and):
            return False
        return not ((A["start"] + A["length"] + par <= B["start"]) or
                    (B["start"] + B["length"] + par <= A["start"]))
    return None  # unsupported


def maxdays_violation(sols, D):
    days = set()
    for s in sols:
        for di, ch in enumerate(s["days"]):
            if ch == "1":
                days.add(di)
    return max(0, len(days) - D)


def maxdayload_violation(sols, S, nrWeeks, nrDays):
    over = 0
    for w in range(nrWeeks):
        for d in range(nrDays):
            load = sum(s["length"] for s in sols
                       if s["days"][d] == "1" and s["weeks"][w] == "1")
            over += max(0, load - S)
    return over // nrWeeks


def maxbreaks_violation(sols, R, S, nrWeeks, nrDays):
    over = 0
    for w in range(nrWeeks):
        for d in range(nrDays):
            ivs = sorted((s["start"], s["start"] + s["length"]) for s in sols
                         if s["days"][d] == "1" and s["weeks"][w] == "1")
            if not ivs:
                continue
            blocks = []
            cs, ce = ivs[0]
            for st, en in ivs[1:]:
                if st - ce > S:
                    blocks.append((cs, ce))
                    cs, ce = st, en
                else:
                    ce = max(ce, en)
            blocks.append((cs, ce))
            over += max(0, len(blocks) - 1 - R)
    return over // nrWeeks


def maxblock_violation(sols, M, S, nrWeeks, nrDays):
    over = 0
    for w in range(nrWeeks):
        for d in range(nrDays):
            ivs = sorted((s["start"], s["start"] + s["length"]) for s in sols
                         if s["days"][d] == "1" and s["weeks"][w] == "1")
            if not ivs:
                continue
            blocks = []
            cs, ce = ivs[0]
            for st, en in ivs[1:]:
                if st - ce > S:
                    blocks.append((cs, ce))
                    cs, ce = st, en
                else:
                    ce = max(ce, en)
            blocks.append((cs, ce))
            over += sum(1 for cs, ce in blocks if ce - cs > M)
    return over // nrWeeks


def score(inst, sol):
    """sol: {'classes': [...]} -> report dict."""
    classes = inst["classes"]
    rooms = inst["rooms"]
    nrDays, nrWeeks = inst["nrDays"], inst["nrWeeks"]
    v = Violations()

    # --- structural checks
    seen = set()
    for sc in sol["classes"]:
        cid = sc["classId"]
        if cid in seen:
            v.add("dup-class", f"class {cid} appears twice")
        seen.add(cid)
        if cid not in classes:
            v.add("unknown-class", f"class {cid} not in instance")
            continue
        c = classes[cid]
        idx = sc["candidate"]
        if not (0 <= idx < len(c["times"])):
            v.add("bad-candidate", f"class {cid} candidate index {idx} out of range")
            continue
        t = c["times"][idx]
        for k in ("days", "start", "length", "weeks"):
            if str(sc[k]) != str(t[k]):
                v.add("cand-mismatch", f"class {cid} field {k}: {sc[k]} != {t[k]}")
        if sc.get("room") is None:
            if c["rooms"]:
                v.add("no-room", f"class {cid} requires a room")
            continue
        if sc["room"] not in c["rooms"]:
            v.add("bad-room", f"class {cid}: room {sc['room']} not in candidates")
            continue
        # unavailable windows
        r = sc["room"]
        for (ud, us, ul, uw) in rooms[r]["unavailable"]:
            for (w, dow) in meetings_of(sc):
                if ud[dow] == "1" and uw[w] == "1":
                    if sc["start"] < us + ul and us < sc["start"] + sc["length"]:
                        v.add("room-unavailable",
                              f"class {cid} room {r} unavailable at week {w} day {dow}")

    missing = set(classes) - seen
    if missing:
        v.add("missing-classes", f"{len(missing)} classes missing: {sorted(missing)[:10]}...")

    # room uniqueness per (date-cell): meetings of different classes in same room same (w,dow)
    # must also not overlap in time-of-day
    cell_room = defaultdict(list)
    for sc in sol["classes"]:
        if sc.get("room") is None:
            continue
        for (w, dow) in meetings_of(sc):
            cell_room[(w, dow, sc["room"])].append(sc)
    for key, lst in cell_room.items():
        if len(lst) > 1:
            for i in range(len(lst)):
                for j in range(i + 1, len(lst)):
                    if time_overlap(lst[i], lst[j]):
                        v.add("room-collision",
                              f"room {key[2]} double-booked week {key[0]} day {key[1]}: "
                              f"classes {lst[i]['classId']} & {lst[j]['classId']}")

    assigned = {sc["classId"]: sc for sc in sol["classes"] if sc["classId"] in classes}

    # --- distribution
    hard_vio = 0
    soft_pen = 0
    soft_counts = defaultdict(int)
    unsupported = set()
    for d in inst["distributions"]:
        ids = [cid for cid in d["classes"] if cid in assigned]
        if len(ids) < len(d["classes"]):
            v.add("dist-missing-class", f"{d['type']} references missing class(es)")
        if d["type"] in ("MaxDays", "MaxDayLoad", "MaxBreaks", "MaxBlock"):
            sols = [assigned[cid] for cid in ids]
            val = None
            if d["type"] == "MaxDays":
                val = maxdays_violation(sols, d["param"])
            elif d["type"] == "MaxDayLoad":
                val = maxdayload_violation(sols, d["param"], nrWeeks, nrDays)
            elif d["type"] == "MaxBreaks":
                val = maxbreaks_violation(sols, d["param"][0] if isinstance(d["param"], list) else d.get("param2", 0), 0, nrWeeks, nrDays)
            elif d["type"] == "MaxBlock":
                val = maxblock_violation(sols, d["param"][0] if isinstance(d["param"], list) else d.get("param2", 0), 0, nrWeeks, nrDays)
            if val is None:
                unsupported.add(d["type"])
                continue
            if d["required"]:
                if val > 0:
                    hard_vio += 1
                    v.add("hard-dist", f"{d['type']}({d['param']}) violated by {val}")
            else:
                soft_pen += d["penalty"] * val
                if val:
                    soft_counts[d["type"]] += val
            continue
        # pairwise types
        n_vio = 0
        for i in range(len(ids)):
            for j in range(i + 1, len(ids)):
                A, B = assigned[ids[i]], assigned[ids[j]]
                res = pair_violated(d["type"], A, B, d["param"], rooms, None)
                if res is None:
                    unsupported.add(d["type"])
                elif res:
                    n_vio += 1
        if d["required"]:
            if n_vio:
                hard_vio += 1
                v.add("hard-dist", f"{d['type']} violated by {n_vio} pair(s)")
        else:
            soft_pen += d["penalty"] * n_vio
            soft_counts[d["type"]] += n_vio

    # Note: class limit is NOT compared with room capacity; the ITC room
    # domain already encodes suitability (FAQ I1).
    # --- costs
    time_pen = 0
    room_pen = 0
    for sc in sol["classes"]:
        if sc["classId"] not in classes:
            continue
        c = classes[sc["classId"]]
        if 0 <= sc["candidate"] < len(c["times"]):
            time_pen += c["times"][sc["candidate"]]["penalty"]
            if sc.get("room") is not None:
                room_pen += c["rooms"].get(sc["room"], 0)

    w = inst["weights"]
    total = (w["time"] * time_pen + w["room"] * room_pen +
             w["distribution"] * soft_pen + w["student"] * 0)

    return dict(
        feasible=(not v.items) and hard_vio == 0 and not missing,
        violations=[list(x) for x in v.items][:50],
        n_violations=len(v.items),
        hard_violations=hard_vio,
        cost=dict(
            time=time_pen, room=room_pen, distribution=soft_pen, student=0,
            weights=w, total=total,
        ),
        soft_violation_counts=dict(soft_counts),
        unsupported_types=sorted(unsupported),
    )


def emit_itc_xml(inst, sol, path, runtime=None):
    lines = ['<?xml version="1.0" encoding="UTF-8"?>']
    attrs = f'name="{inst["name"]}"'
    if runtime:
        attrs += f' runtime="{runtime.get("seconds", 0):.1f}" cores="{runtime.get("cores", 1)}" technique="{runtime.get("technique", "AcaTime DFS+GA")}" author="{runtime.get("author", "AcaTime benchmark")}"'
    lines.append(f"<solution {attrs}>")
    for sc in sorted(sol["classes"], key=lambda x: x["classId"]):
        a = f'  <class id="{sc["classId"]}" days="{sc["days"]}" start="{sc["start"]}" weeks="{sc["weeks"]}"'
        if sc.get("room") is not None:
            a += f' room="{sc["room"]}"'
        lines.append(a + "/>")
    lines.append("</solution>")
    with open(path, "w") as f:
        f.write("\n".join(lines))


def main():
    inst_path, sol_path = sys.argv[1], sys.argv[2]
    emit_path = None
    if "--emit-xml" in sys.argv:
        emit_path = sys.argv[sys.argv.index("--emit-xml") + 1]
    inst = parse_instance(inst_path)
    with open(sol_path) as f:
        sol = json.load(f)
    rep = score(inst, sol)
    print(json.dumps(rep, indent=1))
    if emit_path:
        emit_itc_xml(inst, sol, emit_path, runtime=sol.get("runtime"))
        print("wrote", emit_path, file=sys.stderr)


if __name__ == "__main__":
    main()
