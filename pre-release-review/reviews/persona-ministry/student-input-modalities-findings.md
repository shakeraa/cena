---
persona: ministry
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red
---

## Summary

From the Ministry-compliance / exam-fidelity lens, this brief has a load-bearing blind spot: input modality is exam-format policy, not a UX choice. Bagrut Chemistry, Bagrut Literature, and PET quantitative each have Ministry-specified (or NITE-specified) response conventions that students are graded against on the real exam. If Cena trains a different habit — MC-only for chem, LLM-rubric for literature essays, MathLive-typed for PET — we are optimizing for the wrong artefact and the student pays on exam day. That is a fidelity regression, not a feature gap. Two of the section-8 product questions (#4 chem Launch-scope, #5 humanities Launch-scope) are therefore not "ship-vs-slip" trade-offs; they are "ship-and-mislead" vs "slip". Q1 photo-OCR has a real but fixable chain-of-custody gap. Red until section 7.6 gets its own policy; easily amber once the conventions below are codified.

## Section 7.6 — Bagrut-format fidelity across modalities

### Bagrut Chemistry notation — Ministry conventions and the MC-only trap

The Ministry שאלון for Chemistry 5 יח"ל (`037xxx` family) expects students to **write** balanced chemical equations with state symbols in parentheses (`(s)`, `(l)`, `(g)`, `(aq)`), stoichiometric coefficients as integers on the left of the formula, subscripts for atom counts, charge as right-superscript on ions, arrows `→` / `⇌` for forward / equilibrium, and to show **per-mol** stoichiometric work with units carried through every line. Lewis structures, oxidation-number annotations above atoms, and electron-pushing arrows for organic mechanisms are also expected notation. Answers are graded partially — a correct formula with wrong state symbols, or an unbalanced equation with correct products, earns different partial credit than MC binary right/wrong.

If Cena launches Bagrut Chemistry with MC-only (section 8 position #4), we are training students to **recognize** balanced equations without producing them — the generation-effect regression is just the pedagogy half; the exam-fidelity half is that partial-credit strategy (which atoms to balance first, which state symbol to assign, how to show working so the grader can give partials) is a trained skill the student won't have. This is not hypothetical: NITE-PET-style test-wiseness research shows students who only practiced MC perform measurably worse on constructed-response variants of the same content. MC-only chem at launch = shipping a product that claims Bagrut prep and demonstrably under-prepares.

### Bagrut Literature essay format — can the LLM rubric defensibly grade?

Bagrut Literature (שאלון `008xxx` Hebrew stream, `009xxx` Arabic stream) expects a **structured essay** (מסה מובנית): thesis paragraph with claim + text reference, 2–3 body paragraphs each anchored to a cited passage with specific discourse markers (`כפי שניתן לראות`, `לעומת זאת`, `מכאן ש-`), counter-position paragraph, synthesis/conclusion. Length expectations are שאלון-specific (typically 250–500 words per prompt; the longer reflective essay is ~700). Graders use the Ministry's מחוון (rubric) with weighted axes: textual fidelity, argument structure, linguistic register, discourse cohesion, originality.

Can PRR-033 rubric DSL + LLM grader defensibly grade these? **Partially, and not defensibly enough to claim Bagrut-grade feedback at launch.** LLM-rubric graders on Hebrew literary essays have documented failure modes: over-reward of length, under-reward of structural originality, inconsistency across rhetorical registers, and RTL + מקראי (biblical Hebrew) quotation handling that trips tokenizers. The rubric DSL can score **some** axes (length, presence of required discourse markers, quotation-citation presence) deterministically — that subset is defensible. The argumentative-quality axis is LLM-judgement and must be labelled as such to the student, with an appeals path (persona-ethics 7.3 flag stands). Launching humanities as MC-only (#5) dodges the problem but fails even harder on fidelity — Bagrut Literature is **constructed-response by definition**; the MC version is not the same exam.

### PET quantitative — paper + MC vs MathLive

NITE-PET quantitative is explicitly **no-calculator, paper scratch, MC-only** with a ~1.2-minute-per-item pace. Test-takers who train with MathLive typed input develop the habit of typing-then-simplifying rather than the scratch-then-eliminate habit that the timing demands. The modality mismatch is real and measurable; PET preparation literature from NITE and commercial prep vendors explicitly warns against over-use of symbolic input tools for PET drill. Cena's Q3 free-form math is correct for Bagrut Math (which **does** allow showing work) but wrong as the default for PET. Recommendation: PET sessions should default to **MC-only with an optional scratch surface** (tablet stylus or paper-photo — see Q1) that is not graded and not OCR'd back into the canonical attempt, just captured for the student's post-session review. Training the PET habit requires MC-first, time-pressured, paper-adjacent.

## Q1 photo-OCR chain-of-custody

If a student's photo-OCR diagnosis is later questioned — by the student, a parent, a school, or a regulator — what can Cena produce? Today, per ADR-0003 and the brief's stated session-scope, the answer is "the photo and OCR output are purged at 30 days". That is correct for privacy but **wrong for audit**. A durable, hashed, non-PII audit record must survive: `{photoSha256, ocrEngineVersion, visionModelVersion, promptTemplateVersion, outputHash, sessionId, studentOpaqueId, timestampUtc, moderationVerdict}`. Keep **no image and no OCR text** beyond 30 days; keep the chain-of-custody row for 7 years (standard education-record retention). Without this, "the model said my answer was wrong" becomes unfalsifiable after 30 days — both directions. Add to PRR-003a / PRR-223 RTBF cascade as an explicit carve-out: hashes and version pins survive erasure (they contain no PII); the artefacts do not.

## Section 8 positions

1. **Q1 framing (narrow vs broad)** — Ministry-neutral on pedagogy; prefer **narrow** for chain-of-custody tractability. Broad framing multiplies the audit surface without a fidelity gain.
2. **Q2 hide-then-reveal** — Ministry-neutral; no Bagrut/PET rule depends on it.
3. **Q3 architecture** — per-subject components. Chemistry notation and literary essay structure are different grammars; a shared `FreeformInputField<T>` will collapse to lowest-common-denominator and lose fidelity.
4. **Chem Launch-scope — REJECT MC-only.** Either ship chem with constructed-response equation input + balancing + state-symbol annotation, or slip Bagrut Chemistry out of Launch. MC-only chem misrepresents the exam we claim to prep.
5. **Humanities Launch-scope — REJECT MC-only; allow essay-input with labelled LLM-grade.** Ship the free-form essay field with PRR-033 rubric DSL covering deterministic axes + a **clearly-labelled "AI-assisted feedback, not a grade"** disclaimer on LLM-judged axes, teacher-override hook, and an appeals path. If that can't ship, slip humanities — do not ship MC-humanities and call it Bagrut prep.
6. **LLM-cost cap** — finops owns; Ministry-neutral except to note that compliance costs (chain-of-custody storage, per-שאלון rubric version pinning) are not optional.

## Recommended PRR tasks

- **PRR-250 (P0, pre-launch)**: Bagrut Chemistry constructed-response input component — balanced-equation entry, state-symbol palette, stoichiometry table, per-mol calculator, Ministry-notation lint. Blocks chem at Launch.
- **PRR-251 (P0, pre-launch)**: Bagrut Literature / Humanities essay-input component — length tracker per-שאלון, required-discourse-marker hint, citation-field, RTL-aware. Pairs with PRR-033.
- **PRR-252 (P1, pre-launch)**: PET-mode session renderer — MC-only, time-pressured, optional un-graded scratch capture, MathLive disabled. Per-target (ADR-0050) toggle keyed on `PET`.
- **PRR-253 (P1, pre-launch)**: Photo-OCR chain-of-custody ledger — append-only hash table, 7-year retention, RTBF-carve-out documented. Extends PRR-003a.
- **PRR-254 (P2)**: LLM-rubric defensibility disclosure — per-axis labelling (deterministic vs LLM-judged), appeals path, teacher-override hook. Extends PRR-033.
- **Annotate PRR-033**: rubric DSL must version-pin per שאלון code + מיקוד revision (carry forward from multi-target-exam-plan finding).
- **Annotate ADR-0050**: exam-target carries `responseFormatPolicy: 'constructed-response' | 'mc-only' | 'mixed'` derived from the שאלון; session runner honors it.

## Blockers / non-negotiables

- **BLOCKER (red)**: MC-only Bagrut Chemistry at Launch. Either build constructed-response input (PRR-250) or slip chem. Shipping MC-chem as "Bagrut prep" is a fidelity misrepresentation.
- **BLOCKER (red)**: MC-only Bagrut Humanities at Launch. Either ship essay-input with labelled-LLM-grade + appeals (PRR-251 + PRR-254) or slip humanities.
- **BLOCKER (red)**: PET sessions defaulting to MathLive. Train the paper+MC habit (PRR-252) or we're optimizing against exam day.
- **Non-negotiable**: Q1 photo chain-of-custody ledger (PRR-253). No image retained past 30 days; hashes + version pins retained 7 years. Carve-out explicit in RTBF.
- **Non-negotiable**: No student-facing item is a real Bagrut / PET question (Bagrut-reference-only memory). Restate in any ADR arising from this brief.

## Questions back to decision-holder

1. Chem and humanities — hard-slip out of Launch if PRR-250/251 don't land, or carry a feature-flag that hides those targets from exam-plan selection until the input UX ships? (Prefer hide-from-selection over ship-degraded.)
2. PET scratch-capture: photo-of-paper only, or also a tablet-stylus canvas? Tablet canvas is more authentic for some test centres; photo is simpler infra.
3. Who owns per-שאלון responseFormatPolicy curation — content lead or bagrut-fidelity sub-agent? Same owner-gap as PRR-218 in the multi-target review.
