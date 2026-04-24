#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Local k8s bring-up (M1, k3d-based)
# Task: t_9c67e81050d6 — replicated-pod pressure-test scaffold
# =============================================================================
# Brings up a k3d cluster named `cena-local`, builds + imports Cena images,
# creates dev secrets pointing at the docker-compose dev infra on
# host.docker.internal, installs the Helm chart with values-local.yaml.
#
# Idempotent: re-running after cluster exists skips creation and does a
# `helm upgrade --install` instead of a fresh install.
#
# Keeps docker-compose untouched — the two stacks coexist. The k3d pods
# reach postgres/redis/nats/firebase-emulator on host.docker.internal.
# =============================================================================

set -euo pipefail

CLUSTER_NAME="${CLUSTER_NAME:-cena-local}"
NAMESPACE="${NAMESPACE:-cena-local}"
RELEASE_NAME="${RELEASE_NAME:-cena}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CHART_DIR="${REPO_ROOT}/deploy/helm/cena"
VALUES_FILE="${CHART_DIR}/values-local.yaml"
IMAGE_TAG="local"

# Dev-only credentials (mirrors docker-compose.yml) — NOT secrets.
DEV_PG_USER="cena"
DEV_PG_PASSWORD="cena_dev_password"
DEV_PG_DB="cena"
DEV_REDIS_PASSWORD="cena_dev_redis"
DEV_NATS_USER="cena_api_user"
DEV_NATS_PASSWORD="dev_api_pass"

# -----------------------------------------------------------------------------
# Output helpers
# -----------------------------------------------------------------------------
step()   { printf "\n\033[1;34m==> %s\033[0m\n" "$*"; }
ok()     { printf "    \033[32m✓\033[0m %s\n" "$*"; }
warn()   { printf "    \033[33m!\033[0m %s\n" "$*"; }
fail()   { printf "\n\033[1;31m✗ %s\033[0m\n" "$*" >&2; exit 1; }

