# 11 — Social Learning

## Overview

Social features turn solo study into a community experience — students see classmates' progress, swap solutions, and compete in friendly ways. All social features are **opt-in**, privacy-safe, and age-gated.

## Mobile Parity

- [class_activity_feed.dart](../../src/mobile/lib/features/social/class_activity_feed.dart)
- [daily_challenge_card.dart](../../src/mobile/lib/features/social/daily_challenge_card.dart)
- [widgets/peer_solutions_sheet.dart](../../src/mobile/lib/features/social/widgets/peer_solutions_sheet.dart)
- [widgets/social_feed_card.dart](../../src/mobile/lib/features/social/widgets/social_feed_card.dart)
- [social-learning-research.md](../social-learning-research.md)

## Pages

### `/social` — Class Feed

Twitter-style feed of recent activity from the student's class / cohort.

Feed item types:
- `studentCompletedSession` — e.g. "Ali finished a 30-min math session and mastered 3 concepts"
- `studentEarnedBadge`
- `studentHitStreak`
- `peerSharedSolution`
- `teacherAnnouncement`
- `classReachedMilestone`
- `dailyChallengeLive`

Features:
- Infinite scroll
- Reaction emojis (like, clap, mind-blown, lightbulb) — rate-limited per user
- Comments (moderated, locked under age 13)
- Filter by type and author
- Mute classmates individually

### `/social/peers` — Peer Solutions

Browse anonymized peer solutions to hard questions.

- Filter by subject / concept / question
- Sort by upvotes, recent, complexity
- Each solution shows method, steps, time-taken
- Vote up / save / "explain this to me" (opens tutor)
- Report inappropriate content

Peer solutions are **anonymized by default** — shown as "Student from grade 10" unless the author explicitly opts in to show their display name.

### `/social/leaderboard`

Multi-scope leaderboard (see [06-gamification](06-gamification.md)).

- Scopes: class / friends / grade / school / region / global
- Periods: today / this week / this month / all-time
- Metrics: XP / mastery points / streaks
- Own rank sticky

### `/social/friends` — Friends (Web-Only)

Mobile has no dedicated friends list; web adds one.

- Friend requests + accept / reject
- View a friend's public profile (progress dashboard, badges, streak)
- Compare mastery with a friend side-by-side
- "Invite to study room" CTA

## Study Rooms (Web-Only)

A lightweight co-op experience: invite friends (or joins an open room) for a co-study session.

- Each member works on their own question stream but sees the others' progress bars.
- Ambient presence: animated avatars on a shared canvas.
- Optional text chat channel (heavily moderated, age-gated).
- "Tutor call-in" — one student can call the tutor and the whole room sees the answer.

Not live video (out of scope for v1).

## Privacy & Safety

- Display name is the only identity surfaced publicly; full name is never shown.
- Under-13 students can only see class-feed events, not comments or peer solutions.
- Teachers / admins moderate class feeds; all comments go through content moderation.
- Students can mute, block, and report any peer at any time.
- Social features can be disabled per-student by a parent or admin.
- All data shared over social is logged for audit (gdpr-compliant).

## Components

| Component | Purpose |
|-----------|---------|
| `<SocialFeed>` | Infinite-scroll feed container |
| `<SocialFeedCard>` | Single feed item — type-specific renderer |
| `<ReactionBar>` | Emoji reactions with counts |
| `<CommentsThread>` | Threaded comments with moderation flags |
| `<PeerSolutionCard>` | Solution card with vote, save, report |
| `<PeerSolutionFilter>` | Filter chips + sort dropdown |
| `<LeaderboardTable>` | Sticky own-rank leaderboard |
| `<FriendsList>` | Friend list + request flow |
| `<FriendCompareView>` | Side-by-side mastery comparison |
| `<StudyRoomLobby>` | Create / join co-op room |
| `<StudyRoomCanvas>` | Shared ambient canvas with presence avatars |

## Acceptance Criteria

- [ ] `STU-SOC-001` — `/social` class feed loads with infinite scroll.
- [ ] `STU-SOC-002` — All feed item types render with correct icons and copy.
- [ ] `STU-SOC-003` — Reactions work and are rate-limited.
- [ ] `STU-SOC-004` — Comments are threaded, moderated, and locked for under-13.
- [ ] `STU-SOC-005` — Filter / sort / mute controls work.
- [ ] `STU-SOC-006` — `/social/peers` shows anonymized peer solutions with vote / save / report.
- [ ] `STU-SOC-007` — Peer solutions filter by subject / concept / question.
- [ ] `STU-SOC-008` — "Explain this to me" opens tutor with the solution as context.
- [ ] `STU-SOC-009` — Report flow submits to moderation and hides the item locally.
- [ ] `STU-SOC-010` — Leaderboard supports class / friends / grade / school / region / global.
- [ ] `STU-SOC-011` — Leaderboard period toggle: today / week / month / all-time.
- [ ] `STU-SOC-012` — Own rank sticky on leaderboard.
- [ ] `STU-SOC-013` — Friends list supports request / accept / reject / block.
- [ ] `STU-SOC-014` — Friend compare view shows mastery, streak, XP side-by-side.
- [ ] `STU-SOC-015` — Study room lobby supports create / join / invite.
- [ ] `STU-SOC-016` — Study room shared canvas shows ambient presence avatars.
- [ ] `STU-SOC-017` — Study room text chat is age-gated and moderated.
- [ ] `STU-SOC-018` — Privacy: display name only, no full names, no emails, no phone.
- [ ] `STU-SOC-019` — Parent / admin can disable social features per student.
- [ ] `STU-SOC-020` — All social actions pass through content moderation server-side.
- [ ] `STU-SOC-021` — Empty states for no friends, no class, no solutions.

## Backend Dependencies

- `GET /api/social/class-feed?page=&filter=` — new
- `POST /api/social/reactions` — new
- `POST /api/social/comments` — new
- `GET /api/social/peers/solutions?questionId=&subject=&sort=` — new
- `POST /api/social/peers/solutions/{id}/vote` — new
- `POST /api/social/peers/solutions/{id}/report` — new
- `GET /api/social/leaderboard?scope=&period=` — new
- `GET /api/social/friends` — new
- `POST /api/social/friends/request` — new
- `POST /api/social/friends/{id}/accept` — new
- `GET /api/social/study-rooms` — new
- `POST /api/social/study-rooms` — new
- Hub events for study-room presence, reactions, live leaderboard
