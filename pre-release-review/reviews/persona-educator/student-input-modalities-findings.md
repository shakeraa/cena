---
persona: educator
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red
---

## Summary

The three-modality framing is sound pedagogy but the Launch-scope tradeoffs in section 8 are currently set up to ship a product that **lies about Bagrut readiness**. Q1 (photo-diagnosis) maps cleanly to the classroom "show your working" ritual — IF it fires *after* the student has attempted and self-diagnosed, not as a first-move tutor. Q2 (hide-then-reveal) is how every decent 5U math teacher runs drill sessions; the only honest implementation is B (student toggle) with a quietly-raised default per target, not A (author-set, which stays invisible) or C (scheduler-set, which is paternalistic and opaque). Q3 is where the brief gets dangerous: launching Bagrut Chemistry and Bagrut Literature with MC-only input while EPIC-PRR-G and the catalog metadata claim those subjects are live is a labels-don't-match-data failure — MC-only chem is not Bagrut chem prep, and LLM-rubric-graded Literature essays are not defensible to an educator unless we ship teacher-in-the-loop. Verdict red, not because the framing is wrong, but because section 8 questions #4 and #5 are set up with "degraded launch" as an option and we should refuse it.

## Section 7.1 answers

### Q1 — Does photo-diagnosis match how teachers actually use "show your working"?

Yes, but only when **sequenced correctly**. In a Grade-11 5U math class the ritual is: student attempts → student self-checks → teacher looks at the working → teacher asks "where do you think you went wrong?" *before* pointing. The teacher almost never circles the error first; that destroys the metacognitive loop we want. So for Cena:

- **Appropriate**: after MC-wrong, after an explicit "let me try to find it myself" step (even a 30-second timer + "which line feels off?" self-diagnosis prompt), THEN the photo-diagnosis affordance appears.
- **Crutch**: photo upload offered immediately on wrong answer with no self-diagnosis interstitial. Within a week, students learn to skip the thinking and outsource error-finding to the vision model. This is the pedagogical equivalent of handing every 5U student a solutions manual.

Grade-12 literature classroom analogue: a teacher reading a student essay never says "line 3 is wrong" — they ask "which claim are you least sure about?" The photo-diagnosis UX needs a claim-your-own-error beat before Cena diagnoses, or it replaces the exact cognitive work Bagrut is measuring. This is not optional.

**Framing recommendation: narrow.** Broad ("ask about anything") turns Cena into a photo-answer-service, loses the pedagogical frame, and blows through the ADR-0050 Q5 cost cap.

### Q2 — Which of A/B/C maps to how teachers run drill sessions?

**B, with a twist.** Real classroom drill sessions: the teacher covers the multiple-choice options with their hand on the projector, makes the class call out the answer, THEN reveals the options. That's literally hide-then-reveal with a per-student toggle for pace — some students need the stem hidden first, some thrive with options-visible mental triage. Per-question author-set (A) is invisible to the student and won't generalize; pedagogy-driven scheduler (C) is the "assigned hard version" that 17-year-olds correctly perceive as punitive and opt out of.

The twist: the **default state should shift by target maturity**. First week with a new Bagrut target → options visible (student needs to learn the shape of the exam). Week 4+ on that target → options hidden by default but one-click reveal. This is not option C in disguise — it's a sensible default students can override at any time, and we tell them why ("you're ready for exam-shaped practice now").

Teacher-default override (class-wide "drill mode on" for a session) is a nice-to-have but ties to PRR-236 classroom UI; do not block B on it.

### Q3 — Literature/History/Tanakh: what's the realistic teacher-graded vs. Cena-graded split? Can we honestly CAS-gate non-math free-form?

**We cannot CAS-gate any non-math free-form answer. Full stop.** CAS is SymPy (ADR-0002). SymPy has no opinion on whether an essay about the Akedah is a strong reading or a weak one. Calling the rubric DSL (PRR-033) "CAS-gated" is a category error we must not make in user-facing copy or internal documentation. An LLM rubric grader is an LLM rubric grader — it is a probabilistic assistant, not a correctness oracle.

Realistic split for humanities:

- **Cena can do reliably**: structural feedback (thesis present? citations present? length on-target? required literary device mentioned?), vocabulary/register flags, Hebrew grammar surface errors, factual-claim-check against a reference corpus (e.g., "you said the Tanakh passage happens in Genesis 12, actually Genesis 22").
- **Cena cannot do reliably without teacher**: holistic essay grade, argument-quality rubric, literary-interpretation-merit, Bagrut-specific scoring bands (the 0–100 the Ministry actually awards). The inter-rater reliability between two human Bagrut literature examiners is not great; an LLM solo-grader is worse and we'd be shipping a slot machine with a rubric skin.

