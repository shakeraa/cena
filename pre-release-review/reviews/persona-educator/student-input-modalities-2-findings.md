---
persona: educator
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: yellow
---

## Summary

The 002-brief is sharper than the 001: it correctly separates the Q2 default-state design from the Q3 modality selection and names the exam-fidelity hazard honestly. My 001 RED verdict was about chem MC-only shipping with a "Bagrut Chemistry" label — that's not at issue here, so this review downgrades to yellow. Yellow, not green, because four items still need hardening before an ADR: (1) the default-state choice interacts with PRR-228 in a way the brief underplays; (2) scaffolding-under-hidden-options is described as a UX question but is actually a pedagogy-correctness question; (3) the math-modality tradeoff is posed as symmetric and it is not — one side trains the wrong exam habit, the other marks correct answers wrong; (4) the $3.30 cap math (5.2/student/month at 400 answers) is load-bearing and deserves an explicit cap-or-degrade decision before any HWR vendor is picked. The brief's direction — writing-pad primary for math/physics, typed for chem-reactions + Lewis-pad secondary, keyboard for language — is correct. Ship-gate is sequencing, not direction.

## Section 6.1 answers

**3.1 default state — classroom workflow.** Teacher-run drill sessions are **hidden-first with a visible-default for the first encounter**. The projector-covered-options ritual only works after students have seen the shape of the question type. Map to Cena: Option A (hidden-by-default) when session is tagged "drill"/"retrieval-practice"; Option B (visible-by-default, opt-in hide) for first-encounter items and for the diagnostic-onboarding block. NOT option C (N-second auto-hide) — that's a timer-ish pattern that Ship-gate reviewers will rightly flag under ADR-0048, and it trains "stare harder" not "retrieve harder." The toggle copy must say "I'll try without seeing the options first," never "hard mode" (loss-aversion framing).

**3.4 scaffolding interaction.** This is not just UX. A hint delivered while options are hidden either (a) silently reveals options to "anchor the hint" — destroys the generation effect the toggle exists to create, or (b) hints against a stem-only state — which is what a good teacher actually does ("what's the first quantity you'd compute?"). Ship (b): scaffolding must be stem-grounded, not option-grounded, when hidden-options is on. Options stay hidden until student commits. This is how 5U math tutors actually scaffold; it's also cheaper because the scaffolding prompt doesn't need to know the distractors.

**4.x confirm/challenge.**
- Math (4.1): **confirm writing-pad primary for Bagrut Math + PET Quant, MathLive secondary.** Challenge the SAT carve-out — SAT Math is paper-bubbled but student work is on paper scratch; SAT-primary keyboard assumes typed-math habit among Israeli SAT-takers which I doubt. Keep writing-pad primary across all three; MathLive as fast-path for confident students.
- Physics (4.2): **confirm.** Writing-pad + MathLive matches how 5U physics is actually worked.
- Chem (4.3): **confirm typed-primary for reactions, pad for Lewis.** Add: typed notation must accept Hebrew-keyboard-friendly variants (some teachers write states as `(ג)` not `(g)`) or the tool trains a notation that won't match teacher marking.
- Language (4.4): **confirm keyboard-only.** Handwriting long-form Hebrew/Arabic with HWR is a non-starter at current accuracy; Berninger's handwriting-advantage studies do not overcome 5-10% character-error rates on HWR outputs.

**Hardest tradeoff — MathLive-wrong-habit vs HWR-marks-correct-wrong.** Pick **writing-pad with HWR**, accept the HWR error rate, mitigate with a "confirm what we read" step that surfaces the HWR-parsed expression to the student for one-tap correction before CAS grades it. Rationale: a wrong-habit failure mode compounds over six months of daily practice and blows up on exam day; an HWR error mode is visible-per-item and the student can correct it in two seconds. The latter is recoverable, the former is not. Also: the confirm-parse step itself is pedagogically useful (students learn to read their own notation). Cost of the confirm step is negligible, closes most of the "correct-answer-marked-wrong" hazard, and preserves exam fidelity. Non-negotiable: if HWR confidence is below a threshold (~0.85), force the confirm step; above, allow quiet-parse with a "tap to fix" affordance.

**3.7 commit-and-compare flow — teacher ask or over-design?** Teachers do ask for this, but not in the form the brief proposes. The classroom version is "write your answer on your paper, now look at the choices" — no typing, no equivalence check, no auto-select. The Cena analogue is: hidden-options → student commits "I'm ready" → options appear → student picks. The typed-answer-with-CAS-equivalence variant is over-designed for Launch: it adds input infrastructure to MC items for a marginal generation-effect gain and doubles the grading surface. Ship the simple commit; defer the typed-commit-with-equivalence to post-Launch behind an A/B.

