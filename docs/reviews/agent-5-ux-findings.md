---
agent: claude-subagent-ux
role: UX & Broken-Workflow Auditor
date: 2026-04-11
branch: claude-subagent-ux/review-2026-04-11
mode: live
dev_servers:
  admin: http://localhost:5174 (reachable)
  student: http://localhost:5175 (reachable after fixing broken npm install)
notes: |
  Both dev servers were driven via Chrome DevTools MCP. Evidence captured as
  screenshots under docs/reviews/screenshots/ and console / DOM snapshots.
  Student dev server initially failed — broken npm install left node_modules
  without binaries (postinstall crashed silently); a manual reinstall was
  required. Captured as FIND-ux-001.
---

## Summary by severity

| sev | count |
|-----|------:|
| p0  | 6 |
| p1  | 7 |
| p2  | 4 |
| p3  | 1 |
| total | 18 |

## Top categories

- **label-drift** (data says one thing, UI labels another, or task IDs / "stub"
  / "placeholder" text leaks into user-facing surfaces)
- **dead-button** (button fires no handler and no network)
- **silent-failure** (request fails and nothing is surfaced to the user)
- **i18n-fallback-leak** (AR/HE leak English)
- **broken-workflow** (entire route dies in dev)

---

## Findings

### FIND-ux-001 (p0, category: ux)
**title**: Student dev server cannot start — broken `dev` script + failed
postinstall leaves no vite binary

evidence:
  - type: file
    content: |
      /Users/shaker/edu-apps/cena/src/student/full-version/package.json#L8
      "dev": "vite --port 5175"
  - type: shell
    content: |
      $ npm run dev
      > cena-student-web@0.0.1 dev
      > vite --port 5175
      sh: vite: command not found
  - type: shell
    content: |
      $ npm install --no-audit --prefer-offline
      (warnings for stylelint peer dep conflicts, then silently exits before
       writing node_modules/.bin; no "added N packages" line)
      $ ls node_modules/.bin/vite
      ls: ... No such file or directory
      $ npm install --ignore-scripts
      (works; .bin/vite present after)
  - type: file
    content: |
      "postinstall": "npm run build:icons && npm run msw:init"
      (the icon build and/or msw init is failing on clean install and taking
       down the whole install, leaving the repo in an unusable state)

impact: Any new contributor cloning the repo cannot run the student web. There
is no documented workaround. This is a broken dev workflow and it directly
caused this review to lose ~10 minutes to recover.

fix-hint: Move icon build + msw init to an explicit `setup` script and make
them tolerant to already-present outputs, or gate postinstall on a sentinel
file. Also add a preflight check in `dev` so the failure is loud.

---

### FIND-ux-002 (p0, category: ux)
**title**: Student web's `pages/index.vue` ships a Vuexy-demo empty state
with a dead "Save" CTA that sits under "No sessions yet" and does nothing

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-003-student-root.png
  - type: file
    content: |
      src/student/full-version/src/pages/index.vue:46-59
      <StudentEmptyState
        :title="t('empty.noSessions')"
        :subtitle="t('empty.noSessionsSubtitle')"
        icon="tabler-books"
      >
        <template #actions>
          <VBtn
            color="primary"
            data-testid="index-empty-cta"
          >
            {{ t('common.save') }}
          </VBtn>
        </template>
      </StudentEmptyState>
  - type: interaction
    content: |
      Clicked uid=5_8 button "Save" — no network requests, no state
      change, no console output, no navigation. The button is literally
      a no-op.

why-p0: The user explicitly banned stubs, mock, and placeholder UX
("NO stubs — production grade", 2026-04-11 memory). This page is the
student web's `/` route. It is what a new user lands on. The page
title literally reads "STU-W-01 design system chassis. Theme, tokens,
locales, and shared components wired up." and the CTA button says
"Save" while the empty-state copy says "Start your first learning
session to see it here." Label-data mismatch, dead button, AND the
screen is self-identifying as a task ticket number.

fix: Delete `pages/index.vue` as a visible route. Make `/` the student
home or redirect it to `/home` / `/login`. If the dev chassis is needed
for internal work, move it under `/_dev/chassis` alongside the other
dev-only pages.

---

