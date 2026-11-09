#!/usr/bin/env python3
"""ITC 2019 instance XML -> AcaTime SimplifiedScheduleDataDto JSON + meta.

Converts a (reduced or original) ITC 2019 competition instance into:
  - out/<instance>/api-scheduledata.json  (input for the local solver runner)
  - out/<instance>/meta.json              (grid maps, penalties; debug + reference)

The transformation is design-preserving: a candidate time (days,start,length,weeks)
stays a candidate pattern; the solver assigns meetings of ONE chosen candidate to
(date,pair) grid cells; synthetic per-class teachers/groups/types keep the built-in
rules from seeing cross-class interactions (all inter-class semantics live in the
generated constraint scripts).

Usage: python3 tools/convert.py data/tg-fal17_reduced.xml [outdir_base]
       [--room-aware | --room-aware-exact] [--seed-rooms <sol.json>]

--room-aware writes api-scheduledata-roomaware.json with a per-meeting room
relaxation in the generated objective; --room-aware-exact writes
api-scheduledata-roomaware-exact.json with one room per class. --seed-rooms
bakes a room/candidate hint into the room-aware objective (useful when the GA
seed carries a room-feasible assignment). The default DTO
is untouched in both cases.
"""

import json
import sys
import os
import xml.etree.ElementTree as ET
from datetime import date, timedelta

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scripts_gen as SG

# selective built-in cluster pruning: hard SA/NO distribution constraints with
# at most this many classes get a shared group id (a clique in the
# must-not-overlap graph, so the built-in same-group check is sound). Bigger
# constraints are enforced exactly by the validator scripts instead.
CLUSTER_CAP = 100000


def parse_int_attr(e, name, default=None):
    v = e.get(name)
    return None if v is None else int(v)


def parse_days_weeks(s):
    return s  # keep raw strings ('0'/'1'), index 0 = first day/week


def parse_instance(path):
    tree = ET.parse(path)
    root = tree.getroot()
    name = root.get("name")
    nr_days = int(root.get("nrDays"))
    nr_weeks = int(root.get("nrWeeks"))
    slots_per_day = int(root.get("slotsPerDay"))
    assert nr_days == 7, "converter handles 7-day instances (nrDays=%d)" % nr_days
    opt = root.find("optimization")
    weights = dict(
        time=int(opt.get("time", "0")),
        room=int(opt.get("room", "0")),
        distribution=int(opt.get("distribution", "0")),
        student=int(opt.get("student", "0")),
    )

    rooms = {}
    for r in root.find("rooms").findall("room"):
        rid = int(r.get("id"))
        rooms[rid] = dict(
            id=rid,
            capacity=int(r.get("capacity", "0")),
            unavailable=[dict(days=(u.get("days") or "").ljust(nr_days, "0"),
                              start=int(u.get("start")),
                              length=int(u.get("length")),
                              weeks=(u.get("weeks") or "").ljust(nr_weeks, "0"))
                         for u in r.findall("unavailable")],
            travel={int(t.get("room")): int(t.get("value")) for t in r.findall("travel")},
        )

    classes = []
    if root.find("classes") is not None:
        raise SystemExit("original-format <classes> root not supported in this pass; please use the reduced (DTU) instance files")
    for crs in root.find("courses").findall("course"):
        course_id = int(crs.get("id"))
        for cfg in crs.findall("config"):
            config_id = int(cfg.get("id"))
            for sub in cfg.findall("subpart"):
                subpart_id = int(sub.get("id"))
                for c in sub.findall("class"):
                    cid = int(c.get("id"))
                    limit = int(c.get("limit", "0"))
                    parent = c.get("parent")
                    rooms_c = []
                    for rel in c.findall("room"):
                        rooms_c.append(dict(id=int(rel.get("id")),
                                            penalty=int(rel.get("penalty", "0") or 0)))
                    if not rooms_c and c.get("room") is not None:
                        ra = c.get("room")
                        if ra != "false":
                            rooms_c = [dict(id=int(x), penalty=0) for x in ra.split(",") if x]
                    times = []
                    for t in c.findall("time"):
                        times.append(dict(days=(t.get("days") or "").ljust(nr_days, "0"),
                                          start=int(t.get("start")),
                                          length=int(t.get("length")),
                                          weeks=(t.get("weeks") or "").ljust(nr_weeks, "0"),
                                          penalty=int(t.get("penalty", "0") or 0)))
                    assert times, "class %s has no candidate times" % cid
                    classes.append(dict(
                        classId=cid, course=course_id, config=config_id, subpart=subpart_id,
                        limit=limit, parent=parent, rooms=rooms_c, times=times,
                        hasRoom=bool(rooms_c),
                        meetings=(len(times[0]["days"].replace("0", ""))
                                  * len(times[0]["weeks"].replace("0", ""))),
                    ))
    classes.sort(key=lambda c: c["classId"])
    for i, c in enumerate(classes):
        c["index"] = i
        mt = {len(t["days"].replace("0", "")) * len(t["weeks"].replace("0", "")) for t in c["times"]}
        assert len(mt) == 1, "class %d candidates have different meeting counts %s" % (c["classId"], mt)
        c["meetings"] = mt.pop()

    distributions = []
    for d in root.find("distributions").findall("distribution"):
        typ = d.get("type")
        param = 0
        if "(" in typ:
            base, ps = typ.split("(", 1)
            param = int(ps.split(")", 1)[0])
            # note: keep the raw string with param for reference
        distributions.append(dict(
            type=d.get("type"),
            param=param,
            required=(d.get("required", "false") == "true"),
            penalty=int(d.get("penalty", "0") or 0),
            classes=[int(e.get("id")) for e in d.findall("class")],
        ))

    students = root.find("students") if root.find("students") is not None else None
    n_students = len(list(students)) if students is not None else 0

    return dict(name=name, nrDays=nr_days, nrWeeks=nr_weeks, slotsPerDay=slots_per_day,
                weights=weights, rooms=rooms, classes=classes, distributions=distributions,
                students=n_students)


