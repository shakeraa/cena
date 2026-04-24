---
persona: cogsci
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: yellow
---

## Summary

Q2 hide-then-reveal is the single highest-ROI pedagogy lever in this brief and
it is cheap to ship — but the effect-size literature is smaller than the brief
implies, and Option C (pedagogy-driven hide) is a latent dark-pattern. Q1
photo-diagnosis is defensible only under the **narrow** framing; broad framing
converts productive struggle into externalized error-correction and should be
rejected on cogsci grounds independent of cost. Q3: MathInput does NOT carry
chemistry representation adequately — Johnstone's triangle and Taber's
conceptual-change literature require chem-native input, not a shared
abstraction. Ship Q2-B + Q1-narrow + Q3-chem-native; the brief's "MC-only at
Launch is degraded pedagogy" line is correct and the loss is large, not small.

## Section 7.2 answers

**Generation effect (Q2) — effect sizes and robustness.** Slamecka & Graf
(1978, JEP:HL&M) is the original; pooled effect across the classic generation
paradigm is **d≈0.40** (Bertsch, Pesta, Wiscott & McDaniel 2007 meta-analysis,
k=86, d=0.40 on cued recall). Bjork's desirable-difficulty framing (Bjork &
Bjork 2011) subsumes generation under a broader umbrella but the effect is
**not uniformly d≈1.0** — it shrinks to d≈0.20 when retention interval is
short (<1 day) and when the to-be-generated content is unfamiliar to the
learner (Bertsch et al. 2007). For exam-prep populations working
at-or-below-threshold material, d≈0.25–0.35 is the honest anchor. Robust
across labs, weaker than lab studies on well-learned stimuli suggest.
**Worth building**: yes — but expect d≈0.3, not transformative. Small,
reliable, free.