### FIND-ux-003 (p0, category: ux, broken-workflow)
**title**: Student web is **completely unusable** the moment the admin app
sets its cookies — MSW cookie parser throws on the admin's space-containing
cookie names and every non-`/` route renders as a blank page

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-004-student-home-msw-broken.png
  - type: screenshot
    content: docs/reviews/screenshots/ux-005-student-login-blank.png
  - type: console
    content: |
      [error] [MSW] Uncaught exception in the request handler for "GET
      http://localhost:5175/node_modules/vuetify/lib/components/VAlert/index.js
      ?v=2d5a6fb7":

      TypeError: argument name is invalid
          at Object.serialize (.../msw.js:181)
          at getAllRequestCookies (.../msw.js:317)
          at HttpHandler.parse (.../msw.js:383)
          ...

      (same error repeats for every dynamic import — Vuetify components,
       Vue SFC styles, auth.vue, StudentAuthCard, etc.)

      [error] TypeError: Failed to fetch dynamically imported module:
      http://localhost:5175/src/layouts/auth.vue

      [warn] [Vue Router warn]: Unexpected error when starting the router:
      TypeError: Failed to fetch dynamically imported module
  - type: evaluate
    content: |
      document.cookie =>
      "vuexy-color-scheme=dark; vuexy-language=en;
       cena admin-color-scheme=dark; cena admin-language=en;
       IronWatch-color-scheme=dark; IronWatch-language=en"

      The cookie names "cena admin-color-scheme" and "cena admin-language"
      contain a literal space character. RFC 6265 / RFC 2616 reserves SP
      from the token grammar, so these names are technically invalid.
      MSW's cookie parser calls cookie-library `serialize()` which
      throws `TypeError: argument name is invalid` when any cookie name
      in the Cookie header contains a space. The exception is thrown
      BEFORE handler routing, so EVERY single request the SW sees —
      including Vite HMR dynamic imports — returns as a 500, which
      breaks the entire app.

  - type: file
    content: |
      src/admin/full-version/themeConfig.ts:12-13
      app: {
        title: 'cena admin' as const,
      }

      src/admin/full-version/src/@layouts/stores/config.ts:8-12
      export const namespaceConfig = (str: string) =>
        `${layoutConfig.app.title}-${str}`
      export const cookieRef = <T>(key: string, defaultValue: T) => {
        return useCookie<T>(namespaceConfig(key),
          { default: () => defaultValue })
      }

      Every admin cookie is namespaced `cena admin-<key>`. Since admin
      (5174) and student (5175) share the `localhost` origin semantics
      only at the scheme/host/port level for cookie scope, BUT cookies
      are actually scoped per host, the spec says they shouldn't cross.
      However in THIS dev setup both apps run under `localhost` without
      an explicit domain and, depending on browser, the cookies leak.
      The repro is reproducible: visit admin, visit student, everything
      breaks. Clearing just `cena admin-*` fixes it.

  - type: repro-steps
    content: |
      1. Start admin dev server (`npm run dev` in src/admin/full-version)
      2. Start student dev server (`npm run dev` in src/student/full-version)
      3. Visit http://localhost:5174/ → admin login renders, sets
         `cena admin-color-scheme` + `cena admin-language` cookies
      4. Visit http://localhost:5175/home → blank page; console shows
         dozens of MSW cookie parse errors and `Failed to fetch
         dynamically imported module: auth.vue`
      5. In devtools: `document.cookie = 'cena admin-color-scheme=;expires=
         Thu, 01 Jan 1970 00:00:00 GMT;path=/'`
      6. Reload: page works.

why-p0:
  - The student app is the product. It is unusable in the local dev
    environment that the repo's own `dev.Dockerfile` / compose files
    promote.
  - The root cause is ALSO a separate label-drift / correctness bug in
    admin (cookie names must not contain SP per RFC 6265). A stricter
    server-side cookie parser on the .NET or Node backend will drop the
    Cookie header altogether, making admin auth unusable in prod too.
  - Three unrelated dev sessions in a row hit this before the review
    found the cause; no error message in the student app hints at
    cookies as the culprit.

fix:
  1. `src/admin/full-version/themeConfig.ts` line 13: change `'cena admin'`
     to `'cena-admin'` (hyphen, not space). This is a one-line fix and
     ripples to ~8 cookies.
  2. Add a migration that reads old `cena admin-*` cookies on first
     load and rewrites them as `cena-admin-*`, then expires the old
     ones. Otherwise existing sessions carry the poison forever.
  3. Harden `src/student/full-version/src/plugins/fake-api/index.ts` so
     MSW's `onUnhandledRequest: 'bypass'` also bypasses requests whose
     cookie parsing throws — i.e., wrap the handler pipeline in a
     try/catch that falls back to real-fetch. MSW should never take
     down HMR.

