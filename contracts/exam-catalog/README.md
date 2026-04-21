Exam catalog source of truth (prr-220, ADR-0050).

Each `*.yml` file in this directory represents one catalog entry for an exam
target. The `ExamCatalogService` loads them at startup and serves them via
`GET /api/v1/catalog/exam-targets[?locale=…]`.

Editing rules
-------------

- `catalog_version` is monotonically increasing. Bump the top-level
  `CATALOG_VERSION` in `catalog-meta.yml` when any file here changes — the
  service refuses to serve a catalog whose version decreases.
- `exam_code` is the catalog primary key. Do NOT rename — the aggregate
  invariants key off it.
- `ministry_subject_code` + `ministry_question_paper_codes[]` are the
  authoritative Ministry identifiers (ADR-0050 §2). Display names are
  localized metadata.
- `availability` ∈ `{launch, roadmap, queued}` — the student SPA shows
  different chips for each.
- `regulator` ∈ `{ministry_of_education, nite, collegeboard, ib, other}`.
- `item_bank_status` ∈ `{full, reference-only, unavailable}` — honest tile.

Display locales
---------------

Each entry carries `display.<locale>.{name, short_description}` for
`en`, `he`, `ar`. Other locales fall back to `en`.

Admin rebuild
-------------

`POST /api/admin/catalog/rebuild` reloads this directory without a host
restart. SuperAdmin-only.
