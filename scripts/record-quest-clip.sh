#!/usr/bin/env bash
# OHH-60: timed Quest screen capture, then pull the mp4 into a dated folder.
# Talks to adb only (devices, getprop, screenrecord, pull, rm of that mp4).
# Does not start a robot, open a robot socket, or publish a command.
# --dry-run prints the plan and does not call adb.
set -euo pipefail

SIZE="1920x1080"
BITRATE="8000000"
REMOTE="/sdcard/ohh60-quest-teleop.mp4"
SECONDS_LIMIT=45
DRY_RUN=0
SERIAL=""
OUT_PARENT=""

usage() {
  cat <<'EOF'
Usage: scripts/record-quest-clip.sh [--dry-run] [--seconds 30-60] [--serial SERIAL] [--out DIR]

Checks that adb devices shows a Quest, records the headset screen for the
given number of seconds, and pulls the mp4 into a UTC-dated folder.
--dry-run prints those commands and exits. It does not call adb.
EOF
}

fail() {
  echo "record-quest-clip: $1" >&2
  exit "${2:-1}"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run) DRY_RUN=1; shift ;;
    --seconds)
      [[ $# -ge 2 ]] || fail "--seconds needs a number" 2
      SECONDS_LIMIT="$2"
      shift 2
      ;;
    --serial)
      [[ $# -ge 2 ]] || fail "--serial needs a value" 2
      SERIAL="$2"
      shift 2
      ;;
    --out)
      [[ $# -ge 2 ]] || fail "--out needs a directory" 2
      OUT_PARENT="$2"
      shift 2
      ;;
    -h|--help) usage; exit 0 ;;
    *) fail "unknown argument: $1" 2 ;;
  esac
done

if ! [[ "$SECONDS_LIMIT" =~ ^[0-9]+$ ]] || (( SECONDS_LIMIT < 30 || SECONDS_LIMIT > 60 )); then
  fail "--seconds must be an integer from 30 to 60" 2
fi

if [[ -n "$SERIAL" && ! "$SERIAL" =~ ^[A-Za-z0-9._:-]+$ ]]; then
  fail "--serial has unexpected characters" 2
fi

root="$(cd "$(dirname "$0")/.." && pwd)"
if [[ -z "$OUT_PARENT" ]]; then
  OUT_PARENT="$root/recordings/quest-clips"
fi
stamp="$(date -u +%Y%m%d-%H%M%S)"
dest_dir="$OUT_PARENT/$stamp"
dest_file="$dest_dir/ohh60-quest-teleop.mp4"
serial_display="${SERIAL:-<serial>}"

print_plan() {
  echo "OHH-60 record-quest-clip: dry-run. No adb call. No device. No robot command."
  echo "Would check: adb devices -l"
  echo "Would require one row whose status is device and whose model contains Quest."
  echo "Would run: adb -s ${serial_display} shell rm -f ${REMOTE}"
  echo "Would run: adb -s ${serial_display} shell screenrecord --size ${SIZE} --bit-rate ${BITRATE} --time-limit ${SECONDS_LIMIT} ${REMOTE}"
  echo "Would run: adb -s ${serial_display} pull ${REMOTE} ${dest_file}"
  echo "Would run: adb -s ${serial_display} shell rm -f ${REMOTE}"
  echo "Next: watch the mp4, rename it sim or real using docs/recording/quest-teleop-clip.md, and upload. This script never sends a robot command."
}

if [[ "$DRY_RUN" -eq 1 ]]; then
  print_plan
  exit 0
fi

command -v adb >/dev/null 2>&1 || fail "adb is not on PATH"

listing="$(adb devices -l | tr -d '\r')"
matches=()
skipped=""
while IFS= read -r line; do
  [[ -z "$line" || "$line" == List\ of\ devices\ attached* ]] && continue
  # shellcheck disable=SC2086
  set -- $line
  [[ $# -ge 2 ]] || continue
  row_serial="$1"
  row_status="$2"
  [[ "$row_status" == "device" ]] || continue
  if [[ -n "$SERIAL" && "$row_serial" != "$SERIAL" ]]; then
    continue
  fi
  if printf '%s\n' "$line" | grep -Eiq 'model:[^[:space:]]*[Qq]uest'; then
    matches+=("$row_serial")
    continue
  fi
  model="$(adb -s "$row_serial" shell getprop ro.product.model | tr -d '\r')"
  if printf '%s\n' "$model" | grep -iq 'quest'; then
    matches+=("$row_serial")
  else
    skipped="${skipped}${row_serial} (${model:-no model})"$'\n'
  fi
done <<< "$listing"

if [[ ${#matches[@]} -eq 0 ]]; then
  printf '%s\n' "$listing" >&2
  if [[ -n "$skipped" ]]; then
    printf 'Not a Quest:\n%s' "$skipped" >&2
  fi
  fail "adb devices did not show a Quest in the device state. Allow the USB prompt on the headset if the row says unauthorized."
fi
if [[ ${#matches[@]} -gt 1 ]]; then
  printf '%s\n' "${matches[@]}" >&2
  fail "more than one Quest is attached. Re-run with --serial."
fi
serial="${matches[0]}"

echo "OHH-60 record-quest-clip: recording ${SECONDS_LIMIT}s from ${serial}."
echo "Perform the shot list in docs/recording/quest-teleop-clip.md. This script will not send a robot command."
adb -s "$serial" shell rm -f "$REMOTE" || true
if ! adb -s "$serial" shell screenrecord --size "$SIZE" --bit-rate "$BITRATE" --time-limit "$SECONDS_LIMIT" "$REMOTE"; then
  fail "screenrecord failed. If the headset rejects ${SIZE}, use Meta Quest Developer Hub. See docs/recording/quest-teleop-clip.md. This script did not send a robot command."
fi
mkdir -p "$dest_dir"
adb -s "$serial" pull "$REMOTE" "$dest_file"
if [[ ! -s "$dest_file" ]]; then
  fail "pull produced an empty file. The headset copy is still at ${REMOTE}."
fi
adb -s "$serial" shell rm -f "$REMOTE" || echo "record-quest-clip: pulled the file; could not remove ${REMOTE} on the headset." >&2

echo "Pulled: ${dest_file}"
echo "Next:"
echo "1. Watch the file. If it is black or only the 2D shell, record again from Meta Quest Developer Hub. See docs/recording/quest-teleop-clip.md."
echo "2. Rename to ohh60-sim-quest-<UTC date>.mp4 or ohh60-real-quest-<UTC date>.mp4. REAL only after the OHH-234 stages pass."
echo "3. Pair it with the external room camera and upload with the title and description in that doc."
echo "4. This script did not send a robot command."
