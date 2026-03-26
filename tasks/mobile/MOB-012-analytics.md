# MOB-012: Privacy-Safe Analytics — SHA-256 PII Hashing, Batch Upload

**Priority:** P2 — blocks data-driven decisions
**Blocked by:** MOB-005 (State)
**Estimated effort:** 1 day
**Contract:** `contracts/mobile/lib/core/services/analytics_service.dart`

---

## Context

Client-side analytics uses SHA-256 hashed student IDs — no plaintext PII ever stored or transmitted in analytics events. Events batched locally and uploaded in bulk.

## Subtasks

### MOB-012.1: Analytics Event System
- [ ] Base `AnalyticsEvent` with `hashedStudentId` (SHA-256 of Firebase UID + salt)
- [ ] Events: SessionStart, SessionEnd, QuestionAttempted, ConceptMastered, FeatureUsed
- [ ] Local queue: SQLite table, max 10,000 events before forced upload

### MOB-012.2: Batch Upload
- [ ] Upload every 5 minutes or when 100 events queued
- [ ] HTTP POST to `/api/analytics/batch` with gzip compression
- [ ] Retry on failure: 3 attempts with exponential backoff
- [ ] Events deleted from local queue after successful upload

**Test:**
```dart
test('Analytics hashes student ID', () {
  final event = SessionStartEvent(hashedStudentId: hashStudentId('uid-123'));
  expect(event.hashedStudentId, isNot(equals('uid-123')));
  expect(event.hashedStudentId.length, equals(64)); // SHA-256 hex
});
```

---

## Definition of Done
- [ ] No plaintext PII in analytics events
- [ ] Batch upload working
- [ ] PR reviewed by architect
