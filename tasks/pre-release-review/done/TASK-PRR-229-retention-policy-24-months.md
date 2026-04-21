# TASK-PRR-229: 24-month post-archive retention policy + user-extend opt-in

**Priority**: P1 — persona-privacy
**Effort**: S (3-5 days)
**Lens consensus**: persona-privacy
**Source docs**: persona-privacy findings (Section 8 indefinite-retention rejected; PPL Amendment 13 + GDPR Art. 5(1)(e))
**Assignee hint**: kimi-coder + privacy review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p1, privacy, retention
**Status**: Blocked on PRR-217 (ADR records the policy), PRR-218 (aggregate fields)
**Source**: persona-privacy review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Bound archived-target retention at 24 months post-archive with user-extendable opt-in. Replaces the brief's "retained indefinitely" language which violates PPL Amendment 13 purpose-limitation + GDPR Article 5(1)(e).

## Policy

- `ArchivedAt + 24 months` → retention worker crypto-shreds event payload + purges derived projections (via PRR-223 cascade).
- User opt-in `retain_exam_history: true` on profile → extends to `ArchivedAt + 60 months` (max).
- Opt-in surface: settings page "Keep exam history for 5 years instead of 2" toggle; disclosure copy states the default + extension.
- Default is the 24-month short window.

## Files

- `src/actors/Cena.Actors/Retention/ExamTargetRetentionPolicy.cs` (new)
- `src/actors/Cena.Actors/Retention/ExamTargetRetentionWorker.cs` (from PRR-223 — ensure this policy wires in)
- `src/student/full-version/src/pages/settings/privacy.vue` — add retention toggle.
- Privacy policy doc update (`docs/legal/privacy-policy.md` or equivalent) — disclose the default + opt-in.
- Tests: worker shreds at 24m, honors opt-in extension, respects re-toggle, audit entry written.

## Definition of Done

- Worker verified in staging against fixture archived-target rows with adjusted timestamps.
- Opt-in UI + copy reviewed by persona-privacy.
- Privacy policy updated, legal sign-off recorded.
- Audit log complete per-shred.

## Non-negotiable references

- PPL Amendment 13 (Israel).
- GDPR Article 5(1)(e) purpose-limitation.
- ADR-0003 (misconception-session-scope — this is a separate category, bounded at 24m).
- PRR-003a (RTBF ADR).
- Memory "Honest not complimentary".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + privacy-policy diff>"`

## Related

- PRR-217, PRR-218, PRR-223, PRR-015.