**Defensible design**: rubric DSL produces **structured feedback** (not a grade). The student sees a checklist ("you have a thesis, you have two citations, you are missing a counter-argument paragraph"). A teacher produces the actual grade. If a teacher is not in the loop (self-learner persona), Cena produces a **predicted band with an honest uncertainty range** and a disclaimer, never a single-number grade. Anything else is the score-prediction dark-pattern banned by PRR-019.

For the Arab-stream chemistry class (the third test persona in my head): the honest position is harder still. Arabic-language Bagrut Chemistry rubric content is thinner than Hebrew-language; an LLM rubric grader trained on mostly Hebrew exemplars will systematically under-grade Arabic-language answers. Until we have bench-tested inter-grader agreement by language, any humanities auto-grade in Arabic is off the table.

## Additional findings

- **The photo-diagnosis flow needs a mandatory "self-diagnosis first" beat in the UX**, not just a recommended one. Otherwise it trains learned helplessness. 15-30 seconds and two taps ("which step do you least trust?" → free-text or tap-to-highlight-on-stem) is enough; costs nothing in LLM tokens; preserves cognitive ownership.
- **The brief conflates "free-form input UX" with "free-form grading"**. These are two separable problems. We can ship a chem-reaction input component (student writes `2H₂ + O₂ → 2H₂O`) that renders correctly, is balanced-checkable by a deterministic chem library (not LLM, not SymPy — something like RDKit or a balance-equation parser), and doesn't need LLM grading at all. That's honest chem prep. Conflating them forces us to delay chem input until LLM grading is bench-tested, which is the wrong gate.
- **Hebrew language Bagrut (הבעה/לשון)** is missing from the Q3 discussion entirely. It's the largest Bagrut by student-count and it's 2U mandatory. Its input UX is long-form Hebrew text with register + grammar + structure scoring. Any freeform-input-UX architecture decision that doesn't account for Hebrew Bagrut is scoped too narrow.
- **Chemistry lab-component (30% of the 5U grade)** is never going to be Cena-gradable — it's physical lab work. Cena prep for chem must acknowledge this in the catalog metadata so students and teachers see the 70%-of-grade honest ceiling.
- **Retake candidates** (per my last review) often practice on old moed papers. The photo-diagnosis use case is especially high for retake students working through *paper* copies of past exams — they have pencil-and-paper annotations that Cena needs to read. Good use case; strengthens Q1 narrow framing.
- **Generation-effect evidence** for Bagrut-age students is thinner than the brief suggests. Slamecka & Graf (1978) was college-age word-pair recall. The closer literature for 16-18yo exam prep is Karpicke & Roediger retrieval-practice work; effect size is there but noisy. Don't sell Q2 to stakeholders as "research says 30% improvement"; sell it as "it matches how good teachers run drill, zero dark-pattern surface, low build cost."

## Section 8 positions

1. **Q1 framing — narrow.** Broad opens moderation, cost, and prompt-injection-via-OCR surface for no pedagogical gain.
2. **Q2 implementation — B (student toggle)**, with a sensible default that raises with target maturity. Do not ship C; it's paternalistic and students will revolt.
3. **Q3 architecture — shared `FreeformInputField<T>` abstraction** with per-subject adapters. Chem gets a reaction-balance adapter (deterministic, not LLM). Hebrew/Arabic long-form gets a text-with-structure adapter. Humanities essay gets a rubric-feedback-only adapter (no grade). DRY wins because the moderation/PII/retention plumbing is identical across adapters.
4. **Q3 chem Launch-scope — chem slips unless reaction-input + balance-checker ship.** MC-only Bagrut Chemistry is not Bagrut Chemistry prep. Shipping it with the catalog entry saying "Bagrut Chemistry 5U" is the labels-don't-match-data bug the user explicitly flagged. If engineering can't land the chem-reaction input + deterministic balance-checker by Launch, mark chem `itemBankStatus: reference-only` in the catalog (per the PRR-221 pattern from multi-target review) and be honest that Cena tracks the date but doesn't practice Bagrut Chemistry yet. Do NOT launch with MC-only and call it chem prep.
5. **Q3 humanities Launch-scope — same answer.** Literature/History/Tanakh with MC-only is not those exams. Ship them as `reference-only` catalog entries (tracks the date, acknowledges the syllabus, declines to schedule sessions) until long-form input + rubric-feedback UX exists, with teacher-in-the-loop grading for the graded portion. Self-learner persona gets structured feedback and a disclaimer, never a single-number grade.
6. **Cost cap**: narrow Q1 + B for Q2 + shared Q3 abstraction stays inside $3.30/student/month if we cache aggressively and rate-limit photo uploads to ~5/session. Broad framing breaks the cap; refuse it.

