# DATA-011: Crypto-Shredding for GDPR Compliance

**Priority:** P1 — legal compliance, blocks production with EU-resident students
**Blocked by:** DATA-001 (Marten event store), INF-004 (AWS KMS)
**Estimated effort:** 4 days
**Contract:** `contracts/data/marten-event-store.cs` (event types with PII fields), `contracts/llm/acl-interfaces.py` (PII annotation), `contracts/llm/routing-config.yaml` (section 8: PII handling)

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.** Every line must be real, working logic. See `tasks/00-master-plan.md` for the full rule. `throw UnimplementedError`, `// TODO: implement`, empty bodies, and mock returns are FORBIDDEN in source code. If you cannot implement it fully, file a blocking dependency instead.

## Context
Israeli privacy law (Protection of Privacy Law 5741-1981) and GDPR (for any EU-resident students) require the ability to permanently delete all personal data for a given student. In an append-only event store, you cannot delete individual events without breaking the stream. Crypto-shredding solves this: every student's PII fields are encrypted with a per-student AES-256-GCM key stored in AWS KMS. When a deletion request arrives, we destroy the KMS key — rendering all encrypted PII fields unreadable, while preserving the event stream structure and non-PII analytics data. This task implements the encryption envelope, Marten serialization hooks, KMS key lifecycle, and the deletion ceremony.

## Subtasks

### DATA-011.1: Per-Student AES-256-GCM Key Generation & KMS Envelope
**Files to create/modify:**
- `src/Cena.Data/Crypto/StudentKeyManager.cs` — per-student data encryption key (DEK) lifecycle
- `src/Cena.Data/Crypto/KmsEnvelopeEncryption.cs` — envelope encryption using AWS KMS
- `src/Cena.Data/Crypto/CryptoConstants.cs` — algorithm constants

**Acceptance:**
- [ ] Envelope encryption pattern:
  1. Per-student Data Encryption Key (DEK): AES-256-GCM, 256-bit key, 96-bit nonce
  2. DEK encrypted by AWS KMS Customer Master Key (CMK) -> stored as ciphertext blob alongside student record
  3. Plaintext DEK cached in memory for active sessions (cleared on session end)
- [ ] `GenerateStudentKey(studentId)` -> generates DEK, encrypts with KMS CMK, stores encrypted DEK in PostgreSQL
- [ ] `GetStudentKey(studentId)` -> retrieves encrypted DEK, decrypts via KMS, caches plaintext DEK
- [ ] `DestroyStudentKey(studentId)` -> schedules KMS key deletion (7-day minimum), deletes encrypted DEK from PostgreSQL
- [ ] DEK storage table in PostgreSQL (within `cena` schema):
  ```sql
  CREATE TABLE cena.student_data_keys (
    student_id TEXT PRIMARY KEY,
    encrypted_dek BYTEA NOT NULL,
    kms_key_id TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    destroyed_at TIMESTAMPTZ NULL
  );
  ```
- [ ] KMS CMK ARN from env: `CENA_KMS_CMK_ARN`
- [ ] AES-256-GCM with 96-bit nonce, authentication tag appended to ciphertext
- [ ] Nonce is unique per encryption operation (CSPRNG)

