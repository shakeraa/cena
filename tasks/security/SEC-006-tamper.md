# SEC-006: Offline SQLite HMAC Signing, Server Validation, Sequence Monotonicity

**Priority:** P0 — blocks offline sync trust
**Blocked by:** SEC-001 (Firebase Auth), MOB-001 (Flutter scaffold)
**Estimated effort:** 2 days
**Contract:** `contracts/frontend/offline-sync-client.ts` (QueuedEvent), `contracts/REVIEW_security.md` (C-3: SQLite Tampering)

---

## Context

Offline events are stored in client-side SQLite and replayed on reconnection. A student can modify the SQLite database to insert fabricated `ExerciseAttempted` events (all `is_correct: true`), inflating BKT mastery. The existing `queueChecksum` only verifies queue integrity, not individual event authenticity. HMAC-SHA256 signatures per event using a device-bound key (iOS Keychain / Android Keystore) make tampering detectable.

## Subtasks

### SEC-006.1: Device-Bound HMAC Key Generation + Event Signing

**Files to create/modify:**
- `src/mobile/lib/core/security/device_key_manager.dart` — Keychain/Keystore key management
- `src/mobile/lib/core/security/event_signer.dart` — HMAC-SHA256 per-event signing
- `src/mobile/lib/core/services/offline_sync_service.dart` — integrate signing into event queue

**Acceptance:**
- [ ] HMAC key generated on first app launch, stored in iOS Keychain / Android Keystore
- [ ] Key is non-extractable: cannot be read by rooted device inspection (hardware-backed where available)
- [ ] Each `QueuedEvent` gets an `hmacSignature` field: `HMAC-SHA256(key, canonicalize(event))`
- [ ] Canonical form: JSON with sorted keys, no whitespace, UTF-8 encoded
- [ ] Fields included in HMAC: `idempotencyKey`, `studentId`, `clientSeq`, `eventType`, `eventPayload`, `offlineTimestamp`
- [ ] `clockOffsetMs` and `status` excluded from HMAC (mutable fields)
- [ ] Key registered with server on first successful auth: `POST /api/devices/register { deviceId, publicKeyOrHmacKeyHash }`
- [ ] Key rotation: new key on app reinstall, old events signed with old key (server keeps key history per device)

**Test:**
```dart
test('Event signing produces consistent HMAC', () {
  final signer = EventSigner(key: testKey);
  final event = QueuedEvent(
    idempotencyKey: 'uuid-1', studentId: 'stu-1', clientSeq: 1,
    eventType: 'ExerciseAttempted', eventPayload: '{"correct":true}',
    offlineTimestamp: '2026-03-26T10:00:00Z',
  );
  final sig1 = signer.sign(event);
  final sig2 = signer.sign(event);
  expect(sig1, equals(sig2));
});

test('Tampered event produces different HMAC', () {
  final signer = EventSigner(key: testKey);
  final event = makeTestEvent(correct: true);
  final tamperedEvent = event.copyWith(eventPayload: '{"correct":false}');
  expect(signer.sign(event), isNot(equals(signer.sign(tamperedEvent))));
});
```

**Edge cases:**
- Keystore unavailable (very old Android) -> fall back to encrypted SharedPreferences with WARNING
- App data cleared but events still in SQLite -> events become unverifiable, server rejects batch
- Multiple devices for same student -> each device has its own key, server tracks per-device

---

### SEC-006.2: Server-Side HMAC Validation + Plausibility Checks

**Files to create/modify:**
- `src/Cena.Web/Services/SyncValidationService.cs` — HMAC verification + behavioral plausibility
- `src/Cena.Web/Services/DeviceKeyStore.cs` — device key registry (PostgreSQL)