## Recommended new PRR tasks

| ID-placeholder | Title | Why | Priority | Effort |
|---|---|---|---|---|
| PRR-244 | Photo-diagnosis "self-diagnosis first" UX beat (mandatory interstitial) | Preserves metacognitive ownership; prevents crutch-pattern; minimal build | P0 | S |
| PRR-245 | Hide-then-reveal (Option B) per-session student toggle + maturity-based default | Matches teacher drill workflow; zero content cost; zero dark-pattern surface | P0 | M |
| PRR-246 | Chem reaction-input + deterministic balance-checker (RDKit or equivalent) | Honest Bagrut Chem prep; decouples chem-input from LLM-grading gate | P0 | L |
| PRR-247 | Shared `FreeformInputField<T>` + per-subject adapter architecture | One moderation/PII/retention pipeline; per-discipline rendering | P1 | M |
| PRR-248 | Humanities long-form input + structured rubric-feedback (no numeric grade) UX | Honest literature/history/Tanakh prep; rubric DSL produces feedback not grade | P1 | L |
| PRR-249 | Catalog honesty: chem + humanities `itemBankStatus: reference-only` if input UX slips | Do not ship MC-only chem/humanities calling it Bagrut prep | P0 | S |
| PRR-250 | Photo-diagnosis narrow-framing gate (only from wrong-answer affordance) | Scope guard; ties to ADR-0050 cost cap | P0 | S |
| PRR-251 | Rubric-DSL user-facing copy audit: remove "CAS-gated" / "verified correct" claims from any non-math grader | ADR-0002 is SymPy-only; mis-labeling LLM rubric as CAS is a non-negotiable violation | P0 | S |
| PRR-252 | Hebrew Bagrut (הבעה/לשון) long-form input UX scoping | Largest Bagrut; missing from brief entirely | P1 | M |
| PRR-253 | Arabic-language rubric-grader inter-rater reliability bench before any auto-grade ships | Prevents systematic under-grading of Arab-stream answers | P0 | M |

## Blockers / non-negotiables

- **Blocker 1**: Launching Bagrut Chemistry with MC-only while the catalog says "Bagrut Chemistry 5U" is a labels-don't-match-data failure and should not ship. Either ship chem reaction-input, or mark chem `reference-only`. No third option.
- **Blocker 2**: Any user-facing or internal copy that describes the LLM rubric grader (PRR-033) as "CAS-gated", "verified", "correct", or any cognate is a lie about what the system actually does. ADR-0002 scopes SymPy as the sole correctness oracle. Audit and remove before Launch.
- **Blocker 3**: Photo-diagnosis without a mandatory self-diagnosis beat is a crutch-pattern and degrades the exact skill Bagrut measures. The beat is cheap; the absence is not.
- **Non-negotiable**: Humanities single-number auto-grade from LLM rubric is a score-prediction dark-pattern (PRR-019 bans it). Structured feedback only; teacher-in-the-loop for grades; predicted-band-with-uncertainty for self-learners.

## Questions back to decision-holder

1. **Chem Launch scope**: are you willing to slip chem to `reference-only` if reaction-input doesn't land in time, or is there executive pressure to ship MC-only-but-called-Bagrut? Because the latter is the bug pattern we've explicitly banned.
2. **Humanities same question**: reference-only slip vs. degraded launch — confirm the call.
3. **Teacher-in-the-loop grading for humanities**: is this in Launch scope via EPIC-PRR-C classroom UI, or post-Launch? If post-Launch, what's the self-learner humanities story — structured feedback only?
4. **Default for hide-then-reveal**: do you want the maturity-raising default, or strict opt-in (default visible, student chooses to hide)? Strict opt-in is safer but gets lower adoption.
5. **Photo-upload rate limit**: 5/session? 3/day? Need a number to hold the $3.30 cap.
