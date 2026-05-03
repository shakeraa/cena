---
id: FIND-ARCH-017
task_id: t_37119818c91a
severity: P0 — Critical
lens: arch
tags: [reverify, arch, stub]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-arch-017: MSW intercepts /api/* in production student-web builds

## Summary

MSW intercepts /api/* in production student-web builds

## Severity

**P0 — Critical**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

**Goal**: Stop MSW from intercepting `/api/*` calls in production
student-web builds. The student app currently ships with mock data
overriding real backend calls because the fake-api plugin's
`worker.start()` is invoked unconditionally at app boot.

**Files to read first**:
  - src/student/full-version/src/plugins/fake-api/index.ts
  - src/student/full-version/src/@core/utils/plugins.ts
  - src/student/full-version/vite.config.ts
  - src/student/full-version/.gitignore

**Files to touch**:
  - src/student/full-version/src/plugins/fake-api/index.ts
    (gate `worker.start()` behind `import.meta.env.DEV`)
  - src/student/full-version/vite.config.ts
    (add a build-mode exclusion so the fake-api dir is not bundled)
  - src/student/full-version/.gitignore
    (re-enable `public/mockServiceWorker.js` line; add a dev-only
     postinstall to generate it)
  - tests/e2e/student-web/no-msw-in-production.spec.ts (new)

**Definition of Done**:
  - [ ] `npm run build` produces a `dist/` bundle that has zero
        references to `setupWorker`, `fake-api`, or `mockServiceWorker`.
        Verify with `grep -r 'setupWorker\|fake-api' src/student/full-version/dist/`.
  - [ ] `npm run preview` followed by manual `/api/me` request
        returns the real backend's 401 (not the MSW mock 200).
  - [ ] New Playwright e2e test asserts the above.
  - [ ] FIND-ux-005's "no stub leakage" guard script still passes.
  - [ ] Dev loop (`npm run dev`) still uses MSW for offline work.

**Reporting requirements**:
  - In your --result, paste the byte-size delta on `dist/assets/index-*.js`
    before and after the fix (it should shrink by the size of all
    student-* MSW handlers, ~50–200 KB).
  - Paste the new e2e test output and the curl/network log showing
    the unmocked /api/me hitting the real backend.

**Reference**: FIND-arch-017 in docs/reviews/agent-arch-reverify-2026-04-11.md


## Evidence & context

- Lens report: `docs/reviews/agent-arch-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_37119818c91a`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
