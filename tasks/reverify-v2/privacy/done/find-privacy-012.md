---
id: FIND-PRIVACY-012
task_id: t_1484495513fe
severity: P1 — High
lens: privacy
tags: [reverify, privacy, FERPA, audit]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-012: FERPA audit middleware only covers 6 hardcoded paths — most admin reads not audited

## Summary

FERPA audit middleware only covers 6 hardcoded paths — most admin reads not audited

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

framework: FERPA (34 CFR §99.32 record of disclosures)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Replace the hardcoded 6-path allowlist in `StudentDataAuditMiddleware` with
a deny-list-based approach that audits EVERY admin/student/me path returning
student-identifiable data, except an explicit list of safe non-identifying
endpoints.

## Background

`src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs:21-29`:

```csharp
private static readonly HashSet<string> AuditedPaths = new(StringComparer.OrdinalIgnoreCase)
{
    "/api/admin/mastery",
    "/api/admin/focus",
    "/api/admin/tutoring",
    "/api/admin/outreach",
    "/api/admin/cultural",
    "/api/v1/mastery"
};
```

That's only 6 path prefixes. The middleware short-circuits on every other
path with `await _next(context); return;` BEFORE logging.

Endpoints that read student data outside this list — and are therefore NOT
audited — include:
- /api/admin/students/* (all student CRUD)
- /api/admin/leaderboard/* (peer comparison reads)
- /api/admin/sessions/* (session reads)
- /api/admin/analytics/* (focus + behavioral analytics)
- /api/admin/gdpr/* (the consent and erasure endpoints — extra ironic that
  the GDPR endpoints themselves are not §99.32-audited)
- /api/admin/compliance/* (the compliance endpoint surface itself)
- /api/me/* (student self-reads — arguably also need a §99.32 entry per
  the §99.7 right of inspection)

FERPA §99.32 requires the disclosure record for ALL non-routine disclosures,
not for an arbitrary 6-path subset.

## Files

- `src/shared/Cena.Infrastructure/Compliance/StudentDataAuditMiddleware.cs`
  (rewrite the path matching logic)
- `src/shared/Cena.Infrastructure/Compliance/FerpaAuditedAttribute.cs`
  (NEW — endpoint-handler tag)
- `src/shared/Cena.Infrastructure/Compliance/FerpaPublicAttribute.cs`
  (NEW — explicit opt-out for non-student endpoints, e.g. /api/admin/health)
- All admin endpoints — bulk-tag with [FerpaAudited]
- Public infra endpoints — tag with [FerpaPublic]
- `tests/Cena.Infrastructure.Tests/Compliance/FerpaCoverageTests.cs` (NEW)

## Definition of Done

1. Middleware no longer uses AuditedPaths allowlist.
2. New rule:
   - Any /api/admin/*, /api/v1/*, /api/me/* path is audited UNLESS it has
     [FerpaPublic].
   - Public infrastructure endpoints (/api/admin/health, /api/admin/version,
     swagger, openapi) are tagged [FerpaPublic].
3. Audited entry includes the endpoint name (handler delegate name) in
   addition to the URL path.
4. Coverage test walks the registered endpoint table and asserts every
   endpoint has either [FerpaAudited] or [FerpaPublic]. Build fails if a
   new endpoint is added without an explicit tag.
5. Integration test: call GET /api/admin/leaderboard, then query
   StudentRecordAccessLog for that path and find the row.
6. Integration test: call GET /api/admin/health, then query
   StudentRecordAccessLog and find NO row.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-012-ferpa-coverage`. Result must include:

- the count of newly-audited endpoints
- the test output showing coverage = 100%
- a list of every [FerpaPublic] handler with justification

## Out of scope

- The retention of the audit log itself (FIND-privacy-013)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_1484495513fe`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
