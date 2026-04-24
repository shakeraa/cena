# SEC-005: Crypto-Shredding Orchestration — Deletion Cascade Across 5 Data Stores

**Priority:** P1 — legal compliance (GDPR Article 17, Israeli PPL)
**Blocked by:** INF-002 (RDS), INF-004 (Redis), INF-005 (S3), DATA-001, DATA-003
**Estimated effort:** 3 days
**Contract:** `contracts/data/marten-event-store.cs`, `contracts/data/redis-contracts.ts`, `contracts/data/s3-export-schema.json`, `contracts/REVIEW_security.md` (H-1)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context

Student data (minors aged 16-18) is stored across PostgreSQL (Marten events + snapshots), Redis (session cache, budgets, rate limits, idempotency), S3 (anonymized Parquet exports), NATS JetStream (90-day event retention), and Neo4j (MCM confidence scores). GDPR Article 17 right-to-erasure requires complete deletion. Crypto-shredding encrypts all PII with a per-student key stored in AWS KMS; erasure = delete the key. The append-only Marten event store makes physical deletion impossible, making crypto-shredding mandatory.

## Subtasks

### SEC-005.1: Per-Student Envelope Encryption

**Files to create/modify:**
- `src/Cena.Data/Encryption/StudentKeyManager.cs` — AWS KMS key management per student
- `src/Cena.Data/Encryption/FieldEncryptor.cs` — encrypt/decrypt PII fields in Marten events
- `src/Cena.Data/EventStore/EncryptedEventStore.cs` — Marten middleware that encrypts PII on write, decrypts on read

**Acceptance:**
- [ ] Each student gets a unique data encryption key (DEK) generated via AWS KMS `GenerateDataKey`
- [ ] DEK stored in KMS, wrapped with a master key (CMK). Only the encrypted DEK is cached locally
- [ ] PII fields in Marten events encrypted at write time: `StudentId`, `DisplayName`, `SchoolId`
- [ ] Non-PII fields (mastery scores, timestamps, concept IDs) remain in plaintext for analytics
- [ ] Read path decrypts PII fields transparently via Marten pipeline
- [ ] Key rotation: new DEK every 90 days, old events remain readable with old key
- [ ] Performance: encryption adds < 1ms per event write (AES-256-GCM)
- [ ] Key metadata stored: `{ studentId, kmsKeyId, createdAt, rotatedAt, deletedAt }`

**Test:**
```csharp
[Fact]
public async Task EncryptedEvent_DecryptsTransparently()
{
    var store = CreateEncryptedEventStore();
    var studentId = "student-123";
    await store.AppendEvent(studentId, new ConceptAttempted_V1 { StudentId = studentId, ConceptId = "math-1" });

    // Raw read should show encrypted StudentId
    var rawEvents = await ReadRawEventsFromPostgres(studentId);
    Assert.NotEqual(studentId, rawEvents[0].Data["StudentId"]);

    // Marten read should decrypt transparently
    var events = await store.FetchStream(studentId);
    Assert.Equal(studentId, ((ConceptAttempted_V1)events[0].Data).StudentId);
}

[Fact]
public async Task DeletedKey_MakesEventsUnreadable()
{
    var keyManager = new StudentKeyManager(_kmsClient);
    var studentId = "student-to-delete";
    await keyManager.DeleteKey(studentId);

    var store = CreateEncryptedEventStore();
    await Assert.ThrowsAsync<KeyDeletedException>(() => store.FetchStream(studentId));
}
```

**Edge cases:**
- KMS throttled -> retry with backoff, cache DEK for 5 minutes
- Student created before encryption enabled -> migrate existing events in background job
- Concurrent reads during key rotation -> both old and new DEK valid during rotation window

---

### SEC-005.2: Deletion Cascade Orchestrator

**Files to create/modify:**
- `src/Cena.Data/Deletion/DeletionOrchestrator.cs` — 5-store cascade coordinator
- `src/Cena.Data/Deletion/DeletionManifest.cs` — tracks deletion progress across stores
- `src/Cena.Data/Deletion/IDeletionStep.cs` — interface for per-store deletion

**Acceptance:**
- [ ] `DeleteStudentAsync(studentId)` triggers cascade across all 5 stores:
  1. **KMS**: Schedule key deletion (7-day pending window per AWS requirement)
  2. **Redis**: Delete all keys matching `cena:*:{studentId}*` (session, budget, rate limit, idempotency)
  3. **PostgreSQL**: Mark Marten stream as tombstoned (events become unreadable after KMS key deletion)
  4. **S3**: Identify and re-anonymize any Parquet exports containing this student's HMAC
  5. **NATS JetStream**: Publish `StudentDeleted` event for all consumers to purge local caches
