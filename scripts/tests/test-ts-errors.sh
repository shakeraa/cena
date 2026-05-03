#!/bin/bash
# QLT-001: TypeScript error regression test
# Fails if any TS errors exist in the admin frontend
set -euo pipefail

cd "$(dirname "$0")/../../src/admin/full-version"

echo "=== QLT-001: TypeScript Error Check ==="
echo "Running vue-tsc --noEmit..."

ERRORS=$(npx vue-tsc --noEmit 2>&1 | grep "error TS" || true)
COUNT=$(echo "$ERRORS" | grep -c "error TS" || true)

if [ "$COUNT" -gt 0 ]; then
  echo ""
  echo "FAIL: $COUNT TypeScript error(s) found"
  echo "──────────────────────────────────────"

  # Group by file
  echo "$ERRORS" | sed 's/(.*//;s/src\///' | sort | uniq -c | sort -rn | while read cnt file; do
    echo "  $cnt error(s) in $file"
  done

  echo ""
  echo "Details:"
  echo "$ERRORS"
  echo ""
  echo "──────────────────────────────────────"
  echo "Run 'npx vue-tsc --noEmit' in src/admin/full-version/ to reproduce"
  exit 1
else
  echo "PASS: 0 TypeScript errors"
  exit 0
fi
