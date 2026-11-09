#!/usr/bin/env python3
"""Convert an ITC solution JSON (tools/score.py or LNS output) into a runner
seed JSON suitable for AcaTime's SeedSchedule parameter.

Inverse of tools/post.py's candidate pinning: for every class the chosen
candidate carries days/weeks/start; each (week, day) bit becomes one slot on
the corresponding season date, with the pair index of the candidate start.

Usage:
  python3 tools/run_from_solution.py <meta.json> <solution.json> <out-run.json>
"""

import datetime
import json
import sys


def main():
    meta_path, sol_path, out_path = sys.argv[1:4]
    meta = json.load(open(meta_path))
    sol = json.load(open(sol_path))
    classes = {c["classId"]: c for c in meta["classes"]}
    starts = meta["starts"]
    spair = {s: i + 1 for i, s in enumerate(starts)}
    base = datetime.date(*meta["baseDate"])

    slots = []
    missing = 0
    for sc in sol["classes"]:
        cid = sc["classId"]
        c = classes.get(cid)
        if c is None:
            continue
        k = int(sc["candidate"])
        if k < 0 or k >= len(c["times"]):
            missing += 1
            continue
        t = c["times"][k]
        pair = spair[t["start"]]
        room = sc.get("room")
        lesson = 0
        for wi, chw in enumerate(t["weeks"]):
            if chw != "1":
                continue
            for di, chd in enumerate(t["days"]):
                if chd != "1":
                    continue
                d = base + datetime.timedelta(days=7 * wi + di)
                lesson += 1
                # Optional room hint: consumed by ApplySeedSchedule. Old run
                # files without this field remain valid.
                slots.append(dict(classId=cid, lessonNumber=lesson,
                                  date=d.isoformat(), pair=pair, room=room))
        if lesson != c["meetings"]:
            missing += 1

    run = dict(algorithm="seed", results=[dict(name="seed",
                                               totalEstimation=0,
                                               error=None,
                                               slots=slots)])
    with open(out_path, "w") as f:
        json.dump(run, f, separators=(",", ":"))
    print("wrote %s (%d slots, %d classes; %d classes not convertible)"
          % (out_path, len(slots), len(sol["classes"]), missing))


if __name__ == "__main__":
    main()