**Test:**
```csharp
[Fact]
public async Task GenerateStudentKey_StoresEncryptedDek()
{
    var kms = new MockKmsClient();
    var db = CreateTestDb();
    var manager = new StudentKeyManager(kms, db, "arn:aws:kms:eu-west-1:123:key/test-cmk");

    await manager.GenerateStudentKeyAsync("student-001");

    var row = await db.QuerySingleAsync(
        "SELECT encrypted_dek, kms_key_id FROM cena.student_data_keys WHERE student_id = @id",
        new { id = "student-001" }
    );
    Assert.NotNull(row.encrypted_dek);
    Assert.Equal("arn:aws:kms:eu-west-1:123:key/test-cmk", row.kms_key_id);
}

[Fact]
public async Task GetStudentKey_ReturnsDecryptedDek()
{
    var kms = new MockKmsClient();
    var db = CreateTestDb();
    var manager = new StudentKeyManager(kms, db, "arn:aws:kms:eu-west-1:123:key/test-cmk");

    await manager.GenerateStudentKeyAsync("student-001");
    var dek = await manager.GetStudentKeyAsync("student-001");

    Assert.NotNull(dek);
    Assert.Equal(32, dek.Length); // 256-bit key = 32 bytes
}

[Fact]
public async Task DestroyStudentKey_RendersDataUnreadable()
{
    var kms = new MockKmsClient();
    var db = CreateTestDb();
    var manager = new StudentKeyManager(kms, db, "arn:aws:kms:eu-west-1:123:key/test-cmk");

    await manager.GenerateStudentKeyAsync("student-001");
    var originalDek = await manager.GetStudentKeyAsync("student-001");

    await manager.DestroyStudentKeyAsync("student-001");

    // Key should no longer be retrievable
    await Assert.ThrowsAsync<KeyNotFoundException>(
        () => manager.GetStudentKeyAsync("student-001")
    );

    // Verify destroyed_at is set
    var row = await db.QuerySingleAsync(
        "SELECT destroyed_at FROM cena.student_data_keys WHERE student_id = @id",
        new { id = "student-001" }
    );
    Assert.NotNull(row.destroyed_at);
}

[Fact]
public async Task GenerateTwoStudents_DifferentKeys()
{
    var kms = new MockKmsClient();
    var db = CreateTestDb();
    var manager = new StudentKeyManager(kms, db, "arn:aws:kms:eu-west-1:123:key/test-cmk");

    await manager.GenerateStudentKeyAsync("student-001");
    await manager.GenerateStudentKeyAsync("student-002");

    var dek1 = await manager.GetStudentKeyAsync("student-001");
    var dek2 = await manager.GetStudentKeyAsync("student-002");

    Assert.False(dek1.SequenceEqual(dek2));
}
```

**Edge cases:**
- KMS unavailable -> cache plaintext DEK for active sessions; new key generation fails with retry
- Student requests deletion while session is active -> end session first, then destroy key
- Key already destroyed (double deletion) -> idempotent, log WARNING, return success
- KMS key rotation (annual) -> old CMK still decrypts existing DEKs (KMS handles this)

---

### DATA-011.2: Marten Serialization Hook for PII Encryption
**Files to create/modify:**
- `src/Cena.Data/Crypto/PiiEncryptionSerializer.cs` — custom Marten serializer wrapping System.Text.Json
- `src/Cena.Data/Crypto/PiiFieldAttribute.cs` — attribute marking PII fields on events

**Acceptance:**
- [ ] Custom serializer wraps Marten's default System.Text.Json serializer
- [ ] On serialization (event append): PII-annotated fields are encrypted with the student's DEK before JSON storage
- [ ] On deserialization (event read): PII fields are decrypted with the student's DEK after JSON parsing
- [ ] PII fields identified from contracts:
  - `ConceptAttempted_V1.StudentId`
  - `ConceptMastered_V1.StudentId`
  - `SessionStarted_V1.StudentId`
  - `SessionEnded_V1.StudentId`
  - All event types containing `StudentId` field
  - `AnnotationAdded_V1.ContentHash` (student annotations even hashed could be PII-adjacent)
- [ ] Encrypted field format: Base64-encoded `nonce || ciphertext || auth_tag`
- [ ] Non-PII fields (ConceptId, IsCorrect, ResponseTimeMs, etc.) remain in plaintext for analytics
- [ ] After key destruction, encrypted fields deserialize to `"[REDACTED]"` string instead of throwing

