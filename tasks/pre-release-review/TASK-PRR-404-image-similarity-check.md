# TASK-PRR-404: Image-similarity check — reject re-uploads in tight window

**Priority**: P1
**Effort**: M (1 week)
**Lens consensus**: persona #6 engineering (prevent "keep re-asking until I like the answer")
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + ML
**Tags**: epic=epic-prr-j, abuse-prevention, priority=p1
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Reject near-identical re-uploads within a short window (e.g., 5 min). Prevents "re-upload until system gives the diagnosis I want" gaming.

## Scope

- Perceptual hash of uploaded photo before delete.
- Recent-hash bloom per student; near-match → friendly rejection with "wait 5 min or edit your work".
- Hash stored with existing hash-ledger (PRR-246 from EPIC-PRR-H).

## Files

- `src/backend/Cena.Diagnostic/Intake/ImageSimilarityCheck.cs`
- Tests.

## Definition of Done

- Near-duplicate re-uploads rejected.
- Novel uploads accepted.
- Full sln green.

## Non-negotiable references

- Memory "No stubs".

## Reporting

complete via: standard queue complete.

## Related

- [EPIC-PRR-H PRR-246](EPIC-PRR-H-student-input-modalities.md) — hash ledger reuse
