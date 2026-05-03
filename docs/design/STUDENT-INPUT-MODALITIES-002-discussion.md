# STUDENT-INPUT-MODALITIES-002 — Q2 hide-reveal depth + Q3 keyboard vs. writing-pad. Persona Discussion Brief

> **Status**: Discussion draft — pre-ADR, pre-task-split.
> **Date**: 2026-04-22
> **Supersedes in scope**: Sections 4 (Q2) and 5 (Q3) of [STUDENT-INPUT-MODALITIES-001](STUDENT-INPUT-MODALITIES-001-discussion.md). Q1 (photo) already resolved via MSP architecture in that brief's synthesis.
> **Scope**: Deeper design questions on two topics the 001-brief under-specified:
>
> 1. **Q2 (hide-then-reveal)** — Option B (per-session student toggle) was 8/10 consensus, but the open design dimensions weren't discussed: default state, UI pattern, server-side enforcement, timer/cue, interaction with scaffolding, diagnostic, and per-question override.
> 2. **Q3 (input modality)** — previous brief asked "shared `FreeformInputField<T>` or per-subject components?" but didn't answer the sharper question: **for each subject, keyboard (typed) or writing pad (handwritten on a canvas)?**
>
> **Next**: personas file findings; synthesize; update discussion brief; draft tasks.

---

## 1. Problem statement

After the 001-brief reviews landed, two follow-up design questions emerged:

- **Q2** — we know *that* we'll ship the per-session toggle, but we haven't designed it. Default state, UI pattern, server-side contract, interaction with hints/scaffolding, diagnostic-onboarding (PRR-228) applicability — all undefined.
- **Q3 sub-question** — the user asked: "do we provide keyboard for this? or virtual writing pad?" This is the actual UX-surface decision. The 001-brief's "shared-vs-per-subject" was the architecture question; this is the modality question.

Both are Launch-scope if we're shipping Bagrut / PET / SAT with real input UX (per persona-educator + persona-ministry: MC-only Launch for chem / humanities is unshippable).

## 2. Current codebase state (evidence)

| Capability | Status | Evidence |
|---|---|---|
| `MathLive` virtual math keyboard | **Exists, mature** | PRR-206 wired to step-solver; used in `useStepSolver.ts` |
| Virtual math keyboard locale variants (ar / he) | **Partial** | Numerals preference tracked in PRR-032; Arabic/Hebrew operator names not in MathLive localization table |
| Physics FBD drawing canvas | **Exists** | PRR-208 wire-fbd-construct-for-physics |
| Stylus / touch writing pad for freehand math | **Does not exist** | Nothing found in `src/student/.../components/` |
| Chemistry input component | **Does not exist** | Confirmed in 001-brief |
| Handwriting-recognition (HWR) pipeline | **Does not exist** | No client HWR; no backend HWR; OCR cascade (ADR-0033) is for printed text only |
| Language text-area input | **Exists, basic** | Standard `VTextarea`; Arabic/Hebrew RTL has documented gaps (persona-a11y finding) |
| Hide-answer-options mode | **Does not exist** | Confirmed in 001-brief |

## 3. Q2 — Hide-then-reveal: open design dimensions

Ship-decision = option B (per-session student toggle). What's still undecided:

### 3.1 Default state

When the toggle is ON for a session, is each question:

- **A. Hidden by default** — student must click "Show options" to reveal. Maximizes generation effect but adds friction every item.
- **B. Visible by default, student chooses to hide** — inverts the current behavior only when the student wants to self-test. Lower effect but lower friction.
- **C. Hidden after N seconds** — shows stem immediately, auto-hides after 3-5s to allow context without anchoring. Compromise but adds a timer-ish UX (ADR-0048 risk).

### 3.2 UI pattern

How do options get hidden / revealed?

- Collapsed `<details>` summary element (tiny "Show options" pill) — preserves layout.
- Options replaced by placeholder text "Click to reveal options" — clearer affordance, bigger click target, a11y-friendlier.
- Options completely absent from DOM until clicked — safest for redteam (no client-side leak) but harder to animate and causes reflow.

### 3.3 Server-side enforcement

Persona-redteam flagged that UI-only hiding is security theater: a scripted client can fetch the full question payload (options included) from the API regardless of hidden/shown state, defeating the generation-effect pedagogy for anyone who wants to cheat.

Options:

- **Never enforce server-side** — accept cheating is possible; UI is for self-discipline only. Cheapest.
- **Optional server-side redaction mode** — student-controlled flag on their session; when enabled, API returns question payload without options until a separate `POST /reveal` call. Honest pedagogy, higher engineering cost, still student-controlled.
- **Classroom-enforced server-side redaction** — teacher turns it on for class session; API enforces regardless of student toggle. Wires to PRR-236.

