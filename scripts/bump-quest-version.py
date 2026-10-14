#!/usr/bin/env python3
"""Bump the Quest bundle version and Android version code.

Edits ProjectSettings/ProjectSettings.asset only:

  bundleVersion
  AndroidBundleVersionCode

The script does not commit, tag, push, or contact the network. Other version
fields (for example tvOSBundleVersion) are left unchanged.

The release workflow reads these two fields and passes them to GameCI. The git
tag must be v<bundleVersion> with the exact string stored here.
"""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PROJECT = ROOT / "ProjectSettings" / "ProjectSettings.asset"
MAX_VERSION_CODE = 2147483647


class VersionError(Exception):
    """The ProjectSettings file or the requested bump is not usable."""


@dataclass(frozen=True)
class QuestVersion:
    bundle_version: str
    bundle_code: int


def line_body(line: str) -> str:
    return line.rstrip("\r\n")


def line_ending(line: str) -> str:
    if line.endswith("\r\n"):
        return "\r\n"
    if line.endswith("\n"):
        return "\n"
    if line.endswith("\r"):
        return "\r"
    return ""


def _key_prefix(key: str) -> str:
    return f"  {key}: "


def read_key(lines: list[str], key: str) -> str:
    prefix = _key_prefix(key)
    found = [line_body(line) for line in lines if line_body(line).startswith(prefix)]
    if len(found) != 1:
        raise VersionError(f"expected exactly one '{prefix.strip()}' line, found {len(found)}")
    value = found[0][len(prefix) :].strip()
    if not value:
        raise VersionError(f"'{prefix.strip()}' has an empty value")
    return value


def set_key(lines: list[str], key: str, value: str) -> None:
    prefix = _key_prefix(key)
    indexes = [i for i, line in enumerate(lines) if line_body(line).startswith(prefix)]
    if len(indexes) != 1:
        raise VersionError(f"expected exactly one '{prefix.strip()}' line, found {len(indexes)}")
    index = indexes[0]
    lines[index] = f"{prefix}{value}{line_ending(lines[index])}"


def parse_components(value: str, *, require_three: bool) -> tuple[int, int, int]:
    parts = value.split(".")
    if require_three and len(parts) != 3:
        raise VersionError(f"version {value!r} must be major.minor.patch")
    if not 1 <= len(parts) <= 3:
        raise VersionError(f"version {value!r} must have 1 to 3 numeric components")
    numbers: list[int] = []
    for part in parts:
        if not part.isdigit() or (len(part) > 1 and part.startswith("0")):
            raise VersionError(f"version component {part!r} in {value!r} is not an integer")
        numbers.append(int(part))
    while len(numbers) < 3:
        numbers.append(0)
    return numbers[0], numbers[1], numbers[2]


def read_quest_version(text: str) -> QuestVersion:
    lines = text.splitlines(keepends=True)
    bundle = read_key(lines, "bundleVersion")
    parse_components(bundle, require_three=False)
    code_text = read_key(lines, "AndroidBundleVersionCode")
    if not code_text.isdigit() or (len(code_text) > 1 and code_text.startswith("0")):
        raise VersionError(f"AndroidBundleVersionCode {code_text!r} is not an integer")
    return QuestVersion(bundle, int(code_text))


