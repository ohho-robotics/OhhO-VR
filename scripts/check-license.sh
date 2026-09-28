#!/usr/bin/env bash
# Confirms the root licence is Apache-2.0 and vendored Meta notices are intact.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

fail() {
  echo "check-license: $1" >&2
  exit 1
}

test -f LICENSE || fail "root LICENSE is missing"
grep -q "Apache License" LICENSE || fail "LICENSE is not Apache-2.0"
grep -q "Version 2.0, January 2004" LICENSE || fail "LICENSE is not Apache-2.0 Version 2.0"
grep -q "Copyright 2026 OhhO Robotics" LICENSE || fail "LICENSE copyright line changed"

test -f NOTICE || fail "NOTICE is missing"
grep -q "Oculus SDK License Agreement" NOTICE || fail "NOTICE does not keep the Meta licence name"
grep -q "Packages/com.meta.xr.sdk.core/LICENSE.md" NOTICE || fail "NOTICE does not point at the Meta Core licence"
grep -q "Packages/com.meta.xr.sdk.interaction/LICENSE.md" NOTICE || fail "NOTICE does not point at the Meta Interaction licence"

for f in Packages/com.meta.xr.sdk.core/LICENSE.md Packages/com.meta.xr.sdk.interaction/LICENSE.md; do
  test -f "$f" || fail "missing $f"
  grep -q "Oculus SDK License Agreement" "$f" || fail "$f no longer states the Oculus SDK licence"
done

grep -q "Apache_2.0" README.md || fail "README badge is not Apache-2.0"
grep -q "not Apache-2.0" README.md || fail "README does not say the Meta SDK is not Apache-2.0"
if grep -nE 'MIT / Apache|License-MIT' README.md; then
  fail "README still advertises MIT for this repo"
fi

echo "check-license: ok"
