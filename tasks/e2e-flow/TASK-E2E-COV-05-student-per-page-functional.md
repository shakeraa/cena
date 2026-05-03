# TASK-E2E-COV-05: Per-page functional matrix — student SPA

**Status**: Proposed
**Priority**: P1
**Epic**: Coverage matrix (extends EPIC-E2E-A through E + K)
**Tag**: `@coverage @functional @student @p1`
**Spec path**: `src/student/full-version/tests/e2e-flow/workflows/EPIC-X-student-per-page-{area}.spec.ts` (one per area)
**Prereqs**:
- TASK-E2E-INFRA-03 (dynamic-route seed fixture) for `/parent/dashboard`, `/session`, `/tutor/[threadId]`, etc.
- Question-bank seeder for the practice/session pages

## Why this exists

Per-epic specs cover registration, sign-in, sign-out, password reset, parent-bind, sub flows, photo-upload, consent, parent-dashboard, offline banner, RTL. That's ~10 pages with deep coverage. **The other 19+ student pages have only smoke**: notifications, knowledge-graph, /progress/{mastery,sessions,time}, /social/{leaderboard,peers,friends}, /challenges/{daily,boss}, /settings/{appearance,notifications,privacy,study-plan}, /subscription/cancel, /accessibility-statement, /privacy/children, etc.

Real students click filter chips, send messages on /social, claim daily challenge rewards, edit their study plan. None of those interactions are asserted today.

## Sub-specs

| Spec | Surface | Primary actions |
| --- | --- | --- |
| `EPIC-X-student-progress.spec.ts` | `/progress` + `/progress/mastery` + `/progress/sessions` + `/progress/time` | filter by date range, drill into a session, mastery delta visible |
| `EPIC-X-student-social.spec.ts` | `/social` + `/social/leaderboard` + `/social/peers` + `/social/friends` | leaderboard scope toggle (global / class / friends), add a friend, view peer solution |
| `EPIC-X-student-challenges.spec.ts` | `/challenges` + `/challenges/daily` + `/challenges/boss` | start daily challenge, complete one round, claim reward, boss-battle entry gate |
| `EPIC-X-student-settings.spec.ts` | `/settings` + sub-tabs (account, appearance, notifications, privacy, study-plan) | change locale, toggle dark mode, set notification frequency, edit study plan, save persists |
| `EPIC-X-student-notifications.spec.ts` | `/notifications` | mark-as-read, mark-all-as-read, filter unread, badge count drops |
| `EPIC-X-student-knowledge-graph.spec.ts` | `/knowledge-graph` | graph renders, click node → drill-down, search topic |
| `EPIC-X-student-profile.spec.ts` | `/profile` + `/profile/edit` | edit display name, save round-trips to `/api/me/profile`, accommodations preferences flag persists |

## Boundary assertions (per-action)

| Boundary | Assertion |
| --- | --- |
| DOM | Action confirmation visible (toast, dialog close, count update) |
| API | Endpoint returns 2xx; no console-error from a 4xx |
| State | Read-model reflects the change |
| Persistence | Hard refresh keeps the change |
| Locale | At least one action per spec re-runs in `ar` locale to catch RTL form regressions |

## Regression this catches

- A student-private setting that silently fails to save (the SPA toasts success but the backend returned 4xx)
- A leaderboard scope toggle that doesn't actually fire a new query (UI shows the right tab but data is stale)
- A daily-challenge claim that double-counts reward XP
- A friend-add that bypasses tenant scoping (privacy regression)

## Done when

- [ ] All 7 sub-specs ship with ≥ 1 primary-action click+assert per page in their surface
- [ ] Each spec uses the diagnostic-collection pattern
- [ ] Dynamic-route subset gated on TASK-E2E-INFRA-03 and skipped with `test.fixme` until that lands
- [ ] At least one action per spec re-runs in `ar` locale
- [ ] Tagged `@functional @student @p1`
