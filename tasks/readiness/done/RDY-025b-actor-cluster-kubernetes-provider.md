# RDY-025b: Actor Host Kubernetes Cluster Provider

- **Priority**: Critical — BLOCKS RDY-025 staging/production deploy
- **Complexity**: Senior .NET engineer + K8s RBAC
- **Source**: RDY-025 review gap
- **Tier**: 2 (blocks production deploy of Actor Host)
- **Effort**: 2-3 days
- **Depends on**: RDY-025 (chart is ready; just needs code wiring)

## Problem

`src/actors/Cena.Actors.Host/Program.cs:334-338` throws when `Cluster:Provider=kubernetes` because the `Proto.Cluster.Kubernetes` NuGet package is not installed. Only `Proto.Cluster.TestProvider` (dev-only, in-memory) is referenced.

The RDY-025 Helm chart is complete but intentionally does NOT set `Cluster:Provider` — deploying today would either (a) throw the "TestProvider not allowed outside Development" error or (b) silently single-pod cluster if env var were set to `test`. Either way, the 3-replica Actor Host deployment cannot form a real cluster.

## Scope

### 1. Add NuGet package

In `src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj`:

```xml
<PackageReference Include="Proto.Cluster.Kubernetes" Version="1.8.0" />
```

### 2. Wire KubernetesProvider in Program.cs

Replace the throw at Program.cs:334-338:

```csharp
"kubernetes" => new KubernetesProvider(
    new KubernetesProviderConfig(
        podLabelSelector: "app.kubernetes.io/component=actors",
        watchTimeoutSeconds: 30)),
```

### 3. RBAC for pod discovery

Add to Helm chart: `deploy/helm/cena/templates/actors-rbac.yaml`:

- Role: get/list/watch pods in namespace
- RoleBinding: Role -> ServiceAccount created by chart

### 4. Enable Cluster:Provider

Once code + RBAC are in place, add to ConfigMap and wire into actors-deployment env:

```yaml
- name: Cluster__Provider
  value: "kubernetes"
```

### 5. Validate cluster formation

- Deploy 3 Actor Host replicas to staging
- Confirm logs show "cluster joined" on all 3 pods
- Confirm SRV records resolve all 3 via headless service
- Test rolling update preserves cluster membership (PDB minAvailable=2)

## Files to Modify

- `src/actors/Cena.Actors.Host/Cena.Actors.Host.csproj`
- `src/actors/Cena.Actors.Host/Program.cs` (remove throw, add KubernetesProvider)
- New: `deploy/helm/cena/templates/actors-rbac.yaml`
- `deploy/helm/cena/templates/configmap.yaml` (add cluster.provider back)
- `deploy/helm/cena/templates/actors-deployment.yaml` (wire env var)
- `deploy/DEPLOYMENT.md` (remove Known Blockers section)

## Acceptance Criteria

- [ ] Proto.Cluster.Kubernetes NuGet added
- [ ] KubernetesProvider wired in Program.cs
- [ ] RBAC template creates Role + RoleBinding scoped to namespace
- [ ] 3-replica Actor Host deployment forms a cluster in staging
- [ ] Rolling update preserves quorum (PDB minAvailable=2)
- [ ] SRV records via headless service resolve all replicas
- [ ] DEPLOYMENT.md Known Blockers section removed