**Test:**
```csharp
[Fact]
public async Task PiiSerializer_EncryptsStudentId()
{
    var keyManager = await CreateKeyManagerWithStudentKey("student-001");
    var serializer = new PiiEncryptionSerializer(keyManager);

    var evt = new ConceptAttempted_V1(
        StudentId: "student-001", ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 2, AnswerChangeCount: 1, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    );

    var json = serializer.Serialize(evt);
    var jsonStr = System.Text.Encoding.UTF8.GetString(json);

    // StudentId should NOT appear in plaintext
    Assert.DoesNotContain("student-001", jsonStr);
    // ConceptId should still be plaintext (not PII)
    Assert.Contains("math-fractions", jsonStr);
}

[Fact]
public async Task PiiSerializer_DecryptsCorrectly()
{
    var keyManager = await CreateKeyManagerWithStudentKey("student-001");
    var serializer = new PiiEncryptionSerializer(keyManager);

    var original = new ConceptAttempted_V1(
        StudentId: "student-001", ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 2, AnswerChangeCount: 1, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    );

    var json = serializer.Serialize(original);
    var deserialized = serializer.Deserialize<ConceptAttempted_V1>(json);

    Assert.Equal("student-001", deserialized.StudentId);
    Assert.Equal("math-fractions", deserialized.ConceptId);
}

[Fact]
public async Task PiiSerializer_AfterKeyDestruction_ReturnsRedacted()
{
    var keyManager = await CreateKeyManagerWithStudentKey("student-001");
    var serializer = new PiiEncryptionSerializer(keyManager);

    var evt = new ConceptAttempted_V1(
        StudentId: "student-001", ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    );

    var json = serializer.Serialize(evt);

    // Destroy the key
    await keyManager.DestroyStudentKeyAsync("student-001");

    // Deserialization should return [REDACTED] for PII fields
    var deserialized = serializer.Deserialize<ConceptAttempted_V1>(json);
    Assert.Equal("[REDACTED]", deserialized.StudentId);
    // Non-PII fields still readable
    Assert.Equal("math-fractions", deserialized.ConceptId);
    Assert.True(deserialized.IsCorrect);
}

[Fact]
public async Task PiiSerializer_NonPiiFields_NeverEncrypted()
{
    var keyManager = await CreateKeyManagerWithStudentKey("student-001");
    var serializer = new PiiEncryptionSerializer(keyManager);

    var evt = new XpAwarded_V1(
        StudentId: "student-001", XpAmount: 30, Source: "exercise_correct",
        TotalXp: 500, DifficultyLevel: "application", DifficultyMultiplier: 3
    );

    var json = serializer.Serialize(evt);
    var jsonStr = System.Text.Encoding.UTF8.GetString(json);

    // Non-PII fields in plaintext
    Assert.Contains("exercise_correct", jsonStr);
    Assert.Contains("500", jsonStr);
    // PII field encrypted
    Assert.DoesNotContain("student-001", jsonStr);
}
```

**Edge cases:**
- Event with null PII field -> skip encryption, store null
- Very long student ID (UUID format) -> AES-GCM handles variable-length plaintext
- Concurrent reads on same student key -> DEK cached after first decrypt, subsequent reads use cache
- Marten snapshot rebuild after key destruction -> snapshot Apply receives `[REDACTED]` for StudentId, must handle gracefully

---

### DATA-011.3: Deletion Ceremony (Right to Erasure)
**Files to create/modify:**
- `src/Cena.Data/Crypto/DeletionCeremony.cs` — orchestrates the full deletion workflow
- `src/Cena.Data/Crypto/DeletionAuditLog.cs` — immutable audit trail of deletion requests

**Acceptance:**
- [ ] Deletion ceremony steps (in order):
  1. Validate deletion request (admin-initiated or student-initiated, requires authorization)
  2. End all active sessions for the student
  3. Destroy DEK via `StudentKeyManager.DestroyStudentKeyAsync()`
  4. Delete Redis keys: `cena:session:state:{studentId}`, `cena:budget:tokens:{studentId}:*`, `cena:ratelimit:*:{studentId}`, `cena:idempotency:event:{studentId}:*`
  5. Delete Neo4j student-specific data (if any — currently MCM is concept-level, not student-level)
  6. Emit domain event: `StudentDataDeleted_V1(StudentId, DeletedAt, DeletedBy, DeletionReason)`
  7. Log audit record: deletion timestamp, who authorized, what was deleted, confirmation hash
- [ ] Audit log stored in separate PostgreSQL table (NOT in the event store — audit log must survive even if student data is shredded):
  ```sql
  CREATE TABLE cena.deletion_audit_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    student_id_hash TEXT NOT NULL,  -- SHA-256 hash, NOT the actual student ID
    requested_at TIMESTAMPTZ NOT NULL,
    completed_at TIMESTAMPTZ,
    requested_by TEXT NOT NULL,     -- admin user ID
    deletion_reason TEXT NOT NULL,  -- "student_request" | "parent_request" | "legal_order" | "data_retention_expiry"
    steps_completed JSONB NOT NULL DEFAULT '[]',
    confirmation_hash TEXT,         -- SHA-256 of all deleted key material
    status TEXT NOT NULL DEFAULT 'pending'  -- "pending" | "in_progress" | "completed" | "failed"
  );
  ```
