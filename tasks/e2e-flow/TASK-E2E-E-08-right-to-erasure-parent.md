# TASK-E2E-E-08: Right-to-be-forgotten cascade (parent-initiated, ADR-0038)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-E](EPIC-E2E-E-parent-console.md)
**Tag**: `@gdpr @compliance @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/rtbf-parent.spec.ts`

## Journey

Parent requests child deletion → `IRightToErasureService` runs → ConsentAggregate + StudentProfile + DigestPreferences + WhatsAppRecipient all crypto-shredded → manifest shows "Preserved via ADR-0038 crypto-shred" → 90-day audit window starts.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | All personal rows shredded; aggregates preserved as tombstones |
| Negative-property | Post-erasure, encrypted personal columns decrypt to nothing (wrong key) |
| Manifest | Every cascade listed |
| Bus | `RightToErasureCompletedV1` |

## Regression this catches

Orphan row left behind (partial erasure); manifest missing a cascade; event-stream leaks original child id post-shred.

## Done when

- [ ] Spec lands
- [ ] Shares cascade implementation with G-08 (admin-initiated); one code path, two triggers
- [ ] Tagged `@gdpr @p0`
