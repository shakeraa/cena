# Cena load + capacity harness (EPIC-PRR-K)

Versioned k6 scripts + k3d topology + chaos driver for the PRR-053
Bagrut-morning traffic plan. Goal: turn three "we don't know" questions
(from the EPIC-PRR-K body) into measured numbers before Launch.

## Files

- `k3d-cluster.yaml` — k3d cluster config (1 control plane + 2 workers)
- `actor-host-deployment.yaml` — Deployment + Service + RBAC for running the
  real `KubernetesProvider` (not `TestProvider`) locally
- `session-load.js` — k6 Virtual-User simulator: register→session→answer×5→end
- `bagrut-spike.sh` — Bagrut-morning ramp + chaos-injection driver
- `reports/` — per-run output (`.gitignore`'d; created on first run)

## Quick start

```bash
# 1. Install prereqs
brew install k6 k3d kubectl

# 2. Stand up the cluster (≈60s)
k3d cluster create --config tests/load/k3d-cluster.yaml

# 3. Build + push a dev actor-host image into the k3d local registry
docker build -t k3d-cena-capacity-registry:5000/cena-actor-host:dev \
  -f src/actors/Cena.Actors.Host/Dockerfile .
docker push k3d-cena-capacity-registry:5000/cena-actor-host:dev

# 4. Deploy
kubectl apply -f tests/load/actor-host-deployment.yaml
kubectl get pods -n cena-capacity -w   # wait for 2/2 ready

# 5. Seed an auth token (one-time)
export CENA_LOAD_TOKEN=$(./scripts/load/seed-token.sh)

# 6. Run the spike
./tests/load/bagrut-spike.sh
```

Results land in `tests/load/reports/bagrut-spike-${timestamp}/`:
- `summary.json` — k6 summary (p95 latencies, error rates)
- `metrics.ndjson` — full per-point metrics for post-hoc analysis
- `k8s-events.log` — kubectl events during the run (chaos-recovery visibility)
- `k8s-pods.log` — final pod states

## SLO thresholds (launch-gate)

Encoded in `session-load.js` — a run that breaches any threshold exits
non-zero, suitable for CI:

| Threshold | Target |
|-----------|--------|
| `session_start` p95 | < 2000ms |
| `answer_submit` p95 | < 1000ms |
| `http_req_failed` rate | < 1% |

If any breach, read the chaos phase timeline in `k8s-events.log` — most
often it's Postgres-restart recovery taking longer than the connection-pool
backoff window.

## Related

- [EPIC-PRR-K](../../tasks/pre-release-review/EPIC-PRR-K-actor-cluster-capacity-validation.md) — parent epic
- [TASK-PRR-053](../../tasks/pre-release-review/TASK-PRR-053-exam-day-capacity-plan-bagrut-traffic-forecast.md) — traffic forecast this validates
- [TASK-PRR-231](../../tasks/pre-release-review/TASK-PRR-231-amend-capacity-plan-sat-pet.md) — SAT/PET extension
