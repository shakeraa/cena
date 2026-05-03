# RDY-023: Diagnostic Quiz in Onboarding

- **Priority**: Medium — calibrates starting mastery
- **Complexity**: Mid engineer (frontend + backend)
- **Source**: Expert panel audit — Nadia (Pedagogy), Lior (UX)
- **Tier**: 3
- **Effort**: 1 week

## Problem

Onboarding collects role and language but doesn't assess starting knowledge. All students begin with `P_Initial=0.10` regardless of prior knowledge. A 5-unit student who already mastered derivatives gets the same starting point as a student seeing calculus for the first time.

## Scope

### 1. Diagnostic quiz step in onboarding

After subject selection, present 5-10 quick questions per selected subject:
- Questions span easy/medium/hard (one per difficulty band per topic)
- No scaffolding, no hints (diagnostic mode)
- Timed: 2 minutes per question max
- Results set initial BKT mastery per concept

### 2. Ability initialization from diagnostic

- Use diagnostic responses to estimate initial IRT ability (theta) via maximum likelihood
- Map estimated theta → BKT `P_Initial` per concept (theta-to-mastery mapping table)
- Correct answer at difficulty X → higher theta estimate → higher P_Initial for that concept cluster
- Skip diagnostic for concepts not covered in quiz (use default P_Initial)

> **Cross-review (Nadia)**: The original spec said "initialize BKT mastery" directly from correct/incorrect, but the system uses IRT for item selection. Diagnostic should estimate IRT theta first, then derive BKT P_Initial from theta. This ensures consistency between the IRT ability model and BKT mastery model.

### 3. Skip option

- Diagnostic is optional ("Skip — I'll start from scratch")
- Skipping sets all concepts to default P_Initial

### 4. UX

- Progress bar showing question count
- Quick feedback (correct/incorrect icon, no explanation)
- "This helps us personalize your experience" messaging
- Max 3 minutes total to avoid onboarding fatigue

## Files to Modify

- `src/student/full-version/src/views/apps/onboarding.vue` — add diagnostic step
- New: `src/student/full-version/src/components/onboarding/DiagnosticQuiz.vue`
- `src/api/Cena.Student.Api.Host/Endpoints/SessionEndpoints.cs` — diagnostic session type
- `src/actors/Cena.Actors/Services/BktService.cs` — accept initial mastery from diagnostic

## Acceptance Criteria

- [ ] Diagnostic quiz appears in onboarding after subject selection
- [ ] 5-10 questions per subject across difficulty levels
- [ ] Results estimate IRT theta, then initialize BKT mastery per concept via theta-to-mastery mapping
- [ ] Skip option available (defaults to P_Initial=0.10)
- [ ] Completes in under 3 minutes
- [ ] No scaffolding/hints during diagnostic
- [ ] Diagnostic quiz is keyboard-navigable (skip, select, submit via keyboard)
- [ ] Progress bar has `aria-valuenow`, `aria-valuemin`, `aria-valuemax`
- [ ] Question timer is informational only (no auto-skip on timeout)

> **Cross-review (Tamar)**: Zero a11y criteria existed. Added keyboard navigation, ARIA progress bar, and timer behavior.
