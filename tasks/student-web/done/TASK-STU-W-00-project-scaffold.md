# TASK-STU-W-00: Project Scaffold

**Priority**: HIGH — blocks every other student-web task
**Effort**: 2-3 days
**Phase**: 1
**Depends on**: [DB-06](../../docs/tasks/infra-db-migration/TASK-DB-06-split-hosts.md) (new student host exists to point the dev server at)
**Backend tasks**: none
**Status**: Not Started

---

## Goal

Stand up a new Vue 3 + Vuetify SPA under `src/student/full-version/`, copied from the Vuexy starter shell but **independent** from the admin app, with Vite, TypeScript, Pinia, file-based routing, i18n, and CI wired.

## Spec

Full architecture spec lives in [docs/student/00-overview.md](../../docs/student/00-overview.md). This task implements the stack + tooling foundation only; feature work happens in later tasks.

Key decision carried over from the session discussion:
- **Copy, don't share**. The student app is a consumer product and will drift from the admin tool by design. Fork the Vuexy starter into its own directory; do not extract a shared package with admin.

## Scope

In scope:

- `src/student/full-version/` directory copied from Vuexy starter (not from admin pages)
- `package.json` with distinct name `cena-student-web`
- Vite config on port `5175` (admin uses `5174`)
- TypeScript strict mode, `tsconfig.json` matching admin conventions
- `unplugin-vue-router` for file-based routing under `src/pages/`
- Pinia root store
- `vue-i18n` wired with empty `en.json`, `ar.json`, `he.json` stubs
- `@microsoft/signalr`, `ofetch`, `@iconify-json/tabler`, `katex`, `@vueuse/core` installed but not yet wired (later tasks)
- ESLint + Prettier configured to match admin repo style
- Vitest installed with one example passing test
- Playwright installed with one example passing test targeting the dev server
- Dockerfile for nginx-based production serving (multi-stage: `node:20` build, `nginx:alpine` runtime)
- `.env.example` with `VITE_API_BASE`, `VITE_HUB_URL`, `VITE_FIREBASE_*` placeholders
- `README.md` with "how to run", "how to test", "how to build"
- Registration in root `package.json` workspaces if the repo uses them

Out of scope:

- Any pages beyond a placeholder `index.vue` and a `/404.vue`
- Auth wiring (STU-W-02)
- Design tokens beyond the raw Vuexy defaults (STU-W-01)
- SignalR client code (STU-W-03)
- Backend endpoints (any `STB-*` task)

## Definition of Done

- [ ] `cd src/student/full-version && npm install && npm run dev` opens a working Vuexy starter on port 5175
- [ ] `npm run build` produces a production bundle
- [ ] `npm run lint` passes
- [ ] `npm run test:unit` passes the example test
- [ ] `npm run test:e2e` passes the example Playwright test
- [ ] Dockerfile builds and `docker run` serves the bundle on port 8080
- [ ] CI workflow runs lint + unit tests + E2E tests on every PR touching `src/student/full-version/**`
- [ ] README has working setup instructions
- [ ] The admin app under `src/admin/full-version/` is untouched
- [ ] No shared package was extracted (explicit non-goal)
- [ ] PR description references the "copy, don't share" decision from the session discussion

## Cross-Cutting Concerns (from bundle README)

All the cross-cutting concerns apply from day one. Even the placeholder page should:

- Use an `<EmptyState>` component (even if just a stub)
- Have a dark-mode switch wired
- Pass axe-core with zero violations
- Render RTL when the locale is `ar` or `he`

## Risks

- **Vuexy licensing / provenance** — confirm the admin's Vuexy starter can be copied to a second directory under the same license. Resolve before starting.
- **Monorepo tooling** — if the repo uses npm workspaces, pnpm, or yarn, match the admin app's choice. Do not introduce a second package manager.
- **Node version drift** — lock to the same Node version the admin uses (`.nvmrc` or `engines` field).
