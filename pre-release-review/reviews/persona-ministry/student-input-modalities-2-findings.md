---
persona: ministry
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: amber
supersedes_scope: sections 4 + 5 of 001-brief
---

## Summary

The 002-brief moves the right direction on two of the three fidelity fronts I flagged red in the 001-brief (PET and chem). Writing-pad-primary for PET quant resolves the MathLive-trains-wrong-habit concern I raised, provided MathLive is demoted to secondary and not default-on. Typed-primary with a Lewis-pad fallback matches Bagrut Chem שאלון conventions well enough to be defensible. The humanities essay question (4.4) is correctly pushed to typed-only and is fine. The load-bearing new gap is that **none** of the subjects in section 4 name a Ministry notation-linter or שאלון-version-pinned rubric — the modality question is answered, but the fidelity-policy question stays open. Amber, not red, because the architectural decisions no longer actively train wrong habits; fidelity gates are annotations, not redesign. Section 6.6 below codifies each subject's Ministry expectation; blockers listed at end.

## Section 6.6 — Bagrut / NITE fidelity by modality

### PET quant (4.1) — writing-pad primary resolves my 001 concern, with caveats

NITE's מבחן פסיכומטרי quantitative section is no-calculator, paper-scratch, MC-only, ~1.2-min/item. Writing-pad-primary with MC commit matches the paper-scratch habit. **Confirmed: resolves the MathLive-default concern from 001.** Caveats:

