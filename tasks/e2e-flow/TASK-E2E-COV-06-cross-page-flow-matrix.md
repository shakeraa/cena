# TASK-E2E-COV-06: Cross-page flow matrix — pairwise nav + deep-link + returnTo

**Status**: Proposed
**Priority**: P1
**Epic**: Coverage matrix
**Tag**: `@coverage @cross-page @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-X-cross-page-matrix.spec.ts` (new)

## Why this exists

The current `EPIC-X-cross-page-journey.spec.ts` covers ONE student walk (6 routes via `page.goto`) and ONE admin sidebar drive (3 routes by visible label). Real user behaviour is messier:

- **Deep links from emails / notifications** — user clicks a `/parent/dashboard?child=X&period=week` link from a digest email and lands directly on a parameterized route. Does the SPA hydrate state correctly? Does it survive a refresh?
- **`returnTo` survival** — user is signed-out, hits `/account/subscription`, gets bounced to `/login?returnTo=/account/subscription`, signs in, and should land back on `/account/subscription` (not `/home`).
- **Browser back-forward across auth state changes** — user signs out from `/profile`, browser back goes to `/profile` (route guard bounces to /login? Or rehydrates? Documented behaviour matters).
- **In-flight request preservation across nav** — start a tutor request, click the side-nav before the response arrives. Does the request abort cleanly or land in a stale page?
- **PWA install + reload** — install the PWA, close it, reopen — auth state survives via Firebase IndexedDB.

This task closes the matrix.

## Sub-flows to assert

| Flow | Assertion |
| --- | --- |
| Deep-link from email | `/parent/dashboard?child=X` lands directly on dashboard with X selected; refresh keeps state |
| `returnTo` survival | `/account/subscription` → `/login?returnTo=...` → submit → `/account/subscription` (NOT /home) |
| Back across sign-out | sign out from `/profile` → browser back → bounced to `/login` (route guard fires) |
| Back across sign-in | `/login` → submit → `/home` → browser back → guard does NOT bounce to `/login` (no infinite loop) |
| In-flight nav abort | start a slow API call on `/tutor`, click `/home` mid-flight — no console-error from the aborted request |
| PWA reload session | install PWA (`beforeinstallprompt` mock) → close → reopen → still signed in |
| Tab-restore session | open `/home` in tab A, sign out in tab A, tab B (still on `/home`) detects the sign-out and bounces |
| Locale survives nav | toggle locale to `ar` on `/settings/appearance` → navigate to `/home` → `dir=rtl` preserved |
| Theme survives nav | toggle dark mode → navigate → theme preserved |
| Onboarding redirect | not-onboarded user hits `/home` → guard sends to `/onboarding` → completes → `/home` is reachable |

## Boundary assertions

| Boundary | Assertion |
| --- | --- |
| Router | `router.currentRoute.value.path` equals expected after each transition |
| Auth | `authStore.isSignedIn` matches the expected post-flow state |
| Network | No 4xx/5xx caused by stale tokens after sign-out |
| Storage | `cena-mock-me` / Firebase IndexedDB / `cena-student-locale` reflect the expected post-flow state |

## Regression this catches

- The classic `returnTo` regression: SPA strips the query param on first redirect and you can never come back to where you were
- A multi-tab regression where tab B keeps a CASL ability cookie after tab A signed out
- A locale-toggle that the router guard quietly resets on next nav
- A guard infinite-loop after sign-in (sign-in → /home → guard sees stale state → /login → ...)

## Done when

- [ ] All 10 sub-flows in this task are spec'd
- [ ] At least one flow runs in `ar` and `he` to catch RTL-specific routing bugs
- [ ] Tagged `@cross-page @p1`
- [ ] Diagnostics-collection per flow (console + page errors + 4xx/5xx)
