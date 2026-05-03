# TASK-PRR-230: Parent visibility default-hidden 13+ (consent surface)

**Priority**: P1 — persona-privacy
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-privacy, persona-ethics
**Source docs**: persona-privacy findings, brief §10.6, EPIC-PRR-C (parent aggregate consent)
**Assignee hint**: kimi-coder + privacy review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, privacy, parent
**Status**: Blocked on EPIC-PRR-C (parent aggregate) + PRR-218 (target aggregate)
**Source**: persona-privacy review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Exam plan visibility from parent dashboard is:
- **Students <13**: visible by default (parent aggregate governs — COPPA + EPIC-PRR-C consent).
- **Students 13–17**: **hidden by default**; student can grant via opt-in.
- **Students ≥18**: never visible; student is adult.

## Consent surface

- On 13th birthday: student sees a one-time "your exam plan is now private by default; would you like to share with your parents?" prompt. No social-pressure copy.
- On 18th birthday: any existing parent-share is revoked automatically; parent dashboard shows "student is now 18 — plan is private".

## Implementation

- `ExamTargetVisibility { studentId, parentId, grantedAt, revokedAt? }` as separate projection.
- Parent dashboard query filters by visibility grant.
- Auto-revoke job at 18th birthday (daily worker).

## Files

- `src/actors/Cena.Actors/Parents/ExamTargetVisibilityProjection.cs` (new)
- Parent dashboard query filter update.
- Birthday-triggered revoke worker.
- Tests: under-13 visible, 13-17 hidden, 13→14 no regression, 17→18 auto-revoke, explicit grant honored.

## Definition of Done

- All three age bands behave per policy.
- Auto-revoke at 18 verified.
- Consent copy reviewed by persona-ethics + persona-privacy.
- Audit log for grant/revoke/auto-revoke events.

## Non-negotiable references

- COPPA (under-13).
- Israel PPL minor protections.
- EPIC-PRR-C (parent aggregate).
- ADR-0003, ADR-0048.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch>"`

## Related

- PRR-218, EPIC-PRR-C, PRR-014 (parent auth ADR).
