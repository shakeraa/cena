# TASK-PRR-220: Exam catalog service (Global + TenantOverlay + offline fallback + CDN runbook)

**Priority**: P0
**Effort**: M (1-2 weeks)
**Lens consensus**: persona-enterprise, persona-ministry, persona-sre, persona-finops
**Source docs**: persona-enterprise (GlobalCatalog + TenantOverlay shape), persona-ministry (numeric codes), persona-sre (CDN fallback), persona-finops (no CDN delta)
**Assignee hint**: kimi-coder
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, catalog
**Status**: Blocked on PRR-217 (ADR-0049 Ministry code schema)
**Source**: 10-persona review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Serve the canonical exam catalog as `{GlobalCatalog, TenantCatalogOverlay}` via versioned, i18n'd JSON with an offline fallback bundled into the student SPA so a CDN outage on Bagrut morning doesn't block onboarding.

## Shape

```json
{
  "catalog_version": "2026.04.21-01",
  "global": {
    "exams": [
      {
        "exam_code": "BAGRUT_MATH",
        "ministry_subject_code": "035",
        "question_papers": [
          {"code": "035581", "track": "5U", "units": 5},
          {"code": "035582", "track": "5U", "units": 5},
          {"code": "035583", "track": "5U", "units": 5},
          {"code": "035572", "track": "4U", "units": 4},
          {"code": "035371", "track": "3U", "units": 3}
        ],
        "tracks": ["3U", "4U", "5U"],
        "sittings": [
          {"code": "TASHPA_SUMMER_A", "academic_year": "תשפ\"ו", "season": "summer", "moed": "A", "canonical_date": "2026-06-15"},
          {"code": "TASHPA_SUMMER_B", "academic_year": "תשפ\"ו", "season": "summer", "moed": "B", "canonical_date": "2026-07-20"}
        ],
        "display": {
          "en": {"name": "Bagrut Math", "description": "..."},
          "he": {"name": "בגרות במתמטיקה", "description": "..."},
          "ar": {"name": "بجروت الرياضيات", "description": "..."}
        },
        "default_lead_days": 180,
        "item_bank_status": "full",
        "passback_eligible": true,
        "regulator": "ministry_of_education"
      }
    ],
    "family_order": ["BAGRUT", "STANDARDIZED"],
    "families": {
      "BAGRUT": ["BAGRUT_MATH", "BAGRUT_PHYSICS", ...],
      "STANDARDIZED": ["PET", "SAT"]
    }
  },
  "tenant_overlay": {
    "enabled_exam_codes": ["BAGRUT_MATH", "BAGRUT_PHYSICS", "PET"],
    "disabled_exam_codes": [],
    "custom_display": {}
  }
}
```

## Constraints

- **Ministry codes are primary** (persona-ministry): catalog entries carry `ministry_subject_code` + `question_papers[].code` as שאלון numerics. Display names are localized metadata.
- **Sittings are named tuples** (persona-educator + ministry): `{code, academic_year, season, moed, canonical_date}`. `canonical_date` is derivable, `code` is canonical.
- **PET regulator = NITE**, not Ministry of Education. `regulator` field distinguishes.
- **Overlay is empty but present at Launch** (persona-enterprise): shape is future-proof without retrofit.
- **`passback_eligible`** flag ties into PRR-037.
- **`item_bank_status`** ∈ `{full, reference-only, unavailable}`. Persona-educator's "catalog shows humanities honestly" call.
- **`regulator`** ∈ `{ministry_of_education, nite, collegeboard}`.

## Endpoint

`GET /api/catalog/exams?locale={en|he|ar}` → the above shape for the caller's tenant.

- Auth: optional (persona-redteam: if unauth, cache-key includes `tenant_id` header to avoid fingerprinting other tenants' enabled lists).
- Response headers: `Cache-Control: public, max-age=300, stale-while-revalidate=86400` (persona-sre).
- ETag per `catalog_version`.

## Offline fallback

- Student SPA bundles a `catalog-fallback.json` at build time (global catalog only, no overlay).
- If catalog API fails + no SW cache, SPA uses fallback with banner "using offline catalog — tenant overrides unavailable".
- SW caches last-successful response with 24h TTL.

## CDN runbook

- Primary: edge-cached via existing CDN.
- Secondary: SPA fallback bundle (per above).
- Break-glass: named on-call owner can force `Cache-Control: no-store` + disable tenant overlays globally via feature flag.
- Runbook doc under `docs/runbooks/exam-catalog-outage.md`.

## Files

- `src/api/Cena.Student.Api.Host/Endpoints/CatalogEndpoints.cs` (new)
- `src/api/Cena.Student.Api.Host/Catalog/CatalogService.cs` (new)
- `src/api/Cena.Student.Api.Host/Catalog/CatalogData/global-catalog.json` (seed data, versioned)
- `src/student/full-version/public/catalog-fallback.json` (build-time bake)
- `src/student/full-version/src/composables/useExamCatalog.ts` (new)
- `docs/runbooks/exam-catalog-outage.md` (new)
- Tests: happy path, CDN failure fallback, overlay merge, locale fallback.

## Definition of Done

- Endpoint live + cached.
- Fallback bundle wired + SW cache strategy tested with CDN dropped.
- Runbook reviewed by persona-sre.
- Ministry codes validated against Ministry-published canonical table by persona-ministry sign-off.
- Per-tenant overlay tested with 2+ tenants in staging.
- Full `Cena.Actors.sln` builds cleanly.

## Non-negotiable references

- ADR-0001 (tenancy) — overlay is per-enrollment-scope, not just tenant-header.
- Memory "Honest not complimentary" — `item_bank_status: reference-only` is honest, not a stub.
- Memory "No stubs — production grade".

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + runbook URL>"`

## Related

- PRR-218 (aggregate validation against catalog), PRR-221 (onboarding uses catalog), PRR-231 (capacity amendment uses sittings).
