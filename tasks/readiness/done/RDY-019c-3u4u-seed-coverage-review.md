# RDY-019c: 3u/4u Seed + Coverage Report + Curriculum Expert Review

**Parent**: [RDY-019](tasks/readiness/RDY-019-bagrut-corpus-ingestion.md) (split 3 of 3)
**Priority**: Medium
**Complexity**: Mid engineer + curriculum expert
**Effort**: ~1 week (engineering) + expert review turnaround
**Blocker status**: BLOCKED until RDY-019a (taxonomy) merged. Partially blocked on curriculum expert availability.

## Problem

3-unit and 4-unit math tracks are either absent or mentioned only in research notes. No way to verify topic coverage across tracks. Quality gate does not surface per-topic coverage gaps.

## Scope

### 1. 3u/4u seed corpora

- Populate at least 10 seed questions per track (3-unit, 4-unit)
- Seed items go through `QuestionBankService` and are CAS-verified (ADR-0002)
- Every seed item mapped to taxonomy node from RDY-019a

### 2. Topic coverage report

Extend `src/shared/Cena.Infrastructure/Content/QualityGateService.cs`:
- Per-topic question count (by taxonomy leaf)
- Per-topic difficulty distribution (Bloom level, IRT `b` parameter histogram)
- Coverage percentage vs. taxonomy (what fraction of leaves have >=N items)
- Gap list: every taxonomy leaf with zero items flagged

Surface via admin dashboard endpoint: `GET /api/v1/admin/content/coverage`

### 3. Curriculum expert review

- Package taxonomy + coverage report for curriculum expert
- Capture sign-off record in `docs/curriculum/bagrut-taxonomy-review.md` (expert name, date, version SHA, issues raised, resolutions)

## Files to Modify

- Edit: `src/shared/Cena.Infrastructure/Content/QualityGateService.cs` (coverage method)
- New: `src/api/Cena.Admin.Api/ContentCoverageEndpoints.cs`
- New seed data: 10 × math_3u items, 10 × math_4u items (use existing seed pattern)
- New: `docs/curriculum/bagrut-taxonomy-review.md`
- Frontend: admin coverage dashboard widget (separate sub-subtask if large)

## Acceptance Criteria

- [ ] 10+ math_3u seed items, CAS-verified, taxonomy-mapped
- [ ] 10+ math_4u seed items, CAS-verified, taxonomy-mapped
- [ ] Coverage report returns structured JSON with per-leaf counts + difficulty histogram + gap list
- [ ] Admin endpoint `/api/v1/admin/content/coverage` works and is tenant-scoped
- [ ] Curriculum expert review record committed
- [ ] Full `Cena.Actors.sln` builds with 0 errors

## Coordination notes

- Depends on RDY-019a (taxonomy JSON must exist for mapping + gap computation).
- Independent of RDY-019b — can run in parallel once RDY-019a lands.
- Use `/api/v1/` prefix per RDY-010 if that lands first; otherwise plain `/api/` and update when versioning merges.
- Expert review is a real gate — cannot close without signed record.

---- events ----
2026-04-15T16:31:46.812Z  enqueued   -
