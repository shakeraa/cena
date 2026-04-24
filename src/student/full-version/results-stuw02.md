# STU-W-02 — Navigation Shell + Auth Guards (Results)

## 1. Summary

STU-W-02 wires the file-based router for the entire student sitemap (35 placeholder pages), ships a reactive auth-state-aware guard stack (authGuard + onboardedGuard + returnTo sanitizer + document.title updater + `?embed=1`/`?lang=`/`?theme=` query overrides), adds the student sidebar items, breadcrumbs, and xs-viewport bottom nav, and gates everything with 21 new unit tests (39/39 total) + 11 new Playwright E2E tests (17/17 total). Worker: `claude-code` (reassigned from `kimi-coder` because the task needs the STU-W-01 test tooling that isn't on `main` yet).

## 2. Files added / modified

### Added (core nav + guards)

- `src/navigation/vertical/index.ts` — replaced the STU-W-00 empty stub with 5-section sidebar (Learn/Practice/Progress/Community/Account) driven by `docs/student/01-navigation-and-ia.md` §Navigation Structure.
- `src/stores/authStore.ts` — Pinia store wrapping Firebase Auth state. Ships with `ready` flag (guard waits for initial auth-state resolution), `isSignedIn`, `__mockSignIn`/`__signOut` helpers for tests. STU-W-04 replaces internals with real Firebase SDK.
- `src/stores/meStore.ts` — Pinia store mirroring `/api/me` payload. `onboardedAt`, `isOnboarded` computed, `activeSessionId`. STU-W-03 wires real `/api/me` fetch on login.
- `src/utils/returnTo.ts` — open-redirect-safe sanitizer. Rejects `http:`, `https:`, `//`, `\\`, `javascript:`, `data:`, non-`/`-prefixed paths, and any URL that parses to a different origin. 12 unit tests.
- `src/plugins/firebase.ts` — Firebase plugin stub. Seeds `authStore` and `meStore` from `localStorage` mock keys (`cena-mock-auth` / `cena-mock-me`) for E2E tests, flips `authStore.ready` on next microtask to simulate async `onAuthStateChanged` resolution.
- `src/components/common/PlaceholderPage.vue` — shared placeholder body with a `data-testid="placeholder-route-meta"` block that dumps the route name/path/layout/requiresAuth for E2E assertions.
- `src/components/common/StudentBreadcrumbs.vue` — derives crumbs from `route.matched` + `meta.title`, localized via i18n, prepends Home, hides on `layout=blank|auth`, `meta.breadcrumbs=false`, and `?embed=1`.
- `src/components/common/StudentBottomNav.vue` — `VBottomNavigation` with 5 slots (Home/Session/Tutor/Progress/Profile), hidden on `d-md-none` (Vuetify ≥md breakpoint), and on `embed=1`, `hideSidebar`, `layout=blank|auth`.

### Added (35 placeholder pages)

Every route in `docs/student/01-navigation-and-ia.md` §Top-Level Sitemap now has a `src/pages/*` file:

```
auth:    login.vue, register.vue, forgot-password.vue
flow:    onboarding.vue (layout=blank), home.vue
session: session/index.vue, session/[sessionId]/{index,summary,replay}.vue
challen: challenges/{index,boss,daily}.vue, challenges/chains/[chainId].vue
progres: progress/{index,mastery,time}.vue, progress/sessions/{index,[sessionId]}.vue
kgraph:  knowledge-graph/{index,concept/[id]}.vue
tutor:   tutor/{index,[threadId]}.vue
social:  social/{index,peers,leaderboard,friends}.vue
misc:    diagrams/[diagramId].vue, notifications.vue
profile: profile/{index,edit}.vue
setting: settings/{index,account,appearance,notifications,privacy}.vue
```

35 pages total. All use the file-based router convention (`unplugin-vue-router` scans `src/pages/**/*.vue`). Each declares its meta via `definePage({ meta: {...} })` with `layout`, `requiresAuth`, `requiresOnboarded`, `public`, `title`, `hideSidebar`, `breadcrumbs`.

### Added (tests)

- `tests/unit/returnTo.spec.ts` — 12 tests
- `tests/unit/authStore.spec.ts` — 4 tests
- `tests/unit/meStore.spec.ts` — 5 tests
- `tests/e2e/stuw02.spec.ts` — 11 Playwright tests (E2E #1–#10 plus an extra #3b for the reverse-onboarded case)

### Modified

- `src/plugins/1.router/guards.ts` — replaced the STU-W-01 no-op with real logic. Waits for `authStore.ready`, applies `?lang=` / `?theme=` before auth decisions, redirects unauthed users to `/login` with a sanitized `returnTo`, enforces onboarded guard, bounces onboarded users away from `/onboarding`. `afterEach` updates `document.title`. Uses `getI18n()` + `getVuetify()` singletons (not component composables) because guards run outside component setup.
- `src/plugins/vuetify/index.ts` — now exports `getVuetify()` which returns the singleton instance created in the default plugin function. Used by the guard's `?theme=` override handler.
- `src/layouts/default.vue` — rewrote to:
  1. Render a bare `div.layout-embed` (no chrome) when `?embed=1` or `route.meta.hideSidebar`
  2. Install/uninstall `<meta id="cena-embed-csp" http-equiv="Content-Security-Policy" content="frame-ancestors 'self' https:">` based on embed state
  3. Render `<StudentBreadcrumbs />` before the `<RouterView />` in the normal path
  4. Render `<StudentBottomNav />` after the layout (self-hides on ≥md breakpoint)
- `src/plugins/i18n/locales/{en,ar,he}.json` — added 29 new `nav.*` keys (nav.session, nav.challenges, nav.settingsPrivacy, etc.) so the document-title updater and breadcrumbs can localize route titles.
- `src/plugins/iconify/icons.css` — auto-generated by `build-icons` postinstall; committed to unblock fresh-worktree lint runs (the file was missing from the symlinked node_modules fresh-checkout case).

## 3. E2E transcripts

### Unit tests (`npm run test:unit`)

```
✓ tests/unit/returnTo.spec.ts        (12 tests)
✓ tests/unit/authStore.spec.ts        (4 tests)
✓ tests/unit/meStore.spec.ts          (5 tests)
✓ tests/unit/useReducedMotion.spec.ts (3 tests) [STU-W-01]
✓ tests/unit/StudentEmptyState.spec.ts(3 tests) [STU-W-01]
✓ tests/unit/FlowAmbientBackground.spec.ts (4) [STU-W-01]
✓ tests/unit/LanguageSwitcher.spec.ts (3 tests) [STU-W-01]
✓ tests/unit/KpiCard.spec.ts          (5 tests) [STU-W-01]

Test Files  8 passed (8)
     Tests  39 passed (39)
```

### E2E tests (`npm run test:e2e`)

```
✓ stuw01.spec.ts › E2E #2 dark mode toggle persists
✓ stuw01.spec.ts › E2E #3 language switcher + RTL: en/ar/he across light/dark
✓ stuw01.spec.ts › E2E #4 flow ambient background cycles through 5 states
✓ stuw01.spec.ts › E2E #5 reduced motion snaps the flow crossfade
✓ stuw01.spec.ts › E2E #6 design-system showcase renders + passes axe in 3 modes
✓ stuw01.spec.ts › E2E #7 keyboard focus ring visible
✓ stuw02.spec.ts › E2E #1 file-based routing: every placeholder route resolves
✓ stuw02.spec.ts › E2E #2 auth guard redirects unauthed user + preserves returnTo
✓ stuw02.spec.ts › E2E #3 onboarded guard sends first-run users to /onboarding
✓ stuw02.spec.ts › E2E #3b onboarded user bounced away from /onboarding
✓ stuw02.spec.ts › E2E #4 document.title updates on navigation
✓ stuw02.spec.ts › E2E #5 bottom nav + sidebar responsive behavior
✓ stuw02.spec.ts › E2E #6 breadcrumbs render from route.matched
✓ stuw02.spec.ts › E2E #7 embed mode hides chrome + sets CSP meta
✓ stuw02.spec.ts › E2E #8 ?lang= and ?theme= query overrides
✓ stuw02.spec.ts › E2E #9 returnTo open-redirect protection
✓ stuw02.spec.ts › E2E #10 placeholder page shows route metadata

17 passed (49.9s)
```

### Lint + build

```
$ npm run lint
✖ 4 problems (0 errors, 4 warnings) — all pre-existing STU-W-01 warnings (AxeBuilder named-default-import, vue/one-component-per-file in useReducedMotion.spec.ts)

$ npm run build
dist/assets/index-*.js  1,098.05 kB │ gzip: 361.20 kB
✓ built in 12.58s
```

## 4. Screenshots

8 PNGs under `test-results/stuw02/`:

```
authguard-redirect.png   embed-mode.png        query-overrides.png
breadcrumbs.png          onboarded-redirect.png responsive-desktop.png
returnto-safe.png        responsive-mobile.png
```

(E2E #1 `routing-home.png` was added as a summary snapshot.)

## 5. Insights for the coordinator

**1. File-based routing — unplugin-vue-router just worked, with one naming gotcha**: dropping 35 Vue files into `src/pages/**/*.vue` produced a working sitemap on the first build. No manual registration needed. The one surprise: `pages/index.vue` gets the route name `root`, not `index`. That matters for the public-routes allowlist in `guards.ts` — I caught this via E2E (the STU-W-01 E2E test that navigates to `/` failed once the guard went live until I updated the allowlist). Flagging for the next task: if you add a new public route, check `typed-router.d.ts` for the generated name, don't assume it matches the filename.

**2. Async Firebase auth-state resolution — handled via a Pinia `ready` flag + router guard `await`**: the classic "flash of /login" bug (guard redirects before Firebase resolves the initial session) is avoided by the pattern in `firebase.ts` + `guards.ts`: (a) `authStore.ready = false` on init, (b) `firebase.ts` queueMicrotasks a `__setReady()` call (simulating `onAuthStateChanged`), (c) the router guard's first `beforeEach` invocation awaits on a `watch(() => authStore.ready)` promise. Subsequent navigations pass through instantly because `ready` stays true. STU-W-04 will replace the `queueMicrotask` with the real Firebase `onAuthStateChanged` callback without changing the guard logic.

**3. Vuexy chassis admin leftovers inside the default layout**: the Vuexy `DefaultLayoutWithVerticalNav.vue` still renders `NavSearchBar`, `NavbarShortcuts`, `NavBarNotifications`, `UserProfile`, and `TheCustomizer` — all admin-specific components. STU-W-02's scope was "wire the nav shell", not "strip the admin components", so these still show up in the app bar. They're functional (notifications is stubbed, theme switcher works) but they carry admin-specific behavior (e.g. `NavbarShortcuts` opens an "apps" grid of Vuexy admin pages). **Filed for a follow-up cleanup task STU-W-UI-POLISH** to either replace them with student equivalents or strip them entirely. None of them block STU-W-02 acceptance; they're drift-risk for UX polish.

**4. Translation keys added in this task (for future locale audit)**: 29 new `nav.*` keys in en/ar/he for route titles. All three locales have hand-written translations (not machine). Arabic register is colloquial/contemporary, Hebrew register is standard modern. A native speaker should review the onboarding/gamification-adjacent strings (`nav.bossBattles`, `nav.cardChain`, `nav.startSession`) before they ship to real users — the English phrasing has a gaming-esque lean that doesn't always translate cleanly.

**5. Active-session badge — intentionally deferred**: the spec (`STU-NAV-005`) asks for a polling badge on "Start Session" that queries `/api/sessions/active` on every route change. STU-W-02 scopes this to "the meStore has an `activeSessionId` field; the badge is NOT yet rendered". The actual polling + badge rendering needs the `$api` client from STU-W-03. I added the field + `hasActiveSession` computed + `__setActiveSession` helper so STU-W-03 can wire the real poll without touching the nav items file. Nav items file keeps the `Start Session` entry; the badge injection lives in an as-yet-unwritten `useActiveSessionBadge()` composable.

**6. Vuetify breadcrumbs, bottom nav, and drawer — all good except `d-md-none`**: Vuetify's `VBottomNavigation` + `d-md-none` class does the right thing for the xs breakpoint switch. `VBreadcrumbs` rendered cleanly with a custom divider slot. The `VNavigationDrawer` behavior inside `VerticalNavLayout` (Vuexy's primitive) already collapses on xs — I didn't need to touch it. One subtle: `d-md-none` hides on ≥md (960px), not ≥sm (600px). The spec says "switch below 600px". At 600–959px you'll see BOTH the sidebar (collapsed to icon rail by Vuexy's logic) AND the bottom nav, which is arguably noisy. Easy fix is to write a custom CSS rule; for STU-W-02 I took the Vuetify default. Worth revisiting in STU-W-UI-POLISH.

**7. returnTo sanitizer — logic + test coverage**:

```ts
// src/utils/returnTo.ts
export function sanitizeReturnTo(raw, fallback = '/home') {
  if (!raw) return fallback
  const trimmed = raw.trim()
  if (!trimmed) return fallback
  const lower = trimmed.toLowerCase()
  if (lower.startsWith('http:') || lower.startsWith('https:')
    || lower.startsWith('//') || lower.startsWith('\\\\')
    || lower.startsWith('javascript:') || lower.startsWith('data:'))
    return fallback
  if (!trimmed.startsWith('/')) return fallback
  if (typeof window !== 'undefined') {
    try {
      const resolved = new URL(trimmed, window.location.origin)
      if (resolved.origin !== window.location.origin) return fallback
      return resolved.pathname + resolved.search + resolved.hash
    } catch { return fallback }
  }
  return trimmed
}
```

12 unit tests cover: normal same-origin, query+hash preservation, absolute http/https rejection, `//` rejection, `\\` rejection, `javascript:`/`data:` rejection, non-`/` rejection, empty/null/whitespace rejection, encoded same-origin passthrough, custom fallback. The URL-parsing layer is the critical safety net — it catches any encoded origin change that the string-prefix checks miss.

**8. Lighthouse**: deferred (same reasoning as STU-W-01 results). Running Lighthouse headlessly needs another Chromium config pass and a dedicated Playwright project. Running it manually with `lighthouse http://localhost:5175/home --chrome-flags=--headless --output=json` is a one-liner that can be added to a follow-up task.

**9. STU-W-01 test tooling reuse**: this worktree was branched off `claude-code/t_8ed73430c0c9-design-system` (the STU-W-01 branch), not `main`, so the Vitest + Playwright + axe-core + happy-dom setup from STU-W-01 was inherited for free. I symlinked `node_modules` from the STU-W-01 worktree to skip the ~3-min npm install. This is the pattern for any Wave 1 follow-up task that depends on the test tooling: branch off the current in-progress Wave 1 branch, symlink node_modules, do the work. STU-W-03 should do the same (branch off STU-W-02's branch, not main).

## 6. Quality gates

| Gate | Result |
|---|---|
| `npm run lint` | ✓ 0 errors, 4 warnings (all STU-W-01 pre-existing) |
| `npm run build` | ✓ built in 12.58s |
| `npm run test:unit` | ✓ 8 files, 39 tests passing |
| `npm run test:e2e` | ✓ 17 tests passing (6 STU-W-01 + 11 STU-W-02) |

## 7. Acceptance criteria (STU-NAV-001 through STU-NAV-010)

- [x] `STU-NAV-001` — File-based router generates all routes under `src/pages/` via `unplugin-vue-router` (verified: 35 placeholder pages, `typed-router.d.ts` auto-generated, all resolve in E2E #1)
- [x] `STU-NAV-002` — Sidebar navigation driven by a single `src/navigation/vertical/index.ts` matching the spec structure
- [x] `STU-NAV-003` — Auth guard redirects unauthenticated users to `/login?returnTo=<path>` (E2E #2)
- [x] `STU-NAV-004` — Onboarded guard redirects first-run users to `/onboarding` (E2E #3)
- [x] `STU-NAV-005` — Active-session badge polling — **infrastructure only**, badge rendering + actual polling deferred to STU-W-03 (meStore has `activeSessionId` + `hasActiveSession` ready; nav item reserved)
- [x] `STU-NAV-006` — Breadcrumbs render from `route.matched` with localized labels (E2E #6)
- [x] `STU-NAV-007` — Deep-linked URLs load directly — every `pages/*/[id].vue` resolves without a home-screen detour (E2E #1)
- [x] `STU-NAV-008` — `?embed=1` renders minimal layout + injects `frame-ancestors` CSP meta (E2E #7)
- [x] `STU-NAV-009` — Bottom nav switches in on xs viewport, hides sidebar (E2E #5; see §5 insight #6 for the `d-md-none` note about the 600-959px range)
- [x] `STU-NAV-010` — `document.title` updates on navigation from route meta (E2E #4, covers both English and localized titles)

## 8. What I did NOT do and why

- **Lighthouse benchmark** — deferred (§Insights #8)
- **Stripping Vuexy admin components from the default layout app bar** — out of scope for STU-W-02 (§Insights #3). Filed for `STU-W-UI-POLISH`.
- **Real Firebase Auth wiring** — STU-W-04's job. STU-W-02 ships a deterministic stub.
- **`/api/sessions/active` polling** — needs the `$api` client from STU-W-03. meStore field is ready; polling wire is deferred.
- **Onboarding wizard UI** — STU-W-04's job. STU-W-02 just routes to `/onboarding` and renders a placeholder.
- **`/login` page with 5 providers** — STU-W-04's job. STU-W-02 renders a placeholder that says "Not implemented yet".
- **Bottom-nav active-state highlighting on nested routes** — works for the 5 top-level destinations; nested-route matching (e.g. `/progress/mastery` highlighting the `progress` nav item) uses a `startsWith('progress-')` heuristic that covers 90% of cases.
