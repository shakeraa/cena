#!/bin/bash
# QLT-002: .NET build warning regression test
# Fails if any CS warnings exist in the actors solution
set -euo pipefail

cd "$(dirname "$0")/../.."

echo "=== QLT-002: .NET Build Warning Check ==="
echo "Building Cena.Actors.sln (no-incremental)..."

BUILD_OUTPUT=$(dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1)
WARNINGS=$(echo "$BUILD_OUTPUT" | grep "warning CS" || true)
COUNT=$(echo "$WARNINGS" | grep -c "warning CS" || true)

if [ "$COUNT" -gt 0 ]; then
  echo ""
  echo "FAIL: $COUNT build warning(s) found"
  echo "──────────────────────────────────────"

  # Group by warning code
  echo "$WARNINGS" | grep -o 'warning CS[0-9]*' | sort | uniq -c | sort -rn | while read cnt code; do
    echo "  $cnt × $code"
  done

  echo ""
  echo "By file:"
  echo "$WARNINGS" | sed 's|.*/||;s/(.*//;s/\.cs.*/.cs/' | sort | uniq -c | sort -rn | while read cnt file; do
    echo "  $cnt warning(s) in $file"
  done

  echo ""
  echo "──────────────────────────────────────"
  echo "Run 'dotnet build src/actors/Cena.Actors.sln --no-incremental 2>&1 | grep \"warning CS\"' to reproduce"
  exit 1
else
  echo "PASS: 0 build warnings"
  exit 0
fi
