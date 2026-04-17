# RDY-057: Admin Bagrut PDF Ingest Trigger (UI)

**Parent**: Phase 2.3 `5ba32b4` (RDY-OCR-WIREUP-C)
**Priority**: Normal — the service is DI-registered and tested; only the
UI entry point is missing
**Complexity**: Small frontend
**Effort**: 0.5–1 day
**Blocker status**: None.

## Problem

Phase 2.3 shipped `IBagrutPdfIngestionService` wired to
`IOcrCascadeService` with `CascadeSurface.AdminBatch` +
`SourceType.BagrutReference`. 10 unit tests green. But the admin UI
has no direct trigger — curators can only get Bagrut PDFs into the
system through the generic `/api/admin/ingestion/upload` endpoint,
which loses the Bagrut-specific handling (exam code, reference-only
provenance, per-page draft emission).

## Scope

### 1. Admin endpoint

New minimal-API endpoint:

```
POST /api/admin/ingestion/bagrut
Content-Type: multipart/form-data

Fields:
  file       — application/pdf
  examCode   — required string (e.g. "math-5u-2023-winter")
  uploadedBy — optional; defaults to the authenticated super-admin claim
```

Returns the `PdfIngestionResult` already defined in
`BagrutPdfIngestionService.cs`.

Auth: `SuperAdminOnly` — Bagrut reference ingest is a restricted
operation (content-origin provenance requires audit).

### 2. Admin UI

New file: `src/admin/full-version/src/views/apps/ingestion/BagrutUploadDialog.vue`.

- Modal triggered from a new "Upload Bagrut PDF" button on the
  ingestion pipeline page.
- Required field: exam code (free text, matches the `exam-code-regex`
  used elsewhere — `^[a-z0-9\-_]{3,64}$`).
- File picker, PDF-only.
- Shows `PdfIngestionResult.Warnings[]` as severity-graded chips
  (`encrypted_pdf:*` → red, `cas_failed:*` → amber, `fallback_used:*`
  → blue, `some_drafts_low_confidence` → amber).
- Lists the extracted `Drafts[]` with a link to each draft in the
  review queue.

### 3. Tests

- Endpoint integration test: `Cena.Admin.Api.Tests/Ingestion/
  BagrutIngestEndpointTests.cs` — exercises multipart upload + service
  invocation (service substituted with NSubstitute).
- Frontend component test (Vitest): modal renders, exam-code
  validation, dispatches `$api` POST, renders all warning severities.

## Files to Modify

- New: `src/api/Cena.Admin.Api/Ingestion/BagrutIngestEndpoints.cs`
- Edit: `src/api/Cena.Admin.Api/Registration/CenaAdminServiceRegistration.cs`
  (map the new endpoint)
- New: `src/api/Cena.Admin.Api.Tests/Ingestion/BagrutIngestEndpointTests.cs`
- New: `src/admin/full-version/src/views/apps/ingestion/BagrutUploadDialog.vue`
- Edit: `src/admin/full-version/src/pages/apps/ingestion/pipeline.vue`
  (add the "Upload Bagrut PDF" button)

## Acceptance Criteria

- [ ] `POST /api/admin/ingestion/bagrut` accepts PDF + examCode, returns
      `PdfIngestionResult` JSON
- [ ] Super-admin-only gate enforced (403 otherwise)
- [ ] Modal renders + posts; warnings + drafts list render
- [ ] Encrypted-PDF path produces the `encrypted_pdf` warning chip
- [ ] Endpoint + component tests green
- [ ] Full `Cena.Actors.sln` builds clean

## Coordination notes

- `BagrutPdfIngestionService` is Phase-2.3 stable — do NOT modify the
  service contract.
- The generic upload path stays untouched; this is an additive
  Bagrut-specific surface.
- Match the existing `UploadDialog.vue` Vuexy patterns
  (AppDrawerHeaderSection, VBtn primary, VAlert for warnings).
