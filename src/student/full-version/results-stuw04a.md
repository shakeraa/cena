# STU-W-04A — Auth UI Phase A (Results)

## 1. Summary

STU-W-04A ships real `/login`, `/register`, and `/forgot-password` pages with email+password forms wired to the existing `authStore.__mockSignIn` from STU-W-02. Replaces the "Not implemented yet" placeholders that STU-W-02 had left for the auth routes. Email+password only — OAuth providers (Google/Apple/MS/phone) land in STU-W-04B, the 7-step onboarding wizard lands in STU-W-04C, edge cases (COPPA, parental gate, guest trial, multi-tab) in STU-W-04D. Worker: `claude-code` (branch off STU-W-03).

**Cumulative at HEAD**: 0 lint errors, 66/66 unit tests passing, 24/24 Playwright E2E passing (6 STU-W-01 + 11 STU-W-02 + 7 STU-W-04A).

## 2. Files added / modified

### Added

- `src/components/common/StudentAuthCard.vue` — centered card with title + subtitle + default slot + optional footer slot. Renders inside the `auth.vue` layout from STU-W-01. 440px max-width, elevation 8, border-separated footer.
- `src/components/common/EmailPasswordForm.vue` — VForm with email + password (+ display name in register mode) fields, client-side validation (required + email regex + 6-char password), `submit` event emitter. Accepts `loading`, `errorMessage`, `submitLocked`, `lockedSecondsRemaining` props. CTA label auto-switches between "Sign in" / "Create account" / "Try again in Ns".
- `tests/unit/EmailPasswordForm.spec.ts` — 8 tests covering login/register mode differences, empty-submit validation, invalid-email/short-password rejection, submit payload shape (login + register), and `submitLocked` prop behavior.
- `tests/e2e/stuw04a.spec.ts` — 7 Playwright tests covering the full UX matrix.

### Modified

- `src/pages/login.vue` — replaced placeholder with real UI: StudentAuthCard + EmailPasswordForm + forgot-password link + register link in footer. Handles:
  - `fail@test.com` → inline error, increments failed-attempts counter
  - 3 failed attempts → soft lockout for 5 s (submit disabled, CTA shows countdown, countdown ticks every 250 ms)
  - Success → `authStore.__mockSignIn` + `meStore.__setProfile` with `onboardedAt` set → navigate to sanitized `returnTo` query param or `/home`
  - Proper timer cleanup on unmount
- `src/pages/register.vue` — replaced placeholder with real UI. Mode=register of EmailPasswordForm adds display name field. Handles:
  - `exists@test.com` → inline "email already exists" error
  - Success → `authStore.__mockSignIn` + `meStore.__setProfile` with `onboardedAt: null` → navigate to `/onboarding` (placeholder remains — wizard lands in STU-W-04C)
- `src/pages/forgot-password.vue` — replaced placeholder with real UI: inline-validated email input + submit → optimistic "check your email" confirmation card. No real Firebase `sendPasswordResetEmail` yet (STU-W-04C will wire it).
- `src/plugins/i18n/locales/en.json`, `ar.json`, `he.json` — added 32 new keys under an `auth.*` namespace (signIn, signUp, forgotPassword, email, password, displayName, CTAs, link labels, page titles, validation errors, error-state messages). All three locales hand-written, not machine-translated.
- `tests/unit/setup.ts` — added the auth keys to the mock i18n messages so component tests can resolve them.

### Unchanged (verified)

- `src/layouts/auth.vue` (STU-W-01 — already good)
- `src/stores/authStore.ts` (STU-W-02 — consumed as-is)
- `src/stores/meStore.ts` (STU-W-02 — consumed as-is)
- `src/utils/returnTo.ts` (STU-W-02 — consumed as-is)
- `src/plugins/1.router/guards.ts` (STU-W-02 — no changes needed)

## 3. Quality gates

```
$ npm run lint
✖ 5 problems (0 errors, 5 warnings) — all pre-existing

$ npm run build
✓ built in 11.7s

$ npm run test:unit
Test Files  12 passed (12)
     Tests  66 passed (66)

$ npm run test:e2e
  24 passed (1.2m)
```

## 4. E2E transcripts (stuw04a)

```
✓ E2E #1 /login renders form and submits valid credentials → /home
✓ E2E #2 /login?returnTo=/progress/mastery → sign in → lands at returnTo
✓ E2E #3 /login rejects fail@test.com with inline error
✓ E2E #3b /login soft-locks submit after 3 failed attempts
✓ E2E #4 /register fills form, submits → /onboarding
✓ E2E #5 /forgot-password submits email and shows confirmation
✓ E2E #6 auth pages pass axe in light mode (color-contrast disabled per STU-A11Y-CONTRAST exemption)
```

## 5. Screenshots

Under `test-results/stuw04a/`:

```
login-empty.png      login-error.png      register-filled.png      forgot-confirmed.png
```

## 6. Insights for the coordinator / next phases

**1. The `@` character in vue-i18n message values is a link-syntax trap**: I initially set `auth.emailPlaceholder` to `"you@example.com"` in all three locale files. vue-i18n's message compiler treats `@` as the start of a linked message (like `@:common.foo`), which caused the production build to fail with "Message compilation error: Unexpected lexical analysis". The warnings fired during unit tests but were swallowed (tests still passed), which masked the problem until I ran `npm run build`. **Rule for future locale work**: never use literal `@` in message values without escaping — either use `{'@'}` interpolation or rephrase to avoid the character entirely. I went with "Enter your email" / "أدخل بريدك الإلكتروني" / "הזן את הדוא״ל שלך" as the placeholder copy.

