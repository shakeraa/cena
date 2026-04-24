#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Local k8s teardown
# Task: t_9c67e81050d6
# =============================================================================
# Deletes the k3d cluster created by scripts/k8s-local-up.sh. Does NOT
# touch docker-compose — that stack is managed independently.
# =============================================================================

set -euo pipefail

CLUSTER_NAME="${CLUSTER_NAME:-cena-local}"

step() { printf "\n\033[1;34m==> %s\033[0m\n" "$*"; }
ok()   { printf "    \033[32m✓\033[0m %s\n" "$*"; }

command -v k3d >/dev/null 2>&1 || { echo "k3d not found" >&2; exit 1; }

step "k3d cluster delete ${CLUSTER_NAME}"
if k3d cluster list -o json 2>/dev/null | grep -q "\"name\":\"${CLUSTER_NAME}\""; then
    k3d cluster delete "${CLUSTER_NAME}"
    ok "cluster deleted"
else
    ok "cluster not present — nothing to delete"
fi

step "docker-compose stack is untouched"
ok "postgres / redis / nats / firebase-emulator still running if they were up"
