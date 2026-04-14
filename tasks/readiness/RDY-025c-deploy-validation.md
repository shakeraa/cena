# RDY-025c: Deployment Validation — Run End-to-End

- **Priority**: High — RDY-025 manifests are unverified against a running cluster
- **Complexity**: DevOps engineer
- **Source**: RDY-025 review gap
- **Tier**: 2
- **Effort**: 2-3 days
- **Depends on**: RDY-025 (done), RDY-025b (Proto.Cluster.Kubernetes wiring)

## Problem

RDY-025 delivered Dockerfiles + Helm chart + values overlays + docs, but nothing was ever run. The chart `helm lint` and `helm template` pass, and the Dockerfiles were reviewed for correctness, but:

- No `docker build` was executed against any of the 3 Dockerfiles
- No `helm install` was executed against any cluster (kind/minikube/staging)
- Images have never been pulled, run, or health-checked inside a pod
- Ingress paths / TLS / cert-manager integration has never been proven

Until a real dry run happens, the RDY-025 deliverables are paper specs.

## Scope

### 1. Docker image builds

Build all 4 images locally to catch any COPY path errors, project reference issues, or restore failures:

```bash
docker build -f src/actors/Cena.Actors.Host/Dockerfile    -t cena/actors-host:local    .
docker build -f src/api/Cena.Student.Api.Host/Dockerfile  -t cena/student-api:local   .
docker build -f src/api/Cena.Admin.Api.Host/Dockerfile    -t cena/admin-api:local     .
docker build -f src/api/Cena.Db.Migrator/Dockerfile       -t cena/db-migrator:local   .
```

Record image sizes. Multi-stage runtime images should be < 300 MB each.

### 2. Local smoke test (docker-compose or manual)

For each image, run the container against local PostgreSQL/Redis/NATS (docker-compose.yml):

- Verify startup: no crash within 30s
- Verify HEALTHCHECK passes: `docker inspect --format='{{.State.Health.Status}}' <container>` returns `healthy`
- Hit `/health/ready` and `/health/live` from outside the container

### 3. kind-cluster dry run

Stand up a local kind cluster and deploy the chart:

```bash
kind create cluster --name cena-test
kind load docker-image cena/actors-host:local cena/student-api:local cena/admin-api:local cena/db-migrator:local --name cena-test

# Create required secrets with test values
kubectl create namespace cena
kubectl -n cena create secret generic cena-db-credentials \
  --from-literal=connection-string-migrator='...' \
  --from-literal=connection-string-student='...' \
  --from-literal=connection-string-admin='...' \
  --from-literal=connection-string-actors='...'
# ... (Redis, NATS, Firebase per DEPLOYMENT.md)

helm install cena deploy/helm/cena -f deploy/helm/cena/values-staging.yaml \
  --namespace cena \
  --set actors.image.repository=cena/actors-host \
  --set actors.image.tag=local \
  --set actors.image.pullPolicy=Never \
  ... (same for student/admin/migrator)
```

Verify:

- All pods Running + Ready within 5 minutes
- PDB shows `minAvailable` enforced
- HPA registers correctly
- Migrator Job completes successfully (exit 0)
- Student + Admin API respond to `/health/ready`
- Actor Host (after RDY-025b) shows 3 cluster members in logs

### 4. Ingress smoke test

- Install nginx-ingress in kind: `kubectl apply -f ...`
- Port-forward ingress-nginx-controller
- curl `localhost/api/student/health/ready` → 200
- curl `localhost/api/admin/health/ready` → 200

### 5. Failure-mode tests

- Kill a Student API pod → HPA/ReplicaSet recreates within 30s
- Drain a node with Actor Host pod → PDB blocks drain if it would violate `minAvailable`
- Delete Redis secret → Student API pod fails readiness, does NOT flood-restart

### 6. Image size + layer caching

- Measure image sizes (target < 300 MB for runtime)
- Verify NuGet restore layer cached on source-only edits (touch a `.cs` file, rebuild; restore layer should be cache hit)

### 7. Document gaps found

Any issue found during real run → fix in RDY-025 chart + DEPLOYMENT.md.
Unresolvable issues → new tracked task.

## Files to Modify

- Potentially `deploy/helm/cena/` templates if bugs surface
- Potentially Dockerfiles if build fails
- `deploy/DEPLOYMENT.md` — add verified-against-kind note
- New: `deploy/README.md` or `deploy/runbook.md` for issue log

## Acceptance Criteria

- [ ] All 4 Docker images build successfully and are < 300 MB
- [ ] All containers pass Docker HEALTHCHECK within 60s of start
- [ ] Helm chart deploys cleanly to kind cluster via `helm install`
- [ ] All pods reach Running + Ready state
- [ ] Migrator Job runs and exits 0
- [ ] Student + Admin APIs respond to `/health/ready` through Ingress
- [ ] Actor Host forms a 3-member cluster (requires RDY-025b)
- [ ] PDB enforcement verified via `kubectl drain`
- [ ] HPA registered and scaled (simulated load)
- [ ] Runbook documents any operational quirks found

## Notes

- This task should happen AFTER RDY-025b unblocks the Actor Host crashloop
- A CI job that runs `docker build` + `helm lint` + `helm template` on every PR would have caught most issues earlier — consider adding that in a separate infra task
- Resource limits in `values.yaml` are placeholder estimates; retune after real workload observation
