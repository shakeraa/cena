---
id: FIND-ARCH-020
task_id: t_017eed8be44b
severity: P0 — Critical
lens: arch
tags: [reverify, arch, stub]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-020: IMAP/Cloud-dir test + Embedding reindex stubs return fake success

## Summary

IMAP/Cloud-dir test + Embedding reindex stubs return fake success

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

**Goal**: Replace three placeholder admin endpoints with real
network calls. No more `return Task.FromResult(true)` lies.

**Files to read first**:
  - src/api/Cena.Admin.Api/IngestionSettingsService.cs (TestEmailConnectionAsync, TestCloudDirAsync)
  - src/api/Cena.Admin.Api/EmbeddingAdminService.cs (RequestReindexAsync)
  - src/actors/Cena.Actors/Services/EmbeddingIngestionHandler.cs

**Files to touch**:
  - src/api/Cena.Admin.Api/IngestionSettingsService.cs
    (TestEmailConnectionAsync, TestCloudDirAsync)
  - src/api/Cena.Admin.Api/EmbeddingAdminService.cs
    (RequestReindexAsync — publish NATS command)
  - src/actors/Cena.Actors/Services/EmbeddingIngestionHandler.cs
    OR a new ReindexCoordinatorService to handle the reindex command
  - Cena.Admin.Api.csproj (add MailKit, AWSSDK.S3, Google.Cloud.Storage.V1,
    Azure.Storage.Blobs as needed)

**Definition of Done**:
  - [ ] `grep -n "Placeholder\|placeholder\|would use" src/api/Cena.Admin.Api/IngestionSettingsService.cs src/api/Cena.Admin.Api/EmbeddingAdminService.cs` returns zero
  - [ ] Each test method returns `false` with a specific reason on
        real-world failure modes (DNS, auth, permission denied)
  - [ ] Reindex publishes a real NATS command consumed by a real
        background worker that updates `ReindexJobDocument` rows
  - [ ] Admin UI shows real connection-test results

**Reporting requirements**:
  - Paste sample success and failure output for each provider.
  - Confirm the reindex job actually completes (not just enqueues).

**Reference**: FIND-arch-020 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_017eed8be44b`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
