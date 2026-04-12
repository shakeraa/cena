---
id: FIND-DATA-024
task_id: t_731b808f3ad1
severity: P1 — High
lens: data
tags: [reverify, data, perf, lying-label]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-data-024: audit log ignores filter param + full mt_events seq-count per page + hardcoded IP

## Summary

audit log ignores filter param + full mt_events seq-count per page + hardcoded IP

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
Replace the broken audit log (which ignores its filter parameter and
sequentially counts every event in mt_events on every page click) with a
dedicated AuditEventDocument + SecurityAuditProjection. Wire
AuditLogFilterRequest into the query. Capture real client IPs at emit time.

ROOT CAUSE
SystemMonitoringService.GetAuditLogAsync was scaffolded with a request
parameter that was never threaded into the WHERE clause. IpAddress was
hardcoded to "server" as a placeholder that never got replaced. The query
conflates every Marten event as an audit entry, 99% of which are not
audit-relevant (focus_score_updated_v1, concept_attempted_v1, etc.).

EVIDENCE
  $ sed -n '285,313p' src/api/Cena.Admin.Api/SystemMonitoringService.cs
    285: public async Task<AuditLogResponse> GetAuditLogAsync(
    286:     AuditLogFilterRequest request, int page, int pageSize)
    287: {
    288:     await using var session = _store.QuerySession();
    289:
    290:     // Query real events from Marten event store as audit log entries
    291:     var query = session.Events.QueryAllRawEvents()
    292:         .OrderByDescending(e => e.Timestamp);
    293:
    294:     var totalCount = await query.CountAsync();  ← full seq-count on every page
    295:
    296:     var rawEvents = await query
    297:         .Skip((page - 1) * pageSize)
    298:         .Take(pageSize)
    299:         .ToListAsync();
    ...
    309:         IpAddress: "server"   ← hardcoded placeholder

  `request` is parameter #1. It is NEVER read. The filter UI in the admin
  audit log page has zero effect on the response.

IMPACT
1. FERPA / breach-forensics requirement for audit trails is unusable
   (every entry says IP "server", no filtering works).
2. Multi-second seq count on every page click for any store >1M events.
3. Audit log is 99% noise (every domain event shows up).
4. Lying label: filter UI is decorative.

FILES TO TOUCH
  - src/api/Cena.Admin.Api/SystemMonitoringService.cs:285-313
  - src/shared/Cena.Infrastructure/Documents/AuditEventDocument.cs (NEW)
  - src/actors/Cena.Actors/Audit/SecurityAuditProjection.cs (NEW)
  - src/actors/Cena.Actors/Configuration/MartenConfiguration.cs (register)
  - src/api/Cena.Admin.Api/SystemMonitoringEndpoints.cs (forward HttpContext
    for IP capture at emit time)

FILES TO READ FIRST
  - .agentdb/AGENT_CODER_INSTRUCTIONS.md
  - docs/reviews/agent-data-reverify-2026-04-11.md FIND-data-024
  - src/shared/Cena.Infrastructure/Auth/CenaClaimsTransformer.cs

DEFINITION OF DONE
  - AuditEventDocument defined with Timestamp, ActorUserId, ActorIpAddress,
    Action, TargetType, TargetEntityId, SchoolId, Details fields.
  - Indexes on Timestamp, ActorUserId, ActorIpAddress, TargetEntityId, SchoolId.
  - SecurityAuditProjection subscribes only to audit-relevant events
    (Login_V1, RoleChanged_V1, StudentDataAccessed_V1, GdprDsarRequested_V1,
    etc.).
  - GetAuditLogAsync queries the AuditEventDocument with all four
    AuditLogFilterRequest fields applied.
  - Real IP captured from HttpContext.Request.Headers["X-Forwarded-For"]
    with fallback to Connection.RemoteIpAddress at every audit-event emit site.
  - Integration test: seed 5 audit events for user A and 5 for user B,
    call GetAuditLogAsync with filter userId=A, assert response contains
    only the 5 user-A events.
  - Startup fail-fast: if AuditEventDocument is not registered, the
    admin host refuses to start.
  - dotnet test green.

REPORTING REQUIREMENTS
  complete --result with branch, files, test path, curl paste showing
  the filter parameter actually filters.

TAGS: reverify, data, perf, lying-label
LINKED REPORT: docs/reviews/agent-data-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-data-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_731b808f3ad1`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
