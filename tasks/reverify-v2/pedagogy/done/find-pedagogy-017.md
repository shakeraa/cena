---
id: FIND-PEDAGOGY-017
task_id: t_b8d4df8b2911
severity: P1 — High
lens: pedagogy
tags: [reverify, pedagogy, i18n]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-pedagogy-017: AnswerFeedback.vue renders English server feedback string alongside translated heading

## Summary

AnswerFeedback.vue renders English server feedback string alongside translated heading

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

FIND-pedagogy-017: Stop rendering English server-shipped feedback string in AnswerFeedback
related_prior_finding: FIND-pedagogy-001
classification: partial-fix

src/student/full-version/src/components/session/AnswerFeedback.vue line
87 renders feedback.feedback as raw text:

  87:        {{ feedback.feedback }}

The server hard-codes that field to literal English in
src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1074:

  1074:        var label = isCorrect ? "Correct" : "Not quite";

A Hebrew learner sees the heading "נכון!" (line 72 of AnswerFeedback.vue,
correctly using i18n key session.runner.correct) followed by the body
"Correct" (the server-shipped English string). Bilingual mash-up. The
test at SessionAnswerEndpointTests.cs:145 asserts the literal English
string `Assert.Equal("Correct", response.Feedback);` — locking the bug
in.

User rule "labels match data" (feedback_labels_match_data 2026-03-26).
Hattie & Timperley (2007), DOI 10.3102/003465430298487 — feedback must
be delivered in a form the learner can decode without effort.

Live reproduction (verified 2026-04-11 against origin/main @ cc3f702):
  1. document.cookie = 'cena-student-language=he; path=/'
  2. Open the session runner
  3. Answer a question
  4. Heading: נכון!  Body: Correct  (mixed)

Files to read first:
  - src/student/full-version/src/components/session/AnswerFeedback.vue (lines 71-88)
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:1062-1118
  - src/actors/Cena.Actors.Tests/Session/SessionAnswerEndpointTests.cs:120-148
  - src/api/Cena.Api.Contracts/Sessions/SessionDtos.cs:175-183

Definition of done:
  - AnswerFeedback.vue line 87 removed; only the i18n-translated heading
    (lines 71-72) and the structured Explanation/DistractorRationale
    (lines 96-109) render in the feedback card.
  - BuildAnswerFeedback.label removed; SessionAnswerResponseDto.Feedback
    marked obsolete (keep field for one release for backwards-compat).
  - SessionAnswerEndpointTests.cs:145 assertion replaced: assert that
    Feedback field is null or empty, NOT "Correct".
  - Playwright test: navigate to session runner with HE locale
    (via the gate-aware path from FIND-pedagogy-010 fix), complete a
    question, and assert the feedback card text contains only Hebrew
    characters (no Latin "Correct" / "Not quite" / "Great work").
  - Vitest: AnswerFeedback.vue under i18n locale 'he' renders heading
    "נכון!" and body containing only Hebrew characters.

Reporting:
  - Branch <worker>/<task-id>-find-pedagogy-017-feedback-i18n
  - Push, then complete with summary + branch + test names


## Evidence & context

- Lens report: `docs/reviews/agent-pedagogy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_b8d4df8b2911`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
