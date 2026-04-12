---
id: FIND-PEDAGOGY-013
task_id: t_d36e5f09a241
severity: P0 — Critical
lens: pedagogy
tags: [reverify, pedagogy, i18n]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-pedagogy-013: Authored question Explanation is monolingual — student locale ignored on the wire

## Summary

Authored question Explanation is monolingual — student locale ignored on the wire

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

FIND-pedagogy-013: Per-locale Explanation + DistractorRationale on the answer endpoint
related_prior_finding: FIND-pedagogy-001 (extension)
classification: new

A learner practicing in Arabic on a question authored in Hebrew receives
the explanation in Hebrew. Cena's question bank supports multi-language
versions via LanguageVersionAdded_V1 events, but:
  1. The event carries Stem + Options, NOT Explanation
     (src/actors/Cena.Actors/Events/QuestionEvents.cs:262-270)
  2. QuestionDocument.Explanation is a single string with no language
     tag (src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:74)
  3. SessionEndpoints.BuildAnswerFeedback ships questionDoc.Explanation
     as-is, ignoring student locale
     (src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1114-1116)

Per August & Shanahan 2006 (ISBN 978-0805860788), comprehension feedback
in a language the learner cannot read has zero formative value. Combined
with FIND-pedagogy-001 closing the binary-feedback gap, this means the
formative pipeline now ships text — but text the wrong student can't
necessarily read. Cummins (2000, ISBN 978-1853594748) on bilingual CALP
transfer: feedback in the language of instruction is a precondition.

Files to read first:
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1062-1118
  - src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs:74-86
  - src/actors/Cena.Actors/Events/QuestionEvents.cs:260-271
  - src/actors/Cena.Actors/Questions/QuestionListProjection.cs:174-181
  - src/actors/Cena.Actors/Questions/QuestionState.cs

Definition of done:
  - QuestionDocument carries ExplanationByLocale: Dict<locale,string>
    with the legacy single-string Explanation backfilled into the EN slot.
  - LanguageVersionAdded_V1 carries Explanation + DistractorRationales
    per locale (or a new event LanguageExplanationAdded_V1).
  - BuildAnswerFeedback resolves Explanation by student locale with
    fallback chain: current → en → null. NEVER fall back to a language
    the learner did not request (e.g., never serve HE to an AR learner).
  - QuestionListProjection.Apply(LanguageVersionAdded_V1) persists
    explanation translations into the read model.
  - Backfill script promotes existing single-string explanations to EN.
  - Unit: BuildAnswerFeedback with studentLocale='ar' on a question with
    explanation only in 'he' returns null (no inappropriate fallback).
  - Unit: same with studentLocale='en' returns the EN string.
  - Integration: a learner whose JWT carries locale='ar' POSTs
    /api/sessions/{id}/answer to a HE-authored question and the response
    Explanation is in AR after backfill, or null before backfill.

Reporting:
  - Branch <worker>/<task-id>-find-pedagogy-013-explanation-locale
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_d36e5f09a241`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