def compute_derived(inst):
    classes = inst["classes"]
    by_id = {c["classId"]: c for c in classes}

    starts = sorted({t["start"] for c in classes for t in c["times"]})
    inst["starts"] = starts

    # travel maxima per ordered class pair, from candidate room sets (only where disjoint)
    travelMax = {c["classId"]: {} for c in classes}
    for d in inst["distributions"]:
        if d["type"].split("(")[0] != "SameAttendees":
            continue
        ids = [cid for cid in d["classes"] if by_id[cid]["hasRoom"]]
        for i in range(len(ids)):
            for j in range(i + 1, len(ids)):
                a, b = ids[i], ids[j]
                rooms_a = [r["id"] for r in by_id[a]["rooms"]]
                rooms_b = [r["id"] for r in by_id[b]["rooms"]]
                tv = max((inst["rooms"][ra]["travel"].get(rb, 0))
                         for ra in rooms_a for rb in rooms_b) if rooms_a and rooms_b else 0
                prev = travelMax[a].get(b, 0)
                if tv > prev:
                    travelMax[a][b] = travelMax[b][a] = tv

    for c in classes:
        c["travelMax"] = {pid: v for pid, v in travelMax[c["classId"]].items() if v > 0}

    # hard SameTime / SameDays / SameAttendees / NotOverlap partner lists
    sa_idx = 0
    no_idx = 0
    saPartners = {c["classId"]: set() for c in classes}
    noPartners = {c["classId"]: set() for c in classes}
    ddPartners = {c["classId"]: set() for c in classes}
    precedencePairs = []
    sameTimePartners = {c["classId"]: [] for c in classes}
    sameDaysPartners = {c["classId"]: [] for c in classes}
    clusterGids = {c["classId"]: [] for c in classes}
    for i, d in enumerate(inst["distributions"]):
        base = d["type"].split("(")[0]
        ids = d["classes"]
        if d["required"]:
            if base == "SameAttendees":
                sa_idx += 1
                # selective built-in cluster pruning: only small cliques get a
                # shared group (FC flood was caused by the giant constraints;
                # big cliques are still enforced exactly by the validator).
                if len(ids) <= CLUSTER_CAP:
                    for cid in ids:
                        clusterGids[cid].append(SG.CLUSTER_SA_BASE + i)
                for a in range(len(ids)):
                    for b in range(len(ids)):
                        if a != b:
                            saPartners[ids[a]].add(ids[b])
            elif base == "NotOverlap":
                no_idx += 1
                if len(ids) <= CLUSTER_CAP:
                    for cid in ids:
                        clusterGids[cid].append(SG.CLUSTER_NO_BASE + i)
                for a in range(len(ids)):
                    for b in range(len(ids)):
                        if a != b:
                            noPartners[ids[a]].add(ids[b])
            elif base == "SameTime":
                for a in range(len(ids)):
                    for b in range(len(ids)):
                        if a != b:
                            sameTimePartners[ids[a]].append(ids[b])
            elif base == "SameDays":
                for a in range(len(ids)):
                    for b in range(len(ids)):
                        if a != b:
                            sameDaysPartners[ids[a]].append(ids[b])
            elif base == "DifferentDays":
                for a in range(len(ids)):
                    for b in range(len(ids)):
                        if a != b:
                            ddPartners[ids[a]].add(ids[b])
            elif base == "Precedence":
                for a in range(len(ids) - 1):
                    precedencePairs.append((ids[a], ids[a + 1]))
    for c in classes:
        c["saPartners"] = sorted(saPartners[c["classId"]])
        c["noPartners"] = sorted(noPartners[c["classId"]])
        c["ddPartners"] = sorted(ddPartners[c["classId"]])
        c["sameTimePartners"] = sameTimePartners[c["classId"]]
        c["sameDaysPartners"] = sameDaysPartners[c["classId"]]
        c["clusterGids"] = sorted(set(clusterGids[c["classId"]]))
    inst["precedencePairs"] = precedencePairs
    meta_index = {c["classId"]: c["index"] for c in classes}
    inst["classIndexByClassId"] = meta_index


