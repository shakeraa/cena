# ADR-0039 — BKT parameter policy and worked-example fading hysteresis

- **Status**: Accepted
- **Date**: 2026-04-20
- **Decision Makers**: Shaker (project owner), persona-cogsci (cognitive-science lens), persona-educator (educator lens)
- **Task**: prr-041
- **Related**: [ADR-0003](0003-misconception-session-scope.md), [ADR-0002](0002-sympy-correctness-oracle.md), [ADR-0001](0001-multi-institute-enrollment.md)

---

## Context

Bayesian Knowledge Tracing already runs in production (`src/actors/Cena.Actors/Services/BktService.cs` and `src/actors/Cena.Actors/Mastery/BktTracer.cs`). The four canonical BKT parameters — prior mastery `PInit`, learning transition `PLearn`, slip `PSlip`, guess `PGuess` — are currently settable at multiple layers: hard-coded defaults, per-concept provider overrides (`IBktParameterProvider`), and a calibration pipeline that can in principle rewrite them from logged data. That flexibility is a liability: it means a silently-committed calibration change can shift every student's mastery curve overnight, with no ADR, no review, no sign-off.

Two additional weaknesses surfaced in the pre-release review:

- **Worked-example fading has no hysteresis.** Students oscillate between scaffolding levels on noisy signal, which both undermines the pedagogy (learners experience thrash, not progression) and produces confusing UI. The educator persona flagged this as a correctness issue, not a polish one.
- **Cohort difficulty target was 60%.** That's below the learner-engagement literature's consensus floor for a normal sample; the educator persona called it "demoralising" and produced citations showing 70–80% is the evidence-backed band for sustained engagement in K-12 mathematics.

## Decision

### BKT parameters are fixed at Koedinger literature defaults

We adopted the canonical Koedinger/Corbett defaults as **locked constants**:

| Parameter | Value | Role |
|---|---|---|
| `PInit` | 0.30 | Initial mastery probability at first exposure. |
| `PLearn` | 0.15 | Probability of transitioning unmastered → mastered per correct attempt. |
| `PSlip` | 0.10 | Probability of an incorrect response despite mastery. |
| `PGuess` | 0.15 | Probability of a correct response without mastery. |

These values are the defaults Koedinger and Corbett used in the original Cognitive Tutor evaluations and remain the cross-study reference point in the BKT literature. We are not picking them because they are optimal for Cena specifically — we are picking them because they are **predictable**, they let us reason about mastery transitions without consulting a trained model, and they give us a shared baseline against which any future per-domain or per-student calibration can be measured.

### Per-student parameter learning is forbidden

This is a deliberate extension of the policy locked in [ADR-0003](0003-misconception-session-scope.md) (misconception data stays session-scoped, never trains student-level models). Per-student BKT parameters would be a student-level model trained on the student's own trajectories — exactly the class of artefact ADR-0003 forbids, for the same COPPA / PPL / Edmodo-precedent reasons.

Any future proposal to tune BKT parameters — per-cohort, per-domain, per-track — requires:

1. A new ADR with explicit human sign-off,
2. A human-readable rationale for the parameter delta,
3. An architecture test update that pins the new constants in the same place.

An architecture test `BktParametersLockedTest` (forthcoming under `tests/architecture/`) enforces that the four constants declared in `Cena.Actors.Mastery.BktParameters` match the values in the table above. CI fails if a PR changes them without a companion ADR.

### Worked-example fading has hysteresis

Faded scaffolding moves up or down in discrete steps only under the following rules:

- **Promotion (fade to a less scaffolded level).** Three consecutive correct attempts at level *L* with the current parameters must be observed before the student fades to level *L-1*.
- **Demotion (fall back to more scaffolding).** Any incorrect attempt at level *L-1* restores the student to level *L* immediately.
- **Minimum dwell time.** A student must complete at least two attempts at any newly-reached level before either promotion or demotion logic is allowed to fire. This prevents a one-correct-one-wrong sequence from producing two scaffolding transitions on two attempts.

Hysteresis is *asymmetric* by design: promotion is slow, demotion is fast. That matches the cognitive-science persona's observation that it is far more damaging to under-scaffold a stuck student than to over-scaffold a fluent one.

### Cohort difficulty target

The default cohort difficulty target (the target correct-rate the scheduler aims for when picking items) moves from 60% to **75%** in the normal case. Two overrides apply:

- In the 30-day window preceding a Bagrut session, target rises to **85%** — the educator review established that close to high-stakes exams, learners need the confidence boost of high hit-rate practice more than they need novel difficulty.
- For students flagged with an **anxious** affective profile (see [ADR-0037](0037-affective-signal-in-pedagogy.md)), target rises by a further **+5pp** — they get 80% in the normal case and 90% in the pre-exam window.

These are scheduler inputs, not BKT parameters. They tune which item the scheduler picks next; they do not change what BKT infers about mastery from a given observation.

## Consequences

### Positive

- Mastery transitions become predictable: a reviewer can manually compute an expected posterior given a prior and an observation, which makes debugging student-report complaints tractable.
- Cold-start works sanely: a new student at `PInit = 0.30` needs roughly five correct attempts to cross the 0.85 mastery threshold under these parameters, which matches the educator persona's intuition about how long "getting it" should take.
- Per-student parameter drift is structurally prevented, not just discouraged.
- Hysteresis eliminates the observed worked-example thrash, which was the top educator complaint about the Phase-1 adaptive scheduler.
- 75% cohort target aligns with the engagement literature's evidence-backed band, and the +5pp and pre-exam overrides protect the two populations where the default is most likely to be wrong.

### Negative

- We give up the ability to react quickly to a domain or concept where the Koedinger defaults are empirically wrong for our content. That trade-off is deliberate: the wrong fix for "this concept is mis-calibrated" is "silently re-tune parameters from logged data". The right fix is to re-author the concept or its question bank, which does not require parameter changes.

### Neutral

- `IBktParameterProvider` retains its DI seam — it just returns the locked defaults for now. If the provider is ever used to return non-default values, the architecture test catches it.

## References

- Koedinger, K. R., & Corbett, A. T. (2006). *Cognitive Tutors: Technology bringing learning science to the classroom.* In R. K. Sawyer (Ed.), *The Cambridge Handbook of the Learning Sciences* (pp. 61–77). Cambridge University Press. — the baseline parameter conventions adopted above.
- Corbett, A. T., & Anderson, J. R. (1994). *Knowledge tracing: Modeling the acquisition of procedural knowledge.* User Modeling and User-Adapted Interaction, 4(4), 253–278. — canonical BKT model.
- [ADR-0003](0003-misconception-session-scope.md) — per-student model prohibition extended here to BKT parameters.
- [ADR-0037](0037-affective-signal-in-pedagogy.md) — the anxious-profile flag consumed by the cohort-difficulty override.
- `src/actors/Cena.Actors/Mastery/BktParameters.cs` — locked constants declared here.
- `docs/tasks/pre-release-review/TASK-PRR-041.md` — task body.
