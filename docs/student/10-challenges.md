# 10 — Challenges

## Overview

Challenges are curated, high-stakes practice experiences: card chains, daily challenges, and boss battles. They layer engagement and friendly competition on top of the regular session flow.

## Mobile Parity

- [challenges_screen.dart](../../src/mobile/lib/features/challenges/challenges_screen.dart)
- [card_chain_progress.dart](../../src/mobile/lib/features/challenges/card_chain_progress.dart)
- [daily_challenge_card.dart](../../src/mobile/lib/features/social/daily_challenge_card.dart)
- [boss_battle_screen.dart](../../src/mobile/lib/features/session/widgets/boss_battle_screen.dart)
- [boss_battle_result.dart](../../src/mobile/lib/features/session/widgets/boss_battle_result.dart)
- [models/boss_battle.dart](../../src/mobile/lib/features/session/models/boss_battle.dart)

## Pages

### `/challenges` — Challenges Hub

Landing page with three sections:

1. **Today** — Daily challenge card + today's quest.
2. **Card Chains** — Grid of active chains with progress rings.
3. **Boss Battles** — Available boss encounters grouped by subject / topic.

### `/challenges/daily`

Daily challenge: a fixed set of 10 questions generated globally for all students on that date (so students can compare scores).

Features:
- Time limit (10 min default)
- Leaderboard specific to today's challenge
- Streak bonus if the student completes it N days in a row
- Share score card

### `/challenges/boss`

Boss battles menu. Each boss represents a mastery milestone (e.g. "Master of Quadratics", "Cell Biology Expert").

Features per boss:
- Name + portrait (Rive or SVG)
- Required mastery threshold to unlock
- XP reward + exclusive badge
- Previous attempts (attempts remaining today)
- "Start Battle" CTA → launches a boss-battle session (see [05-learning-session](05-learning-session.md))

### `/challenges/chains/:chainId`

Card chain progress view.

A chain is a sequence of linked questions where answering one correctly unlocks the next. Like a puzzle progression.

Features:
- Vertical timeline of cards (unlocked / locked)
- Current card highlighted
- Overall progress ring
- Chain story context (fiction-wrapped learning)
- Reward per chain completion

## Components

| Component | Purpose |
|-----------|---------|
| `<DailyChallengeCard>` | Highlighted banner card on home + challenges hub |
| `<DailyChallengeLeaderboard>` | Today's top scores, with own rank sticky |
| `<BossBattleCard>` | Boss preview card with portrait, stats, CTA |
| `<BossBattleMenu>` | Filterable grid of boss battles |
| `<ChainOverviewCard>` | Card showing chain name, progress, next card preview |
| `<ChainProgressTimeline>` | Vertical timeline with unlocked / locked states |
| `<ChallengeShareCard>` | Generates a shareable image |

## Web-Specific Enhancements

- **Challenge calendar** — week view showing past daily challenges with scores.
- **Replay past daily challenges** — mobile only shows today; web keeps the last 30 days accessible for practice.
- **Boss battle spectator mode** — teachers can watch a student's boss battle live (with consent).
- **Chain editor preview** — a read-only preview of upcoming chains (for students who want to plan ahead).
- **Team chains** — classroom-wide chains where the whole class contributes answers (web-only team view).
- **Tournament mode** — scheduled events where multiple classes compete on the same challenge set.

## Acceptance Criteria

- [ ] `STU-CHL-001` — `/challenges` hub shows Today, Card Chains, Boss Battles sections.
- [ ] `STU-CHL-002` — Daily challenge card displays today's challenge, expiry, and start CTA.
- [ ] `STU-CHL-003` — Daily challenge is the same question set for all students on a given date.
- [ ] `STU-CHL-004` — Daily challenge leaderboard updates in realtime over SignalR.
- [ ] `STU-CHL-005` — Streak bonus applied when completing N days in a row.
- [ ] `STU-CHL-006` — Share score card generates an OG image.
- [ ] `STU-CHL-007` — Boss battle menu lists available bosses with mastery-threshold gating.
- [ ] `STU-CHL-008` — Locked bosses show required mastery threshold.
- [ ] `STU-CHL-009` — Starting a boss battle launches a boss-mode session.
- [ ] `STU-CHL-010` — Attempts-per-day limit enforced server-side and shown in UI.
- [ ] `STU-CHL-011` — Card chain page renders timeline, current card, unlocked / locked states.
- [ ] `STU-CHL-012` — Completing a chain awards XP and triggers celebration overlay.
- [ ] `STU-CHL-013` — Chain story context is rendered with markdown support.
- [ ] `STU-CHL-014` — Web-only: challenge calendar shows last 30 days.
- [ ] `STU-CHL-015` — Web-only: replay past daily challenges scored in practice mode (not leaderboard).
- [ ] `STU-CHL-016` — Web-only: team chains show classroom aggregate progress.
- [ ] `STU-CHL-017` — Web-only: tournament mode supports multi-class scheduled events.
- [ ] `STU-CHL-018` — All challenge pages have empty-state illustrations.

## Backend Dependencies

- `GET /api/challenges/daily` — new
- `POST /api/challenges/daily/start` — new
- `GET /api/challenges/daily/leaderboard` — new
- `GET /api/challenges/daily/history?limit=30` — new (web-only)
- `GET /api/challenges/boss` — new
- `POST /api/challenges/boss/{id}/start` — new
- `GET /api/challenges/chains` — new
- `GET /api/challenges/chains/{id}` — new
- `GET /api/challenges/tournaments` — new