- [ ] Deletion is irreversible after KMS key destruction window (7-day grace period)
- [ ] During 7-day grace period: admin can cancel deletion (KMS key not yet destroyed)
- [ ] After 7 days: KMS automatically destroys the key, data is permanently unrecoverable
- [ ] `StudentDataDeleted_V1` event appended to a special `_deletions` stream (not the student's stream)

**Test:**
```csharp
[Fact]
public async Task DeletionCeremony_CompletesAllSteps()
{
    var ceremony = await CreateTestCeremony("student-001");
    var result = await ceremony.ExecuteAsync(
        studentId: "student-001",
        requestedBy: "admin-001",
        reason: "student_request"
    );

    Assert.Equal("completed", result.Status);
    Assert.True(result.StepsCompleted.Contains("destroy_dek"));
    Assert.True(result.StepsCompleted.Contains("clear_redis"));
    Assert.True(result.StepsCompleted.Contains("emit_event"));
    Assert.True(result.StepsCompleted.Contains("audit_logged"));
}

[Fact]
public async Task DeletionCeremony_AuditLog_NeverContainsRawStudentId()
{
    var ceremony = await CreateTestCeremony("student-001");
    await ceremony.ExecuteAsync("student-001", "admin-001", "student_request");

    var db = ceremony.GetDb();
    var log = await db.QuerySingleAsync(
        "SELECT student_id_hash FROM cena.deletion_audit_log LIMIT 1"
    );
    Assert.NotEqual("student-001", log.student_id_hash);
    Assert.Equal(64, log.student_id_hash.Length); // Full SHA-256 hex
}

[Fact]
public async Task DeletionCeremony_CanBeCancelledWithinGracePeriod()
{
    var ceremony = await CreateTestCeremony("student-001");
    var result = await ceremony.ExecuteAsync("student-001", "admin-001", "student_request");

    // Within 7-day grace period, cancellation is possible
    var cancelled = await ceremony.CancelDeletionAsync("student-001", "admin-001");
    Assert.True(cancelled);

    // Student data is still accessible
    var keyManager = ceremony.GetKeyManager();
    var dek = await keyManager.GetStudentKeyAsync("student-001");
    Assert.NotNull(dek);
}

[Fact]
public async Task DeletionCeremony_AfterGracePeriod_Irreversible()
{
    var ceremony = await CreateTestCeremony("student-001");
    await ceremony.ExecuteAsync("student-001", "admin-001", "student_request");

    // Simulate KMS key destruction (past 7-day window)
    await ceremony.SimulateKmsKeyDestruction("student-001");

    var cancelled = await ceremony.CancelDeletionAsync("student-001", "admin-001");
    Assert.False(cancelled); // Too late, key is gone
}

[Fact]
public async Task DeletionCeremony_ClearsRedisKeys()
{
    var ceremony = await CreateTestCeremony("student-001");
    var redis = ceremony.GetRedis();

    // Populate Redis with student data
    await redis.SetAsync("cena:session:state:{student-001}", "session-data", TTL.SESSION);
    await redis.IncrAsync("cena:budget:tokens:{student-001}:2026-03-26");

    await ceremony.ExecuteAsync("student-001", "admin-001", "student_request");

    // Redis keys should be gone
    Assert.Null(await redis.GetAsync("cena:session:state:{student-001}"));
    Assert.Null(await redis.GetAsync("cena:budget:tokens:{student-001}:2026-03-26"));
}
```

**Edge cases:**
- Deletion request for nonexistent student -> return success (idempotent, nothing to delete)
- Concurrent deletion requests for same student -> second request waits for first to complete (distributed lock)
- Partial failure (Redis cleared but KMS destroy fails) -> retry from last successful step, audit log tracks progress
- Analytics queries on shredded data -> queries still work, PII fields show `[REDACTED]`, aggregate stats preserved

---

### DATA-011.4: Marten Event Store Hook Registration
**Files to create/modify:**
- `src/Cena.Data/EventStore/MartenConfiguration.cs` (modify) — register crypto serializer
- `src/Cena.Data/Crypto/MartenCryptoExtensions.cs` — extension method to wire crypto into Marten

**Acceptance:**
- [ ] `ConfigureCenaEventStore()` enhanced with optional crypto-shredding:
  ```csharp
  opts.UseCryptoShredding(keyManager);  // New extension method
  ```
- [ ] Crypto serializer registered as Marten's custom serializer, wrapping the existing System.Text.Json serializer
- [ ] Serializer interceptor pattern: Marten calls `Serialize()` -> crypto serializer encrypts PII -> stores JSON; `Deserialize()` -> parses JSON -> decrypts PII
- [ ] Feature flag: `CENA_CRYPTO_SHREDDING_ENABLED` env var (default: true in production, false in dev/test for simplicity)
- [ ] When disabled: no encryption, StudentId stored in plaintext (development convenience)
- [ ] When enabled: all new events have PII encrypted, old unencrypted events still readable (backward compatible)
- [ ] Snapshot rebuild with crypto: Apply methods receive decrypted events normally; after key destruction, Apply methods receive `[REDACTED]` for StudentId

**Test:**
```csharp
[Fact]
public async Task MartenWithCrypto_RoundTripEvent()
{
    var keyManager = await CreateTestKeyManager();
    await keyManager.GenerateStudentKeyAsync("student-001");

    var store = CreateTestStore(opts =>
    {
        opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test");
        opts.UseCryptoShredding(keyManager);
    });

    var session = store.LightweightSession();
    session.Events.Append("student-001", new ConceptAttempted_V1(
        StudentId: "student-001", ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    ));
    await session.SaveChangesAsync();

    // Read back
    var events = await session.Events.FetchStreamAsync("student-001");
    var evt = (ConceptAttempted_V1)events[0].Data;
    Assert.Equal("student-001", evt.StudentId);  // Decrypted successfully
    Assert.Equal("math-fractions", evt.ConceptId); // Non-PII unchanged
}

[Fact]
public async Task MartenWithCrypto_AfterDeletion_PIIRedacted()
{
    var keyManager = await CreateTestKeyManager();
    await keyManager.GenerateStudentKeyAsync("student-001");

    var store = CreateTestStore(opts =>
    {
        opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test");
        opts.UseCryptoShredding(keyManager);
    });

    // Write event
    var session = store.LightweightSession();
    session.Events.Append("student-001", new SessionStarted_V1(
        "student-001", "sess-1", "tablet", "2.0", "socratic", null, false, DateTimeOffset.UtcNow
    ));
    await session.SaveChangesAsync();

    // Destroy key
    await keyManager.DestroyStudentKeyAsync("student-001");

    // Read back — PII should be [REDACTED]
    var events = await session.Events.FetchStreamAsync("student-001");
    var evt = (SessionStarted_V1)events[0].Data;
    Assert.Equal("[REDACTED]", evt.StudentId);
    Assert.Equal("tablet", evt.DeviceType);  // Non-PII still readable
}

[Fact]
public async Task MartenWithCrypto_DisabledByFeatureFlag()
{
    Environment.SetEnvironmentVariable("CENA_CRYPTO_SHREDDING_ENABLED", "false");
    try
    {
        var store = CreateTestStore(opts =>
        {
            opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test");
            // UseCryptoShredding not called — feature flag is off
        });

        var session = store.LightweightSession();
        session.Events.Append("student-001", new SessionStarted_V1(
            "student-001", "sess-1", "tablet", "2.0", "socratic", null, false, DateTimeOffset.UtcNow
        ));
        await session.SaveChangesAsync();

        var events = await session.Events.FetchStreamAsync("student-001");
        var evt = (SessionStarted_V1)events[0].Data;
        Assert.Equal("student-001", evt.StudentId); // Plaintext when crypto disabled
    }
    finally
    {
        Environment.SetEnvironmentVariable("CENA_CRYPTO_SHREDDING_ENABLED", null);
    }
}
```

**Edge cases:**
- Mixed encrypted/unencrypted events in same stream (gradual rollout) -> serializer detects Base64 prefix to determine if field is encrypted
- DEK cache evicted (memory pressure) -> re-fetch from KMS on next read (100ms latency hit)
- Projection rebuild scans millions of events -> batch KMS decrypt calls to avoid throttling (KMS quota: 10,000 symmetric operations/sec)

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullCryptoShredding_EndToEnd()
{
    var kms = new MockKmsClient();
    var db = CreateTestDb();
    var redis = CreateTestRedis();
    var keyManager = new StudentKeyManager(kms, db, "arn:aws:kms:eu-west-1:123:key/test-cmk");

    // 1. Generate student key
    await keyManager.GenerateStudentKeyAsync("student-001");

    // 2. Create Marten store with crypto
    var store = CreateTestStore(opts =>
    {
        opts.ConfigureCenaEventStore("Host=localhost;Database=cena_test");
        opts.UseCryptoShredding(keyManager);
    });

    // 3. Write events
    var session = store.LightweightSession();
    session.Events.Append("student-001", new SessionStarted_V1(
        "student-001", "sess-1", "tablet", "2.0", "socratic", "control", false, DateTimeOffset.UtcNow
    ));
    session.Events.Append("student-001", new ConceptAttempted_V1(
        StudentId: "student-001", ConceptId: "math-fractions", SessionId: "sess-1",
        IsCorrect: true, ResponseTimeMs: 5000, QuestionId: "q1",
        QuestionType: "numeric", MethodologyActive: "socratic",
        ErrorType: "none", PriorMastery: 0.4, PosteriorMastery: 0.55,
        HintCountUsed: 0, WasSkipped: false, AnswerHash: "abc",
        BackspaceCount: 0, AnswerChangeCount: 0, WasOffline: false,
        Timestamp: DateTimeOffset.UtcNow
    ));
    await session.SaveChangesAsync();

    // 4. Populate Redis
    await redis.SetAsync("cena:session:state:{student-001}", "data");

    // 5. Verify data is readable
    var events = await session.Events.FetchStreamAsync("student-001");
    Assert.Equal("student-001", ((SessionStarted_V1)events[0].Data).StudentId);

    // 6. Execute deletion ceremony
    var ceremony = new DeletionCeremony(keyManager, redis, store, db);
    var result = await ceremony.ExecuteAsync("student-001", "admin-001", "student_request");
    Assert.Equal("completed", result.Status);

    // 7. Verify PII is shredded
    var eventsAfter = await session.Events.FetchStreamAsync("student-001");
    Assert.Equal("[REDACTED]", ((SessionStarted_V1)eventsAfter[0].Data).StudentId);

    // 8. Non-PII still readable (analytics preserved)
    Assert.Equal("math-fractions", ((ConceptAttempted_V1)eventsAfter[1].Data).ConceptId);
    Assert.True(((ConceptAttempted_V1)eventsAfter[1].Data).IsCorrect);

    // 9. Redis cleared
    Assert.Null(await redis.GetAsync("cena:session:state:{student-001}"));

    // 10. Audit log exists
    var audit = await db.QuerySingleAsync(
        "SELECT status FROM cena.deletion_audit_log WHERE student_id_hash = @hash",
        new { hash = SHA256Hash("student-001") }
    );
    Assert.Equal("completed", audit.status);
}
```

## Rollback Criteria
If crypto-shredding causes performance or operational issues:
- Disable via feature flag `CENA_CRYPTO_SHREDDING_ENABLED=false`
- New events stored in plaintext, old encrypted events still readable (DEKs not destroyed)
- Deletion requests queued but not executed until crypto is re-enabled
- Acceptable temporary state: manual deletion via PostgreSQL admin access (non-compliant, documented risk)

## Definition of Done
- [ ] All 4 subtasks pass their individual tests
- [ ] Integration test passes
- [ ] `dotnet test --filter "Category=CryptoShredding"` -> 0 failures
- [ ] PII fields encrypted at rest in PostgreSQL event store
- [ ] PII fields decrypted transparently on read
- [ ] After key destruction, PII fields return `[REDACTED]`
- [ ] Non-PII analytics data preserved after deletion
- [ ] Deletion ceremony completes all steps: end session, destroy DEK, clear Redis, emit event, audit log
- [ ] Audit log never contains raw student ID
- [ ] 7-day grace period with cancellation support
- [ ] Feature flag disables crypto for development environments
- [ ] PR reviewed by architect (you)
