# RDY-025: Deployment Manifests (Kubernetes + Docker)

- **Priority**: Medium — blocks production deployment
- **Complexity**: Senior DevOps engineer
- **Source**: Expert panel audit — Dina (Architecture)
- **Tier**: 3
- **Effort**: 3-4 weeks (revised from 1-2 weeks — Phase 1: Dockerfiles + basic K8s 1w, Phase 2: HPA/PDB/Ingress/testing 2w)

> **Rami's challenge**: Scope is massive when you count: 3 multi-stage Dockerfiles, K8s manifests (Deployments, Services, ConfigMaps, Secrets), HPA, PDB, Ingress routing, database manifests, testing deployment from zero, and documentation. Also: resource limits are placeholder estimates until pilot metrics exist — note this in manifests.

## Problem

No Kubernetes specs, Docker image layering config, or replica configuration exists. The platform runs only via `docker-compose.yml` (development). Production deployment is undefined.

## Scope

### 1. Docker multi-stage builds

For each host (Actor Host, Student API, Admin API):
- Multi-stage Dockerfile (restore → build → publish → runtime)
- Minimal runtime image (Alpine-based)
- Non-root user
- Health check in Dockerfile

### 2. Kubernetes manifests

- Deployment per service (Actor Host, Student API, Admin API)
- Service definitions (ClusterIP for internal, LoadBalancer for APIs)
- ConfigMap for non-secret configuration
- Secret references for credentials (Firebase, NATS, PostgreSQL, Redis)
- HPA (Horizontal Pod Autoscaler) for API hosts
- PDB (Pod Disruption Budget) for Actor Host (cluster membership)

### 3. Resource limits

- CPU/memory requests and limits per service
- Actor Host: higher memory (event store, snapshots)
- API hosts: lower memory, higher replica count

### 4. Ingress

- Nginx Ingress or equivalent
- TLS termination
- Path-based routing: `/api/student/*` → Student API, `/api/admin/*` → Admin API

### 5. Infrastructure dependencies

- PostgreSQL: StatefulSet or managed service reference
- Redis: StatefulSet or managed service reference
- NATS: Helm chart or managed service reference
- Neo4j: StatefulSet or managed service reference

## Files to Create

- New: `deploy/docker/Dockerfile.actors` — Actor Host
- New: `deploy/docker/Dockerfile.student-api` — Student API
- New: `deploy/docker/Dockerfile.admin-api` — Admin API
- New: `deploy/k8s/base/` — Kustomize base (deployments, services, configmaps)
- New: `deploy/k8s/overlays/staging/` — staging overrides
- New: `deploy/k8s/overlays/production/` — production overrides

## Acceptance Criteria

- [ ] Multi-stage Dockerfiles for all 3 hosts
- [ ] K8s manifests deploy all services with correct health probes
- [ ] Secrets referenced (not hardcoded) in manifests
- [ ] HPA configured for API hosts
- [ ] Resource limits defined per service
- [ ] `kubectl apply -k deploy/k8s/overlays/staging/` deploys successfully
- [ ] Documentation: how to deploy from zero to running cluster
