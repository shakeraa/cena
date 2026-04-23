# TASK-PRR-422: End-to-end latency SLO (<15 sec p95)

**Priority**: P0
**Effort**: M (1-2 weeks perf + tuning)
**Lens consensus**: persona #1 student (responsiveness), #6 engineering
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: backend + devops
**Tags**: epic=epic-prr-j, performance, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Measured end-to-end latency from photo-upload to diagnostic-return: **p95 < 15 seconds**.

## Scope

- Telemetry at each pipeline stage: upload, OCR, step-extraction, CAS chain, template match, render.
- SLO alert fires when breached over rolling 1h.
- Progress indicator UX during wait (shipgate-compliant, no countdown).

## Files

- Telemetry spans.
- `src/student/full-version/src/components/diagnostic/AnalysisProgress.vue`
- Grafana dashboard config.

## Definition of Done

- p95 <15 sec in steady-state traffic.
- Telemetry surfaces bottlenecks.
- Progress UX shipgate-compliant.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) — no countdown UI.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-360](TASK-PRR-360-step-chain-verifier.md), [PRR-374](TASK-PRR-374-template-matching-scorer.md)
