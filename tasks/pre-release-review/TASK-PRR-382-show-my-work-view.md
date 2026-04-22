# TASK-PRR-382: Expandable "show my work" CAS-chain view

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona #3 teacher (auditability), #4 curious-student (transparency)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + math-render
**Tags**: epic=epic-prr-j, ux, teacher-facing, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

Expandable drawer surfacing the CAS verification chain from [PRR-363](TASK-PRR-363-cas-chain-export-format.md) — per-step: what the student wrote, expected vs. detected expression, operation applied, equivalence result. Teacher-auditable evidence.

## Scope

- Rendered KaTeX math, LTR inside RTL per memory "Math always LTR".
- Operation labels HE/AR/EN.
- Collapsed by default; student/teacher opts in.
- Copy-to-clipboard for teacher to include in feedback.

## Files

- `src/student/full-version/src/components/diagnostic/ShowMyWorkDrawer.vue`
- Tests.

## Definition of Done

- Drawer renders full CAS chain.
- Math displays LTR.
- Copy-to-clipboard works.

## Non-negotiable references

- Memory "Math always LTR".
- [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md).

## Reporting

complete via: standard queue complete.

## Related

- [PRR-363](TASK-PRR-363-cas-chain-export-format.md), [PRR-380](TASK-PRR-380-diagnostic-result-screen.md)
