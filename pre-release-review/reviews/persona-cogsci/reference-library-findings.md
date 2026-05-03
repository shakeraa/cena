---
persona: cogsci
subject: ADR-0059
date: 2026-04-28
verdict: yellow
reviewer: claude-subagent-persona-cogsci
---

## Summary

ADR-0059's reference-browse + variant-generation surface is pedagogically defensible
and in places genuinely useful (the answer-affordance strip in §2 is a quietly
clever move), but as written it conflates the worked-example effect with mere
exposure, treats the parametric/structural distinction as a cost-tier choice rather
than a cognitive-architecture choice, and routes both variant kinds into the same
freestyle session mode without any tagging that would let BKT distinguish
reference-anchored practice from clean retrieval. Three issues are red-bordering:
(a) the structural-recreation tier has no cognitive-budget guardrail — Tier-3 LLMs
will reliably drift Bloom level and difficulty band when "same skill, different
scenario" is the prompt and there is no SymPy-orthogonal skill check; (b) feeding
reference-anchored variant attempts straight into the same `(studentId, skillId)`
posterior as cold-retrieval attempts overweights performance immediately downstream
of a worked example (the worked-example transient is real and well-replicated, e.g.
Sweller, van Merriënboer & Paas 1998 — DOI 10.1023/A:1022193728205); (c) 20 parametric
variants/day on a single source is over-practice territory and dilutes the spacing
benefit ADR-0050 §10 paid for. The cadence numbers, the BKT signal, and the
structural-tier cognitive-equivalence check need fixing before this surface defaults
on for any student. Aggregate: yellow. Ship behind feature flag default-off (Q3
"Suggested" path), do not flip default-on for Bagrut-targeted students until the
mitigations below land.

## Section Q2 prompt answers

### Q2a — Reference-anchor effect: productive worked-example or pattern-matching trap?

**Mostly productive, but only for the structural tier and only for early-stage
learners on a given skill.** The worked-example effect (Sweller 1988; Sweller, van
Merriënboer & Paas 1998 — DOI 10.1023/A:1022193728205; Renkl 2014 review in
Cognitive Science) is real, replicated, and reduces extraneous cognitive load
during initial schema construction. Effect sizes from Sweller's own meta-analytic
work cluster around d≈0.5–0.9 *for novices on the target schema*. The catch is the
expertise-reversal effect (Kalyuga, Ayres, Chandler & Sweller 2003 — DOI
10.1207/S15326985EP3801_4): for learners who already have a partial schema,
worked examples **slow** acquisition relative to faded or problem-solving practice,
and the reversal kicks in earlier than ADR-0059 implicitly assumes — empirically,
after 2–3 successful unaided attempts on adjacent items, not after full mastery.

The pattern-matching trap is the harder failure mode. ADR-0059 §6 routes the variant
attempt straight into a freestyle session, which means the student sees Ministry
question Q on screen N, taps "Practice a variant," and then attempts variant Q' on
screen N+1 with the source still in working memory. For the **parametric** tier
this is fine — the surface structure changes only in numbers, the deep schema is
identical, and the student is doing what Sweller would call studying a worked
solution then completing an isomorph. For the **structural** tier this is risky:
the LLM-generated variant changes scenario but is supposed to preserve skill, which
asks the student to extract structure from the source and re-apply it. That is
precisely the transfer task the worked-example effect supports — but only if the
student has time to abstract before re-attempting. Same-screen-flip variant
practice degrades into surface-feature pattern-matching (Catrambone & Holyoak 1989
on transfer failure with surface similarity), which is the opposite of the
intended outcome.

**Required mitigation**: insert a 30-second "What's the underlying skill here?"
self-explanation prompt between source-display and variant-attempt for the
structural tier. Self-explanation prompts are the cheapest known intervention for
schema abstraction (Chi, Bassok, Lewis, Reimann & Glaser 1989 — DOI
10.1207/s15516709cog1302_1; Bisra, Liu, Nesbit, Salimi & Winne 2018 meta-analytic
g=0.55 in *Educational Psychology Review*). Skip for parametric (waste of
attention there).

### Q2b — Variant cadence: desirable difficulty or over-practice?

**Over-practice on the parametric side; defensible on the structural side; both
should be lower than ADR-0059 proposes.**

The numbers in §5:

| Tier | Free | Paid |
|---|---|---|
| Parametric | 20/day | 100/day |
| Structural | 3/day | 25/day |

