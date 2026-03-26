# SEC-006: Offline SQLite HMAC Signing and Server-Side Validation

**Priority:** P1 — mitigates mastery score manipulation
**Blocked by:** SEC-001 (authentication), ACT-002 (StudentActor sync endpoint)
**Estimated effort:** 2 days
**Contract:** `docs/offline-sync-protocol.md`, `contracts/REVIEW_security.md` C-3

---

## Context

The security review (`contracts/REVIEW_security.md` C-3) identified a critical vulnerability: the offline sync system stores events in client-side SQLite, and events like `ExerciseAttempted` are classified as `conditional`, meaning the server gives them weight during BKT mastery calculations. A student (or someone with physical device access) can modify the SQLite database to insert fabricated `ExerciseAttempted` events — all marked `is_correct: true` with fast response times — driving their mastery score toward 1.0.

The existing `queueChecksum` (SHA-256 over the full queue) only verifies queue integrity, not individual event authenticity. This task adds per-event HMAC-SHA256 signing using a device-bound key stored in iOS Keychain / Android Keystore, and server-side plausibility validation.

The offline sync protocol (`docs/offline-sync-protocol.md`) classifies events as Unconditional, Conditional, or Server-Authoritative. The HMAC protects Conditional events (the ones that affect mastery calculations). Unconditional events (annotations, session boundaries) are signed for completeness but the server accepts them regardless. Server-Authoritative events (`ConceptMastered`) are recalculated server-side and ignore client values entirely.

---

## Subtasks

### SEC-006.1: Device-Bound HMAC Key Generation and Storage

**Files to create/modify:**
- `apps/mobile/src/crypto/HmacKeyManager.ts` — key generation and storage
- `apps/mobile/src/crypto/EventSigner.ts` — per-event HMAC signing
- `apps/mobile/src/crypto/native/ios/KeychainHelper.swift` — iOS Keychain access
- `apps/mobile/src/crypto/native/android/KeystoreHelper.kt` — Android Keystore access
- `apps/mobile/src/crypto/__tests__/EventSigner.test.ts`

**Acceptance:**
- [ ] HMAC key generated on first app launch using platform secure random
- [ ] Key stored in platform-native secure storage:
  - **iOS:** Keychain with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` (survives app backgrounding, not device restore)
  - **Android:** Android Keystore with `setUserAuthenticationRequired(false)` (no biometric needed — the key is device-bound, not user-bound)
- [ ] Key is 256-bit (32 bytes), generated via `crypto.getRandomValues()` backed by platform RNG
- [ ] Key ID (first 8 bytes of SHA-256 of the key) sent to server during `SyncRequest` for key identification
- [ ] Key registered with server on first sync: `RegisterDeviceKey(deviceId, keyId, publicComponent)` — server stores the key mapping
- [ ] Key rotation: new key generated if:
  - User logs out and back in
  - App detects Keychain/Keystore tampering
  - Server requests key rotation (compromised device)
- [ ] Old events signed with old key remain valid during a 7-day grace period
- [ ] Key never leaves the device (no export, no backup, no cloud sync)

**Test:**
```typescript
describe('HmacKeyManager', () => {
  it('generates a 256-bit key on first launch', async () => {
    const manager = new HmacKeyManager();
    const key = await manager.getOrCreateKey();
    expect(key.length).toBe(32); // 256 bits
  });

  it('returns same key on subsequent calls', async () => {
    const manager = new HmacKeyManager();
    const key1 = await manager.getOrCreateKey();
    const key2 = await manager.getOrCreateKey();
    expect(key1).toEqual(key2);
  });

  it('generates new key after logout', async () => {
    const manager = new HmacKeyManager();
    const key1 = await manager.getOrCreateKey();
    await manager.rotateKey();
    const key2 = await manager.getOrCreateKey();
    expect(key1).not.toEqual(key2);
  });

  it('stores key in Keychain/Keystore, not AsyncStorage', async () => {
    const manager = new HmacKeyManager();
    await manager.getOrCreateKey();
    const asyncStorageValue = await AsyncStorage.getItem('hmac_key');
    expect(asyncStorageValue).toBeNull(); // Must NOT be in AsyncStorage
  });
});

