#!/usr/bin/env python3
"""Fail when a GameCI edit-mode run did not execute any tests.

GameCI writes NUnit XML at <artifactsPath>/editmode-results.xml
(unity-tests.yml sets artifactsPath to test-results). The job must not
stay green when that file is missing or reports zero executed cases.
"""
from __future__ import annotations

import os
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def local_name(tag: str) -> str:
    if tag.startswith("{"):
        return tag.split("}", 1)[1]
    return tag


def find_results(root: Path) -> Path | None:
    preferred = sorted(root.rglob("editmode-results.xml"))
    if preferred:
        return preferred[0]
    candidates = []
    for path in root.rglob("*.xml"):
        name = path.name.lower()
        if "coverage" in name:
            continue
        if name.endswith("-results.xml") or name == "testresults.xml":
            candidates.append(path)
    return sorted(candidates)[0] if candidates else None


def int_attr(element: ET.Element, name: str) -> int | None:
    raw = element.attrib.get(name)
    if raw is None or raw == "":
        return None
    try:
        return int(raw)
    except ValueError:
        return None


def results_element(root: ET.Element) -> ET.Element:
    if local_name(root.tag) in ("test-run", "test-results"):
        return root
    for child in root.iter():
        if local_name(child.tag) in ("test-run", "test-results"):
            return child
    return root


def summarize(path: Path) -> dict:
    element = results_element(ET.parse(path).getroot())
    total = int_attr(element, "total")
    if total is None:
        total = int_attr(element, "testcasecount")
    passed = int_attr(element, "passed")
    failed = int_attr(element, "failed")
    skipped = int_attr(element, "skipped")
    inconclusive = int_attr(element, "inconclusive")
    has_split = any(value is not None for value in (passed, failed, inconclusive))
    cases = [node for node in element.iter() if local_name(node.tag) == "test-case"]
    if not has_split and cases:
        passed = failed = skipped = inconclusive = 0
        for case in cases:
            result = case.attrib.get("result", "").lower()
            if result in ("skipped", "ignored"):
                skipped += 1
            elif result == "failed":
                failed += 1
            elif result == "inconclusive":
                inconclusive += 1
            else:
                passed += 1
        has_split = True
    passed = passed or 0
    failed = failed or 0
    skipped = skipped or 0
    inconclusive = inconclusive or 0
    if total is None:
        total = passed + failed + skipped + inconclusive if has_split else len(cases)
    ran = passed + failed + inconclusive
    return {
        "file": str(path),
        "result": element.attrib.get("result", ""),
        "total": total,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "inconclusive": inconclusive,
        "ran": ran,
    }


def write_summary(lines: list[str]) -> None:
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary:
        return
    with open(summary, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")


def main(argv: list[str]) -> int:
    directory = Path(argv[1] if len(argv) > 1 else "test-results")
    if not directory.is_dir():
        message = f"No test results directory at {directory}; 0 tests executed."
        print(f"::error::{message}")
        write_summary(["### Edit-mode tests", "", message])
        return 1

    xml_path = find_results(directory)
    if xml_path is None:
        message = f"No NUnit results XML under {directory}; 0 tests executed."
        print(f"::error::{message}")
        write_summary(["### Edit-mode tests", "", message])
        return 1

    info = summarize(xml_path)
    print(
        "editmode results: "
        f"total={info['total']} passed={info['passed']} failed={info['failed']} "
        f"skipped={info['skipped']} inconclusive={info['inconclusive']} "
        f"executed={info['ran']} result={info['result'] or 'unknown'} file={info['file']}"
    )
    write_summary(
        [
            "### Edit-mode tests",
            "",
            f"- file: `{info['file']}`",
            f"- result: {info['result'] or 'unknown'}",
            f"- total: {info['total']}",
            f"- passed: {info['passed']}",
            f"- failed: {info['failed']}",
            f"- skipped: {info['skipped']}",
            f"- inconclusive: {info['inconclusive']}",
            f"- executed (passed + failed + inconclusive): {info['ran']}",
        ]
    )
    if info["total"] <= 0 or info["ran"] <= 0:
        print("::error::Edit-mode run executed 0 tests. Refusing a green job with an empty suite.")
        return 1
    print(f"::notice::Edit-mode tests executed: {info['ran']} (passed={info['passed']} of total={info['total']})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
