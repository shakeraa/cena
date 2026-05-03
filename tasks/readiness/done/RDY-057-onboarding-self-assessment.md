# RDY-057: Onboarding Self-Assessment Step

- **Status**: Requested — not started
- **Requested by**: Shaker, 2026-04-18
- **Source**: Conversational request during dev-stack validation
- **Tier**: 3 (feature — not a ship-blocker)
- **Effort**: 2-4 days
- **Dependencies**:
  - RDY-023 Diagnostic Quiz (landed — this is a sibling / complementary step)
  - BKT calibration (RDY-024 landed, RDY-024b pending post-pilot) — self-assessment feeds the student's initial prior for affective state, not the cognitive theta
  - Onboarding stepper refactor (currently 5 steps; this adds a 6th or embeds in step 4/5)

## Problem

The current onboarding flow (welcome → role → language → diagnostic → confirm) captures
cognitive starting state (via the diagnostic quiz's IRT theta init) but **nothing about how
the student feels about the subject**. Two students with identical diagnostic scores can
have wildly different self-concept, anxiety, and prior-class experiences that materially
affect which pedagogical approach works for them on day one (cf. Nadia's review lens on
affective signals; Dr. Lior on progressive disclosure and session boundaries).

Per Shaker: *"in the onboarding, the user enters his feedback, on the last step, on how
he/she feels about certain subjects topics … what are his/her strengths and so on."*

## Scope (draft — to be refined in design)

### 1. New onboarding step: "Tell us about yourself"

Positioned **after** the diagnostic quiz, **before** the confirm step. Optional but
strongly recommended (not skippable with a default — user either answers or says
"skip for now"; skipping flags the profile so future sessions can prompt).

Captures:

- **Subject-level confidence** — 1-5 Likert per track subject (Algebra / Functions /
  Calculus / Geometry / Trigonometry / Probability / Stats / Vectors — and Physics
  equivalents for 5U physics track).
- **Self-identified strengths** — multi-select chips: "I'm good at visualizing", "I
  work through problems step by step", "I memorize formulas well", "I connect math
  to real examples", "I enjoy proofs", etc. (8-12 chips max to avoid choice overload.)
- **Self-identified friction points** — the mirror of strengths. "Word problems
  confuse me", "I freeze on test questions", "I don't know where to start", "I
  lose track in long derivations", etc.
- **Topic-level feelings** — for each of the 3-5 concept clusters the diagnostic
  touched, a one-emoji/one-word reaction: 😊 solid / 🤔 unsure / 😰 anxious /
  ❌ haven't seen it. Purely affective — the diagnostic already captured the cognitive
  signal.
- **Free-text** — "Anything else we should know?" (200-char cap, optional). Stored
  session-scoped per ADR-0003 misconception-scope rules unless the student opts in
  to persistent profiling.

### 2. Data model

- New document `OnboardingSelfAssessmentDocument` scoped to `StudentId`. Fields:
  `{ StudentId, CapturedAt, SubjectConfidence: Dict<concept, 1..5>, Strengths: string[],
  FrictionPoints: string[], TopicFeelings: Dict<concept, 'solid'|'unsure'|'anxious'|'new'>,
  FreeText?: string, OptInPersistent: bool }`.
- Default retention: 90 days unless `OptInPersistent=true` (matches ADR-0003 misconception
  rules for anything approaching psychological profiling).
- Explicitly excluded from ML training corpus (`[MlExcluded]` attribute per RDY-006).

### 3. Use by downstream systems

- **Session opener**: if `TopicFeelings[topic] = 'anxious'`, LearningSessionActor opens
  with a confidence-building scaffold (faded worked example at the lower difficulty band)
  rather than cold-starting the student on the hardest item.
- **Teacher dashboard**: roll-up view per classroom showing aggregate strength / friction
  patterns. Not per-student PII.
- **Parent view**: parent sees their child's self-reported strengths (positive framing);
  friction points stay between student + teacher (consent model per COPPA).
- **BKT prior**: subject-confidence Likert adjusts `P_Initial` for BKT+ slightly — not
  as a hard signal (students misjudge themselves) but as a tiebreaker between two
  similar-theta placements.

### 4. UX rules (carry RDY-002 / RDY-015 a11y + RTL requirements)

- All text fully localized (en/he/ar); no emoji as primary signal — always pair with
  text label because VoiceOver+RTL handling of emoji is inconsistent.
- Likert slider has keyboard-accessible +/- buttons and ARIA labels per step.
- Chip selection is single-gesture (tap/click) with clear selected state — not a
  modal or a multi-step expansion.
- RTL mirroring of order but NOT of emoji (RTL locales keep emoji left-to-right per
  Tamar's bidi rules).
- Time budget per Dr. Lior: the whole step should fit in 60-90 seconds. If it
  expands past that, bucket into two shorter sub-steps.

### 5. Out of scope for this task

- Automatic re-assessment (monthly check-in) — tracked separately if this pilots well.
- NLP analysis of free-text — start with a simple char-cap + language detection,
  no sentiment modelling on student-minor text (Ran's red line on COPPA).
- Exposing self-assessment to the AI tutor's prompt context — separate policy
  decision; requires ADR because it touches misconception-scope boundary.

## Acceptance Criteria

- [ ] New step renders after diagnostic and before confirm in `src/student/full-version/src/pages/onboarding.vue`.
- [ ] Skippable with "skip for now" that flags `SelfAssessmentSkipped=true` on the profile.
- [ ] Data lands in a new Marten document, retention default 90 days.
- [ ] i18n keys added for en / he / ar; all labels paired with text (no emoji-only).
- [ ] A11y sweep passes (RDY-015 + RDY-030 criteria).
- [ ] Session opener respects `TopicFeelings[current_topic] === 'anxious'` by
  choosing a faded worked example in preference to a direct problem.
- [ ] BKT prior adjustment documented (ADR update to RDY-024 if the adjustment factor
  is non-trivial).

## Open questions

- Affective state is a sensitive field. Does this need its own ADR before ship?
  Likely yes — the data isn't strictly "misconception" but shares the same
  psychological-profiling concern that ADR-0003 governs.
- How do self-reported strengths interact with the BKT mastery model? They're
  self-signal, not observed behaviour; should be weighted accordingly.
- Parent consent: does this step require explicit parental opt-in for under-13s
  per COPPA? (Probably — Ran should weigh in.)

## Links

- Student onboarding page: [src/student/full-version/src/pages/onboarding.vue](../../src/student/full-version/src/pages/onboarding.vue)
- Existing diagnostic: [src/student/full-version/src/components/onboarding/DiagnosticQuiz.vue](../../src/student/full-version/src/components/onboarding/DiagnosticQuiz.vue)
- Session opener (where anxious signal should route): `src/actors/Cena.Actors/Sessions/LearningSessionActor.cs`
- Related ADRs: ADR-0003 (misconception scope), RDY-023 (diagnostic), RDY-024 (BKT calibration)
