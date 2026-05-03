# Cena Syllabi

Authored YAML manifests, one per `CurriculumTrackDocument`. Ingested into
`SyllabusDocument` + `ChapterDocument` via:

```bash
# From repo root, with the dev stack up
docker compose -f docker-compose.yml -f docker-compose.app.yml \
  run --rm db-migrator-like-image \
  dotnet /app/Cena.Tools.DbAdmin.dll syllabus-ingest \
    --manifest /src/config/syllabi/math-bagrut-5unit.yaml \
    --author "amjad@example.edu"

# Or local dev:
cd src/tools/Cena.Tools.DbAdmin
dotnet run -- syllabus-ingest \
  --manifest ../../../config/syllabi/math-bagrut-5unit.yaml \
  --author "amjad@example.edu"
```

## Authoring rules

1. **Chapter order is authoritative**. Don't reorder after students exist
   on the track without a migration plan — their advancement state uses
   chapter ids, not indexes, so this is safe but the UX may look odd.
2. **Ministry code** should match the Bagrut exam structure
   (e.g. `806.1` = exam 806 section 1). Leave blank if non-Ministry.
3. **Locales**: `en` is required. `he` + `ar` are required before
   student-visibility. The ingest tool warns on missing locales.
4. **Prereq chapters**: DAG, not tree. Cycles are rejected at ingest.
5. **Learning objectives**: must resolve to existing
   `LearningObjectiveDocument` rows. Missing LOs are rejected at ingest.

## Manifest schema

```yaml
track: <string>           # TrackId (FK to CurriculumTrackDocument.TrackId)
version: <semver>         # bump on each revision
bagrutTrack: None|ThreeUnit|FourUnit|FiveUnit
ministryCodes: [<string>] # e.g. ["806", "807"]
chapters:
  - slug: <kebab-case>
    order: <int>          # 1-indexed; strictly monotonic per syllabus
    title:
      en: <string>
      he: <string>
      ar: <string>
    learningObjectiveIds: [<loId>, ...]
    prerequisiteChapterSlugs: [<slug>, ...]
    expectedWeeks: <int>
    ministryCode: <string>  # optional, e.g. "806.2"
```

## Current manifests

- `math-bagrut-5unit.yaml` — Math 5-unit (Bagrut 806 + 807). **DRAFT
  — awaiting Amjad review.** The chapter structure here is a
  reasonable first pass based on standard Bagrut 806/807 ordering;
  curriculum-expert review required before student-visibility.
