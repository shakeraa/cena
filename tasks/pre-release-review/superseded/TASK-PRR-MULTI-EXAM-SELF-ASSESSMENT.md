---
id: prr-multi-exam-self-assessment
depends-on: [MULTI-TARGET-EXAM-PLAN-001]
priority: P1
tier: mvp
tags: [onboarding, pedagogy, i18n, il-bagrut, multi-exam]
---

# Multi-exam support for self-assessment + diagnostic steps

## Why this exists

User observed 2026-04-21 on `/onboarding` (Arabic locale): the
self-assessment step presents 8 hard-coded Bagrut Math topics
(الجبر, الدوال, التفاضل, الهندسة, حساب المثلثات, الاحتمال, الإحصاء,
المتجهات). There is no accommodation for students preparing for:

- **Arabic-sector Israeli students** on the same Bagrut Math track
  (same subjects, but Arabic-language variant of the same exam — this
  one we DO technically cover, just not labelled that way)
- **Bagrut subjects other than Math** (Physics, Chemistry, Biology,
  English, Hebrew literature, Arabic language, Civics, History)
- **West Bank / Gaza / Jordan Tawjihi** (different syllabus structure,
  scientific-stream vs literary-stream splits)
- **IB / SAT / Cambridge IGCSE** international-curriculum students
  (same school can host multiple programs)
- **Year-level prep** that isn't yet a Bagrut track (9th/10th grade
  preparatory work)

## Current code state

`src/student/full-version/src/components/onboarding/SelfAssessmentStep.vue`:

```ts
// line 51-62 (hard-coded)
// Canonical Bagrut-aligned subjects. Keys match the server-side concept
const SUBJECTS = [
  'algebra', 'functions', 'calculus', 'geometry',
  'trigonometry', 'probability', 'statistics', 'vectors',
] as const
```

Similarly `DiagnosticQuiz.vue` likely pulls from the same assumption.

## Goal

Self-assessment (and diagnostic) present subjects that match the
**ExamTarget** the student selected in the earlier onboarding step
(Role / Track / LanguagePicker → ExamTarget). A student who selected
"Bagrut — Physics 5 units" sees physics topics. A Tawjihi student sees
the scientific-stream topic set. An IB student sees IB SL/HL topic
set.

## Dependency on MULTI-TARGET-EXAM-PLAN-001

The design doc at `docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md`
(added 2026-04-21, awaiting 10-persona review) proposes
`StudentPlan = List<ExamTarget>` where `grade` is per-target rather
than per-student. That design is the prerequisite data model. This
task is blocked on its acceptance. Once accepted:

1. Server-side `IExamCatalogService` returns the topic list for a given
   `ExamTargetCode` (e.g., `bagrut-math-5u`, `bagrut-physics-5u`,
   `tawjihi-scientific`, `ib-math-hl-aa`, `ib-math-hl-ai`).
2. Topic codes are language-agnostic (`algebra`, `mechanics`,
   `thermodynamics`); i18n bundles carry the display labels per locale.
3. Onboarding emits the selected `ExamTargetCode` BEFORE self-assessment
   so that step can render the matching topic list.

## Scope (post-dependency)

### 1. Backend

- `GET /api/v1/catalog/exam-targets` — lists available targets,
  grouped by region (IL-Bagrut, IL-Tawjihi, International-IB, etc.).
- `GET /api/v1/catalog/exam-targets/{code}/topics` — topic list for
  one target. Each entry: `{ code, displayKeys: {en,he,ar},
  difficulty_band_hint }`.
- Catalog source: curated YAML in `contracts/exam-catalog/*.yml`,
  one file per target, version-pinned.
- Admin UI to edit/version these (separate task, admin-panel scope).

### 2. i18n

- `onboarding.selfAssessment.subjects.<code>` keys across en/he/ar for
  every topic we ship. Start with what's already covered (Bagrut Math)
  and grow as catalogs land.

### 3. Student SPA

- `SelfAssessmentStep.vue` reads `onboardingStore.examTarget?.topics` or
  calls the catalog endpoint on mount. Fallback: if no target selected
  yet (very early flows), default to the current hard-coded Bagrut Math
  list with a banner "Based on Bagrut Math — change via Target picker".
- `DiagnosticQuiz.vue` same treatment: question pool pulled from the
  target-specific pool rather than blanket.
- `ExamPlanStep.vue` (prr-148 follow-up) must also respect the target
  code for deadline date (Bagrut summer 2026-06-30 vs Tawjihi 2026-07-15
  vs IB May 2026 session, etc.).

### 4. Content-team deliverables (out of engineering scope, tracked here)

- Topic-list YAMLs for: Bagrut Physics 5u, Bagrut Chemistry 5u,
  Bagrut Biology 5u, Tawjihi scientific stream math, IB Math HL AA,
  IB Math HL AI.
- Native translations for every topic code in en + he + ar.

## Non-negotiables

- Files ≤500 LOC.
- ADR-0043 preserved — Ministry exam papers remain reference-only; the
  catalog stores topic taxonomy only, not verbatim Ministry text.
- No stubs — every catalog entry ships with full i18n triples (en+he+ar)
  or isn't shipped.
- Ship-gate scanner green on copy.
- Arabic numerals toggle (prr-032) applies to any numeric-grade entries.

## Senior-architect protocol

Ask *why* the original SelfAssessmentStep ever hard-coded the subjects.
Likely answer: the first product cut was Bagrut-Math-only. That made
sense as an MVP constraint. Now that the multi-target plan is on the
table, the hard-code is a constraint that must be removed carefully —
not by adding a second hard-code branch for Tawjihi, but by making the
subject list entirely data-driven.

## Reporting

```sh
git add -A
git commit -m "feat(multi-exam): track-driven self-assessment + diagnostic subject lists"
git push
```
