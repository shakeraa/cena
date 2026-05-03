---
id: FIND-DATA-028
task_id: t_503e56126008
severity: P1 — High
lens: data
tags: [reverify, data, stub, fake-data]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-028: GamificationEndpoints.GetBadges fabricates EarnedAt from Random (fake data on learner surface)

## Summary

GamificationEndpoints.GetBadges fabricates EarnedAt from Random (fake data on learner surface)

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

GOAL
Replace the fabricated badge EarnedAt timestamp (Random.Next(1, 60) days
ago) with real BadgeEarned_V1 event timestamps via a dedicated projection.
Eliminate fake data from a learner-facing endpoint (violates the "no stubs,
production grade" user rule).

ROOT CAUSE
The badges endpoint computes earnedness on-the-fly from session/XP/streak
counters instead of consuming BadgeEarned_V1 events. The author needed an
EarnedAt field for the DTO and fabricated one with
`DateTime.UtcNow.AddDays(-new Random(badgeDef.Id.GetHashCode()).Next(1, 60))`.
The Random seed is the badge ID hash, so the "earned date" is stable per
badge but completely unrelated to when the student actually earned it.

EVIDENCE
  $ sed -n '92,100p' src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
    92: if (isEarned)
    93: {
    94:     earned.Add(new Badge(
    95:         BadgeId: badgeDef.Id,
    96:         Name: badgeDef.Name,
    97:         Description: badgeDef.Description,
    98:         IconName: badgeDef.IconName,
    99:         Tier: badgeDef.Tier,
    100:         EarnedAt: DateTime.UtcNow.AddDays(-new Random(badgeDef.Id.GetHashCode()).Next(1, 60))));
    101: }

USER RULE VIOLATED
The user banned stub/canned/fake backend code 2026-04-11:
~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_no_stubs_production_grade.md
"all Phase 1 stubs must be hardened retroactively; no more Phase 1 stub →
Phase 1b real pattern"

FILES TO TOUCH
  - src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
  - src/actors/Cena.Actors/Projections/StudentBadgesProjection.cs (NEW)
  - src/actors/Cena.Actors/Sessions/LearningSessionEndedHandler.cs (or
    wherever badge threshold is evaluated — emit BadgeEarned_V1)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-028
  - ~/.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_no_stubs_production_grade.md
  - src/actors/Cena.Actors/Events/EngagementEvents.cs (BadgeEarned_V1 record)

DEFINITION OF DONE
  - Zero Random calls in GamificationEndpoints.
  - BadgeEarned_V1 is emitted at the point the threshold is first crossed
    (idempotent: reconfirming the same badge on a later session does not
    emit a duplicate event).
  - StudentBadgesProjection keyed by StudentId captures (BadgeId, AwardedAt)
    pairs. New events update the map; duplicate events are no-ops.
  - GetBadges reads from the projection and returns real timestamps.
  - Integration test: seed a BadgeEarned_V1 event with AwardedAt=2026-03-01.
    Hit /api/gamification/badges. Assert response shows EarnedAt=2026-03-01,
    NOT a Random-derived value within the last 60 days.
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with branch, files, test path, paste showing real
  timestamp in the response.

TAGS: reverify, data, stub, fake-data
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_503e56126008`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
