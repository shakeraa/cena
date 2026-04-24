# TASK-STU-W-05: Home Dashboard

**Priority**: HIGH — default post-login destination; first impression of a returning student
**Effort**: 3-4 days
**Phase**: 2
**Depends on**: [STU-W-04](TASK-STU-W-04-auth-onboarding.md)
**Backend tasks**: [STB-00](../student-backend/TASK-STB-00-me-profile-onboarding.md), [STB-02](../student-backend/TASK-STB-02-plan-review-recommendations.md)
**Status**: Not Started

---

## Goal

Build `/home` as a responsive, realtime, customizable dashboard that tells the returning student "here's where you left off, here's what's next, here's what you've accomplished" in under 1 second after login.

## Spec

Full specification in [docs/student/04-home-dashboard.md](../../docs/student/04-home-dashboard.md). All `STU-HOME-001` through `STU-HOME-017` acceptance criteria form this task's checklist.

## Scope

In scope:

- `/home` page with hero row, two-column middle section, recommended carousel, quick actions, class feed teaser, momentum + flow cards
- Widgets implemented as standalone components under `src/components/home/`:
  - `<HomeGreeting>` — time + locale aware
  - `<StreakWidget>` — mirrors mobile `streak_widget.dart`
  - `<XpRing>` — circular progress toward next level, animates on XP gain
  - `<ResumeSessionCard>` — present only if `/api/sessions/active` returns a session
  - `<TodaysPlanCard>` — goal progress + next block preview
  - `<ReviewDueCard>` — SRS due count + oldest-due age
  - `<RecommendedSessions>` — horizontal carousel, keyboard navigable
  - `<QuickActions>` — 4 large tiles with Tabler icons
  - `<ClassFeedTeaser>` — last 3 feed items, falls back to global challenges if not enrolled
  - `<MomentumCard>` — ApexCharts weekly XP area chart
  - `<FlowTimeSummaryCard>` — mirrors mobile `FlowTimeSummary`, hidden if 0 %
- Parallel data fetch on mount using `Promise.all` via `useApiQuery` from STU-W-03
- SignalR subscriptions: `xp_gained`, `streak_extended`, `streak_broken`, `session_started`, `session_ended`, `review_card_due`, `class_feed_item_added` — each patches the relevant widget in place
- Widget visibility + order configurable via `useHomeLayout()` composable backed by `PUT /api/me/preferences/home-layout` (STB-00)
- Drag-to-reorder in `/settings/home-layout` (implemented here, not STU-W-14) — uses `vuedraggable`
- Empty states: first-time student (big welcome + start CTA), no review due, solo learner (class feed → global challenges)
- Skeleton loading states for every widget during initial fetch
- Localized time formatting via `Intl.DateTimeFormat` with the student's timezone
- Aria live region announcements for streak broken / XP gained when they update in-place

Out of scope:

- Actually implementing SRS review logic (STB-02)
- Generating the recommended sessions list (STB-02)
- XP curve math (already lives in `meStore` or gamification helper from STU-W-07)
- Feed content moderation UI (STU-W-12)

## Definition of Done

- [ ] All 17 `STU-HOME-*` acceptance criteria in [04-home-dashboard.md](../../docs/student/04-home-dashboard.md) pass
- [ ] Page passes Lighthouse performance ≥ 90 on a mid-range laptop profile, LCP ≤ 1.5 s
- [ ] Widget skeletons appear within 50 ms of navigation; real content swaps in without layout shift
- [ ] SignalR XP-gained event animates the XP ring within 50 ms of receipt
- [ ] Widget drag-reorder persists and survives a hard refresh
- [ ] First-time empty state hides irrelevant widgets and surfaces the "start your first session" CTA
- [ ] Playwright covers: home first-load, home with active session (Resume card visible), home with review due, home drag-reorder, empty first-time state
- [ ] All widgets pass axe-core audit in all three locales and both themes
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **N+1 on mount** — 8 parallel requests on a cold cache will saturate a cheap laptop. Budget the waterfall with `Promise.all` + 2 s total timeout; any widget not returned in time falls back to a skeleton forever (user can refresh).
- **ApexCharts bundle bloat** — lazy-load the chart library so the home bundle stays under 200 KB. Verify with a bundle analyzer step in CI.
- **Widget layout state** is user-personal — never cache across users in localStorage; key the persistence by student ID.
- **Time-zone drift** — student's stored timezone might be stale (they moved). Read `Intl.DateTimeFormat().resolvedOptions().timeZone` on each session and warn on mismatch.
- **Realtime XP + optimistic UI** can double-count if both the REST response and the hub event arrive for the same gain. Dedupe by event ID.
