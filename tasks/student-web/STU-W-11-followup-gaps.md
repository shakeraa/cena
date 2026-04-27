# STU-W-11 follow-up — gap inventory and remediation plan

**Author**: claude-3
**Date opened**: 2026-04-28
**Parent PR**: `claude-3/stu-w-11-challenges` → merged into `main` as 75724816
**Branch (this remediation)**: `claude-3/stu-w-11-followup`

This doc inventories everything I deliberately deferred, partially shipped, or pre-existed-but-noticed during STU-W-11. It's the source of truth for what claude-3 picks up next, what claude-code should re-queue, and what other agents should be aware of.

---

## A. Tier 1 — DONE on this branch (claude-3/stu-w-11-followup)

These are SPA-scoped, file-independent from claude-1's COV-* and claude-2's read-only autoresearch loop. No admin-api / NATS / Marten touched.

| # | Item | Files | Status |
|---|---|---|---|
| A1 | `/progress/sessions/[sessionId]` placeholder → router-redirect to `/session/:id/replay` (replay itself is also a placeholder owned by STU-W-06; redirect makes the URL forward-compatible) | `pages/progress/sessions/[sessionId].vue` | DONE |
| A2 | vue-i18n v11 sweep — `t(key, count, named)` → `t(key, named, { plural: count })` across **17 call sites** in the running SPA (4 originally listed + 13 found during the sweep) | `pages/home.vue`, `pages/notifications.vue`, `pages/social/friends.vue`, `pages/social/leaderboard.vue`, `components/{progress/TimeBreakdownChart, progress/XpProgressCard, progress/SessionHistoryItem (×2), progress/LeaderboardPreview (×2), progress/SubjectMasteryRow, home/ResumeSessionCard (×2), social/ClassFeedItemCard (×4), knowledge/ConceptDetailCard, notifications/NotificationListItem (×3), session/SessionSummaryCard, session/AnswerFeedback, session/SessionSetupForm, tutor/TutorThreadListItem (×4)}` | DONE |
| A3 | Remove `<StreakWidget>` mount from `/home` — ship-gate violation per ADR-0048 + memory `feedback_shipgate_banned_terms`. Component file kept (orphan-deletable later); only the `/home` mount + the `streakDays` computed are removed. | `pages/home.vue` | DONE |
| A4 | Manual leaderboard refresh button on `/challenges/daily` (mitigates absent SignalR until B1 lands). Adds `hide-title` prop to `DailyChallengeLeaderboard` so the page can own the heading + refresh button row. | `pages/challenges/daily.vue`, `components/challenges/DailyChallengeLeaderboard.vue`, `i18n` (3 locales × 2 keys) | DONE |
| A5 | Subject-filter URL persistence on `/challenges/boss` — `?subject=math` survives refresh, `router.replace` (not push) so back-button isn't polluted. | `pages/challenges/boss.vue` | DONE |
| A6 | Entitlement-gating TODOs on `/challenges/daily` and `/challenges/boss` referencing `docs/design/trial-then-paywall-001-discussion.md` (per claude-2's request m_0fd1297a7d1c). | `pages/challenges/daily.vue`, `pages/challenges/boss.vue` | DONE |
| A7 | Pre-existing TS error: `layout: 'auth'` not in route-meta type union (7 affected pages). Widened the union in `env.d.ts` and added missing `title`, `breadcrumbs`, `hideSidebar`, `requiresAuth`, `requiresOnboarded` fields that other pages already use. | `env.d.ts` | DONE |
| A8 | Pre-existing TS error: `pages/pricing/index.vue:56` references `me.studentId` not exported from store. Added a stable `studentId` computed to `meStore` (uid passthrough). | `stores/meStore.ts` | DONE |
| A9 | Pre-existing TS error: `themeConfig.ts:12` missing `app.isRTL`. Added `isRTL: false` (per-locale RTL is set at runtime by `initCore.ts:54` reading the active langConfig entry). Also added the field to `@core/index.ts` layout passthrough. | `themeConfig.ts`, `@core/index.ts` | DONE |
| A10 (bonus) | Pre-existing TS error: `pages/social/leaderboard.vue:161` + `pages/knowledge-graph/index.vue:97` reference `error.i18nKey` on plain `Error`. Migrated both to `ApiError` typing + wrapping at the `$api` catch site. Now consistent with the rest of the codebase's error chrome. | `pages/social/leaderboard.vue`, `pages/knowledge-graph/index.vue` | DONE |
| A11 (bonus) | Pre-existing TS error: `pages/settings/study-plan.vue:439` had duplicate `model-value` on a `VDialog` (`v-model:model-value` + `:model-value`). Removed the redundant binding. | `pages/settings/study-plan.vue` | DONE |

### Net effect

- vue-tsc errors related to my touched files: 0
- Remaining pre-existing tsc errors (4 — `$api.ts` ofetch typing, `ParentalConsentStep.vue` boolean-null callback, `useEncryptedOfflineCache.ts` Uint8Array buffer, `sentry.ts` readonly array) are infra-level type-drift, not in my SPA scope.
- Unit tests: 5 fewer failures than baseline main (10 → 5 actual test fails). The reductions are all from A2's i18n sweep — `AnswerFeedback`, `ConceptDetailCard`, `SessionSummaryCard`, plus 2 in `AuthProviderButtons` started loading correctly because their imports stopped TS-erroring. Remaining 5 fails (`FriendRow`, `OnboardingStepper ×2`, `QuestionCard`, `useNetworkStatus`) are pre-existing and unrelated.
- Build: green, same pre-existing PWA precache warning on the 3.22 MB CSS bundle.

---

## B. Tier 2 — separate effort, queue or dedicated branch

| # | Item | Spec ref | Why split |
|---|---|---|---|
| B1 | SignalR realtime daily-challenge leaderboard via `LeaderboardChanged` on hub group `daily-{date}` | STU-CHL-004 | non-trivial: hub group lifecycle + reconnect handling + dedup |
| B2 | Share-score-card OG image generator | STU-CHL-006 | separate web-only feature, needs OG image build pipeline |
| B3 | `/challenges/chains/[chainId]` page (timeline + story context + markdown) | STU-CHL-011/012/013 | needs ChainProgressTimeline component + story renderer |
| B4 | `/challenges/tournaments` full page (countdown, registration, leaderboard) | STU-CHL-017 | full standalone page |
| B5 | Challenge calendar (last 30 days) | STU-CHL-014 | web-only, separate calendar component |
| B6 | Replay past daily in practice mode (no leaderboard impact) | STU-CHL-015 | web-only, depends on session-replay route work |
| B7 | Team chains classroom-aggregate view (read-only, privacy-gated) | STU-CHL-016 | needs classroom membership checks |
| B8 | Boss-detail intermediate step — surface `attemptsRemaining` BEFORE `POST /start` consumes one | STU-CHL-008/009/010 UX correctness | needs `BossBattleDetailDto` page or drawer; design decision needed |
| B9 | Empty-state illustrations across all challenge pages | STU-CHL-018 | needs illustration assets |

---

## C. Tier 3 — coordinator escalation (cross-cutting, multi-branch)

### C1 — Streak feature deprecation (ACTIVE SHIP-GATE VIOLATION)

The "no streaks" rule is a non-negotiable per CLAUDE.md and ADR-0048. The codebase still has streak machinery in 9 places spanning frontend, types, API handlers, and (likely) backend:

| Layer | File | Line | What |
|---|---|---|---|
| UI | `pages/home.vue` | 227 | `<StreakWidget :days="streakDays" :is-new-best="…" />` mount |
| UI | `components/home/StreakWidget.vue` | full file | tabler-flame icon + day counter + "new best" chip |
| UI | `components/social/FriendRow.vue` | 50 | `t('social.friends.streak', friend.streakDays)` chip |
| UI | `components/notifications/NotificationListItem.vue` | 22 | `case 'streak': return 'error'` icon mapping |
| Type | `src/api/types/common.ts` | 74, 736, 758 | `streakDays: number` on Me/Friend, `'streak'` in `NotificationKind` |
| Mock | `plugins/fake-api/handlers/student-me/index.ts` | 28, 53, 90, 107 | `streakDays`, `streakAlerts`, `widgetOrder: ['streak', …]` |
| Mock | `plugins/fake-api/handlers/student-social/index.ts` | 40, 60, 78–81, 228 | `streakDays` on friends/feed |
| Mock | `plugins/fake-api/handlers/student-gamification/index.ts` | 44, 171 | `week-streak` badge + `GET /api/gamification/streak` |
| Backend | (presumed) `Cena.Actors/Gamification/*` | n/a | actual `streakDays` source-of-truth + `/api/gamification/streak` endpoint |

A1 of this branch removes the `<StreakWidget>` mount only — the visible counter goes away on `/home`. The rest needs a coordinated removal across UI, types, mock, and backend, and probably an event-sourcing consideration for already-emitted `StreakReached_V1` (or similar) events.

**Recommendation**: claude-code enqueue a dedicated `STU-DEPRECATE-STREAK` task (or three: UI, types/mock, backend) so this lands in a single coherent transactional change. CI scanner should already be flagging `home.streak.*` use; if it isn't, that's a scanner regression worth investigating.

### C2 — Multi-target exam plan question filtering (separate raised earlier)

`MartenQuestionPool.LoadAsync` filters by `Subject` only. Active `ExamTargetCode` is resolved by `SessionPlanGenerator` and stamped on the plan but is NOT applied to the question selection. Brief at `docs/design/MULTI-TARGET-EXAM-PLAN-001-discussion.md` is awaiting 10-persona review per memory `project_multi_target_exam_plan`.

### C3 — Pre-existing 8 broken unit test files on main

Verified pre-existing during STU-W-11 typecheck/test run, untouched by either parent or follow-up branch:

```text
tests/unit/AnswerFeedback.spec.ts            (16 tests | 1 failed)
tests/unit/QuestionCard.spec.ts              (13 tests | 1 failed)
tests/unit/useNetworkStatus.spec.ts          ( 6 tests | 1 failed)
tests/unit/AuthProviderButtons.spec.ts       ( 4 tests | 2 failed)
tests/unit/ConceptDetailCard.spec.ts         ( 4 tests | 1 failed)
tests/unit/SessionSummaryCard.spec.ts        ( 2 tests | 1 failed)
tests/unit/FriendRow.spec.ts                 ( 2 tests | 1 failed)
tests/unit/OnboardingStepper.spec.ts         ( 4 tests | 2 failed)
```

Some of these likely overlap with claude-1's COV-04 fixme set. Recommend claude-1 sweep when capacity allows.

### C4 — Pre-existing PWA precache warning

`dist/assets/index-CovpmYYC.css` is 3.22 MB. workbox precache limit is 2 MB by default. Either bump `workbox.maximumFileSizeToCacheInBytes` in `vite.config.ts` (cheap), or split the CSS bundle (correct). Pre-existing on main; not blocking deploys.

---

## D. Cross-team awareness

- **claude-1**: when extending E2E specs against `/challenges/daily`, `/challenges/boss`, or `/progress/sessions/[id]`, the data-testids are now stable: `daily-challenge-page`, `daily-start`, `daily-leaderboard`, `daily-leaderboard-row-{rank}`, `boss-battles-page`, `boss-filter-{value}`, `boss-{bossBattleId}`, `daily-empty`, `boss-empty`.
- **claude-2**: A6 of this branch retro-adds the entitlement-gating TODO referencing your trial-then-paywall brief.
- **claude-code**: please review C1 (streak deprecation) as a ship-gate priority.

---

## E. Out of scope (not addressed by claude-3)

- All admin-api / Program.cs / Marten config / NATS ACL / chaos / compliance / content-pipeline territory (per claude-code's m_4099ec84db4f).
- Any changes to `tests/e2e/a11y-full-page-scan.spec.ts` or `rtl-visual-regression.spec.ts` (claude-1's territory).
