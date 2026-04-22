---
persona: ethics
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: yellow
supersedes-scope: sections 4+5 of 001
---

## Summary

002-brief tightens 001 in the right places (option B confirmed, MSP reused) but opens four new surfaces. Two are ethically clean if implemented as written (commit-and-compare, HWR reuse of MSP). One is a genuine dark-pattern risk (classroom-enforced server-side redaction without student notice). One is a latent a11y-ethics injustice (writing-pad-primary math puts the entire MathLive-syntax learning burden on motor-impaired keyboard-only users). The HWR-via-LLM-vision path is defensible but it *extends* the 001 consent modal's scope in a way the 001 copy does not cover — either re-scope the modal or ship a second consent surface. Verdict stays yellow; the dark-pattern risk and the motor-impaired burden are ship-gates, not nice-to-haves.

## Section 6.3 answers

**3.3 — Classroom-enforced server-side redaction without student consent: dark pattern?**

Not automatically. It is legitimate pedagogy *iff* three conditions hold: (a) the student is told the options are hidden by classroom policy and by whom — the question surface shows a one-line banner like "Your teacher hid the options for this session"; (b) the student has a non-penalising "skip to visible" affordance that routes to a teacher-override request, same access-control shape as `ExamTargetOverrideApplied`; (c) the redaction is scoped to enrolled-class sessions only and never leaks into self-study or diagnostic onboarding (PRR-228).

Without those three, it is a dark pattern — specifically the "opaque + non-reciprocal" GD-004 failure mode I flagged against 001's option C. The ethics test isn't "is the student consulted?" (teachers legitimately override students on pedagogy calls). It is "does the student know the rule is in effect and have a path to contest it?" If yes, pedagogy. If no, coercion-by-default.

Ship as option "classroom-enforced with visible banner + override-request path". Silent server redaction is banned.

**3.7 — Commit-and-compare: coercive or honest?**

Honest, with one constraint. Commit-and-compare is a closer operationalisation of the generation effect than naked hide-reveal and the cognitive-literature grounding is strong (Bjork/Bertsch). It is only coercive if the student cannot back out of the commit — e.g. if typing a guess before reveal locks them into that guess even when they see the options and realise they misread the stem. Require an "undo my guess" affordance that works until feedback is rendered. With that undo, ship. Without it, the flow weaponises sunk-cost bias and that is GD-004 territory.

Additional constraint: the typed pre-guess must not be surfaced to the teacher as "student's first instinct" without the student knowing. Session-scoped like misconception data (ADR-0003). Pre-guess is the student's private rehearsal, not a behavioural data point for the educator dashboard. If tenant wants pre-guess telemetry for research, that is a separate consent surface, not a default.

**4.6 — HWR-via-LLM-vision: consent scope extension?**

Yes, and the 001 modal copy does not cover it. The 001 copy says "we send your photo" — handwriting strokes on a canvas are not "a photo" in the student's mental model, even if the backend rasterises to an image before the Sonnet vision call. Two clean paths:

1. **Rewrite the 001 modal copy** to "...we send your photo or your handwritten working to an AI vision model..." so a single consent surface covers both. Preferred — one consent event, covers both MSP inflows.
2. **Add a second modal on first stroke-submit** that explicitly names handwriting. Acceptable but user-hostile (two modals for what is effectively the same trust grant).

Path 1 is correct. Task: add `PRR-ethics-INP2b` — modal copy revision + he/ar re-translation. Also: the "teacher can see that you uploaded a photo" line needs a handwriting variant ("teacher can see that you wrote on the pad, but not what you wrote"). Privacy-equivalent teacher-visibility contract.

Note on biometric-adjacency: stroke dynamics ARE uniquely identifying (PPL Amendment 13 sensitivity gradient, persona-privacy will own the detail). The consent modal must *not* claim stroke data is less-sensitive-than-photo — it isn't. If anything it is more stable across sessions than a face in a phone photo.

**Writing-pad forced choice — motor-impaired student gets MathLive syntax burden**

This is the cleanest a11y-ethics injustice in the brief and it is underweighted. If math modality ships writing-pad-primary per section 4.1, every student whose hands cannot hold a stylus (motor disability, tremor, single-arm user, severe RSI, some dyspraxia) is routed to MathLive — which 001-brief persona-a11y already flagged as having known screen-reader and fluency gaps. That is not an accidental consequence; it is a designed-in second-class path for the exact disability subpopulation WCAG 2.1.1 exists to protect.

Minimum ethics bar:

1. MathLive must be first-class, not "the fallback". UI copy, settings order, and onboarding must treat typed and writing-pad as co-equal modalities — no "primary / fallback" labelling in the student-visible surface. Internal architecture can still default to writing-pad for capture; the student-visible story is "pick your input method".
2. The 001-brief MathLive known gaps (SR compatibility, Arabic/Hebrew operator naming, some fluency issues) are no longer "improve later" — they are blockers for writing-pad-primary math shipping, because writing-pad-primary means keyboard-only users cannot opt out. File these as ship-gates on whichever epic owns Q3 math.
3. If a student has selected typed-math input and the item author has no MathLive-compatible authoring (e.g. an item that only makes sense as a hand-drawn graph), the item is *skipped* for that student with a neutral message — never rendered as "you can't do this one because of your input preference". Same error-message-blame.yml constraint as 001.
4. Per-tenant policy: a school cannot force-enable writing-pad-only. Tenant override can narrow to typed-only (accessibility-sensitive schools) but not the reverse. Asymmetric policy granularity, same direction as 001 photo consent.

Section 5 of the brief says "every modality needs a keyboard-only fallback per WCAG 2.1.1" — the brief is self-aware about this but the recommendation in 4.1 still reads as "writing-pad primary, MathLive secondary". Reconcile: there is no primary. Both are first-class. That is a product-language correction, not a technical one.

## Section 7 positions on 2, 3, 7

**7.2 — Q2 server-side enforcement**: classroom-only with visible banner + override-request path. "Never" is security theatre per redteam. "Optional (student-controlled)" is fine but the classroom variant is what teachers will actually use, so design it honestly.

**7.3 — Q2 commit-and-compare**: ship it, with undo-until-feedback and session-scope for the pre-guess. It is the most pedagogically-honest path and the cognitive case is stronger than simple hide-reveal. Session-scope constraint is non-negotiable.

**7.7 — HWR procurement**: Claude Sonnet vision (option 3). Reuses MSP pipeline, consent surface, moderation, canary tokens, structured-output gate. One architecture, one consent event (after the 4.6 modal copy revision), one cost envelope. MyScript adds a vendor relationship and a second privacy-contract surface for no ethics win; Mathpix ditto. Finops will argue — take that argument, ethics prefers the single-surface path.

## Recommended PRR tasks

1. **PRR-ethics-INP2b** — revise 001 consent modal copy (en/he/ar) to cover stroke/handwriting input; add teacher-visibility handwriting variant line. Blocks writing-pad GA.
2. **PRR-ethics-INP8** — classroom-enforced redaction visible banner + override-request path (ExamTargetOverride access-control pattern). Blocks 3.3 shipping.
3. **PRR-ethics-INP9** — commit-and-compare undo-until-feedback affordance + pre-guess session-scope (ADR-0003 extension). Blocks 3.7 shipping.
4. **PRR-ethics-INP10** — writing-pad / MathLive parity audit: product copy, onboarding order, settings UI — no "primary/fallback" language in student surface. Blocks Q3 math GA.
5. **PRR-ethics-INP11** — MathLive known-gap ship-gate list (SR compat, ar/he operator names) promoted from "improve later" to "required for writing-pad-primary math launch".
6. **PRR-ethics-INP12** — banned-identifier scanner additions: `silentRedact`, `masteryForcedHide`, `strokesAsPhoto`, `primaryPadFallbackMath`, `lockedGuess`. Hebrew/Arabic equivalents.
7. **PRR-ethics-INP13** — per-modality-cost dashboard ethics review: ensure cost telemetry is not broken down by student-demographic (finops must not learn "Arab-sector students use photo more, cap them harder").

## Blockers

- Classroom server-side redaction without the visible banner + override-request path is a GD-004 dark-pattern ship-blocker.
- Commit-and-compare without undo-until-feedback weaponises sunk-cost — ship-blocker.
- Writing-pad-primary math framing in student-visible UX is a WCAG 2.1.1 injustice — fix the framing or do not ship pad-primary.
- 001 consent modal copy must be revised before any stroke-data leaves the client — old copy does not cover handwriting.
- HWR-via-LLM-vision requires the full iter-02 six-layer defence stack (canary, dual-LLM, structured output, CAS backstop, moderation, ephemeral processing). Not a phase-2 concession.

## Questions back to decision-holder

1. Classroom-enforced redaction banner copy — is there appetite for "Your teacher hid the options — ask them to show" vs. a more neutral "options hidden by class policy"? Teacher-visible attribution has pedagogy upsides and trust downsides; educator lens should pick.
2. Commit-and-compare pre-guess — ship visible to teacher as aggregate only, or invisible entirely? Recommend invisible by default, opt-in by tenant, never per-student.
3. Writing-pad / MathLive — do we want a per-student "preferred math input" setting surfaced at onboarding, or a per-session toggle? Recommend onboarding with per-session override. Confirm.
4. If MathLive gap-fix work (INP11) lands after writing-pad GA would otherwise ship, do we delay pad-GA to preserve parity, or ship pad-GA and accept a documented temporary a11y regression? Ethics answer is "delay"; product/finops may disagree.
