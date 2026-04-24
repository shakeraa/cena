---
persona: cogsci
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: yellow
---

## Summary

Default state and diagnostic applicability are the two decisions that most
move the learning needle; both deserve harsh-honest handling. **Visible-first
with opt-in hide ships near-zero generation effect** (selection bias on a
~d=0.30 effect collapses to d≈0.05–0.10 in practice). Hide-first captures the
real effect but adds friction; the honest compromise is hide-first *within a
"practice mode" session type*, never as global default. For modalities:
exam-fidelity > cognitive-load for math (Bagrut/PET are paper); typing is
better for literature at high-school scale (Berninger shows a *fluency*
dependency, not a universal typing loss). Chem needs both surfaces because
Johnstone's triangle does not collapse to one representation.

## Section 6.2 answers

**Default state (3.1) — does the Bjork/Bertsch effect hold under
hidden-by-default vs visible-by-default-with-opt-in-hide?** No — not
symmetrically. The generation effect (Bertsch, Pesta, Wiscott & McDaniel
2007 meta-analysis, k=86, **d=0.40** on cued recall) requires that the
retrieval attempt actually happens. Kornell & Bjork (2007, 2008) and
Karpicke, Butler & Roediger (2009) all document the same **metacognitive
misalignment**: students systematically under-select desirable-difficulty
conditions because they feel less fluent. If hide is opt-in from a
visible-default, the students who most need generation (low-mastery, exam-
anxious) will opt out — the subpopulation actually practicing generation
becomes the already-strong, and the treatment dose on the target
population approaches zero. The realized effect on a visible-default build
is **d≈0.05–0.10**, not d=0.30. Hide-default inside a *practice mode* is
the only build that ships the actual effect. Compromise posture: hide is
default ON inside "practice mode" sessions (student-chosen, one-click
switch); visible-default is the norm for graded attempts, diagnostic, and
exam simulations. Do **not** ship visible-default-with-opt-in-hide and
claim generation-effect pedagogy — that is literature-to-copy
misrepresentation.

**Diagnostic applicability (3.5) — does hide-reveal belong in PRR-228?**
**No. Contaminates.** PRR-228 is a 6–8-item adaptive block measuring
**retrieval strength on skill primitives** to seed per-target mastery.
Mixing hide-reveal into that measurement conflates three constructs:
(1) skill mastery, (2) recognition-vs-recall disposition, (3) student's
chosen self-test intensity. The Roediger & Karpicke (2006) test-enhanced-
learning studies are clear that retrieval-strength assessment and
retrieval-practice-as-intervention are separable — and the diagnostic is
the former, not the latter. The 85% completion floor is also at risk: adding
a cognitive-load multiplier to the easiest-first onboarding arc will shave
several points off completion and the loss is bigger than the
measurement-fidelity gain. **Ship PRR-228 with options always visible;
apply hide-reveal only in post-diagnostic practice sessions.**

**Math typing vs handwriting (4.1) — Berninger vs Mueller & Oppenheimer.**
Mueller & Oppenheimer (2014) "The Pen is Mightier than the Keyboard"
studied *lecture note-taking*, not math production — the effect (better
conceptual retention for handwritten notes, d≈0.3) replicates unevenly
(Morehead, Dunlosky & Rawson 2019 direct replication at k=3 found
near-zero on factual and **null** on conceptual). **Don't cite Mueller &
Oppenheimer for math input — it is the wrong paradigm.** Berninger et al.
(1997, 2002, 2006) is relevant: for *composition* handwriting recruits
more grapho-motor-linked verbal working memory than typing in
low-fluency writers, advantage shrinks to zero with keyboard fluency. For
**math specifically**, the dominant cogsci finding is **exam-fidelity
practice effects** — transfer from practice modality to test modality is
the single biggest variable (Roediger & Karpicke 2006 transfer-appropriate
processing). Bagrut/PET quant is paper. **Writing-pad primary for math is
the pedagogically correct call; MathLive trains a modality that will not
be available on exam day.** Mueller-Oppenheimer is not the supporting
citation; transfer-appropriate processing is.

**Chem (4.3) — Johnstone + Taber coverage of typed + Lewis-pad combo.**
Johnstone (1993, 2000) insists chem understanding requires coordination
across macro / sub-micro / symbolic representations; Taber (2013) adds
that conceptual change resists purely algebraic treatment. Typed-only
captures **symbolic only**; writing-pad Lewis structures capture
**sub-microscopic** (bonding, electron domain, geometry reasoning). A
*combined* input where students balance symbolically AND draw Lewis for
mechanism items captures two of the three vertices of the triangle.
Macro (observation-of-reaction) is still missing — that's simulation /
video, post-Launch. **Combo is the minimum viable chem pedagogy;
typed-only would be wrong-model, writing-pad-only would be slow and
underuse the CAS-style balance checker.** Persona-educator's RDKit
balance-check proposal is the right symbolic oracle.

