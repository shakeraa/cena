# INF-011: NATS Account-Based Authorization

**Priority:** P0 — BLOCKER (without NATS auth, any service can publish/subscribe to any subject)
**Blocked by:** SEC-009 (Firebase Auth — service identity model)
**Blocks:** All cross-context NATS communication in production
**Estimated effort:** 3 days
**Contract:** `contracts/backend/nats-subjects.md` (subject hierarchy, stream configs, consumer groups)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
The Cena NATS JetStream topology (per `nats-subjects.md`) has 8 bounded-context streams and 15+ consumer groups. Without account-based authorization, the outreach service can publish to `cena.learner.events.>`, the analytics consumer can modify learner streams, and a compromised service gains full cluster access. NATS decentralized auth with accounts, users, and JWTs enforces least-privilege per service at the wire level.

## Subtasks

### INF-011.1: NATS Account Creation Per Bounded Context
**Files:**
- `config/nats/accounts/operator.nk` — operator signing key (never committed — generated at setup)
- `config/nats/accounts/generate-accounts.sh` — account generation script
- `config/nats/resolver.conf` — NATS server resolver config
- `docs/nats-auth-topology.md` — account topology documentation

**Acceptance:**
- [ ] Operator key generated and stored in AWS Secrets Manager (never in git)
- [ ] One NATS account per bounded context: `learner`, `pedagogy`, `engagement`, `outreach`, `analytics`, `curriculum`, `content`, `school`, `system`
- [ ] Each account has explicit export/import declarations matching `nats-subjects.md` hierarchy
- [ ] Account `learner` exports: `cena.learner.events.>` (stream)
- [ ] Account `engagement` imports: `cena.learner.events.ConceptAttempted`, `cena.learner.events.ConceptMastered` from `learner`
- [ ] Account `outreach` imports: `cena.engagement.events.StreakExpiring`, `cena.engagement.events.ReviewDue`, `cena.learner.events.StagnationDetected`, `cena.learner.events.CognitiveLoadCooldownComplete` from respective accounts
- [ ] Account `analytics` imports: `cena.learner.events.>`, `cena.pedagogy.events.>`, `cena.engagement.events.>`, `cena.outreach.events.>` (read-only)
- [ ] Account `system` exports: `cena.system.>` (health, metrics, DLQ)
- [ ] NATS server configured with `resolver: MEMORY` for development, `resolver: NATS` (full resolver) for production
- [ ] All account JWTs have 90-day expiry with auto-rotation script

**Test:**
```bash
#!/bin/bash
# test_nats_accounts.sh — validates account isolation

# Setup: start NATS with resolver config
nats-server -c config/nats/resolver.conf &
NATS_PID=$!
sleep 2

# Test 1: Learner account can publish to learner events
nats pub cena.learner.events.ConceptAttempted '{"test":true}' \
  --creds config/nats/creds/learner.creds
echo "PASS: learner can publish to learner events"

# Test 2: Outreach account CANNOT publish to learner events
if nats pub cena.learner.events.ConceptAttempted '{"test":true}' \
  --creds config/nats/creds/outreach.creds 2>&1 | grep -q "Permissions Violation"; then
  echo "PASS: outreach cannot publish to learner events"
else
  echo "FAIL: outreach should not be able to publish to learner events"
  exit 1
fi

# Test 3: Analytics can subscribe to learner events (read-only)
timeout 3 nats sub "cena.learner.events.>" \
  --creds config/nats/creds/analytics.creds &
SUB_PID=$!
nats pub cena.learner.events.ConceptAttempted '{"test":true}' \
  --creds config/nats/creds/learner.creds
wait $SUB_PID
echo "PASS: analytics can subscribe to learner events"

# Test 4: Analytics CANNOT publish to learner events
if nats pub cena.learner.events.ConceptAttempted '{"test":true}' \
  --creds config/nats/creds/analytics.creds 2>&1 | grep -q "Permissions Violation"; then
  echo "PASS: analytics cannot publish to learner events"
else
  echo "FAIL: analytics should not be able to publish to learner events"
  exit 1
fi

kill $NATS_PID
```

---

### INF-011.2: JWT Token Provisioning Per Service Instance
**Files:**
- `config/nats/users/generate-user-jwts.sh` — user JWT generation per service
- `src/Cena.Infrastructure/Nats/NatsCredentialProvider.cs` — .NET credential loader
- `src/cena_llm/nats/credential_provider.py` — Python credential loader
- `config/nats/users/permissions/` — per-user permission definitions