describe('EventSigner', () => {
  it('signs an event with HMAC-SHA256', async () => {
    const signer = new EventSigner(testKey);
    const event = {
      eventType: 'ExerciseAttempted',
      timestamp: '2026-03-26T14:30:00Z',
      payload: { questionId: 'q1', isCorrect: true, responseTimeMs: 3500 }
    };

    const signature = await signer.sign(event);
    expect(signature).toMatch(/^[a-f0-9]{64}$/); // SHA-256 hex

    // Verify deterministic
    const signature2 = await signer.sign(event);
    expect(signature).toBe(signature2);
  });

  it('different payload produces different signature', async () => {
    const signer = new EventSigner(testKey);
    const event1 = { eventType: 'ExerciseAttempted', payload: { isCorrect: true } };
    const event2 = { eventType: 'ExerciseAttempted', payload: { isCorrect: false } };

    const sig1 = await signer.sign(event1);
    const sig2 = await signer.sign(event2);
    expect(sig1).not.toBe(sig2);
  });
});
```

**Edge cases:**
- App reinstalled (Keychain/Keystore cleared) — new key generated; pending unsigned events from before reinstall are sent with `signature: null` and accepted at reduced weight (0.5)
- Jailbroken iOS / rooted Android — Keychain/Keystore may be accessible; HMAC is defense-in-depth, not sole protection; server-side plausibility checks (SEC-006.3) provide the second layer
- Key ID collision (extremely unlikely with 8-byte prefix of SHA-256) — server falls back to full key comparison
- Device time manipulation — HMAC signs the event timestamp; server compares against `SyncRequest.client_clock_offset_ms` from the sync protocol

---

### SEC-006.2: Client-Side Event Signing and Queue Integrity

**Files to create/modify:**
- `apps/mobile/src/offline/OfflineEventQueue.ts` — modify to sign events on enqueue
- `apps/mobile/src/offline/SyncClient.ts` — include signatures in `SyncRequest`
- `apps/mobile/src/offline/schema/QueuedEvent.ts` — add `hmac_signature` field
- `apps/mobile/src/offline/__tests__/OfflineEventQueue.test.ts`

**Acceptance:**
- [ ] Every event enqueued in the offline SQLite database includes:
  ```typescript
  interface SignedQueuedEvent {
    // Existing fields from docs/offline-sync-protocol.md
    event_id: string;           // UUIDv7
    event_type: string;         // e.g., "ExerciseAttempted"
    timestamp: string;          // ISO 8601 UTC
    payload: object;            // Event-specific data
    classification: 'unconditional' | 'conditional' | 'server_authoritative';

    // New fields (this task)
    hmac_signature: string;     // HMAC-SHA256(key, canonical_payload) as hex
    key_id: string;             // First 8 bytes of SHA-256(key) as hex
    signing_version: number;    // Schema version for future changes (start at 1)
  }
  ```
- [ ] HMAC input is a **canonical JSON** of the event (sorted keys, no whitespace) to ensure deterministic signing:
  ```
  HMAC-SHA256(key, sort_keys(json.dumps({
    "event_id": "...",
    "event_type": "...",
    "timestamp": "...",
    "payload": { ... sorted ... }
  })))
  ```
- [ ] Queue checksum (`queue_checksum` in `SyncRequest`) now uses HMAC-SHA256 over the concatenation of all event signatures (not just SHA-256 of payloads)
- [ ] SQLite column `hmac_signature TEXT NOT NULL` added via migration
- [ ] Events signed at enqueue time (not at sync time) — signing at enqueue prevents retroactive tampering between enqueue and sync
- [ ] `SyncRequest` includes `device_key_id` field for server-side key lookup

**Test:**
```typescript
describe('OfflineEventQueue with signing', () => {
  it('signs events at enqueue time', async () => {
    const queue = new OfflineEventQueue(testDb, testSigner);
    await queue.enqueue({
      eventType: 'ExerciseAttempted',
      payload: { questionId: 'q1', isCorrect: true, responseTimeMs: 3000 }
    });

    const events = await queue.getAll();
    expect(events[0].hmac_signature).toBeDefined();
    expect(events[0].hmac_signature).toMatch(/^[a-f0-9]{64}$/);
    expect(events[0].key_id).toBeDefined();
    expect(events[0].signing_version).toBe(1);
  });

  it('queue checksum uses HMAC chain', async () => {
    const queue = new OfflineEventQueue(testDb, testSigner);
    await queue.enqueue({ eventType: 'ExerciseAttempted', payload: { isCorrect: true } });
    await queue.enqueue({ eventType: 'ExerciseAttempted', payload: { isCorrect: false } });

    const checksum = await queue.computeChecksum();
    expect(checksum).toMatch(/^hmac-sha256:[a-f0-9]{64}$/);
  });

  it('detects tampered event in SQLite', async () => {
    const queue = new OfflineEventQueue(testDb, testSigner);
    await queue.enqueue({
      eventType: 'ExerciseAttempted',
      payload: { questionId: 'q1', isCorrect: false, responseTimeMs: 15000 }
    });

    // Simulate SQLite tampering: change isCorrect to true
    await testDb.runAsync(
      "UPDATE offline_events SET payload = json_set(payload, '$.isCorrect', 1) WHERE event_type = 'ExerciseAttempted'"
    );

    // Verification should detect the tampering
    const events = await queue.getAll();
    const isValid = await testSigner.verify(events[0]);
    expect(isValid).toBe(false);
  });

  it('canonical JSON produces deterministic signatures', async () => {
    const signer = new EventSigner(testKey);
    // Same data, different key order
    const sig1 = await signer.sign({ payload: { a: 1, b: 2 }, eventType: 'test' });
    const sig2 = await signer.sign({ eventType: 'test', payload: { b: 2, a: 1 } });
    expect(sig1).toBe(sig2);
  });
});
```

**Edge cases:**
- Large offline queue (500+ events) — signing overhead is <1ms per event; total signing time for 500 events < 500ms
- SQLite WAL mode corruption during offline — events may be partially written; the HMAC will fail verification for corrupted events; server handles them as `signature: invalid`
- App update changes payload schema — `signing_version` field handles schema evolution; server validates against the correct schema version
- `JSON.stringify` varies between platforms — use a deterministic JSON serializer (sort keys, no trailing commas, UTF-8 normalized)

---

### SEC-006.3: Server-Side Validation and Plausibility Checks

**Files to create/modify:**
- `src/Cena.Infrastructure/Sync/HmacValidator.cs` — HMAC signature verification
- `src/Cena.Infrastructure/Sync/PlausibilityChecker.cs` — behavioral plausibility
- `src/Cena.Domain/Services/SyncValidationService.cs` — orchestrates validation
- `src/Cena.Actors/StudentActor.SyncHandler.cs` — modify sync handling to validate

**Acceptance:**

**HMAC Verification:**
- [ ] Server looks up device key by `device_key_id` from the `SyncRequest`
- [ ] Each event's `hmac_signature` verified against the stored key
- [ ] Events with invalid signatures:
  - Conditional events: rejected (weight = 0), logged as `TAMPER_DETECTED`
  - Unconditional events: accepted (they don't affect mastery) but flagged
  - Entire sync request: if >50% of events have invalid signatures, reject the entire batch and log `BULK_TAMPER_DETECTED` critical alert
- [ ] Events with `signature: null` (pre-HMAC events during migration): accepted at reduced weight (0.5) for 30 days after feature launch, then rejected

**Plausibility Checks (defense-in-depth, per `contracts/REVIEW_security.md` C-3):**
- [ ] Response time check: `responseTimeMs < 500ms` for questions classified as `bloom_level >= application` flags the event as `implausibly_fast`
- [ ] Accuracy pattern check: >95% accuracy over 20+ offline events triggers `implausibly_accurate` flag
- [ ] Session duration check: >200 events in a single offline session (4+ hours of non-stop perfect answers) triggers `marathon_session` flag
- [ ] Flagged events are accepted but with reduced BKT weight (0.5 instead of 1.0)
- [ ] Plausibility flags aggregated per student: if a student triggers 3+ flags in a week, emit `TamperSuspected` event for investigation

**Behavioral Fingerprinting:**
- [ ] Compare offline attempt patterns against student's historical baseline:
  - Average response time (offline) vs. historical average: >2 standard deviations triggers flag
  - Accuracy (offline) vs. historical accuracy: >2 standard deviations triggers flag
  - Response time variance (offline): perfect consistency (std dev < 100ms) across 10+ events triggers flag (bots have low variance)
- [ ] Fingerprinting is non-blocking: it only reduces weight, never rejects outright (false positives would punish legitimate students)

**Test:**
```csharp
[Fact]
public async Task ValidHmac_AcceptsEventAtFullWeight()
{
    var key = GenerateTestKey();
    var evt = CreateSignedEvent(key, isCorrect: true, responseTimeMs: 5000);

    var result = await _validator.Validate(evt, key);

    Assert.True(result.IsValid);
    Assert.Equal(1.0, result.Weight);
}

