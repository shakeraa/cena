# ACT-001: Proto.Actor Cluster Bootstrap

**Priority:** P0 — blocks ALL actor work
**Blocked by:** DATA-001 (PostgreSQL running), INF-001 (VPC)
**Estimated effort:** 2 days
**Contract:** `contracts/actors/cluster_config.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Proto.Actor needs a running cluster before any StudentActor can activate. This task sets up the cluster with DynamoDB discovery, gRPC transport, and health checks. Everything else in the actor system depends on this.

## Subtasks

### ACT-001.1: NuGet Dependencies + Project Scaffold
**Files to create/modify:**
- `src/Cena.Actors/Cena.Actors.csproj` — new .NET 9 class library
- `src/Cena.Actors.Host/Cena.Actors.Host.csproj` — new ASP.NET Core host
- `src/Cena.Actors.Host/Program.cs` — entry point

**Acceptance:**
- [ ] `dotnet new classlib -n Cena.Actors -f net9.0`
- [ ] `dotnet new web -n Cena.Actors.Host -f net9.0`
- [ ] NuGet packages installed:
  ```xml
  <PackageReference Include="Proto.Actor" Version="1.*" />
  <PackageReference Include="Proto.Cluster" Version="1.*" />
  <PackageReference Include="Proto.Cluster.AmazonDynamoDB" Version="1.*" />
  <PackageReference Include="Proto.Remote.GrpcNet" Version="1.*" />
  <PackageReference Include="Proto.Persistence.Marten" Version="1.*" />
  <PackageReference Include="Marten" Version="7.*" />
  <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
  ```
- [ ] Solution builds: `dotnet build` exits 0
- [ ] Host starts: `dotnet run --project src/Cena.Actors.Host` → listening on port 5000

**Test:**
```bash
dotnet build src/Cena.sln
# Assert: 0 errors, 0 warnings (treat warnings as errors in CI)
```

---

### ACT-001.2: DynamoDB Cluster Provider
**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/ClusterConfiguration.cs`

**Acceptance:**
- [ ] DynamoDB table `cena-cluster-members` auto-created on first start
- [ ] Table schema: `PK: MemberId (S)`, `TTL: HeartbeatExpiration (N)`
- [ ] Poll interval: 3 seconds
- [ ] Heartbeat expiration: 30 seconds (TODO: reduce to 15s after split-brain testing)
- [ ] `DeregisterOnShutdown = true`
- [ ] Works with `aws dynamodb` local (Docker) for development
- [ ] Works with real DynamoDB (staging/prod via IAM role)

**Test:**
```csharp
[Fact]
public async Task Cluster_RegistersInDynamoDB()
{
    var system = CreateTestActorSystem();
    await system.Cluster().StartMemberAsync();

    var items = await _dynamoDb.ScanAsync(new ScanRequest("cena-cluster-members"));
    Assert.Single(items.Items);

    await system.Cluster().ShutdownAsync();
    items = await _dynamoDb.ScanAsync(new ScanRequest("cena-cluster-members"));
    Assert.Empty(items.Items); // DeregisterOnShutdown
}
```

**Edge cases:**
- DynamoDB unreachable at startup → retry with backoff, log ERROR, don't crash
- DynamoDB throttled → exponential backoff on HeartbeatDeregistration
- Local dev without DynamoDB → fall back to `TestProvider` (in-memory)

---

### ACT-001.3: gRPC Remote Transport
**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/RemoteConfiguration.cs`
- `src/Cena.Actors/Serialization/ProtobufSerializer.cs`

**Acceptance:**
- [ ] gRPC transport on port 5001 (configurable via env `PROTO_REMOTE_PORT`)
- [ ] Protobuf serialization for ALL actor messages (not JSON)
- [ ] TLS optional in dev, REQUIRED in staging/prod (via env `PROTO_REMOTE_TLS=true`)
- [ ] Advertised host: container hostname in ECS, `localhost` in dev
- [ ] Connection timeout: 5 seconds
- [ ] Max message size: 4MB (enough for batch sync payloads)

**Test:**
```csharp
[Fact]
public async Task TwoNodes_CommunicateViaGrpc()
{
    var system1 = CreateTestActorSystem(port: 5001);
    var system2 = CreateTestActorSystem(port: 5002);

    await system1.Cluster().StartMemberAsync();
    await system2.Cluster().StartMemberAsync();

    // Send message from node1 to actor on node2
    var pid = system2.Root.Spawn(Props.FromFunc(ctx => {
        if (ctx.Message is string s) ctx.Respond(s.ToUpper());
        return Task.CompletedTask;
    }));

    var response = await system1.Root.RequestAsync<string>(pid, "hello", TimeSpan.FromSeconds(5));
    Assert.Equal("HELLO", response);
}
```

**Edge cases:**
- Port conflict → log FATAL, fail fast (don't silently fall back)
- Message exceeds 4MB → Proto.Actor throws, actor handles via supervision
- Network partition between nodes → heartbeat expiration handles this (ACT-001.2)

---

### ACT-001.4: Health Check Endpoints
**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/HealthChecks.cs`
- `src/Cena.Actors.Host/Program.cs` (add health check middleware)