---

### FIND-ux-004 (p0, category: label-drift)
**title**: Student home `/home` renders KPIs ("Minutes today: 18",
"Questions: 84", "Accuracy: 76%", "Level 7: 40%") as hard-coded constants
with **zero** data source. The labels claim "today's stats" but the
numbers never change.

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-007-student-home-mock-data.png
  - type: file
    content: |
      src/student/full-version/src/pages/home.vue:24-49
      // Constants that STB-00 doesn't return yet — STU-W-05C wires them
      // when STB-02 (plan/review/recommendations) lands and STU-W-05D
      // picks up live values from SignalR.
      const MOCK_MINUTES_TODAY = 18
      const MOCK_QUESTIONS_TODAY = 84
      const MOCK_ACCURACY = 76
      const MOCK_FLOW_STATE: FlowState = 'approaching'

      // Derived values from the real /api/me payload.
      const level = computed(() => me.value?.level ?? 1)
      const streakDays = computed(() => me.value?.streakDays ?? 0)

      const xpProgressPercent = computed(() => {
        // STB-00's MeBootstrapDto returns level but not xp-within-level.
        // Stub 40% as a visual placeholder until STB-03 lands the
        // real gamification endpoint with xp + xpToNextLevel.
        return 40
      })
  - type: file
    content: |
      src/student/full-version/src/pages/home.vue:121-148
      <KpiCard label="Minutes today" :value="MOCK_MINUTES_TODAY" ...
      <KpiCard label="Questions"     :value="MOCK_QUESTIONS_TODAY" ...
      <KpiCard label="Accuracy"      :value="`${MOCK_ACCURACY}%`" ...
      <KpiCard :label="`Level ${level}`" :value="`${xpProgressPercent}%`" ...

why-p0:
  - User's explicit memory rule: "Labels match data — UI labels, API keys,
    and variable names must describe what the data actually is"
  - User's explicit rule: "NO stubs — production grade" (2026-04-11)
  - This is literally the home screen. The first thing a student sees
    after signing in is 4 KPIs that look real, are labelled "today", and
    are in fact compile-time constants. Every student sees the same
    numbers. The FlowAmbientBackground is also driven by MOCK_FLOW_STATE
    = 'approaching' regardless of actual flow state.

fix:
  - Remove MOCK_* and render a loading skeleton (already present) / empty
    state for these KPIs until STB-02/STB-03 lands. Better: hide the
    KPI section entirely with a "Your stats will show here after your
    first session" empty state.
  - DO NOT ship a screen that displays fabricated numbers labelled as
    real data.

---

### FIND-ux-005 (p0, category: label-drift)
**title**: Student AI tutor responses leak the task ID `(STB-04b will wire
real LLM streaming.)` and the word "stub" directly into user-facing chat
bubbles

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-010-tutor-task-id-leak.png
  - type: screenshot
    content: docs/reviews/screenshots/ux-011-tutor-stub-reply.png
  - type: interaction
    content: |
      Navigate to /tutor, click "Help with quadratic equations" thread.
      Visible message history:

      USER: How do I solve x² − 5x + 6 = 0 using the quadratic formula?
      ASSISTANT: Great question! For x² − 5x + 6 = 0, we have a=1, b=−5,
        c=6. Using the quadratic formula: x = (5 ± √(25 − 24)) / 2
        = (5 ± 1) / 2, which gives x = 3 or x = 2. (STB-04b will wire
        real LLM streaming.)
      USER: What if the discriminant is negative?
      ASSISTANT: Great question! If b² − 4ac is negative, the equation
        has no real solutions — only complex ones involving the
        imaginary unit i. (STB-04b will wire real LLM streaming.)

      Send a new message "What is 2 + 2":

      ASSISTANT: Great question! (STB-04b will wire real LLM streaming.)
      For now, here's a stub reply that echoes the spirit of your
      question so you can test the chat UI end-to-end.

  - type: file
    content: |
      src/student/full-version/src/plugins/fake-api/handlers/student-tutor/index.ts:78
      content: 'Great question! For x² − 5x + 6 = 0 ... (STB-04b will wire real LLM streaming.)'
      ... (lines 94, 113, 195 all contain the same leak)