**2. Playwright `waitForURL(regex)` with path-in-query is a subtle test bug**: The test `waitForURL(/\/progress\/mastery/)` returned IMMEDIATELY when the page was still on `/login?returnTo=/progress/mastery`, because the regex matches anywhere in the URL string including the query. Then the `expect(pathname).toBe('/progress/mastery')` checked the current pathname (`/login`) and failed. **Fix**: use a predicate function — `waitForURL(url => new URL(url).pathname === '/progress/mastery')`. Applied this fix to all three navigation assertions in stuw04a.

**3. Axe `link-in-text-block-style` caught real a11y issues**: The auth-page footer links (`RouterLink` to `/register`, `/login`, `/forgot-password`) initially had `text-decoration-none` or no decoration styling at all. Axe flagged this — inline links inside a text block need a visual cue beyond color alone to distinguish them from surrounding text, especially when color contrast is marginal. Added `text-decoration-underline` to all four links. **Follow-up for STU-W-04B**: when OAuth provider buttons land, use `<VBtn variant="text">` for them — buttons self-style and don't hit this rule.

**4. The STU-W-03 msw service worker fix recurs per worktree**: Fresh worktrees that symlink `node_modules` from another worktree don't get `public/mockServiceWorker.js` because that file is generated by `msw init public/` in the postinstall script, which only runs during actual `npm install`. I copied it manually at worktree setup. **Strong recommendation**: commit `public/mockServiceWorker.js` in a small cleanup PR so future worktrees don't trip on this. Flagged in STU-W-03 insights too.

**5. Mock-backend sentinels for tests**: The login page treats `fail@test.com` as "wrong credentials" and register treats `exists@test.com` as "already in use". These are hard-coded strings and will be replaced when real Firebase lands. Document them in the AGENTS.md / QUEUE.md so backend folks don't mistakenly register real test accounts with those addresses.

**6. Soft lockout UX**: after 3 failed attempts, submit disables for 5 seconds. The countdown ticks every 250 ms for smooth visual feedback. The CTA label switches to "Try again in Ns" via the i18n interpolation. Timer cleanup is wired via `onBeforeUnmount` to avoid interval leaks if the user navigates away mid-lockout. Real Firebase enforces its own server-side throttle; the client-side UX is layered on top to give immediate feedback without a round-trip.

**7. What's deferred to later phases**:
- **STU-W-04B (OAuth providers)**: Google / Apple / Microsoft / phone-SMS sign-in buttons + `@firebase/auth` SDK wiring + `accessTokenFactory` hookup for SignalR
- **STU-W-04C (onboarding wizard)**: 7-step wizard (welcome → role → language → subjects → goals → diagnostic → confirm) + `onboardingStore` Pinia store with localStorage resume + `POST /api/me/onboarding` call (needs STB-00)
- **STU-W-04D (edge cases)**: COPPA under-13 parental-consent flow, classroom code entry, guest trial with conversion prompt, multi-tab BroadcastChannel detection

Each of these is a separate scoped queue task. None block Wave 2 feature work — the mock sign-in in 04A is enough to let feature tasks (home dashboard, etc.) develop against a signed-in state.

**8. Unit tests for the page-level logic**: I added 8 tests for `EmailPasswordForm.vue` but NOT for `login.vue` / `register.vue` / `forgot-password.vue`. Page-level integration is covered by the 7 Playwright E2E tests which exercise the full browser flow including router navigation. Unit-testing the pages would require mounting with a router + pinia + i18n harness that's more complex than the E2E value. Documented for STU-W-04B so that task can match the pattern.

## 7. Acceptance criteria

Against the scoped DoD in the Phase A task body:

- [x] `StudentAuthCard.vue` and `EmailPasswordForm.vue` exist and are auto-imported (verified via unit tests and E2E)
- [x] `/login`, `/register`, `/forgot-password` pages render a real UI (verified via E2E screenshots)
- [x] `/login` submit → mockSignIn → navigate to sanitized returnTo or `/home` (E2E #1, #2)
- [x] `/register` submit → mockSignIn → navigate to `/onboarding` (E2E #4)
- [x] `/forgot-password` shows a "check your email" confirmation on submit (E2E #5)
- [x] Soft lockout after 3 failed attempts on `/login` (E2E #3b)
- [x] 32+ new i18n keys present in en/ar/he
- [x] 8 new unit tests pass (required 7+)
- [x] 7 new Playwright E2E tests pass (required 6)
- [x] Existing STU-W-01/02/03 tests still pass (17 existing + 7 new = 24/24 E2E, 58 existing + 8 new = 66/66 unit)
- [x] Lint clean (0 errors)
- [x] Build clean
- [x] Branch `claude-code/t_91bc268dc7ad-auth-ui-phase-a` pushed

## 8. What I did NOT do and why

- **Real Firebase Auth SDK** — deferred to STU-W-04B. The current flow goes through the existing `authStore.__mockSignIn` which was designed in STU-W-02 specifically for this.
- **OAuth provider buttons** — deferred to STU-W-04B. Phase A is email+password only.
- **7-step onboarding wizard** — deferred to STU-W-04C. The placeholder `/onboarding` page stays as-is for now.
- **COPPA / parental gate / guest trial / multi-tab** — deferred to STU-W-04D.
- **OAuth provider buttons accessibility deep dive** — will come with Phase B.
- **Integration tests that actually talk to Firebase** — deferred until STU-W-04B wires the real SDK.
- **`public/mockServiceWorker.js` commit** — explicitly out of scope; should be a standalone cleanup PR.
