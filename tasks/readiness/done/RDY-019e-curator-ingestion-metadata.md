# RDY-019e: Curator Ingestion Metadata + Operator Review Loop

**Parent**: [RDY-019](RDY-019-bagrut-corpus-ingestion.md) (split 5 of 5)
**Related**: [RDY-019-ocr-spike](RDY-019-ocr-spike.md) (defines the hint-consumer contract the cascade needs)
**Priority**: High — today's admin ingestion is metadata-blind, so content routes to wrong glossaries / wrong CAS subject
**Complexity**: Mid engineer (backend contract + persistence + endpoint) + frontend (admin UI review modal)
**Effort**: 4–6 days
**Blocker status**: Benefits from the OCR spike's `OcrContextHints` definition, but can run in parallel

## Problem

Admin has three ingestion paths (B1 batch scrape, B2 interactive upload, B3 cloud-dir drop-zone) feeding the same [`IngestionPipelineService`](../../src/api/Cena.Admin.Api/IngestionPipelineService.cs). Today the contracts carry **zero curator-supplied metadata**:

- [`CloudDirIngestRequest`](../../src/api/Cena.Api.Contracts/Admin/Ingestion/IngestionDtos.cs) has `Provider`, `BucketOrPath`, `FileKeys`, `Prefix` — that's it.
- Interactive upload forwards the raw file — no subject / language / grade / source type fields.
- Cascade must infer everything from content, which is error-prone for mixed Hebrew/Arabic/English math+physics archives. A misclassified PDF contaminates the question bank.

The right shape is a **two-phase ingestion handshake**:

1. System auto-extracts what metadata it can from filename, path, embedded PDF metadata, and a light OCR preview → returns a draft.
2. Curator reviews the draft, edits / adds / removes fields, then confirms — only then does the file enter the full cascade.

## Scope

### 1. Curator metadata schema

New record `CuratorMetadata` on `PipelineItemDocument`:

- `Subject` (enum: math / physics / chemistry / biology / ...)
- `Track` (enum: math_5u / math_4u / math_3u / physics_5u / foundation / ...)
- `Language` (he / ar / en)
- `SecondaryLanguages` (list, e.g. a bilingual textbook)
- `SourceType` (enum: bagrut-reference / ministry / textbook / teacher-submitted / legacy-scan / other)
- `SourceName` (free text — e.g. "Bagrut 2024 summer, 806")
- `Year` (int?, optional)
- `TaxonomyNodes` (list of leaf IDs from [RDY-019a](RDY-019a-bagrut-taxonomy.md), once it lands)
- `Provenance` (free text — curator notes, copyright context, special instructions)
- `ExtractedBy` (auto / curator / override — tracks who wrote each field so the review UI can show auto-extracted values in grey vs curator-edited in black)
- `ReviewStatus` (draft / confirmed / rejected)
- `ReviewedBy` / `ReviewedAt` (auth context on confirm)

Persisted per pipeline item; becomes authoritative hints for:
- The OCR cascade (glossary selection, CAS subject routing — see OCR spike §7)
- The quality gate (language check, topic-completeness check)
- The CAS router (math vs physics vs chemistry dispatch)

### 2. Auto-extract phase

Before the cascade runs, a cheap extractor populates a `CuratorMetadata` draft:

- Parse filename / path (`s3://cena-content/math-5u-2024/bagrut-summer-806.pdf` → `Subject=math, Track=math_5u, Year=2024, SourceType=bagrut-reference`)
- Pull embedded PDF metadata (author, title, keywords, language tag)
- Run a 1-page OCR preview, classify language (Hebrew / Arabic / English / mixed) and detect math vs physics markers
- Leave unresolved fields null with `ExtractedBy=auto` on populated fields

Auto-extract is **cheap and advisory** — gets the curator 80% there so review is one-click for the common case.

### 3. Operator review UI

Admin dashboard panel: "Pipeline items awaiting metadata review".

