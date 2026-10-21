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
grep -q 'not yet tagged' README.md || fail "README does not say the coordinated release is not yet tagged"
grep -q 'docs/compatibility-matrix.md' README.md || fail "README does not link the compatibility matrix"
grep -q 'ghcr.io/ohho-robotics/omnibot-sim:vr-1.0.0' README.md || fail "README does not pin the placeholder sim image tag"

test -f docs/compatibility-matrix.md || fail "docs/compatibility-matrix.md is missing"
test -f CHANGELOG.md || fail "CHANGELOG.md is missing"
test -f docs/release-tags.md || fail "docs/release-tags.md is missing"
test -f scripts/print-release-commands.sh || fail "scripts/print-release-commands.sh is missing"

for needle in \
  'not yet tagged' \
  'protocolVersion' \
  '1.0.0' \
  'ohho.telemetry.v1' \
  'RobotDescriptor' \
  'ohho.descriptor.v1' \
  'vr-profile-v1.0.0' \
  'ghcr.io/ohho-robotics/omnibot-sim:vr-1.0.0' \
  'placeholder' \
  '4ebc555a7495ee7a2cd0f62cbe85fbc201ad1d4b' \
  'b427a2d11055e3c1e817960369945a501234e9e9' \
  '6f690d50b5991b13a35430581f84626529ab870d' \
  '5a20f1c23c10c814b8a790536a80b21695b3ed82' \
  'b010b84c2bab7ec4f11f928af1b20be1fc31cc95' \
  'ddb4beefd3e81f9c6ae0d21f75a9b0820023de05'
do
  grep -q "$needle" docs/compatibility-matrix.md || fail "compatibility matrix is missing: $needle"
done

grep -q 'not released yet' CHANGELOG.md || fail "CHANGELOG does not say 1.0.0 is not released yet"
grep -q 'Keep a Changelog' CHANGELOG.md || fail "CHANGELOG is not Keep a Changelog style"
grep -q 'git tag -a v1.0.0' docs/release-tags.md || fail "release command sheet is missing the OhhO-VR tag command"
grep -q 'gh release create v1.0.0' docs/release-tags.md || fail "release command sheet is missing gh release create for OhhO-VR"
grep -q 'git tag -a vr-profile-v1.0.0' docs/release-tags.md || fail "release command sheet is missing the VR profile tag command"
grep -q 'git tag -a protocol-v1.0.0' docs/release-tags.md || fail "release command sheet is missing the protocol tag command"
grep -q 'Do not run' docs/release-tags.md || fail "release command sheet does not tell a human to stop"

if grep -nE '^git tag |^git push |^gh release create ' scripts/print-release-commands.sh; then
  fail "print-release-commands.sh would execute a tag or release command"
fi

if grep -nE 'v1\.0\.0 is released|release is published|image is on GHCR|has been tagged' \
  README.md docs/compatibility-matrix.md CHANGELOG.md docs/release-tags.md; then
  fail "coordinated-release docs claim a tag or image that does not exist"
fi

grep -q 'bundleVersion: 1.0' ProjectSettings/ProjectSettings.asset || fail "ProjectSettings bundleVersion is no longer 1.0; update the matrix"

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

test -f docs/recording/quest-teleop-clip.md || fail "docs/recording/quest-teleop-clip.md is missing"
test -f scripts/record-quest-clip.sh || fail "scripts/record-quest-clip.sh is missing"
test -f scripts/record-quest-clip.ps1 || fail "scripts/record-quest-clip.ps1 is missing"
grep -q '1920x1080' scripts/record-quest-clip.sh || fail "bash recorder is missing 1920x1080"
grep -q '1920x1080' scripts/record-quest-clip.ps1 || fail "powershell recorder is missing 1920x1080"
grep -q '8000000' scripts/record-quest-clip.sh || fail "bash recorder is missing bitrate 8000000"
grep -q '8000000' scripts/record-quest-clip.ps1 || fail "powershell recorder is missing bitrate 8000000"
grep -q -- '--dry-run' scripts/record-quest-clip.sh || fail "bash recorder has no --dry-run"
grep -q -- '--dry-run' scripts/record-quest-clip.ps1 || fail "powershell recorder has no --dry-run"
grep -q 'Needs Varun: Quest + robot, record and upload' docs/recording/quest-teleop-clip.md || fail "clip doc is missing the Needs Varun note"
if grep -nE 'ros2|cmd_vel|topic pub' scripts/record-quest-clip.sh scripts/record-quest-clip.ps1; then
  fail "a clip recorder names a robot command"
fi

python3 - << 'PY'
from pathlib import Path
text = Path("README.md").read_text(encoding="utf-8").replace("\r\n", "\n")
marker = "## Demo clip\n"
start = text.find(marker)
if start < 0:
    raise SystemExit("README has no Demo clip section")
rest = text[start + len(marker):]
next_heading = rest.find("\n## ")
section = rest if next_heading < 0 else rest[:next_heading]
required = (
    "pending",
    "docs/recording/quest-teleop-clip.md",
    "Needs Varun: Quest + robot, record and upload",
)
for item in required:
    if item not in section:
        raise SystemExit("Demo clip section missing: " + item)
lower = section.lower()
if "youtu.be" in lower or "youtube.com" in lower or "http://" in lower or "https://" in lower:
    raise SystemExit("Demo clip section has a URL while the clip is pending")
print("demo-clip section ok")
PY

dry="$(bash scripts/record-quest-clip.sh --dry-run)"
printf '%s\n' "$dry"
printf '%s\n' "$dry" | grep -F 'No adb call' >/dev/null || fail "dry-run did not promise to skip adb"
printf '%s\n' "$dry" | grep -F 'screenrecord --size 1920x1080 --bit-rate 8000000 --time-limit 45' >/dev/null || fail "dry-run is missing the timed screenrecord"
printf '%s\n' "$dry" | grep -F 'pull /sdcard/ohh60-quest-teleop.mp4' >/dev/null || fail "dry-run is missing adb pull"
if printf '%s\n' "$dry" | grep -F 'List of devices attached' >/dev/null; then
  fail "dry-run contacted adb"
fi
if bash scripts/record-quest-clip.sh --dry-run --seconds 29; then
  fail "--seconds 29 should fail"
fi

echo "check-public-docs: ok"
