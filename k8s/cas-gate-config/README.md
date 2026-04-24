# CAS Gate Runtime Config (RDY-036 / CAS-DEFERRED-OPS)

Three env vars gate the CAS pipeline in every Cena deployment target.
This directory ships them as a ConfigMap (non-secret) + Secret (opaque)
so ops can flip modes without touching images.

| Variable | Default | Source |
|---|---|---|
| `CENA_CAS_GATE_MODE` | `Enforce` | `cena-cas-gate` ConfigMap |
| `CENA_CAS_OVERRIDE_ENABLED` | `false` | `cena-cas-gate-operator` Secret |
| `CENA_ALLOW_PREPILOT_WIPE` | `false` | `cena-cas-gate-operator` Secret |

## Apply

```
kubectl apply -k k8s/cas-gate-config
```

Then reference from each consuming Deployment. Either:

1. Kustomize patch — add to the Deployment's parent overlay:
   ```yaml
   patchesStrategicMerge:
     - ../../k8s/cas-gate-config/deployment-patch.yaml
   ```
   with `PATCH_TARGET_DEPLOYMENT` swapped for the real name.

2. Direct envFrom — add to the Deployment's container spec:
   ```yaml
   envFrom:
     - configMapRef: { name: cena-cas-gate }
     - secretRef:    { name: cena-cas-gate-operator }
   ```

## Operator flip

```
# Staging → Enforce (no redeploy)
kubectl set env deploy/cena-admin-api -n cena-platform \
  CENA_CAS_GATE_MODE=Enforce

# Or edit the ConfigMap and restart the rollout
kubectl rollout restart deploy/cena-admin-api -n cena-platform
```

## Docker compose

Local-dev counterpart lives at `config/cas-gate/cas-gate.env.example`.
Copy to `config/cas-gate/cas-gate.env` (git-ignored), customize, then:

```
docker compose --env-file config/cas-gate/cas-gate.env up
```

## Links
- ADR-0032: docs/adr/0032-cas-gated-question-ingestion.md
- Runbook: docs/ops/alerts/cas-gate.md
- Load baseline: ops/reports/cas-load-baseline.md
- Chaos runbook: tests/chaos/sympy-sigkill-test.md
