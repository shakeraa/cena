# EPIC-E2E-L — Accessibility & i18n flow-level checks

**Status**: Proposed
**Priority**: P1 (Ministry-of-Education + WCAG compliance is a contractual obligation for institute-tier)
**Related**: [Memory: Math always LTR](../../.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_math_always_ltr.md), [Memory: Language Strategy](../../.claude/projects/-Users-shaker-edu-apps-cena/memory/feedback_language_strategy.md), existing `tests/e2e/a11y-toolbar-ratchet.spec.ts` + `rtl-visual-regression.spec.ts`

---

## Why this exists

Existing `tests/e2e/` covers UI-only a11y / RTL regressions. This epic adds the **flow-level** variants — does the subscribe flow work end-to-end in Arabic / Hebrew? Does parent-digest email render RTL correctly? Does CAS-verified math stay LTR inside a Hebrew session?

## Workflows

### E2E-L-01 — Full subscription flow in Arabic

**Journey**: set locale = `ar` → run EPIC-E2E-B-01 subscription happy path → every screen renders RTL, cycle toggle labels localized, confirm page localized.

**Boundaries**: `document.dir === 'rtl'` throughout, no raw i18n keys rendered (`pricing.tier.plus.title` not visible), KaTeX math segments (if any in pricing copy) inside `<bdi dir="ltr">`.

**Regression caught**: dir attribute not set; raw i18n key leaks; RTL bleed into math.

### E2E-L-02 — Full session flow in Hebrew

**Journey**: set locale = `he` → run EPIC-E2E-C-02 practice session → math equations render LTR inside RTL question bodies → hint cards RTL → feedback RTL.

**Boundaries**: `<bdi dir="ltr">` wraps every math segment, question-number ordering correct, KaTeX `\cdot` etc. not mirrored.

**Regression caught**: math mirrored (notorious reversed-equation bug); hint card direction wrong; question number swapped (e.g., "1 שאלה" read backward).

### E2E-L-03 — Parent digest email rendered RTL

**Journey**: set parent locale = `he` → weekly digest triggers → email received → rendered HTML has `dir="rtl"` + `html lang="he"` → child's name rendered correctly → any math excerpt inside `<bdi dir="ltr">`.

**Boundaries**: email HTML source (captured by test SMTP sink), DOM snapshot of email content.

**Regression caught**: LTR template used for RTL audience (catastrophic readability); mixed-direction content not isolated.

### E2E-L-04 — Consent-gated observability respects locale (FIND-privacy-016)

**Journey**: consent dialog renders in ar / he → consent text is authoritative-policy translated (not machine-translated) → flipping consent in each locale appends correct event to ConsentAggregate.

**Boundaries**: consent text matches the legal-team-approved translation (static assertion), event appended correctly regardless of locale.

**Regression caught**: legal text changed without translation; locale-specific placeholders not filled; event stored with locale-specific string (should be language-neutral key).

### E2E-L-05 — Keyboard-only subscription flow

**Journey**: run EPIC-E2E-B-01 using only keyboard navigation — Tab, Enter, Space, Esc. Every actionable element reachable in logical order.

**Boundaries**: tab order matches visual order, focus indicator always visible (not `outline: 0`), skip-to-content link works, modal focus traps correctly.

**Regression caught**: focus lost after modal close; tab order jumps into admin-only elements; skip-link broken.

### E2E-L-06 — Screen-reader-friendly session flow (aria-live)

**Journey**: run EPIC-E2E-C-02 with aria-live polite regions in place — correct/wrong feedback, hint surfacing, session-end summary all announced.

**Boundaries**: `[aria-live]` regions present + correct politeness level, no `aria-hidden` on interactive elements.

**Regression caught**: aria-live region swallowed by a stacking wrapper; politeness level wrong (assertive where polite is right or vice versa); interactive element inside aria-hidden tree.

### E2E-L-07 — High-contrast mode + Vuexy theme integration

**Journey**: user picks a11y toolbar → high-contrast mode on → primary-color ratchet satisfies WCAG AA on every surface (session, pricing, parent dashboard).

**Boundaries**: contrast ratio assertion (axe + AxeBuilder.with('wcag2aa')), primary color locked per memory (Vuexy #7367F0 — use pattern not alternate color).

**Regression caught**: contrast regression on new page; a11y toolbar bypassed on a specific flow; color-only information (e.g., red/green feedback without icon).

## Out of scope

- Static-page a11y (covered by the existing `tests/e2e/a11y-*.spec.ts`)
- Automated translation quality — legal-team review is out-of-band

## Definition of Done

- [ ] 7 workflows green
- [ ] RTL visuals assertions on every flow's critical screen (at least 1 per epic A-G cross-linked here)
- [ ] L-03 (parent digest email RTL) tagged `@i18n @parent` since the email is the only surface outside the SPA
- [ ] axe `wcag2aa` clean on every flagship landing page
