---
id: FIND-DATA-023
task_id: t_c5be29342c1d
severity: P0 — Critical
lens: data
tags: [reverify, data, fake-fix, perf, regression]
status: pending
assignee: unassigned
created: 2026-04-11
type: regression
type: fake-fix
---

# FIND-data-023: StudentLifetimeStatsProjection reintroduces wall-clock Apply + is dead code (fake-fix of FIND-data-009)

## Summary

StudentLifetimeStatsProjection reintroduces wall-clock Apply + is dead code (fake-fix of FIND-data-009)

## Severity

**P0 — Critical** — REGRESSION — FAKE-FIX

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

GOAL
Three-fold fix to FIND-data-009's StudentLifetimeStatsProjection:
(1) eliminate wall-clock reads inside Apply handlers,
(2) fix the broken streak math,
(3) actually migrate the 13 read-path call sites to use the projection
    instead of QueryAllRawEvents full-scans.

ROOT CAUSE
The FIND-data-009 fix added a new projection but never migrated the
consumer code and introduced the exact anti-pattern FIND-data-001 was
supposed to retire (reading DateTimeOffset.UtcNow inside an Apply handler).
The projection is registered as Inline (real cost on every event append)
but read by zero call sites. It is also functionally incorrect: streak
math reassigns LastSessionAt BEFORE computing daysSinceLast.

EVIDENCE
  $ rg "DateTimeOffset.UtcNow|DateTime.UtcNow" \
      src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs -n
    118:    stats.UpdatedAt = DateTimeOffset.UtcNow;
    (inside Apply(BadgeEarned_V1, stats) — same anti-pattern as FIND-data-001)

  $ sed -n '82,103p' StudentLifetimeStatsProjection.cs
    82: public void Apply(LearningSessionStarted_V1 e, StudentLifetimeStats stats)
    83: {
    84:     stats.TotalSessions++;
    85:     stats.LastSessionAt = e.StartedAt;     ← assigned BEFORE comparison
    86:
    87:     // Streak calculation (simplified: consecutive days)
    88:     if (stats.LastSessionAt.HasValue)
    89:     {
    90:         var daysSinceLast = (e.StartedAt - stats.LastSessionAt.Value).TotalDays;
    91:         // daysSinceLast is ALWAYS 0 because LastSessionAt was just set above
    92:         if (daysSinceLast <= 1)
    93:             stats.CurrentStreak++;     ← increments on every event, never resets

  $ rg "Query<StudentLifetimeStats>|LoadAsync<StudentLifetimeStats>" src/
    (no matches — projection is dead code)

  $ rg "QueryAllRawEvents" src/api -c
    GamificationEndpoints.cs: 5
    StudentInsightsService.cs: 13
    SessionEndpoints.cs: 2
    StudentAnalyticsEndpoints.cs: 2

IMPACT
- Wall-clock reads in Apply break event-sourcing replay determinism.
- Streak math is silently wrong.
- The projection costs are paid (inline) but the read path is still
  global event scans. Double cost, zero benefit.

FILES TO TOUCH
  - src/actors/Cena.Actors/Projections/StudentLifetimeStatsProjection.cs
  - src/api/Cena.Student.Api.Host/Endpoints/GamificationEndpoints.cs
    (5 QueryAllRawEvents sites)
  - src/api/Cena.Admin.Api/StudentInsightsService.cs (13 sites)
  - src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs:219, 348
  - src/api/Cena.Student.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs:54, 139
  - src/actors/Cena.Actors/Projections/StudentDailyStatsRollupProjection.cs
    (NEW, keyed by (StudentId, Date) for per-day breakdowns)

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-023
  - docs/reviews/agent-3-data-findings.md FIND-data-001 (wall-clock-in-Apply)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs:198
    (projection registration)

DEFINITION OF DONE
  - Every Apply handler in StudentLifetimeStatsProjection uses timestamps
    from the event payload only. No DateTimeOffset.UtcNow, no DateTime.UtcNow.
  - Streak math fixed: capture prevLastSessionAt BEFORE the assignment,
    then use the captured value in the comparison.
  - Unit test asserts a real consecutive-day chain: 3 events 24 hours
    apart → CurrentStreak == 3; 4th event 48 hours later → CurrentStreak == 1.
  - The 22 QueryAllRawEvents call sites across the four read-path files
    are migrated. Lifetime stats reads use LoadAsync<StudentLifetimeStats>.
    Per-day breakdowns (heatmap, degradation curve) use the new
    StudentDailyStatsRollupProjection.
  - QueryAllRawEvents count in src/api/ drops from 31 to ≤ 5 (only justified
    bootstrap / monitoring / outbox).
  - Determinism test: project forward, rebuild from scratch, assert the
    documents are byte-identical.
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with diff stats, ripgrep proof of QueryAllRawEvents
  reduction count, paste of the streak unit test output, paste of the
  determinism test output.

TAGS: reverify, data, fake-fix, perf
RELATED PRIOR FINDING: FIND-data-001 (wall-clock anti-pattern) AND
  FIND-data-009 (the fix for that finding introduced this one)
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_c5be29342c1d`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