For each item, show:
- Filename + thumbnail of first page
- Auto-extracted `CuratorMetadata` with per-field origin badge (auto / curator)
- Editable form: change any field, add missing fields, **remove** auto-extracted fields that were wrong
- `Confirm` button (transitions `ReviewStatus` → `confirmed`, kicks off the OCR cascade with these hints as authoritative)
- `Reject` button (transitions to `rejected`, item does not enter the cascade; audit event written)
- Batch actions: select N files, apply the same metadata to all (common pattern when a whole directory is one exam year)

### 4. API surface

New endpoints under `/api/v1/admin/ingestion/metadata`:

- `GET /pending` — list pipeline items with `ReviewStatus=draft`
- `GET /{itemId}` — full `CuratorMetadata` draft for one item
- `PATCH /{itemId}` — curator edits; partial updates allowed (field-level), audit which fields changed
- `DELETE /{itemId}/{field}` — explicit "remove this auto-extracted value"
- `POST /{itemId}/confirm` — mark confirmed, dispatch to OCR cascade
- `POST /{itemId}/reject` — mark rejected
- `POST /batch/confirm` — apply one `CuratorMetadata` template to N items

All endpoints require `ModeratorOrAbove` auth (matches the rest of `AdminDashboardEndpoints`).

### 5. Backwards compatibility

- Student-uploaded photos (Surface A) stay metadata-free — student isn't a curator.
- Existing pipeline items without `CuratorMetadata` replay with `ReviewStatus=confirmed` + `ExtractedBy=legacy` so the ingestion doesn't stall.
- Cascade must still work with hints absent (per OCR spike §7) — hints are advisory, never required.

## Files to Modify

- New: `src/api/Cena.Api.Contracts/Admin/Ingestion/CuratorMetadataDtos.cs`
- Edit: `src/api/Cena.Api.Contracts/Admin/Ingestion/IngestionDtos.cs` (add `Metadata?: CuratorMetadata` to `CloudDirIngestRequest`)
- Edit: `src/shared/Cena.Infrastructure/Documents/PipelineItemDocument.cs` (add `CuratorMetadata` field)
- New: `src/api/Cena.Admin.Api/Ingestion/CuratorMetadataExtractor.cs` (auto-extract phase)
- New: `src/api/Cena.Admin.Api/Ingestion/CuratorMetadataEndpoints.cs`
- New: `src/api/Cena.Admin.Api/Ingestion/ICuratorMetadataService.cs` + implementation
- Edit: `src/api/Cena.Admin.Api/IngestionPipelineService.cs` (hand off to cascade with metadata)
- Edit: `src/api/Cena.Admin.Api/IngestionPipelineCloudDir.cs` (accept optional `CuratorMetadata` on batch ingest)
- Frontend: admin dashboard "Ingestion Review" panel (Vuexy) — task-split if large
- New: `src/api/Cena.Admin.Api.Tests/Ingestion/CuratorMetadataEndpointsTests.cs`

## Acceptance Criteria

- [ ] `CuratorMetadata` record defined, persisted on `PipelineItemDocument`, index on `ReviewStatus`
- [ ] Auto-extractor populates at least `Subject`, `Language`, `SourceType` from filename + PDF metadata + 1-page OCR preview
- [ ] Admin UI panel lists draft items, allows field-level edit / add / remove, shows auto vs curator origin per field
- [ ] Confirm / reject endpoints are audit-logged (who, when, which fields were overridden)
- [ ] Batch-confirm works for a directory of files with shared metadata
- [ ] Cascade reads `CuratorMetadata` when `ReviewStatus=confirmed` and uses it as authoritative hints
- [ ] Items with `ReviewStatus=draft` do NOT enter the cascade — curator gate is enforced
- [ ] Legacy pipeline items backfill to `ReviewStatus=confirmed / ExtractedBy=legacy` — no stall
- [ ] Tests cover: auto-extract from filename path, PATCH partial update, DELETE field removal, batch confirm, auth policy

## Coordination notes

- This is a contract + persistence change — needs the OCR spike's [`OcrContextHints`](RDY-019-ocr-spike.md) shape agreed before implementation so the cascade and the metadata record speak the same language.
- Student-side Surface A is out of scope — no curator in that flow.
- CAS subject routing and glossary selection should switch on `CuratorMetadata.Subject` + `Language` once this lands (follow-up).