### 3.4 Interaction with scaffolding ladder

`ScaffoldingService` exists (mentioned in 001-brief). If a student is in hidden-options mode and requests a hint from the scaffolding ladder:

- Does the hint reveal options? (Loses the generation effect.)
- Does the hint work against a "you haven't committed an answer yet" state? (Awkward — what does "hint" mean if the student hasn't seen options?)
- Should scaffolding be unavailable until the student reveals + commits? (Cleanest but adds another friction point.)

### 3.5 Diagnostic-onboarding (PRR-228) applicability

The 6-8-item per-target diagnostic blocks (PRR-228) are intentionally adaptive + easy-first. Does hide-then-reveal apply during diagnostic?

- If yes: the diagnostic measures retrieval strength, not recognition — arguably more accurate, but adds cognitive load right when we're trying to keep completion ≥85%.
- If no: onboarding-diagnostic shows options normally; only the session runner honors the toggle.

### 3.6 Per-question override

Many questions have no MC options at all (step-solver math, chem reactions, essay). The toggle is moot for these. The runner must detect question type and only apply the hide-reveal flow to MC.

Also: some authors may want to **force options-visible** for specific items (e.g. "choose which is correct from these 4 graphs" — the options ARE the question). Need author-level flag?

### 3.7 Commit-and-compare UX

If hide-then-reveal is about "try first, then check your answer", there's a commit flow implicit:

- Student reads stem.
- Student works on paper.
- Student clicks "Reveal options".
- Student sees options, picks one.
- Student gets feedback.

Alternative: student types their best guess before revealing, then compares:

- Student reads stem.
- Student types/writes their answer (free-form or "A/B/C/D guess").
- Student clicks "Reveal options".
- System shows both: what student guessed vs the MC options. If the student's typed answer is equivalent (CAS / string compare), auto-select.

The second flow gets closer to the true generation effect but requires typed-answer infrastructure on an MC question. Worth it?

## 4. Q3 — Keyboard vs. writing pad: subject-by-subject

Every subject needs to answer: **keyboard (typed), writing pad (handwritten + HWR), or both?**

### 4.1 Math (Bagrut Math 3U/4U/5U, PET Quant, SAT Math)

- **Keyboard (MathLive)**: exists, mature, fast for proficient users, CAS-parseable, SR-compatible (with known gaps per persona-a11y). **Trade-off**: students spend 11-12 years doing math on paper; MathLive is a new skill.
- **Writing pad**: zero learning curve, matches exam day (Bagrut + PET are paper). **Trade-off**: HWR error rate on handwritten math ranges 3-15% depending on content. A 10% error rate on a CAS-graded answer is catastrophic ("correct answer marked wrong"). MyScript's math HWR is industry standard but adds ~$0.0001/character cost + vendor dependency.

**Persona-ministry already said** (001-brief): PET quant is no-calculator paper-scratch; **MathLive as default trains the wrong habit**. Writing-pad primary for PET preserves exam fidelity.

**Recommended posture**:
- **Math-primary modality = writing pad with HWR, secondary = MathLive typed** for PET + Bagrut Math items.
- SAT math: keyboard-primary (US test is on-paper but US students have grown up on typed math; different habit).

### 4.2 Physics (Bagrut Physics 5U)

- FBD (free-body diagrams): drawing canvas exists via PRR-208.
- Math expressions: MathLive via step-solver.
- Circuit diagrams (electromagnetism): nothing exists.
- Gaps: schematic input for circuits, vector diagrams beyond FBD, energy-bar charts.

**Recommended posture**: writing-pad primary for diagrams; MathLive for equations. Same math posture as Bagrut Math.

### 4.3 Chemistry (Bagrut Chem 5U)

- Balanced reactions + state symbols: need new component. Typed notation is viable: `2H2 + O2 → 2H2O (l)`. Standard + parseable.
- Structural formulas (Lewis, aromatic rings): writing pad better than keyboard. Chem HWR specialized but exists (e.g. MolScribe).
- Per-mol stoichiometric working: typed numeric/algebraic.

**Recommended posture**: **typed-primary for reactions + stoichiometry + Lewis structures via writing pad**, feeding RDKit-class balance-checker (per persona-educator's proposal).

### 4.4 Language (Bagrut Literature / History / Civics / Tanakh / Language / English / Arabic)

- Essay answers: standard text area with RTL-aware rendering. Keyboard only.
- Handwriting for essays? **No.** HWR accuracy on essay-length handwritten text in Hebrew/Arabic is not production-viable. Typed only.
- Voice-to-text as a11y fallback? Post-Launch consideration.

**Recommended posture**: **keyboard only** for language answers. Per-locale keyboard (he/ar/en) + paragraph-length tracking for rubric alignment.

### 4.5 Cross-subject question: where does the writing pad live architecturally?

Options:

- **Single `WritingPad.vue` component** shared across math / physics / chem, with per-subject HWR adapter. Shared UI state (pen thickness, undo, zoom).
- **Per-subject canvas** (`MathPad.vue`, `ChemPad.vue`) with independent HWR. Tight coupling to subject semantics but lots of duplication.

Persona-a11y's 001-brief recommendation was shared `FreeformInputField<T>`. Persona-cogsci said chem adapter is substantial not thin. The convergence: shared canvas + subject-specific HWR adapter.

### 4.6 HWR procurement

Three paths:

- **Client-side HWR** (JS library like MyScript.js / iink.js): zero network latency, works offline, accuracy ~85-92% on math. Licensing cost recurring.
- **Server-side HWR** (TrOCR, Google Cloud Vision HWR, Mathpix API): higher accuracy (~93-97%), network dependency, per-call cost $0.005-0.02.
- **LLM vision as HWR** (Claude Sonnet on an image of the written stroke): ~90% accuracy on complex math, same cost as Q1 photo-diagnosis ($0.013/call per finops), reuses MSP pipeline.

The **third option is compelling**: reuses the MSP infrastructure we're building for Q1 photo-of-solution, no new vendor, same moderation surface (though hand-drawn content is simpler than uploaded photos).

## 5. Shared concerns

- **A11y**: every modality needs a keyboard-only fallback per WCAG 2.1.1. Writing pad without stylus support on desktop = motor-impaired exclusion. Must offer typed alternative to every pad.
- **Cost**: writing-pad → HWR → cost. Per finops $3.30 ceiling: if every answer triggers an HWR call at $0.013, that's ~$5.20/student/month at 400 answers/month. **Over the cap by 58%.** Would require per-day HWR call cap or hybrid (e.g. MathLive input finalizes; writing-pad scratch is discardable).
- **Redteam**: writing-pad injection via text written on canvas is the same OCR-injection surface as Q1 photo; same gates (canary tokens, structured-schema output) apply.
- **Ministry/fidelity**: PET + Bagrut are paper; writing pad matches exam habit. Language is typed; keyboard matches exam habit. The modality should mirror the real test.

## 6. Persona-lens discussion prompts

Each lens files findings under `pre-release-review/reviews/persona-<lens>/student-input-modalities-2-findings.md`.

### 6.1 persona-educator

- Section 3.1 default state — hidden-first vs visible-first? What matches teacher-guided drill practice?
- Section 3.4 scaffolding interaction — your take on "hint under hidden options"?
- Section 4: for each subject (math / physics / chem / language), what's the right modality? Challenge my recommendations.
- What exam-fidelity hazard matters more — MathLive typing trains wrong habit for paper Bagrut, or writing-pad HWR errors mark correct answers wrong?
- Section 3.7 commit-and-compare flow — would a teacher ask for this, or is it over-designed?

### 6.2 persona-cogsci

- Generation effect default state — Bjork/Bertsch effect size holds under hidden-by-default vs visible-by-default with opt-in hide?
- Diagnostic-onboarding applicability (3.5) — does hide-reveal belong in PRR-228, or is retrieval-strength measurement contaminated?
- Handwriting (4.1) as exam-fidelity proxy vs MathLive as cognitive-load measure — which matters more for learning?
- Chem input as concept representation (Johnstone triangle + Taber) — does writing pad for Lewis structures + typed for balancing capture both symbolic + sub-microscopic?
- Literature answers typed vs handwritten — known difference in essay quality? (Berninger's handwriting-vs-typing studies.)

### 6.3 persona-ethics

- Section 3.3 — classroom-enforced server-side redaction without student consent: dark pattern?
- Section 3.7 commit-and-compare — forces commit; is that coercive or honest?
- Section 4.6 HWR-via-LLM-vision — prompt-injection-via-handwriting is still present even if simpler than photo; consent scope extends?
- Writing pad vs keyboard as forced choice — if student has motor impairment and only keyboard works, do they get a worse experience (MathLive syntax overhead)?

### 6.4 persona-a11y

- Writing pad mandatory stylus support — what's the keyboard-only flow for users who can't use a stylus?
- MathLive known gaps (001-brief): will they block math writing-pad-primary posture, or improve if writing pad is primary and MathLive is secondary?
- Section 3.2 UI pattern — which pattern is most SR-friendly for hidden-then-revealed options?
- Chem structural-formula pad — blind students CANNOT draw Lewis structures. Typed alternative must be first-class. Confirm?
- Section 4.4 language text-area RTL — specific Vuetify VTextarea gaps + workarounds?

### 6.5 persona-enterprise

- Section 3.3 classroom-enforced redaction — should this wire through PRR-236 class defaults?
- Tenant-level "allow writing pad: true/false" — schools banning phones may ban tablets-as-styli too. Policy granularity?
- HWR vendor selection — tenant-level overrides for FedRAMP / data-sovereignty tenants?
- Feature-flag shape — same `TenantPolicyOverlay<T>` from 001-brief enterprise review?

### 6.6 persona-ministry

- Writing-pad primary for PET quant (4.1) — confirms your 001-brief concern about MathLive training wrong habit?
- Bagrut Chem notation fidelity — does typed notation `2H2 + O2 → 2H2O (l)` match Ministry's שאלון-expected format, or is there notation variance?
- Bagrut Lit / History / Civics — any Ministry-expected length, paragraph structure, citation style we're not capturing?
- Hand-drawn Lewis structures as שאלון-valid — do real Bagrut chem papers accept typed notation, hand-drawn, or both?

### 6.7 persona-sre

- HWR vendor choice (4.6) — availability + latency profile per option. Claude Sonnet vision vs dedicated HWR?
- Client-side HWR (MyScript) latency at p95/p99 on low-end Android?
- Server-side HWR during Bagrut spike — capacity add on top of PRR-231.
- Writing-pad tile / stroke-data storage — does it hit the same 24h TTL as Q1 photo?
- Section 3.3 server-enforced redaction — cache-invalidation story when mode toggles mid-session?

### 6.8 persona-redteam

- Writing-pad canary-token defense — is "draw this canary glyph" viable, or does HWR destroy the token?
- HWR-via-LLM-vision (4.6) — prompt-injection surface equivalent to Q1 photo, reuses gates?
- Section 3.3 — can student manipulate their local `hidden-options-toggle` state to fetch the full payload if server-enforcement is absent?
- Section 4.4 typed essay — LLM rubric prompt injection surface, again (PRR-022 gap you already flagged)?
- Stroke-data replay — can a student re-submit identical strokes across sessions? Dedupe cost?

### 6.9 persona-privacy

- Writing-pad stroke data — is handwriting uniquely identifying? (Known result: yes, stroke dynamics are biometric-adjacent; PPL Amendment 13 question.)
- HWR-via-LLM-vision — same VisionPromptSafetyGate gate as Q1 photo, or an additional path?
- Stroke-data retention — 24h TTL with derived-output 30d, same as Q1 photo?
- Section 3.3 server-side redaction — does the redacted-question projection need separate RTBF cascade handling?

### 6.10 persona-finops

- Section 5 cost sanity — 400 answers/month × $0.013 HWR = $5.20/student/month. Real projected answer-per-month count? Better cap discipline design?
- Claude Sonnet vision (4.6) reuses Q1 infrastructure — cache hit rate for HWR vs for photo-of-solution? Probably lower (more unique strokes).
- Per-modality cost dashboard needs per-subject breakdown to prevent "math HWR is bleeding us" blindness?
- Tier-bump for writing-pad-heavy use — premium-SKU path?

## 7. Open product questions back to decision-holder

These DO NOT converge in-brief and need your call:

1. **Q2 default state** (3.1): hidden-first, visible-first, or N-second reveal?
2. **Q2 server-side enforcement** (3.3): never, optional, or classroom-only?
3. **Q2 commit-and-compare flow** (3.7): ship it, or start simple and add later?
4. **Q3 math modality** (4.1): writing-pad primary + MathLive secondary, OR MathLive primary + writing-pad scratch?
5. **Q3 chem modality** (4.3): typed-primary for reactions, writing-pad secondary for Lewis? Or visual-primary everywhere?
6. **Q3 language modality** (4.4): confirm keyboard-only?
7. **Q3 HWR procurement** (4.6): Claude Sonnet vision (reuses Q1 MSP), MyScript (client), Mathpix (server), or mix?
8. **Section 5 cap** — if HWR breaks the $3.30 ceiling, do we cap (daily HWR call limit per student), scope down (HWR only for specific question types), or tier-bump?

---

**History**:
- 2026-04-22: Draft after 001-brief surfaced the need for deeper Q2 + Q3 modality design.
