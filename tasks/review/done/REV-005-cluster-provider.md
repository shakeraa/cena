# REV-005: Wire Production Cluster Provider for Proto.Actor

**Priority:** P0 -- BLOCKER (multi-node deployment impossible with TestProvider)
**Blocked by:** None for Kubernetes provider; INF-002 for DynamoDB provider
**Blocks:** Horizontal scaling, production deployment
**Estimated effort:** 2 days
**Source:** System Review 2026-03-28 -- Lead Architect (Actor System C4), Backend Senior (C1), DevOps (Finding #6)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule.

## Purpose

The Actor Host `Program.cs` (lines 258-265) falls through to `TestProvider` (in-memory, single-node) for ALL environments, including production. A `LogWarning` is emitted but execution continues. This means:
- Actor activations are lost on restart (no durable discovery)
- Multi-node clustering is impossible
- The `TestProvider` is explicitly documented by Proto.Actor as "for testing only"

## Architect's Decision

Implement a **configuration-driven provider selection** with fail-fast in production:

| Environment | Provider | Why |
|-------------|----------|-----|
| `Development` | `TestProvider` | Fast local iteration, no external dependencies |
| `Staging` | `ConsulProvider` or `KubernetesProvider` | Real clustering with lightweight infra |
| `Production` | `KubernetesProvider` (if K8s) or `ConsulProvider` (if ECS) | Production-grade discovery |

**Do not use DynamoDB cluster provider** unless the deployment target is specifically ECS with DynamoDB. Kubernetes-native provider is simpler and avoids an additional AWS dependency.

## Subtasks

### REV-005.1: Add Configuration-Driven Provider Selection

**Files to modify:**
- `src/actors/Cena.Actors.Host/Program.cs` (lines 245-270)
- `src/actors/Cena.Actors.Host/appsettings.json` -- add cluster config section
- `src/actors/Cena.Actors.Host/appsettings.Development.json` -- dev overrides

**NuGet packages to add (conditional):**
- `Proto.Cluster.Kubernetes` -- for K8s deployments
- `Proto.Cluster.Consul` -- for Consul-based discovery (ECS/VM)

**Pattern:**
```csharp
var clusterProviderType = builder.Configuration["Cluster:Provider"] ?? "test";

IClusterProvider clusterProvider = clusterProviderType.ToLowerInvariant() switch
{
    "test" when builder.Environment.IsDevelopment() =>
        new TestProvider(new TestProviderOptions(), new InMemAgent()),

    "test" =>
        throw new InvalidOperationException(
            "TestProvider is not allowed outside Development. " +
            "Set Cluster:Provider to 'kubernetes' or 'consul'."),

    "kubernetes" =>
        new KubernetesProvider(),

    "consul" =>
        new ConsulProvider(new ConsulProviderConfig
        {
            Address = new Uri(builder.Configuration["Cluster:ConsulAddress"]
                ?? throw new InvalidOperationException("Cluster:ConsulAddress required"))
        }),

    _ => throw new InvalidOperationException(
        $"Unknown cluster provider: '{clusterProviderType}'. " +
        "Valid values: test (dev only), kubernetes, consul.")
};
```

**Acceptance:**
- [ ] `Development` environment uses TestProvider (unchanged behavior)
- [ ] Non-development with `Cluster:Provider=test` throws `InvalidOperationException` with clear message
- [ ] `Cluster:Provider=kubernetes` creates `KubernetesProvider`
- [ ] `Cluster:Provider=consul` creates `ConsulProvider` with configured address
- [ ] Unknown provider values throw with valid options listed
- [ ] No silent fallback to TestProvider in any non-dev environment

### REV-005.2: Add Cluster Configuration to appsettings

**File to modify:** `src/actors/Cena.Actors.Host/appsettings.json`

```json
{
  "Cluster": {
    "Provider": "test",
    "ClusterName": "cena-cluster",
    "AdvertisedHost": "localhost",
    "MinNodes": 1,
    "MaxNodes": 5,
    "ConsulAddress": "http://consul:8500"
  }
}
```

**File to modify:** `src/actors/Cena.Actors.Host/appsettings.Development.json`

```json
{
  "Cluster": {
    "Provider": "test"
  }
}
```

**Acceptance:**
- [ ] Provider is configurable via `appsettings.json` or environment variable `Cluster__Provider`
- [ ] All cluster settings have sensible defaults for development
- [ ] Production requires explicit configuration (no working defaults for non-test providers)

### REV-005.3: Add Health Check for Cluster Membership

**File to modify:** `src/actors/Cena.Actors.Host/Program.cs` (health check section)

**Enhancement:** The existing readiness probe checks cluster membership. Enhance it to also verify the cluster provider is healthy:

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("cluster-membership", () =>
    {
        var cluster = actorSystem.Cluster();
        var members = cluster.MemberList.GetAllMembers();
        var minNodes = builder.Configuration.GetValue<int>("Cluster:MinNodes", 1);

        if (members.Length < minNodes)
            return HealthCheckResult.Degraded(
                $"Cluster has {members.Length} members, minimum is {minNodes}");

        return HealthCheckResult.Healthy(
            $"Cluster healthy: {members.Length} members");
    }, tags: new[] { "ready" });
```

**Acceptance:**
- [ ] `/health/ready` returns Degraded if member count < MinNodes
- [ ] `/health/ready` returns Healthy with member count in description
- [ ] Kubernetes probes can use this for rolling deployment gates
