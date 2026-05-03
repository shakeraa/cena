# TASK-PRR-430: Local k3d cluster running production-parity KubernetesProvider

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-sre
**Source docs**: [src/actors/Cena.Actors.Host/Program.cs:489-537](../../src/actors/Cena.Actors.Host/Program.cs), [docker-compose.app.yml](../../docker-compose.app.yml)
**Assignee hint**: kimi-coder + SRE / platform-eng review
**Tags**: source=actor-cluster-capacity, epic=epic-prr-k, priority=p0, k8s, sre, production-parity
**Status**: Ready
**Tier**: launch
**Epic**: [EPIC-PRR-K](EPIC-PRR-K-actor-cluster-capacity-validation.md)

---

## Why

Today `docker-compose.app.yml:305` runs exactly one actor-host container with `container_name: cena-actor-host`. Program.cs (lines 489-503) picks TestProvider in Dev and KubernetesProvider in Prod. The KubernetesProvider code path has **never been exercised outside production**. That is the exact shape of a bug no developer catches until the first post-deploy pod rollover.

The question this task answers: can a developer reproduce the real cluster, discovery, partition, and identity-lookup behavior on a laptop, running the same Program.cs code path that Prod runs?

The answer must be yes. Not "yes, sort of, with a TestProvider." Yes running the real `Proto.Cluster.Kubernetes.KubernetesProvider` with the real `PartitionIdentityLookup`, the real NATS discovery where applicable, the real Redis session store, the real Postgres Marten event store. The only delta is replica count and cluster size.

## How

### Architecture

- **k3d** (k3s-in-Docker) as the local k8s cluster. k3d was chosen over minikube + kind because it boots in <30s, runs in the existing Docker Desktop, and shares networking with the existing `docker-compose.app.yml` services through a bridge network. Alternatives considered + rejected: `docker-compose up --scale actor-host=N` (blocked by container_name collision + no discovery mechanism), `minikube` (slower boot, heavier resource footprint), `kind` (no built-in ingress, would need extra wiring).
- **Helm chart** at `deploy/helm/cena-actor-host/` with templates for Deployment, Service, ServiceAccount, Role, RoleBinding (KubernetesProvider needs pod-list permission), HorizontalPodAutoscaler, PodDisruptionBudget. Same chart goes to staging + prod; only `values.yaml` differs per environment.
- **Cluster topology**: 1 k3d control-plane node + 2-3 worker nodes + N actor-host replicas + the existing docker-compose services (postgres, redis, nats, neo4j) exposed to k3d via hostNetwork or bridge.
- **Environment convergence**: remove the `container_name: cena-actor-host` hardcode from docker-compose.app.yml (replaced by k3d + Helm for the actor-host; other services stay in compose until a separate epic migrates them).

### Program.cs changes

- Remove the `TestProvider` branch at line 499. Replace with a new `DevSeedNodeOrKubernetesProvider` that reads `Cluster:Provider` config — `kubernetes` in all environments except deep unit tests, which use `InMemAgent` directly via TestServer + WebApplicationFactory.
- Validate that every Actors integration test running today still passes after removing the TestProvider branch from the Host. Some tests may need to adopt the new in-process-cluster-via-TestProvider pattern via a dedicated `Cena.Actors.TestHost` project instead of the production Host.
- No "for now" shims, no "TODO port this later" comments. If something breaks, fix it. Per memory "No stubs — production grade."

### Helm chart essentials

- **RBAC**: ServiceAccount `cena-actor-host` with a Role granting `list+get pods` in its own namespace. This is what `Proto.Cluster.Kubernetes.KubernetesProvider` needs to discover peers; no cluster-wide permissions.
- **Readiness probe**: hits a new `/cluster/ready` endpoint that returns 200 only after the replica has joined the cluster and received initial partition assignment. Wire this endpoint in Program.cs — reuse the existing `HealthChecks` pattern.
- **PodDisruptionBudget**: `minAvailable: 1` (single-replica in Dev is fine; multi-replica in Prod). Blocks k8s from evicting the last replica during voluntary disruption (node drain).
- **HPA**: scaled on CPU + the actor-host's custom metric `actor_activations_per_second` exposed via OpenTelemetry. Wire the metric in the cluster code that responds to `ClusterPidCache` activations.
- **Init container or one-shot Job**: waits for Postgres + Redis + NATS before actor-host pods start. Prevents Proto.Cluster from crashing on startup when dependencies aren't ready.

