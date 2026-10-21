#!/usr/bin/env python3
"""Turn filled beta feedback files into a P1–P3 triage list.

Accepts either the key/value template in docs/beta/feedback-form.md or the
markdown GitHub writes for .github/ISSUE_TEMPLATE/beta-feedback.yml
(headings like "### Tester id").

Priority (first match wins; every matching reason is kept):

P1
  crash_count >= 1
  crashes is set and is not none / no / n/a
  completed_first_drive is no
  time_to_first_drive_source is not-reached
  comfort is quit

P2
  comfort is mild
  at least one confusion point
  time_to_first_drive_s is greater than 180

P3
  everything else that parsed

A blank time with a completed drive stays P3 and is noted as a missing metric.
This script does not read session-notes templates.

Usage:
  python tools/beta_triage.py tools/testdata/beta_feedback
  python tools/beta_triage.py path/to/one-feedback.md
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass, field
from pathlib import Path

SLOW_DRIVE_S = 180.0
NONE_CRASHES = {"none", "no", "n/a", "na", ""}
# Tokens that mean "nothing to report" on crashes and confusion. They are real
# answers, not missing fields. tester_id and comfort do not use this set.
BLANK_TOKENS = {"", "_no response_", "n/a", "na", "none", "no"}

HEADING_TO_KEY = {
    "tester id": "tester_id",
    "date": "date",
    "build": "build",
    "sim": "sim",
    "completed first drive": "completed_first_drive",
    "time to first drive (seconds)": "time_to_first_drive_s",
    "time source": "time_to_first_drive_source",
    "crashes": "crashes",
    "crash count": "crash_count",
    "comfort": "comfort",
    "comfort notes": "comfort_notes",
    "confusion points": "confusion",
    "notes": "notes",
}

REQUIRED = ("tester_id", "completed_first_drive", "crashes", "crash_count", "comfort")


class FeedbackParseError(ValueError):
    def __init__(self, source: str, message: str):
        super().__init__(f"{source}: {message}")
        self.source = source
        self.message = message


@dataclass
class Session:
    source: str
    tester_id: str
    completed_first_drive: bool
    crashes: str
    crash_count: int
    comfort: str
    date: str = ""
    build: str = ""
    sim: str = "unknown"
    time_s: float | None = None
    time_source: str = ""
    comfort_notes: str = ""
    confusion: list[str] = field(default_factory=list)
    notes: str = ""
    missing_time: bool = False


@dataclass
class Item:
    session: Session
    priority: str
    reasons: list[str]


def parse_feedback(text: str, source: str) -> Session:
    raw = text.replace("\r\n", "\n").replace("\r", "\n")
    if "\n### " in "\n" + raw or raw.startswith("### "):
        fields = _parse_headings(raw, source)
    else:
        fields = _parse_keys(raw, source)
    return _session_from_fields(fields, source)


def classify(session: Session) -> tuple[str, list[str]]:
    reasons: list[str] = []
    if session.crash_count > 0:
        reasons.append(f"crash_count={session.crash_count}")
    if session.crashes.strip().lower() not in NONE_CRASHES:
        reasons.append(f"crashes={session.crashes.strip()}")
    if not session.completed_first_drive:
        reasons.append("did not complete first drive")
    if session.time_source == "not-reached":
        reasons.append("time_to_first_drive_source=not-reached")
    if session.comfort == "quit":
        reasons.append("comfort=quit")
    if reasons:
        return "P1", reasons

    if session.comfort == "mild":
        reasons.append("comfort=mild")
    if session.confusion:
        reasons.append(f"confusion={len(session.confusion)}")
    if session.time_s is not None and session.time_s > SLOW_DRIVE_S:
        reasons.append(f"time_to_first_drive_s={_num(session.time_s)} > {int(SLOW_DRIVE_S)}")
    if reasons:
        return "P2", reasons

    reasons = ["completed first drive without crash, comfort, or confusion flags"]
    if session.missing_time:
        reasons.append("time_to_first_drive_s missing")
    return "P3", reasons


def triage_paths(paths: list[Path]) -> tuple[list[Item], list[FeedbackParseError]]:
    files = _expand(paths)
    items: list[Item] = []
    errors: list[FeedbackParseError] = []
    for path in files:
        try:
            text = path.read_text(encoding="utf-8")
            session = parse_feedback(text, str(path))
        except FeedbackParseError as exc:
            errors.append(exc)
            continue
        except OSError as exc:
            errors.append(FeedbackParseError(str(path), str(exc)))
            continue
        priority, reasons = classify(session)
        items.append(Item(session, priority, reasons))
    items.sort(key=lambda item: (item.priority, item.session.tester_id.lower(), item.session.source))
    return items, errors


def render(items: list[Item], errors: list[FeedbackParseError]) -> str:
    lines = [
        "# Beta triage",
        f"files: {len(items) + len(errors)}",
        f"parsed: {len(items)}",
        f"errors: {len(errors)}",
        "",
    ]
    for priority in ("P1", "P2", "P3"):
        group = [item for item in items if item.priority == priority]
        lines.append(f"## {priority} ({len(group)})")
        if not group:
            lines.append("- none")
        for item in group:
            session = item.session
            why = "; ".join(item.reasons)
            lines.append(
                f"- {session.tester_id} ({_short(session.source)}): {why}"
                f" [sim={session.sim or 'unknown'} build={session.build or 'unknown'}]"
            )
        lines.append("")
    lines.append("## Parse errors")
    if not errors:
        lines.append("- none")
    else:
        for err in errors:
            lines.append(f"- {err.source}: {err.message}")
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Summarise OhhO Quest beta feedback into P1-P3.")
    parser.add_argument("paths", nargs="+", help="Feedback markdown files or a directory of them")
    parser.add_argument("--out", help="Also write the summary to this path")
    args = parser.parse_args(argv)
    items, errors = triage_paths([Path(p) for p in args.paths])
    text = render(items, errors)
    sys.stdout.write(text)
    if args.out:
        Path(args.out).write_text(text, encoding="utf-8")
    return 1 if errors else 0


def _expand(paths: list[Path]) -> list[Path]:
    files: list[Path] = []
    for path in paths:
        if path.is_dir():
            files.extend(sorted(p for p in path.glob("*.md") if p.is_file()))
        else:
            files.append(path)
    return files


def _parse_keys(text: str, source: str) -> dict[str, object]:
    fields: dict[str, object] = {}
    lines = text.split("\n")
    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            i += 1
            continue
        if ":" not in stripped:
            raise FeedbackParseError(source, f"expected key: value, got {stripped!r}")
        key, _, value = stripped.partition(":")
        key = key.strip()
        value = value.strip()
        if key == "notes":
            rest = []
            if value:
                rest.append(value)
            rest.extend(lines[i + 1 :])
            fields["notes"] = "\n".join(rest).strip()
            break
        if key == "confusion" and value == "":
            points: list[str] = []
            i += 1
            while i < len(lines):
                item = lines[i].strip()
                if not item:
                    i += 1
                    continue
                if item.startswith("#"):
                    i += 1
                    continue
                if not item.startswith("- "):
                    break
                points.append(item[2:].strip())
                i += 1
            fields["confusion"] = points
            continue
        if key == "confusion":
            fields["confusion"] = [] if _is_blank_token(value) else [value]
            i += 1
            continue
        fields[key] = value
        i += 1
    return fields


def _parse_headings(text: str, source: str) -> dict[str, object]:
    sections: list[tuple[str, str]] = []
    current_key: str | None = None
    buf: list[str] = []
    for line in text.split("\n"):
        if line.startswith("### "):
            if current_key is not None:
                sections.append((current_key, "\n".join(buf).strip()))
            title = line[4:].strip().lower()
            current_key = HEADING_TO_KEY.get(title)
            if current_key is None:
                raise FeedbackParseError(source, f"unknown heading {line[4:].strip()!r}")
            buf = []
        elif current_key is not None:
            buf.append(line)
    if current_key is not None:
        sections.append((current_key, "\n".join(buf).strip()))
    fields: dict[str, object] = {}
    for key, body in sections:
        if key == "confusion":
            points = []
            for line in body.split("\n"):
                item = line.strip()
                if _is_blank_token(item):
                    continue
                if item.startswith("- "):
                    item = item[2:].strip()
                points.append(item)
            fields[key] = points
        elif key == "notes":
            fields[key] = "" if _is_empty(body) else body
        else:
            one = body.split("\n", 1)[0].strip()
            fields[key] = "" if _is_empty(one) else one
    return fields


def _session_from_fields(fields: dict[str, object], source: str) -> Session:
    missing = []
    for key in REQUIRED:
        if key == "crashes":
            raw = fields.get("crashes", None)
            if raw is None or str(raw).strip() == "" or str(raw).strip().lower() == "_no response_":
                missing.append("crashes")
            continue
        if key not in fields or _is_empty(fields.get(key)):
            missing.append(key)
    if missing:
        raise FeedbackParseError(source, "missing " + ", ".join(missing))

    completed = _yes_no(fields.get("completed_first_drive"), source, "completed_first_drive")
    comfort = str(fields.get("comfort", "")).strip().lower()
    if comfort not in {"fine", "mild", "quit"}:
        raise FeedbackParseError(source, f"comfort must be fine, mild, or quit (got {comfort!r})")
    try:
        crash_count = int(str(fields.get("crash_count")).strip())
    except (TypeError, ValueError) as exc:
        raise FeedbackParseError(source, "crash_count must be an integer") from exc
    if crash_count < 0:
        raise FeedbackParseError(source, "crash_count must be >= 0")

    time_raw = fields.get("time_to_first_drive_s", "")
    time_s: float | None = None
    missing_time = _is_empty(time_raw)
    if not missing_time:
        try:
            time_s = float(str(time_raw).strip())
        except ValueError as exc:
            raise FeedbackParseError(source, "time_to_first_drive_s must be a number") from exc
        if time_s < 0:
            raise FeedbackParseError(source, "time_to_first_drive_s must be >= 0")

    time_source = str(fields.get("time_to_first_drive_source", "")).strip().lower()
    if time_source and time_source not in {"headset-log", "self-timed", "not-reached"}:
        raise FeedbackParseError(
            source, "time_to_first_drive_source must be headset-log, self-timed, or not-reached"
        )
    sim = str(fields.get("sim", "")).strip().lower() or "unknown"
    if sim not in {"local", "cloud", "unknown"}:
        raise FeedbackParseError(source, "sim must be local, cloud, or unknown")

    confusion = fields.get("confusion", [])
    if isinstance(confusion, str):
        confusion = [] if _is_empty(confusion) else [confusion]
    if not isinstance(confusion, list):
        raise FeedbackParseError(source, "confusion must be a list")

    crashes = str(fields.get("crashes", "")).strip()
    return Session(
        source=source,
        tester_id=str(fields["tester_id"]).strip(),
        completed_first_drive=completed,
        crashes=crashes,
        crash_count=crash_count,
        comfort=comfort,
        date=str(fields.get("date", "")).strip(),
        build=str(fields.get("build", "")).strip(),
        sim=sim,
        time_s=time_s,
        time_source=time_source,
        comfort_notes=str(fields.get("comfort_notes", "")).strip(),
        confusion=[str(item).strip() for item in confusion if str(item).strip()],
        notes=str(fields.get("notes", "")).strip(),
        missing_time=missing_time,
    )


def _yes_no(value: object, source: str, key: str) -> bool:
    text = str(value).strip().lower()
    if text in {"yes", "true", "y"}:
        return True
    if text in {"no", "false", "n"}:
        return False
    raise FeedbackParseError(source, f"{key} must be yes or no")


def _is_empty(value: object) -> bool:
    """True when a required answer was left blank. The word none is not blank."""
    if value is None:
        return True
    if isinstance(value, list):
        return len(value) == 0
    return str(value).strip().lower() in {"", "_no response_"}


def _is_blank_token(value: object) -> bool:
    return str(value).strip().lower() in BLANK_TOKENS


def _num(value: float) -> str:
    text = f"{value:.3f}".rstrip("0").rstrip(".")
    return text


def _short(source: str) -> str:
    return Path(source).name


if __name__ == "__main__":
    sys.exit(main())