[Fact]
public async Task InvalidHmac_RejectsConditionalEvent()
{
    var key = GenerateTestKey();
    var evt = CreateSignedEvent(key, isCorrect: true, responseTimeMs: 5000);
    evt.Payload["isCorrect"] = false; // Tamper after signing

    var result = await _validator.Validate(evt, key);

    Assert.False(result.IsValid);
    Assert.Equal(0.0, result.Weight);
    Assert.Equal("TAMPER_DETECTED", result.RejectionReason);
}

[Fact]
public async Task ImplausiblyFastResponse_ReducesWeight()
{
    var key = GenerateTestKey();
    var evt = CreateSignedEvent(key, isCorrect: true, responseTimeMs: 200,
        bloomLevel: "application"); // Complex question answered in 200ms

    var result = await _plausibilityChecker.Check(evt);

    Assert.Equal(0.5, result.Weight);
    Assert.Contains("implausibly_fast", result.Flags);
}

[Fact]
public async Task PerfectAccuracyOffline_ReducesWeight()
{
    var key = GenerateTestKey();
    var events = Enumerable.Range(0, 25)
        .Select(i => CreateSignedEvent(key, isCorrect: true, responseTimeMs: 3000 + i * 100))
        .ToList();

    var results = await _plausibilityChecker.CheckBatch(events);

    Assert.True(results.Any(r => r.Flags.Contains("implausibly_accurate")));
}