why-p0: Task-tracker IDs in user-facing text. The word "stub" is visible
to the user. Exact prohibition from user memory: "NO stubs — production
grade".

fix:
  - Rewrite the handlers without mentioning any task ID or the word "stub".
    If they must remain as dev fixtures they must read like plausible
    mock content a designer could show in a demo video.
  - Add an eslint rule or pre-commit grep that forbids `STB-\d|STU-W-\d`
    strings inside `src/student/full-version/src/plugins/fake-api/handlers/**`
    content fields.

---

### FIND-ux-006 (p0, category: ux)
**title**: Student "Forgot password" form silently drops the submitted
email — no API call, no email ever sent

evidence:
  - type: file
    content: |
      src/student/full-version/src/pages/forgot-password.vue:24-43
      async function handleSubmit() {
        ...
        loading.value = true
        await new Promise(resolve => setTimeout(resolve, 120))
        loading.value = false

        // Phase A: no real Firebase call. STU-W-04C wires `sendPasswordResetEmail`.
        submitted.value = true
      }

      The function literally waits 120ms with `setTimeout` and flips a
      boolean. There is no network call, no Firebase call, no backend
      call. The UI then shows a success state that claims the email
      was sent.

why-p0:
  - A real student locked out of their account would submit this form,
    see a success screen, and never receive the email. They have no
    recourse and no way to know the form didn't actually do anything.
  - This is a silent drop of user input on a critical account-recovery
    flow.

fix: Either gate the route behind a dev-only check so it's not reachable
in production, or wire `sendPasswordResetEmail` now — do not ship a
success UI that lies.

---

