#!/usr/bin/env bash
# Print the human tag/release command sheet. Does not create tags or releases.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
echo "OHH-249 print-only. No tag is created. No GitHub Release is created."
cat "$root/docs/release-tags.md"
