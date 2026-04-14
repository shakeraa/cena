# Cena Deployment Guide (RDY-025)

Zero-to-running-cluster procedure for staging and production.

## Architecture

Three hosts, deployed independently, talk via PostgreSQL (event store), Redis (cache), NATS (async bus):

```
                        ┌─────────────┐
                        │   Ingress   │  (nginx + cert-manager TLS)
                        └──────┬──────┘
                 ┌─────────────┴─────────────┐
                 │                           │
          ┌──────▼──────┐            ┌───────▼───────┐
          │ Student API │            │   Admin API   │  (HPA, PDB)
          │  :5051      │            │   :5052       │
          └──────┬──────┘            └───────┬───────┘
                 │                           │
                 └──────┬──────────┬─────────┘
                        │          │
              NATS      ▼          ▼   Proto.Cluster gossip
             ┌─────┐ ┌────────────────┐  (headless Service)
             │NATS ◄─┤   Actor Host   │
             └──▲──┘ │   :5050 /8090  │  (3 replicas, PDB minAvailable=2)
                │   └───┬──────┬─────┘
                │       │      │
                │    PG │      │ Redis
                │   ┌───▼──┐ ┌─▼─────┐
                │   │Marten│ │Stack  │
                │   │ PG   │ │Exchange│
                │   └──────┘ └───────┘
                │
             (events)
```

## Prerequisites

- Kubernetes cluster 1.28+ (GKE/EKS/AKS tested; local: kind/minikube)
- Helm 3.12+
- `kubectl` with cluster admin
- External (managed or self-hosted):
  - PostgreSQL 16+ with `pgvector` extension
  - Redis 7+
  - NATS 2.10+ with JetStream
- Container registry access (default: `ghcr.io/cena-platform/*`)

## Why Helm, not Kustomize

The task spec suggested Kustomize overlays; this repo uses **Helm** instead because:

1. A complete Helm chart already exists at `deploy/helm/cena/` (templates, HPA, services, migrator job)
2. Helm's values-file composition (`values.yaml` + `values-staging.yaml` + `values-production.yaml`) gives the same overlay semantics as Kustomize with less duplication
3. Helm's release lifecycle (install/upgrade/rollback) integrates with CD tooling (ArgoCD, Flux) more cleanly for apps with hooks (migrator job)
4. The migrator runs as a Helm pre-install/pre-upgrade hook — moving that pattern to Kustomize would require a custom controller

Decision documented here instead of an ADR to keep the decision close to the deployment docs. If the team later prefers Kustomize, the migration path is mechanical (`helm template` → split into overlays).

## 1. Build and push images

Three multi-stage Dockerfiles live alongside each host:

```bash
# Build context is the repo root for all three images
docker build -f src/actors/Cena.Actors.Host/Dockerfile    -t ghcr.io/cena-platform/cena-actors-host:$TAG    .
docker build -f src/api/Cena.Student.Api.Host/Dockerfile  -t ghcr.io/cena-platform/cena-student-api:$TAG   .
docker build -f src/api/Cena.Admin.Api.Host/Dockerfile    -t ghcr.io/cena-platform/cena-admin-api:$TAG     .
docker build -f src/api/Cena.Db.Migrator/Dockerfile       -t ghcr.io/cena-platform/cena-db-migrator:$TAG   .

docker push ghcr.io/cena-platform/cena-actors-host:$TAG
docker push ghcr.io/cena-platform/cena-student-api:$TAG
docker push ghcr.io/cena-platform/cena-admin-api:$TAG
docker push ghcr.io/cena-platform/cena-db-migrator:$TAG
```

All images:
- Are non-root (uid 10001, group 10001)
- Expose only the required ports
- Have a `HEALTHCHECK` that hits `/health/live`
- Use multi-stage builds with NuGet restore cached on `.csproj` changes only

## 2. Create namespace and secrets

Secrets are NEVER in values files. Create them out-of-band before `helm install`:

