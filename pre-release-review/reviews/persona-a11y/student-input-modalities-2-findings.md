---
persona: a11y
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: red
supersedes: 001-brief findings (confirmed, extended)
---

## Section 6.4 — detailed answers

### 6.4.a Writing pad mandatory keyboard-only alternative

**WCAG 2.1.1 (keyboard), 2.1.3 (keyboard, no exception), 2.5.1 (pointer gestures), 2.5.7 (dragging movements, 2.2).** A canvas that requires stylus strokes is a keyboard-trap by construction: there is no `Tab`-order on pixel coordinates, no way for a motor-impaired user on a trackball/switch device to trace a curve, and no way for an SR user to perceive a canvas at all. Writing-pad primary without a mandated typed-equivalent is a dead WCAG 2.1.1 failure. This is ship-blocking.

**Required flow for motor-impaired keyboard-only users** — the writing-pad surface must auto-provide a "Type this answer instead" affordance reachable by `Tab` (visible on first focus, WCAG 2.4.7), that swaps the canvas for the existing MathLive (math), typed reaction notation (chem), FBD object-picker (physics), or plain textarea (language) primitive. The swap is per-question, not per-session — a student may write question 1 on their iPad and type question 2 one-handed, with no penalty. The typed-path MUST feed the same grader (CAS / RDKit / rubric) — it CANNOT be a second-class path that produces lower-fidelity grading.

**Required flow for SR users** — canvas gets `role="img"` + `aria-label="Drawing area, not accessible. Press Tab to switch to typed entry."` and the typed primitive is the primary input; the canvas is `aria-hidden="true"` until the sighted student un-hides it. This is the inverse of "canvas primary with typed fallback" — for SR students the typed primitive is the only path and the canvas never renders meaningfully. WCAG 1.1.1 (non-text content), 1.3.1 (info and relationships), 4.1.2 (name, role, value).

### 6.4.b UI pattern for hide-reveal (3.2) — SR-friendly ranking

Ranked most SR-friendly to least:

1. **"Options replaced by placeholder text"** (most SR-friendly). `aria-live="polite"` on the container announces the placeholder ("Answer options are hidden. Press the reveal button."), reveal swaps content and moves focus to first option, announcement fires naturally. Clear affordance, SR-readable state, no focus-management edge cases. WCAG 4.1.3 (status messages), 2.4.3 (focus order).
2. **Collapsed `<details>` element** — semantically correct (`summary` has native expanded/collapsed ARIA), Enter/Space activates, NVDA/VoiceOver both announce state transitions. Pitfall: custom styling of `::marker` can break SR exposure of the expanded state on older NVDA builds. Workable but fragile.
3. **Options absent from DOM until clicked** (least SR-friendly). Reflow destroys SR virtual-cursor position; when options appear, the SR has no anchor and students lose their place. This also makes `aria-controls` meaningless until after reveal. Avoid.

Pattern #1 is the right default. Pair with `useReducedMotion` (prefer-reduced-motion users get instant swap, no cross-fade).

### 6.4.c Chem — typed-Lewis is primary, confirm + notation standard

**Confirmed: blind students cannot draw Lewis structures. Typed must be primary, not fallback.** WCAG 1.1.1, 2.1.1, 1.3.1. Any drag-draw canvas is enhancement-only and must have full typed equivalent for every structural entry.

**Notation standard recommendation: SMILES (canonical, OpenSMILES spec) as the submission format**, with a **Ministry-compatible pretty-render layer** in parallel. Reasoning:

- **SMILES** is machine-parseable (RDKit, OpenBabel), has explicit support for lone pairs via `[O-]`, `[N+]`, radicals via `[CH3]`, aromatic rings via lowercase, and charge/bond semantics. It is the de-facto typed standard for chemistry software and the parser is free + battle-tested.
- **InChI** is a canonical-identifier format not an author-friendly input format; it's opaque to students. Use InChI only for de-dup/equivalence-checking on the server (compute InChI from student's SMILES submission, compare to canonical-answer InChI).
- **Custom DSL** (e.g. `H-O-H` ASCII with explicit bonds) is tempting for pedagogy but creates a new dialect to teach, document, and localize — ship-risk.

**Ministry format reconciliation**: Bagrut chem graders expect conventional line-angle / Lewis dot-and-cross. The student submits SMILES-with-lone-pairs `[H][O]([H])` or the richer `[OH2]`; the server parses with RDKit, pretty-renders it as a Lewis diagram alongside the student's typed entry ("here's what you wrote, does it match what you mean?"). The pretty-render image is `aria-hidden` for SR users — the SR only reads the student's typed SMILES + a human-readable description ("water, two hydrogen-oxygen bonds, two lone pairs on oxygen"). WCAG 1.1.1 text alternative, 3.3.3 error suggestion, 3.3.4 error prevention.

The `ChemInput` component API: `{ smiles: string, lewisRender: SVGElement (aria-hidden), sympyEquivalent: string, srDescription: string }`. Blind students read `srDescription`; sighted students see `lewisRender`; the grader consumes `smiles`/`sympyEquivalent`.

### 6.4.d Language textarea — Vuetify VTextarea RTL gaps + Arabic literature 500-word workarounds

Confirmed from 001-brief: per-field `lang` + `dir` mandatory (WCAG 3.1.2), not inherited. Specific gaps for **500-word Arabic Bagrut literature essays** on current Vuetify VTextarea:

1. **Counter position** — Vuetify renders character/word count via pseudo-element positioned `right: auto; left: 12px` in RTL mode, but certain Vuetify 3.x builds miscalculate when `dir="rtl"` is on the input only (not the page). **Workaround**: wrap in explicit `<div dir="rtl" lang="ar">` and set `hide-details="auto"` + render count in a sibling `<div aria-live="polite">`.
2. **Selection-handle direction on iOS Safari** — known WebKit bug, no Vuetify fix. **Workaround**: accept, document, add iOS-specific Playwright smoke to confirm selection-replace works even if handle visuals look wrong.
3. **Bidi cursor jump** on Arabic essay with embedded English terms (common in literature essays citing English criticism). **Workaround**: force `unicode-bidi: plaintext` on the textarea; test cursor behavior after typing `"كما قال T.S. Eliot في"`. Playwright required.
4. **Autocorrect/autocapitalize** — must be `autocapitalize="none" autocorrect="off" spellcheck="true"` with Arabic spellcheck dictionary loaded (browser-dependent; document fallback).
5. **Word count for Arabic** — `string.split(/\s+/)` miscounts because Arabic diacritics and ZWJ characters can be counted or not depending on locale. **Workaround**: use `Intl.Segmenter('ar', { granularity: 'word' })` (Baseline 2024, all target browsers support).
6. **Right-to-left paragraph indent** — CSS `text-indent` applies on the leading edge correctly with logical properties, but Vuetify's `.v-field__input` has a hardcoded `padding-left` in some themes. **Workaround**: override with `padding-inline-start`.
7. **500-word auto-save** — Arabic students losing 500 words mid-essay is a non-trivial a11y concern (motor-impaired students retyping at reduced WPM suffer disproportionately). Mandate localStorage auto-save every 30s with `aria-live` announcement on save ("Saved 324 words"). WCAG 3.3.4 error prevention.

### 6.4.e Writing pad stylus requirement — desktop-only (no touch) flow

**Desktop-only students WITH a pointer (mouse/trackpad) but no touch or stylus** can technically draw, but mouse-drawing math is a motor nightmare — accuracy of a freehand `∫` with a trackpad is catastrophic, and the HWR error rate compounds. **Recommend: on desktop-no-touch, default to typed primitive (MathLive/chem/FBD-picker) and expose the writing pad only behind an explicit "Draw with mouse" opt-in.** Auto-detect via `@media (pointer: coarse)` + `navigator.maxTouchPoints`; if neither, writing pad is not the default. This also saves the HWR cost for a cohort that would generate high-error strokes.

**Desktop-only students with NO pointer** (keyboard-only) — see 6.4.a; typed path is mandatory and is their only path.

### 6.4.f Section 7 positions

- **Q2 default state (7.1)** — **visible-first with per-question "hide options" toggle**, not hidden-first. Hidden-first forces every SR user to execute an extra "reveal" step on every question, which over 400 items/month is a fatigue/WCAG 2.4.3 friction pattern.
- **Q2 server-side enforcement (7.2)** — **optional, student-controlled**. Classroom-enforced without student consent is a dark-pattern per persona-ethics alignment; also WCAG 3.2.5 (change on request).
- **Q2 commit-and-compare (7.3)** — **ship simple first**. The compare flow doubles the a11y test matrix; add in v2.
- **Q3 math modality (7.4)** — **MathLive primary, writing-pad optional**. Writing-pad primary mandates keyboard-equivalent which is MathLive; making the equivalent "primary" avoids the three-path rebuild.
- **Q3 chem (7.5)** — **typed-primary (SMILES + reaction notation), Lewis canvas optional enhancement**. Confirmed above.
- **Q3 language (7.6)** — **keyboard-only. Confirmed.**
- **Q3 HWR (7.7)** — **Claude Sonnet vision** reuses MSP + existing moderation + consent surface; minimizes new a11y-auditable surfaces.

### 6.4.g Recommended PRR tasks

1. **PRR-NEW-A11Y-G (P0)** — Writing-pad "switch to typed" affordance, canvas `role="img"` + SR description, keyboard-swap flow. Blocks writing-pad ship.
2. **PRR-NEW-A11Y-H (P0)** — `ChemInput` SMILES-primary + `srDescription` + `aria-hidden` Lewis render. Blocks chem ship.
3. **PRR-NEW-A11Y-I (P1)** — Hide-reveal UI pattern per 6.4.b (placeholder+`aria-live`). Reference `AnswerFeedback.vue`.
4. **PRR-NEW-A11Y-J (P1)** — Vuetify VTextarea RTL audit + seven specific workarounds above; Playwright bidi/cursor suite.
5. **PRR-NEW-A11Y-K (P1)** — Pointer-type detection, desktop-no-touch default-to-typed.
6. **PRR-NEW-A11Y-L (P2)** — Essay auto-save with `aria-live` announcement, 30s cadence.

### 6.4.h Blockers

- **BLOCKER** — Writing-pad primary without mandated typed-equivalent per question = WCAG 2.1.1 + 2.5.1 + 2.5.7 failure. Typed equivalent must feed the same grader.
- **BLOCKER** — Chem Lewis canvas without SMILES-primary + `srDescription` = WCAG 1.1.1 + 2.1.1 + 1.3.1 failure.
- **NON-NEGOTIABLE** — Per-field `lang` + `dir` (001-brief finding, re-affirmed).
- **NON-NEGOTIABLE** — Hide-reveal uses the placeholder+`aria-live` pattern; no DOM-absence pattern.
- **NON-NEGOTIABLE** — Writing pad defaults OFF on desktop-no-touch.

**Verdict: RED** until P0 tasks (G, H) are scoped and the mandated typed-equivalent architecture is ADR-locked.