[Fact]
public async Task BulkTamper_RejectsEntireBatch()
{
    var key = GenerateTestKey();
    var events = Enumerable.Range(0, 10)
        .Select(i => CreateSignedEvent(key, isCorrect: true, responseTimeMs: 3000))
        .ToList();

    // Tamper with 6 out of 10 events (>50%)
    for (int i = 0; i < 6; i++)
        events[i].HmacSignature = "0000000000000000000000000000000000000000000000000000000000000000";

    var result = await _validator.ValidateBatch(events, key);

    Assert.Equal("BULK_TAMPER_DETECTED", result.RejectionReason);
    Assert.True(result.AllRejected);
}

[Fact]
public async Task BehavioralFingerprint_DetectsAnomalousVariance()
{
    // Historical baseline: avg response time 8000ms, std dev 3000ms
    var history = new StudentBaseline { AvgResponseTimeMs = 8000, StdDevResponseTimeMs = 3000, AvgAccuracy = 0.65 };

    // Offline events: avg response time 1500ms, std dev 50ms, accuracy 1.0
    var offlineEvents = Enumerable.Range(0, 15)
        .Select(i => CreateSignedEvent(testKey, isCorrect: true, responseTimeMs: 1500 + i % 3))
        .ToList();

    var result = await _fingerprinter.Compare(offlineEvents, history);

    Assert.Contains("response_time_anomaly", result.Flags);
    Assert.Contains("low_variance_anomaly", result.Flags);
}
```

```bash
# Security test: simulate SQLite tampering and verify server rejection
# This is a manual pen-test script

# 1. Export the SQLite database from a test device
adb pull /data/data/com.cena.app/databases/offline_events.db /tmp/

# 2. Tamper with events
sqlite3 /tmp/offline_events.db "UPDATE offline_events SET payload = json_set(payload, '$.isCorrect', 1) WHERE event_type = 'ExerciseAttempted'"

