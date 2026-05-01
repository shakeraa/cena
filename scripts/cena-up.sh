#!/usr/bin/env bash
# =============================================================================
# Cena Platform — safe bring-up script
#
# Wraps `docker compose ... up` with two guardrails added after the
# 2026-05-01 daemon-wedge incidents:
#
#   1. Daemon liveness pre-check — refuse to issue further commands when
#      the Docker daemon is hung. On Apple Silicon Docker Desktop, the
#      vpnkit/lima networking stack wedges under churn; piling more
#      `docker` calls into a hung daemon makes the wedge worse and burns
#      host CPU. Better: stop, tell the operator to restart Docker
#      Desktop, exit with a clear error.
#
#   2. Smart rebuild-vs-restart choice — `docker compose up --build` is
#      heavy (BuildKit + dotnet restore + virtiofs file copy). When you
#      only changed env vars or compose config (not Dockerfile/source),
#      `docker compose restart <svc>` is seconds vs. minutes and doesn't
#      stress BuildKit. This script defaults to restart if the relevant
#      service's source tree has no uncommitted/unstaged changes.
#
# Usage:
#   scripts/cena-up.sh              # bring up everything (full stack)
#   scripts/cena-up.sh admin-api    # one service
#   scripts/cena-up.sh --force-build admin-api    # rebuild even if no source change
#   scripts/cena-up.sh --restart admin-api        # explicitly restart-only
#
# After a Docker Desktop restart (recovery path), always run this with no
# args — it brings the full stack up via the proper compose file order
# (postgres → redis → nats → migrator → admin-api/student-api/actor-host).
# =============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

COMPOSE_ARGS=(
  -p cena
  -f docker-compose.yml
  -f docker-compose.app.yml
  -f config/docker-compose.observability.yml
)

# Parse flags
FORCE_BUILD=0
RESTART_ONLY=0
SVCS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --force-build) FORCE_BUILD=1; shift ;;
    --restart)     RESTART_ONLY=1; shift ;;
    -h|--help)
      sed -n '/^# ===/,/^# ===$/p' "$0" | sed 's/^# \?//' | head -40
      exit 0
      ;;
    -*)
      echo "[cena-up] unknown flag: $1" >&2
      exit 2
      ;;
    *)
      SVCS+=("$1"); shift ;;
  esac
done

# ── 1. Daemon liveness pre-check ─────────────────────────────────────
echo "[cena-up] probing Docker daemon..."
if ! timeout 5 docker version --format '{{.Server.Version}}' >/dev/null 2>&1; then
  cat >&2 <<'ERRMSG'
[cena-up] ERROR: Docker daemon is unresponsive (timeout 5s on `docker version`).

This is the Apple Silicon Docker Desktop wedge — vpnkit/lima networking
gets into a bad state after heavy churn. Piling more docker commands on
the hung daemon makes it worse.

Recovery:
  1. Click the Docker Desktop whale icon in the menu bar → Restart.
     (or: osascript -e 'quit app "Docker Desktop"' && open -a "Docker Desktop")
  2. Wait 60-90 seconds for "Engine running".
  3. Re-run this script (no args) to bring the full stack back up
     in the correct dependency order.
ERRMSG
  exit 1
fi
echo "[cena-up] daemon OK"

# ── 2. Decide build vs restart ───────────────────────────────────────
# Map each service to the source paths whose changes warrant a rebuild.
# (Only Dockerfile/source-code changes need rebuild; compose env vars
# and compose-file edits are picked up by `restart`.)
declare -A SVC_SOURCE_PATHS=(
  [admin-api]="src/api/Cena.Admin.Api src/api/Cena.Admin.Api.Host src/shared src/actors/Cena.Actors"
  [student-api]="src/api/Cena.Student.Api.Host src/shared src/actors/Cena.Actors"
  [actor-host]="src/actors src/shared"
  [admin-spa]="src/admin/full-version/dev.Dockerfile src/admin/full-version/package.json"
  [student-spa]="src/student/full-version/dev.Dockerfile src/student/full-version/package.json"
)

needs_build() {
  local svc="$1"
  local paths="${SVC_SOURCE_PATHS[$svc]:-}"
  [[ -z "$paths" ]] && return 1   # unknown service → no rebuild
  # Treat both staged and unstaged changes as triggers.
  if ! git diff --quiet -- $paths 2>/dev/null; then
    return 0
  fi
  if ! git diff --cached --quiet -- $paths 2>/dev/null; then
    return 0
  fi
  return 1
}

# ── 3. Execute ───────────────────────────────────────────────────────
if [[ ${#SVCS[@]} -eq 0 ]]; then
  # Full-stack bring-up — always uses `up -d` (no --build by default).
  # This is the canonical post-Docker-restart recovery command.
  echo "[cena-up] bringing full stack up (compose up -d, no rebuild)"
  exec docker compose "${COMPOSE_ARGS[@]}" up -d
fi

# Per-service path
for svc in "${SVCS[@]}"; do
  if [[ $RESTART_ONLY -eq 1 ]]; then
    echo "[cena-up] $svc → restart (--restart)"
    docker compose "${COMPOSE_ARGS[@]}" restart "$svc"
  elif [[ $FORCE_BUILD -eq 1 ]]; then
    echo "[cena-up] $svc → rebuild (--force-build)"
    docker compose "${COMPOSE_ARGS[@]}" up -d --build "$svc"
  elif needs_build "$svc"; then
    echo "[cena-up] $svc → source changed, rebuilding"
    docker compose "${COMPOSE_ARGS[@]}" up -d --build "$svc"
  else
    echo "[cena-up] $svc → no source change, restart only (use --force-build to override)"
    docker compose "${COMPOSE_ARGS[@]}" restart "$svc"
  fi
done

echo "[cena-up] done"