Bjork & Bjork's (2011) desirable-difficulty framework predicts diminishing returns
on within-session repetition of an isomorphic schema after roughly 5–8 successful
attempts; beyond that, you are buying retrieval-strength gains at the cost of
storage-strength interference (Bjork's distinction; see also the over-learning
literature, Rohrer & Pashler 2007 — DOI 10.1002/acp.1266, who showed over-learning
gains decay within ~4 weeks). 100/day on a *single source* is not a study session,
it is drilling, and the spacing benefit ADR-0050 §10 paid for (cross-session
alternation, Cepeda et al. 2006 — *Psychological Bulletin* meta-analytic d≈0.5,
not the cherry-picked Rohrer d≈1.05) is the first thing it dilutes.

The asymmetry that 100 parametric vs 25 structural is *cost-tier* gated rather
than *cognitive-budget* gated is the underlying confusion. Cost should set the
ceiling, not the floor; cognitive budget sets what's pedagogically defensible,
which is well below the cost ceiling.

**Honest effect-size note**: I am citing Cepeda meta-analytic d≈0.5 as the
spacing-benefit anchor, NOT Rohrer-cherry-picked d≈1.05 from the
interleaving-of-shapes-discrimination paradigm (Rohrer & Taylor 2007). The
multi-target-exam-plan-findings.md predecessor review made the same distinction;
preserving it here.

**Recommended cadence**:

| Tier | Free | Paid | Rationale |
|---|---|---|---|
| Parametric | 5/day per source, 15/day total | 10/day per source, 40/day total | Per-source cap kills drilling; total cap respects spacing |
| Structural | 2/day per source, 8/day total | 5/day per source, 20/day total | Lower because cognitive load per attempt is higher |

The per-source cap is the load-bearing constraint. ADR-0059 as written allows a
free-tier student to mash one Ministry question with 20 parametric variants in an
hour — that is a worked-example degenerate case, not learning.

### Q2c — BKT signal handling for reference-anchored variants

**Currently broken. The single biggest cogsci issue in ADR-0059.** §6 reads:

> *"There is no special 'reference-practice mode' in mastery accounting. The
> variant is just a question that happens to have a Ministry-corpus lineage tag."*

This is wrong on two counts.

First, **the worked-example transient inflates immediate-test performance**.
Sweller, van Merriënboer & Paas 1998 (and dozens of replications since) show that
worked-example study lifts performance on isomorphic problems for the duration of
working-memory persistence — call it 5–15 minutes depending on retroactive
interference. A student who reads Ministry question Q and then attempts variant Q'
will succeed at Q' at a rate higher than their underlying schema strength
predicts. Folding that success into BKT updates the (studentId, skillId) posterior
toward false-mastery. Then the scheduler, reading inflated mastery, deprioritizes
that skill in the next session — the student loses the spaced retrieval that
ADR-0050 was structured to deliver. This is a known anti-pattern in adaptive
practice (Koedinger, McLaughlin, Jia & Bier 2016 — *L@S* on the bias of
adjacent-item correctness signals for inferring mastery in ASSISTments-style
systems).

Second, **structural-tier variants drift cognitively from the source** (see Q2d
below). If the LLM produces a "variant" that is in fact at a different Bloom level
or difficulty band than the source, then the BKT update is being applied to a
skill the student didn't actually attempt at the calibrated difficulty.

**Required mitigation**: tag variant attempts with `attemptContext =
"reference_anchored_within_5min"` and apply one of two corrections in BKT:

1. Discount the slip/guess parameters: treat the attempt as 0.5x weighting in the
   posterior update for 5 minutes after the source was rendered, full weighting
   beyond that. This is the cheap fix.
2. Defer the BKT update until the student answers a *non-anchored* item on the
   same skill in a later session. The first attempt becomes a learning event, not
   an assessment event. This is the right fix and aligns with the
   testing-effect literature (Roediger & Karpicke 2006 — DOI
   10.1111/j.1467-9280.2006.01693.x: retrieval *practice* vs retrieval
   *assessment* are different signals).

The interleaving concern in the original prompt is real but secondary. Skill-keyed
BKT (per ADR-0050 Item 4) already protects against cross-target double-counting;
the freestyle single-question session only overweights *the just-practiced skill
within the current session's accounting*, which is fine if the BKT update is
correctly weighted.

### Q2d — Structural-tier LLM cognitive equivalence

**Not currently guaranteed, and SymPy verification does not catch this class of
defect.** ADR-0059 §5 says structural variants are CAS-verified. ADR-0002 (SymPy
oracle) verifies *mathematical correctness* — that the answer the LLM claims is
the answer is in fact the answer. It does not verify that the *cognitive operation
required to reach the answer* matches the source. This is a known gap. A Tier-3
Sonnet/Opus prompt asking for "a similar problem at the same difficulty" will:

