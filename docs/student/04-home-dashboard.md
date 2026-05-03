# 04 — Home Dashboard

## Overview

The `/home` page is the student's mission control. It is the default destination after login and sets the tone for the session: what to do next, how much time is expected, and what the student has already accomplished today.

## Mobile Parity

Source of truth:
- [src/mobile/lib/features/home/home_screen.dart](../../src/mobile/lib/features/home/home_screen.dart)
- [src/mobile/lib/features/home/widgets/home_screen_widget_config.dart](../../src/mobile/lib/features/home/widgets/home_screen_widget_config.dart)
- [src/mobile/lib/features/home/widgets/review_due_badge.dart](../../src/mobile/lib/features/home/widgets/review_due_badge.dart)

Mobile shows a stack of widgets: greeting, streak, XP progress, daily plan, review-due, quick actions, class feed teaser.

## Page Layout (Desktop ≥ lg)

```
┌──────────────────────────────────────────────────────────────────┐
│ Greeting + current streak + XP ring                              │ ← Hero row
├────────────────────────────┬─────────────────────────────────────┤
│                            │ Today's Plan                        │
│  Continue where you        │ ┌──────────────────────────────┐    │
│  left off                  │ │ 3 × 15 min blocks            │    │
│  (resume card)             │ │ Progress: ████░░░░░ 12/30min │    │
│                            │ └──────────────────────────────┘    │
│                            │                                     │
│                            │ Review Due                          │
│                            │ 7 cards • oldest: 4 days            │
│                            │ [Review Now]                        │
├────────────────────────────┴─────────────────────────────────────┤
│ Recommended Sessions                                             │
│ [Card 1] [Card 2] [Card 3] [Card 4]                              │
├──────────────────────────────────────────────────────────────────┤
│ Quick Actions: Start Session · AI Tutor · Boss Battle · Graph    │
├──────────────────────────────────────────────────────────────────┤
│ Class Feed Teaser (last 3 items) │ Momentum + flow time summary  │
└──────────────────────────────────────────────────────────────────┘
```

On smaller screens the layout stacks vertically; at `xl` breakpoint the right column gains an extra panel (see [14-web-enhancements](14-web-enhancements.md) for multi-pane).

## Components

| Component | Purpose | Mobile source |
|-----------|---------|---------------|
| `<HomeGreeting>` | Time-aware greeting with student name | new |
| `<StreakWidget>` | Current streak + longest streak + flame icon | `streak_widget.dart` |
| `<XpRing>` | Circular progress toward next level | `gamification_widgets.dart` |
| `<ResumeSessionCard>` | Shows last active or most-recent session with a Resume CTA | new (mobile has a banner) |
| `<TodaysPlanCard>` | Time goal, blocks done, next block preview | new (mobile shows inline) |
| `<ReviewDueCard>` | Due SRS cards count, oldest due age | `review_due_badge.dart` |
| `<RecommendedSessions>` | Horizontal carousel of next-best sessions (accuracy-weighted) | new |
| `<QuickActions>` | 4 large tiles linking to core flows | new |
| `<ClassFeedTeaser>` | Latest 3 items from class feed | `class_activity_feed.dart` |
| `<MomentumCard>` | Weekly XP trend line + momentum meter | `momentum_meter.dart` |
| `<FlowTimeSummaryCard>` | % of last session spent in flow | `flow_ambient_indicator.dart` (FlowTimeSummary) |

## Data Sources

| Widget | Endpoint / Hub event |
|--------|---------------------|
| Greeting | `GET /api/me` |
| Streak, XP, level | `GET /api/analytics/summary` |
| Resume card | `GET /api/sessions/active` |
| Today's plan | `GET /api/me/plan/today` (new) |
| Review due | `GET /api/review/due` (new) |
| Recommended | `GET /api/recommendations/sessions` (new) |
| Class feed teaser | `GET /api/social/class-feed?limit=3` (new) |
| Momentum | `GET /api/analytics/progress?period=weekly` (exists) |
| Flow summary | Computed from last session document, pulled via `GET /api/sessions/{id}` |

## Widget Configurability

Mobile exposes a `home_screen_widget_config.dart` that lets the student toggle which widgets are shown. The web version mirrors this and adds drag-to-reorder:

- Settings → Home Layout → list of widgets with toggles and a drag handle.
- Config persists to backend: `PUT /api/me/preferences/home-layout`.
- Default layout matches the wireframe above.

## Realtime Updates (SignalR)

Home subscribes to these events and patches widgets in place without a refresh:

| Event | Updates |
|-------|---------|
| `xp_gained` | XP ring, momentum |
| `streak_extended` / `streak_broken` | Streak widget |
| `session_started` / `session_ended` | Resume card, today's plan progress |
| `review_card_due` | Review-due count |
| `class_feed_item_added` | Class feed teaser |

## Empty States

- **First-time student** (no sessions): large welcome card with "Start your first session" CTA, Today's Plan shows the daily time goal target, other cards hidden.
- **No review due**: "You're all caught up!" illustration + link to start a new session.
- **No classroom / solo learner**: Class feed teaser is replaced with "Global challenges" card.

## Acceptance Criteria

- [ ] `STU-HOME-001` — `/home` route loads with `default` layout and fetches all widget data in parallel on mount.
- [ ] `STU-HOME-002` — Greeting is time-aware ("Good morning / afternoon / evening") using the user's locale and timezone.
- [ ] `STU-HOME-003` — Streak widget matches mobile behavior (current streak, longest streak, days-until-risk).
- [ ] `STU-HOME-004` — XP ring shows current level progress and animates on XP gain events.
- [ ] `STU-HOME-005` — Resume card appears only when `/api/sessions/active` returns a session.
- [ ] `STU-HOME-006` — Today's plan shows configured time goal, blocks completed, and next block preview.
- [ ] `STU-HOME-007` — Review due card shows count, oldest due age, and routes to `/session?mode=review`.
- [ ] `STU-HOME-008` — Recommended sessions carousel shows 4 cards and scrolls horizontally.
- [ ] `STU-HOME-009` — Quick actions tiles link to session launcher, tutor, boss battles, knowledge graph.
- [ ] `STU-HOME-010` — Class feed teaser shows latest 3 items or falls back to global challenges.
- [ ] `STU-HOME-011` — Momentum card renders an ApexCharts area chart with the last 7 days of XP.
- [ ] `STU-HOME-012` — Flow time summary card shows last session's flow percentage only if > 0.
- [ ] `STU-HOME-013` — Widget visibility and order are configurable in settings and persist to backend.
- [ ] `STU-HOME-014` — SignalR events patch widgets in place with smooth animation.
- [ ] `STU-HOME-015` — First-time empty state is shown when no sessions exist and hides irrelevant widgets.
- [ ] `STU-HOME-016` — All widgets have skeleton loading states.
- [ ] `STU-HOME-017` — Page passes Lighthouse performance ≥ 90 on mid-range laptop profile.

## Backend Dependencies

- `GET /api/me` — new
- `GET /api/analytics/summary` — exists
- `GET /api/sessions/active` — exists
- `GET /api/me/plan/today` — new
- `GET /api/review/due` — new (SRS review queue — may need mobile parity work)
- `GET /api/recommendations/sessions` — new
- `GET /api/social/class-feed` — new
- `PUT /api/me/preferences/home-layout` — new
