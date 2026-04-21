# TASK-PRR-221: Onboarding UI — exam-targets + per-target-plan steps

**Priority**: P0 — UI blocker; persona-a11y verdict red
**Effort**: L (2-3 weeks, includes a11y prototype work)
**Lens consensus**: persona-a11y (red), persona-educator, persona-ethics, persona-enterprise
**Source docs**: brief §5, persona-a11y findings (VDatePicker RTL + SR announcement), persona-ethics findings (nag copy guardrails)
**Assignee hint**: kimi-coder + a11y review
**Tags**: source=multi-target-exam-plan-001, epic=epic-prr-f, priority=p0, ui, a11y-gated
**Status**: Blocked on PRR-220 (catalog) + PRR-218 (endpoints)
**Source**: 10-persona review
**Tier**: mvp
**Epic**: [EPIC-PRR-F](EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Goal

Insert two new onboarding steps — `exam-targets` (multi-select) and `per-target-plan` (loop over selected) — between `role` and `language`, retiring the orphaned `OnboardingCatalogPicker.vue` and splitting `ExamPlanStep.vue` into `ExamTargetsStep.vue` + `PerTargetPlanStep.vue`. Meet persona-a11y's WCAG scaffolding non-negotiables.

## Flow

```
welcome → role → exam-targets → per-target-plan → language → diagnostic → self-assessment → confirm
```

## `exam-targets` step

- Loads catalog via PRR-220.
- Cards grouped by `family` (BAGRUT / STANDARDIZED).
- Multi-select with `role="group"` + `role="checkbox"` + `aria-checked` per card (persona-a11y: NOT single-select `aria-pressed`).
- Server cap: 5 targets. Soft-warn UI at 4. Cap hit announced via `aria-live="polite"`.
- Search box for long catalogs, debounced.
- RTL mirrored; `<bdi dir="ltr">` around track/code tokens.
- `item_bank_status: reference-only` entries show a dimmed "reference content only" badge; selectable for plan purposes but scheduler won't run sessions (persona-educator).

## `per-target-plan` step

- Loop page per selected target. Must be announced correctly by SR (persona-a11y non-negotiable): "step 4 of 8 — target 2 of 3: Bagrut Physics 5U". Either flatten into top-level steps or use `aria-live` + `aria-labelledby` on a single `aria-current="step"` source of truth.
- Per-target controls:
  - Track picker (if `trackOptions.length > 1`): radio with `aria-label` per track.
  - **שאלון multi-pick sub-step for Bagrut targets only** (per [PRR-243](TASK-PRR-243-bagrut-question-paper-multi-pick.md)): after track is chosen, show catalog `question_papers[]` with defaults-all-checked; student can uncheck. Optional expand "Take some at different sittings?" surfaces per-שאלון sitting override. Non-Bagrut targets skip this sub-step.
  - Sitting picker: list of `{sittingCode}` from catalog, **not** a free date picker. Radio list sorted by `canonical_date` ascending; show canonical date next to sitting label wrapped `<bdi dir="ltr">`.
  - Weekly-hours slider 1..40: `role="slider"`, `aria-valuemin/max/now/text` set. `aria-valuetext` in locale numerals (Western vs Eastern Arabic; see PRR-232).
  - No free-text note field (persona-privacy + redteam + ethics + finops kill).
  - Optional ReasonTag radio: {Retake, NewSubject, ReviewOnly, Enrichment}.
- Live total-hours counter at bottom: `aria-live="polite"`, debounced 300ms, cap warning at sum > 40.
- "Not sure yet? Skip this target" link only if no classroom-assigned targets (persona-educator conditional skip).

## Settings reuse

- `ExamTargetsStep` + `PerTargetPlanStep` components are reused in `/settings/study-plan` (PRR-227).

## VDatePicker decision

- Per persona-a11y red: VDatePicker RTL is unverified; Hebrew calendar has no Vuetify locale. **Use catalog-sitting radio list instead of date picker**. If at any point we need a free-date input, prototype VDatePicker in all three locales with NVDA + VoiceOver + Hebrew/Arabic screen readers before shipping. Document decision in a note on PRR-221.

## Nag copy guardrails (persona-ethics)

- Skip-target path creates a placeholder plan. First home visit shows a dismissible banner: "Finish your exam plan — [Open]". No amber/red tint, no urgency copy, no recurrence after dismiss. Shipgate scanner (PRR-224) enforces identifier bans.

## Files

- `src/student/full-version/src/pages/onboarding.vue` — wire two new steps.
- `src/student/full-version/src/stores/onboardingStore.ts` — extend `WizardStep` union, state for `selectedTargets[]` + `perTargetPlans[]`.
- `src/student/full-version/src/components/onboarding/ExamTargetsStep.vue` (new, replaces orphan `OnboardingCatalogPicker.vue`).
- `src/student/full-version/src/components/onboarding/PerTargetPlanStep.vue` (new, derived from `ExamPlanStep.vue` with multi-target support).
- `src/student/full-version/src/components/onboarding/OnboardingStepper.vue` — nested step semantics.
- Retire/delete: `src/student/full-version/src/components/OnboardingCatalogPicker.vue`, `src/student/full-version/src/components/onboarding/SyllabusMap.vue` (persona-educator: decide retire-or-wire in PR review).
- E2E tests: happy path, cap-at-5, skip-path, RTL rendering he/ar, SR announcement.
- Vitest: store mutations + validation.

## Definition of Done

- All 8 steps navigable keyboard-only in en/he/ar.
- SR announces step context correctly (manual test with NVDA + VoiceOver; recording attached to PR).
- Numerals preference honored on slider values (requires PRR-232).
- Shipgate scanner v2 (PRR-224) passes on onboarding copy.
- Visual regression tests for RTL + LTR + numerals.
- Full `Cena.Actors.sln` builds cleanly (even if .NET-free change, frontend build gate passes).

## Non-negotiable references

- Memory "Math always LTR" (bdi wrapping).
- Memory "Primary color locked" (#7367F0 — contrast fixes via usage pattern only).
- Memory "No stubs — production grade".
- ADR-0048 (exam-prep framing).

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + a11y test recording URL>"`

## Related

- PRR-220 (catalog), PRR-218 (endpoints), PRR-224 (shipgate), PRR-232 (numerals), PRR-227 (settings reuse).
