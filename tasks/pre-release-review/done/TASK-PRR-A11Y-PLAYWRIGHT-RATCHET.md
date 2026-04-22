---
id: prr-a11y-playwright-ratchet
parent: prr-a11y-toolbar-enrich
priority: P2
tier: mvp
tags: [a11y, testing, playwright, architecture-test]
---

# TASK-PRR-A11Y-PLAYWRIGHT-RATCHET — Smoke + layout-wrapper ratchet

## Context

Follow-up #5 from `prr-a11y-toolbar-enrich` close-out (2026-04-21).
The parent task's "Verification gate" section is deferred here so the
MVP can ship without blocking on Playwright scaffolding.

## Scope

- **Playwright smoke test** (cross-layout):
  - Toolbar opens from every layout (default / auth / blank).
  - Language radio flips EN → HE → AR, confirming
    `document.documentElement.dir` flips RTL/LTR accordingly.
  - Text-size slider value survives a full route navigation.
  - `data-a11y-*` attributes on `<html>` reflect drawer state after
    any change.
  - Numerals radio flip updates KaTeX output on a math-bearing page.
- **Architecture test** (ratchet):
  `A11yToolbarMountedOnEveryLayoutTest` — scans
  `src/layouts/*.vue` and fails CI if any layout renders primary
  content without `<A11yToolbar />` somewhere in the template.
  Authored as a Vitest `describe.each` over the layout files.

## Definition of done

- Both test files committed and green on CI.
- Failing case demonstration (temporarily remove toolbar from one
  layout → ratchet fails → restore → ratchet passes).

## Non-negotiables

- Playwright trace on failure (standard Cena config).
- No flaky test marked `test.skip` — either stabilize or don't ship.
- No new dev-dependency unless declared in `--result` upon completion.
