#!/usr/bin/env python3
"""Decimate SO-101 STL meshes to a Quest triangle budget.

The OmniBot xacro renders the arm with boxes and cylinders. The STL files under
omnibot_description/meshes/so101 are the TheRobotStudio SO-ARM100 parts (Apache-2.0),
vendored by OmniBot. They are not referenced by the xacro. This script vertex-clusters
them into OBJ files under Assets/Twin/Meshes/so101 so a later mesh visual stays
under 50_000 triangles in total.

Original centimetre-or-millimetre units are preserved. See ATTRIBUTION.md.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import struct
import sys

BUDGET = 45000
VERTEX_FMT = struct.Struct("<12fH")  # normal, 3 verts, attribute


def default_mesh_dir() -> str:
    env = os.environ.get("OMNIBOT_SO101_MESHES", "")
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        env,
        os.path.normpath(os.path.join(
            here, "..", "..", "..", "..", "OmniBot", "omnibot-ros2",
            "omnibot_description", "meshes", "so101")),
        os.path.expanduser(
            "~/ohho/OmniBot/omnibot-ros2/omnibot_description/meshes/so101"),
    ]
    for path in candidates:
        if path and os.path.isdir(path):
            return path
    return candidates[1]


def default_out() -> str:
    here = os.path.dirname(os.path.abspath(__file__))
    return os.path.normpath(os.path.join(here, "..", "Meshes", "so101"))


def read_binary_stl(path: str):
    with open(path, "rb") as handle:
        header = handle.read(80)
        (count,) = struct.unpack("<I", handle.read(4))
        payload = handle.read(count * 50)
    if len(payload) != count * 50:
        raise RuntimeError(f"{path} is truncated ({count} tris declared)")
    tris = []
    mins = [math.inf, math.inf, math.inf]
    maxs = [-math.inf, -math.inf, -math.inf]
    for i in range(count):
        data = VERTEX_FMT.unpack_from(payload, i * 50)
        verts = (data[3:6], data[6:9], data[9:12])
        tris.append(verts)
        for v in verts:
            for axis in range(3):
                mins[axis] = min(mins[axis], v[axis])
                maxs[axis] = max(maxs[axis], v[axis])
    return tris, mins, maxs, header


def cluster(tris, cell: float):
    acc = {}
    faces = []
    seen = set()
    inv = 1.0 / cell

    def key(v):
        return (
            int(math.floor(v[0] * inv + 0.5)),
            int(math.floor(v[1] * inv + 0.5)),
            int(math.floor(v[2] * inv + 0.5)),
        )

    for tri in tris:
        keys = []
        for v in tri:
            k = key(v)
            slot = acc.get(k)
            if slot is None:
                acc[k] = [v[0], v[1], v[2], 1]
            else:
                slot[0] += v[0]
                slot[1] += v[1]
                slot[2] += v[2]
                slot[3] += 1
            keys.append(k)
        if keys[0] == keys[1] or keys[1] == keys[2] or keys[0] == keys[2]:
            continue
        face = tuple(sorted(keys))
        if face in seen:
            continue
        seen.add(face)
        faces.append(tuple(keys))

    order = list(acc.keys())
    index = {k: i for i, k in enumerate(order)}
    verts = []
    for k in order:
        sx, sy, sz, n = acc[k]
        verts.append((sx / n, sy / n, sz / n))
    tris_out = [(index[a], index[b], index[c]) for a, b, c in faces]
    return verts, tris_out


def diagonal(mins, maxs) -> float:
    return math.sqrt(sum((maxs[i] - mins[i]) ** 2 for i in range(3))) or 1.0


def decimate(tris, mins, maxs, target: int):
    if len(tris) <= target:
        verts = []
        faces = []
        index = {}
        for tri in tris:
            ids = []
            for v in tri:
                key = (round(v[0], 6), round(v[1], 6), round(v[2], 6))
                if key not in index:
                    index[key] = len(verts)
                    verts.append(key)
                ids.append(index[key])
            if ids[0] != ids[1] and ids[1] != ids[2] and ids[0] != ids[2]:
                faces.append(tuple(ids))
        return verts, faces

    diag = diagonal(mins, maxs)
    lo = diag / 100000.0
    hi = diag
    best = None
    for _ in range(28):
        mid = (lo + hi) * 0.5
        verts, faces = cluster(tris, mid)
        if len(faces) > target:
            lo = mid
        else:
            best = (verts, faces)
            hi = mid
    if best is None:
        best = cluster(tris, hi)
    return best


def allocate(counts, budget: int):
    total = sum(counts) or 1
    targets = []
    for count in counts:
        share = max(64, int(round(budget * count / total)))
        targets.append(min(count, share))
    # Largest meshes give up triangles first if rounding overshoots.
    guard = 0
    while sum(targets) > budget and guard < 100000:
        guard += 1
        idx = max(range(len(targets)), key=lambda i: targets[i])
        if targets[idx] <= 64:
            break
        targets[idx] -= 1
    return targets


def write_obj(path: str, source_name: str, source_tris: int, verts, faces):
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(f"# Decimated for Quest from {source_name}\n")
        handle.write(f"# Source triangles: {source_tris}\n")
        handle.write(f"# Decimated triangles: {len(faces)}\n")
        handle.write("# Licence: Apache-2.0, TheRobotStudio/SO-ARM100 via ohho-robotics/OmniBot.\n")
        handle.write("# Units are unchanged from the STL. See ATTRIBUTION.md.\n")
        for x, y, z in verts:
            handle.write(f"v {x:.6f} {y:.6f} {z:.6f}\n")
        for a, b, c in faces:
            handle.write(f"f {a + 1} {b + 1} {c + 1}\n")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mesh-dir", default=default_mesh_dir())
    parser.add_argument("--out", default=default_out())
    parser.add_argument("--budget", type=int, default=BUDGET)
    args = parser.parse_args(argv)
    if not os.path.isdir(args.mesh_dir):
        print(f"mesh dir not found: {args.mesh_dir}", file=sys.stderr)
        return 1
    if args.budget >= 50000:
        print("budget must stay under 50000", file=sys.stderr)
        return 1

    names = sorted(fn for fn in os.listdir(args.mesh_dir) if fn.lower().endswith(".stl"))
    if not names:
        print("no STL files found", file=sys.stderr)
        return 1

    loaded = []
    for name in names:
        tris, mins, maxs, _header = read_binary_stl(os.path.join(args.mesh_dir, name))
        loaded.append((name, tris, mins, maxs))
        print(f"read {name}: {len(tris)} tris")

    targets = allocate([len(tris) for _, tris, _, _ in loaded], args.budget)
    os.makedirs(args.out, exist_ok=True)
    report = []
    total = 0
    for (name, tris, mins, maxs), target in zip(loaded, targets):
        verts, faces = decimate(tris, mins, maxs, target)
        obj_name = os.path.splitext(name)[0] + ".obj"
        write_obj(os.path.join(args.out, obj_name), name, len(tris), verts, faces)
        extent = max(maxs[i] - mins[i] for i in range(3))
        entry = {
            "source": name,
            "obj": obj_name,
            "sourceTriangles": len(tris),
            "triangles": len(faces),
            "bboxMin": [round(v, 4) for v in mins],
            "bboxMax": [round(v, 4) for v in maxs],
            "extent": round(extent, 4),
        }
        report.append(entry)
        total += len(faces)
        print(f"wrote {obj_name}: {len(tris)} -> {len(faces)} (target {target})")

    if total >= 50000:
        print(f"decimated total {total} is not under 50000", file=sys.stderr)
        return 1

    # SO-101 STLs are modelled in millimetres (extents of tens to low hundreds).
    suggested_scale = 0.001 if any(item["extent"] > 5.0 for item in report) else 1.0
    budget_path = os.path.join(args.out, "budget.json")
    payload = {
        "limit": 50000,
        "targetBudget": args.budget,
        "totalTriangles": total,
        "suggestedUniformScale": suggested_scale,
        "note": "Vertex-clustered copies of the SO-101 STLs. The live twin renders URDF primitives.",
        "meshes": report,
    }
    with open(budget_path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2)
        handle.write("\n")
    print(f"total triangles {total} / {args.budget} (limit 50000)")
    print(f"wrote {budget_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