**Acceptance:**
- [ ] Each service instance gets a unique user JWT under its account
- [ ] User JWTs have 24-hour expiry with automatic refresh via `NatsCredentialProvider`
- [ ] .NET `NatsCredentialProvider` loads creds from: (1) file path, (2) environment variable `NATS_CREDS`, (3) AWS Secrets Manager
- [ ] Python `credential_provider.py` loads creds from: (1) file path, (2) env var `NATS_CREDS`, (3) AWS Secrets Manager
- [ ] User-level permissions refine account-level: e.g., `learner-writer` can publish, `learner-reader` can only subscribe
- [ ] Service identity embedded in JWT: `name` claim = `{service}-{instance-id}` for audit trail
- [ ] NATS connection header `Cena-Service-Identity` set on connect for observability
- [ ] Credential rotation: zero-downtime — new JWT loaded while old connection drains

**Test:**
```csharp
[Fact]
public async Task NatsCredentialProvider_LoadsFromFile()
{
    var provider = new NatsCredentialProvider(new NatsCredentialOptions
    {
        CredentialSource = CredentialSource.File,
        FilePath = "config/nats/creds/learner.creds"
    });

    var creds = await provider.GetCredentialsAsync();
    Assert.NotNull(creds);
    Assert.NotEmpty(creds.Jwt);
    Assert.NotEmpty(creds.Seed);
}

[Fact]
public async Task NatsCredentialProvider_LoadsFromEnvVar()
{
    Environment.SetEnvironmentVariable("NATS_CREDS", Convert.ToBase64String(
        File.ReadAllBytes("config/nats/creds/learner.creds")
    ));

    var provider = new NatsCredentialProvider(new NatsCredentialOptions
    {
        CredentialSource = CredentialSource.EnvironmentVariable
    });

    var creds = await provider.GetCredentialsAsync();
    Assert.NotNull(creds);
}

[Fact]
public async Task NatsCredentialProvider_RefreshesBeforeExpiry()
{
    var provider = new NatsCredentialProvider(new NatsCredentialOptions
    {
        CredentialSource = CredentialSource.File,
        FilePath = "config/nats/creds/learner.creds",
        RefreshBeforeExpiryMinutes = 60
    });

    var creds1 = await provider.GetCredentialsAsync();
    // Simulate time passing close to expiry
    _clock.Advance(TimeSpan.FromHours(23));
    var creds2 = await provider.GetCredentialsAsync();

    // Should have refreshed
    Assert.NotEqual(creds1.Jwt, creds2.Jwt);
}
```

---

### INF-011.3: Deny Rule Enforcement & DLQ Routing
**Files:**
- `config/nats/accounts/deny-rules.json` — explicit deny rules per account
- `src/Cena.Infrastructure/Nats/NatsAuthorizationValidator.cs` — startup validation
- `tests/Cena.IntegrationTests/Nats/NatsAuthorizationTests.cs` — boundary tests

**Acceptance:**
- [ ] Explicit deny rules for each account (belt-and-suspenders with export/import):
  - `outreach` DENY publish: `cena.learner.events.>`, `cena.pedagogy.events.>`, `cena.engagement.events.>`
  - `analytics` DENY publish: `cena.*.events.>`, `cena.*.commands.>` (read-only account)
  - `content` DENY publish: `cena.learner.events.>`, `cena.pedagogy.events.>`
  - `school` DENY publish: `cena.learner.events.>` (school reads, not writes)
- [ ] DLQ publish permissions: only the DLQ handler service (in `system` account) can publish to `cena.system.dlq.>`
- [ ] All services: DENY subscribe to `cena.system.dlq.>` except `system` account (DLQ is admin-only)
- [ ] Violation logged: NATS server advisory `$SYS.ACCOUNT.*.DISCONNECT` captured and forwarded to `cena.system.metrics.NatsAuthViolation`
- [ ] Startup validator: `NatsAuthorizationValidator.ValidateOnStartup()` tests publish/subscribe permissions match expected config, fails fast if mismatch

