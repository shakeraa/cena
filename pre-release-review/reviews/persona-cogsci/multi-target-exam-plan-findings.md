---
persona: cogsci
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

The multi-target data model is pedagogically sound — students really do have
multiple concurrent exams, and "grade" is correctly demoted to a per-target
attribute. The scheduler design, however, leaks cogsci: weighted round-robin
across sessions is **alternation, not interleaving**; the 14-day "exam-week
lock" as currently framed pattern-matches to a ship-gate violation; a unified
cold-start diagnostic biases the first-week allocation; and Bagrut-math +
Psychometry-quantitative overlap topically enough that BKT signals will
double-count unless the mastery key is skill-scoped, not target-scoped. Ship
the aggregate; revise the scheduler + diagnostic + 14-day lock before launch.

## Section 9.2 answers

**Q1 — Is weighted round-robin interleaving?** No. The interleaving effect
(Rohrer & Taylor 2007; Rohrer, Dedrick & Burgess 2014, JEP:A d≈1.05 on the
upper end; Brunmair & Richter 2019 meta-analytic d=0.34 with heterogeneity)
depends on **within-block discrimination practice** — the learner must
classify which strategy applies on problems whose topic label is hidden, on a
timescale where the prior trial's schema is still accessible in working
memory. Alternating single-target sessions across days is closer to the
**spacing effect** (Cepeda et al. 2006, Psychol Bull — optimal gap ~10–20% of
retention interval; Kang 2016). Spacing is real and it's what round-robin
actually delivers. It's just not what the doc claims (brief §6). Fix the
language: "cross-session spacing across targets" — not "interleaving." The
desirable-difficulty effect (Bjork & Bjork 2011) subsumes both, but conflating
them overstates the expected gain.

**Q2 — Is single-target-per-session losing desirable difficulty for 2+ target
students?** Partially. Within-session interleaving of **skills inside one
target** is still possible (e.g. algebra vs. geometry inside Bagrut Math 5U)
and delivers most of the Rohrer effect. The loss is cross-target
discrimination practice (e.g. PET-quant probability problem alongside Bagrut
combinatorics), which is plausibly desirable for the overlap zone but has no
direct RCT evidence in standardized-test prep contexts. Acceptable v1
trade-off; log it honestly in ADR-0049 so v2 can revisit.

**Q3 — Diagnostic cold-start bias.** Yes, a unified diagnostic biases
allocation. The first ~20 items across 2–4 targets will be unevenly
distributed in practice (fatigue by the end, item-difficulty calibration
differs across exam families, PET-quant and Bagrut-math share items but score
on different scales). BKT posterior initialized from this cross-target pool
will systematically misestimate the weaker-coverage target — usually the one
drawn later in the battery (classic order effect; Cepeda et al. 2006 on
testing-position artifacts). **Least-bad alternative**: run a small
per-target diagnostic block (6–8 items each), seeded with a shared 3-item
warmup; report per-target priors separately; never average across targets.
Cost: ~3 extra minutes of onboarding for a 3-target student. Cheap insurance.

**Q4 — 14-day exam-week lock: evidence-backed or dark-pattern?** **Yellow
bordering on red.** The evidence for pure massed practice in the final two
weeks is thin — the cramming literature (Rawson & Dunlosky 2011; Kornell
2009) shows massed practice looks good on immediate tests and degrades
retention, which is irrelevant for a once-off Bagrut. So there's a defensible
argument: the target IS the immediate test. But ADR-0048's boundary test is
operational: "would a student feel urgency decoupled from mastery state?" A
**silent** scheduler bias (it quietly favors the near-deadline target) is
time-awareness. **Labeling** that state as "exam-week lock," "crisis mode,"
or surfacing a 14-day countdown crosses into PRR-019 / GD-004 territory.
Recommendation: keep the scheduler behavior; rename internally
("deadline-proximity weighting"); never surface the 14-day threshold as UI
copy or a state badge to the student; parent-facing opt-in only per ADR-0048
§"Per-family informational opt-in."

**Q5 — Interaction with PRR-066 retrieval reframe?** Retrieval prompts
(formulas → application triggers) are skill-scoped, not target-scoped; no
conflict. But if a formula is shared across targets (Pythagoras in Bagrut-5U
geometry and PET-quant), the retrieval prompt should fire under whichever
target's session is active, without double-crediting the recall attempt to
both targets' HLR. This requires skill-keyed HLR, not target-keyed HLR —
which ties directly to Q7 below.

