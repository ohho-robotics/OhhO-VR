"""Print the latency results table from a harness JSON log.

A log with hardware_run false is labelled as not a field measurement.
This script does not connect to a robot.
"""

import json
import sys
from pathlib import Path

SEGMENTS = (
    "input_to_command",
    "command_to_motion",
    "glass_to_glass",
    "telemetry_age",
    "heartbeat_age",
)


def summarize(report):
    lines = []
    if not report.get("hardware_run"):
        lines.append("This log is not a field measurement (hardware_run is false).")
        lines.append("")
    lines.append("| Segment | Budget p95 (ms) | n | incomplete | p50 | p95 | Verdict |")
    lines.append("|---|---:|---:|---:|---:|---:|---|")
    rows = {}
    for row in report.get("summary") or []:
        rows[row.get("segment")] = row
    for segment in SEGMENTS:
        row = rows.get(segment)
        if row is None:
            lines.append("| {0} |  | 0 | 0 |  |  | not_measured |".format(segment))
            continue
        lines.append(
            "| {segment} | {budget} | {n} | {inc} | {p50} | {p95} | {verdict} |".format(
                segment=segment,
                budget=_cell(row.get("budget_p95_ms")),
                n=row.get("n", 0),
                inc=row.get("incomplete", 0),
                p50=_cell(row.get("p50_ms")),
                p95=_cell(row.get("p95_ms")),
                verdict=row.get("verdict") or "not_measured",
            )
        )
    return "\n".join(lines) + "\n"


def _cell(value):
    if value is None:
        return ""
    return str(value)


def main(argv):
    if len(argv) != 2:
        print("usage: summarize_latency_log.py <latency.json>", file=sys.stderr)
        return 2
    report = json.loads(Path(argv[1]).read_text(encoding="utf-8"))
    sys.stdout.write(summarize(report))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
