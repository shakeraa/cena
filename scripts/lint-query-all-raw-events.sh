#!/bin/bash
# =============================================================================
# Cena Platform — QueryAllRawEvents tenant-scoping lint (RDY-037)
#
# Flags any occurrence of Marten's `QueryAllRawEvents()` that does not sit
# alongside an explicit `SchoolId` tenant filter within the same file.
# The admin query pattern must always scope by SchoolId (or route through
# SuperAdmin-only) to prevent cross-tenant data leakage.
#
# Exit codes:
#   0 = all callers are tenant-scoped
#   1 = at least one caller is missing SchoolId in the same file — review
# =============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="${REPO_ROOT}/src"

if [[ ! -d "${SRC}" ]]; then
    echo "error: src directory not found at ${SRC}" >&2
    exit 2
fi

# Find all .cs files containing QueryAllRawEvents, exclude bin/obj.
# Portable to bash 3.2 (macOS default) — no mapfile.
callers_file="$(mktemp)"
trap 'rm -f "${callers_file}"' EXIT

grep -rlE 'QueryAllRawEvents' "${SRC}" \
    --include='*.cs' \
    --exclude-dir=bin \
    --exclude-dir=obj \
    2>/dev/null | sort -u > "${callers_file}" || true

total=$(wc -l < "${callers_file}" | tr -d ' ')

if [[ "${total}" -eq 0 ]]; then
    echo "lint: no QueryAllRawEvents callers found — OK"
    exit 0
fi

missing=0
while IFS= read -r file; do
    [[ -z "${file}" ]] && continue
    if ! grep -q 'SchoolId' "${file}"; then
        rel="${file#${REPO_ROOT}/}"
        echo "lint: MISSING tenant filter — ${rel} uses QueryAllRawEvents without SchoolId"
        missing=$((missing + 1))
    fi
done < "${callers_file}"
if [[ ${missing} -gt 0 ]]; then
    echo "lint: ${missing} of ${total} callers missing SchoolId filter — REVIEW REQUIRED" >&2
    exit 1
fi

echo "lint: all ${total} QueryAllRawEvents callers have SchoolId filters — OK"
exit 0