```bash
# Namespace
kubectl create namespace cena

# Database (keys: connection-string-migrator, -student, -admin, -actors)
kubectl -n cena create secret generic cena-db-credentials \
  --from-literal=connection-string-migrator='Host=pg;Database=cena;Username=migrator;Password=...' \
  --from-literal=connection-string-student='Host=pg;Database=cena;Username=student;Password=...' \
  --from-literal=connection-string-admin='Host=pg;Database=cena;Username=admin;Password=...' \
  --from-literal=connection-string-actors='Host=pg;Database=cena;Username=actors;Password=...'

# Redis
kubectl -n cena create secret generic cena-redis-credentials \
  --from-literal=connection-string='redis-master.cena.svc:6379,password=...,ssl=true'

# NATS
kubectl -n cena create secret generic cena-nats-credentials \
  --from-literal=connection-string='nats://nats.cena.svc:4222' \
  --from-literal=nats-user='cena-actors' \
  --from-literal=nats-password='...'

# Firebase (service-account JSON mounted as file)
kubectl -n cena create secret generic cena-firebase-credentials \
  --from-file=credentials.json=./firebase-service-account.json

# Image pull secret for private registry (if applicable)
kubectl -n cena create secret docker-registry ghcr-credentials \
  --docker-server=ghcr.io \
  --docker-username=<user> \
  --docker-password=<pat-or-token>
```

For production, use **sealed-secrets** or **external-secrets-operator** instead of raw `kubectl create secret`.

## 3. Deploy

### Staging

```bash
helm upgrade --install cena deploy/helm/cena \
  -f deploy/helm/cena/values-staging.yaml \
  --namespace cena-staging --create-namespace \
  --set actors.image.tag=$TAG \
  --set student.image.tag=$TAG \
  --set admin.image.tag=$TAG \
  --set migrator.image.tag=$TAG
```

### Production

```bash
helm upgrade --install cena deploy/helm/cena \
  -f deploy/helm/cena/values-production.yaml \
  --namespace cena --create-namespace \
  --set actors.image.tag=$TAG \
  --set student.image.tag=$TAG \
  --set admin.image.tag=$TAG \
  --set migrator.image.tag=$TAG \
  --atomic --timeout 10m
```

`--atomic` rolls back on failure. `--timeout 10m` covers migrator + actor warmup.

## 4. Verify

```bash
# Pods up and ready
kubectl -n cena get pods -w

# Actor Host cluster members discovered each other
kubectl -n cena logs deploy/cena-actors | grep "cluster joined"

# API health
kubectl -n cena port-forward svc/cena-student 8080:80
curl localhost:8080/health/ready
curl localhost:8080/health/live

# PDB + HPA
kubectl -n cena get pdb
kubectl -n cena get hpa
```

## Resource limits — tuning notes

Current limits in `values.yaml` are **placeholder estimates**. Tune after pilot via Prometheus:

| Service | Starting point | Tune based on |
|---------|---------------|---------------|
| Actor Host | 1 CPU / 2Gi req, 4 CPU / 8Gi lim | Event store cache hit rate, snapshot replay time, `.NET GC` pressure |
| Student API | 500m / 512Mi req, 2 CPU / 1Gi lim | p95 latency on `/api/session/*`, HPA trigger frequency |
| Admin API | 300m / 512Mi req, 1 CPU / 1Gi lim | Usually idle; spikes during question authoring |

OOMKills → bump memory limit (not request). CPU throttling → bump CPU limit.

## Rolling update behavior

| Service | maxSurge | maxUnavailable | Rationale |
|---------|----------|----------------|-----------|
| Actor Host | 1 | 0 | Cluster gossip needs stable membership; never drop below N-1 |
| Student API | 25% | 0 | Zero-downtime rollout |
| Admin API | 25% | 0 | Zero-downtime rollout |

## Rollback

```bash
helm -n cena rollback cena <revision>
# Or to previous:
helm -n cena rollback cena 0
```

The migrator runs on `pre-install,pre-upgrade` hooks — rolling back the Helm release does NOT roll back schema migrations. Schema rollback is a separate manual process (see `src/api/Cena.Db.Migrator/README.md`).

## Teardown

```bash
helm -n cena uninstall cena
kubectl delete namespace cena
```

Event store data in PostgreSQL is **not** deleted — drop the database manually if required.
