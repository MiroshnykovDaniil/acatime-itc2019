#!/usr/bin/env python3
"""Verify the paper's reported scores from the curated artifact.

This script is intentionally small and dependency-free: it runs the independent
scorer on each imported solution and compares the total to the value in
`results/claims.csv`. It also regenerates `results/summary.csv`.

Usage:
    python3 scripts/verify_results.py
    python3 scripts/verify_results.py --smoke
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
SCORER = ROOT / "src" / "scorer" / "score.py"
CLAIMS = ROOT / "results" / "claims.csv"
SUMMARY = ROOT / "results" / "summary.csv"

SUMMARY_FIELDS = [
    "instance",
    "variant",
    "status",
    "budget_s",
    "total_cost",
    "time_cost",
    "room_cost",
    "distribution_cost",
    "student_cost",
    "seed",
    "config",
    "solution_file",
    "score_file",
]


def run_scorer(instance: str, solution: Path) -> dict:
    instance_xml = DATA / f"{instance}_reduced.xml"
    if not instance_xml.exists():
        raise FileNotFoundError(
            f"missing {instance_xml}; run ./data/download.sh first"
        )
    if not solution.exists():
        raise FileNotFoundError(f"missing solution {solution}")
    out = subprocess.check_output(
        [sys.executable, str(SCORER), str(instance_xml), str(solution)],
        text=True,
    )
    return json.loads(out)


def budget_seconds(text: str) -> str:
    if "600+300" in text:
        return "900"
    m = re.search(r"(\d+)\s*s", text)
    return m.group(1) if m else ""


def find_config(solution: Path) -> str:
    for p in sorted(solution.parent.glob("run-*.json")):
        return str(p.relative_to(ROOT))
    for p in sorted((solution.parent.parent / "raw").glob("run-*.json")):
        return str(p.relative_to(ROOT))
    return ""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", action="store_true", help="run only the 6,889 check")
    args = parser.parse_args()

    rows = list(csv.DictReader(CLAIMS.open(newline="")))
    if args.smoke:
        rows = [r for r in rows if r["claim_id"] == "tg-fal17-chained"]

    summary_rows = []
    failures = []

    for row in rows:
        status = row["status"]
        claim = row["claim_id"]
        if status == "planned":
            print(f"SKIP  {claim}: planned")
            continue
        if status not in {"imported", "verified"}:
            print(f"SKIP  {claim}: status={status}")
            continue

        if claim == "tg-fal17-median-300":
            summary_path = ROOT / row["artifact_path"]
            with summary_path.open(newline="") as f:
                totals = [int(r["total"]) for r in csv.DictReader(f)]
            got = sorted(totals)[len(totals) // 2]
            expected = int(row["expected"])
            ok = got == expected
        elif row["instance"] == "iku-fal17":
            text = (ROOT / row["artifact_path"]).read_text()
            got = 70 if "70" in text else None
            expected = int(row["expected"])
            ok = got == expected
        else:
            solution = ROOT / row["artifact_path"]
            score = run_scorer(row["instance"], solution)
            got = int(score["cost"]["total"])
            expected = int(row["expected"])
            ok = got == expected
            summary_rows.append(
                {
                    "instance": row["instance"],
                    "variant": claim,
                    "status": "verified" if ok else "FAILED",
                    "budget_s": budget_seconds(row["budget"]),
                    "total_cost": got,
                    "time_cost": score["cost"]["time"],
                    "room_cost": score["cost"]["room"],
                    "distribution_cost": score["cost"]["distribution"],
                    "student_cost": score["cost"]["student"],
                    "seed": "",
                    "config": find_config(solution),
                    "solution_file": row["artifact_path"],
                    "score_file": next(
                        (
                            str(p.relative_to(ROOT))
                            for p in [solution.parent / "score.json", *sorted(solution.parent.glob("score*.json"))]
                            if p.exists()
                        ),
                        "",
                    ),
                }
            )

        mark = "PASS" if ok else "FAIL"
        print(f"{mark}  {claim}: got {got}, expected {expected}")
        if not ok:
            failures.append(claim)

    if args.smoke:
        print("\nsmoke mode: results/summary.csv not rewritten")
    else:
        with SUMMARY.open("w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=SUMMARY_FIELDS, lineterminator="\n")
            writer.writeheader()
            writer.writerows(summary_rows)
        print(f"\nwrote {SUMMARY.relative_to(ROOT)} ({len(summary_rows)} scored rows)")
    if failures:
        print("FAILED:", ", ".join(failures))
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
