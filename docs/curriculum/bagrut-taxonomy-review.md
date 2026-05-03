# Bagrut Mathematics Taxonomy — Curriculum Expert Review (RDY-019c)

**Taxonomy file**: [scripts/bagrut-taxonomy.json](../../scripts/bagrut-taxonomy.json)
**Version**: 1.0.0
**Tracks**: math_3u (14 subtopics) · math_4u (17 subtopics) · math_5u (42 subtopics)
**Concept mappings**: 42 (1:1 with CurriculumSeedData + prerequisite-graph.json)

## Review process

1. Curriculum expert opens `scripts/bagrut-taxonomy.json` + this document.
2. For each track, expert confirms:
   - Every topic is a recognised syllabus cluster (Ministry 2024 framework).
   - Every subtopic is a distinct assessable competency.
   - `bloom_range` reflects the depth of thinking the Ministry exam sets.
   - No Ministry syllabus topic is missing.
   - No non-Ministry topic has been added.
3. Expert runs the coverage report (`GET /api/v1/admin/content/coverage`)
   against a staging dataset to sanity-check gap distribution.
4. Expert signs off below.

## Sign-off record

| Track   | Expert | Institution | Date | Taxonomy SHA | Notes |
|---------|--------|-------------|------|--------------|-------|
| math_3u | _pending_ | _pending_ | _pending_ | _pending_ | |
| math_4u | _pending_ | _pending_ | _pending_ | _pending_ | |
| math_5u | _pending_ | _pending_ | _pending_ | _pending_ | |

Sign-off requires a PR that:
- Fills the row above with the expert's name, institution, date, and the
  git SHA of `scripts/bagrut-taxonomy.json` at review time.
- Includes a short notes column flagging any deviations from the
  Ministry syllabus or items explicitly deferred (with follow-up task id).
- Is reviewed+merged by the coordinator (claude-code or designated
  human coordinator).

## Known open items

- **math_5u.complex_numbers**: not currently in scripts/bagrut-taxonomy.json
  as a top-level topic (covered implicitly under algebra); flag for
  expert: should this be promoted to a first-class topic?
- **math_5u.three_d** (vectors / planes / solids): present in some 806
  exams, coverage in our taxonomy is under `vectors` only. Expert
  review: does this need a separate geometry leaf?

## Out of scope for this review

- Whether a specific existing question is correctly tagged with a leaf —
  that's caught by the migration `QuestionTaxonomyMapped_V1` events and
  the RDY-019c coverage report, not this syllabus-level review.
- Physics / chemistry / biology tracks — separate future work.

## Coordination

- Review must be recorded before RDY-019c can transition to `done`.
- Until all three tracks are signed off, the coverage endpoint still
  returns data but the gap list includes an `unverified_taxonomy: true`
  marker (implemented in a follow-up once an expert is actually on
  board — tracked with `unverified_taxonomy` hint in the report's
  `gaps` metadata).
