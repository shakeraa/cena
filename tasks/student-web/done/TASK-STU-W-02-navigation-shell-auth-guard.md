# TASK-STU-W-02: Navigation Shell & Auth Guards

**Priority**: HIGH — blocks all feature work that renders inside a layout
**Effort**: 2-3 days
**Phase**: 1
**Depends on**: [STU-W-01](TASK-STU-W-01-design-system-bootstrap.md)
**Backend tasks**: none (uses Firebase Auth client SDK only)
**Status**: Not Started

---

## Goal

Wire the file-based router, sidebar, bottom nav, breadcrumbs, auth guards, and `returnTo` handling so every subsequent feature task can land a page at a route and have it work inside the proper layout with the proper access rules.

## Spec

Full specification lives in [docs/student/01-navigation-and-ia.md](../../docs/student/01-navigation-and-ia.md). All `STU-NAV-*` acceptance criteria in that file form this task's checklist.

## Scope

In scope:

- File-based routing via `unplugin-vue-router` scanning `src/pages/**/*.vue`
- Route `meta` types: `requiresAuth`, `requiresOnboarded`, `layout`, `hideSidebar`, `breadcrumbs`, `title`
- Sidebar navigation driven by a single `src/navigation/vertical/index.ts` matching the structure in the spec
- Bottom nav for `xs` viewport with the 5 core destinations
- Breadcrumbs derived from `route.matched` and `meta.title`, localized
- Global auth guard that checks Firebase auth state and redirects unauthenticated users to `/login?returnTo=<current>`
- Global onboarded guard that checks a Pinia `meStore.onboardedAt` and redirects first-run users to `/onboarding`
- `document.title` updater on every navigation
- Placeholder pages for every top-level route in the sitemap (each just renders a heading + "Not implemented yet" card)
- Active-session badge on the "Start Session" sidebar item — polls `/api/sessions/active` on route change (no-op for now since STB endpoints aren't live; mock the response until STU-W-04 wires the real client)
- `?embed=1` query param renders the `blank` layout and applies CSP `frame-ancestors` via a meta tag
- `?lang=` and `?theme=` query params apply a one-shot override
- Firebase Auth web SDK initialized in a single `src/plugins/firebase.ts`

Out of scope:

- Login / register / forgot-password UI (STU-W-04)
- Onboarding wizard (STU-W-04)
- Real data fetching (STU-W-03 + feature tasks)
- SignalR-driven badge updates (STU-W-03)

## Definition of Done

- [ ] All `STU-NAV-001` through `STU-NAV-010` acceptance criteria from [01-navigation-and-ia.md](../../docs/student/01-navigation-and-ia.md) pass
- [ ] Every route in the sitemap has at least a placeholder page
- [ ] Navigating to a protected route while signed out redirects to `/login?returnTo=<path>` and restores the path on sign-in
- [ ] Navigating to any route updates `document.title` from the route meta
- [ ] Sidebar collapses correctly; bottom nav appears below 600 px width
- [ ] Breadcrumbs render correctly across nested routes and respect localization
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Guard race** — the Firebase auth state is async on boot; the guard must wait for the initial `onAuthStateChanged` resolution before making the first routing decision. Failing to do this causes a flash of `/login`.
- **File-based router hot reload** — `unplugin-vue-router` can get confused by renamed files; document the reset procedure.
- **`returnTo` open-redirect risk** — sanitize `returnTo` to only accept same-origin paths to avoid phishing.
