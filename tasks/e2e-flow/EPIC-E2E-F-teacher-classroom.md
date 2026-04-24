# EPIC-E2E-F — Teacher & classroom operations

**Status**: Proposed
**Priority**: P1 (drives institute-tier retention)
**Related ADRs**: [ADR-0044](../../docs/adr/0044-teacher-schedule-override.md), ADR-0003 (k=10 privacy floor), [RDY-070](../readiness/done/RDY-070-f6-teacher-heatmap-console.md)

---

## Why this exists

Teacher workflows touch cross-student aggregation — the #1 vector for accidental PII leaks (one student's name showing on a classroom dashboard, mastery numbers that de-anonymize a struggling student).

## Workflows

### E2E-F-01 — Heatmap landing (RDY-070)

**Journey**: teacher signs in → `/apps/teacher/heatmap` → topic × difficulty × methodology grid renders → color + pattern encoding (non-color-alone per WCAG) → cell click → drilldown.

**Boundaries**: DOM (grid renders, aria-labels present), DB (ICoverageCellVariantCounter aggregates by institute), RBAC (teacher of institute A cannot see institute B's data).

**Regression caught**: teacher of a different tenant sees this one's rows; color-only encoding slips in (accessibility regression); k<10 cells expose individual students.

### E2E-F-02 — "Assign 15 min" homework from heatmap

**Journey**: teacher picks a struggling topic on heatmap → "Assign 15 min" action → picks classroom roster → emits `HomeworkAssignedV1` → student's next session surfaces this as priority bucket.

**Boundaries**: DOM (modal, roster picker), DB (HomeworkAssignment row), bus event, student side sees prioritized topic on `/home`.

**Regression caught**: homework assigned to wrong classroom; student's priority bucket not updated; teacher attempts to assign past their class's scope.

### E2E-F-03 — Classroom analytics with k=10 floor (prr-026)

**Journey**: teacher visits `/apps/classroom/analytics` → stats shown only when classroom has ≥10 active students → below floor: helpful message, no numbers.

**Boundaries**: DOM (< 10 shows privacy message, ≥ 10 shows stats), backend enforces the floor (frontend CAN'T bypass with dev tools).

**Regression caught**: k-floor enforced only on frontend; teacher with 5 students can inspect individual mastery numbers (cross-student de-anonymization).

### E2E-F-04 — Schedule override (ADR-0044)

**Journey**: teacher changes classroom schedule for next week (holiday, exam prep week) → students see updated plan cadence on `/home`.

**Boundaries**: DOM student-side shows new plan, DB ScheduleOverride row, bus `ScheduleOverriddenV1`, plan regeneration respects override on next materialization.

**Regression caught**: override applied to wrong classroom; plan regeneration lags by > 24h; override doesn't propagate to parent's dashboard.

### E2E-F-05 — Struggling-topic surface (prr-049)

**Journey**: teacher dashboard shows "Topics where ≥3 students struggled this week" — topic, student count, suggested intervention.

**Boundaries**: DB projection refreshes (≤ 1h async per scaling config), UI shows actionable numbers (not vanity streak count per shipgate GD-004).

**Regression caught**: banned engagement copy slips in ("15-day streak!"); wrong denominator (includes inactive students); topic names not localized.

## Out of scope

- Per-student profile drilldown — covered by EPIC-E2E-G admin flows
- Real-time classroom observation (live monitor) — admin/ops surface, not teacher

## Definition of Done

- [ ] 5 workflows green
- [ ] F-03 (k=10) has a separate backend assertion (hit `/api/teacher/analytics` with 9 students → empty body; with 10 → populated)
- [ ] F-01, F-03 tagged `@privacy @k-floor` — blocks merge if red
- [ ] i18n-locale-aware assertions for topic names (test runs in en/ar/he)
