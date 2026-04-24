# TASK-E2E-G-01: Bagrut PDF ingestion (RDY-057)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-G](EPIC-E2E-G-admin-operations.md)
**Tag**: `@admin @ship-gate @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/bagrut-ingestion.spec.ts`

## Journey

Admin uploads Bagrut PDF → OCR cascade → CAS gate verifies extracted items → reference-only bucket (never student-facing raw per ADR-0043) → admin reviews → approves.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Upload progress, OCR preview, approval flow |
| Storage | S3 PDF upload |
| DB | `IngestionPipeline` state transitions |
| Bus | `BagrutIngestionCompletedV1` |
| Ship-gate | Raw Ministry text never marked "shippable" |

## Regression this catches

Raw Ministry text reaches student-facing path (ADR-0043 ship blocker); OCR silently falls back to garbage; PDF upload credentials leak.

## Done when

- [ ] Spec lands
- [ ] Tagged `@ship-gate @p1`
