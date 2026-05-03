# TASK-E2E-BG-03: Backend gap — `GET /api/admin/ingestion/{stats,pipeline-status}` returns 500

**Status**: Proposed
**Priority**: P0 (blocks Bagrut ingestion E2E coverage TASK-E2E-G-01)
**Epic**: Backend gap fixes (EPIC-E2E-G)
**Tag**: `@backend-gap @admin-api @ingestion @p0`
**Owner**: admin-api maintainers
**Surfaced by**: EPIC-G admin smoke; spec captured both endpoints failing on `/apps/ingestion/pipeline` mount

## Evidence

```
GET /api/admin/ingestion/stats           → 500 Internal Server Error
GET /api/admin/ingestion/pipeline-status → 500 Internal Server Error
```

Stack trace from the smoke artifact:

```
Failed to fetch pipeline stats: FetchError: [GET] "/api/admin/ingestion/stats": 500
  at fetchStats (PipelineStats.vue:31)
Failed to fetch pipeline status: FetchError: [GET] "/api/admin/ingestion/pipeline-status": 500
```

`/apps/ingestion/pipeline` is the **admin's only window** into the Bagrut PDF ingestion pipeline (RDY-057). Curators need it to:

- Track which PDFs are currently being processed
- See OCR cascade results
- Approve / reject CAS-verified items before they enter the question bank
- Inspect the dead-letter queue when an OCR pass fails

Without this endpoint, the entire ingestion observability surface is dark.

## What to investigate

1. Reproduce:
   ```bash
   TOKEN=$(...same admin login pattern...)
   curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5052/api/admin/ingestion/stats
   curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5052/api/admin/ingestion/pipeline-status
   ```
2. `docker logs cena-admin-api` for the unhandled exception.
3. Possible causes:
   - `IngestionPipelineService` queries a Marten projection (`IngestionPipelineState`?) that isn't registered or has been event-renamed without an upcaster
   - The pipeline state is per-tenant and the dev seed has no rows; the handler doesn't tolerate empty-result aggregation (`Sum()` / `Average()` on an empty `IEnumerable`)
   - The S3 directory provider (`S3DirectoryProviderTests` exists in the suite) is being lazily called on each stats fetch and throws when no S3 creds are configured in dev

## Definition of done

- [ ] Root cause + PR linked
- [ ] Both endpoints return 200 with a valid empty-corpus shape (`{"totalDocuments":0,"inProgress":0,...}` etc.) when the dev stack has no ingestion data
- [ ] Service-level unit tests for the empty-corpus and "1+ in-progress doc" cases in `Cena.Admin.Api.Tests/Ingestion/`
- [ ] `EPIC-G-admin-pages-smoke.spec.ts` allowlist entry for `/apps/ingestion/pipeline` REMOVED
- [ ] Updated TASK-E2E-G-01 (bagrut-ingestion) to remove the prereq blocker — the spec can now drive the pipeline page and assert against real /stats responses