### FIND-ux-007 (p1, category: label-drift)
**title**: Student home renders entirely in English when locale=he (Hebrew)
or ar (Arabic); the i18n bundle is ~30–60% populated and most page
strings are hardcoded in templates

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-014-student-arabic-leak.png
    note: "locale=ar, dir=rtl — but page title, 'Theme:', 'dark',
           'Toggle Theme', 'Design System', 'Flow States' all still English"
  - type: screenshot
    content: docs/reviews/screenshots/ux-017-student-home-mobile-hebrew-leak.png
    note: "locale=he, dir=rtl — /home page. Title bar, mobile footer nav
           correctly show Hebrew, but main content ('Good afternoon,
           student@cena.local', 'Today's stats', 'MINUTES TODAY',
           'QUESTIONS', 'ACCURACY', 'LEVEL 7', 'Quick actions', 'Start
           Session', 'Ask the Tutor', 'Daily Challenge', 'View Progress',
           card descriptions) is all English."
  - type: shell
    content: |
      $ wc -l src/student/full-version/src/plugins/i18n/locales/*.json
      773 ar.json
      805 en.json
      577 he.json
      2155 total

      HE bundle is 28% smaller than EN — 228 key-lines missing.
  - type: shell
    content: |
      $ grep -i "afternoon\|greeting" en.json
      652: "morning": "Good morning, {name}",
      653: "afternoon": "Good afternoon, {name}",
      654: "evening": "Good evening, {name}",
      $ grep -i "afternoon\|greeting" ar.json he.json
      (no matches)

      So the home greeting is a literal `Good afternoon, {name}` in the
      template. EN has the key but the template doesn't use it. AR + HE
      don't have the key at all.
  - type: file
    content: |
      src/student/full-version/src/pages/home.vue:115-160
      <h2 id="home-kpis-heading" class="sr-only">Today's stats</h2>
      <KpiCard label="Minutes today" ...
      <KpiCard label="Questions" ...
      <KpiCard label="Accuracy" ...
      <KpiCard :label="`Level ${level}`" ...
      <h2 id="home-quick-heading" class="text-h6 mb-3">Quick actions</h2>

      Every one of these is a hard-coded English literal instead of
      `$t('home.kpi.minutes')` etc.

why-p1:
  - User memory rule: "Arabic/Hebrew secondary, Hebrew hideable outside Israel"
    — the explicit intent is AR/HE first-class. They are not.
  - The Hebrew bundle is so incomplete that a Hebrew user still sees the
    majority of the screen in English. This is a label-drift: the locale
    claims to be Hebrew; the content is mostly English.

fix:
  - Replace every hardcoded string literal in pages/home.vue, pages/session,
    pages/tutor, pages/progress, pages/challenges, pages/social with
    `$t('...')` calls.
  - Backfill `he.json` and `ar.json` with the missing 228 / 32 keys.
  - Add a CI step: run vue-i18n-extract against the SFC templates and
    fail the build if any hardcoded string > 3 chars is found in
    `/pages/**.vue`.

---

### FIND-ux-008 (p1, category: label-drift)
**title**: Admin document title is literally `Vuexy - Vuejs Admin Dashboard
Template` across every route

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-018-admin-title-vuexy.png
  - type: file
    content: |
      src/admin/full-version/index.html:8
      <title>Vuexy - Vuejs Admin Dashboard Template</title>
  - type: interaction
    content: |
      All admin routes (login, dashboards/crm, dashboards/admin,
      /404) carry the tab title "Vuexy - Vuejs Admin Dashboard Template".
      The visible branding in the navbar/headline says "Cena Admin" on
      login but the browser tab still says Vuexy.

why-p1: The browser tab is user-facing. It's the first label a user sees
when they're skimming tabs. It directly contradicts the product identity
locked by the project. Also leaks the upstream template name.

fix: Hardcode `<title>Cena Admin</title>` in index.html AND add a
router.afterEach hook that sets `document.title = '<page> · Cena Admin'`
using the same `meta.title` key already wired in the student web.

---

### FIND-ux-009 (p1, category: label-drift)
**title**: Student sidebar logo/link reads "Vuexy" for every logged-in
route (home, tutor, progress, session, settings, etc.)

evidence:
  - type: snapshot
    content: |
      Navigated to /home, /tutor, /tutor/th-1, /session, /progress.
      Every single one shows the sidebar link as:
        uid=NN link "Vuexy" url="http://localhost:5175/"
          uid=NN heading "Vuexy" level="1"
  - type: screenshot
    content: docs/reviews/screenshots/ux-017-student-home-mobile-hebrew-leak.png
    note: "Visible 'Vuexy' H1 in the top-left of the student sidebar"

why-p1: Label-data mismatch on the core brand identity. User is in a
"Cena" app and the h1 of the sidebar says "Vuexy". Same severity as
FIND-ux-008 for the admin.

fix: Change `themeConfig.ts` line `title: 'vuexy'` → `title: 'Cena'`.
One-line fix. Cascade into the i18n `themeConfig.app.title` key.

---

### FIND-ux-010 (p1, category: ux)
**title**: Student auth state does not survive hard navigation / refresh —
every URL share, refresh, or full navigation bounces to /login

evidence:
  - type: interaction
    content: |
      1. POST /login with mock creds → /home renders
      2. Navigate to http://localhost:5175/tutor via url bar
      3. Redirected to /login?returnTo=/tutor — auth lost
      4. Same for /progress, /session, /notifications

      Across-tab: open a second tab to /tutor — redirects to /login.
  - type: file
    content: |
      src/student/full-version/src/stores/authStore.ts:10-54
      export const useAuthStore = defineStore('auth', () => {
        const uid = ref<string | null>(null)
        const email = ref<string | null>(null)
        ...
      })

      No persisted pinia plugin, no IndexedDB, no cookie set by the mock
      sign-in. Every reload wipes state. `__mockSignIn` only touches
      in-memory refs.

why-p1:
  - Real Firebase SDK handles this via IndexedDB, but the dev-loop uses
    the mock. Right now the student UX is "you can log in, but if you
    share the URL with a friend, both of you get bounced to login."
  - This mock is exactly what the user would see in any E2E test that
    uses the default mock login flow. STU-W-04's real Firebase wiring
    will eventually fix this, but until then every click that causes a
    full navigation (e.g. `/tutor/th-1` deep link) breaks.

fix: Persist the mock uid to `sessionStorage` (or a regular cookie) and
restore it in authStore's init. Real Firebase SDK will replace it later.

---

### FIND-ux-011 (p1, category: silent-failure)
**title**: Social `handleAccept` (friend request) and `handleVote` (peer
upvote/downvote) swallow errors — the click appears to succeed even
when the API returns 500

evidence:
  - type: file
    content: |
      src/student/full-version/src/pages/social/friends.vue:24-32
      async function handleAccept(requestId: string) {
        try {
          await $api(`/api/social/friends/${requestId}/accept`,
            { method: 'POST' as any, body: {} as any })
          friendsQuery.refresh()
        }
        catch {
          // swallow
        }
      }

      src/student/full-version/src/pages/social/peers.vue:24-35
      async function handleVote(solutionId: string, direction: 'up' | 'down') {
        try {
          await $api(`/api/social/peers/solutions/${solutionId}/vote`, ...)
          solutionsQuery.refresh()
        }
        catch {
          // swallow — would surface a snackbar in 12b
        }
      }

why-p1: A user clicks "Accept friend" / "Upvote peer solution", the
request fails, the button stops spinning, the list refreshes but the
item didn't actually change — the user has no way to know whether it
worked. This is exactly the "loading spinner / no error surface"
anti-pattern.

fix: Wire a toast / snackbar error surface. The repo already has
`VAlert` and `useSnackbar` equivalents in shared components — use them
instead of a TODO comment.

---

### FIND-ux-012 (p1, category: ux)
**title**: Keyboard shortcut handler intercepts `?` key even while a
textarea is focused — typing `?` anywhere in the tutor chat opens the
keyboard-shortcuts modal and kills the input

evidence:
  - type: interaction
    content: |
      1. Login → /tutor/th-1
      2. Focus the "Ask your tutor anything…" textarea
      3. Type "What is 2 + 2?"
      4. The "?" triggers the Keyboard Shortcuts dialog mid-typing.
         The textarea value becomes "What is 2 + 2" (the ? is eaten)
         and a modal pops over the chat.
  - type: screenshot
    content: docs/reviews/screenshots/ux-011-tutor-stub-reply.png
    note: "after dismissing the modal, the sent message is 'What is 2 + 2'
           — the user lost their ? character"
  - type: file
    content: |
      src/student/full-version/src/components/shell/ShellShortcuts.vue
      (Wired in STU-W-15 Phase A — per the file comment)

why-p1: The tutor page NEEDS users to type questions — including the `?`
character at the end of every question. Right now any question ending
in `?` silently drops the `?` and opens a modal. That's the textbook
definition of "form silently drops input".

fix: The shortcut handler must check `document.activeElement` and bail
out if it's an `INPUT`, `TEXTAREA`, or `contenteditable`. Same for
the `g h`, `g s`, `g p`, `g t` go-to shortcuts — typing "gaspard"
in a textarea right now probably navigates away.

---

### FIND-ux-013 (p1, category: label-drift)
**title**: Student progress leaderboard renders the current user as
"Dev Student" / "You" regardless of the logged-in email (`student@cena.local`
in our repro)

evidence:
  - type: screenshot
    content: docs/reviews/screenshots/ux-012-progress-leaderboard-dev-student.png
  - type: interaction
    content: |
      Logged in as student@cena.local. Navigated to /progress.
      Leaderboard panel lists:
        #1 Alex Chen    2400 XP
        #2 Priya Rao    2250 XP
        #3 Jordan Smith 2100 XP
        #4 Sam Park     1950 XP
        #5 Dev Student (You) 1800 XP

      The "You" label is pinned to a stub user "Dev Student" coming out
      of the leaderboard handler, not the authenticated user.

why-p1: Label says "You" but the data is for someone else. The home
greeting correctly says "Good afternoon, student@cena.local" — but on
the same session on another page the same user is "Dev Student". Two
sources of truth for the same thing.

fix: In the leaderboard MSW handler, inject the authed user's
uid/email/displayName as the "current user" row instead of hardcoding
"Dev Student".

---

### FIND-ux-014 (p1, category: label-drift)
**title**: Hebrew is always visible in the language switcher; user rule is
"Hebrew hideable outside Israel"

evidence:
  - type: snapshot
    content: |
      LanguageSwitcher menu on `/` always lists:
      - English
      - العربية
      - עברית

      There is no geolocation gate, no user preference, no build flag.
  - type: shell
    content: |
      $ grep -ri "israel\|hideHebrew\|geolocation" src/student/full-version/src/
      (only matches: an unrelated invoice mock db says 'Israel' as a
       country, and the locale list is a static ['en','ar','he'].)

why-p1: User's 2026-03-27 memory rule states Hebrew must be hideable
outside Israel. Right now it's hardcoded on for everyone. This is a
label-drift: the UI claims the app supports locale-gating but the code
doesn't.

fix: Add a runtime flag `import.meta.env.VITE_ENABLE_HEBREW` or a
geolocation-driven check, and filter the `LanguageSwitcher` options
accordingly.

---

### FIND-ux-015 (p2, category: ux)
**title**: Admin `system/health` silently swallows `fetchActorNodes` errors;
the Actor Nodes widget renders "0 active actors" without any "API
unavailable" indicator when the backend is down

evidence:
  - type: file
    content: |
      src/admin/full-version/src/pages/apps/system/health.vue:140-156
      const fetchActorNodes = async () => {
        try {
          const data = await $api<ActorNode[]>('/admin/system/actors')
          actorNodes.value = (data ?? []).map(...)
        }
        catch {
          // Actor data unavailable
        }
      }

      Compare to fetchHealth / fetchMetrics in the same file which
      DO set `error.value`.

why-p2: An admin looking at the system-health dashboard sees zero actors
and concludes "no load" when in fact the API is down. Inconsistent
error handling within the same file.

fix: Set `error.value = 'Actor node API unavailable'` in the catch, or
render a distinct "unavailable" chip on the Actor Nodes widget.

---

### FIND-ux-016 (p2, category: label-drift)
**title**: Student `settings/notifications` page claims to save preferences
but only persists to `localStorage` — there is no backend write and the
UI implies network persistence

evidence:
  - type: file
    content: |
      src/student/full-version/src/pages/settings/notifications.vue:19-46
      // Phase A: local toggles persist to localStorage only.
      // STU-W-14b will wire /api/me/settings when STB-00b settings writes land.
      function persist() {
        if (typeof localStorage !== 'undefined')
          localStorage.setItem('cena-notification-prefs',
            JSON.stringify(prefs.value))
      }

      Every toggle switch's @update:model-value calls persist() which
      only writes localStorage. No POST, no PATCH, no $api call.

why-p2: A user changes their notification preferences on their phone;
logs in on their laptop; sees the default preferences back. The UI
never hints that preferences are local-only.

fix: Add a "saved to this device only — will sync when STU-W-14b lands"
inline hint, OR wire the real API now.

---

### FIND-ux-017 (p2, category: label-drift)
**title**: Student web notification bell / badge has accessible name "Badge"
(Vuetify default) instead of a meaningful label like "Notifications"

evidence:
  - type: snapshot
    content: |
      In every /home, /tutor, /progress snapshot the top banner shows:
        uid=NN button "Badge" expandable haspopup="menu"
          uid=NN status "Badge" atomic live="polite"
      In Hebrew it localizes to "תג" (tag) — the Vuetify default slot
      name, not a real label.
  - type: file
    content: |
      src/student/full-version/src/layouts/components/DefaultLayoutWithVerticalNav.vue:10-11
      //   - NavBarNotifications: placeholder notifications bell — will be wired
      //     by STU-W-14 against STB-07's notification endpoint

why-p2: Screen reader users cannot find the notifications bell. Also
label-drift — the button is called "Badge" in the a11y tree but it's
actually a notifications menu.

fix: Add `aria-label="Notifications"` / the i18n equivalent to the button
element. Translate it.

---

### FIND-ux-018 (p3, category: ux)
**title**: Admin login password-visibility toggle has accessible name
"Append" (Vuetify v-text-field slot name leaking into a11y tree)

evidence:
  - type: snapshot
    content: |
      http://localhost:5174/login
      uid=1_10 button "Append"  ← the eye-icon to show password
  - type: screenshot
    content: docs/reviews/screenshots/ux-001-admin-login.png

why-p3: Minor a11y label-drift. "Append" tells no one anything. Low
priority because sighted users still see the eye icon.

fix: `<VTextField append-inner-icon="...">` → use
`append-inner-icon` with an explicit `aria-label`, or wrap the icon in
an explicitly-labelled IconButton.

---

## Mode notes

- Live mode confirmed for both servers.
- Admin dev server: reachable at http://localhost:5174/, clean console on /login.
- Student dev server: required a manual reinstall (`npm install --ignore-scripts`)
  before it would start. See FIND-ux-001.
- Both servers shut down cleanly at end of review.
- Evidence captured in docs/reviews/screenshots/ (19 PNGs).
- Static SFC analysis used as complementary evidence alongside live MCP driving.
