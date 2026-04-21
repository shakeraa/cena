# ADR-0052 — Ministry Bagrut rubric DSL + version pinning + per-track sign-off

- **Status**: Accepted
- **Date proposed**: 2026-04-22
- **Deciders**: Shaker (project owner), claude-code (coordinator); persona-educator + persona-ministry lens consensus
- **Relates to**:
  - [ADR-0002 (SymPy CAS oracle)](0002-sympy-correctness-oracle.md) — correctness is CAS-gated; this ADR is about *grading*, not correctness
  - [ADR-0032 (CAS-gated question ingestion)](0032-cas-gated-question-ingestion.md) — write-side invariant; rubric is the grading-side counterpart
  - [ADR-0040 (accommodation scope + Bagrut parity)](0040-accommodation-scope-and-bagrut-parity.md) — accommodations modulate rubric scoring
  - [ADR-0043 (Bagrut reference-only enforcement)](0043-bagrut-reference-only-enforcement.md) — Ministry text is reference-only; rubrics are the legitimate Ministry-derived artifact we DO serve
  - [ADR-0050 (multi-target exam plan)](0050-multi-target-student-exam-plan.md) — catalog primary key is `exam_code`; rubrics key off the same primary key
- **Source**: [tasks/pre-release-review/TASK-PRR-033-ministry-bagrut-rubric-dsl-version-pinning-per-track-sign-of.md](../../tasks/pre-release-review/TASK-PRR-033-ministry-bagrut-rubric-dsl-version-pinning-per-track-sign-of.md)

---

## Context

Ministry Bagrut rubrics govern how Cena grades AI-authored recreation items served through the diagnostic, exam-simulation, and practice-session seams. Before this decision the rubric was implicit:

- Scoring weights were hard-coded in `AnomalyDetection.cs` and `LlmJudgeSidecar.cs`.
- There was no audit trail for "which version of the rubric produced this grade".
- A Ministry circular revision (e.g. an updated assessment framework published mid-year) required a code change + redeploy, with no sign-off gate separating "engineer changed a constant" from "a Ministry-authorized evaluator approved the change".
- Multi-track support (3U / 4U / 5U) was implicit in hard-coded branches, not data.

Persona-educator flagged the no-versioning gap as a P1 ship-blocker: an educator who asks "what rubric graded my student's mock exam on 2026-03-01?" cannot get an answer. Persona-ministry flagged it independently: a rubric without a sign-off trail is not presentable to a Ministry liaison during an incident review.

---

## Decision

A rubric is a first-class, versioned, data-source-of-truth artifact with three invariants.

### 1. DSL shape (v1)

Rubrics live under `contracts/rubric/*.yml`. One file per `(exam_code, rubric_version)`. File shape locked at v1:

```yaml
exam_code: BAGRUT_MATH_5U         # catalog primary key (ADR-0050)
rubric_version: "1.0.0"           # SemVer; breaking band changes bump major
ministry_circular_ref: "…"        # source citation (non-empty)
approved_by_user_id: "…"          # sign-off user (non-empty)
approved_at_utc: "…Z"             # sign-off timestamp (required)

grade_bands:                      # 0..100 partition, no gaps or overlaps
  - band: excellent
    min_score: 90
    max_score: 100
    descriptor: { en: "…", he: "…", ar: "…" }

scoring_criteria:                 # weights sum to 1.0 ± 0.001
  - criterion_id: method_selection
    weight: 0.3
    display: { en: "…", he: "…", ar: "…" }
    checkpoints:
      - id: identifies_problem_class
        points: 5
        description_en: "…"
```

The v1 DSL deliberately does NOT carry:

- Per-item weight overrides (every item in a track grades under the same weights; item-specific variance is a v2 feature if pedagogically justified).
- Free-form evaluator comments (evaluator notes are a separate aggregate; the rubric is the ruleset, not the commentary).
- Accommodation modifiers (ADR-0040 owns that surface; the rubric is invariant-under-accommodation).

### 2. Version pinning service

`src/actors/Cena.Actors/Assessment/Rubric/IRubricVersionPinning.cs`:

- `PinFor(examCode)` returns the currently-loaded pinned rubric.
- `PinById("EXAM@1.0.0")` is the addressable-artifact lookup used during event replay — a grade stamped with `RubricId=BAGRUT_MATH_5U@1.0.0` remains reconstructable after v1.1.0 ships (full historical-version resolution is §5 below; Launch ships the current-version bridge).
- `ReloadAsync()` replaces the in-memory snapshot atomically. Parse or validation failure leaves the previous snapshot live — fail-closed is the correct posture for a regulator-facing artifact.

