---
id: prr-a11y-semantics-shortcut
parent: prr-a11y-toolbar-enrich
priority: P2
tier: mvp
tags: [a11y, wcag-2-1-aa, keyboard, screen-reader]
---

# TASK-PRR-A11Y-SEMANTICS-SHORTCUT — Alt+A, aria-live announce, skip-to-toolbar

## Context

Follow-up #4 from `prr-a11y-toolbar-enrich` close-out (2026-04-21).

## Scope

- **Alt+A keyboard shortcut**: opens the toolbar from any layout.
  Register alongside ShellShortcuts so it's discoverable in the
  existing cheatsheet. Must not conflict with native OS / browser
  bindings on Windows / macOS / Linux; consider a configurable prefix
  (Alt+Shift+A as fallback).
- **Per-preference aria-live announce**: on any toggle or slider
  change, emit a polite live-region message ("Text size: Large"). The
  announce element is visually hidden but screen-reader reachable.
- **Skip-to-toolbar link**: extend the existing page-top skip-link to
  include "Skip to accessibility toolbar" as a tertiary target.

## Definition of done

- Alt+A opens the drawer from the handle's current `focus-trap` state.
- Announce element has `role="status"` + `aria-live="polite"` and
  debounces bursts (slider drag should announce final value, not
  every tick).
- Manual screen-reader sweep (NVDA on Windows, VoiceOver on macOS)
  confirms announcements fire and the skip-link lands focus.

## Non-negotiables

- No modifier-key conflict with OS screen-reader shortcuts.
- No motion on announce element (prefers-reduced-motion respected).
- Announce text stays in the active locale (no EN fallback leakage).
