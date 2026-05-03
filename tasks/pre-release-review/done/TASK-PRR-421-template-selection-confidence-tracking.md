# TASK-PRR-421: Template-selection confidence tracking + conservative fallback

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: persona #7 ML safety
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend-dev
**Tags**: epic=epic-prr-j, ml-safety, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Track template-match confidence. When below threshold, route to conservative "let me check with your teacher" message, not fabricated narration.

## Scope

- Metric: `TemplateMatchConfidence` per diagnostic.
- Threshold config (start 0.70).
- Below-threshold path → render conservative copy, still mark break location.
- Log to observability for taxonomy iteration.

## Files

- `src/backend/Cena.Diagnostic/Misconception/TemplateConfidenceGate.cs`
- Frontend conservative-fallback rendering.
- Tests.

## Definition of Done

- Low-confidence diagnostics route to conservative path.
- Metric tracked.
- Full sln green.

## Non-negotiable references

- Memory "Honest not complimentary".
- Memory "Labels match data".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-374](TASK-PRR-374-template-matching-scorer.md), [PRR-381](TASK-PRR-381-post-reflection-narration.md)
