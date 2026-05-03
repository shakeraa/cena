#!/usr/bin/env bash
# =============================================================================
# Cena Platform — safe bring-up script
#
# Wraps `docker compose ... up` with three guardrails:
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
#   3. Hot-reload by default — admin-api / student-api / actor-host /
#      sympy-sidecar are brought up against `dotnet watch` (or volume-
#      mounted Python) by default, so backend source edits propagate
#      into the running container without a manual rebuild. This was
#      the missing default that caused the 2026-05-03 staleness bug:
#      a fresh `/enhance-text` endpoint in source kept returning 404
#      from a baked prod-image container until someone ran
#      `docker compose stop && build && up -d` by hand. SPAs (admin-spa /
#      student-spa) already run from `dev.Dockerfile` with bind-mounts
#      via docker-compose.app.yml, independent of this overlay.
#
# Usage:
#   scripts/cena-up.sh                  # full stack, hot-reload (default)
#   scripts/cena-up.sh --prod-image     # full stack, baked prod images
#                                       # (use to test what `docker build`
#                                       # actually publishes — staging/prod
#                                       # parity check)
#   scripts/cena-up.sh admin-api        # one service, hot-reload
#   scripts/cena-up.sh --prod-image admin-api    # one service, prod image
#   scripts/cena-up.sh --force-build admin-api   # rebuild even if no src change
#   scripts/cena-up.sh --restart admin-api       # explicitly restart-only
#
# After a Docker Desktop restart (recovery path), always run this with no
# args — it brings the full stack up via the proper compose file order
# (postgres → redis → nats → migrator → admin-api/student-api/actor-host).
# =============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Parse flags
FORCE_BUILD=0
RESTART_ONLY=0
PROD_IMAGE=0
SVCS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --force-build) FORCE_BUILD=1; shift ;;
    --restart)     RESTART_ONLY=1; shift ;;
    --prod-image)  PROD_IMAGE=1;   shift ;;
    -h|--help)
      # Print everything between the first two ====...==== banner lines.
      awk '/^# =====+$/{n++; next} n==1 {sub(/^# ?/,""); print} n>=2{exit}' "$0"
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

# Compose overlay stack. Hot-reload is the default; --prod-image opts out
# to the production-image composition (no bind-mounts, no `dotnet watch`).
COMPOSE_ARGS=(
  -p cena
  -f docker-compose.yml
  -f docker-compose.app.yml
  -f config/docker-compose.observability.yml
)
if [[ $PROD_IMAGE -eq 0 ]]; then
  COMPOSE_ARGS+=( -f docker-compose.hotreload.yml )
fi

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

# ── 1b. Mode announce + transition warnings ──────────────────────────
# Surface the active composition mode so a user who expected prod-image
# (CI-parity test) doesn't silently get hot-reload, and vice-versa.
if [[ $PROD_IMAGE -eq 1 ]]; then
  echo "[cena-up] mode: PROD-IMAGE (baked images, no hot-reload, no source bind-mounts)"
else
  echo "[cena-up] mode: HOT-RELOAD (default; admin-api/student-api/actor-host run dotnet watch)"
fi

# If a backend container is currently running, detect a mode-switch
# (current image != target image). Compose's `up -d` will recreate it,
# but the user should know a 10-30s recreate is coming so they don't
# kill the script thinking it's hung.
mode_switch_warning() {
  local container="$1" expected_image_substr="$2"
  local current_image
  current_image="$(docker inspect "$container" --format '{{.Config.Image}}' 2>/dev/null || true)"
  [[ -z "$current_image" ]] && return 0   # not running, nothing to switch
  if [[ "$current_image" != *"$expected_image_substr"* ]]; then
    echo "[cena-up]   ↻ $container is running on '$current_image'; will be recreated to '*$expected_image_substr*' (~10-30s)"
  fi
}

if [[ $PROD_IMAGE -eq 0 ]]; then
  TARGET_TAG="dev"
else
  TARGET_TAG="latest"
fi
echo "[cena-up] checking for mode-switch on running containers..."
for c in cena-admin-api cena-student-api cena-actor-host; do
  mode_switch_warning "$c" ":$TARGET_TAG"
done

# ── 1c. First-run notice for missing dev images ──────────────────────
# Building cena-actor-host:dev (or any missing dev image) hits dotnet sdk
# layers + nuget restore for the first time — typically 2-3 minutes per
# image on Apple Silicon. Surface so the user doesn't think the script is
# wedged. Only warn in hot-reload mode; prod-image paths use latest tags
# that are normally pulled/built well before this script runs.
if [[ $PROD_IMAGE -eq 0 ]]; then
  missing=()
  for img in cena-admin-api:dev cena-student-api:dev cena-actor-host:dev; do
    if ! docker image inspect "$img" >/dev/null 2>&1; then
      missing+=( "$img" )
    fi
  done
  if [[ ${#missing[@]} -gt 0 ]]; then
    echo "[cena-up] first-run: will build missing dev image(s): ${missing[*]}"
    echo "[cena-up]   expect ~2-3 min per image (dotnet sdk layers + nuget restore)"
  fi
fi

# ── 2. Decide build vs restart ───────────────────────────────────────
# Map each service to the source paths whose changes warrant a rebuild.
# (Only Dockerfile/source-code changes need rebuild; compose env vars
# and compose-file edits are picked up by `restart`.)
#
# Implemented as a case-statement vs. `declare -A` because macOS's
# default /bin/bash is 3.2 which doesn't support associative arrays —
# `/usr/bin/env bash` may resolve to that on systems without a Homebrew
# bash on PATH ahead of /bin. case keeps us compatible.
svc_source_paths() {
  case "$1" in
    admin-api)   echo "src/api/Cena.Admin.Api src/api/Cena.Admin.Api.Host src/shared src/actors/Cena.Actors" ;;
    student-api) echo "src/api/Cena.Student.Api.Host src/shared src/actors/Cena.Actors" ;;
    actor-host)  echo "src/actors src/shared" ;;
    admin-spa)   echo "src/admin/full-version/dev.Dockerfile src/admin/full-version/package.json" ;;
    student-spa) echo "src/student/full-version/dev.Dockerfile src/student/full-version/package.json" ;;
    *)           echo "" ;;
  esac
}

needs_build() {
  local svc="$1"
  local paths
  paths="$(svc_source_paths "$svc")"
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
