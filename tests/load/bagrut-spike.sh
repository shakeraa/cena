#!/usr/bin/env bash
# =============================================================================
# Cena Platform — Bagrut-spike simulation + chaos injection (PRR-432)
#
# Drives the PRR-053 exam-morning traffic shape: 0 → 500 VUs in 5 minutes,
# sustain 500 VUs for 10 minutes, ramp down to 0 in 2 minutes. During the
# sustain phase, inject chaos: kill an actor-host replica, stop Postgres
# for 30s, saturate NATS. Capture all metrics to tests/load/reports/.
#
# Prerequisites:
#   - k6 installed (brew install k6)
#   - k3d cluster up (./tests/load/k3d-cluster.yaml)
#   - actor-host deployed (tests/load/actor-host-deployment.yaml)
#   - CENA_LOAD_TOKEN pre-seeded in env
#
# Output: tests/load/reports/bagrut-spike-${timestamp}/
# =============================================================================

set -euo pipefail

TIMESTAMP=$(date -u +%Y%m%dT%H%M%SZ)
REPORT_DIR="tests/load/reports/bagrut-spike-${TIMESTAMP}"
mkdir -p "${REPORT_DIR}"

if [[ -z "${CENA_LOAD_TOKEN:-}" ]]; then
  echo "error: CENA_LOAD_TOKEN must be set" >&2
  exit 1
fi

# Guard rails: never run against prod.
if [[ "${CENA_BASE_URL:-http://localhost:5050}" == *"cena.io"* ]] \
   || [[ "${CENA_BASE_URL:-}" == *"prod"* ]]; then
  echo "error: refusing to run load test against what looks like a production URL: ${CENA_BASE_URL}" >&2
  exit 2
fi

echo "[bagrut-spike] Starting at ${TIMESTAMP}"
echo "[bagrut-spike] Target: ${CENA_BASE_URL:-http://localhost:5050}"
echo "[bagrut-spike] Report dir: ${REPORT_DIR}"

# --- Phase 1: ramp + sustain (k6 runs in foreground; chaos in background). ---
k6 run \
  --stage 5m:500,10m:500,2m:0 \
  --summary-export "${REPORT_DIR}/summary.json" \
  --out "json=${REPORT_DIR}/metrics.ndjson" \
  tests/load/session-load.js &
K6_PID=$!

# --- Phase 2: chaos during the 10-min sustain. ---
# Wait for ramp to complete before starting chaos.
sleep 300

echo "[chaos] Sustain phase reached. Injecting faults."

inject_actor_kill() {
  if command -v kubectl >/dev/null 2>&1; then
    local target
    target=$(kubectl get pod -n cena-capacity -l app=cena-actor-host -o jsonpath='{.items[0].metadata.name}')
    if [[ -n "$target" ]]; then
      echo "[chaos] Killing actor-host pod: $target"
      kubectl delete pod -n cena-capacity "$target" --grace-period=0 --force
    fi
  else
    echo "[chaos] kubectl unavailable; skipping actor-host kill"
  fi
}

inject_postgres_stop() {
  if docker ps --format '{{.Names}}' | grep -q '^cena-postgres$'; then
    echo "[chaos] Stopping postgres for 30s"
    docker stop cena-postgres || true
    sleep 30
    docker start cena-postgres || true
  else
    echo "[chaos] cena-postgres container not found; skipping"
  fi
}

inject_nats_saturate() {
  echo "[chaos] Saturating NATS queue with 10k synthetic messages"
  if command -v nats >/dev/null 2>&1; then
    for i in $(seq 1 10000); do
      nats pub cena.chaos.saturate "synthetic-$i" > /dev/null 2>&1 || true
    done
  else
    echo "[chaos] nats CLI unavailable; skipping saturation"
  fi
}

# Stagger chaos events ~2m apart so the cluster has room to recover between.
inject_actor_kill
sleep 120
inject_postgres_stop
sleep 120
inject_nats_saturate

# Wait for k6 to finish the load run.
wait ${K6_PID}
K6_EXIT=$?

echo "[bagrut-spike] k6 exit code: ${K6_EXIT}"
echo "[bagrut-spike] Report written to: ${REPORT_DIR}"

# Capture a kubectl snapshot of pod events for post-mortem.
if command -v kubectl >/dev/null 2>&1; then
  kubectl get events -n cena-capacity --sort-by=.metadata.creationTimestamp \
    > "${REPORT_DIR}/k8s-events.log" 2>&1 || true
  kubectl get pods -n cena-capacity -o wide \
    > "${REPORT_DIR}/k8s-pods.log" 2>&1 || true
fi

exit ${K6_EXIT}
