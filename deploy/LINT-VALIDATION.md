# Helm chart static validation report (RDY-025c partial)

This report captures what can be verified about `deploy/helm/cena/`
without a running Kubernetes cluster. Full end-to-end validation (kind
deploy, HPA scale-test, PDB drain-test, ingress smoke) remains pending
on RDY-025c §3–5 and requires an actual cluster.

**Date of last run**: 2026-04-19
**Run by**: engineering (claude-code)
**Tools**: `helm 3` + `kubeconform 0.7.0`

## Scope of this pass

1. `helm lint` against the chart with all three value overlays
2. `helm template` rendering of each overlay to catch template errors
3. `kubeconform -strict -summary` schema validation of the rendered YAML
   against upstream Kubernetes OpenAPI schemas
4. Spot-check of image pinning, secret references, and env wiring

## Results

| Check | values.yaml (default) | values-staging.yaml | values-production.yaml |
|---|---|---|---|
| `helm lint` | PASS | PASS | PASS |
| `helm template` | PASS (757 lines, 13 resources) | PASS (730 lines, 15 resources) | PASS (848 lines, 18 resources) |
| `kubeconform -strict` | — | **15/15 Valid, 0 Invalid** | **18/18 Valid, 0 Invalid** |

Rendered resource counts by kind (production):

| Kind | Count |
|---|---|
| ConfigMap | 1 |
| Deployment | 3 (admin, student, actors) |
| HorizontalPodAutoscaler | 2 (admin, student) |
| Ingress | 1 |
| Job | 1 (migrator) |
| PodDisruptionBudget | 3 (admin, student, actors) |
| Role | 1 (actors pod discovery) |
| RoleBinding | 1 |
| Service | 4 (admin, student, actors http, actors gossip) |
| ServiceAccount | 1 (actors) |

## Fixed during this pass

### R1. `actors-rbac.yaml` referenced non-existent values path

Template referenced `.Values.cluster.provider` but the chart's value tree
has `.Values.actors.cluster.provider`. `helm lint` failed at the nil
dereference:

```
templates/actors-rbac.yaml:11:18: nil pointer evaluating interface {}.provider
```

Fixed the template to `(((.Values.actors).cluster).provider | default "test")`
so the guard works regardless of whether the nested path is present.

Impact of bug (had it not been caught): Proto.Cluster.Kubernetes would
never have been given a ServiceAccount + Role + RoleBinding, so the actor
pods could not have discovered peers at startup — a silent cluster-never-
forms failure that would only manifest in a real deploy.

Fix: single-line template edit. No values-file change needed.

## Follow-ups (for when a cluster is available)

### F1. Image tags default to `:latest`

All three overlays inherit `image.tag: latest` from `values.yaml`.
Production overlay does not override. Rendering
`values-production.yaml` shows:

```
image: "ghcr.io/cena-platform/cena-actors-host:latest"
image: "ghcr.io/cena-platform/cena-student-api:latest"
image: "ghcr.io/cena-platform/cena-admin-api:latest"
image: "ghcr.io/cena-platform/cena-db-migrator:latest"
```

`:latest` is the classic Kubernetes anti-pattern — no rollback anchor,
pull-cache inconsistency, no correlation between a running pod and the
git SHA that produced it. **Production must pin to a SHA-based tag.**

Recommended: CI produces `ghcr.io/.../cena-actors-host:$(git rev-parse --short HEAD)`
and the release-manager overrides `--set actors.image.tag=$GIT_SHA` on
every `helm upgrade`. Ties in with RDY-064 release correlation.

Action: track in RDY-025 follow-up or new RDY as a CI task.

### F2. `:latest` + `pullPolicy: IfNotPresent` is a combined footgun

When `IfNotPresent` is set against `:latest`, the kubelet won't re-pull
even when the upstream tag moves. Either:
- Pin SHA tags (see F1) and keep `IfNotPresent`, OR
- Set `pullPolicy: Always` while any image is still `:latest`.

### F3. No network-policy templates

The chart delivers RBAC for Proto.Cluster.Kubernetes but no
NetworkPolicy. On a hardened cluster (PSP/PSS restricted, default-deny
egress) the actor pods would lose outbound reachability to Postgres /
Redis / NATS. Add a minimal default-allow NetworkPolicy for each
deployment, or a companion namespace-wide allow-all policy scoped to the
release.

### F4. HPA metric source assumed to be metrics-server

The rendered HPA uses `type: Resource` with `averageUtilization`. This
requires metrics-server to be installed in the cluster. Document as a
prerequisite in `DEPLOYMENT.md` (not done yet).

### F5. Ingress disabled by default — silent on misconfig

`ingress.enabled: false` is the default. Neither staging nor production
overlay flips it to `true` in this chart, so the rendered manifests omit
the Ingress object. Real deployments need overlay-level enablement with
`ingress.hosts[0].host` set to the real DNS record. Add an assertion in
`values-production.yaml` (`ingress.enabled: true` + explicit host) and
a `deploy/DEPLOYMENT.md` step.

### F6. cert-manager annotations

`ingress.annotations` is empty in values. Real prod needs
`cert-manager.io/cluster-issuer: letsencrypt-prod` and ALB / nginx SSL
redirect annotations. Document in `DEPLOYMENT.md`.

### F7. Missing probes on the migrator Job

The migrator runs as a Job with `backoffLimit: 3` — fine for a one-shot.
No readiness / liveness probe needed. Confirmed intentional (runs to
completion once per release).

### F8. Resource limits are placeholder estimates

Per `values.yaml` header comment: "Resource limits below are placeholder
estimates until pilot metrics exist." Still true. Re-tune after the
first week of pilot traffic via Prometheus.

## What this report does NOT prove

Everything in RDY-025c §2–§5 requires a cluster and a docker build:

- [ ] All 4 images build (`docker build` not executed)
- [ ] Images ≤ 300 MB (not measured)
- [ ] HEALTHCHECK passes (no container started)
- [ ] `helm install` succeeds on kind (no kind cluster)
- [ ] Pods reach Running + Ready (no cluster)
- [ ] Migrator Job exits 0 (no cluster)
- [ ] Ingress routes to APIs (no cluster + no ingress)
- [ ] PDB blocks drain (no cluster)
- [ ] HPA scales (no cluster, no load generator)
- [ ] Proto.Cluster forms 3-member cluster (depends on RDY-025b + cluster)

## Reproducing this report

```bash
# Lint
helm lint deploy/helm/cena
helm lint deploy/helm/cena -f deploy/helm/cena/values-staging.yaml
helm lint deploy/helm/cena -f deploy/helm/cena/values-production.yaml

# Render
helm template cena deploy/helm/cena -f deploy/helm/cena/values-staging.yaml    > /tmp/cena-staging.yaml
helm template cena deploy/helm/cena -f deploy/helm/cena/values-production.yaml > /tmp/cena-production.yaml

# Schema validate
kubeconform -strict -summary /tmp/cena-staging.yaml
kubeconform -strict -summary /tmp/cena-production.yaml
```
