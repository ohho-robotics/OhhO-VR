#!/usr/bin/env python3
"""Validate the OHH-234 HIL checklist and print the plan.

This process never opens a port and never publishes a command. OHHO_HIL is
ignored. Exits 0 when the kit files are intact.
"""

from __future__ import annotations

import ast
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SESSION = ROOT / "docs" / "hil-vr-session.md"
TEMPLATE = ROOT / "docs" / "hil-vr-results.template.md"

STAGES = (
    "Stage 1 - Blocks",
    "Stage 2 - Deadman",
    "Stage 3 - E-stop",
    "Stage 4 - Link-loss",
    "Stage 5 - HMD remove",
    "Stage 6 - Floor drive",
    "Stage 7 - Arm",
)

ORDER_LINE = (
    "blocks -> deadman -> e-stop -> link-loss -> HMD remove -> floor drive -> arm"
)

GATES = (
    "| OHH-220 | In Review before the session | blocks through arm |",
    "| OHH-202 | In Review before the session | blocks through arm |",
    "| OHH-193 | In Review before deadman | deadman through arm |",
    "| OHH-221 | In Review before arm | arm |",
)

SESSION_IDS = (
    "ohh234-blocks",
    "ohh234-deadman",
    "ohh234-estop",
    "ohh234-link-loss",
    "ohh234-hmd-remove",
    "ohh234-floor-drive",
    "ohh234-arm",
)

# (first column, budget cell) pairs that must appear in the safety table.
SAFETY_BUDGETS = (
    ("blocks", "0"),
    ("deadman", "same tick"),
    ("estop", "same tick"),
    ("estop", "0"),
    ("link-loss", "350 ms"),
    ("link-loss", "0"),
    ("hmd-remove", "350 ms"),
    ("hmd-remove", "0"),
    ("floor-drive", "same tick"),
    ("floor-drive", "200 ms"),
    ("arm", "0"),
    ("arm", "same tick"),
)

LATENCY_BUDGETS = (
    ("input_to_command", "40"),
    ("command_to_motion", "200"),
    ("glass_to_glass", "180"),
    ("telemetry_age", "80"),
    ("heartbeat_age", "80"),
)

REFUSE_FLAGS = ("--hardware", "--run", "--hil")


class KitError(Exception):
    pass


def _cells(line: str) -> list[str]:
    return [part.strip() for part in line.strip().strip("|").split("|")]


def _is_row(line: str) -> bool:
    stripped = line.strip()
    return stripped.startswith("|") and stripped.endswith("|")


def _is_separator(line: str) -> bool:
    if not _is_row(line):
        return False
    cells = _cells(line)
    return bool(cells) and all(cell.replace(":", "").replace("-", "") == "" for cell in cells)


def markdown_tables(text: str) -> list[tuple[list[str], list[list[str]]]]:
    lines = text.splitlines()
    tables: list[tuple[list[str], list[list[str]]]] = []
    index = 0
    while index < len(lines):
        if not _is_row(lines[index]) or index + 1 >= len(lines) or not _is_separator(lines[index + 1]):
            index += 1
            continue
        header = _cells(lines[index])
        index += 2
        rows: list[list[str]] = []
        while index < len(lines) and _is_row(lines[index]) and not _is_separator(lines[index]):
            rows.append(_cells(lines[index]))
            index += 1
        tables.append((header, rows))
    return tables


def _column(header: list[str], name: str) -> int:
    for index, cell in enumerate(header):
        if cell.lower() == name.lower():
            return index
    raise KitError("table is missing a %s column" % name)


def _require(text: str, needle: str, label: str) -> None:
    if needle not in text:
        raise KitError("%s is missing %r" % (label, needle))


def _stage_sections(text: str) -> list[str]:
    positions = []
    for heading in STAGES:
        marker = "## " + heading
        at = text.find(marker)
        if at < 0:
            raise KitError("checklist is missing heading %r" % marker)
        positions.append(at)
    if positions != sorted(positions):
        raise KitError("stage headings are out of order")
    sections = []
    for index, start in enumerate(positions):
        end = positions[index + 1] if index + 1 < len(positions) else len(text)
        sections.append(text[start:end])
    return sections


def _check_measured_tables(text: str, label: str) -> None:
    found = False
    for header, rows in markdown_tables(text):
        if not any(cell.lower() == "verdict" for cell in header):
            continue
        found = True
        verdict = _column(header, "Verdict")
        measured = [i for i, cell in enumerate(header) if "measured" in cell.lower()]
        if not measured:
            raise KitError("%s verdict table has no Measured column" % label)
        for row in rows:
            if len(row) != len(header):
                raise KitError("%s has a row with %d cells, header has %d" % (label, len(row), len(header)))
            if row[verdict] != "not_measured":
                raise KitError("%s verdict must stay not_measured, found %r" % (label, row[verdict]))
            for index in measured:
                if row[index] != "":
                    raise KitError("%s measured cell must stay empty, found %r" % (label, row[index]))
    if not found:
        raise KitError("%s has no verdict table" % label)