# 3. Push back to device
adb push /tmp/offline_events.db /data/data/com.cena.app/databases/

# 4. Trigger sync from the app
# 5. Check server logs for TAMPER_DETECTED
# Expect: events with invalid HMAC are rejected
```

**Edge cases:**
- Student genuinely answers fast (gifted student) — plausibility only reduces weight, never rejects; after establishing a fast baseline, the fingerprinting adapts
- Device key lost (factory reset) — server has no key for this device ID; events accepted at reduced weight (0.5) until new key is registered
- Man-in-the-middle replays old signed events — each event has a unique `event_id` (UUIDv7); server-side dedup by `event_id` prevents replay
- Time zone manipulation on device — HMAC signs the timestamp; server cross-references with `client_clock_offset_ms` from the sync handshake

---

## Integration Test (all subtasks combined)

```csharp
[Fact]
public async Task FullTamperProtection_EndToEnd()
{
    // 1. Register device key
    var deviceKey = await _keyManager.GenerateKey("device-001");
    await _server.RegisterDeviceKey("student-123", "device-001", deviceKey.KeyId);

    // 2. Create signed offline events
    var signer = new EventSigner(deviceKey);
    var events = new[]
    {
        await signer.SignEvent(new { eventType = "ExerciseAttempted", payload = new { questionId = "q1", isCorrect = true, responseTimeMs = 5000 } }),
        await signer.SignEvent(new { eventType = "ExerciseAttempted", payload = new { questionId = "q2", isCorrect = false, responseTimeMs = 8000 } }),
        await signer.SignEvent(new { eventType = "AnnotationAdded", payload = new { text = "I understand now!" } })
    };

    // 3. Sync to server
    var syncResult = await _syncClient.Sync(new SyncRequest
    {
        StudentId = "student-123",
        DeviceId = "device-001",
        DeviceKeyId = deviceKey.KeyId,
        QueuedEvents = events,
        QueueChecksum = ComputeChecksum(events)
    });

    // 4. All events accepted
    Assert.Equal(3, syncResult.AcceptedCount);
    Assert.Equal(0, syncResult.RejectedCount);

    // 5. Tamper with one event and re-sync
    events[0].Payload["isCorrect"] = false; // Tamper
    syncResult = await _syncClient.Sync(new SyncRequest
    {
        StudentId = "student-123",
        DeviceId = "device-001",
        DeviceKeyId = deviceKey.KeyId,
        QueuedEvents = events,
        QueueChecksum = ComputeChecksum(events) // Checksum also invalid now
    });

    // 6. Tampered event rejected
    Assert.Equal(2, syncResult.AcceptedCount);
    Assert.Equal(1, syncResult.RejectedCount);
    Assert.Contains("TAMPER_DETECTED", syncResult.Rejections[0].Reason);
}
```

## Rollback Criteria

If this task fails or introduces instability:
- **Client-side signing:** remove HMAC requirement; server accepts all events as before (reduced security)
- **Server-side validation:** disable HMAC verification flag (`CENA_HMAC_VERIFY=false`); plausibility checks can remain active (they only reduce weight, never reject)
- **Key registration:** if key storage fails on specific devices, fall back to queue-level checksum only
- Never roll back plausibility checks alone — they are the safety net when HMAC is compromised

## Definition of Done

- [ ] All 3 subtasks pass their individual tests
- [ ] Device-bound HMAC key generated and stored in Keychain (iOS) / Keystore (Android)
- [ ] Every offline event signed with HMAC-SHA256 at enqueue time
- [ ] Server validates HMAC on every conditional event during sync
- [ ] Invalid HMAC on conditional events: rejected (weight = 0)
- [ ] Bulk tampering (>50% invalid) rejects entire sync batch
- [ ] Plausibility checks flag implausibly fast, accurate, or marathon sessions
- [ ] Behavioral fingerprinting compares offline patterns to historical baseline
- [ ] Flagged events accepted at reduced weight (0.5), never silently rejected
- [ ] 3+ flags per student per week triggers `TamperSuspected` alert
- [ ] Integration test passes end-to-end
- [ ] PR reviewed by architect and security reviewer
