---
id: FIND-PRIVACY-009
task_id: t_ee705ad16927
severity: P1 — High
lens: privacy
tags: [reverify, privacy, GDPR, ICO-Children, dpia]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-009: No DPIA exists for high-risk minor profiling + AI tutoring

## Summary

No DPIA exists for high-risk minor profiling + AI tutoring

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

framework: GDPR (Art 35 DPIA), ICO-Children (Std 1, 5, 12)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Author a Data Protection Impact Assessment (DPIA) for Cena and commit it
to the repo at `docs/compliance/dpia-2026-04.md`. Then add a CI gate that
asserts a DPIA exists for the current quarter.

## Background

Searching the entire repo for DPIA artifacts:

```
$ find . -iname '*dpia*' -o -iname '*data.protection.impact*'
(zero matches)
$ find docs -iname '*privacy*' -o -iname '*compliance*' -o -iname '*risk*' | head
(only review reports and a "social-learning-research.md" file)
```

WP29 + ICO joint guidance treats the systematic monitoring of children + AI
profiling + automated decisions affecting educational outcomes as
PRESUMPTIVELY high-risk under GDPR Art 35, requiring a DPIA before processing
begins. Cena does ALL of the above:
- Behavioral profiling via Bayesian Knowledge Tracing (mastery prediction)
- AI-driven question selection (Elo difficulty service)
- AI tutoring with a third-party processor (Anthropic, FIND-privacy-008)
- Cross-tenant analytics (FocusAnalyticsService)
- Adaptive learning that affects educational outcomes for the child

ICO Children's Code Standard 1 (best interests of the child) and Standard 12
(profiling) cannot be discharged without the DPIA exercise.

## Files

- `docs/compliance/dpia-2026-04.md` (NEW — annual DPIA)
- `docs/compliance/dpia-template.md` (NEW — based on ICO sample template)
- `scripts/ci-dpia-check.mjs` (NEW — fail CI if no DPIA for current quarter
  exists)
- `.github/workflows/ci.yml` (wire the check)

## Definition of Done

1. DPIA authored covering the 9 ICO-template sections:
   - The need for a DPIA (justification)
   - Description of the processing
   - Consultation
   - Necessity and proportionality
   - Risks (with explicit risk level: low/medium/high)
   - Mitigations
   - Sign-off
   - Cross-link to processor agreements (Anthropic, Firebase, Google Fonts)
   - Annual review date
2. Cover the 7+ processing purposes from FIND-privacy-007.
3. Mitigations identified for each high-risk profiling element (Elo,
   mastery prediction, adaptive question selection, AI tutoring, social
   visibility).
4. Cross-link from the public privacy policy (FIND-privacy-002, ICO Std 4).
5. CI gate: `node scripts/ci-dpia-check.mjs` fails if no `dpia-YYYY-QN.md`
   for the current quarter exists in `docs/compliance/`.
6. Document pinned to a Git tag for the year so historical DPIAs are
   preserved.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-009-dpia`. Result must include:

- the DPIA file path
- a summary of the highest-risk processing identified
- the CI script output

## Out of scope

- The actual mitigations (those are tracked under their own findings)
- The DPO's sign-off (placeholder until DPO appointed — FIND-privacy-014)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_ee705ad16927`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
