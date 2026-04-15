#!/usr/bin/env bash
# =============================================================================
# Cena post-install task runner
#
# Executes every task under scripts/post-install/tasks/ in numeric order,
# stopping on the first failure. Optional single-task selection via the
# first positional arg (number or slug substring).
# =============================================================================

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=./lib/common.sh
source "$HERE/lib/common.sh"

cena_load_env_file "${CENA_ENV_FILE:-.env}"

SELECTOR="${1:-}"

# Portable array-fill (bash 3.2 on macOS has no `mapfile`).
ALL_TASKS=()
while IFS= read -r line; do
  ALL_TASKS+=("$line")
done < <(find "$HERE/tasks" -maxdepth 1 -type f -name '*.sh' -print | sort)

if [ "${#ALL_TASKS[@]}" -eq 0 ]; then
  cena_warn "no tasks found under $HERE/tasks — nothing to do"
  exit 0
fi

run_task() {
  local task_path="$1"
  local name
  name="$(basename "$task_path")"
  cena_log "--- $name ---"
  if [ ! -x "$task_path" ]; then
    cena_warn "$name is not executable, chmod +x would help — running via bash"
    bash "$task_path"
  else
    "$task_path"
  fi
}

matches_selector() {
  local path="$1"
  local sel="$2"
  [ -z "$sel" ] && return 0
  local name
  name="$(basename "$path" .sh)"
  [[ "$name" == "$sel"* ]] || [[ "$name" == *"$sel"* ]]
}

ran=0
for t in "${ALL_TASKS[@]}"; do
  if matches_selector "$t" "$SELECTOR"; then
    run_task "$t"
    ran=$((ran + 1))
  fi
done

if [ "$ran" -eq 0 ]; then
  cena_err "selector '$SELECTOR' matched no task"
  exit 2
fi

cena_log "done ($ran task(s) completed)"