1. **MathLive must be OFF by default for PET-target sessions**, not merely "secondary". Section 4.1 says "primary = writing pad, secondary = MathLive typed". If the UI still shows a "switch to MathLive" button, power-users will use it, and we re-create the wrong-habit problem. PET session renderer (PRR-252 from 001) should hide MathLive entirely and only surface it if the student explicitly turns on an accessibility flag.
2. **HWR-graded answer is wrong for PET.** PET is MC — the student picks A/B/C/D. The writing pad is **scratch**, not a graded artefact. HWR back to CAS for grading a PET item is both unnecessary and a fidelity regression (adds a step the paper exam doesn't have). Use the pad as un-graded capture; the graded action is still MC commit.
3. **Time pressure is exam-authentic but is a shipgate banned-terms risk.** Paper PET is ~20 min / 20 items. Presenting that pacing truthfully is fidelity, not a time-pressure mechanic, but the UI copy must read "pace coach: ~1.2 min/item" not "⏰ hurry up". ADR-0048 framing applies.

### Bagrut Chem typed notation (4.3) — mostly fits, palette required

Real שאלון 037xxx papers accept — and grade partials on — this family of notations:

- Equations: coefficients as integers left of formula, subscripts for atom counts, arrows `→` (forward) / `⇌` (equilibrium) / `⇄` sometimes accepted. `=` is **not** accepted as reaction arrow.
- State symbols: `(s)`, `(l)`, `(g)`, `(aq)` — parentheses required, lowercase, English Latin letters (the convention is Latin even on Hebrew שאלון; students do not write `(נ)` / `(ג)` / `(מ)`).
- Ions: charge as right-superscript with number then sign, e.g. `Cu^{2+}`, `SO4^{2-}`. Sign-first `+2` is a partial-credit deduction.
- Equilibrium/enthalpy: `ΔH` written explicitly; sign + units expected (`kJ·mol^{-1}` or `kJ/mol`).
- Organic: bond-line or condensed formula both accepted; Lewis structures are accepted when the question asks for electron-pair reasoning.

`2H2 + O2 → 2H2O (l)` as given in the brief **is grader-acceptable** with one nit: the convention is `2H₂ + O₂ → 2H₂O (l)` with subscripts, and the space before `(l)` is idiomatic Hebrew-stream but Arabic-stream papers sometimes omit it. Our input component must (a) render subscripts automatically from `H2` → `H₂`, (b) offer a state-symbol palette so the student does not type `(L)` / `(aq.)` and lose partials, (c) reject `=` as arrow and suggest `→`. Without a **notation linter that mirrors the Ministry מחוון partial-credit rubric**, a CAS balance check alone is not Bagrut-faithful — a balanced equation with `(L)` still loses marks on paper. PRR-250 must own that linter explicitly.

### Lewis structures hand-drawn (5) — yes, paper Bagrut accepts

Yes. שאלון 037xxx questions that ask for מבנה לואיס expect a hand-drawn structure on paper with explicit lone pairs as dot-pairs, bonds as lines (single/double/triple), and formal charge annotations where relevant. Graders do not expect SMILES or InChI — those are outside the מחוון. **Writing-pad + MolScribe-class HWR is the right surface for Lewis structures.** Keyboard-only Lewis entry is not exam-authentic (typing `[O-]=[N+]=O` trains cheminformatics, not exam notation). Caveat: chem-HWR accuracy on student-drawn Lewis ranges 70–85%; any confidence below ~92% must surface a "we couldn't parse your drawing — please retype or retake" fallback rather than silently grading. A false-reject on a correctly-drawn Lewis is a fidelity disaster (student learns the right notation, tool marks it wrong).

### Bagrut Lit / History / Civics typed essay (4.4) — keyboard-only is right, rubric is not

Keyboard-only matches exam reality: Bagrut essays are written by hand on paper, but Ministry graders care about **structure + citation + register**, not handwriting quality. Typed capture loses nothing graders care about. Ministry-expected structure by שאלון family:

- **Bagrut Literature 008xxx / 009xxx**: מסה מובנית — thesis + 2–3 body paragraphs with cited passages + counter-position + synthesis. 250–500 words typical prompt; 600–800 for the reflective essay. Discourse markers (`כפי שניתן לראות`, `לעומת זאת`, `מכאן ש-`) expected. Citations as passage reference (`שורות 14–17`) or quoted text in quotation marks.
- **Bagrut History 022xxx**: source-based constructed response. Required: historical-period anchor, at least one primary-source citation from the provided document, cause-effect chain. 300–500 words. Graders weight textual fidelity to the source over prose quality.
- **Bagrut Civics 034xxx**: demokratia-principle-anchored argument. Required: named principle (`שלטון העם`, `זכויות אדם`), case/precedent reference (often an Israeli Supreme Court ruling named in the corpus), counter-position. 250–400 words.

**Can LLM rubric + typed input defensibly prep students for the paper essay?** Partially, as I said in 001. Deterministic axes (word count band, required-discourse-marker presence, citation-field populated) the rubric DSL handles cleanly and is defensible. Argument-quality + textual-fidelity + register are LLM-judged and **must be labelled as such** to the student — "AI feedback, not a Ministry grade" — with teacher-override. Additional 002-specific caveat I missed in 001: **the rubric must version-pin per מיקוד year** (e.g. `מיקוד תשפ"ו`). Ministry changes required topics yearly; an essay prompt against last year's מיקוד trains outdated content. PRR-033 annotation covers this.

## Section 7 positions

1. **Q2 default state** — Ministry-neutral (hide-reveal is not a Bagrut rule). Prefer **visible-first with opt-in hide** (3.1.B) — lower friction, and Bagrut papers present options visibly, so exam-fidelity says visible-first.
2. **Q2 server-side enforcement** — Ministry-neutral; classroom-enforced mode (3.3.C) conflicts with nothing in the מחוון.
3. **Q2 commit-and-compare** — skip for Launch. Not a Bagrut flow.
4. **Q3 math modality** — **writing-pad primary for Bagrut Math + PET, MathLive secondary and OFF-by-default for PET targets**. SAT math keyboard-primary is fine; SAT is not Ministry scope.
5. **Q3 chem modality** — **typed-primary for equations + stoichiometry with Ministry notation linter (palette + subscript auto-render + state-symbol enforcement), writing-pad secondary for Lewis structures with confidence-gated parse**. Agree with 4.3 direction, add the linter.
6. **Q3 language modality** — **confirmed keyboard-only**. No Ministry objection.
7. **Q3 HWR procurement** — Ministry-neutral on vendor, but any chosen path must log the engine + model version into the chain-of-custody ledger (PRR-253) so a "HWR misread my answer" dispute can be reproduced.

## Recommended PRR tasks

- **PRR-255 (P0)**: Ministry-notation linter for Bagrut Chem input — state-symbol palette, subscript auto-render, arrow-normalization, per-שאלון מחוון version pin. Extends PRR-250.
- **PRR-256 (P0)**: PET-target session renderer hardening — MathLive hidden (not just non-default), writing-pad is scratch-only not graded, MC commit is the graded action, time-awareness copy complies with ADR-0048. Extends PRR-252.
- **PRR-257 (P1)**: Chem-HWR confidence gate — minimum parse-confidence threshold (~92%), false-reject → retake/retype fallback, never silently mis-grade a correct drawing.
- **PRR-258 (P1)**: Essay rubric מיקוד-version pin — per-axis labelling (deterministic vs LLM-judged), מיקוד year on session, rubric-DSL version pin. Extends PRR-033 + PRR-251 + PRR-254.

## Blockers

- **BLOCKER (red)**: Bagrut Chem typed input without the Ministry-notation linter (PRR-255). CAS-balanced is not Bagrut-faithful if `(L)` loses partials in the grader's מחוון.
- **BLOCKER (red)**: PET target sessions where MathLive is reachable by default. Hide it or we re-introduce the wrong-habit regression.
- **BLOCKER (red)**: Lewis-structure HWR without a confidence-gated fallback (PRR-257). False-reject on correct drawing = fidelity disaster.
- **Non-negotiable**: Essay rubric must name the מיקוד year on every session (PRR-258). Prepping against stale מיקוד misrepresents Bagrut prep.
- **Non-negotiable**: Student-facing items remain AI-authored recreations per Bagrut-reference-only memory; no שאלון text leaks into the MC/essay prompts.

## Questions back to decision-holder

1. Who curates the per-שאלון notation מחוון for the chem linter (PRR-255)? Same owner-gap as PRR-218 / responseFormatPolicy.
2. PET writing-pad: stroke-data retention — treat as Q1 photo (24h TTL + 30d derived) or discard entirely since it is never graded?
3. Chem-HWR confidence threshold (PRR-257) — calibrate on a real Bagrut-aligned corpus before Launch, or ship conservative (95%+) and tune later?