## Additional findings

**Weighted round-robin weights drift from mastery reality.** The student sets
`WeeklyHours` per target at onboarding. Brief §6 explicitly forbids
auto-rebalancing by diagnostic performance (correctly — that's paternalism,
ADR-0048). But the research on student-directed study (Kornell & Bjork 2007,
JEP:L&C — students systematically over-study easy and under-study hard
material) means the declared allocation is often miscalibrated. Ship-safe
middle path: show the student a **non-coercive weekly "here's where your time
went vs. your plan" honest mirror** (no nudging, no red alerts, just the
data). Lets autonomy stand while giving metacognitive feedback that is
documented to improve self-regulation (Zimmerman 2002).

**PET verbal native-language constraint.** The brief §4 correctly notes PET
Verbal Hebrew ≠ PET Verbal Arabic. From a cogsci standpoint the learning
trajectories are language-native — the mastery model for PET-Verbal-Hebrew
and PET-Verbal-Arabic are distinct skills, not translations. ADR-0049 must
make this explicit so a student who studies both does not have cross-target
mastery leakage.

**Retake cohort is a distinct cogsci surface.** A Bagrut retake candidate
has a prior score and a tight timeline (often 3–6 months). Cumulative
practice / retrieval-strength research (Karpicke & Roediger 2008) suggests
retake students benefit more from spaced retrieval over the known curriculum
than from comprehensive diagnostic re-mapping. The brief treats retakes as
just "another target" — fine for v1 data model, but flag for v2: retake
targets should pre-fill a higher weekly-hours default and skip the full
diagnostic in favor of a weakness-focused warmup.

## Section 10 positions

**Q2 (free-text note)**: Drop. Pedagogically adds nothing the scheduler can
use; persona-privacy is correct that it's a PII trap. If kept, restrict to
prefilled tags ("retake", "first attempt", "accommodated time").

**Q3 (targets cap)**: 4 is correct. Cognitive load on plan management and
study-context switching (Rubinstein, Meyer & Evans 2001 on task-switching
costs) rises nonlinearly past 3–4. A real student with 3 Bagrut subjects +
PET is already near the ceiling. Reject requests for 6–8; those users are
almost always teachers/testers, not students.

## Recommended new PRR tasks

1. **PRR-NEW-A**: Per-target diagnostic blocks with shared warmup (replaces
   unified cold-start). Owner: cogsci + frontend. M.
2. **PRR-NEW-B**: Rename "exam-week lock" to "deadline-proximity weighting"
   everywhere in code/UI; add ship-gate rule so `examWeekLock`, `crisisMode`,
   `14DaysLeft` identifiers fail CI. Owner: shipgate. S.
3. **PRR-NEW-C**: Skill-keyed (not target-keyed) HLR / BKT to prevent
   double-counting across Bagrut-math ↔ PET-quant overlap. Coordinate with
   PRR-072 coverage matrix and ADR-0049. Owner: actors. L.
4. **PRR-NEW-D**: "Honest mirror" weekly cadence surface — planned vs actual
   hours per target, no nudging copy. Subject to ADR-0048 review. Owner:
   student-web. M.
5. **PRR-NEW-E**: Deadline-proximity weighting must not surface as UI copy
   or badge; add test asserting no `days-until`/`N-days-remaining` strings in
   student routes. Owner: shipgate. S.

## Blockers / non-negotiables

- **Red if shipped as written**: any surface that shows the student a 14-day
  or N-day countdown tied to a target deadline. Violates ADR-0048 + PRR-019.
- **Red if shipped as written**: shared BKT posterior across Bagrut-math and
  PET-quant (double-counting). Requires skill-keyed mastery decomposition
  before scheduler can weight fairly.
- **Yellow**: "interleaving" language in section 6 — change to "cross-session
  spacing" or the ADR-0049 effect-size claims will overstate Rohrer
  literature. Brunmair d=0.34 is the honest anchor, not d=1.05.

## Questions back to decision-holder

1. Is the 14-day threshold intentionally visible to students (it's a UI
   feature) or silent (scheduler-only)? If visible, it must go.
2. Does the mastery model key on skill-ID or on (target-ID, skill-ID)? The
   brief doesn't say, but the answer changes the scheduler fundamentally.
3. Are teachers allowed to see cross-target mastery aggregates for their
   class (parent-aggregate-adjacent question, EPIC-PRR-C)? If yes, k-anonymity
   per ADR-0003 Decision-2 applies.
