---
persona: a11y
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red
---

## Summary

Three modalities, three different a11y blast radii. Q2 hide-then-reveal is the only one that is close to safe-to-ship — it is a controlled disclosure pattern we already have primitives for (`useReducedMotion`, existing `aria-live` tests in `tests/a11y/aria-live-on-dynamic.spec.ts`). Q1 photo upload and Q3 freeform non-math input both ship new failure modes that our current scaffolding cannot absorb without new work: photo capture as a *required* step excludes motor-impaired and desktop-only users unless there is a typed-work fallback; chem/Arabic-literature inputs have no screen-reader story at all; and MathLive — which we already lean on — has well-documented SR gaps that Q3 would inherit and widen. Net verdict: **red** until Q1 guarantees a typed-work alternative path and Q3 commits to keyboard-first primitives per subject.

## Section 7.4 answers

**Q1 photo — motor-impairment alternatives, desktop-only students, "type your work instead" fallback?**

Photo capture demands a steady two-handed phone operation. Students with tremor, one-handed users, and students who use head-pointers or switch devices will either fail the capture or produce blurry photos the OCR rejects — each retry is a cognitive tax (WCAG 2.5.5 target size, 2.5.1 pointer gestures, 2.4.6 heading/labels for retry error states). Desktop-only students (meaningful cohort — not every Bagrut student has a phone they can point-and-shoot with, and exam-prep happens at a desk) cannot use the flow at all if "photo" is the only entry point. **Mandatory: the "I got this wrong, help me understand why" affordance must offer three paths — (a) photo capture, (b) file upload from disk (drag-drop or `input[type=file]`, already supported by `pdf-upload.vue`), (c) a textarea-based "describe your working" fallback that feeds the same misconception pipeline as OCR output.** Option (c) is also the motor-impairment escape hatch and is the one feature that keeps this from being WCAG 2.1.1 (keyboard) + 1.3.5 (input purpose) failures. Narrow framing from §3 is the better call partly because it makes this three-path requirement tractable. WCAG 1.1.1 (non-text content — OCR must produce text equivalents that feed back to the student, not just into the LLM silently), 2.1.1, 2.5.1.

**Q2 hide-then-reveal — keyboard-only trigger, SR announcement, reduced-motion?**

This one is tractable. The "Show my options" trigger is a `<button>` — keyboard-reachable by definition, activated by Enter/Space (WCAG 2.1.1). The reveal must (a) move focus to the first option, not stay on the button (WCAG 2.4.3 focus order), (b) announce via `aria-live="polite"` on the options container — same pattern as `AnswerFeedback.vue` and our existing `aria-live-on-dynamic.spec.ts` coverage — with copy like "4 answer options revealed. Option 1 of 4: ...", (c) respect `prefers-reduced-motion` via the existing `useReducedMotion` composable — no slide-down, no fade; the options either exist or they don't when reduced-motion is on. The button's `aria-expanded` must flip true→true-permanent, not be a toggle (one-way reveal is the intended pedagogy). **Recommend Option B (per-session student toggle) over C (pedagogy-driven) on a11y + ethics grounds** — C is opaque to the student which is both a dark-pattern concern (persona-ethics) and a WCAG 3.2 "predictability" concern for SR users who can't visually eyeball whether options exist. WCAG 4.1.2 (name/role/value — `aria-expanded`, `aria-controls`), 2.3.3 (animation from interactions).

**Q3 chem input — low-vision if drag-draw; typed alternative?**

Drag-to-draw ring structures, lone-pair placement, and reaction-arrow tools are **unusable by low-vision, screen-reader, and motor-impaired users** if they are the only entry path. Ministry Bagrut Chemistry written answers are mostly typed notation anyway (`H2SO4 + 2NaOH -> Na2SO4 + 2H2O`), which is what graders read. **Mandatory: every chem input primitive has a typed-notation equivalent.** Proposed: a ChemInput component that accepts typed SMILES-lite or plain reaction notation, with an optional visual canvas that is explicitly marked as enhancement-only and has `aria-hidden="true"` plus a visible "use text input instead" toggle. The parser lives server-side (same pattern as MathLive → LaTeX → SymPy). This also answers the shared-abstraction question in §5: the shared abstraction is typed-first + optional visual layer, not a shared visual canvas. WCAG 1.1.1, 1.3.1, 2.1.1.

