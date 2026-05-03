# TASK-PRR-223: RTBF cascade for ExamTarget + derived projections

**Priority**: P0 — persona-privacy launch blocker
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-privacy
**Source docs**: persona-privacy findings (RTBF derivation cascade), PRR-003a (ADR: event-sourced RTBF), PRR-015 (retention worker pattern)
**Assignee hint**: kimi-coder + privacy review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, privacy, rtbf
**Status**: Blocked on PRR-003a ADR + PRR-218 aggregate
**Source**: persona-privacy review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

When a student invokes right-to-be-forgotten, `ExamTarget*` events must be crypto-shredded (per PRR-003b) AND all downstream derivations (coverage matrix per-student state, mastery projection, scheduler state, sitting-calendar personalization, prompt cache keys) must be purged. Launch-blocking for RTBF completeness.

## Scope

### Direct events

- `ExamTargetAdded/Updated/Archived/OverrideApplied` events — crypto-shredded via existing infra (PRR-003b).
- Event store retains tombstones with shredded payloads.

### Derived projections (must cascade)

- **Coverage matrix per-student rows**: student-specific coverage percentages linked to their targets → purge.
- **Mastery projection `(studentId, skillId)`**: NOT purged just because target was shredded — mastery is per-student, not per-target. BUT if the whole student invokes RTBF, all mastery rows purge via existing student-RTBF path (separate task).
- **Scheduler state**: any cached `ActiveExamTargetId`, `ExamTargetOverride` history → purge.
- **Prompt cache**: cache keys that embed `(examCode, track, sittingCode)` for this student → invalidate.
- **Sitting-calendar personalization**: any "your next sitting is X" derived state → purge.

### Retention worker integration (PRR-015 pattern)

- New `ExamTargetRetentionWorker` extends the pattern. Sweeps archived-target events past 24mo window (PRR-229) → crypto-shreds payloads. Idempotent.

### Audit

- RTBF audit log records: event IDs shredded, projections purged, caches invalidated, timestamp, requester-id.

## Files

- `src/actors/Cena.Actors/Retention/ExamTargetRetentionWorker.cs` (new, follows PRR-015 pattern)
- `src/actors/Cena.Actors/Rtbf/ExamTargetRtbfCascade.cs` (new)
- Coverage-matrix projection purge hook.
- Scheduler state purge hook.
- Prompt-cache invalidation hook.
- Integration tests: invoke RTBF → verify event shredded + all derivations purged + audit entry written.
- Chaos test: cascade halfway fails; ensure retry + eventual consistency.

## Definition of Done

- All 5 derivation categories have purge hooks invoked on RTBF.
- Idempotent: cascade replay doesn't double-fail.
- Audit log complete per-cascade.
- persona-privacy sign-off that no identifying residue remains post-cascade (including LLM prompt logs).
- Coordinated with existing student-RTBF flow — no double-purge, no missed path.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR (pending via PRR-003a) — event-sourced RTBF.
- PRR-003b — crypto-shredding infra.
- PRR-015 — retention worker pattern.
- PRR-022 — ban PII in LLM prompts.
- ADR-0003 — misconception session-scope (separate path).
- Memory "Quality over speed" — no shortcuts in RTBF.

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + audit log sample>"`

## Related

- PRR-003a, PRR-003b, PRR-015, PRR-229 (retention policy).
