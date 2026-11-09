#!/usr/bin/env python3
"""Extract a small standalone ITC 2019 sub-instance (for pipeline smoke tests and
a size-vs-feasibility scaling experiment).

Selection: BFS over the class-constraint graph (classes are linked if they share
any distribution constraint), starting from the class with the highest constraint
degree (or a given seed). Distribution constraints are kept only if ALL their
classes are inside the selection (required for a valid standalone instance).
All rooms are kept.

Usage: python3 tools/subset.py data/tg-fal17_reduced.xml 40 out/tg-fal17/sub40.xml [seed_class_id]
"""

import sys
import os
import xml.etree.ElementTree as ET
from collections import deque

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from convert import parse_instance


def main():
    src, n_target, out = sys.argv[1], int(sys.argv[2]), sys.argv[3]
    seed = int(sys.argv[4]) if len(sys.argv) > 4 else None
    inst = parse_instance(src)

    # constraint graph
    adj = {c["classId"]: set() for c in inst["classes"]}
    for d in inst["distributions"]:
        ids = d["classes"]
        for i in range(len(ids)):
            for j in range(i + 1, len(ids)):
                adj[ids[i]].add(ids[j])
                adj[ids[j]].add(ids[i])

    if seed is None:
        seed = max(adj, key=lambda k: len(adj[k]))

    # BFS
    sel = {seed}
    q = deque([seed])
    while q and len(sel) < n_target:
        u = q.popleft()
        for v in sorted(adj[u]):
            if v not in sel and len(sel) < n_target:
                sel.add(v)
                q.append(v)

    cls_by_id = {c["classId"]: c for c in inst["classes"]}
    kept_dists = [d for d in inst["distributions"] if all(x in sel for x in d["classes"])]

    root = ET.Element("problem", dict(
        name="%s-sub%d" % (inst["name"], len(sel)),
        nrDays=str(inst["nrDays"]), nrWeeks=str(inst["nrWeeks"]),
        slotsPerDay=str(inst["slotsPerDay"])))
    w = inst["weights"]
    ET.SubElement(root, "optimization", dict(time=str(w["time"]), room=str(w["room"]),
                                             distribution=str(w["distribution"]),
                                             student=str(w["student"])))
    rooms_el = ET.SubElement(root, "rooms")
    # parse rooms from the source tree for exact copy
    src_root = ET.parse(src).getroot()
    for r in src_root.find("rooms").findall("room"):
        rooms_el.append(r)

    courses_el = ET.SubElement(root, "courses")
    course_el = ET.SubElement(courses_el, "course", {"id": "1"})
    cfg_el = ET.SubElement(course_el, "config", {"id": "1"})
    sub_el = ET.SubElement(cfg_el, "subpart", {"id": "1"})
    for cid in sorted(sel):
        c = cls_by_id[cid]
        ce = ET.SubElement(sub_el, "class", {"id": str(cid), "limit": str(c["limit"])})
        if c["parent"] is not None:
            ce.set("parent", str(c["parent"]))
        for r in c["rooms"]:
            ET.SubElement(ce, "room", {"id": str(r["id"]), "penalty": str(r["penalty"])})
        for t in c["times"]:
            ET.SubElement(ce, "time", {"days": t["days"], "start": str(t["start"]),
                                       "length": str(t["length"]), "weeks": t["weeks"],
                                       "penalty": str(t["penalty"])})

    dists_el = ET.SubElement(root, "distributions")
    src_dists = src_root.find("distributions")
    src_dlist = src_dists.findall("distribution")
    # kept_dists preserves order of inst["distributions"] which mirrors source order
    it = iter(kept_dists)
    src_by_typ = []
    # rebuild distribution elements directly from parsed data is lossy for type strings
    # with parameters; instead filter source elements by their classes.
    for de in src_dlist:
        ids = [int(e.get("id")) for e in de.findall("class")]
        if all(x in sel for x in ids):
            dists_el.append(de)
    ET.SubElement(root, "students")

    os.makedirs(os.path.dirname(out), exist_ok=True)
    ET.ElementTree(root).write(out, encoding="utf-8", xml_declaration=True)

    print("classes: %d (target %d), kept distributions: %d, seed=%d" %
          (len(sel), n_target, len(dists_el.findall("distribution")), seed))


if __name__ == "__main__":
    main()