Every graded attempt's event payload MUST stamp `RubricId` at the moment of grading. This is how a 2026-03-01 grade stays reproducible after a 2026-06-01 rubric revision.

### 3. Sign-off metadata is mandatory

The triple `(approved_by_user_id, approved_at_utc, ministry_circular_ref)` is REQUIRED on every rubric. The YAML loader rejects any rubric missing any of the three. This is the audit trail a regulator, a night-shift on-call, or an educator escalation can read at 03:00 on a Bagrut exam morning:

- **approved_by_user_id** — the human (or explicitly-marked service principal) who signed the change. Matches queue-worker identity conventions.
- **approved_at_utc** — when. ISO-8601 with `Z` suffix.
- **ministry_circular_ref** — the source citation. Either a Ministry circular identifier, an explicit source date (`"2026-03-24-MOE-…"`), or an "internal-review-YYYY-MM-DD" tag when the change is internal clarification rather than Ministry-driven.

A rubric cannot be merged to `main` without all three — the architecture test `RubricSignOffMetadataRequiredTest` is the CI gate.

### 4. Validation invariants

Enforced at load; violation → rubric refused → previous snapshot stays live:

- `grade_bands` partition `0..100` with no gap, no overlap, start at 0, end at 100.
- `scoring_criteria` weights sum to `1.0 ± 0.001`.
- Every `criterion` has at least one `checkpoint` with `points > 0`.
- `display.en` is mandatory on every localized surface; `he` and `ar` are optional (fall back to `en`).
- `exam_code` must match an entry in `contracts/exam-catalog/` — a rubric orphaned from the catalog is not loadable.

### 5. Historical replay (future-work scope)

Launch loads the currently-pinned rubric per exam code. `PinById` resolves an addressable id only against the current version. A future rubric revision will:

1. Ship the new `contracts/rubric/bagrut-math-5yu.yml` at `rubric_version: 1.1.0`.
2. Archive the superseded v1.0.0 file under `contracts/rubric/archive/`.
3. Extend `RubricYamlLoader` to load both the current + archived files into an immutable history list.
4. `PinById("BAGRUT_MATH_5U@1.0.0")` then resolves against the archive.

The archive-side change is out of scope for Launch because Cena has no grading history to replay yet. The `RubricId` stamp on every graded event is present from day one, so historical reconstruction is guaranteed once §5 ships.

---

## Consequences

- **Positive**: rubrics are data, not code. A Ministry circular revision becomes a contracts-only PR reviewed by the Ministry liaison, not an engineering change. The sign-off triple is the artifact a regulator or educator can point to. Every graded attempt carries a stable `RubricId` for audit replay.
- **Negative**: YAML-based rubrics introduce a parse-time failure surface. Mitigation: the loader fails closed (previous snapshot stays live) and all three architecture tests (sign-off metadata, catalog match, weights-sum) run in CI.
- **Operational**: a rubric revision that does not sum to weight=1.0 fails CI — not production. A rubric revision that breaks the partition invariant fails CI. The runbook for a mid-year Ministry circular is: land the new `contracts/rubric/*.yml`, CI validates, merge, live hosts hot-reload via admin-rebuild (the reload endpoint is future work; Launch requires a restart).

## Enforcement summary

| Invariant | Artefact | Catches |
|---|---|---|
| DSL shape stable | `RubricDslTypes.cs` record types | Compile-time schema drift |
| Sign-off mandatory | `RubricYamlLoader.MapRubric` | Unsigned rubric reaches runtime |
| Weights sum to 1.0 | `RubricYamlLoader.ValidateWeightSum` | Criterion-weight drift |
| Band partition 0..100 | `RubricYamlLoader.ValidateBandPartition` | Gap or overlap in grade bands |
| Cross-tenant, singleton | `IRubricVersionPinning` registration | Per-tenant rubric forks (Ministry rubric is global) |
| Arch test (sign-off) | `RubricSignOffMetadataRequiredTest` | CI fail on missing sign-off field |
| Arch test (catalog match) | `RubricExamCodeMatchesCatalogTest` | Orphaned rubric whose exam code is unknown |

---

## Revisiting

Revisit within 12 months, or sooner if any of the following occurs:

- A Ministry circular revision materially changes the weighting structure (new `criterion` type, banding model change).
- `PinById` historical-version resolution (§5) needs to ship — revisit the archive directory convention.
- Accommodations-modulated scoring (ADR-0040) evolves to the point where rubric rules and accommodation rules need to share a canonical representation.

Each revisit must either reaffirm this decision or supersede it explicitly via a new ADR.