# -----------------------------------------------------------------------------
# Preflight
# -----------------------------------------------------------------------------
preflight() {
    step "Preflight"
    for t in k3d kubectl helm docker; do
        command -v "$t" >/dev/null 2>&1 || fail "Missing: $t. Install via: brew install $t"
        ok "$t: $(command -v "$t")"
    done

    # Docker running?
    docker info >/dev/null 2>&1 || fail "Docker daemon not running."
    ok "Docker daemon reachable"

    # docker-compose dev infra running? We rely on the three infra containers.
    local missing=()
    for c in cena-postgres cena-redis cena-nats; do
        docker inspect -f '{{.State.Running}}' "$c" 2>/dev/null | grep -q true \
            || missing+=("$c")
    done
    if [[ ${#missing[@]} -gt 0 ]]; then
        fail "docker-compose dev infra containers not running: ${missing[*]}
    Bring them up first: cd \"${REPO_ROOT}\" && docker compose up -d postgres redis nats"
    fi
    ok "docker-compose infra up (postgres, redis, nats)"

    # firebase-emulator is optional for local — warn if missing.
    if ! docker inspect -f '{{.State.Running}}' cena-firebase-emulator 2>/dev/null | grep -q true; then
        warn "cena-firebase-emulator not running — admin-api will run in local-only mode (expected per REV-001)"
    fi

    # Ports 5050 + 5052 must be free — they will be k3d's NodePort
    # mappings for student-api and admin-api. If the docker-compose app
    # containers are already bound there, k3d will fail to create the
    # loadbalancer container. The two stacks share infra (postgres /
    # redis / nats) but not app ports; pick one at a time.
    local port_conflicts=()
    for port in 5050 5052; do
        # lsof is universal on macOS; Docker also reports via `docker ps`.
        if lsof -iTCP -sTCP:LISTEN -n -P 2>/dev/null | grep -q ":${port}[^0-9]"; then
            port_conflicts+=("$port")
        fi
    done
    if [[ ${#port_conflicts[@]} -gt 0 ]]; then
        fail "Host ports already in use: ${port_conflicts[*]}
    These are the NodePort mappings k3d needs for student-api (5050) and admin-api (5052).
    Stop the conflicting docker-compose app containers (infra stays up):

      docker compose stop admin-api student-api actor-host admin-spa student-spa sympy-sidecar

    Then re-run this script. To bring docker-compose apps back later:

      docker compose start admin-api student-api actor-host admin-spa student-spa sympy-sidecar

    (Or override CLUSTER_NAME + edit values-local.yaml nodePort fields to use a different pair.)"
    fi
    ok "host ports 5050 / 5052 are free"
}

# -----------------------------------------------------------------------------
# Cluster
# -----------------------------------------------------------------------------
ensure_cluster() {
    step "k3d cluster '${CLUSTER_NAME}'"
    if k3d cluster list -o json 2>/dev/null | grep -q "\"name\":\"${CLUSTER_NAME}\""; then
        ok "cluster already exists — reusing"
    else
        # --port maps host port → cluster LoadBalancer (traefik) → NodePort.
        # Student on 5050, admin on 5052 (mirrors docker-compose host ports).
        # --api-port 6445 keeps the k3d API off the default 6443 in case the
        # user has something else on it.
        # --k3s-arg '--disable=traefik@server:*' keeps things lean; we use
        # NodePort directly, no Ingress.
        k3d cluster create "${CLUSTER_NAME}" \
            --api-port 6445 \
            --port "5050:30050@loadbalancer" \
            --port "5052:30052@loadbalancer" \
            --k3s-arg '--disable=traefik@server:*' \
            --wait
        ok "cluster created"
    fi

    kubectl config use-context "k3d-${CLUSTER_NAME}" >/dev/null
    kubectl create namespace "${NAMESPACE}" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    ok "namespace '${NAMESPACE}' ready"
}

# -----------------------------------------------------------------------------
# Images
# -----------------------------------------------------------------------------
build_and_import_images() {
    step "Build + import Cena images into k3d"

    declare -A DOCKERFILES=(
        [cena-db-migrator]="src/api/Cena.Db.Migrator/Dockerfile"
        [cena-actors-host]="src/actors/Cena.Actors.Host/Dockerfile"
        [cena-student-api]="src/api/Cena.Student.Api.Host/Dockerfile"
        [cena-admin-api]="src/api/Cena.Admin.Api.Host/Dockerfile"
    )

    for svc in "${!DOCKERFILES[@]}"; do
        local df="${DOCKERFILES[$svc]}"
        local tag="ghcr.io/cena-platform/${svc}:${IMAGE_TAG}"
        step "  build ${svc} ← ${df}"
        docker build \
            --platform linux/arm64 \
            -f "${REPO_ROOT}/${df}" \
            -t "${tag}" \
            "${REPO_ROOT}"
        ok "built ${tag}"

        k3d image import "${tag}" -c "${CLUSTER_NAME}" >/dev/null
        ok "imported ${tag} into k3d"
    done
}

# -----------------------------------------------------------------------------
# Secrets (dev-only, not committed)
# -----------------------------------------------------------------------------
create_secrets() {
    step "Create dev secrets"

    local pg_host="host.docker.internal"
    local pg_port=5433
    local pg_base="Host=${pg_host};Port=${pg_port};Database=${DEV_PG_DB};Username=${DEV_PG_USER};Password=${DEV_PG_PASSWORD}"

    kubectl -n "${NAMESPACE}" create secret generic cena-db-credentials \
        --from-literal=connection-string-migrator="${pg_base}" \
        --from-literal=connection-string-student="${pg_base}" \
        --from-literal=connection-string-admin="${pg_base}" \
        --from-literal=connection-string-actors="${pg_base}" \
        --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    ok "cena-db-credentials → host.docker.internal:${pg_port}"

    kubectl -n "${NAMESPACE}" create secret generic cena-redis-credentials \
        --from-literal=connection-string="host.docker.internal:6380,password=${DEV_REDIS_PASSWORD},abortConnect=false" \
        --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    ok "cena-redis-credentials → host.docker.internal:6380"

    kubectl -n "${NAMESPACE}" create secret generic cena-nats-credentials \
        --from-literal=connection-string="nats://host.docker.internal:4222" \
        --from-literal=nats-user="${DEV_NATS_USER}" \
        --from-literal=nats-password="${DEV_NATS_PASSWORD}" \
        --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    ok "cena-nats-credentials → host.docker.internal:4222"

    # Firebase: stub empty creds so the admin-api starts in local-only
    # mode (REV-001 dev behaviour). Real creds never ship to local.
    kubectl -n "${NAMESPACE}" create secret generic cena-firebase-credentials \
        --from-literal=credentials.json='{}' \
        --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    ok "cena-firebase-credentials (stub — dev local-only mode)"
}

# -----------------------------------------------------------------------------
# Helm
# -----------------------------------------------------------------------------
helm_install() {
    step "Helm upgrade --install ${RELEASE_NAME}"
    helm upgrade --install "${RELEASE_NAME}" "${CHART_DIR}" \
        --namespace "${NAMESPACE}" \
        --values "${VALUES_FILE}" \
        --wait --timeout 5m
    ok "chart installed"
}

# -----------------------------------------------------------------------------
# Post-install hints
# -----------------------------------------------------------------------------
report() {
    step "Cluster state"
    kubectl -n "${NAMESPACE}" get pods -o wide
    echo
    cat <<EOF
Next steps
  kubectl -n ${NAMESPACE} get pods -w
  kubectl -n ${NAMESPACE} logs -l app.kubernetes.io/component=actors --tail=100 -f
  open http://localhost:5050/health/ready       # student
  open http://localhost:5052/health/ready       # admin

Scale actor replicas
  kubectl -n ${NAMESPACE} scale deploy/cena-actors --replicas=5

Run the emulator against the cluster (safe load — do NOT exceed 20× speed)
  docker compose -f docker-compose.yml -f docker-compose.app.yml \\
    --profile emulator run --rm \\
    -e EMU_STUDENTS=200 -e EMU_SPEED=10 -e EMU_DURATION=120 emulator

Teardown
  scripts/k8s-local-down.sh
EOF
}

main() {
    preflight
    ensure_cluster
    build_and_import_images
    create_secrets
    helm_install
    report
}

main "$@"
