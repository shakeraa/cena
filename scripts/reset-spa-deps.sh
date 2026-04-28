#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Untangle pnpm/npm mixed-lockfile state in the SPA tree
# (TASK-E2E-INFRA-02).
#
# HARD CONSTRAINT (per user memory feedback_destroy_containers_not_node_modules):
# do NOT `rm -rf node_modules` on the host. The dev stack runs against the
# container's own node_modules volume; recovery goes through container reset
# + lockfile-level cleanup, NOT host-side nuke.
#
# What this script does:
#   1. Recreate the cena-student-spa container's node_modules volume
#      (cheap, zero risk to host node_modules).
#   2. Drop pnpm-lock.yaml from the SPA tree (project is npm-majority;
#      pnpm-lock is leftover from a prior workflow). Idempotent.
#   3. Clean npm cache (cache-only operation; does NOT touch node_modules).
#   4. Re-run `npm install --prefer-offline` so npm reconciles its own tree.
#
# This script does NOT do `rm -rf node_modules`. If you need that, do it
# manually in a fresh shell — but first confirm steps 1-3 didn't already
# resolve your symptoms.
#
# Usage:
#   ./scripts/reset-spa-deps.sh           # student SPA (default)
#   ./scripts/reset-spa-deps.sh --admin   # admin SPA instead
#   ./scripts/reset-spa-deps.sh --check   # diagnose only, no changes
# =============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SPA_NAME="student"
CHECK_ONLY=false

while [ $# -gt 0 ]; do
  case "$1" in
    --admin) SPA_NAME="admin"; shift ;;
    --check) CHECK_ONLY=true; shift ;;
    *) echo "unknown arg: $1"; exit 2 ;;
  esac
done

if [ "$SPA_NAME" = "student" ]; then
  CONTAINER="cena-student-spa"
  SPA_DIR="$REPO_ROOT/src/student/full-version"
else
  CONTAINER="cena-admin-spa"
  SPA_DIR="$REPO_ROOT/src/admin/full-version"
fi

echo "──────────────────────────────────────────────────────────────"
echo " SPA dependency reset"
echo "   target: $SPA_NAME ($CONTAINER)"
echo "   dir:    $SPA_DIR"
echo "   mode:   $([ "$CHECK_ONLY" = true ] && echo 'CHECK-ONLY' || echo 'APPLY')"
echo "──────────────────────────────────────────────────────────────"
echo

# ── 1. Diagnose ────────────────────────────────────────────────
echo "▶ Diagnosing current state..."
HAS_PNPM_LOCK=false
HAS_NPM_LOCK=false
[ -f "$SPA_DIR/pnpm-lock.yaml" ] && { HAS_PNPM_LOCK=true; echo "  · pnpm-lock.yaml present (leftover)"; }
[ -f "$SPA_DIR/package-lock.json" ] && { HAS_NPM_LOCK=true; echo "  · package-lock.json present (canonical)"; }

if [ "$HAS_PNPM_LOCK" = true ] && [ "$HAS_NPM_LOCK" = true ]; then
  echo "  ⚠ Mixed-lockfile state detected. This is the root of the install failures."
fi

# Container check
if docker ps --format '{{.Names}}' | grep -q "^$CONTAINER\$"; then
  echo "  · $CONTAINER is running"
else
  echo "  · $CONTAINER not running (will be started after volume reset)"
fi

if [ "$CHECK_ONLY" = true ]; then
  echo
  echo "▶ Check-only mode — no changes made."
  exit 0
fi

# ── 2. Container/volume reset ─────────────────────────────────
echo
echo "▶ Step 1/3: container/volume reset for $CONTAINER..."
docker compose -f "$REPO_ROOT/docker-compose.yml" -f "$REPO_ROOT/docker-compose.app.yml" \
  rm -fsv "$SPA_NAME-spa" 2>/dev/null || true
# Volume name follows the docker-compose convention
VOL=$(docker volume ls --format '{{.Name}}' | grep -E "_${SPA_NAME}_node_modules\$|_${SPA_NAME}-spa-node_modules\$" || true)
if [ -n "$VOL" ]; then
  echo "  · removing volume: $VOL"
  docker volume rm "$VOL" 2>/dev/null || echo "    (volume in use; will recreate on next up)"
fi

# ── 3. Lockfile cleanup ───────────────────────────────────────
echo
echo "▶ Step 2/3: lockfile cleanup in $SPA_DIR..."
if [ "$HAS_PNPM_LOCK" = true ]; then
  cd "$SPA_DIR"
  if git ls-files --error-unmatch pnpm-lock.yaml >/dev/null 2>&1; then
    echo "  · git rm pnpm-lock.yaml"
    git rm pnpm-lock.yaml >/dev/null
  else
    echo "  · rm pnpm-lock.yaml (untracked)"
    rm -f pnpm-lock.yaml
  fi
  if ! grep -qx 'pnpm-lock.yaml' .gitignore 2>/dev/null; then
    echo 'pnpm-lock.yaml' >> .gitignore
    echo "  · added pnpm-lock.yaml to .gitignore"
  fi
else
  echo "  · no pnpm-lock.yaml to remove"
fi

# ── 4. npm cache clean + reinstall ─────────────────────────────
echo
echo "▶ Step 3/3: npm cache clean + reinstall..."
cd "$SPA_DIR"
npm cache clean --force >/dev/null 2>&1 || true
echo "  · cache cleaned"
npm install --prefer-offline 2>&1 | tail -3

# ── 5. Restart container ──────────────────────────────────────
echo
echo "▶ Restarting $CONTAINER with fresh node_modules volume..."
docker compose -f "$REPO_ROOT/docker-compose.yml" -f "$REPO_ROOT/docker-compose.app.yml" \
  up -d "$SPA_NAME-spa"

echo
echo "✓ Done. Container $CONTAINER is starting with a fresh node_modules volume."
echo "  Verify with: docker logs $CONTAINER --tail 20"
