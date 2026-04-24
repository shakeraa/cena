# RDY-049: `QuestionCasBinding` — Composite Unique Index

- **Priority**: Medium — concurrency safety
- **Complexity**: Low (Marten config + migration)
- **Effort**: 2-4 hours

## Problem

The idempotency cache in `CasVerificationGate` only short-circuits on `Status=Verified`. Concurrent verify calls on the same `(QuestionId, CorrectAnswerHash)` can produce multiple binding rows. RDY-036 §4 required the unique index; it was never added.

## Scope

- Marten `UniqueIndex` over `(QuestionId, CorrectAnswerHash)` on `QuestionCasBinding`
- Cache hit on any `Status` where `VerifiedAt > now - 5m` returns the cached outcome
- Integration test: 10 concurrent verify calls produce exactly 1 CAS call + 1 binding row

## Acceptance

- [ ] Unique index applied in Marten config
- [ ] Concurrency test green
