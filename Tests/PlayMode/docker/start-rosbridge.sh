#!/usr/bin/env bash
# Runs inside the rosbridge container. Compose does not interpolate this file.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
ok=0
for attempt in 1 2 3; do
  if apt-get update && apt-get install -y --no-install-recommends ros-humble-rosbridge-suite; then
    ok=1
    break
  fi
  echo "apt attempt ${attempt} failed" >&2
  sleep 15
done
test "${ok}" = 1
set +u
# shellcheck disable=SC1091
source /opt/ros/humble/setup.bash
exec ros2 launch rosbridge_server rosbridge_websocket_launch.xml address:=0.0.0.0 port:=9090