**Acceptance:**
- [ ] `GET /health/ready` — returns 200 when cluster has ≥1 member AND PostgreSQL is reachable
- [ ] `GET /health/live` — returns 200 when process is running (liveness probe)
- [ ] Ready check fails → ECS stops routing traffic to this task
- [ ] Live check fails → ECS restarts the task
- [ ] Response body includes: cluster member count, PostgreSQL connection status, uptime

**Test:**
```csharp
[Fact]
public async Task HealthCheck_ReturnsUnhealthyWhenNoClusterMembers()
{
    // Don't start the cluster
    var response = await _httpClient.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.Contains("unhealthy", body.ToLower());
}

[Fact]
public async Task HealthCheck_ReturnsHealthyWhenClusterActive()
{
    await _actorSystem.Cluster().StartMemberAsync();
    var response = await _httpClient.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

---

### ACT-001.5: OpenTelemetry + Structured Logging
**Files to create/modify:**
- `src/Cena.Actors/Infrastructure/TelemetryConfiguration.cs`
- `src/Cena.Actors.Host/appsettings.json`

**Acceptance:**
- [ ] `ActivitySource("Cena.Actors")` registered for all actor operations
- [ ] `Meter("Cena.Actors")` registered for metrics
- [ ] Serilog with structured JSON logging (not text)
- [ ] Log levels: ERROR for failures, WARNING for degradation, INFO for lifecycle, DEBUG for messages
- [ ] OpenTelemetry exporter: OTLP (configurable endpoint via env)
- [ ] Correlation ID propagated from HTTP request → actor messages → NATS events

**Test:**
```csharp
[Fact]
public void TelemetryConfiguration_RegistersAllSources()
{
    var services = new ServiceCollection();
    services.AddCenaTelemetry();
    var provider = services.BuildServiceProvider();

    var meterProvider = provider.GetRequiredService<MeterProvider>();
    Assert.NotNull(meterProvider);

    // Verify activity source is registered
    using var activity = new ActivitySource("Cena.Actors").StartActivity("test");
    Assert.NotNull(activity);
}
```

---

### ACT-001.6: Graceful Shutdown Hook
**Files to create/modify:**
- `src/Cena.Actors.Host/Program.cs` (add `IHostLifetime` hook)

**Acceptance:**
- [ ] `SIGTERM` triggers graceful shutdown (ECS sends this before killing)
- [ ] Shutdown drains: stop accepting activations → passivate active actors → flush state → leave cluster
- [ ] Max shutdown time: 30 seconds (ECS `stopTimeout`)
- [ ] If 30s exceeded: log WARNING, force exit (ECS kills anyway)
- [ ] No data loss: all in-flight events persisted before shutdown completes

**Test:**
```csharp
[Fact]
public async Task GracefulShutdown_CompletesWithin30Seconds()
{
    await _actorSystem.Cluster().StartMemberAsync();
    // Activate 10 test actors
    for (int i = 0; i < 10; i++)
        await ActivateTestStudentActor($"student-{i}");

    var sw = Stopwatch.StartNew();
    await _actorSystem.Cluster().ShutdownAsync();
    sw.Stop();

    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30));
    // Verify cluster membership table is empty
    var items = await _dynamoDb.ScanAsync(new ScanRequest("cena-cluster-members"));
    Assert.Empty(items.Items);
}
```

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullClusterBootstrap_EndToEnd()
{
    // 1. Start cluster
    var host = await CreateAndStartHost();

    // 2. Health check passes
    var health = await httpClient.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.OK, health.StatusCode);

    // 3. DynamoDB has member
    var members = await ScanClusterMembers();
    Assert.Single(members);

    // 4. Can activate a virtual actor
    var grain = host.Services.GetRequiredService<Cluster>()
        .GetGrain<IStudentGrain>("test-student");
    Assert.NotNull(grain);

    // 5. Shutdown cleans up
    await host.StopAsync();
    members = await ScanClusterMembers();
    Assert.Empty(members);
}
```

## Rollback Criteria
If this task fails or introduces instability:
- Revert to single-process mode (no cluster, local-only actors)
- All downstream actor tasks are blocked until this is stable
- Acceptable temporary state: development uses in-memory provider, staging tests DynamoDB

## Definition of Done
- [ ] All 6 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=ClusterBootstrap"` → 0 failures
- [ ] Staging deployment: cluster starts, health checks pass, can activate 1 actor
- [ ] PR reviewed by architect (you)
