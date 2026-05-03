# TASK-PRR-261: BKT discounting for reference-anchored attempts (R12 split-out)

**Priority**: P1 — gates ADR-0059 pedagogical correctness
**Effort**: S (2-3 days; mastery-update path + tests)
**Source docs**: ADR-0059 §15.9 + §14.4 R12, persona-cogsci findings (Sweller 1998 worked-example transient), claude-code self-audit
**Assignee hint**: kimi-coder (mastery / BKT context)
**Tags**: source=claude-code-audit-2026-04-28,epic=epic-prr-n,priority=p1,backend,bkt,pedagogy
**Status**: Ready
**Tier**: launch-adjacent
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md)

---

## Goal

Persona-cogsci flagged ADR-0059's "single most load-bearing fix": variant attempts that follow a reference render are subject to the worked-example transient (Sweller 1998 DOI 10.1023/A:1022193728205). Without discounting, scheduler reads false-mastery and ADR-0050 spacing breaks. Apply 0.5× posterior weighting (or defer the BKT update) for ~5 minutes post-source-render.

## Scope

1. Track per-attempt context: `referenceAnchoredWithinSeconds` field on attempt event indicating time since source render.
2. BKT update path applies 0.5× posterior weight when `referenceAnchoredWithinSeconds <= 300`.
3. After 5 minutes, full posterior weighting resumes.
4. Per-attempt event records the discount factor for analytics + BKT calibration audit.
5. Tests: full-weight attempt vs reference-anchored attempt produce different posteriors; spacing benefit (ADR-0050 Item 4) preserved.

## Files

- `src/actors/Cena.Actors/Mastery/BktUpdateService.cs` (or wherever BKT lives)
- `src/actors/Cena.Actors/Events/QuestionAttemptedEvents.cs` (extend with field)
- `src/actors/Cena.Actors.Tests/Mastery/BktReferenceAnchorDiscountTests.cs` (new)

## Definition of Done

- Reference-anchored attempts apply 0.5× discount.
- BKT calibration audit records the discount factor.
- Spacing-benefit test green.
- Architecture test catches future code paths that read browse signal into BKT (per ADR-0059 §15.6 invariant).

## Blocking

- PRR-245 emits the timing signal.

## Reporting

`node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + BKT calibration test>"`
