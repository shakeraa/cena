---
persona: a11y
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: red
---

## Summary

Multi-target plan is the right product move, but the surface it introduces — a Vuetify date picker in he/ar, a weekly-hours slider in three locales, an 8-step wizard with an inner N-page loop, and catalog cards with track radios — lands on every known weak spot of our current a11y scaffolding. The existing `ExamPlanStep.vue` (PRR-148) quietly sidesteps two of these by using a raw `<input type="date">` inside a `<bdi dir="ltr">` and a bare `<input type="range">` with no screen-reader scaffolding; once the design moves to Vuetify components and a per-target loop, those shortcuts break. Three issues rise to ship-blocker: (1) VDatePicker RTL correctness is unverified and the known reversed-equation failure mode applies directly, (2) the per-target inner loop has no defined SR-announceable "step 4 of 8 — target 2 of 3" position, (3) PRR-032's numerals toggle is referenced in the brief but neither the task file nor the `numeralSystem` store field exists in code.

## Section 9.4 answers

**Date picker in Vuetify — does it actually render correctly in he/ar?**

Red flag. The current `ExamPlanStep.vue` does **not** use VDatePicker — it uses a native `<input type="date">` wrapped in `<bdi dir="ltr">` (ExamPlanStep.vue:125–134). That's a deliberate dodge, and it works because native pickers inherit the OS locale. The brief's move to a richer catalog-driven flow implies VDatePicker (Vuetify has no silent LTR→RTL fallback guarantee; the Hebrew calendar has zero coverage in `@vuetify/locale` and Arabic-Umm-al-Qura is an opt-in). I could not find a single `VDatePicker` / `v-date-picker` usage in `src/student/full-version/src` — meaning we have zero existing evidence this works. **Must be prototyped in all three locales (en/he/ar), with a screen reader, before any ADR locks the design.** If it falls back to Gregorian-LTR silently with Arabic numerals mangled, that's the exact reversed-equation class of bug the user caught before. WCAG 1.3.2 (Meaningful Sequence), 3.1.1 (Language of Page), 1.4.10 (Reflow) all at risk.

**Weekly-hours slider — screen reader announcement quality + numerals toggle (PRR-032)?**

Current implementation (ExamPlanStep.vue:159–168) is a raw `<input type="range">` with no `aria-valuetext`, no `aria-describedby` pointing at the min/max helper, and a value-readout `({{ weeklyHours }}h)` baked into the visual label that screen readers will re-announce on every keystroke causing narrator chatter. The "total across all targets" counter (section 5, step 3) needs `aria-live="polite"` with a debounce or every slider tick broadcasts. **PRR-032 numerals toggle does not apply here today** because PRR-032 is referenced-but-not-written — I searched `tasks/pre-release-review/` and the file is absent, and `numeralSystem` does not exist in `onboardingStore.ts`. That's not just a gap in the slider — it means Arabic students see Western digits everywhere until we actually write and ship that work. Blocker-adjacent.

**8-step wizard keyboard flow — logical back-target + tab order?**

The wizard stepper (`OnboardingStepper.vue`) only carries `aria-label="step X of Y"` on an outer element; there's no `role="tablist"` / `role="tabpanel"` pattern, no `aria-current="step"`, and back-navigation is whatever button the step component implements (ExamPlanStep.vue has skip + confirm but no back). Adding two steps (`exam-targets`, `per-target-plan`) without fixing this leaves 8 panels with inconsistent back semantics. Per-target-plan is especially bad: if the student lands on target 2 of 3 and hits browser-back, they either go to step 1 of target-1 or blow out to `role` (no defined behavior in the brief). WCAG 2.4.3 (Focus Order), 3.2.3 (Consistent Navigation).

**Exam-target cards — focus ring, keyboard selection, group semantics for multi-select?**

Brief says "Tap to select; checkmark" and "minimum 1, maximum 4" — that's a multi-select checkbox group, not a radio group. LanguagePicker.vue (the cited good example) uses `role="button"` + `aria-pressed` on a VCard — fine for single-select but **wrong for multi-select**. Correct pattern for multi-select cards: `role="group"` wrapper with `aria-labelledby` pointing at the section heading, individual cards as `role="checkbox"` with `aria-checked`. Focus ring must not rely on Vuetify's default `primary` outline because `#7367F0` at 2px offset on light bg is 3.1:1 against white — passes WCAG 1.4.11 (3:1 non-text) but thin; against colored card backgrounds it can drop below threshold. Also: the 4-target cap needs an `aria-describedby` announcement when the student tries to select a 5th, not a silent disabled state. WCAG 4.1.2 (Name Role Value), 1.4.11.

**Per-target-plan loop (N pages) — how does a screen reader know "step 4 of 8 — page 2 of your 3 targets"?**

Brief does not answer this and it's the hardest problem. Options: (a) flatten — each per-target page counts as its own top-level step, so a 3-target plan is 8+(3-1)=10 steps; (b) nested — stepper shows "4/8", and the panel announces "Bagrut Math 5U — target 2 of 3" via `aria-live`. I recommend (a): flattening avoids `aria-live` fighting the stepper and matches our existing linear model. If (b) is chosen, the panel must carry `aria-label="{{ exam.name }} — target {{ i }} of {{ n }}"` and the outer `aria-label="step 4 of 8"` must be updated to stay in sync on every target transition — two sources of truth is how focus gets lost. Either way this needs spelling out in ADR-0049 before task split. WCAG 1.3.1 (Info and Relationships).

