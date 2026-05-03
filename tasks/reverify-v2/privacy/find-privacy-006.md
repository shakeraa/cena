---
id: FIND-PRIVACY-006
task_id: t_2ad633abfa65
severity: P1 — High
lens: privacy
tags: [reverify, privacy, GDPR, portability]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-006: GDPR data export incomplete — only profile snapshot, missing tutor history + sessions + events

## Summary

GDPR data export incomplete — only profile snapshot, missing tutor history + sessions + events

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

framework: GDPR (Art 20 data portability)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Make the GDPR Article 20 portability export complete. Today
StudentDataExporter only iterates the public properties of a single passed-in
object (StudentProfileSnapshot). Tutor history, learning sessions,
notifications, devices, preferences, share tokens, and the event stream are
all excluded — the most sensitive PII (free-text tutor conversations) is
NOT exported.

## Background

`src/shared/Cena.Infrastructure/Compliance/StudentDataExporter.cs:70-93` is
a generic reflection scanner over a single object. The caller in
`src/api/Cena.Admin.Api/GdprEndpoints.cs:67-80` passes only the
StudentProfileSnapshot, so the export is structurally incomplete.

Stores holding student PII that are NOT exported today:
- Event stream (raw events for the student)
- TutorThreadDocument
- TutorMessageDocument (the most sensitive — free-text from the child)
- StudentPreferencesDocument
- DeviceSessionDocument
- ShareTokenDocument
- LearningSession events / focus events
- Notifications
- StudentRecordAccessLog (the student's own §99.32 view)
- AdminUser (only relevant if the student is also an admin)

## Files

- `src/shared/Cena.Infrastructure/Compliance/StudentDataExportOrchestrator.cs`
  (NEW)
- `src/shared/Cena.Infrastructure/Compliance/StudentDataExporter.cs` (refactor
  to iterate the orchestrator's per-store collection)
- `src/api/Cena.Admin.Api/GdprEndpoints.cs` (call the orchestrator instead
  of the single-object exporter)
- `src/api/Cena.Student.Api.Host/Endpoints/MeGdprEndpoints.cs` (NEW from
  FIND-privacy-003) — student self-service export endpoint

## Definition of Done

1. StudentDataExportOrchestrator walks every Marten document store touching
   the student (profile, prefs, devices, tutor threads, tutor messages,
   share tokens, notifications, audit log) plus the event stream.
2. Returns a StudentDataExport with one section per store.
3. PII fields annotated with their PiiAttribute classification (depends on
   FIND-privacy-011 for completeness).
4. Output is a downloadable ZIP (not inline JSON) with:
   - profile.json
   - preferences.json
   - devices.json
   - tutor-threads.json
   - tutor-messages.json
   - learning-sessions.json
   - notifications.json
   - access-log.json (the FERPA §99.32 disclosure log for the student)
   - manifest.json (signed integrity hash)
   - README.md (child-friendly explanation of what's in the export)
5. Manifest signed so a parent can verify integrity.
6. Test that creates 3 tutor threads, 5 sessions, 2 devices, then runs the
   exporter and asserts the resulting bundle contains rows from all 6 store
   types plus the event stream.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-006-export-complete`. Result must include:

- the per-store query strategy
- the manifest signing approach
- a sample manifest from the test
- the bundle ZIP test fixture path

## Out of scope

- Cross-tenant data (tenant boundary holds)
- Backup tape data (out-of-band per privacy policy)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_2ad633abfa65`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