**Literature (4.4) — handwriting-vs-typing essay quality, high-school
populations.** Berninger, Abbott, Augsburger & Garcia (2009) and
Connelly, Gee & Walsh (2007) both find a **fluency-mediated** effect:
high-school typists who type at ≥80% of their handwriting fluency produce
**equal or better** essay quality typed; those below that fluency threshold
show handwriting advantage of d≈0.25–0.35. For Israeli high-school
populations in 2026, keyboard fluency (Hebrew + English) is saturated.
Arabic keyboard fluency is lower in some tenant populations — tenant-level
data would be needed. Default posture **keyboard-only is correct** for the
main population; Arabic-primary tenants may need a writing-pad opt-in
with HWR, with quality caveat surfaced. Don't ship this as "no difference"
— it's "no difference at saturated fluency, handwriting advantage below."

## Section 7 positions

- **Q3.1 default state**: hide-first inside practice-mode session type;
  visible-first elsewhere. Reject global visible-with-opt-in-hide as
  generation-effect build — it isn't.
- **Q3.2 UI pattern**: placeholder-replacement over `<details>`
  (affordance clarity, bigger target).
- **Q3.3 server-side enforcement**: optional, student-controlled. Classroom-
  enforced without consent is mild dark pattern (see persona-ethics).
- **Q3.4 scaffolding**: unavailable until student commits a first guess (free-
  form) then reveals — preserves generation, gives scaffolding a real
  anchor.
- **Q3.5 diagnostic**: **no** — PRR-228 visible-options only.
- **Q3.7 commit-and-compare**: ship it. This is the **actual** generation
  effect (produce-then-check), not just hide-then-reveal. Without
  commit-first, hide-reveal is theatre.
- **Q4 math**: writing-pad primary, MathLive secondary (exam-fidelity wins).
- **Q4 chem**: typed + Lewis-pad combo, both first-class.
- **Q4 literature**: keyboard-only main; Arabic-primary tenants opt-in pad.
- **Q4 HWR**: Claude Sonnet vision (reuses MSP) is the fastest to ship,
  acknowledge ~90% accuracy is a pedagogy problem when CAS-grading. Cap
  HWR calls per session to preserve finops.

## Recommended PRR tasks

1. **PRR-NEW-L**: Practice-mode session type with hide-first default;
  commit-then-reveal flow; generation-effect copy anchored at d=0.30
  "~15% improvement on delayed recall." Owner: student-web. M.
2. **PRR-NEW-M**: PRR-228 diagnostic exclusion — block hide-reveal during
  onboarding-diagnostic session type; guardrail test. Owner: student-web
  + shipgate. S.
3. **PRR-NEW-N**: Writing-pad primary for math (MathPad.vue); MathLive
  demoted to secondary toggle. Subject-specific HWR adapter using Claude
  Sonnet vision. Owner: student-web + content-eng. L.
4. **PRR-NEW-O**: Chem combo input — typed reaction/stoichiometry +
  Lewis-pad with RDKit-class balance checker; both first-class. Owner:
  student-web + content-eng. L.
5. **PRR-NEW-P**: Cogsci-validated metric — log practice-mode opt-in rate,
  commit-before-reveal rate, delayed-recall delta on repeated skills;
  observational, not causal. Owner: analytics. S.
6. **PRR-NEW-Q**: Copy audit — any surface claiming "generation effect"
  must be gated behind commit-first UX; no generation-effect language on
  visible-default screens. Owner: content-eng + shipgate. S.

## Blockers

- **Red if shipped**: visible-default-with-opt-in-hide marketed as
  generation-effect pedagogy. Realized d≈0.05–0.10; the claim is
  literature-to-copy misrepresentation.
- **Red if shipped**: hide-reveal inside PRR-228 diagnostic. Contaminates
  retrieval-strength measurement and threatens 85% completion floor.
- **Red if shipped**: MathLive-primary-for-Bagrut-math branded as
  exam-fidelity. Transfer-appropriate processing predicts negative
  transfer to paper exam.
- **Yellow**: chem typed-only; underuses Johnstone-triangle coverage and
  leaves Lewis-structure reasoning unassessed.
- **Yellow**: HWR at ~90% accuracy on CAS-graded math without a
  "confirm recognition before grade" step will mis-grade correct answers
  — pedagogy-negative and trust-negative.

## Questions back to decision-holder

1. Is practice-mode-as-separate-session-type acceptable UX? If everything
  must live in one session flow, generation-effect pedagogy downgrades to
  "available, rarely used" and we should stop citing d=0.30.
2. Is writing-pad primary for math acceptable given the HWR accuracy gap,
  or is the exam-fidelity argument weaker than the mis-graded-correct-
  answer risk? (I weight exam-fidelity higher, but this is a real
  tradeoff.)
3. Arabic-primary tenant literature pad — tenant-level data on keyboard
  fluency available? If not, default keyboard-only for v1 and revisit.
