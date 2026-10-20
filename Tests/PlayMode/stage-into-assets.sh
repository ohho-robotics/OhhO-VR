#!/usr/bin/env bash
# Copy play-mode sources into Assets so Unity compiles them.
# Runtime scripts have no asmdef and join Assembly-CSharp, which is the only
# way FakeRobotLink can implement IRobotLink. The copy is local and must not
# be committed. Package scripts under Packages/ are ignored unless they have
# an asmdef, and an asmdef cannot reference Assembly-CSharp.
set -euo pipefail
source_dir="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$source_dir/../.." && pwd)"
dest="$root/Assets/OhhoPlayModeTests"
rm -rf "$dest"
mkdir -p "$dest"
cp -a "$root/Tests/PlayMode/Runtime" "$dest/Runtime"
cp -a "$root/Tests/PlayMode/Tests" "$dest/Tests"
cp -a "$root/Tests/PlayMode/Editor" "$dest/Editor"
echo "Staged play-mode tests at $dest"
