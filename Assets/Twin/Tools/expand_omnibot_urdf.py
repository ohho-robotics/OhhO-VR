#!/usr/bin/env python3
"""Expand omnibot.urdf.xacro into a plain URDF XML document.

The Quest client does not ship a ROS xacro binary. This script evaluates the
subset of xacro that omnibot.urdf.xacro uses (properties, args, macros, if /
unless, and ${} expressions) and writes Assets/Twin/URDF/omnibot.urdf.xml.

Default args match the desktop description: drive=mecanum, lidar=false,
minimal_sensors=false. sim.launch.py passes drive=planar lidar=true
minimal_sensors=true; pass those flags to regenerate that variant.

Arm joint names are the OHH-199 sim joints: arm_shoulder_pan,
arm_shoulder_lift, arm_elbow_flex, arm_wrist_flex, arm_wrist_roll, arm_gripper.
"""

from __future__ import annotations

import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET

XACRO = "http://www.ros.org/wiki/xacro"
X = f"{{{XACRO}}}"

ARG_RE = re.compile(r"\$\(arg\s+([A-Za-z_][A-Za-z0-9_]*)\)")
EXPR_RE = re.compile(r"\$\{([^}]*)\}|\$\(arg\s+([A-Za-z_][A-Za-z0-9_]*)\)")

REQUIRED_JOINTS = (
    "arm_shoulder_pan",
    "arm_shoulder_lift",
    "arm_elbow_flex",
    "arm_wrist_flex",
    "arm_wrist_roll",
    "arm_gripper",
    "front_left_wheel_joint",
    "front_right_wheel_joint",
    "rear_left_wheel_joint",
    "rear_right_wheel_joint",
    "base_footprint_joint",
)


def coerce(value):
    if isinstance(value, (bool, int, float)):
        return value
    text = str(value).strip()
    lower = text.lower()
    if lower == "true":
        return True
    if lower == "false":
        return False
    try:
        return float(text)
    except ValueError:
        return text


def format_value(value) -> str:
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, float):
        return f"{value:.12g}"
    return str(value)


def eval_expr(expr: str, scope: dict):
    def arg_repl(match: re.Match) -> str:
        name = match.group(1)
        if name not in scope:
            raise KeyError(f"xacro arg '{name}' is not set")
        return str(scope[name])

    python_expr = ARG_RE.sub(arg_repl, expr)
    try:
        return eval(python_expr, {"__builtins__": {}}, scope)  # noqa: S307 — trusted local xacro
    except Exception as exc:  # noqa: BLE001 — surface the expression that failed
        raise RuntimeError(f"failed to evaluate xacro expression ${{{expr}}}: {exc}") from exc


def substitute(text: str, scope: dict, whole: bool = False):
    if text is None:
        return ""
    matches = list(EXPR_RE.finditer(text))
    if not matches:
        return text
    if whole and len(matches) == 1 and matches[0].span() == (0, len(text)):
        expr, arg = matches[0].group(1), matches[0].group(2)
        if arg is not None:
            if arg not in scope:
                raise KeyError(f"xacro arg '{arg}' is not set")
            return scope[arg]
        return eval_expr(expr, scope)

    pieces = []
    cursor = 0
    for match in matches:
        pieces.append(text[cursor:match.start()])
        expr, arg = match.group(1), match.group(2)
        if arg is not None:
            if arg not in scope:
                raise KeyError(f"xacro arg '{arg}' is not set")
            pieces.append(format_value(scope[arg]))
        else:
            pieces.append(format_value(eval_expr(expr, scope)))
        cursor = match.end()
    pieces.append(text[cursor:])
    return "".join(pieces)


def is_true(value) -> bool:
    value = coerce(value)
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    return False


class Macro:
    def __init__(self, params, body):
        self.params = params  # list of (name, default or None)
        self.body = body      # parent element whose children are the macro body


def parse_params(spec: str):
    params = []
    for raw in spec.split():
        if ":=" in raw:
            name, default = raw.split(":=", 1)
            params.append((name, default))
        else:
            params.append((raw, None))
    return params


def local_tag(tag: str) -> str:
    if tag.startswith("{"):
        return tag.split("}", 1)[1]
    return tag


class Expander:
    def __init__(self, scope: dict):
        self.scope = scope
        self.macros = {}

    def process_children(self, elem, scope: dict):
        out = []
        for child in list(elem):
            out.extend(self.process(child, scope))
        return out

    def process(self, elem, scope: dict):
        tag = elem.tag
        if tag == X + "property":
            name = elem.attrib["name"]
            raw = elem.attrib.get("value", "")
            scope[name] = coerce(substitute(raw, scope, whole=True))
            return []
        if tag == X + "arg":
            name = elem.attrib["name"]
            if name not in scope:
                scope[name] = coerce(substitute(elem.attrib.get("default", ""), scope, whole=True))
            return []
        if tag == X + "macro":
            name = elem.attrib["name"]
            self.macros[name] = Macro(parse_params(elem.attrib.get("params", "")), elem)
            return []
        if tag == X + "if":
            cond = substitute(elem.attrib.get("value", ""), scope, whole=True)
            return self.process_children(elem, scope) if is_true(cond) else []
        if tag == X + "unless":
            cond = substitute(elem.attrib.get("value", ""), scope, whole=True)
            return [] if is_true(cond) else self.process_children(elem, scope)
        if isinstance(tag, str) and tag.startswith(X):
            name = local_tag(tag)
            if name in self.macros:
                return self.expand_macro(name, elem, scope)
            raise RuntimeError(f"unsupported xacro tag '{name}'")
        return [self.emit(elem, scope)]

    def expand_macro(self, name: str, call, scope: dict):
        macro = self.macros[name]
        local = dict(scope)
        provided = {}
        for key, value in call.attrib.items():
            provided[local_tag(key)] = coerce(substitute(value, scope, whole=True))
        for param, default in macro.params:
            if param in provided:
                local[param] = provided[param]
            elif default is not None:
                local[param] = coerce(substitute(default, scope, whole=True))
            else:
                raise RuntimeError(f"macro '{name}' missing parameter '{param}'")
        unknown = set(provided) - {name for name, _ in macro.params}
        if unknown:
            raise RuntimeError(f"macro '{name}' got unexpected parameters {sorted(unknown)}")
        return self.process_children(macro.body, local)

    def emit(self, elem, scope: dict):
        node = ET.Element(local_tag(elem.tag))
        for key, value in elem.attrib.items():
            if key.startswith("{") and XACRO in key:
                continue
            node.set(local_tag(key), str(substitute(value, scope, whole=False)))
        if elem.text and elem.text.strip():
            node.text = str(substitute(elem.text, scope, whole=False))
        for child in self.process_children(elem, scope):
            node.append(child)
        return node


