---
id: prr-a11y-expanded-controls
parent: prr-a11y-toolbar-enrich
priority: P2
tier: mvp
tags: [a11y, wcag-2-1-aa, enrichment]
---

# TASK-PRR-A11Y-EXPANDED-CONTROLS — Color-blind / reading guide / line-height / etc.

## Context

Follow-up #3 from `prr-a11y-toolbar-enrich` close-out (2026-04-21).
The parent task's "expanded a11y controls" section is deferred to a
dedicated follow-up to keep the MVP toolbar under the 500-LOC cap and
focused on the IL-legal-compliance hotspots (language +
statement link).

## Scope

- **Color-blind modes**: protanopia / deuteranopia / tritanopia filter
  toggles. CSS filter applied on `<main>` so the PRR-209 heatmap
  remains distinguishable. Toolbar copy clarifies "simulates what a
  color-blind student sees" for teacher self-audit.
- **Reading guide**: thin horizontal ruler following the cursor
  (toggle on/off). Helps ADHD-type tracking.
- **Underline-all-links**: global override for hover-only underlines.
  WCAG 2.4.7 focus visibility.
- **Cursor magnifier**: boost cursor size via CSS `cursor: url(...)`
  with a stepped 24/32/48 px choice.
- **Line-height slider**: expose 1.2–2.0 slider for all locales
  (currently AR hard-codes 1.6).

Each lands as an additional toggle / slider in A11yToolbar, extra
`data-a11y-*` attributes on `<html>`, and matching SCSS rules in
`assets/styles/styles.scss`.

## Definition of done

- Each preference round-trips through a11yStore + localStorage + DOM
  attribute.
- Unit tests covering store mutations and attribute application.
- Playwright smoke confirming toolbar controls work across layouts.

## Non-negotiables

- A11yToolbar.vue ≤500 LOC — split into partial child components if
  needed.
- No hue shift on primary #7367F0 when contrast toggles interact.
- Ship-gate green on all new copy in en / ar / he.