**Reduced-motion (PRR-070) across step transitions?**

Currently LanguagePicker has a local `@media (prefers-reduced-motion: reduce)` block and global reset exists per PRR-070 task. Step transitions between 8 panels (likely a slide or fade in Vuetify) must be audited — I did not verify in code. Add to PRR-070 scope.

## Additional findings

1. **Track picker radio (3U/4U/5U)** — needs `<bdi dir="ltr">` around the track code inside the radio label in he/ar. The brief says this in §4 catalog metadata but §5 step 1 ("radio: 3U / 4U / 5U") forgets to re-state it. Implementer footgun.
2. **Free-text note field (≤200 chars)** — if kept (open question 10.2), needs live character-count announcement via `aria-live="polite"` at 180/200, not a silent truncation at 200. And `lang` attribute must flip to match selected locale, not page locale.
3. **"Not sure yet, skip" → placeholder target** — the post-onboarding nag must be dismissible via keyboard (Escape) and not a focus trap. Home-dashboard nags are a classic focus-management bug.
4. **Per-target deadline in UTC, displayed in local** — a11y angle: date announcement must use the user's locale (`Intl.DateTimeFormat(locale)`) not UTC ISO, otherwise blind students hear `2026-06-15T00:00:00Z` read as a string of numbers.
5. **Catalog search/filter box** (§5) — needs `role="searchbox"` + `aria-controls` pointing at the card grid + live-region result count.

## Section 10 positions

- **10.2 free-text note** — drop it. 200 chars of arbitrary text is a PII + l10n + a11y triple-tax for marginal scheduler value. If retained, it's another keyboard-announceable surface to test and localize.
- **10.3 max targets cap** — stay at 4. Higher caps worsen the per-target-loop SR narration problem; every target doubles the number of compound screen-reader announcements a user has to parse.
- **10.4 require-at-least-one vs skip** — require one. "Skip → placeholder + nag" adds a whole new error state that needs to be SR-announceable everywhere it appears. Single required selection is cleaner.
- **10.6 parent visibility of exam plan** — default hidden for students 16+, default visible for <16. Not an a11y issue directly, but the visibility toggle is.

## Recommended new PRR tasks

1. **PRR-NEW-A11Y-1 (P0)** — Prototype VDatePicker in he/ar/en and verify against NVDA + VoiceOver + TalkBack before ADR-0049 locks the design. If any locale falls back to LTR or to Gregorian-only, document the constraint and pick a different picker (native `<input type="date">` is the current fallback and works).
2. **PRR-NEW-A11Y-2 (P0)** — Define the nested-step SR announcement pattern for per-target-plan. Spec must pick flattened vs nested and name the ARIA attributes. Blocks task split.
3. **PRR-NEW-A11Y-3 (P1)** — Weekly-hours slider `aria-valuetext` + total-hours live region debounce. Covers all sliders, not just exam-plan.
4. **PRR-NEW-A11Y-4 (P1)** — Exam-target cards as `role="group"` + `role="checkbox"` multi-select, with max-cap announcement. Reuse for any future card-grid multi-select.
5. **Create the missing PRR-032 file** — the brief references it but `/tasks/pre-release-review/TASK-PRR-032-*` does not exist in the filesystem. Either write it or stop citing it as if it does.

## Blockers / non-negotiables

- **BLOCKER** — VDatePicker he/ar verification (see 9.4 above). Shipping a silently-LTR date picker to Arabic/Hebrew Bagrut students is the reversed-equation bug at a higher surface. Non-launchable.
- **BLOCKER** — Nested-step SR pattern must be specified in ADR-0049. Shipping an undefined "step 4 of 8 + target 2 of 3" is a WCAG 1.3.1 violation against screen-reader users.
- **NON-NEGOTIABLE** — `<bdi dir="ltr">` around every track code ("3U/4U/5U"), every deadline date, every weekly-hours numeric value. This is the math-always-LTR memory; any PR that ships without it gets reverted.
- **NON-NEGOTIABLE** — Primary color stays `#7367F0`. Focus rings meet 3:1 against the actual card background, not the page background. Verify per card state (default, hover, selected).

## Questions back to decision-holder

1. VDatePicker or native `<input type="date">`? Native picker sidesteps the locale-fallback blocker but looks worse and varies by OS. Which trade-off wins?
2. Flattened steps (10 total for 3-target user) or nested with inner pagination (8 total + inner 1-of-3)? This shapes ADR-0049's UX contract.
3. Is per-target free-text note worth keeping given combined privacy + a11y + l10n cost? I'd drop it.
4. PET verbal sections are language-native (§4) — does that include the onboarding diagnostic for PET? If a student picks PET-Hebrew and the onboarding diagnostic is English, SR narration will language-mismatch. Worth a separate finding in persona-educator or persona-cogsci.
