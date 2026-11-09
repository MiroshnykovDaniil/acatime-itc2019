#!/usr/bin/env python3
"""Profile an ITC 2019 instance XML: structure stats needed by the converter design.

Usage: python3 profile_instance.py <instance.xml>
"""
import sys
import collections
import xml.etree.ElementTree as ET


def main(path):
    tree = ET.parse(path)
    root = tree.getroot()
    nr_days = int(root.get("nrDays"))
    nr_weeks = int(root.get("nrWeeks"))
    slots_per_day = int(root.get("slotsPerDay"))
    opt = root.find("optimization")
    weights = dict(time=int(opt.get("time")), room=int(opt.get("room")),
                   distribution=int(opt.get("distribution")), student=int(opt.get("student")))

    print(f"instance={root.get('name')} nrDays={nr_days} nrWeeks={nr_weeks} slotsPerDay={slots_per_day}")
    print(f"weights={weights}")

    rooms = root.find("rooms").findall("room")
    print(f"rooms={len(rooms)}")
    n_unavail = sum(len(r.findall("unavailable")) for r in rooms)
    n_travel = sum(len(r.findall("travel")) for r in rooms)
    caps = sorted(int(r.get("capacity")) for r in rooms)
    print(f"  capacity min/med/max = {caps[0]}/{caps[len(caps)//2]}/{caps[-1]}  unavailable={n_unavail}  travel={n_travel}")

    # classes live in classes/config/subpart/class
    n_conf = 0; n_subpart = 0
    classes = []
    for cfg in root.find("classes").findall("config"):
        n_conf += 1
        for subpart in cfg.findall("subpart"):
            n_subpart += 1
            classes.extend(subpart.findall("class"))
    print(f"classes={len(classes)}")

    # class stats
    n_noroom = 0
    room_counts = []
    time_counts = []
    limits = []
    starts = set()
    lengths = collections.Counter()
    days_used = collections.Counter()
    weeks_used = collections.Counter()
    parent_n = 0
    for c in classes:
        rooms_attr = c.get("room")
        if rooms_attr is None or rooms_attr == "false":
            n_noroom += 1
        else:
            room_counts.append(len(rooms_attr.split(",")))
        times = c.findall("time")
        time_counts.append(len(times))
        limits.append(int(c.get("limit")))
        if c.get("parent") is not None:
            parent_n += 1
        for t in times:
            d = t.get("days"); w = t.get("weeks")
            starts.add(int(t.get("start")))
            lengths[int(t.get("length"))] += 1
            for i, ch in enumerate(d):
                if ch == "1":
                    days_used[i] += 1
            for i, ch in enumerate(w):
                if ch == "1":
                    weeks_used[i] += 1

    print(f"  no-room classes={n_noroom}")
    if room_counts:
        rc = sorted(room_counts)
        print(f"  rooms/class min/med/max = {rc[0]}/{rc[len(rc)//2]}/{rc[-1]}  avg={sum(rc)/len(rc):.2f}")
    tc = sorted(time_counts)
    print(f"  times/class min/med/max = {tc[0]}/{tc[len(tc)//2]}/{tc[-1]}  avg={sum(tc)/len(tc):.2f}")
    lim = sorted(limits)
    print(f"  limit min/med/max = {lim[0]}/{lim[len(lim)//2]}/{lim[-1]}  total_students_possible={sum(lim)}")
    print(f"  distinct starts={len(starts)}: {sorted(starts)}")
    print(f"  distinct lengths: {dict(sorted(lengths.items()))}")
    print(f"  day-of-week usage (7 bits): {[days_used.get(i,0) for i in range(nr_days)]}")
    print(f"  week usage: {[weeks_used.get(i,0) for i in range(nr_weeks)]}")
    print(f"  classes with parent={parent_n}")

    # total meetings (occurrences)
    total_meet = 0
    for c in classes:
        for t in c.findall("time"):
            total_meet += t.get("days").count("1") * t.get("weeks").count("1")
    print(f"  max possible meetings if one candidate each={total_meet} (upper bound; each class picks ONE candidate)")

    # distribution
    dists = root.find("distributions").findall("distribution")
    by_type = collections.Counter()
    by_req = collections.Counter()
    by_pen = collections.Counter()
    pair_counts = collections.Counter()
    for d in dists:
        typ = d.get("type"); req = d.get("required", "false"); pen = d.get("penalty", "0")
        con = d.findall("class")
        by_type[typ] += 1
        by_req[req] += 1
        if req == "false":
            by_pen[int(pen)] += 1
        pair_counts[len(con)] += 1
    print(f"distributions={len(dists)} by_type={dict(by_type)}")
    print(f"  required vs soft: {dict(by_req)}  soft penalties: {dict(by_pen)}")
    print(f"  class-pair sizes (nr of classes in constraint): {dict(pair_counts)}")

    # travel times only for some types
    trav_needed = {"SameGroup", "SameAttendees", "Precedence", "SameTime", "SameStart", "SameDays"}
    # also compute per-type required counts
    per_type_req = collections.defaultdict(collections.Counter)
    for d in dists:
        per_type_req[d.get("type")][d.get("required", "false")] += 1
    for t in sorted(per_type_req):
        print(f"    {t}: {dict(per_type_req[t])}")


if __name__ == "__main__":
    main(sys.argv[1])
