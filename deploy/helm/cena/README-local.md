# Running the Cena Helm chart locally on M1 (k3d)

This is the one-command local-Kubernetes path for Cena on an M1 Mac. Runs
the same Helm chart production uses, with values overridden for
lightweight resources and reuse of the `docker-compose` dev infra for
Postgres/Redis/NATS/Firebase.

**Docker Compose is not replaced.** Both stacks run side-by-side. The k3d
pods reach the docker-compose infra containers via `host.docker.internal`,
so there's one set of dev credentials and one data volume.

Primary purpose: prove Proto.Actor cluster membership stabilises across
`replicaCount > 1` actor-host pods, which docker-compose cannot
demonstrate, and run replicated-pod pressure tests.

## Prerequisites

| Tool | Install |
|---|---|
| Docker Desktop (or Colima/OrbStack) | Expected to already be running |
| k3d ≥ 5.x | `brew install k3d` |
| kubectl | `brew install kubectl` |
| helm ≥ 3.x | `brew install helm` |
| docker-compose dev infra | `docker compose up -d postgres redis nats` (firebase-emulator optional) |

The up-script verifies all four before touching anything.

## One-command up / down

```bash
scripts/k8s-local-up.sh       # ~3-5 min first run (builds 4 images), ~1 min subsequent
scripts/k8s-local-down.sh     # deletes the k3d cluster; docker-compose untouched
```

The up-script is idempotent — re-running after the cluster exists does a
`helm upgrade --install` instead of recreating.

## What the up-script does

1. **Preflight** — verify k3d, kubectl, helm, docker on PATH, and that
   `cena-postgres`, `cena-redis`, `cena-nats` containers are running.
2. **Cluster** — create `k3d cluster cena-local` with:
   - Host port 5050 → cluster LoadBalancer → NodePort 30050 (student-api)
   - Host port 5052 → cluster LoadBalancer → NodePort 30052 (admin-api)
   - Traefik disabled (we use NodePort direct, no Ingress)
   - API port 6445 (keeps 6443 free)
3. **Build + import images** — `docker build --platform linux/arm64` each
   of the four service Dockerfiles, tag as
   `ghcr.io/cena-platform/<svc>:local`, then `k3d image import` them into
   the cluster so `pullPolicy: IfNotPresent` finds them without a pull.
4. **Secrets** — `kubectl create secret` for db/redis/nats/firebase,
   pointing at `host.docker.internal`. Dev credentials hardcoded in the
   script mirror `docker-compose.yml`; not committed as secrets.
5. **Helm** — `helm upgrade --install cena deploy/helm/cena/ -f
   values-local.yaml` and wait 5 min for readiness.

## What's deployed

| Component | Replicas | Ports (host → cluster) | Notes |
|---|---|---|---|
| `cena-actors` | **3** | — (gossip internal) | Proto.Actor cluster. **This is the point of the exercise.** |
| `cena-student-api` | 1 | 5050 → NodePort 30050 | |
| `cena-admin-api` | 1 | 5052 → NodePort 30052 | |
| `cena-migrator` | Job (pre-install hook) | — | Runs once, applies DB migrations, exits. |

Postgres / Redis / NATS / Firebase-emulator come from docker-compose on
`host.docker.internal`.

## Verifying Proto.Cluster membership

The main reason this setup exists. After `k8s-local-up.sh`:

```bash
# Should show 3 actor-host pods Ready
kubectl -n cena-local get pods -l app.kubernetes.io/component=actors

# Watch gossip/cluster-join events across all 3 pods
kubectl -n cena-local logs -l app.kubernetes.io/component=actors --tail=0 -f \
  | grep -iE "Cluster|Member|Gossip|Joined"

# Scale to 5 to test dynamic membership
kubectl -n cena-local scale deploy/cena-actors --replicas=5
kubectl -n cena-local get pods -w
```

Signs of healthy cluster membership:
- Every pod logs `MemberJoined` events for the others.
- No `MemberLeft` / `MemberRejoined` flapping after the initial join.
- Zero pod restarts after the initial readiness window (~30s).

## Running the emulator against the cluster

Use the existing emulator container — it reaches the cluster APIs via
the NodePort mappings on localhost. Stay within the **safe emulator
envelope** (`project_emulator_stress_envelope.md`):

```bash
# 200 students × 10× speed × 120s — safe, heavy, proven clean on
# docker-compose and now against the k3d cluster.
docker compose -f docker-compose.yml -f docker-compose.app.yml \
  --profile emulator run --rm \
  -e EMU_STUDENTS=200 -e EMU_SPEED=10 -e EMU_DURATION=120 \
  -e NATS_URL=nats://host.docker.internal:4222 \
  emulator
```

**Do not** run `EMU_SPEED=25` — the known-broken cell from RDY-081 /
postmortem 2026-04-19 hangs Docker Desktop's FS service.

## Troubleshooting

| Symptom | Probable cause | Fix |
|---|---|---|
| `Host.docker.internal: no such host` | Non-Docker-Desktop runtime | Add `--k3s-arg '--kubelet-arg=cluster-domain=cluster.local'` or switch to Docker Desktop. k3d on Docker Desktop injects the mapping. |
| `ImagePullBackOff` on any Cena pod | Image not imported into k3d | Re-run `scripts/k8s-local-up.sh` — the build+import step is idempotent. Or manually: `k3d image import ghcr.io/cena-platform/cena-actors-host:local -c cena-local` |
| `cena-migrator` job fails | Can't reach Postgres | Verify `docker ps \| grep cena-postgres` is healthy and accepting on :5433. |
| `cena-actors` pods crashloop | Proto.Cluster RBAC missing | `kubectl -n cena-local get sa cena-actors` should have bound role `cena-actors-cluster-reader`. If missing, `helm upgrade` again. |
| Port 5050 / 5052 already in use | docker-compose SPAs or APIs still bound | Stop conflicting containers or override via `CLUSTER_NAME=cena-alt ... scripts/k8s-local-up.sh` and edit the values-local NodePorts. |

## Why k3d and not X

| Option | Considered? | Verdict |
|---|---|---|
| Docker Desktop built-in k8s | Yes | Rejected: cluster lifecycle tied to Desktop app, full reset on toggle, shares the same VM so OOM blast radius is worse. |
| kind | Yes | Equivalent correctness. k3d is ~2× faster to start and uses fewer resources per node on M1. |
| OrbStack k8s | Considered | Good option; not chosen because it's an additional paid product. The scripts should work on OrbStack with minor tweaks. |
| minikube | No | Slower, heavier, less M1-native. |

## Relationship to production deployment

Everything under `deploy/helm/cena/templates/` is shared between local and
production. `values-local.yaml`, `values-staging.yaml`, and
`values-production.yaml` are the three environment overlays. If a change
only applies to local, it belongs in `values-local.yaml`; if it applies
everywhere, it goes in `values.yaml` (the default).

Do not introduce k3d-specific Helm templates or annotations. The chart
stays portable; k3d-awareness lives in this script/values pair.

## See also

- [deploy/DEPLOYMENT.md](../../DEPLOYMENT.md) — production + CI deploy flow
- [docs/operations/deploy-runbook.md](../../../docs/operations/deploy-runbook.md) — the ops runbook the Helm chart was designed for
- `project_emulator_stress_envelope.md` (agent memory) — safe emulator load bounds
- `project_rdy081_fix_plan.md` (agent memory) — why Rich mode is pinned and where the 3-writer pattern still needs refactoring