**Test:**
```csharp
[Theory]
[InlineData("outreach", "cena.learner.events.ConceptAttempted", false)]
[InlineData("outreach", "cena.outreach.events.MessageSent", true)]
[InlineData("analytics", "cena.learner.events.ConceptAttempted", false)]  // analytics cannot PUBLISH
[InlineData("learner", "cena.learner.events.ConceptAttempted", true)]
[InlineData("content", "cena.content.events.ContentPublished", true)]
[InlineData("content", "cena.learner.events.ConceptAttempted", false)]
[InlineData("school", "cena.school.events.StudentEnrolled", true)]
[InlineData("school", "cena.learner.events.ConceptAttempted", false)]
public async Task PublishPermission_EnforcedPerAccount(string account, string subject, bool shouldSucceed)
{
    var connection = await ConnectAs(account);
    if (shouldSucceed)
    {
        await connection.PublishAsync(subject, new byte[] { 0x01 });
        // No exception = success
    }
    else
    {
        await Assert.ThrowsAsync<NatsException>(
            () => connection.PublishAsync(subject, new byte[] { 0x01 }));
    }
}

[Fact]
public async Task UnauthorizedService_ConnectionRejected()
{
    // Use a completely unknown credential
    var opts = NatsOpts.Default with { Url = _natsUrl };
    await Assert.ThrowsAsync<NatsException>(
        () => new NatsConnection(opts).ConnectAsync());
}

[Fact]
public async Task DlqPublish_OnlyAllowedFromSystemAccount()
{
    var systemConn = await ConnectAs("system");
    await systemConn.PublishAsync("cena.system.dlq.learner.ConceptAttempted", new byte[] { 0x01 });
    // Success

    var learnerConn = await ConnectAs("learner");
    await Assert.ThrowsAsync<NatsException>(
        () => learnerConn.PublishAsync("cena.system.dlq.learner.ConceptAttempted", new byte[] { 0x01 }));
}

[Fact]
public async Task StartupValidator_DetectsMismatch()
{
    // Simulate misconfigured permissions
    var validator = new NatsAuthorizationValidator(_connection, ExpectedPermissions.Learner);
    var result = await validator.ValidateOnStartup();

    Assert.True(result.IsValid);
    Assert.Empty(result.Violations);
}
```

**Edge cases:**
- Account JWT expired mid-session → NATS disconnects, auto-reconnect with refreshed JWT
- New bounded context added → must create account + update all import/export rules before deployment
- NATS cluster partition → messages queue locally, publish on reconnect; authorization state is per-server (no split-brain for auth)

---

## Integration Test (full authorization flow)

```csharp
[Fact]
public async Task FullAuthFlow_CrossContextEventRouting()
{
    // 1. Learner publishes ConceptAttempted
    var learnerConn = await ConnectAs("learner");
    await learnerConn.PublishAsync("cena.learner.events.ConceptAttempted",
        JsonSerializer.SerializeToUtf8Bytes(new { studentId = "s1", conceptId = "algebra-1" }));

    // 2. Engagement receives it (imported)
    var engagementConn = await ConnectAs("engagement");
    var sub = await engagementConn.SubscribeAsync<byte[]>("cena.learner.events.ConceptAttempted");
    var msg = await sub.Msgs.ReadAsync(CancellationToken.None);
    Assert.NotNull(msg.Data);

    // 3. Analytics receives it (imported, read-only)
    var analyticsConn = await ConnectAs("analytics");
    var analyticsSub = await analyticsConn.SubscribeAsync<byte[]>("cena.learner.events.ConceptAttempted");
    // Re-publish for analytics to see
    await learnerConn.PublishAsync("cena.learner.events.ConceptAttempted",
        JsonSerializer.SerializeToUtf8Bytes(new { studentId = "s1", conceptId = "algebra-1" }));
    var analyticsMsg = await analyticsSub.Msgs.ReadAsync(CancellationToken.None);
    Assert.NotNull(analyticsMsg.Data);

    // 4. Outreach CANNOT receive learner events directly (must go through engagement)
    var outreachConn = await ConnectAs("outreach");
    await Assert.ThrowsAsync<NatsException>(
        () => outreachConn.SubscribeAsync<byte[]>("cena.learner.events.ConceptAttempted"));
}
```

## Rollback Criteria
- If NATS auth causes >1% message delivery failure: disable account auth, revert to open cluster with network-level isolation (VPC only)
- If JWT refresh causes connection storms: extend JWT TTL to 7 days
- If export/import config causes circular dependencies: flatten to 3 accounts (core, supporting, infrastructure)

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] No operator key, seed, or credential file committed to git
- [ ] `nats server check connection` passes for all 9 accounts
- [ ] Authorization violations logged to `cena.system.metrics.NatsAuthViolation`
- [ ] Zero permission violations in 24-hour staging soak test
- [ ] Account topology documented in `docs/nats-auth-topology.md`
- [ ] PR reviewed by architect (you)