def build_dto(inst, room_aware=False, room_exact=False):
    classes = inst["classes"]
    starts = inst["starts"]
    base = SG.BASE_DATE
    begin = date(base[0], base[1], base[2])
    end = begin + timedelta(days=7 * inst["nrWeeks"] - 1)

    def dt(v):
        return "%04d-%02d-%02dT00:00:00" % (v.year, v.month, v.day)

    # classroom types: one per DISTINCT candidate-room list. The ITC room
    # domain already encodes suitability; per FAQ I1, class limit is NOT
    # compared with room capacity. A type's room set == the list.
    list_groups = {}
    for c in classes:
        if not c["hasRoom"]:
            c["roomTypeId"] = None
            continue
        key = tuple(sorted(r["id"] for r in c["rooms"]))
        if key not in list_groups:
            list_groups[key] = SG.CLASSROOM_TYPE_BASE + len(list_groups)
        c["roomTypeId"] = list_groups[key]
    classroom_types = {}
    for key, tid in list_groups.items():
        for rid in key:
            classroom_types.setdefault(rid, []).append(tid)
    classrooms = []

    gss = []
    for c in classes:
        cid = c["classId"]
        defined = [dict(SeriesNumber=i + 1, NumberOfLessons=1, SplitType=1,
                        StartInAnyWeek=True) for i in range(c["meetings"])]
        subject = dict(
            Id=cid, Name="cls#%d" % cid, NumberOfLessons=c["meetings"],
            DisciplineId=c["course"], DisciplineName="course#%d" % c["course"],
            SubjectTypeId=c["config"], SubjectTypeName="config#%d" % c["config"],
            SubjectTypeShortName="cfg", NoClassroom=not c["hasRoom"],
            ClassroomTypes=[] if not c["hasRoom"] else [
                dict(ClassroomTypeId=c["roomTypeId"],
                     ClassroomTypeName="rt#%d" % c["roomTypeId"], Priority=0)],
            DefinedSeries=defined)
        n_partners = (len(c["saPartners"]) + len(c["noPartners"])
                      + len(c["sameTimePartners"]) + len(c["sameDaysPartners"]))
        groups = [dict(Id=SG.OWN_GROUP_BASE + cid, Name="own#%d" % cid, SubgroupName=None,
                       SubgroupId=None, SubgroupVariantId=None, SubgroupVariantName=None,
                       SubgroupCount=0,
                       # metadata carriers for the slot-priority script (MRV):
                       # CourseYearId = candidate-room count (30 = roomless),
                       # EducationalProgramId = hard-partner count
                       CourseYearId=30 if not c["hasRoom"] else len(c["rooms"]),
                       CourseYearName="rooms#%d" % len(c["rooms"]),
                       EducationalProgramId=n_partners,
                       EducationalProgramName="partners#%d" % n_partners)]
        # selective hard-cluster groups (SameAttendees / NotOverlap cliques with
        # <= CLUSTER_CAP classes): the built-in same-group no-overlap check and
        # forward-check prune exact (date, pair) collisions early. Own group
        # stays first: the slot-priority script reads Groups.First().
        for gid in c["clusterGids"]:
            groups.append(dict(
                Id=gid, Name=("cluSA#" if gid < SG.CLUSTER_NO_BASE else "cluNO#") + str(gid),
                SubgroupName=None, SubgroupId=None,
                SubgroupVariantId=None, SubgroupVariantName=None,
                SubgroupCount=0, CourseYearId=0, CourseYearName="",
                EducationalProgramId=0, EducationalProgramName=""))
        gss.append(dict(
            Id=cid,
            Teacher=dict(Id=SG.TEACHER_BASE + cid, Name="T#cls%d" % cid, Position="itc"),
            Subject=subject, StudentCount=c["limit"], Groups=groups))

    classrooms = []
    for rid in sorted(inst["rooms"]):
        rt = inst["rooms"][rid]
        classrooms.append(dict(
            Id=rid, Name="room#%d" % rid, StudentCount=rt["capacity"],
            ClassroomTypes=[dict(ClassroomTypeId=t, ClassroomTypeName="t%d" % t, Priority=i)
                            for i, t in enumerate(sorted(classroom_types.get(rid, [])))]))

    cm = SG.build_candidate_arrays(inst)
    travels = SG.build_travel_arrays(inst, inst.get("pin_rooms"))
    partners = SG.build_partner_arrays(inst)
    cells = SG.build_cell_arrays(inst, cm)
    vpa = SG.build_value_partner_arrays(inst)
    inst["vp_off"], inst["vp_id"] = vpa["vp_off"], vpa["vp_id"]
    pairs = SG.build_pair_constraint_arrays(inst)

    uf = dict(
        ScheduleEstimations=[dict(Id=1, Name="itc-official-cost",
                                  Description="official ITC cost proxi (time + distribution soft penalties)",
                                  SelectorScript=None,
                                  MainScript=SG.gen_official_cost(inst, cm, travels, pairs,
                                                                  inst["weights"]["time"],
                                                                  inst["weights"]["distribution"],
                                                                  room_aware=room_aware,
                                                                  room_exact=room_exact))],
        ScheduleSlotEstimations=[dict(Id=2, Name="itc-time-guidance",
                                      Description="candidate time penalty guidance",
                                      SelectorScript=None,
                                      MainScript=SG.gen_value_guide(inst, cm, travels, cells))],
        UnitaryConstraints=[dict(Id=3, Name="itc-domain-shaping",
                                 Description="allowed (date,pair) per class occurrence patterns",
                                 SelectorScript=SG.UNITARY_SELECTOR,
                                 MainScript=SG.gen_unitary(inst, cm, travels))],
        SlotPriorities=[],
        SlotValidators=[
            dict(Id=4, Name="itc-hard", SelectorScript=None,
                 Description="hard SameAttendees/NotOverlap/SameTime/SameDays + intra-class pattern consistency (conservative, partner-based)",
                 MainScript=SG.gen_validator_hard(inst, cm, travels, partners, cells)),
        ],
    )

    dto = dict(FacultySeason=dict(Id=1, Name=inst["name"], BeginSeason=dt(begin),
                                  EndSeason=dt(end), MaxLessonsPerDay=len(starts),
                                  GroupSubjects=gss, Classrooms=classrooms),
               UserFunctions=uf)
    return dto, cm, travels, partners, pairs


