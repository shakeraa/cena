# Onboarding — "Which exam are you preparing for?" step

**Date:** 2026-04-21
**Trigger:** User observation on `/onboarding` (Arabic locale) — self-assessment
presents 8 Bagrut-Math topics. Student never asked what exam they're preparing
for. `onboardingStore.WizardStep` = `welcome | role | language | diagnostic |
self-assessment | confirm`. No exam-target step. `OnboardingCatalogPicker.vue`
exists but is not mounted. `examTarget` / `trackCode` not in the store.
**Sibling doc:** `docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md`
**Format:** 4 iterations of 10-persona discussion, converging to concrete
deliverables.

---

## Iteration 1 — Opening positions

Each persona answers: **"Should we add an exam-target step, and where?"**

### Miriam — Veteran Math Educator
Yes, and it must come **before** self-assessment. Asking an 11th-grader how
they feel about "vectors" is meaningless if they're preparing for Bagrut
Biology or Tawjihi literary-stream. Confidence ratings without a grounded
topic list are noise. Place the step after `role` (so we know whether the
respondent is student vs parent vs teacher) and before `diagnostic`
(diagnostic questions also depend on the target).

### Rajiv — Enterprise Systems Architect
The data model must come first. If `StudentPlan = List<ExamTarget>`
(MULTI-TARGET-EXAM-PLAN-001), then onboarding captures one or more
`ExamTargetCode`s and attaches them to the plan. One step can add multiple
targets — we shouldn't force a student preparing for Math + Physics to run
onboarding twice. Step must emit a non-empty list; empty → block advance.

### Noa — Privacy Counsel-Engineer
Exam-target is **operational data**, not sensitive under GDPR Art 9. No
minimization concern with storing it. BUT: targets can signal religion
(Talmud Bagrut track ⇒ Jewish religious identity) or ethnicity (Arabic-sector
Bagrut variant ⇒ Arab student). If we ever expose target to a tenant other
than the one the student enrolled with, that's a data-subject concern.
Recommend: target is tenant-scoped, never exported to cross-tenant analytics
or 3rd-party sub-processors without explicit consent (ADR-0035 / prr-035).

