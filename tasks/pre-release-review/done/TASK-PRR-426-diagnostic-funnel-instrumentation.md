# TASK-PRR-426: Diagnostic funnel instrumentation (events from wrong-answer → resolution) (TAIL)

**Priority**: P0
**Effort**: S (3-5 days)
**Lens consensus**: tail — needed from day 1 to tune
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + data-eng
**Tags**: epic=epic-prr-j, observability, priority=p0, tail
**Status**: Ready
**Source**: tail addition 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Event stream for the whole diagnostic funnel: wrong-answer → see-CTA → click-upload → capture → preview-shown → preview-confirmed → analysis-start → analysis-complete → reflection-gate-shown → retry-submitted / hint-requested → retry-success / narration-shown → dispute-filed.

## Scope

- Event names + schemas.
- Kafka/event-stream (or project-convention equivalent).
- Dashboard rollups for success metrics (§10 of epic).

## Files

- Event schemas in `src/backend/Cena.Diagnostic/Observability/Events/`.
- Wiring across frontend + backend.
- Dashboard config.

## Definition of Done

- All funnel events emit.
- Dashboard roll-ups match epic success criteria (§10).

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — event retention aligned.

## Reporting

complete via: standard queue complete.

## Related

- Epic §10 success criteria
