#!/bin/bash
# QLT-003: .NET test suite — must all pass
set -euo pipefail

cd "$(dirname "$0")/../.."

echo "=== QLT-003: .NET Test Suite ==="
echo "Running dotnet test..."

OUTPUT=$(dotnet test src/actors/Cena.Actors.Tests/ --no-restore 2>&1) || true
echo "$OUTPUT" | tail -5

if echo "$OUTPUT" | grep -q "Failed:     0"; then
  PASSED=$(echo "$OUTPUT" | grep -o 'Passed: *[0-9]*' | grep -o '[0-9]*' || echo "?")
  echo "PASS: $PASSED tests passed"
  exit 0
else
  echo "FAIL: Test failures detected"
  echo "$OUTPUT" | grep "Failed" || true
  exit 1
fi