**Photo-diagnosis (Q1) — external correction vs. metacognitive
self-diagnosis.** The relevant literature is **feedback-timing** and
**generation-then-feedback**, not Karpicke & Roediger's retrieval-practice
line (that's about testing, not external diagnosis). Two findings matter:
(a) Butler, Karpicke & Roediger (2007) — immediate feedback on
incorrect-but-committed responses produces d≈0.5 on transfer vs. no feedback,
but the gain collapses if feedback arrives before the student has *committed*
to a wrong answer (Kulhavy & Stock 1989 response-certitude model). (b)
Metcalfe (2017, Annu Rev Psychol) — error-correction feedback works when the
learner has already engaged their own error-monitoring; it backfires as a
crutch when offered proactively. **Implication for Q1**: narrow framing
(student picked wrong → *then* upload work → diagnosis) is pedagogically
productive, d≈0.3–0.5 on transfer. Broad framing ("ask the tutor anything
with a photo") short-circuits metacognitive monitoring and the evidence
predicts **zero or negative** transfer gain. Reject broad framing.

**Chemistry as conceptual change (Q3).** Johnstone's triangle (1993) — chem
learning requires coordination across macro / sub-micro / symbolic
representations; symbolic-only input teaches symbolic-only understanding.
Taber (2013, Chem Ed Res Prac) documents that persistent misconceptions (ionic
vs. covalent, oxidation states, equilibrium) resist purely algebraic
treatment. A MathInput pretending to be a chem-reaction editor will accept
`H2 + O2 -> H2O` syntactically but cannot distinguish a student who balanced
by coefficient-guessing from one who reasoned sub-microscopically. **Chem
needs its own input** — minimally: coefficient-aware balancer, state symbols,
arrow semantics (→ vs. ⇌), and a per-mol reasoning surface. Shared
`FreeformInputField<T>` with a chem adapter underneath is fine architecturally
but the chem adapter is substantial, not a thin wrapper.

**Hide-then-reveal dimension — per-session wins.** Per-question (A) is too
granular; author-set means authors will forget and the treatment dose goes to
zero. Per-target (C) is paternalistic and opaque (see dark-pattern flag
below). **Per-session student toggle (B)** is the right dimension because
(1) generation effect requires *consistent* application within a study block
to develop the retrieval habit, (2) student autonomy is preserved (d≈0.3 is
too small to justify overriding preference), (3) Kornell & Bjork (2007)
showed students under-select desirable-difficulty conditions — but the fix is
default-nudge-once + honest copy ("try first — retention improves ~15%"),
not forcing.

## Q3 MC-only retention loss — honest number

Free-response vs. MC retention: Kang, McDermott & Roediger (2007, EJCP)
d≈0.35 favoring free-response on 2-day delayed test; McDaniel, Anderson,
Derbish & Morrisette (2007) d≈0.45 on transfer. On Bagrut/PET-style *same-
format* criterion tests the MC deficit shrinks to d≈0.15–0.25 because the
criterion rewards recognition. **Honest verdict**: MC-only for chem and
humanities at Launch is a **moderate loss (d≈0.3)**, not massive (d>0.5) and
not trivial (d<0.2). On a standardized-exam proxy it is closer to d≈0.2.
Shippable-with-disclosure, not shippable-silently. EPIC-PRR-G's claim of
"full content at Launch" is technically honest but pedagogically misleading
if the item format is MC-only — recommend epic copy change.

## Dark-pattern flag: Q2 Option C

Option C (scheduler hides options from high-mastery students, shows them to
low-mastery) is paternalistic-adjacent. It violates the ADR-0048 boundary
test as I applied it to the 14-day exam-week lock in my MULTI-TARGET review:
"would a student feel urgency/condition imposed decoupled from their own
choice?" A student who never chose the hard mode and cannot surface why the
interface behaves differently today is being nudged opaquely. This is not as
severe as a streak counter (GD-004) but it pattern-matches the same anti-
autonomy axis. **Red if shipped as C-default-on; yellow if C is offered only
as an opt-in "coach mode" with transparent copy ("Cena will hide options when
you're strong in this skill")**. Persona-ethics should confirm.

## Section 8 positions

- **Q1 framing**: **narrow only**. Broad framing is pedagogically net-negative
  per Metcalfe 2017; cost argument is secondary. Hard reject broad.
- **Q2 implementation**: **B (per-session student toggle)** with one-time
  default-nudge copy. Reject C as default.
- **Q3 architecture**: shared `FreeformInputField<T>` is fine; chem adapter
  is substantial (not thin) and must implement Johnstone-triangle affordances.
- **Q3 chem Launch-scope**: slip chem to post-Launch over shipping MC-only.
  d≈0.2–0.3 loss is not "degraded" — it's wrong-pedagogy marketed as full
  Bagrut prep.
- **Q3 humanities Launch-scope**: MC-only humanities at Launch is equally
  wrong — rubric DSL exists (PRR-033), wire it.
- **Cost cap**: narrow Q1 + opt-in Q2 + chem-native Q3 stays inside $3.30 if
  Q1 is rate-limited to ~3 photos/session and rubric grading is cached.

## Recommended PRR tasks

1. **PRR-NEW-F**: Ship Q2 hide-then-reveal as per-session student toggle;
   default off; one-time nudge copy citing "+15% retention" (honest, matches
   d≈0.3). Owner: student-web. S.
2. **PRR-NEW-G**: Photo-diagnosis restricted to post-incorrect-MCQ path;
   ship-gate rule blocks broad-framing entry points. Owner: student-web +
   shipgate. M.
3. **PRR-NEW-H**: Chemistry input component — balancer, state symbols, arrow
   semantics, per-mol surface; NOT a MathInput re-skin. Owner: student-web +
   content-eng. L.
4. **PRR-NEW-I**: Rubric-DSL student-facing long-answer component for
   humanities; async grading UX (awaiting_rubric pattern). Owner: student-web.
   M.
5. **PRR-NEW-J**: Epic-PRR-G copy — remove "full Bagrut content at Launch"
   unless chem + humanities ship with native input. Owner: content-eng. S.
6. **PRR-NEW-K**: Cogsci-validated metric — log whether students who toggle
   Q2-on show higher delayed-session accuracy on repeated skills; honest
   observational, not causal. Owner: analytics. S.

## Blockers

- **Red if shipped**: Q1 broad framing. Metacognitive-crutch risk is not a
  cost argument, it's a pedagogy argument. No amount of rate limiting fixes
  the wrong mental model.
- **Red if shipped**: Q2 Option C as default-on-silent. Re-scope as opt-in
  transparent coach mode or drop.
- **Red if shipped**: chem MC-only sold as Bagrut-Chem-at-Launch. Honest
  label or slip.
- **Yellow**: Q2 copy overstating effect size ("dramatically boost retention"
  etc.). Use d≈0.3 / "~15% improvement on delayed recall" as the honest
  anchor. Same lesson as Brunmair d=0.34 in the MULTI-TARGET review.

## Questions back to decision-holder

1. Is Q1 definitely scoped to post-incorrect-MCQ, or is there a PM desire to
   surface photo-upload earlier in the attempt flow? (If the latter, expect
   red flag.)
2. For Q3 chem, is the content-eng team resourced to build chem-native input,
   or is "slip chem" the realistic call? Either is fine — MC-only-and-label-
   it-Bagrut-Chem is not.
3. For Q2 default-nudge copy, will legal/marketing accept "~15% retention
   improvement" as the public number? It's accurate; it's also smaller than
   ed-tech copy usually wants.
