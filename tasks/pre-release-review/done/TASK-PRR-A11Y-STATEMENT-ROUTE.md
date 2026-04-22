---
id: prr-a11y-statement-route
parent: prr-a11y-toolbar-enrich
priority: P1
tier: mvp
tags: [a11y, legal, il-reg-5773-2013, content]
---

# TASK-PRR-A11Y-STATEMENT-ROUTE — `/accessibility-statement` route + content

## Context

Follow-up #1 filed from `prr-a11y-toolbar-enrich` close-out
(2026-04-21). The A11yToolbar footer now links to
`/accessibility-statement`, but the route target is not yet authored.

IL Reg 5773-2013 §35 requires a published accessibility statement on
every public-facing site. Minimum content per §35(b):
(1) declared conformance level (we target WCAG 2.1 AA),
(2) list of known exceptions / remediation plan,
(3) assistive tech tested with (NVDA, JAWS, VoiceOver),
(4) contact method (we use `accessibility@cena.app`).

## Scope

- New page component `src/student/full-version/src/pages/accessibility-statement.vue`.
- Localized content in en / ar / he under
  `src/student/full-version/src/plugins/i18n/locales/*.json`
  (new top-level key `accessibilityStatement.*`).
- Route registered via the file-based router (pages plugin auto-picks
  it up from the filename).
- Render in the default layout (authenticated) AND an unauthenticated
  variant mounted under `/public/accessibility-statement` so the page
  is reachable without login — required by §35 for pre-auth surfaces.

## Definition of done

- Both routes reachable (auth + unauth) without crashing.
- Ship-gate banned-mechanics scanner green on all three locales.
- Keyboard-accessible headings (H1 → H2 → H3 hierarchy, no skips).
- Last-updated date displayed and pulled from a constant so the legal
  team can bump it on review.

## Non-negotiables

- ≤500 LOC.
- No dark-pattern copy (no streaks, no loss-aversion).
- Hebrew is a hideable locale — don't hard-code visibility.