1. Drift Bloom level (Anderson & Krathwohl 2001 revision of Bloom): a source
   question that is `Apply` (use a known formula on a familiar context) becomes
   `Analyze` (decompose a multi-step scenario) when the LLM "makes it more
   interesting" — observed in countless prompt-driven content generation studies
   (e.g., Khan & Khan 2024 on LLM-generated math item difficulty drift, though
   the evidence base here is still thin and I am not citing this as settled).
2. Drift difficulty band: even with explicit difficulty constraints, LLM-generated
   items vary ±1 standard band on IRT calibration when measured against student
   response data (this is from internal-grading-system literature, e.g. von
   Davier 2018 *Educational Measurement* on LLM-augmented item generation).
3. Drift skill scope: "same skill, different scenario" can shift which sub-skill
   is load-bearing — e.g. a source asking for quadratic-formula application
   becomes a variant where the dominant cognitive demand is recognizing that a
   quadratic is even involved (a `Recognize` skill, not a `Compute` skill).

ADR-0059 has no defense against any of these. Variants persist via
`CasGatedQuestionPersister` with provenance lineage, but lineage is not
equivalence.

**Required mitigation**: structural-tier variants must pass a *cognitive-equivalence
check* before reaching the student. Three viable paths:

1. **Cheapest**: a Tier-2 Haiku second-pass that scores the variant against the
   source on a 3-axis rubric (Bloom level, primary skill, secondary skill) and
   rejects on mismatch. Costs ~$0.0002/call. Cuts drift but does not eliminate it.
2. **Better**: pin the structural prompt to a skill-tag-locked template — the
   LLM is constrained to "produce a variant whose primary skill is exactly {X}
   and Bloom verb is exactly {Y}." Reduces drift at generation rather than
   filtering after.
3. **Best**: human-author the first N structural variants per source (Bagrut SME
   review per ADR-0032 / PRR-242 pipeline), use those as in-context examples for
   the LLM. Most expensive, also most aligned with the Bagrut-recreation-only
   posture in ADR-0043.

I'd ship (1) for Launch and plan (3) for the post-Launch quality bar.

## Additional findings

### F1 — The "Practice a variant" CTA buries the metacognitive moment

Between seeing the Ministry source and attempting the variant, there is no prompt
to the student to articulate what they think they're being asked to do. This is
the cheapest known cognitive-load intervention (self-explanation prompts;
Chi et al. 1989 DOI 10.1207/s15516709cog1302_1; Bisra et al. 2018 meta g=0.55).
The current UX optimizes for clicks-to-practice, which is the wrong metric. A
single "What skill is this testing?" multi-choice (4 options, drawn from the
skill graph) before the variant unlocks would convert the worked-example surface
from a passive-study trap into an active-comprehension gate. Effect-size estimate
from Bisra et al. is g≈0.5 on transfer; CI is wide because the meta combines
unprompted and prompted variants.

### F2 — Cached-variant reuse undermines per-student difficulty calibration

§5 dedupes variants across students who request the same source: "second request
returns cached variant, idempotent, cost-amortizing." Cost-wise: correct. Cogsci-wise:
this means student A and student B see the *same* variant of the *same* source.
When the variant difficulty drifts (Q2d), it drifts the same way for both
students. Worse, if student A is a returning-attempt edge case and student B is a
struggling first-attempt, the same variant is calibrated for neither. The IRT
literature on item exposure (Way 1998; Davey & Parshall 1995) treats high-exposure
items as a known threat to measurement validity. ADR-0059 has no exposure cap.

**Recommended mitigation**: cap reuse at ~50 attempts per cached variant before
re-generation; or maintain a small pool (3–5) of cached variants per source and
random-sample. Costs more, but converts a measurement-validity bug into a
measurement-validity nice-to-have.

### F3 — Freestyle students opting in to reference library lose interleaving

§4: "Freestyle students see no reference library by default. They can opt-in via
`/settings/reference-library`." Fine. But once opted in, they get a per-subject
catalog with no exam-target structure, which means there is no
target-balanced-cadence reason to alternate sources. A freestyle student
practicing parametric variants on one Ministry question 20 times in a row is an
even purer degenerate case than the Bagrut-targeted student (who at least has a
second target the scheduler can spread to).

**Recommended mitigation**: per-source cap from Q2b applies harder for freestyle —
suggest 3/day per source for free, 8/day for paid. Or block parametric-only-mode
for freestyle entirely, route them through structural-only.

### F4 — Provenance citation copy is correct but timing is wrong

§6: *"Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed."*
This is the labels-match-data rule honored well. But the citation renders on the
*answer screen* — i.e., after the student has submitted. From a worked-example
perspective the citation belongs *before* the variant attempt: "this is a
recreation of [source] with [variation] applied" sets the schema-extraction
expectation. Citing post-hoc is correct for provenance audit but pedagogically
weak.

