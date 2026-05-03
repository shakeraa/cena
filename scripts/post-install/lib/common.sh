#!/usr/bin/env bash
# =============================================================================
# Cena post-install task library
#
# Shared helpers for every task under scripts/post-install/tasks/.
# Source this at the top of each task:
#
#   #!/usr/bin/env bash
#   set -euo pipefail
#   source "$(dirname "$0")/../lib/common.sh"
#   post_install_begin "my-task"
#   ... do work ...
#   post_install_ok "my-task"
# =============================================================================

set -euo pipefail

# Colours — disabled when stdout is not a TTY so log parsers stay clean.
if [ -t 1 ]; then
  CENA_C_RED=$'\033[0;31m'
  CENA_C_GREEN=$'\033[0;32m'
  CENA_C_YELLOW=$'\033[0;33m'
  CENA_C_CYAN=$'\033[0;36m'
  CENA_C_RESET=$'\033[0m'
else
  CENA_C_RED=""; CENA_C_GREEN=""; CENA_C_YELLOW=""; CENA_C_CYAN=""; CENA_C_RESET=""
fi

cena_log() {
  printf '%s[POST_INSTALL]%s %s\n' "$CENA_C_CYAN" "$CENA_C_RESET" "$*" >&2
}

cena_warn() {
  printf '%s[POST_INSTALL WARN]%s %s\n' "$CENA_C_YELLOW" "$CENA_C_RESET" "$*" >&2
}

cena_err() {
  printf '%s[POST_INSTALL FAIL]%s %s\n' "$CENA_C_RED" "$CENA_C_RESET" "$*" >&2
}

# Millisecond-resolution monotonic clock — use as a difference, not an absolute.
cena_now_ms() {
  # date +%N is 0 on macOS so fall back to perl for portability.
  if date +%N 2>/dev/null | grep -qv '^N'; then
    date +%s%3N
  else
    perl -MTime::HiRes=time -e 'printf "%d\n", time()*1000'
  fi
}

CENA_TASK_BEGIN_MS=0
CENA_TASK_SLUG=""

post_install_begin() {
  CENA_TASK_SLUG="$1"
  CENA_TASK_BEGIN_MS=$(cena_now_ms)
  cena_log "task=$CENA_TASK_SLUG status=begin"
}

post_install_ok() {
  local slug="${1:-$CENA_TASK_SLUG}"
  local end_ms
  end_ms=$(cena_now_ms)
  local dur=$((end_ms - CENA_TASK_BEGIN_MS))
  printf '%s[POST_INSTALL]%s task=%s status=ok duration_ms=%d\n' \
    "$CENA_C_GREEN" "$CENA_C_RESET" "$slug" "$dur"
}

post_install_skip() {
  local slug="${1:-$CENA_TASK_SLUG}"
  local reason="${2:-no reason}"
  printf '%s[POST_INSTALL]%s task=%s status=skip reason="%s"\n' \
    "$CENA_C_YELLOW" "$CENA_C_RESET" "$slug" "$reason"
}

post_install_fail() {
  local slug="${1:-$CENA_TASK_SLUG}"
  local reason="${2:-no reason}"
  cena_err "task=$slug status=fail reason=\"$reason\""
  exit 1
}

# Require an env var; fail the task if unset.
cena_require_env() {
  local name="$1"
  if [ -z "${!name:-}" ]; then
    post_install_fail "${CENA_TASK_SLUG:-unknown}" "$name must be set"
  fi
}

# Is this a dry run?
cena_is_dry_run() {
  [ "${DRY_RUN:-0}" = "1" ]
}

# curl wrapper that picks up the admin bearer token automatically and fails
# the task on non-2xx.
cena_admin_curl() {
  cena_require_env CENA_ADMIN_URL
  cena_require_env CENA_ADMIN_TOKEN
  local method="$1"; shift
  local path="$1"; shift
  if cena_is_dry_run; then
    cena_log "DRY_RUN: would ${method} ${CENA_ADMIN_URL}${path}"
    return 0
  fi
  curl --fail-with-body -sS \
    -X "$method" \
    -H "Authorization: Bearer $CENA_ADMIN_TOKEN" \
    -H 'Content-Type: application/json' \
    "$@" \
    "${CENA_ADMIN_URL}${path}"
}

# Load an env file if present (default ./.env).
cena_load_env_file() {
  local file="${1:-.env}"
  if [ -f "$file" ]; then
    set -a
    # shellcheck disable=SC1090
    . "$file"
    set +a
    cena_log "loaded env file: $file"
  fi
}