**Acceptance:**
- [ ] Server verifies HMAC for every event in sync batch
- [ ] Invalid HMAC -> event rejected, logged as `tamper_detected`, student flagged
- [ ] Behavioral plausibility checks on validated events:
  - Response time < 500ms for difficulty > 5 -> flag as suspicious (too fast)
  - 100% accuracy across 20+ consecutive attempts -> flag for review
  - Events with future timestamps (> server time + 5 min) -> reject
- [ ] Flagged events still processed but mastery updates marked as `provisional`
- [ ] 3+ tamper detections per student -> account locked pending admin review

**Test:**
```csharp
[Fact]
public async Task SyncValidation_RejectsInvalidHmac()
{
    var syncRequest = CreateSyncRequestWithTamperedEvent();
    var result = await _validationService.ValidateSync(syncRequest);
    Assert.Contains(result.RejectedEvents, e => e.Reason == "invalid_hmac");
}

[Fact]
public async Task SyncValidation_FlagsSuspiciouslyFastResponses()
{
    var syncRequest = CreateSyncRequestWithEvent(responseTimeMs: 200, difficulty: 8);
    var result = await _validationService.ValidateSync(syncRequest);
    Assert.Contains(result.FlaggedEvents, e => e.Flag == "implausible_response_time");
}
```

**Edge cases:**
- Device key not yet registered (first sync) -> register key, then validate
- Clock skew > 8 hours -> use server-receive time per existing contract
- Batch contains mix of valid and invalid events -> process valid, reject invalid

---

### SEC-006.3: Sequence Monotonicity Enforcement

**Files to create/modify:**
- `src/Cena.Web/Services/SequenceValidator.cs` — enforce monotonic clientSeq per device
- `src/Cena.Data/EventStore/SequenceTracker.cs` — persist last-seen sequence per device

**Acceptance:**
- [ ] `clientSeq` must be strictly monotonically increasing per device
- [ ] Gap in sequence (e.g., seq 5, 6, 8) -> WARNING log, events accepted (events may be filtered client-side)
- [ ] Duplicate sequence -> reject duplicate, keep first occurrence (idempotency)
- [ ] Regression (e.g., seq 10, 11, 7) -> reject seq 7, log as `sequence_regression`
- [ ] Timestamp monotonicity: if seq N has timestamp T, seq N+1 must have timestamp >= T-60s (60s tolerance for clock jitter)
- [ ] Timestamp regression > 60s -> reject event, log as `timestamp_regression`
- [ ] Last-seen sequence persisted per (studentId, deviceId) in PostgreSQL

**Test:**
```csharp
[Fact]
public void SequenceValidator_RejectsRegression()
{
    var validator = new SequenceValidator();
    validator.Validate(deviceId: "dev-1", clientSeq: 10, timestamp: T(0));
    validator.Validate(deviceId: "dev-1", clientSeq: 11, timestamp: T(1));

    var result = validator.Validate(deviceId: "dev-1", clientSeq: 7, timestamp: T(2));
    Assert.Equal(ValidationResult.Rejected, result.Status);
    Assert.Equal("sequence_regression", result.Reason);
}

[Fact]
public void SequenceValidator_AcceptsGap()
{
    var validator = new SequenceValidator();
    validator.Validate(deviceId: "dev-1", clientSeq: 5, timestamp: T(0));
    var result = validator.Validate(deviceId: "dev-1", clientSeq: 8, timestamp: T(1));
    Assert.Equal(ValidationResult.Accepted, result.Status);
}
```

---

## Rollback Criteria
If HMAC validation causes sync failures for legitimate users:
- Disable HMAC validation, keep plausibility checks only
- Log all unvalidated syncs for manual review
- Ensure behavioral plausibility alone catches the most egregious tampering

## Definition of Done
- [ ] All 3 subtasks pass their individual tests
- [ ] End-to-end: offline session -> sign events -> sync -> server validates HMAC + sequence
- [ ] Tampered SQLite database detected and rejected in staging test
- [ ] `dotnet test --filter "Category=OfflineTamper"` -> 0 failures
- [ ] PR reviewed by architect