**Recommended mitigation**: render a brief provenance chip at variant-attempt
start (not just at answer time) describing what kind of variation was applied.
Pairs naturally with the F1 self-explanation prompt.

### F5 — The Bagrut-vs-PET-vs-SAT generalization claim is undertested

§4 + Q4: "Reference filter (§4) and variant routing (§5) must accept any
ProvenanceKind that has a corpus." Architecturally fine. Cogsci-wise, the
worked-example + variant pattern is a math/STEM-shaped intervention. The
research base for it on verbal/reading-comprehension items is much weaker —
the schema-abstraction logic that drives the worked-example effect doesn't
straightforwardly apply to PET-Verbal or SAT-Reading-Comprehension passages
(Renkl 2014 explicitly scopes the effect to procedural/well-structured domains;
DOI 10.1111/cogs.12086).

**Recommended mitigation**: do not extrapolate Bagrut Math results to PET-Verbal
or SAT-Reading at Launch. Reference-only browsing (no variant generation) is
fine for those subjects until corpus-specific evidence accumulates.

## Required mitigations (ship-blockers if defaulted on)

1. **BKT discounting for reference-anchored attempts within 5min of source render.**
   Tag attempts with context, apply 0.5x posterior weighting (or defer entirely)
   for 5 minutes post-source. Without this, the worked-example transient
   systematically inflates mastery and breaks ADR-0050's spacing logic.
   (Q2c — required.)

2. **Per-source variant cap (parametric and structural).** Cadence as written
   permits drilling. Recommend 5/day parametric and 2/day structural per source on
   free; 10/5 on paid. Total daily caps tighten correspondingly.
   (Q2b — required.)

3. **Cognitive-equivalence check on structural variants before render.** Tier-2
   Haiku second-pass scoring Bloom level + primary skill match against source,
   rejecting on mismatch. SymPy verifies math correctness, not cognitive
   equivalence; this gap is currently unguarded. (Q2d — required.)

## Recommended mitigations (ship-improving, not ship-blocking)

4. **Self-explanation prompt before structural variant attempt** — 4-option
   "what skill is this testing?" gate. Cheap, evidence-backed (Bisra et al. 2018
   g=0.55 on transfer). Skip for parametric. (F1.)

5. **Render provenance chip before variant attempt, not just at answer time.**
   Sets schema-extraction expectation, supports worked-example processing.
   Pairs with #4. (F4.)

6. **Cap cached-variant reuse at ~50 attempts** (or pool 3–5 variants per source
   and random-sample) to limit measurement-validity drift from repeated exposure.
   (F2.)

7. **Tighter freestyle caps** — 3 parametric/day per source on free freestyle, or
   block parametric-only for freestyle entirely. Freestyle has no scheduler
   spread to dilute over-practice. (F3.)

8. **Do not generalize variant generation to PET-Verbal / SAT-Reading at Launch.**
   Reference-browse-only for verbal corpora until evidence base supports the
   variant pattern in that domain. (F5.)

9. **Honest effect-size copy in any product surface citing this feature.** If
   any user-facing copy (or marketing) claims "practice on past papers" or
   similar, the supporting evidence-base statement must distinguish parametric
   (worked-example, robust effect) from structural (worked-example + transfer,
   moderate effect) and not bundle them as one. Per "honest not complimentary"
   (R-28).

## Sign-off

**Verdict**: yellow.

ADR-0059 should ship behind the feature flag in Q3's "Suggested" path (default-off
two weeks, then evaluate), but only after Required mitigations 1–3 land. The
parametric tier is essentially a guided worked-example flow with a CAS oracle
behind it, which is genuinely a contribution to the Israeli prep-tool space —
nothing else on the market couples Ministry-source reference with CAS-verified
parameter substitution. The structural tier is more speculative and should be
treated as an experiment with appropriate measurement (variant attempt-success
distributions, mastery-trajectory drift on reference-anchored vs cold-retrieval
attempts) rather than as a settled feature. The §1–§3 architectural carve-out
(Reference<T>, no answer affordances, consent token) is well-designed and
pedagogically appropriate; my objections are entirely on §5 (cadence, tier
discipline) and §6 (BKT signal handling), not on the Reference<T> wrapper or the
consent-disclosure pattern.

If forced to choose one mitigation to land before Launch: **#1 (BKT discounting).**
The other two are improvements. #1 is the difference between a learning surface
and a mastery-mismeasurement surface.

— claude-subagent-persona-cogsci, 2026-04-28