**Q3 Arabic literature input — keyboard + RTL textarea; Vuetify RTL issues?**

Native textarea RTL works when `dir="rtl"` + `lang="ar"` are set on the field (not just the page) — Vuetify's `VTextarea` in our current code relies on CSS logical properties that mostly hold but known gaps exist: (1) cursor-jump on bidi content (Arabic paragraph + embedded English term), (2) selection-handle direction on iOS Safari, (3) character-count positioning (Vuetify puts counter in `::after` which renders on the wrong side in RTL on some versions). Arabic keyboard input depends on OS IME — not our concern for input but autocorrect/autocapitalize must be disabled (`autocapitalize="none"` — Latin capitalization logic applied to Arabic is broken). **Per-field `lang` and `dir` attributes are mandatory — do not inherit from page** — an English-literature item in an Arabic-locale student session must render the textarea LTR regardless of page direction; same rule as the math-always-LTR memory, applied to prose. Recommend explicit Playwright RTL test per modality before ship. WCAG 1.3.2 (meaningful sequence), 3.1.2 (language of parts).

**MathLive SR compatibility — known gaps?**

MathLive announces via a hidden live region and has `aria-label` on the math field, but documented gaps: (1) step-by-step entry produces extremely chatty narration — `x` + `^` + `2` announces as three separate events, not "x squared" — inside a timed session this becomes fatiguing; (2) navigating fractions/subscripts via arrow keys produces inconsistent announcements across NVDA/JAWS/VoiceOver — the MathLive team tracks this in their issue list, not resolved; (3) MathLive's virtual-keyboard is a mouse/touch target by default — keyboard-only activation path exists but is discoverable only via docs; (4) Arabic and Hebrew locale labels for math operators do not exist in MathLive — the SR reads operator names in English even in an Arabic session (this is the Arabic-numerals-equivalent bug for operator names; PRR-032 is numerals, a sibling PRR-NEW for operator-name locale is warranted). Q3's "extend to physics/chem via MathInput" therefore inherits four known SR gaps, and ship readiness depends on **not regressing beyond today's MathInput a11y baseline** — a session-level a11y regression test is required before expanding MathInput into new subjects. WCAG 1.3.1, 4.1.2.

## Q1 consent modal copy — WCAG cognitive-load

Photo-upload by a minor requires first-time consent copy. Draft copy must satisfy WCAG 3.1.5 (reading level) — lower-secondary reading level, ≤9 grade equivalent. Banned jargon: "OCR", "vision model", "CSAM moderation", "prompt injection", "Tier 3 LLM", "misconception extraction". Use verbs the student understands: "We'll read your paper and find where the answer went wrong. We delete the photo after 30 days. We never share it with your classmates or teachers unless you ask us to." Modal must be dismissable by Escape (WCAG 2.1.2 no keyboard trap) and the "Agree" button must not be the default focus — the "Read more" link must be, so the student has a path to detail before consent (WCAG 3.3.2 labels/instructions). Arabic/Hebrew copy must be translated for meaning, not literal — "we'll read your paper" is idiomatic in English but needs reframing in Arabic where "قراءة" has academic connotations.

## Section 8 positions