- [ ] Deletion manifest persisted in PostgreSQL: `{ studentId, requestedAt, steps: [{store, status, completedAt}] }`
- [ ] Each step is idempotent and independently retryable
- [ ] If any step fails: continue with remaining steps, mark failed step for retry
- [ ] Final status: `completed` only when ALL 5 steps succeed
- [ ] Deletion request audit logged with the requesting user (parent/admin) and timestamp
- [ ] Maximum deletion completion time: 7 days (KMS pending deletion is the bottleneck)
- [ ] Admin dashboard shows deletion progress per student

**Test:**
```csharp
[Fact]
public async Task DeletionCascade_DeletesAcrossAllStores()
{
    var studentId = await CreateStudentWithData();
    var orchestrator = new DeletionOrchestrator(_kms, _redis, _marten, _s3, _nats);

    var manifest = await orchestrator.DeleteStudentAsync(studentId, requestedBy: "parent-uid");

    Assert.Equal(5, manifest.Steps.Count);
    Assert.All(manifest.Steps, step => Assert.True(
        step.Status == DeletionStatus.Completed || step.Status == DeletionStatus.Pending));

    // Redis keys gone immediately
    var redisKeys = await _redis.Keys($"cena:*:{studentId}*");
    Assert.Empty(redisKeys);

    // Marten stream tombstoned
    Assert.True(await IsStreamTombstoned(studentId));
}

[Fact]
public async Task DeletionCascade_RetriesFailedStep()
{
    var orchestrator = CreateOrchestratorWithFailingRedis();
    var manifest = await orchestrator.DeleteStudentAsync("student-123", requestedBy: "admin");

    Assert.Equal(DeletionStatus.Failed, manifest.Steps.First(s => s.Store == "redis").Status);
    Assert.Equal(DeletionStatus.Completed, manifest.Steps.First(s => s.Store == "kms").Status);

    // Retry
    await orchestrator.RetryFailedStepsAsync("student-123");
    var updated = await orchestrator.GetManifest("student-123");
    Assert.Equal(DeletionStatus.Completed, updated.Steps.First(s => s.Store == "redis").Status);
}
```

**Edge cases:**
- Deletion requested for student currently in active session -> passivate actor first, then delete
- Deletion requested twice -> idempotent, second request returns existing manifest
- KMS key already in pending deletion -> skip step, mark completed
- S3 export not yet generated for this student -> skip S3 step

---

### SEC-005.3: Compliance Verification + Automated Audit

**Files to create/modify:**
- `src/Cena.Data/Deletion/ComplianceVerifier.cs` — post-deletion verification
- `scripts/security/gdpr-compliance-report.sh` — monthly compliance report generator
- `tests/Security/GdprDeletionTests.cs`

**Acceptance:**
- [ ] Post-deletion verifier checks: KMS key deleted, Redis keys absent, Marten stream unreadable, NATS event published
- [ ] Monthly compliance report: total deletion requests, completion rate, average completion time, failed steps
- [ ] Report stored in S3 compliance bucket with 7-year retention
- [ ] Alert if any deletion request older than 30 days is not fully completed
- [ ] Staging test: create student, generate data across all stores, delete, verify complete erasure

**Test:**
```csharp
[Fact]
public async Task ComplianceVerifier_ConfirmsFullDeletion()
{
    var studentId = await CreateStudentWithFullDataAcrossAllStores();
    await _orchestrator.DeleteStudentAsync(studentId, requestedBy: "admin");
    await SimulateKmsKeyDeletion(studentId); // Skip 7-day wait in test

    var result = await _verifier.Verify(studentId);
    Assert.True(result.IsFullyDeleted);
    Assert.All(result.StoreResults, r => Assert.True(r.IsClean));
}
```

---

## Rollback Criteria
If crypto-shredding causes data access issues:
- Disable encryption for new events, keep existing encrypted events as-is
- Implement manual deletion runbook as interim measure
- Prioritize KMS key management stability over full cascade automation

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] End-to-end: create student -> populate all 5 stores -> delete -> verify zero data access
- [ ] `dotnet test --filter "Category=GDPR"` -> 0 failures
- [ ] Monthly compliance report generates successfully
- [ ] Legal team approves deletion procedure documentation
- [ ] PR reviewed by architect