### Developer workflow

```
k3d cluster create cena-dev --agents 2 --port "8090:8090@loadbalancer" --registry-create cena-dev-registry
docker-compose -f docker-compose.app.yml up postgres redis nats neo4j
docker build -t cena-actor-host:dev -f src/actors/Cena.Actors.Host/Dockerfile .
k3d image import cena-actor-host:dev -c cena-dev
helm install cena-actor-host deploy/helm/cena-actor-host --values deploy/helm/cena-actor-host/values-dev.yaml --set replicaCount=3
kubectl port-forward svc/cena-actor-host 5050:5050
```

A single `scripts/dev-cluster-up.sh` wraps this. A `dev-cluster-down.sh` unwinds it. Idempotent; safe to run repeatedly.

### Observability

The existing `docker-compose.observability.yml` Grafana + OTLP + Prometheus stack continues to receive metrics/traces from the k3d pods (OTLP collector in k3d forwards to the Grafana host in compose). Dashboards at `infra/observability/dashboards/cena-actors.json` get new panels for: replica count, partition-owner distribution, actor-activation-per-second, rebalance-events-per-minute.

## Files

- `deploy/helm/cena-actor-host/` (new)
  - `Chart.yaml`, `values.yaml`, `values-dev.yaml`, `values-staging.yaml`, `values-prod.yaml`
  - `templates/deployment.yaml`, `service.yaml`, `serviceaccount.yaml`, `role.yaml`, `rolebinding.yaml`, `hpa.yaml`, `pdb.yaml`
- `scripts/dev-cluster-up.sh` + `scripts/dev-cluster-down.sh` (new)
- `src/actors/Cena.Actors.Host/Program.cs` — replace TestProvider branch with the new provider-factory logic
- `src/actors/Cena.Actors.Host/ClusterReadinessEndpoint.cs` (new) — `/cluster/ready` probe handler
- `src/actors/Cena.Actors.Host/ClusterActivationMetrics.cs` (new) — OpenTelemetry metric for HPA
- `src/actors/Cena.Actors.TestHost/` (new, maybe) — if integration tests need an in-process cluster after TestProvider removal from the Host
- `docker-compose.app.yml` — remove `container_name: cena-actor-host` and the actor-host service (moves to k3d)
- `docs/ops/runbooks/local-cluster-setup.md` (new)
- `infra/observability/dashboards/cena-actors.json` — new panels

## Definition of Done

- `scripts/dev-cluster-up.sh` creates a k3d cluster with 3 actor-host replicas that successfully form a Proto.Cluster via the real KubernetesProvider. Verified by log output "Member joined cluster" ×3 and by `kubectl exec` into a pod and curl-ing its own `/cluster/members` endpoint showing all 3 peers.
- Partition rebalance verified by `kubectl delete pod cena-actor-host-<n>` and observing a peer take over the dead pod's partitions within 5s (configurable threshold).
- All existing `Cena.Actors.Tests` pass against the refactored Program.cs. No "TestProvider in production" crutch remains.
- Full `Cena.Actors.sln` builds cleanly (`dotnet build src/actors/Cena.Actors.sln`).
- `docs/ops/runbooks/local-cluster-setup.md` covers the developer onboarding path + common failure modes (image not loaded to k3d, RBAC missing, Postgres not reachable from k3d network).
- Grafana dashboard shows replica count, partition distribution, activation rate for a dev cluster under load.
- No breaking changes to Student/Admin API — they continue to reach actor-host via the same `http://cena-actor-host:5050` DNS name (now resolved via k8s service + kubectl port-forward in Dev).

## Non-negotiable references

- Memory "No stubs — production grade" — removing TestProvider from Host is the concrete application of this.
- Memory "Senior Architect mindset" — traced the failure mode (Dev/Prod divergence) to root cause.
- Memory "Full sln build gate".
- Memory "Check container state before build".
- [ADR-0012](../../docs/adr/0012-student-actor-split.md) — aggregates that live on the cluster.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + helm chart SHA + dev-cluster-up.sh output transcript>"`

## Related

- EPIC-PRR-K.
- PRR-431 (load-test harness — runs against this cluster).
- PRR-432 (chaos simulation — runs against this cluster).
