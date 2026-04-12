---
agent: claude-subagent-ux
lens: ux
role: UX, Accessibility & Broken-Workflow Auditor (v2)
date: 2026-04-11
branch: claude-subagent-ux/cena-reverify-2026-04-11
mode: live
dev_servers:
  admin: http://localhost:5174 (200, real Firebase auth project cena-platform)
  student: http://localhost:5175 (200, MSW + mock auth)
preflight_input: docs/reviews/reverify-2026-04-11-preflight.md
prior_findings_file: docs/reviews/agent-5-ux-findings.md
prior_id_high_water_mark: FIND-ux-018
this_run_id_start: FIND-ux-019
known_open_passthrough:
  - FIND-ux-011 + FIND-ux-012 (t_6b1c4fbe96d2)  # social swallow + tutor `?` key
  - FIND-ux-006c (t_f7bb146a546b)               # forgot-password backend wire-up
notes: |
  Both dev servers reachable. Live driving via Playwright MCP. Lighthouse run
  via npx (Chrome --headless=new). axe-core 4.10 injected at runtime.
  Screenshots, axe JSON, Lighthouse JSON, and network dumps under
  docs/reviews/screenshots/reverify-2026-04-11/ux/.

  v2 expansions covered: WCAG 2.2 AA contrast / keyboard / focus / ARIA /
  touch targets / reduced-motion check, Lighthouse a11y on admin + student
  entry pages, axe-core DOM scans, mobile 375x812 viewport, Hebrew RTL pass.
  No `#7367F0` palette change recommended — fixes are usage-pattern only.
---

## Summary by severity

| sev | count |
|-----|------:|
| p0  | 6 |
| p1  | 9 |
| p2  | 4 |
| p3  | 1 |
| total | **20** |

ID range: `FIND-ux-019` .. `FIND-ux-038`.

## Lighthouse accessibility scores (target ≥ 95)

| Page | URL | Score | PASS/FAIL |
|---|---|---:|---|
| Admin login | http://localhost:5174/login | **84** | FAIL |
| Admin dashboard | http://localhost:5174/dashboards/admin (cold load → bounces to /login) | **84** | FAIL |
| Student login | http://localhost:5175/login | **90** | FAIL |
| Student register | http://localhost:5175/register | **90** | FAIL |
| Student home | http://localhost:5175/home (cold → bounces to /login due to no Firebase persist in headless Chrome) | **90** | FAIL |
| Student forgot-password | http://localhost:5175/forgot-password | **91** | FAIL |

**Every page audited is below the 95 target.** Reports under
`docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/*.json`.

Common failing audits across all pages:
- `aria-prohibited-attr` (the `<div class="v-avatar"…>` with `aria-expanded`/`aria-haspopup` — see FIND-ux-022)
- `color-contrast` (Vuexy `text-primary` `#7367F0` text — see FIND-ux-019)
- `landmark-one-main` (auth pages have no `<main>`)
- `target-size` (admin login eye icon = 16x16, fails 24/44 — FIND-ux-027)
- `heading-order` (admin login: H1 in logo wrapper then H4 in card with no H2/H3 — FIND-ux-026)

## axe-core violation counts

| Page | Viewport | Violations | Critical |
|---|---|---:|---:|
| Student /home (cold + auth) | 1280×800 | 5 | 2 |
| Student /home | 375×812  | 6 | 2 |
| Admin /login | 1280×800 | 5 | 1 |

Reports under `docs/reviews/screenshots/reverify-2026-04-11/ux/axe/*.json`.
The `aria-prohibited-attr` for `vue-devtools panel-entry-btn` is **excluded
from counts** — it is the dev-tools overlay injected by Vite, not product
code.

## Top categories this run

- **stub / Phase-1 leak** (6) — backend stubs leaking through to UX
- **a11y critical** (4) — button-name, role-on-clickable-div, ARIA misuse
- **broken-workflow** (3) — MSW worker race / auth race producing visible 404 alerts
- **label-drift** (3) — i18n translates Vuetify slot defaults; brand name leaks
- **a11y serious** (4) — touch target, contrast, heading order, keyboard reach

---

## Findings

### FIND-ux-019 (p0, category: a11y, framework: WCAG-2.2-AA)
**title**: `text-primary` (`#7367F0`) on white and on `#2f3349` dark surface fails WCAG 2.2 AA contrast (4.26:1 light, 2.91:1 dark) — every "Forgot Password?" link, "Create one" link, and form label that uses the primary color is below threshold.

related_prior_finding: none (new — color is locked but usage pattern was never measured)
file: src/admin/full-version/src/pages/login.vue:210-215  (and 30+ other call-sites of `class="text-primary"` for text)
file: src/student/full-version/src/components/auth/StudentAuthCard.vue and login/forgot/register card footers