def default_xacro() -> str:
    env = os.environ.get("OMNIBOT_XACRO", "")
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        env,
        os.path.normpath(os.path.join(
            here, "..", "..", "..", "..", "OmniBot", "omnibot-ros2",
            "omnibot_description", "urdf", "omnibot.urdf.xacro")),
        os.path.expanduser(
            "~/ohho/OmniBot/omnibot-ros2/omnibot_description/urdf/omnibot.urdf.xacro"),
    ]
    for path in candidates:
        if path and os.path.isfile(path):
            return path
    return candidates[1]


def default_out() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.normpath(os.path.join(here, "..", "URDF", "omnibot.urdf.xml"))


def expand(xacro_path: str, drive: str, lidar: str, minimal_sensors: str) -> ET.Element:
    tree = ET.parse(xacro_path)
    root = tree.getroot()
    if local_tag(root.tag) != "robot":
        raise RuntimeError(f"expected <robot>, found <{local_tag(root.tag)}>")
    scope = {
        "drive": coerce(drive),
        "lidar": coerce(lidar),
        "minimal_sensors": coerce(minimal_sensors),
    }
    expander = Expander(scope)
    robot = ET.Element("robot", {"name": root.attrib.get("name", "omnibot")})
    comment = (
        " Expanded from omnibot.urdf.xacro by Assets/Twin/Tools/expand_omnibot_urdf.py. "
        f"Args: drive={drive} lidar={lidar} minimal_sensors={minimal_sensors}. "
        "Joint and link names match the xacro. Gazebo plugins and transmissions are "
        "kept for a faithful expansion; the Quest twin loader ignores them. "
        "Visuals are the xacro primitives (boxes and cylinders), which is what Gazebo "
        "renders. SO-101 STL meshes are decimated separately under Meshes/so101."
    )
    robot.append(ET.Comment(comment))
    for child in expander.process_children(root, scope):
        robot.append(child)
    return robot


def assert_faithful(robot: ET.Element):
    joints = [el.attrib["name"] for el in robot.iter("joint")]
    links = [el.attrib["name"] for el in robot.iter("link")]
    missing = [name for name in REQUIRED_JOINTS if name not in joints]
    if missing:
        raise SystemExit(f"expanded URDF is missing joints: {missing}")
    if "lidar_link" in links:
        raise SystemExit("default expansion should not include lidar_link (lidar:=false)")
    if "base_footprint" not in links or "base_link" not in links:
        raise SystemExit("expanded URDF is missing the chassis links")
    # No xacro tags may survive.
    for el in robot.iter():
        if isinstance(el.tag, str) and (el.tag.startswith(X) or el.tag.startswith("xacro:")):
            raise SystemExit(f"xacro tag left in output: {el.tag}")
        for value in el.attrib.values():
            if "$" in value:
                raise SystemExit(f"unexpanded expression in <{el.tag}>: {value}")


def write_urdf(robot: ET.Element, path: str):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    ET.indent(robot, space="  ")
    tree = ET.ElementTree(robot)
    tree.write(path, encoding="utf-8", xml_declaration=True)
    # ElementTree writes the declaration without a newline before the root on
    # some versions; re-read and normalise to UTF-8 with a trailing newline.
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read()
    if not text.endswith("\n"):
        text += "\n"
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--xacro", default=default_xacro(), help="path to omnibot.urdf.xacro")
    parser.add_argument("--out", default=default_out(), help="output URDF XML path")
    parser.add_argument("--drive", default="mecanum", choices=("mecanum", "planar"))
    parser.add_argument("--lidar", default="false", choices=("true", "false"))
    parser.add_argument("--minimal-sensors", default="false", choices=("true", "false"))
    args = parser.parse_args(argv)
    if not os.path.isfile(args.xacro):
        print(f"xacro not found: {args.xacro}", file=sys.stderr)
        print("Pass --xacro or set OMNIBOT_XACRO.", file=sys.stderr)
        return 1
    robot = expand(args.xacro, args.drive, args.lidar, args.minimal_sensors)
    assert_faithful(robot)
    write_urdf(robot, args.out)
    joints = [el.attrib["name"] for el in robot.iter("joint")]
    links = [el.attrib["name"] for el in robot.iter("link")]
    print(f"wrote {args.out}")
    print(f"links={len(links)} joints={len(joints)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