def _check_template(text: str) -> None:
    _check_measured_tables(text, "template")
    tables = markdown_tables(text)
    safety = None
    latency = None
    for header, rows in tables:
        if header and header[0] == "Stage":
            safety = rows
        if header and header[0] == "Profile":
            latency = rows
    if safety is None:
        raise KitError("template is missing the safety table")
    if latency is None:
        raise KitError("template is missing the latency table")
    safety_pairs = {(row[0], row[2]) for row in safety}
    for stage, budget in SAFETY_BUDGETS:
        if (stage, budget) not in safety_pairs:
            raise KitError("template safety table is missing %s / %s" % (stage, budget))
    latency_pairs = {(row[1], row[2]) for row in latency}
    for segment, budget in LATENCY_BUDGETS:
        key = ("`%s`" % segment, budget)
        if key not in latency_pairs:
            raise KitError("template latency table is missing %s / %s" % (segment, budget))
        profile = next(row[0] for row in latency if row[1] == "`%s`" % segment)
        if profile != "Real":
            raise KitError("latency row %s is profile %r" % (segment, profile))


def _check_script_imports() -> None:
    source = Path(__file__).read_text(encoding="utf-8")
    tree = ast.parse(source)
    banned = {"subprocess", "socket", "serial", "can"}
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            names = [alias.name.split(".")[0] for alias in node.names]
        elif isinstance(node, ast.ImportFrom):
            names = [(node.module or "").split(".")[0]]
        else:
            continue
        for name in names:
            if name in banned:
                raise KitError("dry-run imports %s" % name)
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        func = node.func
        if isinstance(func, ast.Attribute) and func.attr in ("system", "popen", "spawnl", "execv"):
            raise KitError("dry-run calls %s" % func.attr)


def validate() -> list[str]:
    _check_script_imports()
    if not SESSION.is_file():
        raise KitError("missing %s" % SESSION.relative_to(ROOT))
    if not TEMPLATE.is_file():
        raise KitError("missing %s" % TEMPLATE.relative_to(ROOT))
    session = SESSION.read_text(encoding="utf-8")
    template = TEMPLATE.read_text(encoding="utf-8")
    _require(session, ORDER_LINE, "checklist")
    _require(session, "second person", "checklist")
    _require(session, "hardware e-stop", "checklist")
    _require(session, "hil-vr-results.template.md", "checklist")
    _require(session, "~/.ohho/vr_sessions", "checklist")
    _require(session, "evidence/ohh-234", "checklist")
    _require(session, "part-0000", "checklist")
    _require(session, "auto_session:=false", "checklist")
    _require(session, "0.2 m/s", "checklist")
    _require(session, "Parallel coding jobs were told to treat OHH-193 as skipped.", "checklist")
    for gate in GATES:
        _require(session, gate, "checklist")
    for session_id in SESSION_IDS:
        _require(session, session_id, "checklist")
    would: list[str] = []
    for section in _stage_sections(session):
        lines = [line[2:].strip() for line in section.splitlines() if line.startswith("- WOULD:")]
        if not lines:
            raise KitError("a stage has no WOULD line")
        if not any("hardware e-stop" in line for line in lines):
            raise KitError("a stage WOULD list omits the hardware e-stop")
        would.extend(lines)
    _check_template(template)
    return would


def main(argv: list[str]) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    if any(flag in argv for flag in REFUSE_FLAGS):
        print("OHH-234 dry run refuses --hardware, --run, and --hil.")
        print("This script has no mode that contacts a robot.")
        return 2
    if argv and argv != ["--help"]:
        print("usage: python scripts/hil_vr_session_dry_run.py")
        return 2
    if argv == ["--help"]:
        print("Validate docs/hil-vr-session.md and docs/hil-vr-results.template.md.")
        print("Prints the WOULD lines. Does not open a port. Exits 0 when the kit is intact.")
        return 0
    try:
        would = validate()
    except KitError as exc:
        print("OHH-234 dry run failed: %s" % exc)
        return 1
    hil = os.environ.get("OHHO_HIL", "")
    print("OHH-234 dry run")
    print("hardware: not contacted")
    print("ports: none opened")
    if hil:
        print("OHHO_HIL=%s is set and ignored. This script does not use it." % hil)
    else:
        print("OHHO_HIL: unset, and not required")
    print("gates a person confirms in Linear:")
    print("  OHH-220 In Review before the session")
    print("  OHH-202 In Review before the session")
    print("  OHH-193 In Review before deadman")
    print("  OHH-221 In Review before arm")
    print("WOULD run, by a person, not by this process:")
    for line in would:
        print("  " + line)
    print("template: docs/hil-vr-results.template.md (measured cells empty)")
    print("evidence layout: evidence/ohh-234/<YYYYMMDD>/")
    print("dry run exit 0")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
