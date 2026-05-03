# TASK-E2E-I-03: Right-to-erasure cascade — crypto-shred invariant (ADR-0038)

**Status**: Proposed
**Priority**: P0
**Epic**: [EPIC-E2E-I](EPIC-E2E-I-gdpr-compliance.md)
**Tag**: `@gdpr @compliance @ship-gate @p0`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/rtbf-crypto-shred.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

Builds on [E-08](TASK-E2E-E-08-right-to-erasure-parent.md) + [G-08](TASK-E2E-G-08-rtbf-admin.md). This variant asserts the crypto-shred invariant: after erasure, encrypted personal columns are un-decryptable (pepper rotated out).

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DB | Encrypted personal columns contain ciphertext that decrypts to nothing |
| Manifest | Lists every cascade target |
| Aggregates | Preserved as tombstones (not hard-deleted) for replay-ability |

## Regression this catches

Personal data left in plaintext; cascade missed a projection; aggregates hard-deleted.

## Done when

- [ ] Spec lands
- [ ] Uses same cascade as E-08 + G-08
- [ ] Tagged `@ship-gate @p0`