### Dr. Kenji — Cognitive Scientist
Target selection is a cognitive-load moment. Picking between 12+ exam
varieties upfront, with no prior exposure, is overload for a first-time
visitor. Use a **progressive disclosure** flow: region (IL / regional / int'l)
→ stream (scientific / humanities / technical) → specific target(s).
Default to region from locale (he → IL, ar → IL or regional, en → int'l) but
NEVER lock — one-tap to change region.

### Layla — L10n/A11y Engineer
Target names must be rendered in the target's native language alongside the
student's chosen UI language. E.g., the Bagrut-Physics-5u tile shows
"Bagrut Physics 5u" + a subtitle in Hebrew "בגרות בפיזיקה 5 יח״ל". Students
often recognize the exam's official name before the English translation.
Screen-reader order: exam name → units/difficulty → official-language subtitle.

### Marcus — Offensive Security Red-Teamer
Target code is low-sensitivity, but the LIST of available targets is
tenant-scoped (an institute that only offers 4u math shouldn't leak that
the global catalog includes IB or SAT). Endpoint must filter by caller's
tenant claims. A red-team concern: target selection will drive downstream
content fetches; ensure authorization runs on every topic-list request, not
just at onboarding.

### Svetlana — FinOps / Product Economist
Every target we support costs content-team hours (topic taxonomy + diagnostic
pool + parametric-template authoring). Start with **one** target live
(Bagrut Math 5u — what we already have) and **four** targets queued for
content work (Bagrut Physics 5u, Bagrut Chemistry 5u, Tawjihi scientific,
IB Math HL AA). Don't ship the picker with 12 empty targets — empties erode
trust faster than a small catalog.

### Danit — Ethical-Persuasion / Game Designer
Target selection is high-stakes for the student — "which future am I
choosing?" framing can induce anxiety. Copy MUST avoid loss-aversion
("don't miss out on Bagrut prep!"), scarcity ("only 2 weeks left to
enroll!"), or identity lock-in ("you are a Bagrut student now"). Neutral:
"Pick what you want to practice. You can add more later." Ship-gate
scanner (prr-163 cohort-context-copy-lockdown) applies.

### Prof. Amir — Ministry of Ed Examiner
For Israeli Bagrut specifically: tracks are `matmatika-3yu / 4yu / 5yu`,
`fizika-3yu/4yu/5yu`, etc. The unit count (`3yu/4yu/5yu`) is not a difficulty
knob — it's the formal Ministry of Education bagrut register. A 5-unit
student taking the 3-unit track is unacceptable to the Ministry record.
The target code MUST encode unit count explicitly. No ambiguity, no "just
Math".

### Dana — Frontline SRE / Incident Commander
The onboarding exam-target selection is critical-path: a student can't
start a session without it. If the catalog endpoint is down, onboarding
is blocked. Mitigation: the catalog must be cached client-side on successful
first load, served from PWA service-worker on subsequent visits, and the
step must degrade gracefully (show the cached catalog with a banner "Catalog
may be outdated — pull to refresh when online").

---

## Iteration 2 — Challenges

Personas push back on each other's iteration-1 positions.

### Rajiv → Dr. Kenji
Progressive disclosure (region → stream → target) is nice in theory but
triples the step count. "Six steps" becomes "eight steps" and we already
saw user friction at six. Counter-proposal: one screen, flat list grouped
visually by region, with search/filter. Single selection OR multi-select
with a max-3 chip constraint. No tree.

### Dr. Kenji → Rajiv
Flat list of 12+ options on one screen violates Hick's Law for novices.
But you're right the step count matters. Compromise: flat list with
collapsible group headers (IL-Bagrut expanded by default when locale is
he/ar; International collapsed). One screen, one step, zero tree navigation.

### Svetlana → Prof. Amir
If target code must encode unit count (`matmatika-5yu`), and we need separate
content pipelines for 3yu / 4yu / 5yu math alone, that's **three** targets
just for Bagrut Math. Your "one live, four queued" budget (Miriam) doesn't
cover this — Bagrut Math alone eats the budget. Counter: single target
code `bagrut-math` with a `unitLevel` field on the student plan, shared
taxonomy, item-pool difficulty bands.

### Prof. Amir → Svetlana
You can share the topic taxonomy but you **cannot** share the item pool.
A 3-unit geometry question is formally a different item than a 5-unit
geometry question. The Ministry publishes separate past-exam archives per
unit. Our recreations (ADR-0043: reference-only) must track the right
pool. Shared taxonomy + separate pools = separate content pipelines. Your
FinOps math is right; the rollout schedule needs to triple.

### Marcus → Noa
Tenant-filtering the catalog endpoint is necessary but not sufficient.
The `ExamTargetCode` stored on the student plan is effectively a
quasi-identifier when combined with region + locale + grade. A 17-year-old
ar-locale student with target `bagrut-chemistry-5u` in a West-Bank-ish
school district narrows to a small cohort. Add to k-anonymity enforcement
(prr-026): any exported aggregate that filters by target must maintain k≥10
on `(target, locale, region, grade_band)`.

### Layla → Danit
I agree with the "neutral copy" rule but your "you can add more later"
undersells agency. The multi-target plan (MULTI-TARGET-EXAM-PLAN-001) says
`StudentPlan = List<ExamTarget>`. Students should be invited to add ALL
targets they're preparing for upfront, not told to "come back later". The
screen should frame it as "Your exams this year" with multi-select and a
"done" button, not a single-select with an edit-later hint.

### Dana → Rajiv
If the list is multi-select, the validation logic (`canAdvance`) has to
handle ≥1 target AND ≤N targets. What's N? Unbounded means a student
could pick 20 targets and the scheduler's weekly-time-budget math becomes
nonsense. Propose N=4 (matches MULTI-TARGET-EXAM-PLAN-001's realistic
concurrency). Anything over 4 gets a soft warning before hard-cap at 6.

### Miriam → Layla
"Your exams this year" is locale-sensitive. In Israel, Bagrut runs
summer/winter/spring — "this year" ambiguous. Propose "Exams I'm preparing
for" or "My exam goals" as target-date-agnostic phrasing. Exam dates are a
per-target field that the ExamPlanStep (prr-148 follow-up) handles AFTER
target selection.

### Prof. Amir → Miriam
"My exam goals" is soft. Ministry examiners expect hard commitments — the
grade-book ties directly to the unit-level declaration. I don't object to
"goals" framing for the student, but the backend must treat the stored
target as a ledger entry, not a preference. Name the field accordingly:
`examRegistration` on the plan, separate from `examGoalDisplay` on the UI.
Decouple the two.

---

## Iteration 3 — Synthesis

Where consensus forms and residual tensions are acknowledged.

### Consensus reached

1. **Step placement:** After `role`, before `language`. (New wizard length:
   7 steps. Layla: step counter in the localized `stepCounter` key already
   pluralizes correctly.)
2. **Step UX:** Single screen, flat list, groups collapsible, region-default
   from locale but overrideable. Multi-select. Max-4 soft-warn, hard-cap 6.
   Copy: "Exams I'm preparing for" as the step title; ship-gate scanner
   enforces neutral framing.
3. **Data model:** `List<ExamTarget>` on `StudentPlan` per
   MULTI-TARGET-EXAM-PLAN-001. Target code encodes unit level explicitly
   (`bagrut-math-3yu`, `bagrut-math-4yu`, `bagrut-math-5yu`). Backend
   stores as `examRegistration` (ledger-grade field); UI displays as
   "exam goals" (Prof. Amir's decoupling).
4. **Catalog:** Tenant-scoped endpoint. PWA service-worker caches on
   success. Degrades to last-known-good with banner on offline.
5. **Launch budget:** Start with 3 live targets (Bagrut Math 3yu/4yu/5yu —
   shared taxonomy, separate pools). Queue 4 more (Bagrut Physics 5u,
   Chemistry 5u, Tawjihi scientific math, IB Math HL AA). Don't ship
   empty tiles.
6. **Privacy:** Target is tenant-scoped. k-anonymity on aggregate exports
   extended to `(target, locale, region, grade_band)` with k≥10 (prr-026).
7. **Copy:** Banned phrasing enforced by existing rulepacks (prr-006
   countdown ban; prr-042 citation-integrity on any effect-size claim).

### Residual tensions (escalate to user)

1. **Multi-target max:** Kenji says 3 is the cognitive-load ceiling for
   first-time users; Rajiv's 4 is the data-model ceiling. **Proposed:**
   ship with 3, allow up to 4 via a "more exams" affordance behind a soft
   confirm ("Most students focus on 3 or fewer — are you sure?"). Escalate
   only if finops + SRE say the difference matters.
2. **Auto-default region:** Miriam wants locale→region auto-inference
   (he/ar → IL). Noa warns against silent defaults on identity-adjacent
   fields. **Proposed:** visually pre-highlight the inferred region but
   require explicit tap to confirm — no silent commit.
3. **"Don't know yet" path:** Danit argues we should let students skip
   with "I'm not sure yet" → we default to Bagrut Math 5u and flag for
   teacher review. Prof. Amir objects: no skip — registration is a hard
   commit. **Proposed:** compromise — `undecided` is an explicit target
   code, NOT a skip. It routes to a neutral general-math pool until the
   student commits. Admin sees an actionable "exam-not-selected" filter.

---

## Iteration 4 — Concrete deliverables

Named tasks, files, tests, and timelines.

### Engineering tasks

1. **`prr-onboarding-exam-target` (this task file).** Insert `exam-target`
   step into `onboardingStore.WizardStep` enum between `role` and
   `language`. Extend `STEP_ORDER`. Update `canAdvance('exam-target')`
   to require `examTargets.length >= 1`.
2. **`TASK-PRR-CATALOG-ENDPOINT`.** `GET /api/v1/catalog/exam-targets?
   locale={code}` → returns tenant-scoped target list grouped by region.
   `GET /api/v1/catalog/exam-targets/{code}/topics` → topic list for a
   target. Admin-authored YAMLs under `contracts/exam-catalog/`.
3. **`TASK-PRR-EXAM-TARGET-PICKER-UI`.** New component
   `ExamTargetStep.vue` with the iteration-3 UX (flat list, collapsible
   groups, multi-select, soft-warn at 3, hard-cap at 6). Self-contained
   step pattern (inner action row; outer Next suppressed per
   `stepOwnsAdvance`).
4. **`TASK-PRR-MULTI-EXAM-SELF-ASSESSMENT`** (already filed today).
   SelfAssessmentStep reads topics from the selected targets rather than
   hard-coding.
5. **`prr-148-followup`** (already filed today). ExamPlanStep reads
   deadline + weekly minutes per target.
6. **`TASK-PRR-CATALOG-K-ANONYMITY-EXTENSION`.** Extend prr-026 k=10
   enforcement to include `(target, locale, region, grade_band)` tuples
   on aggregate exports.

### Content-team deliverables (out of engineering scope, tracked here)

- Topic-taxonomy YAML for: `bagrut-math-3yu`, `bagrut-math-4yu`,
  `bagrut-math-5yu` (shared) + separate item pools per unit level.
- Topic YAMLs + diagnostic pools queued for: `bagrut-physics-5u`,
  `bagrut-chemistry-5u`, `tawjihi-scientific-math`, `ib-math-hl-aa`.
- Native translations (en+he+ar) for every target label + every topic label.

### Tests

- Onboarding Playwright: a student must complete the exam-target step
  before reaching self-assessment; empty selection blocks advance;
  multi-select picks 2 targets; soft-warn fires at 3; hard-cap at 6.
- Architecture ratchet `OnboardingExamTargetStepInStepOrderTest`: the
  `STEP_ORDER` array must include `'exam-target'` between `'role'` and
  `'language'`. Fails CI on drift.
- Catalog endpoint integration: (a) tenant-filtered list, (b) unknown
  target → 404, (c) unauthenticated → 401, (d) service-worker cache-hit
  on offline reload.

### Escalations for user (3 items, iteration-3 tensions)

- Max-target cap: 3 with soft-warn at 4, hard-cap 6 — **user approval
  requested**.
- Region auto-default: pre-highlight, require tap to confirm — **user
  approval requested**.
- "Undecided" target code for non-committal students — **user approval
  requested**.

### Timeline

- Week 0 (today): this doc + task files. User reviews the 3 escalations.
- Week 1: `TASK-PRR-CATALOG-ENDPOINT` + YAML for Bagrut Math 3yu/4yu/5yu.
- Week 2: `TASK-PRR-EXAM-TARGET-PICKER-UI` + wire into onboarding.
  `prr-148-followup` + `prr-multi-exam-self-assessment` unblocked.
- Week 3: k-anonymity extension + Playwright suite.
- Week 4: 2nd wave of content targets (Physics/Chemistry/Tawjihi/IB).

---

## Change log

- 2026-04-21 — document created from user trigger; 10 personas, 4
  iterations; 3 escalations awaiting user decision.
