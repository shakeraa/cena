# EPIC-E2E-C — Student learning core (diagnostic → session → mastery)

**Status**: Proposed
**Priority**: P0 (this IS the product)
**Related ADRs**: [ADR-0002](../../docs/adr/0002-sympy-correctness-oracle.md) (CAS oracle), [ADR-0003](../../docs/adr/0003-misconception-session-scope.md), [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md), [EPIC-PRR-F](../pre-release-review/EPIC-PRR-F-multi-target-onboarding-plan.md)

---

## Why this exists

The learning loop is a 5-boundary hop: SPA → student-api → NATS → actor-host → Marten → SignalR → SPA. Every answer is a round-trip through all 5. A regression in any hop silently breaks mastery updates without a visible error — BKT just stops progressing.

## Workflows

### E2E-C-01 — First-ever sign-in → onboarding diagnostic

**Journey**: fresh student after registration (EPIC-E2E-A-01) → `/onboarding` → exam-target picker (multi-target per ADR-0050) → short diagnostic quiz (MST-013) → backend runs MIRT estimator (MST-018) → plan generated → `/home` shows initial plan.

**Boundaries**: DOM (onboarding wizard steps, diagnostic UI, plan-rendered home), DB (StudentPlan row with exam targets, MIRT theta estimate per concept cluster), bus (`OnboardingCompletedV1`, `PlanGeneratedV1`).

**Regression caught**: blank home page after onboarding (plan generation crashed silently), wrong exam targets saved, MIRT theta out-of-bounds.

### E2E-C-02 — Practice session — happy path

**Journey**: `/home` → "Start practice" → `/session/{id}` → first question loaded (CAS-verified, stem-grounded) → student answers correctly → hint ladder NOT surfaced → mastery updates → next question → session ends after N → `/progress` shows uptick.

**Boundaries**: DOM (question renders with KaTeX LTR inside RTL, correct feedback), DB (Answer event sourced, BKT parameter updated), bus (`AnswerSubmittedV1`, `MasteryUpdatedV1`), SignalR (live progress push to parent dashboard if watching).

**Regression caught**: question not verified by CAS (ADR-0002 violation — shipgate fail), BKT not updating, session never terminates.

### E2E-C-03 — Practice session — wrong answer → hint ladder

**Journey**: same as C-02 but student answers wrong → hint tier 1 surfaces (stem-grounded per PRR-262) → still wrong → tier 2 → still wrong → tier 3 (solution walkthrough) → student marks "I understand" → next question.

**Boundaries**: DOM (hint cards increase specificity, not reveal-then-hide violation), DB (MisconceptionEventV1 sourced but SESSION-SCOPED per ADR-0003, not on profile), bus (misconception emitted with session_id only, NOT student_id).

**Regression caught**: misconception data leaking to student profile (ADR-0003 violation — ship blocker), hint ladder skipping tiers.

### E2E-C-04 — Session with photo upload (PRR-J)

**Journey**: `/session/{id}` → handwritten answer → camera upload → OCR cascade → CAS verifies extracted answer → feedback same as typed answer path.

**Boundaries**: DOM (camera modal, preview, feedback), storage (S3 upload, signed URL), OCR pipeline state transitions, bus (`PhotoUploadedV1`, `OcrCompletedV1`, `AnswerSubmittedV1`), CAS verification.

**Regression caught**: S3 upload credential leak (PRR-414 / PRR-412 deletion SLA), OCR silently falls back to garbage, CAS unverified photo-answer marked correct.

### E2E-C-05 — Session interrupt & resume (offline-tolerant)

**Journey**: mid-session → tab close / connection drop → reopen within 30 min → session resumes at correct question + previous answers preserved.

**Boundaries**: localStorage state, `/api/sessions/{id}/state` resume endpoint, DB SessionAggregate rehydration.

**Regression caught**: resumed session loses prior answers → student re-does questions; session state reset to Q1 → annoying; session locks (another device can't pick up).

### E2E-C-06 — Mastery trajectory visible on /progress

**Journey**: over N sessions → `/progress` → trajectory graph updated with BKT+HLR decay (MST-003, MST-008) → trajectory reflects actual performance (not flat-lined or jittery).

**Boundaries**: DOM (graph updates, not blank), DB (LearningSessionQueueProjection + mastery snapshot), no ship-gate banned terms (no streak, no "days in a row" framing).

**Regression caught**: mastery trajectory frozen, projection lag hides recent wins, banned engagement copy creeping in.

### E2E-C-07 — Socratic explain-it-back flow (prr-074 / F1)

**Journey**: student selects "Explain it back to me" on a solved problem → LLM prompts for student's own words → student types → CAS-verified match to expected concept → mastery +boost (PRR-074).

**Boundaries**: DOM (explain-it-back UI), LLM prompt audit (no PII per ADR-0047), CAS verification path reused, misconception surface.

**Regression caught**: PII leaking into the LLM prompt; explain-it-back feedback hallucinating; mastery boost not recorded.

## Out of scope

- Tutor-session-specific flows — covered by EPIC-E2E-D
- Parent-facing progress view — covered by EPIC-E2E-E
- Teacher heatmap — covered by EPIC-E2E-F

## Definition of Done

- [ ] All 7 workflow specs green
- [ ] Each runs < 90s (sessions are longer than billing flows)
- [ ] C-01, C-02, C-03 tagged `@learning @p0` (blocks merge)
- [ ] C-04 (photo) tagged `@learning @photo-pipeline` — depends on S3 dev stack
- [ ] Misconception-leak sentinel in C-03 verified (EventStore scan for `student_id` on misconception events → must be empty)
