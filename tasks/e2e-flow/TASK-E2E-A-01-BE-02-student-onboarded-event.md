# TASK-E2E-A-01-BE-02: `StudentOnboardedV1` event + NATS emitter

**Status**: Proposed
**Priority**: P0
**Parent**: [TASK-E2E-A-01](TASK-E2E-A-01-student-register.md)
**Epic**: [EPIC-E2E-A](EPIC-E2E-A-auth-onboarding.md)
**Tag**: `@auth @p0 @backend @bus`

## Why this is a prereq

A-01's 4th boundary asserts that `StudentOnboardedV1` appears on `cena.events.student.*.onboarded` within 5s of registration. The event type doesn't exist in the repo (`grep -r "StudentOnboardedV1"` returns zero hits). Without it, the downstream projection fan-out (admin analytics, parent-notifications, recommendation cold-start) has no trigger and A-01's bus boundary can't be wired.

## What to build

1. Event contract `StudentOnboardedV1` in the canonical events project (`src/actors/Cena.Actors/Events/` or wherever `*V1` siblings live — follow existing versioned-event convention):
   - `uid: string`
   - `email: string`
   - `tenant_id: string`
   - `school_id: string`
   - `onboarded_at: datetime`
   - `correlation_id: guid` (for trace stitching)
2. Emitter wired into the `on-first-sign-in` endpoint (TASK-E2E-A-01-BE-01). Subject shape: `cena.events.student.{uid}.onboarded`. Stream + subject config documented alongside the existing `StudentBus` entries in the JetStream bootstrapper.
3. Consumers (out-of-scope for this task but noted for visibility): admin analytics projection, parent-notification fanout — they'll ship under their own feature tasks once the event is published.
4. Integration test in `Cena.Actors.Tests`: triggers `on-first-sign-in` → asserts exactly one `StudentOnboardedV1` envelope lands on the expected subject within 5s.

## Boundary tests to unblock

- A-01 boundary #4b (bus event assertion)
- Any future spec that relies on `StudentOnboardedV1` downstream projections

## Done when

- [ ] Event type + schema in the events project, with versioning comment explaining the V1 contract
- [ ] Emitter invoked by `on-first-sign-in`, idempotent per uid (second call → no duplicate emission)
- [ ] Integration test lands under `src/actors/Cena.Actors.Tests/`
- [ ] JetStream subject documented in the stream config
- [ ] TASK-E2E-INFRA-01 bus-probe can receive the event in A-01's spec run
