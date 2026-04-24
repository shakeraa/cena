---
id: FIND-PRIVACY-005
task_id: t_601136dd9b19
severity: P0 — Critical
lens: privacy
tags: [reverify, privacy, GDPR, Israel-PPL, COPPA, ICO-Children, fake-fix, erasure]
status: pending
assignee: unassigned
created: 2026-04-11
type: fake-fix
---

# FIND-privacy-005: Right-to-erasure is a fake fix — only deletes consent log; ProcessErasureAsync has zero callers

## Summary

Right-to-erasure is a fake fix — only deletes consent log; ProcessErasureAsync has zero callers

## Severity

**P0 — Critical** — FAKE-FIX

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: GDPR (Art 17), Israel-PPL §15A, COPPA (§312.10), ICO-Children (Std 15)
severity: P0 (critical) — fake-fix
lens: privacy
related_prior_finding: none (no prior FIND-privacy-* — but this is a
              regression-of-trust against the existing
              `RightToErasureService` that ships and lies)

## Goal

Replace the current fake right-to-erasure implementation with a real
end-to-end pipeline that actually deletes (or anonymizes) every store
holding PII for the student, with a regression test that proves it works.

## Background

`src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs:74-115`
defines `ProcessErasureAsync` that ONLY deletes:
- ConsentRecord rows for the student
- StudentRecordAccessLog rows for the student

It does NOT delete:
- StudentProfileSnapshot (the actual education record)
- any event in the student's event stream
- LearningSession events (SessionStarted_V1, ConceptAttempted_V1, etc.)
- TutorMessageDocument (tutor chat history with free-text PII)
- TutorThreadDocument
- DeviceSessionDocument
- StudentPreferencesDocument
- any of the projections (FocusAnalytics, MasteryTracking, etc.)
- ShareTokenDocument

The log line at :114 says "Records anonymized." Nothing was anonymized.

Worse: `grep -rn 'ProcessErasureAsync' src/` returns ZERO callers. The method
is dead code. The endpoint at `GdprEndpoints.cs:84-98` only calls
`RequestErasureAsync`. Even the partial erasure never runs.

Worse still: deleting StudentRecordAccessLog on erasure is a FERPA
§99.32(a)(2) violation — the disclosure record must outlive the record.

## Files

- `src/shared/Cena.Infrastructure/Compliance/RightToErasureService.cs`
  (rewrite ProcessErasureAsync end-to-end)
- `src/shared/Cena.Infrastructure/Compliance/ErasureWorker.cs` (NEW
  IHostedService — wire ProcessErasureAsync into a daily cron)
- `src/actors/Cena.Actors/Events/LearnerEvents.cs` (add
  StudentErasureRequested_V1 + StudentErasureCompleted_V1)
- `src/actors/Cena.Actors/Events/StudentProfileSnapshot.cs` (add Apply for
  the new events to handle the anonymization on replay)
- `src/api/Cena.Admin.Api/GdprEndpoints.cs` (replace the lying log with the
  new event-emitting flow)
- New regression test:
  `src/shared/Cena.Infrastructure.Tests/Compliance/RightToErasureEndToEndTests.cs`

## Definition of Done

1. Erasure pipeline emits a `StudentErasureRequested_V1` event into the
   student's stream and the saga walks every store:
   - StudentProfileSnapshot: anonymize all Pii fields (hash with pepper +
     null DisplayName / Bio)
   - Event stream: replace PII fields in every event with their
     hash-with-pepper (don't delete the events — preserve audit/replay)
   - Tutor messages + threads: hard delete
   - DeviceSessionDocument: hard delete
   - StudentPreferencesDocument: hard delete
   - ShareTokenDocument: hard delete
   - Projections (FocusAnalytics, MasteryTracking): null PII columns,
     preserve aggregates if anonymized
   - StudentRecordAccessLog: PRESERVE for FERPA §99.32 (do NOT delete)
   - ConsentRecord: preserve consent provenance for the audit window, then
     let RetentionWorker remove
2. Emits `StudentErasureCompleted_V1` with a manifest of what was erased.
3. Wired into ErasureWorker IHostedService (or the existing RetentionWorker
   from FIND-privacy-004) running daily.
4. Cooling-period gate honoured: rows in `Status=CoolingPeriod` only
   processed after RequestedAt + 30d.
5. Replace the lying log line with a structured `ErasureCompletedManifest`
   listing every action taken.
6. End-to-end regression test:
   - create a student
   - attach profile + 5 sessions + 3 tutor threads + a device + a share token
   - call RequestErasureAsync
   - inject IClock advancing 31 days
   - run the worker tick
   - assert StudentProfileSnapshot.FullName is null OR hashed
   - assert TutorMessageDocument count for the student is 0
   - assert StudentRecordAccessLog count for the student is UNCHANGED
   - assert StudentErasureCompleted_V1 is in the stream

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-005-erasure-real`. Result must include:

- the per-store deletion strategy table
- the per-event-type PII anonymization rules
- the manifest schema
- the regression test fixture path
- a sample manifest from running the test

## Out of scope

- Cross-tenant cascade (assume tenant boundary holds)
- Backup tape erasure (out-of-band; document in the privacy policy)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_601136dd9b19`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