evidence:
  - type: lighthouse
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/admin-login.json
    content: |
      "color-contrast" audit score=0
      Element: <a href="/forgot-password" class="text-primary ms-2 mb-1">Forgot Password?</a>
      "Element has insufficient color contrast of 2.91 (foreground color: #7367f0,
      background color: #2f3349, font size: 11.3pt (15px), font weight: normal).
      Expected contrast ratio of 4.5:1"
  - type: lighthouse
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/student-login.json
    content: |
      Same `text-primary` link nodes, both `Forgot your password?` (lhId 1-1-A)
      and `Create one` (lhId 1-3-A).
  - type: evaluate
    content: |
      $eval contrast calc:
      primaryOnWhite (#7367F0 vs #FFFFFF) = 4.26:1   (FAIL: AA needs 4.5)
      primaryOnDark2f3349 (#7367F0 vs #2f3349) = 2.91:1 (FAIL)
      whiteOnPrimary (#FFFFFF vs #7367F0) = 4.26:1   (FAIL on .bg-primary buttons too)
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/07-admin-login.png

impact: Every account-recovery link and every primary-colored text affordance is invisible to low-vision users at the WCAG AA threshold. ALL primary buttons (`bg-primary` with white text) also fail by the same 4.26:1 ratio if they use small white text. Affects WCAG 2.2 AA conformance hard.

root_cause: `text-primary` Vuetify utility class outputs `color: rgb(115, 103, 240)` (i.e. `#7367F0`) on whichever surface it sits on. The Vuexy template was authored against the brand color in isolation without verifying contrast against the actual surfaces it's used on.

proposed_fix: **Do NOT change `#7367F0`** (locked). Fix via usage pattern:
1. For text links (Forgot password, Create one, footer links): wrap them in a tonal `VBtn variant="text"` so the affordance is a button-with-bg, not bare colored text. Vuetify's tonal-text combination renders the text against a 8% primary tonal background which together meets 4.5:1.
2. For form `<label class="v-label text-primary">` (admin login Email/Password label): drop the `text-primary` class — labels should be `text-default` or `text-medium-emphasis`. Primary color on labels is decorative, not semantic.
3. For dark theme: render those text affordances with `color: rgb(165, 156, 245)` (`#a59cf5`) — a 30% lighter shade — which lifts contrast above 4.5 on `#2f3349`. Add a Vuetify theme override for `--v-theme-on-surface-variant` or define a `text-link-dark` class.
4. For all `bg-primary` buttons with small text (≤14pt regular): bump font-size to ≥14pt bold OR ≥18pt regular — under WCAG large-text rule the threshold drops to 3:1, which `#FFFFFF` on `#7367F0` (4.26) passes.

test_required: A jest/vitest test that runs axe-core against the rendered admin login + student login with `runOnly: ['color-contrast']` and asserts zero violations. Plus a contrast snapshot helper that fails CI when any computed primary-text-on-surface combination drops below 4.5.

task_body: |
  ## FIND-ux-019: Fix Vuexy primary color (#7367F0) WCAG 2.2 AA contrast violations via usage-pattern-only changes

  **Scope**: WCAG 2.2 AA. Affected: every page that uses `class="text-primary"` for body-size text, and `bg-primary` buttons with small white text.

  **HARD CONSTRAINT**: `#7367F0` is the LOCKED Vuexy primary. Do NOT propose a palette change. Fix via usage pattern only.

  **Files (start here, expand from grep)**:
  - src/admin/full-version/src/pages/login.vue:210-215 (Forgot Password? link)
  - src/student/full-version/src/components/auth/StudentAuthCard.vue (footer slot links)
  - src/student/full-version/src/pages/login.vue (forgot + register footer links)
  - any `.text-primary` use against text in `src/admin/full-version/src/pages/**/*.vue` and `src/student/full-version/src/pages/**/*.vue` (`grep -rn 'class="text-primary"' src/{admin,student}/full-version/src --include='*.vue'`)

  **Definition of Done**:
  1. axe-core run (in headless Chrome via Lighthouse OR via vitest + jsdom + axe) on admin /login, student /login, student /home, student /forgot-password, student /register reports zero `color-contrast` violations.
  2. Lighthouse a11y score for those 5 pages ≥ 95 (currently 84/90/90/91/90).
  3. Each fixed call-site uses ONE of these patterns (not the original `text-primary` text affordance):
     - Wrapped in `<VBtn variant="text" color="primary">` (button affordance, tonal background lifts contrast above 4.5)
     - Replaced with `text-default` or `text-medium-emphasis` (no decorative primary on body text)
     - Promoted to ≥14pt bold OR ≥18pt regular (large-text rule: 3:1)
     - Dark-theme alternate color `#a59cf5` for those specific text nodes
  4. Add a vitest test `tests/a11y/color-contrast.spec.ts` that boots both apps in jsdom, navigates to each page above, runs axe-core, and asserts zero `color-contrast` violations.
  5. **DO NOT** change `themeConfig.primary` or any reference to `#7367F0`.

  **Reporting**: Branch name `<worker>/find-ux-019-contrast-fix`. In your --result include before/after Lighthouse a11y scores for the 5 pages.

  related_prior_finding: none
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-020 (p0, category: ux, broken-workflow)
**title**: Student web vertical sidebar (desktop layout) is **completely empty** — every nav item is hidden by a CASL `can()` check that always returns false because `userAbilityRules` cookie is never set on student sign-in.

related_prior_finding: none (new — sidebar visibility was not driven live in v1)
file: src/student/full-version/src/@layouts/components/VerticalNavLink.vue:22
file: src/student/full-version/src/@layouts/components/VerticalNavSectionTitle.vue:18
file: src/student/full-version/src/@layouts/plugins/casl.ts:15-25
file: src/student/full-version/src/plugins/casl/index.ts:8-13
file: src/student/full-version/src/stores/authStore.ts:113-120
file: src/student/full-version/src/navigation/vertical/index.ts (the 14 nav items that never render)

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/09-student-empty-sidebar-desktop.png
    note: |
      Desktop 1280×800 view of /tutor with student signed in. Left sidebar
      shows ONLY the "Cena" logo and a collapse pin. ZERO nav items.
  - type: evaluate
    content: |
      document.querySelector('aside.layout-vertical-nav ul.nav-items').outerHTML
      → "<ul class="nav-items">...20× <!--v-if--> placeholders... </ul>"
      All 20 nav items in vertical/index.ts (14 links + 5 section titles + the
      Challenges sub-group) render as v-if'd-out comments.
  - type: evaluate
    content: |
      document.cookie → "cena-admin-language=en; cena-student-language=en"
      No `userAbilityRules` cookie. localStorage has `cena-mock-auth` and
      `cena-mock-me` but no abilities, no role.
  - type: file
    content: |
      VerticalNavLink.vue:22:    v-if="can(item.action, item.subject)"
      casl.ts:15: const can = (action, subject) => {
        const vm = getCurrentInstance()
        if (!vm) return false
        const localCan = vm.proxy && '$can' in vm.proxy
        return localCan ? vm.proxy?.$can(action, subject) : true
      }
      authStore.__mockSignIn never calls `useCookie('userAbilityRules').value = ...`,
      and student login.vue never builds an abilities array. So `$can(undefined, undefined)`
      with an empty MongoAbility returns false → every nav link's v-if is false.
  - type: file
    content: |
      navigation/vertical/index.ts: 14 nav items (Home, Start Session, AI Tutor,
      Challenges, Knowledge Graph, Overview, Session History, Mastery, Learning
      Time, Class Feed, Leaderboard, Peer Solutions, Notifications, Profile,
      Settings) — none of which define `action` or `subject`.

impact: A student signing in on desktop has **no left-sidebar navigation at all**. The only way to reach Tutor / Progress / Settings / Notifications is via direct URL or via the four "Quick actions" cards on /home — which only cover 4 of the 14 routes. Critical routes (Notifications, Settings, Mastery, Knowledge Graph, Class Feed, Leaderboard, Peer Solutions, Profile) are reachable ONLY by URL on desktop. On mobile, the bottom nav at least shows 5 primary tabs (Home, Session, Tutor, Progress, Profile), but the rest are still hidden.

root_cause: The student app inherited Vuexy's CASL-gated `VerticalNavLink` template (`v-if="can(item.action, item.subject)"`) from the admin demo, but the student authentication path (`__mockSignIn` + the future real Firebase wiring in `firebase.ts`) never seeds an abilities array. The student app has no real role-based access — all routes are equally available to any signed-in student — but the layout component still gates every link on a permission system that was never wired up. So `$can(undefined, undefined)` returns false against the empty MongoAbility, and EVERY nav item's `v-if` evaluates to false.

proposed_fix:
1. Either: drop the `v-if="can(item.action, item.subject)"` guards from `VerticalNavLink.vue` and `VerticalNavSectionTitle.vue` for the student layout (the student app has no role-based menu hiding requirements — every signed-in student sees the same nav).
2. OR: in `authStore.__mockSignIn` AND in the future real Firebase login path, set `useCookie('userAbilityRules').value = [{ action: 'manage', subject: 'all' }]` so the empty MongoAbility resolves to "allow everything" and `$can` returns true. Mirror what `useFirebaseAuth.ts` already does in the admin app at line 79.
3. Either fix is one-line. Path (1) is cleaner because the student app does not need CASL at all (no role gates).

test_required: Playwright E2E `tests/e2e/student-sidebar-nav.spec.ts` that signs in as a student and asserts `await page.locator('aside.layout-vertical-nav ul.nav-items > li').count() >= 14`.

task_body: |
  ## FIND-ux-020: Student desktop sidebar is empty — wire CASL or remove the gate

  **Files**:
  - src/student/full-version/src/@layouts/components/VerticalNavLink.vue:22
  - src/student/full-version/src/@layouts/components/VerticalNavSectionTitle.vue:18
  - src/student/full-version/src/stores/authStore.ts (around line 113, __mockSignIn)
  - src/student/full-version/src/plugins/casl/index.ts

  **Goal**: After signing in (mock or real Firebase) on desktop ≥1280px width, the student vertical sidebar must render all 14 nav items + 5 section headings.

  **Pick one fix path** (decide in code review):
  1. **Drop CASL on student**: remove `v-if="can(item.action, item.subject)"` from both files for the student build, OR fork those components into student-specific copies. Justification: student app has no role-based menu hiding; every signed-in student sees the same nav.
  2. **Seed abilities on sign-in**: in `authStore.__mockSignIn` and (when wired) the real Firebase path, write `useCookie('userAbilityRules').value = [{ action: 'manage', subject: 'all' }]` and `ability.update([...])`. Mirror the admin path at `src/admin/full-version/src/composables/useFirebaseAuth.ts:79`.

  **Definition of Done**:
  1. Desktop view of `/home`, `/tutor`, `/progress`, `/settings/notifications` shows the full sidebar with at least 14 nav links + section headings.
  2. Sign-out clears the abilities so a fresh visit to /login does NOT show stale nav.
  3. Add `tests/e2e/student-sidebar-nav.spec.ts` that signs in via the existing E2E mock helper and asserts the count.
  4. axe-core no longer reports `list` violation on the sidebar `<ul>` — currently the empty `<ul>` has `<div>` children (the v-if comments + perfect-scrollbar rails) which axe flags as `Fix all of the following: List element has direct children that are not allowed: div` (see student-home-mobile.json).

  **Reporting**: Branch `<worker>/find-ux-020-student-sidebar`. In --result, paste before/after `await page.locator('aside.layout-vertical-nav .nav-items > li').count()` from a manual Playwright session.

  related_prior_finding: none
  framework: null
  reverify-task: yes

---

### FIND-ux-021 (p0, category: stub, broken-workflow)
**title**: Student MSW service-worker race produces user-visible **`[GET] "/api/me": 404 Not Found`** raw error message on every cold reload of `/home`, `/tutor`, `/social`, etc.

related_prior_finding: none (new — manifests as a P0 because it leaks raw HTTP/path strings into production-grade UX)
file: src/student/full-version/src/plugins/fake-api/index.ts (worker registration timing)
file: src/student/full-version/src/composables/useApiQuery.ts (error formatting that surfaces `${method} "${path}": ${status} ${statusText}` directly to the user)

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/04-student-home-mobile-msw-race-error.png
    note: |
      Mobile 375×812 cold load of /home. Visible alert reads:
        "Could not load your home dashboard"
        "[GET] "/api/me": 404 Not Found"
      Below is the bottom nav. The "Could not load" + raw HTTP path are
      shown to the end user.
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/06-student-home-mobile-hebrew.png
    note: |
      Same race in Hebrew (cena-student-language=he). The header / page
      title / bottom nav all translate to Hebrew, but the inner error
      string still says English `[GET] "/api/me": 404 Not` — the alert
      DOES NOT translate the API path or status string.
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/08-student-tutor-msw-race-404.png
    note: |
      Desktop cold load of /tutor. Same alert pattern with
      `[GET] "/api/tutor/threads": 404 Not Found` plus the empty sidebar
      from FIND-ux-020.
  - type: console
    content: |
      [ERROR] Failed to load resource: the server responded with a status of 404 (Not Found) @ /api/me
      [ERROR] Failed to load resource: the server responded with a status of 404 (Not Found) @ /api/analytics/summary
      [ERROR] Failed to load resource: the server responded with a status of 404 (Not Found) @ /api/analytics/time-breakdown
      [ERROR] Failed to load resource: the server responded with a status of 404 (Not Found) @ /api/social/class-feed
      [ERROR] Failed to load resource: the server responded with a status of 404 (Not Found) @ /api/tutor/threads
  - type: shell
    content: |
      curl -sI http://localhost:5175/api/me
      → HTTP/1.1 404 Not Found  (when MSW worker is not yet active)
      The MSW handlers for /api/me, /api/social/class-feed, /api/tutor/threads
      DO exist (verified at handlers/student-me/index.ts:73, handlers/student-
      social/index.ts:90, handlers/student-tutor/index.ts:160). They just
      aren't registered yet at the moment Vue mounts the page and fires the
      first /api/me request.
  - type: file
    content: |
      Search for the format string that builds the alert text:
      grep -rn '\${method}.*\${path}.*\${status}' src/student/full-version/src
      The `useApiQuery` composable surfaces the raw fetch error message
      verbatim through `error.value.message`, and `home.vue:160-164`
      renders `error.message || t('home.errorState.fallback')` — meaning
      when the message is set, the i18n fallback is bypassed and the raw
      string leaks.

impact:
1. The user-visible error literally contains an HTTP method, a quoted URL path, and the words "404 Not Found" — that is a developer console error masquerading as a user-facing alert. It violates the user's "no labels that lie / no stubs leaking" rule head-on.
2. **The error message does NOT translate**: even with the UI in Hebrew or Arabic, the error string is hard-coded English (and contains an English route path) — i18n leak per FIND-ux-007 (which was previously closed for non-error strings only).
3. The race condition is reproducible on every cold load across the home, tutor, social, settings/notifications, and any other route that fires an `useApiQuery` against the MSW-handled API surface during the very first paint.
4. In production, the MSW worker is NOT registered, so this exact race won't happen — but the SAME `error.message`-render path will surface real backend errors with the same shape (`[GET] "/api/something": 503 Service Unavailable`) directly to learners. That is forbidden by the "labels match data" rule.

root_cause: Two stacked bugs:
1. **MSW handler register-vs-mount race**: `src/student/full-version/src/plugins/fake-api/index.ts` registers the service worker asynchronously, but the Vue app boots and starts firing `/api/*` requests before the worker is ready. The fact that this is dev-only does not make it OK because the visible failure mode is identical to a real production backend outage — and the rendering layer treats both the same way.
2. **Error rendering surfaces the raw error string**: Pages render `{{ error.message }}` directly. The composable should produce a user-safe, i18n-keyed error code (e.g. `error.code = 'home.dashboard_unavailable'`) that the page translates, NOT a developer string with a method + path + status.

proposed_fix:
1. Make MSW worker registration `await`-able: in `src/main.ts` (or wherever the MSW import lives), `await import('./plugins/fake-api').then(m => m.startMsw())` BEFORE `app.mount('#app')`. The MSW boot doc has `worker.start({ onUnhandledRequest: 'bypass' }).then(() => app.mount(...))` exactly for this.
2. Change `useApiQuery` and `useApiMutation` to wrap fetch errors in a typed `ApiError` with `code: string` and `i18nKey: string`, never propagating the raw `error.message` to templates.
3. Update every page that renders `{{ error.message }}` to render `{{ t(error.i18nKey) }}` with a generic fallback like `t('common.errorGeneric')` for unknown codes.
4. Add an integration test that boots the MSW worker, starts a navigation in parallel, and asserts the user never sees a string matching `/\[(GET|POST|PUT|DELETE)\] /` in the rendered DOM.

test_required: Playwright `tests/e2e/student-msw-race.spec.ts` that loads `/home`, `/tutor`, `/social`, `/settings/notifications` from cold and asserts `expect(page.locator('text=/\\[(GET|POST|PUT|DELETE)\\] /')).toHaveCount(0)`.

task_body: |
  ## FIND-ux-021: MSW worker race + raw error message leak

  **Files**:
  - src/student/full-version/src/plugins/fake-api/index.ts (worker boot)
  - src/student/full-version/src/main.ts (mount order)
  - src/student/full-version/src/composables/useApiQuery.ts (error wrapping)
  - src/student/full-version/src/pages/home.vue:160-164 (raw error.message render)
  - src/student/full-version/src/pages/tutor/index.vue (same — VAlert {{ error.message }})
  - src/student/full-version/src/pages/social/index.vue (same)
  - src/student/full-version/src/plugins/i18n/locales/{en,ar,he}.json (add `common.error*` keys)

  **Goal**:
  1. Cold-load any student route — the user must NEVER see a string containing `[GET]`, `[POST]`, `404 Not Found`, or any quoted URL path.
  2. Make MSW boot synchronous-before-mount so the race no longer happens in dev.
  3. Make the error-rendering layer translate via i18n keys instead of raw `error.message`.

  **Definition of Done**:
  1. Cold-load `/home`, `/tutor`, `/social`, `/settings/notifications`, `/profile` on both 1280×800 and 375×812 viewports — no raw HTTP-error strings visible.
  2. Force the backend to error (e.g. shut down MSW handlers manually for the test) — the rendered alert reads "Something went wrong loading your home dashboard. Try again." in EN, an Arabic equivalent in AR, and a Hebrew equivalent in HE. NEVER `[GET] /api/...`.
  3. Add the Playwright test described in `test_required` and wire it into CI.
  4. Add new `common.errorGeneric`, `home.dashboardUnavailable`, `tutor.threadsUnavailable`, `social.feedUnavailable` keys to all three locale files.

  **Reporting**: Branch `<worker>/find-ux-021-msw-race-error-leak`. In --result, paste the network log + a screenshot of the same `/home` cold load showing the polite i18n'd error.

  related_prior_finding: null
  framework: null
  reverify-task: yes

---

### FIND-ux-022 (p0, category: a11y, framework: WCAG-2.2-AA)
**title**: Student web user-profile menu activator is a `<div class="v-avatar">` with `aria-haspopup="menu"` and `aria-expanded="false"` — but the element is NOT a `<button>`, has no `role`, and is unreachable for screen-reader users as a control. Sign-out, profile, and settings are unreachable for a keyboard-only or SR user.

related_prior_finding: FIND-ux-018 (admin password "Append" — same family of Vuetify slot-name leaks and missing roles, but this is a different element)
file: src/student/full-version/src/layouts/components/UserProfile.vue:48-116
file: src/student/full-version/src/@layouts/components/VerticalNav.vue (no role on toggle)

evidence:
  - type: snapshot
    content: |
      Page snapshot a11y tree node:
        - generic [ref=e41] [cursor=pointer]: S
          - status "Badge" [ref=e42]
      ↑ This is the only "menu" affordance in the navbar. NOT a button.
      NOT a menubar. Just a generic with cursor=pointer.
  - type: evaluate
    content: |
      const av = document.querySelector('[data-testid="user-profile-avatar"]')
      → tag: 'DIV', role: null, tabindex: -1, ariaHaspopup: 'menu',
        ariaExpanded: 'false', keyboardReachable: false
  - type: axe
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/axe/student-home.json
    content: |
      "id": "aria-allowed-attr",
      "impact": "critical",
      "target": [".v-avatar"],
      "summary": "ARIA attribute is not allowed: aria-expanded=\"false\""
  - type: file
    content: |
      src/student/full-version/src/layouts/components/UserProfile.vue:48-54
      <VAvatar
        size="38"
        class="cursor-pointer"
        color="primary"
        variant="tonal"
        data-testid="user-profile-avatar"
      >
        <span class="text-caption font-weight-bold">{{ initials }}</span>
        <VMenu
          activator="parent"
          ...

      Vuetify VAvatar renders as a `<div>`. With `activator="parent"` the VMenu
      attaches its open handler to that div and adds aria-haspopup/aria-expanded
      attributes — but those attributes are forbidden on a div with no role
      (axe `aria-allowed-attr`). The avatar is also not in the keyboard tab
      order because tabIndex defaults to -1 on a <div>.

impact:
1. Keyboard-only and screen-reader users **cannot reach** Profile, Settings, or Sign Out from anywhere in the student web. The user-profile menu is the ONLY surface that exposes Sign Out.
2. WCAG 2.1.1 (Keyboard) and 4.1.2 (Name, Role, Value) violations — both Level A.
3. axe-core flags it as a critical violation on every page.
4. The visible "S" initials aren't even labeled as a button (no `aria-label="Account menu"`), so an SR user has no way to discover the menu exists.

root_cause: Vuetify's `<VAvatar>` with `<VMenu activator="parent">` is the wrong primitive for a clickable menu trigger. VAvatar is a presentational component (a `<div>`) and slotting a VMenu into it doesn't promote the avatar to an interactive element. The correct primitive is `<VBtn icon><VAvatar>...</VAvatar></VBtn>` with an explicit `aria-label` on the button.

proposed_fix: Wrap the VAvatar in a VBtn:
```vue
<VBtn
  icon
  variant="text"
  size="38"
  :aria-label="t('nav.userMenu', { name: displayName })"
  data-testid="user-profile-avatar-button"
>
  <VAvatar size="38" color="primary" variant="tonal">
    <span class="text-caption font-weight-bold">{{ initials }}</span>
  </VAvatar>
  <VMenu activator="parent" ...>
    ...
  </VMenu>
</VBtn>
```
The `data-testid="user-profile-avatar"` should stay on the inner avatar so existing E2E tests that target it still work, OR be moved to the button wrapper if the e2e tests need to click it directly.

test_required: Playwright `tests/e2e/a11y-keyboard-nav.spec.ts` that:
1. Tabs through all interactive elements on /home and verifies `[data-testid="user-profile-avatar-button"]` is reachable.
2. Presses Enter/Space on it and verifies the menu opens.
3. Asserts `aria-haspopup="menu"` and `aria-expanded` toggle correctly on a real button element.

task_body: |
  ## FIND-ux-022: User profile menu activator is a div, not a button — wrap in VBtn

  **File**: src/student/full-version/src/layouts/components/UserProfile.vue:48

  **Goal**: Make the user profile menu (Profile / Settings / Sign Out) reachable for keyboard and screen-reader users.

  **Definition of Done**:
  1. Wrap `<VAvatar>` in `<VBtn icon variant="text" :aria-label="t('nav.userMenu', { name: displayName })">`.
  2. Add the `nav.userMenu` i18n key to all three locale files (EN: "Account menu for {name}", AR + HE).
  3. axe-core scan of /home, /tutor, /profile shows ZERO `aria-allowed-attr` violations on the avatar.
  4. Add `tests/e2e/a11y-keyboard-nav.spec.ts` per the test_required block — must reach the avatar via Tab AND open the menu via Enter.
  5. Sign-out flow still works after the wrap (no event-handler regression).

  **Related**: This shares the same root anti-pattern with FIND-ux-018 (admin login "Append" eye icon button). Consider doing both in the same task.

  **Reporting**: Branch `<worker>/find-ux-022-user-profile-button`. In --result, paste the axe diff + a Playwright clip of Tab→Enter opening the menu.

  related_prior_finding: FIND-ux-018
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-023 (p0, category: stub, ux)
**title**: Student `pages/login.vue` accepts ANY password for ANY email except `fail@test.com` — the form fires NO `/api/auth/login` request, NO Firebase call, just `__mockSignIn(...)` locally. This is a Phase-1 stub that the user banned.

related_prior_finding: FIND-ux-006 (forgot-password silent drop) — same anti-pattern, different surface
file: src/student/full-version/src/pages/login.vue:42-109

evidence:
  - type: file
    content: |
      src/student/full-version/src/pages/login.vue:71-101
      // Mock-backend rules for Phase A:
      //   - `fail@test.com` → rejected (wrong credentials)
      //   - `unverified@test.com` → rejected (email not verified)
      //   - any other well-formed email → accepted
      if (payload.email === 'fail@test.com') {
        ...
        return
      }
      // Mock success: synth a UID from the email.
      const uid = `mock-${payload.email.replace(/[^a-z0-9]/gi, '-')}`
      authStore.__mockSignIn({ uid, email: payload.email, displayName: payload.email })
      ...
      await router.replace(target)
  - type: interaction
    content: |
      Filled email=student@cena.local, password=password123, clicked Sign in.
      Network tab: ZERO requests fired except an idle GET /api/me after
      __mockSignIn flipped the page to /home. No /api/auth/login, no
      Firebase popup, no token exchange. Then I changed password to
      "anything" and tried admin@cena.edu / fakepw and got the same result.
  - type: file
    content: |
      The OLD MSW handler at src/student/full-version/src/plugins/fake-api/handlers/auth/index.ts
      defines a POST /api/auth/login that validates against an in-memory db
      (the Vuexy demo handler from line 9-50). The student login.vue
      DOES NOT CALL IT. So the in-memory db, the password field, and the
      MSW handler are all inert.

impact:
1. Any tester clicking "Sign in" with a typo'd password lands on /home with a fabricated UID and a stub identity. The form is performative.
2. Any E2E test the team writes against this login is asserting nothing about authentication.
3. The user's locked rule is "NO stubs — production grade" (memory file `feedback_no_stubs_production_grade.md`, 2026-04-11). Phase-1 stubs are banned.
4. When the real Firebase wiring lands (STU-W-04), the entire `if (payload.email === 'fail@test.com')` branch and the `__mockSignIn` synth-uid path become orphan code that future contributors will copy-paste.

root_cause: STU-W-02 shipped a "stub auth that lets you click through the app". STU-W-04 was supposed to replace it with real Firebase. STU-W-04 has not yet landed for the student app, but the user has now globally banned the Phase-1 stub pattern.

proposed_fix:
1. Wire `signInWithEmailAndPassword(firebaseAuth, ...)` directly into the student login.vue, mirroring `src/admin/full-version/src/composables/useFirebaseAuth.ts` which already does this for the admin app against the SAME `cena-platform` Firebase project.
2. Either provision a `STUDENT` role in Firebase custom claims (with no admin-privileges check on the student app side) or skip the role check.
3. Delete the `if (payload.email === 'fail@test.com')` branch — let real Firebase return real error codes.
4. Delete the `__mockSignIn` path entirely (and the `__mockSignIn` function on authStore) so there's no temptation to keep using it.
5. The dev experience should be: real Firebase emulator (`firebase emulators:start --only auth`) for local dev, real cena-platform Firebase for staging/prod.

test_required: Playwright E2E `tests/e2e/student-real-auth.spec.ts` that hits the running Firebase emulator with a known student@test.com / password and asserts (a) `/api/auth/...` or Firebase REST endpoints are called, (b) the auth token is set in `authStore.idToken`, (c) wrong password actually shows an error.

task_body: |
  ## FIND-ux-023: Student login is a stub — wire real Firebase Auth (cena-platform)

  **Files**:
  - src/student/full-version/src/pages/login.vue:42-109 (stub login flow)
  - src/student/full-version/src/stores/authStore.ts (delete __mockSignIn after wire-up)
  - src/student/full-version/src/plugins/firebase.ts (replace stub plugin with real Firebase init)
  - src/student/full-version/src/components/common/AuthProviderButtons.vue (Google/Apple/MS/phone — also call real Firebase)
  - src/admin/full-version/src/composables/useFirebaseAuth.ts (mirror this pattern, minus the ADMIN_ROLES gate)

  **Goal**: Student sign-in goes through real Firebase Auth against `cena-platform`. Wrong password shows an error. Good password sets a real ID token. The MSW worker still mocks the BACKEND `/api/me` etc., but Firebase Auth itself is real.

  **Definition of Done**:
  1. Student login.vue calls `signInWithEmailAndPassword(firebaseAuth, ...)`. Wrong creds show real Firebase error code translated to user-friendly i18n key.
  2. Successful sign-in stores a real ID token in `authStore.idToken` (not a `mock-token-{uid}` string).
  3. `__mockSignIn` function and the `if (payload.email === 'fail@test.com')` branch are DELETED.
  4. AuthProviderButtons.vue's Google/Apple buttons call real `signInWithPopup(firebaseAuth, GoogleAuthProvider)` etc. The Microsoft + Phone buttons either get wired or are removed.
  5. Local dev uses Firebase emulator: add `npm run firebase:emulators` and document in README. Tests run against the emulator.
  6. The 5 OAuth provider buttons either work or are removed — currently "Continue with Google" just bypasses to /home with a fake "Google User" name, which is also a stub and also user-banned.

  **Constraint**: This shares scope with FIND-ux-006c (forgot-password backend wire-up) and FIND-ux-021 (the MSW race). Coordinate with `pedagogy` and the queue coordinator to schedule them in one student-auth block.

  **Reporting**: Branch `<worker>/find-ux-023-real-firebase-auth`. In --result, paste the network trace from a successful sign-in showing real `identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=...` call.

  related_prior_finding: FIND-ux-006
  framework: null
  reverify-task: yes

---

### FIND-ux-024 (p0, category: stub, ux)
**title**: Student class-feed "like" / "react" button does NOT update the count even when the API returns successfully — the MSW handler returns hard-coded `newCount: 1` for every reaction, AND the page swallows errors with the same anti-pattern as FIND-ux-011 (only `friends.vue` and `peers.vue` were fixed there; `social/index.vue` was missed).

related_prior_finding: FIND-ux-011 (partially-fixed — friends + peers patched, social class-feed missed)
file: src/student/full-version/src/pages/social/index.vue:25-33
file: src/student/full-version/src/plugins/fake-api/handlers/student-social/index.ts:101-110

evidence:
  - type: interaction
    content: |
      1. Navigate to /social
      2. See post by Alex Chen with "12 likes" and "3 comments"
      3. Click the heart button (testid `react-f1`)
      4. Network: zero POST captured (the page-snapshot ref went stale and
         Playwright's click resolved against a stale ref before MSW was
         confirmed registered).
      5. Manual fetch from page console:
         POST /api/social/reactions → 200 OK
         body: { "ok": true, "itemId": "f1", "reactionType": "heart", "newCount": 1 }
      6. The handler hard-codes `newCount: 1` for ALL items, regardless of
         their actual current count.
  - type: file
    content: |
      src/student/full-version/src/pages/social/index.vue:25-33
      async function handleReact(itemId: string) {
        try {
          await reactMutation.execute({ itemId, reactionType: 'heart' })
          feedQuery.refresh()
        }
        catch {
          // error surfaced via reactMutation.error
        }
      }
      The catch comment says "error surfaced via reactMutation.error" but
      the template (lines 56-77) only renders `feedQuery.error.value`,
      NEVER `reactMutation.error`. So any failure of the reaction call is
      silently swallowed at the page level — exactly the FIND-ux-011 pattern.
  - type: file
    content: |
      src/student/full-version/src/plugins/fake-api/handlers/student-social/index.ts:101-110
      http.post('/api/social/reactions', async ({ request }) => {
        const body = await request.json() as { itemId: string; reactionType: string }
        return HttpResponse.json({
          ok: true,
          itemId: body.itemId,
          reactionType: body.reactionType,
          newCount: 1,                          // ← hard-coded
        })
      })
      No state, no increment, no decrement on un-like. Every click on every
      item returns "newCount: 1" — meaning if Alex Chen had 12 likes and the
      user clicks ❤, the UI's `feedQuery.refresh()` re-fetches the
      class-feed (which returns the same 12 because /api/social/class-feed
      is also hard-coded at SOLUTIONS/feed-handler line ~30), so the count
      JUMPS BACK to 12 — the click leaves no visible trace.

impact: A learner clicking "like" on a classmate's post sees nothing change. They cannot tell whether their reaction was recorded or whether the network failed. Same UX failure mode as FIND-ux-011 in the friend-request flow that was supposed to be fixed.

root_cause: FIND-ux-011 fix patched `friends.vue` and `peers.vue` but missed `social/index.vue`. Independently, the MSW handler is a Phase-1 hard-coded fixture that the user has now banned. Additionally, the class-feed `feedQuery` returns the same items on refresh because the source data is also a static fixture, so even a working POST cannot demonstrate state change.

proposed_fix:
1. Wire `reactMutation.error` to a snackbar or VAlert in `social/index.vue` exactly like FIND-ux-011's fix did for friends + peers.
2. Make the MSW handler hold per-item state (a `Map<itemId, count>`) so reactions actually increment/decrement.
3. Wire optimistic UI: increment the count locally when the user clicks, then reconcile on response.
4. Once real backend lands, replace the MSW state with a real call.

test_required: Playwright `tests/e2e/student-social-react.spec.ts` that clicks ❤ on a feed item and asserts the visible count goes from N to N+1.

task_body: |
  ## FIND-ux-024: Class-feed reaction button silently fails AND increments to fixed value 1

  **Files**:
  - src/student/full-version/src/pages/social/index.vue:25-77 (handler swallow + missing error surface)
  - src/student/full-version/src/plugins/fake-api/handlers/student-social/index.ts:101-110 (hard-coded newCount: 1)
  - src/student/full-version/src/components/social/ClassFeedItemCard.vue (a11y label on heart button)

  **Goal**: User clicks heart → count goes up by 1. User clicks heart again → count goes back down. If the call fails, an error toast appears. The button's accessible name is "Like (12)" not just "12".

  **Definition of Done**:
  1. The MSW handler holds per-item reaction state in a Map and returns the updated count.
  2. `social/index.vue` template renders `<VSnackbar>` or `<VAlert>` bound to `reactMutation.error.value`. The catch block no longer just swallows.
  3. The heart and comment buttons have `:aria-label="t('social.feed.reactionAriaLabel', { count: item.reactionCount })"` so screen-reader users hear "Like, 12 reactions".
  4. Add `tests/e2e/student-social-react.spec.ts` that clicks the heart twice and asserts the count goes 12 → 13 → 12.
  5. Add the same test against the friends-accept and peer-vote flows from the original FIND-ux-011 to make sure those error toasts still surface.

  **Reporting**: Branch `<worker>/find-ux-024-class-feed-react`. In --result, paste a Playwright trace showing the count incrementing.

  related_prior_finding: FIND-ux-011
  framework: null
  reverify-task: yes

---

### FIND-ux-025 (p1, category: a11y, framework: WCAG-2.2-AA)
**title**: Four icon-only buttons in the student web header (sidebar toggle, language switcher, theme switcher, notifications bell) all have `aria-label = null` and no accessible name. Screen-reader users hear "button" with no context for any of them.

related_prior_finding: FIND-ux-017 (Notifications bell labeled "Badge" — partially related but a different issue: that one was the Vuetify VBadge wrapper inheriting "Badge" as the slot-default name, this is the four header icon buttons with no aria-label at all)
file: src/student/full-version/src/layouts/components/DefaultLayoutWithVerticalNav.vue:42-50 (sidebar toggle button)
file: src/student/full-version/src/@core/components/I18n.vue (language switcher button)
file: src/student/full-version/src/layouts/components/NavbarThemeSwitcher.vue (theme button)
file: src/student/full-version/src/layouts/components/NavBarNotifications.vue (notifications button)

evidence:
  - type: evaluate
    content: |
      Header buttons in `.layout-navbar`:
      [
        { icon: 'tabler-menu-2', ariaLabel: null, haspopup: null, expanded: null },
        { icon: 'tabler-language', ariaLabel: null, haspopup: 'menu', expanded: 'false' },
        { icon: 'tabler-device-desktop-analytics', ariaLabel: null, haspopup: 'menu', expanded: 'false' },
        { icon: 'tabler-bell', ariaLabel: null, haspopup: 'menu', expanded: 'false' }
      ]
  - type: axe
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/axe/student-home-mobile.json
    content: |
      "id": "button-name", "impact": "critical", "count": 3
      sample targets: #vertical-nav-toggle-btn, button[aria-controls="v-menu-v-0-2"]
      summary: "Element does not have inner text that is visible to screen
       readers ... aria-label attribute does not exist or is empty ..."

impact: Three of four (or four of four — language switcher only renders when ≥1 language is enabled, theme switcher always, bell always) primary chrome controls in the navbar cannot be discovered or operated by screen-reader users. WCAG 4.1.2 (Name, Role, Value) Level A failure.

root_cause: Vuexy's `<IconBtn>` wrapper does not synthesize an aria-label from its icon child. The student app inherited the un-labeled IconBtn pattern from the admin demo template.

proposed_fix:
1. Add `:aria-label="t('nav.toggleSidebar')"` to the IconBtn at `DefaultLayoutWithVerticalNav.vue:42`.
2. Add `:aria-label="t('nav.languageSwitcher')"` to the I18n.vue trigger button.
3. Add `:aria-label="t('nav.toggleTheme')"` to NavbarThemeSwitcher.vue.
4. Add `:aria-label="t('nav.notificationsBell')"` to NavBarNotifications.vue.
5. Add the corresponding keys to `en.json`, `ar.json`, `he.json`.

test_required: Playwright `tests/e2e/a11y-header-button-names.spec.ts` that asserts every `button` in `.layout-navbar` has a non-empty accessible name in EN, AR, HE.

task_body: |
  ## FIND-ux-025: All 4 student header icon buttons missing aria-label

  **Files**:
  - src/student/full-version/src/layouts/components/DefaultLayoutWithVerticalNav.vue:42 (sidebar toggle)
  - src/student/full-version/src/@core/components/I18n.vue (language switcher activator)
  - src/student/full-version/src/layouts/components/NavbarThemeSwitcher.vue (theme toggle)
  - src/student/full-version/src/layouts/components/NavBarNotifications.vue (notifications activator)
  - src/student/full-version/src/plugins/i18n/locales/en.json + ar.json + he.json (4 new keys)

  **Definition of Done**:
  1. Each of the 4 IconBtn calls receives an `:aria-label="t('nav.<key>')"`.
  2. New keys: `nav.toggleSidebar`, `nav.languageSwitcher`, `nav.toggleTheme`, `nav.notificationsBell` — added to all 3 locale files.
  3. axe-core scan of /home, /tutor, /social, /progress reports zero `button-name` violations on header buttons.
  4. Add `tests/e2e/a11y-header-button-names.spec.ts` per test_required.

  **Reporting**: Branch `<worker>/find-ux-025-header-button-names`. In --result, paste before/after axe `button-name` count.

  related_prior_finding: FIND-ux-017
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-026 (p1, category: a11y, framework: WCAG-2.2-AA)
**title**: Admin login page has heading order H1 → H4 with no H2 / H3 in between. Skipping heading levels breaks document outline for screen readers.

related_prior_finding: none (new — Lighthouse heading-order audit)
file: src/admin/full-version/src/pages/login.vue:106-156

evidence:
  - type: lighthouse
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/admin-login.json
    content: |
      "heading-order" audit score=0
      "Heading elements are not in a sequentially-descending order"
  - type: file
    content: |
      src/admin/full-version/src/pages/login.vue:
      :106  <RouterLink to="/">
      :107    <div class="auth-logo d-flex align-center gap-x-3">
      :108      <VNodeRenderer :nodes="themeConfig.app.logo" />
      :109      <h1 class="auth-title">
      :110        {{ themeConfig.app.brandTitle ?? themeConfig.app.title }}
      :111      </h1>
      ...
      :153  <h4 class="text-h4 mb-1">
      :154    Cena Admin
      :155  </h4>

      Page outline: H1 ("Cena Admin" in the logo), H4 ("Cena Admin" in the
      card title), no H2 / H3 between them. Same text, two competing
      heading levels.

impact: Screen-reader navigation via H-key skips from H1 to H4 with nothing in between. Document outline is broken. ALSO: the page has TWO H-elements both saying "Cena Admin" (the logo H1 and the card H4) — duplicate landmarks at different levels = confusing for AT users.

root_cause: Vuexy template puts the logo wrapper as an H1 and the card-form title as an H4. The page never had an H2/H3 because the auth layout has no intermediate sections.

proposed_fix:
1. Demote the card title from H4 to H2 (`<h2 class="text-h4">` — Vuetify's text-h4 utility class is decoupled from the actual heading level).
2. OR demote the logo H1 to a non-heading element (it's a brand mark, not the page title — make it `<div class="auth-logo">` and let the card H1 own the page title).
3. Best: keep ONE H1 per page = the page's content title. The brand logo should be inside a `<header>` landmark with no heading.

test_required: Add a Lighthouse CI check in CI that fails if `audit-result.audits["heading-order"].score < 1` on `/login`, `/forgot-password`, `/dashboards/admin`.

task_body: |
  ## FIND-ux-026: Admin login heading hierarchy is H1 → H4 (skips H2 + H3)

  **Files**:
  - src/admin/full-version/src/pages/login.vue:106-155
  - same pattern likely repeats in src/admin/full-version/src/pages/forgot-password.vue, register.vue — verify with `grep -n '<h[1-6]' src/admin/full-version/src/pages/{login,register,forgot-password}.vue`

  **Definition of Done**:
  1. Each auth page has exactly one H1 = the page's content title.
  2. Sequential heading order (no skipped levels).
  3. Lighthouse `heading-order` audit returns 1.0 on /login, /forgot-password, /register, /dashboards/admin.

  related_prior_finding: null
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-027 (p1, category: a11y, framework: WCAG-2.2-AA)
**title**: Admin login password show/hide toggle is a 16×16 px icon. Touch target requirement under WCAG 2.5.5 (target-size, AA in 2.2) is 24×24 minimum. Lighthouse flags it.

related_prior_finding: FIND-ux-018 ("Append" accessible name leak — same element, different a11y issue)
file: src/admin/full-version/src/pages/login.vue:201-203

evidence:
  - type: lighthouse
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/admin-login.json
    content: |
      "target-size" audit score=0
      Element: <i class="tabler-eye" ...>
      selector: div.v-input__control > div.v-field > div.v-field__append-inner > i.tabler-eye
      boundingRect: { width: 16, height: 16 }
  - type: file
    content: |
      src/admin/full-version/src/pages/login.vue:201-203
      <AppTextField
        ...
        :append-inner-icon="isPasswordVisible ? 'tabler-eye-off' : 'tabler-eye'"
        @click:append-inner="isPasswordVisible = !isPasswordVisible"
      />

      The icon is a 16×16 `<i>` tag, not a wrapped button. The click event
      is delegated via Vuetify's `@click:append-inner` to the icon node.

impact: Touch users on mobile (and tremor / motor-impairment users on any device) cannot reliably hit the eye icon without zooming.

root_cause: AppTextField uses Vuetify's append-inner-icon slot which renders a `<i>` element directly without padding it out to a touch-target-sized button.

proposed_fix:
1. Replace `:append-inner-icon` with a `template #append-inner` slot containing an explicit `<VBtn icon size="x-small" :aria-label="t('auth.passwordShowToggle')">`.
2. The button takes a 32×32 hit area (Vuetify's `size="x-small"` IconBtn) which exceeds the 24×24 minimum.
3. Bonus: this also fixes FIND-ux-018 (the "Append" name) because the new button has an explicit `aria-label`.

test_required: Lighthouse target-size audit on /login passes with score 1.0; axe `target-size` violation count = 0.

task_body: |
  ## FIND-ux-027: Admin password eye icon is a 16x16 hit target — wrap in IconBtn

  **Files**:
  - src/admin/full-version/src/pages/login.vue:193-203 (password field)
  - src/admin/full-version/src/pages/register.vue (same field, copy/paste)
  - src/admin/full-version/src/pages/forgot-password.vue (likely the same)

  **Definition of Done**:
  1. Replace `:append-inner-icon` + `@click:append-inner` with a `<template #append-inner>` slot containing `<VBtn icon variant="text" size="x-small" :aria-label="...">`.
  2. The button hit area is ≥24×24 CSS px (Vuetify x-small renders ~32×32).
  3. Lighthouse `target-size` audit on /login = 1.0. axe `target-size` count = 0.
  4. The button's `aria-label` is i18n'd and reflects the toggle state ("Show password" / "Hide password").
  5. Same fix applied to register.vue and forgot-password.vue if they have the same field.
  6. **This subsumes FIND-ux-018** (the "Append" leak) — when this lands, mark FIND-ux-018 done.

  related_prior_finding: FIND-ux-018
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-028 (p1, category: i18n, label-drift)
**title**: When the student web is in Hebrew, the navbar notifications button is labeled `"תג"` (Hebrew for "tag/badge"). The user-visible accessible name is the Vuetify default `Badge` slot string passed through the i18n provider.

related_prior_finding: FIND-ux-017 (Notifications "Badge" label) — verifies the bug spreads to other locales when an i18n layer is on top
file: src/student/full-version/src/layouts/components/NavBarNotifications.vue
file: src/student/full-version/src/plugins/i18n/locales/he.json (probable accidental key)

evidence:
  - type: snapshot
    content: |
      With cena-student-language=he, /session page navbar:
      - banner [ref=e23]:
        - generic [ref=e25]:
          - button [ref=e26] [cursor=pointer]   ← menu toggle
          - button [ref=e29] [cursor=pointer]   ← language switcher
          - button "תג" [ref=e32] [cursor=pointer]:
            - status "תג" [ref=e37]
          - generic [ref=e39]:
            - generic [ref=e41] [cursor=pointer]: S
            - status "תג" [ref=e42]
      Three "תג" labels appear. "תג" is the Hebrew word for "tag/badge".
  - type: file
    content: |
      Vuetify VBadge has a default slot text of "Badge" used internally as
      an aria-label fallback when no content is provided. When the i18n
      provider is active, that fallback string passes through `t('Badge')`
      and either translates (if the locale file has a key "Badge") or
      stays as "Badge" in EN. Hebrew gets "תג" because Vuetify's i18n
      bundle includes that string.

impact:
1. Same as FIND-ux-017 (a11y label drift on the notifications bell), but **worse**: in EN it says "Badge" (English Vuetify default), in HE it says "תג" (Hebrew). Both are wrong — the affordance is the notifications menu, not a "tag".
2. Confirms that FIND-ux-017's prior P2 severity should likely be P1 because the i18n layer makes it user-discoverable in 3 languages, not just one.

root_cause: NavBarNotifications.vue wraps the bell in a `<VBadge>` to render the unread-count dot. VBadge's default ARIA name is "Badge". Vuetify's i18n bundle translates "Badge" to "תג" in Hebrew. The fix is to give the wrapping button an explicit `aria-label`, NOT to override Vuetify's badge string.

proposed_fix:
1. In NavBarNotifications.vue, wrap the bell button in an explicitly-labelled IconBtn (see FIND-ux-025 fix).
2. Pass `aria-label=""` (empty) on the inner VBadge so it doesn't add its own conflicting label.

test_required: Same as FIND-ux-025's test, but verify all 3 locales: assert notifications-button accessible name is "Notifications" in EN, "إشعارات" in AR, "התראות" in HE.

task_body: |
  ## FIND-ux-028: Notifications bell labeled "תג" (Hebrew Badge default) — escalate FIND-ux-017 to P1

  **Files**:
  - src/student/full-version/src/layouts/components/NavBarNotifications.vue
  - locale files (add `nav.notificationsBell` if not present)

  **Definition of Done**:
  1. Notifications bell button has explicit `aria-label="t('nav.notificationsBell')"`.
  2. The VBadge inside has `aria-label=""` to suppress the Vuetify default.
  3. Tested in EN, AR, HE: accessible name reflects the notifications affordance, not "Badge" or "תג".
  4. Same pattern applied to the user-profile-avatar's badge (the green online dot).

  **Constraint**: Coordinate with FIND-ux-022 and FIND-ux-025 — these three should land together as one a11y header-bar pass.

  related_prior_finding: FIND-ux-017
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-029 (p1, category: ux)
**title**: Student web manifest declares `icons: [{ src: '/images/logo.png', sizes: '192x192', type: 'image/png' }]` but `/images/logo.png` does not exist on disk. Vite returns the SPA `index.html` (Content-Type: text/html) for the path. Browsers reject the manifest icon and the PWA "Install to home screen" experience is broken.

related_prior_finding: none
file: src/student/full-version/public/manifest.webmanifest (probable — needs verification)
file: src/student/full-version/public/images/  (file is missing)

evidence:
  - type: shell
    content: |
      $ curl -sI http://localhost:5175/images/logo.png
      HTTP/1.1 200 OK
      Vary: Origin
      Content-Type: text/html       ← NOT image/png
      Cache-Control: no-cache
      Etag: W/"af7-..."

      $ ls /Users/shaker/edu-apps/cena/src/student/full-version/public/images/logo.png
      ls: ... No such file or directory
  - type: shell
    content: |
      $ curl -sS http://localhost:5175/manifest.webmanifest | head -20
      {
        "name": "Cena Student",
        "short_name": "Cena",
        ...
        "icons": [
          { "src": "/images/logo.png", "sizes": "192x192", "type": "image/png", "purpose": "any" },
          { "src": "/images/logo.png", "sizes": "512x512", ... }
        ]
      }
  - type: console
    content: |
      [WARNING] Error while trying to use the following icon from the
      Manifest: http://localhost:5175/images/logo.png (Download error or
      resource isn't a valid image)
      ↑ Console warning on every page load.

impact: PWA install prompt is broken. Mobile users cannot add Cena to their home screen with a real icon. The browser address bar shows a placeholder favicon.

root_cause: The manifest references `/images/logo.png` but the file was never added (or was deleted) from `public/images/`. Vite's dev server falls back to the SPA index.html for any unmatched route — masking the 404.

proposed_fix:
1. Add a real `public/images/logo.png` (192×192) AND `public/images/logo-512.png` (512×512), OR
2. Update the manifest to point to existing icons (the SVG logo at `src/assets/...` won't work for a manifest because manifests need raster).
3. Add a build-time check that resolves every manifest icon path against `public/` and fails the build if missing.

test_required: A vitest test that reads `manifest.webmanifest` and asserts every `icons[].src` resolves to a real file with a 200 + image/* Content-Type via fetch().

task_body: |
  ## FIND-ux-029: PWA manifest icon /images/logo.png is missing — Vite returns HTML, browser rejects

  **Files**:
  - src/student/full-version/public/manifest.webmanifest (or `/manifest.json`, find it via `find src/student/full-version/public -name 'manifest*'`)
  - src/student/full-version/public/images/  (need to create logo.png + logo-512.png)
  - vite.config.ts (add a manifest-icon-validation plugin)

  **Definition of Done**:
  1. `curl -I http://localhost:5175/images/logo.png` returns `Content-Type: image/png` and a 200.
  2. The manifest icon at 192×192 and 512×512 are real PNG files derived from the brand SVG.
  3. The Chrome Application > Manifest panel in DevTools shows valid icons (no "Download error").
  4. Add a vitest test under `tests/build/manifest.spec.ts` that reads the manifest and validates every icon resolves.
  5. Same check for the admin host's manifest at port 5174.

  related_prior_finding: null
  framework: null
  reverify-task: yes

---

### FIND-ux-030 (p1, category: a11y, framework: WCAG-2.2-AA)
**title**: Student `SessionSetupForm.vue` subject chips (Math/Physics/Chemistry/Biology/English/History) render as `<span>` with no `role`, no `aria-pressed`, no `aria-selected`. Screen-reader users navigating with Tab hear "Math" but cannot tell it is interactive or whether it is currently selected. Mouse users see the visual selected state but assistive-tech users do not.

related_prior_finding: none
file: src/student/full-version/src/components/session/SessionSetupForm.vue:59-69

evidence:
  - type: snapshot
    content: |
      Page snapshot a11y tree (Hebrew /session page):
      - generic [ref=e57]:
        - generic [ref=e59] [cursor=pointer]: מתמטיקה  ← Math chip
        - generic [ref=e61] [cursor=pointer]: פיזיקה   ← Physics
        - generic [ref=e63] [cursor=pointer]: כימיה   ← Chemistry
        ...
      Each chip is a "generic" (i.e. <span>) with no role.
  - type: evaluate
    content: |
      const chip = document.querySelector('[data-testid="setup-subject-math"]');
      → tag: 'SPAN', role: null, tabindex: 0,
        ariaPressed: null, ariaSelected: null,
        keyboardReachable: true
  - type: file
    content: |
      src/student/full-version/src/components/session/SessionSetupForm.vue:59-69
      <VChip
        v-for="s in SUBJECTS"
        :key="s"
        :color="selectedSubjects.includes(s) ? 'primary' : undefined"
        :variant="selectedSubjects.includes(s) ? 'flat' : 'outlined'"
        :data-testid="`setup-subject-${s}`"
        size="default"
        @click="toggleSubject(s)"
      >
        {{ t(`session.setup.subjects.${s}`) }}
      </VChip>

impact: Keyboard users CAN tab to the chip (tabindex=0 is set by Vuetify) and pressing Space DOES toggle it (Vuetify wires this). But screen-reader users hear "Math" with no indication that it is a control or whether they have selected it. The "Start Session" button stays disabled until at least one subject is chosen — which means SR users cannot start a session because they don't know what to do.

root_cause: Vuetify VChip is presentational by default. To use it as a multi-select toggle the developer must add `role="button"` (or use a `<button>` directly) and `aria-pressed` bindings. The current usage relies on visual state only.

proposed_fix:
1. Add `role="button"` and `:aria-pressed="selectedSubjects.includes(s)"` to each VChip.
2. Add `:aria-label="t('session.setup.subjectChipAria', { subject: t('session.setup.subjects.' + s), selected: selectedSubjects.includes(s) })"`.
3. Better: replace the chip with a `<VBtn>` that already has the right semantics.
4. Best: use a `<fieldset>` of checkboxes — it semantically matches "pick one or more subjects" and is the most a11y-friendly multi-select pattern.

test_required: Playwright `tests/e2e/a11y-session-setup.spec.ts` that navigates to /session with screen-reader simulation, presses Tab to the first subject chip, and asserts `accessibleName` is "Math, not pressed" (or similar) per WCAG 4.1.2.

task_body: |
  ## FIND-ux-030: Session-setup subject chips have no role/aria-pressed — multi-select unusable for SR users

  **Files**:
  - src/student/full-version/src/components/session/SessionSetupForm.vue:59-69 (subject chips)
  - locale files (add `session.setup.subjectChipAria` key with EN/AR/HE)

  **Definition of Done**:
  1. Each subject toggle is a real interactive control with `role="button"` (or is wrapped in a `<button>`/`<input type="checkbox">`).
  2. `aria-pressed` (or `aria-checked`) reflects the current selection state.
  3. axe-core scan of /session reports zero `aria-required-attr` or `button-name` violations on subject chips.
  4. Add `tests/e2e/a11y-session-setup.spec.ts` that uses keyboard-only flow (Tab + Space) to select Math + Physics, then presses Tab+Enter on the disabled-then-enabled Start Session button, and asserts a session POST is fired.
  5. Same fix applied to the duration toggle and mode toggle if they share the pattern (verify with axe).

  related_prior_finding: null
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-031 (p1, category: ux, label-drift)
**title**: Student `pages/login.vue` "Continue with Google / Apple / Microsoft / phone" buttons all bypass real OAuth and just call `__mockSignIn` with a hard-coded `displayName: 'Google User'` / `'Apple User'` / etc. The user lands on /home with a fabricated identity that says `Hi Google User`.

related_prior_finding: FIND-ux-023 (same root: stub auth)
file: src/student/full-version/src/components/common/AuthProviderButtons.vue:55-70

evidence:
  - type: interaction
    content: |
      1. Visit /login (no prior auth).
      2. Click "Continue with Google".
      3. Page navigates to /home and renders "Hi Google User" greeting.
      4. localStorage["cena-mock-auth"] now contains:
         {"uid":"mock-google-mnuqx38i","email":"google-user@example.com",
          "displayName":"Google User"}
      5. No popup, no redirect, no Firebase call, no Google.
  - type: file
    content: |
      src/student/full-version/src/components/common/AuthProviderButtons.vue:62
      authStore.__mockSignIn({ uid, email: fakeEmail, displayName })
      ↑ All four provider buttons (Google, Apple, Microsoft, phone) hit
        the same __mockSignIn path with a hard-coded displayName.

impact: Same as FIND-ux-023 (stub auth), but worse because it actively lies about the identity. A demo to a stakeholder where the engineer clicks "Continue with Google" and the welcome banner says "Hi Google User" is a credibility problem. Tests written against this can never validate real OAuth flows.

root_cause: STU-W-02 stub. Same as FIND-ux-023.

proposed_fix: Same as FIND-ux-023 — wire real `signInWithPopup(firebaseAuth, GoogleAuthProvider)` etc., or remove the buttons entirely until backend support exists. Coordinate with FIND-ux-023 in one task.

test_required: Same as FIND-ux-023.

task_body: |
  ## FIND-ux-031: OAuth provider buttons fake the sign-in — bypass real Google/Apple/Microsoft/phone

  See FIND-ux-023 task body. This is the OAuth half of the same fix:
  - src/student/full-version/src/components/common/AuthProviderButtons.vue
  - Also delete the 'Continue with phone' button if no phone-auth backend exists yet.

  related_prior_finding: FIND-ux-023
  framework: null
  reverify-task: yes

---

### FIND-ux-032 (p1, category: ux, label-drift)
**title**: Student `settings/notifications.vue` toggles persist only to `localStorage` — no backend write. The UI gives no hint that preferences are device-local. Verifies the prior FIND-ux-016 (P2 in v1) is still unfixed and escalates because v2 enforces "no Phase-1 stubs".

related_prior_finding: FIND-ux-016
file: src/student/full-version/src/pages/settings/notifications.vue:19-46

evidence:
  - type: interaction
    content: |
      1. Visit /settings/notifications (logged in as student@cena.local).
      2. Click "Push notifications" toggle (default off → on).
      3. Network: ZERO POST/PATCH/PUT calls fired against any /api/me/* path.
      4. Reload — the toggle remembers the new state via localStorage.
      5. Open a fresh incognito window, sign in as the same user — the
         toggle is back to its default. The setting is device-local.
      6. The page never says "saved on this device only".
  - type: file
    content: |
      src/student/full-version/src/pages/settings/notifications.vue:19-46
      // Phase A: local toggles persist to localStorage only.
      // STU-W-14b will wire /api/me/settings when STB-00b settings writes
      // land.
      function persist() {
        if (typeof localStorage !== 'undefined')
          localStorage.setItem('cena-notification-prefs', JSON.stringify(prefs.value))
      }

impact: A learner changes their notification prefs on their phone. Logs in on their laptop. Sees the defaults back. Can't figure out why. Same as FIND-ux-016 from v1.

root_cause: Phase A stub. Real backend (STB-00b) hasn't landed.

proposed_fix:
1. Either wire the real /api/me/settings endpoint (preferred — banned-stub rule).
2. Or display an inline `<VAlert variant="tonal" type="info">` that says "These preferences are saved to this device only. They will sync to your account when [feature] is enabled." in EN, AR, HE.
3. Per the user's "no stubs" rule, option 1 is mandatory unless the backend is genuinely not ready, in which case the UI should be hidden behind a feature flag, not shown as a fake control.

test_required: Playwright that toggles a preference, signs out, signs in from another tab, and asserts the toggle is in the saved state (not the default).

task_body: |
  ## FIND-ux-032: Settings/Notifications persists to localStorage only — wire real backend or hide

  **Files**:
  - src/student/full-version/src/pages/settings/notifications.vue:19-46
  - The corresponding student backend endpoint at src/api/Cena.Student.Api.Host/Endpoints/MeEndpoints.cs (add /me/settings if not present)

  **Definition of Done**:
  1. PATCH /api/me/settings is wired and the page calls it on every toggle change.
  2. The page reads /api/me/settings on mount and reflects the server state.
  3. Optimistic UI on toggle (don't wait for the round-trip).
  4. Error handling: if the call fails, the toggle reverts and a snackbar appears.
  5. Cross-device test: sign in on two browsers, toggle a pref on browser A, refresh browser B, see the change.
  6. **Or**, if the backend cannot land in this sprint: hide the entire page behind a `VITE_ENABLE_SETTINGS_NOTIFICATIONS` flag and surface a "Coming soon — your school admin can manage notifications for now" placeholder.

  related_prior_finding: FIND-ux-016
  framework: null
  reverify-task: yes

---

### FIND-ux-033 (p1, category: ux, label-drift)
**title**: Admin login OAuth button labels are title-cased: "Sign In With Google" / "Sign In With Apple" / "Forgot Password?". Standard sentence-case is "Sign in with Google" / "Forgot password?". Inconsistent with the student web ("Continue with Google", correctly cased). Branding/i18n hygiene.

related_prior_finding: none
file: src/admin/full-version/src/pages/login.vue:213-264 (or wherever the labels live)

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/07-admin-login.png
    note: |
      Visible buttons:
      - "Sign In"          ← inconsistent (should be "Sign in")
      - "Forgot Password?" ← title-case
      - "Sign In With Google"  ← title-case
      - "Sign In With Apple"   ← title-case
  - type: file
    content: |
      src/admin/full-version/src/pages/login.vue:223-225
      <VBtn block type="submit" :loading="isSubmitting" :disabled="isSubmitting">
        Sign In
      </VBtn>
      ... and the OAuth buttons further down all use "Sign In With X"

impact: Cosmetic but it's a "labels match data" rule violation: same product is title-case in admin and sentence-case in student. Pick one. (Sentence-case is the modern norm.)

root_cause: Vuexy template default is title case.

proposed_fix:
1. Replace hard-coded "Sign In" / "Sign In With X" / "Forgot Password?" with i18n keys.
2. EN values use sentence-case: "Sign in", "Sign in with Google", "Forgot password?".
3. Add to ar.json and he.json.

test_required: Snapshot test on /login that asserts the button labels match the i18n key values exactly.

task_body: |
  ## FIND-ux-033: Admin login labels are title-case — sentence-case + i18n them

  **Files**:
  - src/admin/full-version/src/pages/login.vue (button labels at 213, 224, 248, 263)
  - src/admin/full-version/src/plugins/i18n/locales/*.json (add `auth.signIn`, `auth.signInWithGoogle`, etc.)
  - same for register.vue and forgot-password.vue

  **Definition of Done**:
  1. All 4 OAuth/email buttons + "Forgot password?" link are i18n keys.
  2. EN values use sentence-case.
  3. AR + HE locale values added.
  4. Snapshot test asserts the rendered text matches.

  related_prior_finding: null
  framework: null
  reverify-task: yes

---

### FIND-ux-034 (p2, category: ux)
**title**: Student `/home` mobile bottom nav (`Home`, `Start Session`, `Tutor`, `Progress`, `Profile`) **overlaps** the LEVEL 7 / 14 sessions KPI card. The KPI tile is partially obscured by the nav bar at 375×812.

related_prior_finding: none
file: src/student/full-version/src/layouts/components/StudentBottomNav.vue (likely; verify path)
file: src/student/full-version/src/pages/home.vue (needs bottom-padding to clear the nav)

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/05-student-home-mobile-rendered.png
    note: |
      Bottom nav covers the bottom edge of the LEVEL 7 / 14 sessions KPI
      card. The nav background is white with a subtle border, but the
      "Home" tab + "Tutor" tab labels visually intersect with the card's
      bottom edge.

impact: Visual collision. The KPI value is fully readable but the card chrome is cut off. On longer scrolls (e.g. /progress) the bottom nav permanently hides the last 56–80 px of content unless the page adds bottom padding.

root_cause: The page-level scroll container does not reserve space for the fixed bottom nav. Standard fix: `padding-block-end: env(safe-area-inset-bottom) + 88px` on the main content wrapper, OR set `mb-24` on every page's outer div.

proposed_fix:
1. In the layout-default wrapper, add `padding-block-end: calc(env(safe-area-inset-bottom, 0px) + 88px)` for the `.has-bottom-nav` body class.
2. Add `padding-bottom: 88px` to home.vue, tutor.vue, progress.vue, settings.vue, social.vue, etc., or set it once in `default-layout.scss`.

test_required: Visual regression test (Playwright `toHaveScreenshot`) on /home, /tutor, /progress, /social at 375×812 with the bottom nav present. Compare to a baseline that shows full clearance.

task_body: |
  ## FIND-ux-034: Mobile bottom nav overlaps page content — add safe-area padding

  **Files**:
  - src/student/full-version/src/layouts/default.vue (or the components/* equivalent)
  - src/student/full-version/src/@layouts/styles/default-layout.scss

  **Definition of Done**:
  1. On 375×812 (and 360×640) viewports, the bottom 88px of every authenticated page is reserved/padded so the bottom nav never overlays content.
  2. `padding-block-end: calc(env(safe-area-inset-bottom, 0px) + 88px)` applied to the layout-content-wrapper when the bottom nav is visible.
  3. Visual regression test under `tests/visual/mobile-no-overlap.spec.ts` for /home, /tutor, /progress, /social.

  related_prior_finding: null
  framework: null
  reverify-task: yes

---

### FIND-ux-035 (p2, category: ux)
**title**: Student `/social` like / comment buttons are labeled with the count alone ("12", "3"). No context-providing accessible name. SR users hear "12, button".

related_prior_finding: FIND-ux-024 (related: the like button is also functionally broken)
file: src/student/full-version/src/components/social/ClassFeedItemCard.vue:109-126

evidence:
  - type: snapshot
    content: |
      - button "12" [ref=e87] [cursor=pointer]:
        - generic [ref=e90]: "12"
      - button "3" [ref=e91] [cursor=pointer]:
        - generic [ref=e94]: "3"

impact: Screen-reader users hear bare numbers and cannot tell what the button does. Bad for SR users; also bad for cognitive-disability users.

root_cause: VBtn `prepend-icon="tabler-heart"` with the count as text content. The icon is `aria-hidden="true"` (correctly) but no `aria-label` substitute fills the gap.

proposed_fix:
1. Add `:aria-label="t('social.feed.likeAria', { count: item.reactionCount })"`.
2. EN: "Like, {count} reactions". AR + HE equivalents.
3. Same for the comment button.

task_body: |
  ## FIND-ux-035: Social feed like/comment buttons labelled only with count — add aria-label

  **Files**:
  - src/student/full-version/src/components/social/ClassFeedItemCard.vue:109-126
  - locale files (add `social.feed.likeAria`, `social.feed.commentAria`)

  **Definition of Done**: Each button has an explicit aria-label including the action and count. axe `button-name` count = 0 on /social.

  related_prior_finding: FIND-ux-024
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-036 (p2, category: ux)
**title**: Student `/home` greeting reads `Hi student@cena.local` — the user's email address is shown verbatim as a "display name" because the mock sign-in path stores the email in the displayName field.

related_prior_finding: none
file: src/student/full-version/src/pages/login.vue:94 (mock displayName = email)
file: src/student/full-version/src/components/home/HomeGreeting.vue (probable — renders displayName)

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/reverify-2026-04-11/ux/03-student-home-after-login.png
    note: "Visible heading: 'Hi student@cena.local'"
  - type: file
    content: |
      src/student/full-version/src/pages/login.vue:94
      authStore.__mockSignIn({ uid, email: payload.email, displayName: payload.email })
      ↑ displayName is just the email. Then home.vue greets with displayName.

impact: Email-as-display-name is awkward and exposes the email to anyone shoulder-surfing. Real Firebase usually populates displayName from the user's profile. The mock path lazily reuses the email and the greeting templates render it without sanitizing.

root_cause: Mock sign-in shortcut.

proposed_fix:
1. When real Firebase lands (FIND-ux-023), this goes away naturally.
2. Until then: in the greeting template, prefer `displayName.split('@')[0]` if displayName looks like an email, OR fall back to `t('home.greetingAnonymous')`.

task_body: |
  ## FIND-ux-036: Home greeting shows raw email "student@cena.local"

  **Files**:
  - src/student/full-version/src/components/home/HomeGreeting.vue
  - src/student/full-version/src/pages/login.vue:94 (delete after FIND-ux-023 lands)

  **Definition of Done**:
  1. Greeting prefers a real displayName.
  2. If displayName matches an email regex, show the local-part only ("student", not "student@cena.local").
  3. Tests cover both cases.

  **Constraint**: This becomes a non-issue when FIND-ux-023 lands (real Firebase populates displayName from the user profile). Track as a follow-up to that fix.

  related_prior_finding: FIND-ux-023
  framework: null
  reverify-task: no  (subsumed by FIND-ux-023)

---

### FIND-ux-037 (p2, category: a11y)
**title**: Student `aside.layout-vertical-nav ul.nav-items` element has 20 `<div>` direct children (the perfect-scrollbar rails + the v-if'd-out nav links). axe-core flags `list` violation: "List element has direct children that are not allowed: div".

related_prior_finding: FIND-ux-020 (root cause is the empty sidebar, but the `<div>` rail children are a separate structural issue)
file: src/student/full-version/src/@layouts/components/VerticalNav.vue (perfect-scrollbar UL)

evidence:
  - type: axe
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/axe/student-home-mobile.json
    content: |
      "id": "list", "impact": "serious",
      "target": ["ul"],
      "html": "<ul class=\"ps nav-items ps--active-y\">",
      "summary": "Fix all of the following: List element has direct
       children that are not allowed: div"

impact: HTML semantic violation. AT users may experience the nav as a non-list. Won't fix on its own — depends on the underlying perfect-scrollbar wrapper.

root_cause: vue3-perfect-scrollbar wraps content in `<div class="ps__rail-x">` and `<div class="ps__rail-y">` siblings inside the `<ul tag>`. The wrapper component picks `tag="ul"` from the consumer but injects DIVs as siblings, breaking semantics.

proposed_fix:
1. Replace the `tag="ul"` with `tag="div"` and use `role="list"` + child `role="listitem"` instead — this is a common workaround for libraries that inject non-list children.
2. OR: drop perfect-scrollbar (it's overkill for nav; native overflow:auto + scroll-snap works).

task_body: |
  ## FIND-ux-037: VerticalNav <ul> contains <div> rails — invalid HTML, fix structure

  **File**: src/student/full-version/src/@layouts/components/VerticalNav.vue:127-141

  **Definition of Done**:
  1. Either: change `tag="ul"` → `tag="div" role="list"` and add `role="listitem"` to each nav item rendered child.
  2. OR: drop vue3-perfect-scrollbar dependency for the nav and use native overflow.
  3. axe `list` violation count = 0 on /home and /tutor.

  related_prior_finding: FIND-ux-020
  framework: WCAG-2.2-AA
  reverify-task: yes

---

### FIND-ux-038 (p3, category: a11y)
**title**: Auth pages (admin /login, student /login, /register, /forgot-password) all lack a `<main>` landmark — Lighthouse `landmark-one-main` audit fails on all four pages.

related_prior_finding: none
file: src/admin/full-version/src/pages/login.vue (no `<main>` wrapping the form)
file: src/student/full-version/src/layouts/auth.vue (the auth layout)

evidence:
  - type: lighthouse
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/admin-login.json
    content: |
      "landmark-one-main" audit score=0
      "Document does not have a main landmark."
  - type: lighthouse
    file: docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/student-login.json
    content: same.

impact: Screen-reader users cannot jump to the main content via the `M` key or `landmark` rotor. Minor but P3 because all auth pages have small flat content; the cost of adding a `<main>` is one line.

proposed_fix: Wrap the auth-card in `<main>`, OR add `<main>` to the auth layout component.

task_body: |
  ## FIND-ux-038: Auth pages missing <main> landmark

  **Files**:
  - src/student/full-version/src/layouts/auth.vue
  - src/admin/full-version/src/pages/login.vue + register.vue + forgot-password.vue (or their auth layout)

  **Definition of Done**:
  1. Each auth page has exactly one `<main>` element wrapping the form/card.
  2. Lighthouse `landmark-one-main` audit returns 1.0 on each.

  related_prior_finding: null
  framework: WCAG-2.2-AA
  reverify-task: yes

---

## Known open passthrough (NOT new findings; surfaced from preflight)

These were enqueued before this re-verify and remain open. The `ux` agent did **not** re-discover them; they are noted here so the coordinator and reviewers don't miss that they are still pending.

| Prior ID | Task ID | Summary | Status |
|---|---|---|---|
| FIND-ux-011 + FIND-ux-012 | t_6b1c4fbe96d2 | Social swallow-and-smile (friends/peers fixed; **class-feed/social/index.vue still has the same swallow** — see FIND-ux-024) + tutor `?` key swallowed by ShellShortcuts | OPEN |
| FIND-ux-006c | t_f7bb146a546b | Wire student forgot-password.vue to the new `POST /api/auth/password-reset` endpoint | OPEN |

FIND-ux-011 partial regression is captured as **FIND-ux-024** above.

## Verified-fixed live (preflight had only SFC-level proof — this run drove them in the browser)

| Prior ID | Live verdict | Notes |
|---|---|---|
| FIND-ux-002 (`/pages/index.vue` dev chassis) | verified-fixed live | `/` redirects to `/login`. Tested. |
| FIND-ux-003 (MSW cookie space crash) | verified-fixed live | Loaded admin and student in same browser session, no MSW cookie parse errors in console. |
| FIND-ux-004 (`Hi student` home KPIs hard-coded) | verified-fixed live | Home now reads from /api/analytics/* and renders real `streakDays`, `totalSessions`, `overallAccuracy`, `minutesToday`. The MSW handlers themselves serve canned values but the rendering layer is correct. (See FIND-ux-021 for the broader MSW stub concern, and FIND-ux-024 for the per-handler hardcoded data.) |
| FIND-ux-005 (tutor STB-04b leak) | verified-fixed live | /tutor/th-1 thread shows realistic tutor responses, no `(STB-04b will wire real LLM streaming.)` strings in any chat bubble. |
| FIND-ux-006 (forgot-password silent drop) | verified-fixed live | Page now renders an honest "account recovery is not available from this app" instead of a fake success state. **FIND-ux-006c** still tracks the real backend wire-up. |
| FIND-ux-007 (i18n raw English leaks) | verified-fixed for navigation chrome — but **FIND-ux-021** discovers a new error-message i18n leak (raw HTTP path strings). | |
| FIND-ux-008 (admin title "Vuexy") | verified-fixed live | Tab title reads "Cena Admin". |
| FIND-ux-009 (student sidebar "Vuexy") | verified-fixed live | Sidebar logo reads "Cena". |
| FIND-ux-010 (mock auth doesn't survive reload) | verified-fixed live for *same browser session* — `__mockSignIn` rehydrates from localStorage on cold boot. **HOWEVER**, share-URL across browsers / Lighthouse-headless still bounces to /login (correctly, because there is no real Firebase persistence yet — see FIND-ux-023). | |
| FIND-ux-013 (leaderboard "Dev Student") | verified-fixed at file level — full live verification deferred (the /progress/leaderboard route is one of the routes the empty-sidebar hides; reachable only by URL). | |
| FIND-ux-014 (Hebrew always shown) | verified-fixed live (env-flag pattern is correct; this dev box has the flag on, which is fine). | |
| FIND-ux-015 (Actor-nodes silent error) | not re-verified live (admin requires real Firebase auth and I do not have credentials to drive the admin dashboard end-to-end). Code has been patched per fix commit. | |
| FIND-ux-018 (admin "Append" eye icon) | verified-still-broken live — the password show/hide button is still labeled "Append" (and is also a 16x16 px touch target — see FIND-ux-027 for the merged fix). FIND-ux-018 should be subsumed by FIND-ux-027 in the queue. | |

---

## Appendix A — Lighthouse score table (full)

| Page | URL | A11y score | Failing audits |
|---|---|---:|---|
| Admin login | http://localhost:5174/login | 0.84 | aria-prohibited-attr, color-contrast, heading-order, landmark-one-main, target-size |
| Admin dashboard (cold) | http://localhost:5174/dashboards/admin (bounced to /login) | 0.84 | same as admin login |
| Student login | http://localhost:5175/login | 0.90 | aria-prohibited-attr, color-contrast, landmark-one-main |
| Student register | http://localhost:5175/register | 0.90 | (similar to login) |
| Student home (cold) | http://localhost:5175/home (bounced to /login in headless Chrome — Firebase persistence not present) | 0.90 | same as student login |
| Student forgot-password | http://localhost:5175/forgot-password | 0.91 | (similar to login, slightly different mix) |

JSON reports: `docs/reviews/screenshots/reverify-2026-04-11/ux/lighthouse/`

## Appendix B — axe-core violation counts (per page, after excluding Vue DevTools panel)

| Page | Viewport | Critical | Serious | Moderate | Total |
|---|---|---:|---:|---:|---:|
| Student /home (live, signed-in) | 1280×800 | 2 | 2 | 0 | 4 |
| Student /home (live, signed-in) | 375×812  | 2 | 3 | 0 | 5 |
| Admin /login (cold) | 1280×800 | 1 | 3 | 0 | 4 |

Top critical violations across all pages:
1. `button-name` (3+ instances on every authenticated page — FIND-ux-025)
2. `aria-allowed-attr` (1 instance on every authenticated page — FIND-ux-022)

JSON reports: `docs/reviews/screenshots/reverify-2026-04-11/ux/axe/`

## Appendix C — Screenshots

| File | Purpose |
|---|---|
| 01-student-login.png | Student login page baseline |
| 02-student-home.png | Student home (after Google mock OAuth bypass) |
| 03-student-home-after-login.png | Student home (after email/password mock sign-in) showing empty sidebar + "Hi student@cena.local" greeting |
| 04-student-home-mobile-msw-race-error.png | **Mobile cold load showing user-visible `[GET] "/api/me": 404 Not Found` raw error** |
| 05-student-home-mobile-rendered.png | Mobile home page successfully rendered (after the race resolves) — bottom nav overlap visible |
| 06-student-home-mobile-hebrew.png | Hebrew RTL — error string is NOT translated, leaks raw EN HTTP path |
| 07-admin-login.png | Admin login page — title-cased button labels, "Append" eye icon, contrast violations |
| 08-student-tutor-msw-race-404.png | Desktop /tutor cold load with both empty sidebar AND raw 404 error |
| 09-student-empty-sidebar-desktop.png | Desktop /tutor showing the empty student sidebar in isolation |

## Appendix D — Network capture

Cold-load network trace for /home: `docs/reviews/screenshots/reverify-2026-04-11/ux/network-student-home-cold.txt`

## Appendix E — Out-of-scope notes

- **Firebase API key** committed at `src/admin/full-version/.env`. Out of UX lens scope. The `sec` lens should already be tracking this.
- **The student web's MSW handlers themselves** (`student-social/index.ts:101` `newCount: 1`, etc.) are Phase-1 fixtures. The `pedagogy` and `data` lenses likely have parallel findings. I report the user-visible UX symptoms here (FIND-ux-021, FIND-ux-024) not the full inventory of stub fixtures.
- **Admin dashboard live walkthrough** is blocked because admin auth uses real Firebase against `cena-platform`, and I do not have credentials. Lighthouse runs against the admin protected routes bounced to `/login`, so the dashboard a11y score is effectively the admin login a11y score. A future admin-credentialed run is needed to verify FIND-ux-015 live.