- **8.1 Q1 framing** — **narrow only**. Broad framing triples the moderation surface and doubles the consent copy complexity (now every page is a potential upload trigger, needing context-specific explanations). Narrow framing keeps the modal copy to one decision point.
- **8.2 Q2 implementation** — **Option B (per-session student toggle)**. A and C both have a11y tax: A needs author-authored `reveal_on_request` metadata which is a new content field we'd then have to l10n and validate; C is opaque to SR users who can't tell if the product is silently hiding options from them.
- **8.3 Q3 architecture** — **shared `FreeformInputField<T>` with typed-first primitive + subject-specific enhancement layer**. The shared-abstraction path collapses the a11y test matrix from N-subjects × 3-locales × 3-SRs to 1-primitive × 3-locales × 3-SRs + per-subject renderer smoke tests.
- **8.4 Chem launch-scope** — **slip rather than ship MC-only**. MC-only chem at Launch trains students on the wrong habit (recognition, not production) and persona-ministry will flag the Bagrut-format mismatch. Better to delay chem content than ship degraded pedagogy.
- **8.5 Humanities launch-scope** — **same: slip rather than degrade**. Additional a11y cost: rubric-graded essays need text-area + word-count + spell-check + RTL handling all verified before first student types anything.
- **8.6 Cost cap** — not my lane, but if cost concerns push Q1 toward a premium tier, persona-ethics will flag it and a11y will double-flag it (paywalling the typed-work fallback path would be an accessibility-equity violation).

## Recommended new PRR tasks

1. **PRR-NEW-A11Y-A (P0)** — Q1 typed-work fallback primitive. Three-path entry (photo, file upload, textarea) for the "understand-why" affordance. Blocks Q1 ship.
2. **PRR-NEW-A11Y-B (P0)** — Chem typed-notation parser + `ChemInput` component with typed-first baseline, optional visual layer flagged `aria-hidden` + `display:none` in reduced-motion-plus-screen-reader mode. Blocks Q3 chem.
3. **PRR-NEW-A11Y-C (P1)** — Per-field `lang` + `dir` enforcement pass across every free-form text input (not just Q3 — this is a gap in current MathInput label strings too). Covers WCAG 3.1.2.
4. **PRR-NEW-A11Y-D (P1)** — MathInput SR regression baseline test (NVDA + VoiceOver). Captures current narration of `x^2`, fractions, sqrt. Any Q3-expansion PR must not degrade it.
5. **PRR-NEW-A11Y-E (P1)** — Q1 consent modal copy audit + translation at ≤grade-9 reading level, three locales. Escape-dismissable, focus-to-"read-more"-link not "Agree".
6. **PRR-NEW-A11Y-F (P2)** — MathLive operator-name locale work (sibling to PRR-032 numerals). Verify whether upstream supports it or we need a fork/override layer.

## Blockers / non-negotiables

- **BLOCKER** — Q1 without a typed-work fallback is a ship-stopper. Motor-impaired students + desktop-only students cannot use photo-only flows; WCAG 2.1.1 + 2.5.1 failure.
- **BLOCKER** — Q3 chem without a typed-notation primitive is a ship-stopper. Drag-draw-only chem input is WCAG 1.1.1 + 2.1.1 failure, and Ministry Bagrut format is typed anyway.
- **NON-NEGOTIABLE** — Every free-form field carries its own `lang` + `dir` attributes. No inheritance from page. Same rule as math-always-LTR, applied to prose.
- **NON-NEGOTIABLE** — Q2 reveal trigger must move focus to the first revealed option and announce via `aria-live="polite"`. Existing `AnswerFeedback.vue` is the reference implementation.
- **NON-NEGOTIABLE** — Q2 reveal animation must respect `prefers-reduced-motion` via the existing `useReducedMotion` composable. No new animation primitives introduced.
- **NON-NEGOTIABLE** — Primary color `#7367F0` stays. Focus rings on Q2 options and Q1 upload trigger meet 3:1 contrast against their actual backgrounds, not the page background.

## Questions back to decision-holder

1. Is the "narrow framing" for Q1 locked, or is broad framing still on the table? Narrow makes the consent+a11y story manageable; broad does not.
2. For Q2, do we want an explicit session-start settings affordance ("Try without seeing options this session?") or is it a per-question button the student clicks each time? A11y prefers the former (one decision, not N).
3. Is chem drag-draw worth building at all given it's enhancement-only from an a11y standpoint and every student still needs the typed path? If not, drop it and keep chem purely typed.
4. For Arabic literature textarea — is the target the Ministry Bagrut Arabic-Language-and-Literature exam, or Arabic-as-a-second-language in Hebrew-stream schools? The former needs full RTL essay support; the latter needs mixed-direction handling. Different test matrices.