def apply_bump(
    text: str,
    *,
    part: str | None = None,
    set_version: str | None = None,
    bundle_code: int | None = None,
    keep_code: bool = False,
) -> tuple[str, QuestVersion, QuestVersion]:
    """Return the updated file text plus the version before and after the bump."""
    if keep_code and bundle_code is not None:
        raise VersionError("--keep-code cannot be combined with --bundle-code")
    if set_version is not None and part is not None:
        raise VersionError("pass either a bump part or --set, not both")

    lines = text.splitlines(keepends=True)
    before = read_quest_version(text)
    major, minor, patch = parse_components(before.bundle_version, require_three=False)

    if set_version is not None:
        major, minor, patch = parse_components(set_version, require_three=True)
        new_version = f"{major}.{minor}.{patch}"
    elif part in (None, "patch"):
        new_version = f"{major}.{minor}.{patch + 1}"
    elif part == "minor":
        new_version = f"{major}.{minor + 1}.0"
    elif part == "major":
        new_version = f"{major + 1}.0.0"
    else:
        raise VersionError(f"unknown bump part {part!r}")

    if keep_code:
        new_code = before.bundle_code
        if new_version == before.bundle_version:
            raise VersionError("version is unchanged and --keep-code was set")
    elif bundle_code is not None:
        new_code = bundle_code
        if new_code <= before.bundle_code:
            raise VersionError(
                f"bundle code {new_code} must be greater than current {before.bundle_code}"
            )
    else:
        new_code = before.bundle_code + 1

    if not 1 <= new_code <= MAX_VERSION_CODE:
        raise VersionError(f"bundle code {new_code} is outside 1..{MAX_VERSION_CODE}")

    set_key(lines, "bundleVersion", new_version)
    set_key(lines, "AndroidBundleVersionCode", str(new_code))
    after = QuestVersion(new_version, new_code)
    return "".join(lines), before, after


def format_change(before: QuestVersion, after: QuestVersion, *, dry_run: bool) -> str:
    prefix = "dry-run: " if dry_run else ""
    return (
        f"{prefix}bundleVersion: {before.bundle_version} -> {after.bundle_version}\n"
        f"{prefix}AndroidBundleVersionCode: {before.bundle_code} -> {after.bundle_code}\n"
        f"{prefix}tag: v{after.bundle_version}"
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project",
        type=Path,
        default=DEFAULT_PROJECT,
        help="Path to ProjectSettings.asset (default: this repo's copy)",
    )
    bump = parser.add_mutually_exclusive_group()
    bump.add_argument("--patch", action="store_true", help="Increment the patch component (default)")
    bump.add_argument("--minor", action="store_true", help="Increment minor and reset patch to 0")
    bump.add_argument("--major", action="store_true", help="Increment major and reset minor and patch to 0")
    bump.add_argument("--set", dest="set_version", metavar="VERSION", help="Set major.minor.patch")
    parser.add_argument("--bundle-code", type=int, help="Set AndroidBundleVersionCode to this integer")
    parser.add_argument("--keep-code", action="store_true", help="Leave AndroidBundleVersionCode unchanged")
    parser.add_argument("--dry-run", action="store_true", help="Print the change and do not write")
    parser.add_argument("--print-current", action="store_true", help="Print bundleVersion and exit")
    parser.add_argument("--print-code", action="store_true", help="Print AndroidBundleVersionCode and exit")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    path: Path = args.project
    if not path.is_file():
        print(f"bump-quest-version: {path} does not exist", file=sys.stderr)
        return 1
    text = path.read_bytes().decode("utf-8")
    try:
        current = read_quest_version(text)
        if args.print_current or args.print_code:
            if args.print_current:
                print(current.bundle_version)
            if args.print_code:
                print(current.bundle_code)
            return 0

        part = "major" if args.major else "minor" if args.minor else "patch" if args.patch or not args.set_version else None
        updated, before, after = apply_bump(
            text,
            part=part,
            set_version=args.set_version,
            bundle_code=args.bundle_code,
            keep_code=args.keep_code,
        )
    except VersionError as exc:
        print(f"bump-quest-version: {exc}", file=sys.stderr)
        return 1

    print(format_change(before, after, dry_run=args.dry_run))
    if args.dry_run:
        return 0
    temporary = path.with_name(path.name + ".tmp")
    try:
        temporary.write_bytes(updated.encode("utf-8"))
        temporary.replace(path)
    finally:
        if temporary.exists():
            temporary.unlink()
    print(f"wrote {path}")
    print(f"Commit this file, then tag v{after.bundle_version} when that commit is the release.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
