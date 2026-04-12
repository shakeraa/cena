---
id: FIND-DATA-020
task_id: t_3fffc5d5baad
severity: P0 — Critical
lens: data
tags: [reverify, data, cost, rate-limit]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-020: rate-limit policies are global, not per-user (AI cost bomb)

## Summary

rate-limit policies are global, not per-user (AI cost bomb)

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

GOAL
Convert all five global rate-limit policies (`api`, `ai`, `tutor`, `destructive`
on Admin host; `api`, `ai`, `tutor` on Student host) from unpartitioned
`AddFixedWindowLimiter` to partitioned `AddPolicy` with a user-claim partition
key. Add a second outer tenant-level limiter for `ai` and `tutor` keyed by
SchoolId so one classroom cannot starve the rest of the platform.

ROOT CAUSE
`AddFixedWindowLimiter(name, opt => ...)` registers a SINGLE FixedWindowRateLimiter
for the whole policy name; without a partition function every request hits the
same counter. The only policy with a real partition key is `password-reset`
(FIND-ux-006b on Student host). Every other policy is a global bucket.

EVIDENCE
  $ rg "AddFixedWindowLimiter" src/api -n
    src/api/Cena.Admin.Api.Host/Program.cs:117 → "api"
    src/api/Cena.Admin.Api.Host/Program.cs:125 → "ai"
    src/api/Cena.Admin.Api.Host/Program.cs:133 → "destructive"
    src/api/Cena.Student.Api.Host/Program.cs:165 → "api"
    src/api/Cena.Student.Api.Host/Program.cs:173 → "ai"
    src/api/Cena.Student.Api.Host/Program.cs:181 → "tutor"

  $ rg "per user|per student|per-student" src/api/*/Program.cs -n
    All comments claim per-user / per-student enforcement. All are WRONG.

FILES TO TOUCH
  - src/api/Cena.Admin.Api.Host/Program.cs (lines 112-149)
  - src/api/Cena.Student.Api.Host/Program.cs (lines 160-219)
  - tests/Cena.Api.Host.Tests/RateLimiter/PerUserPartitioningTests.cs (NEW)

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-020
  - src/api/Cena.Student.Api.Host/Program.cs:188-208 (the `password-reset`
    policy already uses AddPolicy with a RateLimitPartition — copy this pattern)

DEFINITION OF DONE
  - Every AddFixedWindowLimiter in both Program.cs files is replaced with
    AddPolicy(...) partitioned by NameIdentifier claim.
  - "ai" and "tutor" policies have a SECOND chained outer limiter partitioned
    by SchoolId (resolved from JWT claim, not body/header).
  - The "100 req/min per user" / "10 req/min per user" / "10 messages/min per
    student" comments are now TRUE.
  - Integration test (tests/Cena.Api.Host.Tests/RateLimiter/
    PerUserPartitioningTests.cs) asserts per-user isolation:
      * Student A fires 12 req to /api/tutor/threads/.../stream
      * Request 11 returns 429 for student A
      * Student B's request 1 in the same window returns 200
  - Integration test for tenant-level outer limiter: 50 students from school A
    drain the school-A tutor bucket; one student from school B still gets 200.
  - dotnet test green.

REPORTING REQUIREMENTS
  node .agentdb/kimi-queue.js complete <id> --worker <you> \
    --result "branch=<branch>, files=<list>, tests-added=<paths>, build=ok,
    tests=ok, per-user-proof=<short curl paste>, tenant-isolation-proof=<paste>"

TAGS: reverify, data, cost, rate-limit
RELATED PRIOR FINDING: none (net-new v2 concern)
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_3fffc5d5baad`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
