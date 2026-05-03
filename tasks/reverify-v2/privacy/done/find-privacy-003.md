---
id: FIND-PRIVACY-003
task_id: t_2d8e5ce4037f
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, COPPA, GDPR, Israel-PPL, dsar]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-003: GDPR rights endpoints all gated AdminOnly — no student/parent self-service

## Summary

GDPR rights endpoints all gated AdminOnly — no student/parent self-service

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

framework: COPPA (§312.6), GDPR (Art 12-22), Israel-PPL §13
severity: P0 (critical)
lens: privacy
related_prior_finding: none

## Goal

Add student-facing and parent-facing self-service GDPR / COPPA / Israel-PPL
rights endpoints. Today every consent / export / erasure endpoint requires
AdminOnly authorization, so a student or parent cannot exercise their rights
without contacting a Cena administrator out of band.

## Background

`src/api/Cena.Admin.Api/GdprEndpoints.cs:28-30`:

```csharp
var group = app.MapGroup("/api/admin/gdpr")
    .RequireAuthorization(CenaAuthPolicies.AdminOnly);
```

Every consent / export / erasure endpoint requires AdminOnly. There is no
`/api/me/gdpr/*` route on the Student host. `grep -rn 'me/gdpr\|me/erasure\|me/export'`
returns zero matches.

This means:
- A student cannot withdraw consent (GDPR Art 7).
- A student cannot request a data export (GDPR Art 20).
- A parent cannot exercise their child's right to deletion (COPPA §312.6,
  GDPR Art 17).
- A data subject cannot file a DSAR (GDPR Art 12, Israel PPL §13).

## Files

- `src/api/Cena.Student.Api.Host/Endpoints/MeGdprEndpoints.cs` (NEW)
- `src/api/Cena.Student.Api.Host/Program.cs` (wire MeGdprEndpoints)
- `src/student/full-version/src/pages/settings/privacy.vue` (add three buttons)
- `src/student/full-version/src/pages/settings/data-rights.vue` (NEW — DSAR + export + delete)
- Optional: `src/parent/` new bounded context for parent-facing surface, or
  a tokenized magic-link path that lets a parent authenticate without a full
  account
- `src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs` (add /api/me/dsar)

## Definition of Done

1. `/api/me/gdpr/consents` (GET) returns the current consent state for the
   authenticated student.
2. `/api/me/gdpr/consents` (POST) records a consent for a specific
   ProcessingPurpose (depends on FIND-privacy-007).
3. `/api/me/gdpr/consents/{purpose}` (DELETE) revokes consent.
4. `/api/me/gdpr/export` (POST) triggers the full export pipeline
   (FIND-privacy-006) and returns a download URL.
5. `/api/me/gdpr/erasure` (POST) triggers RequestErasureAsync. After the
   30-day cooling period the worker (FIND-privacy-005) processes it.
6. `/api/me/gdpr/erasure/status` (GET) returns the status of any in-flight
   request.
7. `/api/me/dsar` (POST) accepts a DSAR with a free-text message and emails
   the DPO mailbox; returns a tracking ID and a 30-day SLA timestamp.
8. settings/privacy.vue surfaces three buttons: Download my data, Delete my
   account, See who has accessed my data.
9. The three buttons fire the corresponding endpoints, show a confirmation
   modal, and surface the result.
10. E2E tests for the three flows.
11. Authorization tests asserting student A cannot read student B's consent.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-003-self-service-rights`. Result must
include:

- the new endpoint surface
- the tokenized parent-link approach if used (otherwise note "deferred to
  parent app — handles parent rights via FIND-privacy-001 parent-email path")
- E2E test paths

## Out of scope

- The actual erasure pipeline correctness (handled by FIND-privacy-005)
- The export pipeline completeness (handled by FIND-privacy-006)
- DPO appointment (handled by FIND-privacy-014)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_2d8e5ce4037f`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
