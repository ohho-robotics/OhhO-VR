#!/usr/bin/env bash
# Fails if public docs still claim files or engine pins that are not in this repo.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

fail() {
  echo "check-public-docs: $1" >&2
  exit 1
}

if grep -nE 'launch_vr_teleop\.sh|launch_rosbridge\.sh|^cd robot_ws|`robot_ws`' README.md SCENE_SETUP.md; then
  fail "README or SCENE_SETUP still names a launch script or robot workspace that is not in this repo"
fi

if grep -nE '2023\.3\.0f1' README.md SCENE_SETUP.md MULTI_ROBOT_TELEOP_ARCHITECTURE.md; then
  fail "a public doc still pins Unity 2023.3.0f1"
fi

if grep -nEi 'omnibot_vr|in this branch' README.md SCENE_SETUP.md MULTI_ROBOT_TELEOP_ARCHITECTURE.md; then
  fail "a public doc still names omnibot_vr or says \"in this branch\""
fi

grep -q '6000.5.2f1' README.md || fail "README does not name Unity 6000.5.2f1"
grep -q '203.0.0' README.md || fail "README does not name Meta XR SDK 203.0.0"
grep -q 'Assets/scene1.unity' README.md || fail "README does not name Assets/scene1.unity"
grep -q 'headset client only' README.md || fail "README does not say this repo is the headset client only"
grep -q 'roadmap' README.md || fail "README does not label WebRTC as roadmap"

test -f Assets/scene1.unity || fail "Assets/scene1.unity is missing"
test ! -e Assets/macantigravity1.unity || fail "Assets/macantigravity1.unity is still in Assets"
grep -q 'path: Assets/scene1.unity' ProjectSettings/EditorBuildSettings.asset || fail "Build Settings does not enable Assets/scene1.unity"

python3 - << 'PY'
from pathlib import Path
p = Path("Packages/com.meta.xr.sdk.core/Scripts/RuntimeOptimizer/Core/RuntimeOptimizerPlugin.cs")
text = p.read_text()
define_at = text.find("#define")
using_at = text.find("\nusing ")
if define_at < 0 or using_at < 0 or define_at > using_at:
    raise SystemExit("RuntimeOptimizerPlugin.cs has #define after a using (CS1032 on Unity 6000.5)")
print("define-order ok")
PY

echo "check-public-docs: ok"