def build_summary(inst, pairs):
    classes = inst["classes"]
    n_noroom = sum(1 for c in classes if not c["hasRoom"])
    n_time_pen = sum(len(c["times"]) for c in classes)
    type_counts = {}
    for d in inst["distributions"]:
        key = (d["type"].split("(")[0], d["required"])
        type_counts[key] = type_counts.get(key, 0) + 1
    unknown_soft = sorted({p[0] for p in pairs["soft"]
                          if p[0] in (9, 10, 20, 21, 22, 23)})
    return dict(
        instance=inst["name"], classes=len(classes), meetings_total=sum(c["meetings"] for c in classes),
        rooms=len(inst["rooms"]), roomless=n_noroom,
        time_candidates=n_time_pen, distinct_starts=inst["starts"],
        weights=inst["weights"], distributions_by=[["%s%s" % (t, "HARD" if r else "soft"), n] for (t, r), n in sorted(type_counts.items())],
        pairs=dict(soft=len(pairs["soft"]), hard=len(pairs["hard"])),
        unknown_soft_est_types=unknown_soft,
        students=inst["students"],
    )


def apply_seed_rooms(inst, seed_path):
    """Record a room/candidate hint from a feasible solution.

    The hint is baked into the generated room-aware objective so the initial
    seed is evaluated with its known feasible room assignment, while the GA
    remains free to change all candidate times and rooms.
    """
    sol = json.load(open(seed_path))
    inst["seed_rooms"] = {sc["classId"]: sc["room"]
                          for sc in sol["classes"] if sc.get("room") is not None}
    inst["seed_cands"] = {sc["classId"]: int(sc["candidate"])
                          for sc in sol["classes"] if sc.get("candidate") is not None}
    print(f"seed rooms: {len(inst['seed_rooms'])} classes from {seed_path}")


