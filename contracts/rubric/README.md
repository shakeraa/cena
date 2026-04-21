# Ministry Bagrut Rubric DSL (prr-033)

Per-track scoring-rubric source of truth. Each `*.yml` file in this
directory represents a single Bagrut track's rubric at a pinned version.
`IRubricVersionPinning` loads them at startup, exposes each `(examCode,
rubricVersion)` pair as an addressable artifact, and refuses to serve
a rubric whose sign-off metadata is missing or malformed.

## Why a separate directory from `contracts/exam-catalog/`?

The exam catalog describes *what exists* (Ministry subject/paper codes,
display names, sittings, availability). The rubric describes *how we
grade a Cena-recreated item* produced under that exam code. The two move
at different cadences — the catalog changes roughly once a year when the
Ministry publishes revised structures; the rubric evolves faster as
internal review surfaces scoring-band ambiguities. Separating them keeps
`catalog_version` stable when a rubric rev is in-flight, and vice versa.

## File shape (v1)

```yaml
exam_code: BAGRUT_MATH_5U            # catalog primary key, must exist in contracts/exam-catalog/
rubric_version: "1.0.0"              # SemVer — bump major on breaking band changes
ministry_circular_ref: "2026-04-01"  # Ministry circular or source date that motivated this rev
approved_by_user_id: "human-…"       # ADR-0052 §3: non-empty, matches queue worker identity
approved_at_utc: "2026-04-19T14:22:00Z"

grade_bands:
  - band: excellent
    min_score: 90
    max_score: 100
    descriptor:
      en: "..."
      he: "..."
      ar: "..."

scoring_criteria:
  - criterion_id: method_selection
    weight: 0.3
    display:
      en: "Method selection"
      he: "בחירת שיטה"
      ar: "اختيار الطريقة"
    checkpoints:
      - id: identifies_problem_class
        points: 5
        description_en: "Identifies the problem class (linear / quadratic / …)."
```

## Editing rules (ADR-0052)

1. `exam_code` must match an entry in `contracts/exam-catalog/` — the
   `RubricVersionPinningService` refuses to load an orphaned rubric.
2. `rubric_version` is monotonically increasing per `exam_code`. A new
   file for the same exam_code at a lower version number is rejected.
3. `approved_by_user_id` and `approved_at_utc` are BOTH required. A
   rubric without sign-off metadata is rejected. The approval triple
   `(user, timestamp, ministry_circular_ref)` is the audit trail a
   regulator can read at 03:00 on a Bagrut exam morning.
4. `ministry_circular_ref` is free-form short text (date, circular
   number, or "internal-review-YYYY-MM-DD"). Required — a rubric that
   cannot cite its source is a rubric without provenance.
5. Weights on `scoring_criteria` MUST sum to 1.0 ± 0.001.
6. Grade-band ranges MUST partition 0..100 with no gaps or overlaps.

## Architecture tests

- `RubricSignOffMetadataRequiredTest` — every YAML in this dir has both
  approval fields non-empty.
- `RubricExamCodeMatchesCatalogTest` — every rubric's `exam_code` has a
  matching entry under `contracts/exam-catalog/`.
- `RubricWeightsSumToOneTest` — scoring criteria weights sum to 1.0.

See [ADR-0052](../../docs/adr/0055-ministry-rubric-version-pinning.md).
