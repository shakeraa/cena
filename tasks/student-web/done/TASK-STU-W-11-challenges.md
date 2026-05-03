# TASK-STU-W-11: Challenges

**Priority**: MEDIUM — engagement driver, not on the critical path
**Effort**: 3-4 days
**Phase**: 3
**Depends on**: [STU-W-06](TASK-STU-W-06-learning-session-core.md)
**Backend tasks**: [STB-05](../student-backend/TASK-STB-05-challenges.md)
**Status**: Not Started

---

## Goal

Build the challenges hub: daily challenge, boss battles menu, card chains, tournaments, and the web-only calendar/history views. Reuses the session runtime from STU-W-06 for actual gameplay.

## Spec

Full specification in [docs/student/10-challenges.md](../../docs/student/10-challenges.md). All 18 `STU-CHL-*` acceptance criteria form this task's checklist.

## Scope

In scope:

- `/challenges` hub page with three sections: Today, Card Chains, Boss Battles
- `/challenges/daily` daily challenge landing with timer, leaderboard, share card, streak-bonus indicator
- `/challenges/boss` boss battles menu with filterable grid, mastery-threshold gating, attempts remaining
- `/challenges/chains/:chainId` card chain page with vertical timeline, current card, unlocked/locked states, story context
- Components:
  - `<DailyChallengeCard>` (also used on home via STU-W-05)
  - `<DailyChallengeLeaderboard>` — sticky own-rank, realtime updates via `LeaderboardChanged` hub event
  - `<BossBattleCard>` — portrait, stats, start CTA
  - `<BossBattleMenu>` — filterable grid
  - `<ChainOverviewCard>`
  - `<ChainProgressTimeline>` — vertical, unlocked/locked markers, story context rendered as markdown
  - `<ChallengeShareCard>` — generates an OG image
- Start CTA on any challenge calls `POST /api/challenges/{type}/start` (STB-05) and navigates to the live session (STU-W-06) which handles the actual gameplay
- Web-only: challenge calendar — week view of past daily challenges with scores
- Web-only: replay past daily challenges in practice mode (no leaderboard impact)
- Web-only: team chains showing classroom aggregate progress (read-only visibility into classmates' contributions, enforced by privacy rules)
- Web-only: tournaments page with scheduled event listings, countdown timers, participation CTA
- Empty states for each section (no challenges, no chains, not enrolled in a class)

Out of scope:

- Boss battle content generation — backend
- Tournament scheduling + pairing — backend
- Teacher spectator mode — future task
- Chain editor preview for upcoming chains — nice-to-have, defer

## Definition of Done

- [ ] All 18 `STU-CHL-*` acceptance criteria in [10-challenges.md](../../docs/student/10-challenges.md) pass
- [ ] Daily challenge leaderboard updates in realtime over SignalR without polling
- [ ] Boss battle mastery gating is enforced client-side (as UX) and server-side (as truth) — test with a locked boss
- [ ] Attempts remaining count updates after each attempt without a manual refresh
- [ ] Starting any challenge type launches the correct session mode in STU-W-06 without code duplication
- [ ] Share card OG image renders correctly in a Twitter card validator and excludes PII
- [ ] Challenge calendar shows correct dates and respects locale
- [ ] Playwright covers: daily challenge start → session → result, boss battle locked/unlocked, chain timeline navigation, tournament page load
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Session runtime duplication** — tempting to build a "mini session" for challenges. Do not. All challenges use STU-W-06 with a mode flag. Challenge-specific UI chrome (health bar, boss portrait) is already STU-W-06's responsibility.
- **Leaderboard timing window** — daily challenges reset at midnight in the student's timezone. Confirm the server uses the same timezone or surfaces the reset time explicitly.
- **Boss battle attempts** — server must be the source of truth for attempts-remaining. A client-side counter can desync if the student has multiple tabs open.
- **Tournament deep linking** — tournament IDs may be sensitive; confirm they're opaque and do not leak participant counts.
- **Locale-specific daily challenges** — if challenges are the same globally, a Hebrew speaker may see English content. Confirm with backend whether daily challenges are per-locale.