## Additional findings

- **PRR-228 diagnostic interaction (3.5).** Hide-reveal MUST be off during the 6-8 item diagnostic block. The diagnostic's job is to calibrate recognition-level proficiency against the target; adding a retrieval-effort variable contaminates the signal and drops completion. Teachers don't do surprise-pop-quiz on day one with a new student — they watch how the student reads the problem first. Match that.
- **Per-question override (3.6).** Author-level "force-visible" flag is needed for items where the options ARE the question (graph-selection, correct-diagram items). Without it the toggle silently breaks those items.
- **Maturity-raising default from 001-brief** still applies: first week with a target = visible-default; week 4+ = hidden-default. This is the honest form of "assigned hard mode" without the paternalism of Option C.
- **Hebrew Bagrut (הבעה/לשון)** — still missing from the brief. Confirming the 001-finding: this is the largest Bagrut by headcount, typed-only, needs its own scoping task.

## Section 7 positions

1. **Q2 default state (3.1)** — hybrid: visible-first for new-target + diagnostic, hidden-first for drill/retrieval sessions after week 4. Not pure A, not pure B, not C.
2. **Q2 server-side enforcement (3.3)** — optional per-student; classroom-enforced via PRR-236 is a post-Launch tenant setting. Never-enforce is too weak (redteam will flag); optional now, classroom later.
3. **Q2 commit-and-compare (3.7)** — ship the simple "I'm ready" commit; defer typed-pre-commit-with-equivalence.
4. **Q3 math modality (4.1)** — writing-pad primary + MathLive secondary + mandatory-when-low-confidence parse-confirm step. Applies to Bagrut Math and PET Quant and SAT Math.
5. **Q3 chem modality (4.3)** — typed-primary for reactions/stoichiometry, Lewis-pad secondary, accept `(ג)/(g)` notation variants.
6. **Q3 language modality (4.4)** — keyboard-only confirmed.

## Recommended new PRR tasks

| ID-placeholder | Title | Why | Priority | Effort |
|---|---|---|---|---|
| PRR-254 | HWR parse-confirm step with confidence threshold (force-confirm <0.85) | Closes "correct-answer-marked-wrong" hazard; pedagogically useful | P0 | M |
| PRR-255 | Hide-then-reveal default-state matrix (new-target=visible, drill/week4+=hidden, diagnostic=off) | Default-state is load-bearing; must not be a single global flag | P0 | S |
| PRR-256 | Stem-grounded scaffolding hints when options are hidden | Preserves generation effect; cheaper prompts | P0 | M |
| PRR-257 | Author-flag `forceOptionsVisible` for items where options ARE the question | Prevents silent-break of graph/diagram-selection items | P0 | S |
| PRR-258 | Chem typed-notation variant acceptance (`(ג)/(g)` state symbols) | Aligns with teacher marking conventions | P1 | S |
| PRR-259 | HWR daily-call cap per student with degrade-to-MathLive fallback | Holds $3.30 ceiling without hard-blocking practice | P0 | M |

## Blockers / non-negotiables

- **Blocker**: shipping math with MathLive-primary (not secondary) trains the wrong habit for paper Bagrut + PET. Writing-pad must be the default entry modality.
- **Blocker**: scaffolding that reveals options under hidden-mode is a pedagogy bug, not a UX bug. Hints stay stem-grounded.
- **Non-negotiable**: hide-then-reveal is OFF during PRR-228 diagnostic. Non-negotiable because the brief leaves it ambiguous.
- **Non-negotiable**: no typed LLM-rubric-graded humanities single-number grade (from 001-brief, still stands).
- **Cap discipline**: if HWR path is chosen without a per-student daily cap + degrade-to-typed fallback, the $3.30 ceiling breaks. Pick the cap.

## Questions back to decision-holder

1. Default-state matrix — approve the hybrid (new-target=visible, drill=hidden, diagnostic=off), or pick a single global default for simplicity?
2. HWR procurement — Claude Sonnet vision (reuses MSP) is the cheapest integration but has highest unit cost; MyScript is cheapest per-call but adds a vendor. Preference?
3. Daily HWR cap number — 20/student/day holds the cap; is that acceptable UX or do we need higher and tier-bump?
4. SAT Math carve-out — keep writing-pad primary (my recommendation) or accept the brief's keyboard-primary?
5. Commit-and-compare — confirm ship the simple commit and defer typed-pre-commit-with-equivalence to post-Launch A/B?
