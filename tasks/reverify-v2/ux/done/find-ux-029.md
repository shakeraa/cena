---
id: FIND-UX-029
task_id: t_75bfec46c698
severity: P1 — High
lens: ux
tags: [reverify, ux, pwa]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-ux-029: PWA manifest icon /images/logo.png is missing — Vite returns HTML, browser rejects

## Summary

PWA manifest icon /images/logo.png is missing — Vite returns HTML, browser rejects

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

## FIND-ux-029: PWA manifest icon /images/logo.png is missing — Vite returns HTML, browser rejects

**Severity**: p1
**Lens**: ux
**Category**: ux

## Files
- src/student/full-version/public/manifest.webmanifest (or `/manifest.json`, find via `find src/student/full-version/public -name 'manifest*'`)
- src/student/full-version/public/images/  (need to create logo.png + logo-512.png)
- vite.config.ts (add a manifest-icon-validation plugin)

## Evidence
- `curl -sI http://localhost:5175/images/logo.png` → `Content-Type: text/html` (Vite SPA fallback) and 200 (the file does NOT exist).
- `ls src/student/full-version/public/images/logo.png` → No such file or directory.
- Console: `[WARNING] Error while trying to use the following icon from the Manifest: http://localhost:5175/images/logo.png (Download error or resource isn't a valid image)` on every page load.
- Manifest: declares `icons: [{ src: '/images/logo.png', sizes: '192x192', type: 'image/png', purpose: 'any' }, { src: '/images/logo.png', sizes: '512x512', ... }]`.

## Definition of Done
1. `curl -I http://localhost:5175/images/logo.png` returns `Content-Type: image/png` and a 200.
2. The manifest icon at 192×192 and 512×512 are real PNG files derived from the brand SVG.
3. The Chrome Application > Manifest panel in DevTools shows valid icons (no "Download error").
4. Add a vitest test under `tests/build/manifest.spec.ts` that reads the manifest and validates every icon resolves with a 200 + `image/*` Content-Type.
5. Same check applied to the admin host's manifest at port 5174.

## Source
docs/reviews/agent-ux-reverify-2026-04-11.md#FIND-ux-029


## Evidence & context

- Lens report: `docs/reviews/agent-ux-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_75bfec46c698`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