def apply_pin(inst, pin_path):
    """Pin solved classes to their chosen candidate times (incremental solving):
    replace each pinned class's candidate-time list with the single chosen time.
    Its meetings count, rooms and penalties are unchanged; the DFS effectively
    assigns it instantly (domain of size 1) and the constraint scripts enforce
    all relations against it."""
    sol = json.load(open(pin_path))
    inst["pin_rooms"] = {sc["classId"]: sc["room"] for sc in sol["classes"] if sc.get("room") is not None}
    by_id = {c["classId"]: c for c in inst["classes"]}
    n = 0
    for sc in sol["classes"]:
        if sc["classId"] not in by_id:
            continue
        c = by_id.get(sc["classId"])
        if c is None:
            continue
        t = c["times"][sc["candidate"]]
        c["times"] = [t]
        n += 1
    print(f"pinned {n} classes from {pin_path}")


def main():
    room_exact = "--room-aware-exact" in sys.argv
    room_aware = room_exact or "--room-aware" in sys.argv
    args = [a for a in sys.argv[1:]
            if a not in ("--pin", "--seed-rooms", "--room-aware", "--room-aware-exact")]
    # --seed-rooms takes a value; remove the value from positional args
    if "--seed-rooms" in sys.argv:
        i = sys.argv.index("--seed-rooms")
        if i + 1 < len(sys.argv) and sys.argv[i + 1] in args:
            args.remove(sys.argv[i + 1])
    pin_path = sys.argv[sys.argv.index("--pin") + 1] if "--pin" in sys.argv else None
    seed_rooms_path = (sys.argv[sys.argv.index("--seed-rooms") + 1]
                       if "--seed-rooms" in sys.argv else None)
    src = args[0]
    inst = parse_instance(src)
    compute_derived(inst)
    if pin_path:
        apply_pin(inst, pin_path)
    if seed_rooms_path:
        apply_seed_rooms(inst, seed_rooms_path)
    dto, cm, travels, partners, pairs = build_dto(
        inst, room_aware=room_aware, room_exact=room_exact)

    out_base = args[1] if len(args) > 1 and args[1] != "--pin" else "."
    outdir = os.path.join(out_base, "out", inst["name"])
    os.makedirs(outdir, exist_ok=True)
    if room_exact:
        dto_name = "api-scheduledata-roomaware-exact.json"
    elif room_aware:
        dto_name = "api-scheduledata-roomaware.json"
    else:
        dto_name = "api-scheduledata.json"

    with open(os.path.join(outdir, dto_name), "w") as f:
        json.dump(dto, f, separators=(",", ":"))

    meta = dict(
        instance=inst["name"], nrDays=inst["nrDays"], nrWeeks=inst["nrWeeks"],
        slotsPerDay=inst["slotsPerDay"], baseDate=SG.BASE_DATE, starts=inst["starts"],
        weights=inst["weights"], rooms=inst["rooms"],
        classes=[dict(classId=c["classId"], index=c["index"], course=c["course"],
                      config=c["config"], subpart=c["subpart"], limit=c["limit"],
                      parent=c["parent"], meetings=c["meetings"], times=c["times"],
                      rooms=c["rooms"], hasRoom=c["hasRoom"],
                      travelMax={str(k): v for k, v in c["travelMax"].items()},
                      saPartners=c["saPartners"], noPartners=c["noPartners"],
                      sameTimePartners=c["sameTimePartners"],
                      sameDaysPartners=c["sameDaysPartners"])
                 for c in inst["classes"]],
        distributions=inst["distributions"])
    with open(os.path.join(outdir, "meta.json"), "w") as f:
        json.dump(meta, f, indent=1)

    summary = build_summary(inst, pairs)
    with open(os.path.join(outdir, "summary.json"), "w") as f:
        json.dump(summary, f, indent=1)
    print(json.dumps(summary, indent=1))
    if room_aware:
        mode = "exact one-room-per-class" if room_exact else "per-meeting relaxation"
        print("room-aware objective: enabled (%s; generated estimator adds room penalty + collision penalty)" % mode)
    mb = os.path.getsize(os.path.join(outdir, dto_name)) / 1e6
    print("%s: %.1f MB" % (dto_name, mb))


if __name__ == "__main__":
    main()
