#!/usr/bin/env python3
"""Aggregate benchmark runs into a summary table (CSV + markdown).

Collects each run-*.json under out/<inst>/, pairs it with its -sol.json scorer
report when present, and prints one row per run.

Usage: python3 tools/aggregate.py out/tg-fal17
"""

import glob
import json
import os
import sys


def main():
    d = sys.argv[1] if len(sys.argv) > 1 else "."
    rows = []
    for rp in sorted(glob.glob(os.path.join(d, "run-*.json"))):
        if rp.endswith("-sol.json"):
            continue
        run = json.load(open(rp))
        res = run.get("results", [])
        best = max((r.get("totalEstimation", -10**18) for r in res), default=None)
        row = dict(
            run=os.path.basename(rp),
            algo=run.get("algorithm"),
            params=" ".join(f"{k}={v}" for k, v in (run.get("parameters") or {}).items()),
            n_results=len(res),
            best_est=best,
            run_seconds=round(run.get("runSeconds", 0), 1),
        )
        sp = rp.replace(".json", "-sol.json")
        if os.path.exists(sp):
            solrep = json.load(open(sp))
            row["feasible"] = solrep["feasible"]
            row["total"] = solrep["cost"]["total"]
            row["c_time"] = solrep["cost"]["time"]
            row["c_room"] = solrep["cost"]["room"]
            row["c_dist"] = solrep["cost"]["distribution"]
        rows.append(row)

    if not rows:
        print("no runs found in", d)
        return

    cols = ["run", "algo", "run_seconds", "n_results", "best_est", "feasible",
            "total", "c_time", "c_room", "c_dist", "params"]
    print("| " + " | ".join(cols) + " |")
    print("|" + "|".join(["---"] * len(cols)) + "|")
    for r in rows:
        print("| " + " | ".join(str(r.get(c, "")) for c in cols) + " |")

    with open(os.path.join(d, "runs-summary.csv"), "w") as f:
        f.write(",".join(cols) + "\n")
        for r in rows:
            f.write(",".join(str(r.get(c, "")) for c in cols) + "\n")
    print("wrote", os.path.join(d, "runs-summary.csv"))


if __name__ == "__main__":
    main()
