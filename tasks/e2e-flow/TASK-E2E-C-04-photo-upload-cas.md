# TASK-E2E-C-04: Session with photo upload (PRR-J)

**Status**: Proposed
**Priority**: P1
**Epic**: [EPIC-E2E-C](EPIC-E2E-C-student-learning-core.md)
**Tag**: `@learning @photo-pipeline @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/photo-upload-session.spec.ts`
**Prereqs**: PRR-436 admin test probe (DB boundary — queue id `t_57d2a2cb8b10`)

## Journey

`/session/{id}` → handwritten answer → camera/file upload → OCR cascade → CAS verifies extracted answer → feedback same as typed-answer path.

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| DOM | Camera modal, preview, feedback render correctly |
| Storage | S3 upload with signed URL; object respects retention policy (PRR-412) |
| DB | `PhotoUploadedV1` → `OcrCompletedV1` → `AnswerSubmittedV1` chain |
| CAS | Extracted answer verified via SymPy sidecar before feedback shown |

## Regression this catches

S3 upload credential leak (PRR-414 / PRR-412); OCR silently falls back to garbage; CAS-unverified photo-answer marked correct.

## Done when

- [ ] Spec lands
- [ ] Retention-deletion fast-forward tested
- [ ] Tagged `@photo-pipeline @p1`
