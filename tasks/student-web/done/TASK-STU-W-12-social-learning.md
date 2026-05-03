# TASK-STU-W-12: Social Learning

**Priority**: MEDIUM — community features, opt-in
**Effort**: 4-5 days
**Phase**: 3
**Depends on**: [STU-W-07](TASK-STU-W-07-gamification.md)
**Backend tasks**: [STB-03](../student-backend/TASK-STB-03-gamification.md), [STB-06](../student-backend/TASK-STB-06-social.md)
**Status**: Not Started

---

## Goal

Deliver the class feed, peer solutions browser, leaderboards (all scopes), friends list, and co-op study rooms — with strict privacy defaults, age gating, and moderation integration.

## Spec

Full specification in [docs/student/11-social-learning.md](../../docs/student/11-social-learning.md). All 21 `STU-SOC-*` acceptance criteria form this task's checklist.

## Scope

In scope:

- `/social` class feed with infinite scroll, all feed item types, reactions, comments, filter + mute
- `/social/peers` peer solutions browser with filter chips, sort, upvote, save, report, "explain this to me" (opens tutor)
- `/social/leaderboard` multi-scope leaderboard (class / friends / grade / school / region / global) × (today / week / month / all-time) × (XP / mastery points)
- `/social/friends` friends list with requests, compare view, "invite to study room"
- `/social/study-rooms` co-op study room lobby + room canvas
- Components:
  - `<SocialFeed>` — infinite scroll container
  - `<SocialFeedCard>` — type-specific renderer for each feed item type
  - `<ReactionBar>` — rate-limited emoji reactions
  - `<CommentsThread>` — threaded, moderated, age-gated
  - `<PeerSolutionCard>` — solution with vote/save/report
  - `<PeerSolutionFilter>` — filter chips + sort dropdown
  - `<LeaderboardTable>` — sticky own-rank, scope/period switchers
  - `<FriendsList>` — request/accept/block
  - `<FriendCompareView>` — side-by-side mastery, streak, XP comparison
  - `<StudyRoomLobby>` — create/join/invite
  - `<StudyRoomCanvas>` — shared ambient canvas with per-user presence avatars, text chat, tutor call-in
- Privacy enforcement:
  - Display name only, never full name / email / phone
  - Under-13 gated out of comments and peer solutions
  - Peer solutions anonymized by default
  - Per-classmate mute / block
  - Report flow submits to moderation via `POST /api/social/peers/solutions/{id}/report` and hides locally
  - Parent / admin can disable social features entirely (honored via `meStore.settings.social.disabled` flag)
- Realtime subscriptions: `ClassFeedItemAdded`, `LeaderboardChanged`, `StudyRoomPresenceChanged`
- Friends invite flow: enter display name or accept a share link
- Study room tutor call-in opens the tutor side panel for everyone in the room (STU-W-08 integration)
- Feed + leaderboard empty states with helpful copy

Out of scope:

- Live video — explicitly deferred
- Peer-to-peer voice — deferred
- DM / direct messaging — deferred, may never ship
- Study room recording / replay — future

## Definition of Done

- [ ] All 21 `STU-SOC-*` acceptance criteria in [11-social-learning.md](../../docs/student/11-social-learning.md) pass
- [ ] Under-13 test account cannot access comments or peer solutions (verified by Playwright)
- [ ] `meStore.settings.social.disabled = true` hides all social surfaces from the sidebar and any entry points
- [ ] Display name is the only identity visible anywhere in social UI (grep check in CI for forbidden fields)
- [ ] Leaderboard sticky own-rank works across all scopes and periods
- [ ] Friend compare view renders two mastery radars side-by-side
- [ ] Study room canvas shows presence avatars for all connected members within 200 ms
- [ ] Report flow submits and visually removes the item within 100 ms (optimistic)
- [ ] Rate-limited reactions throttle correctly (server enforces, client hints)
- [ ] Playwright covers: feed scroll + filter, peer solution upvote + report, leaderboard scope switch, friend request + compare, study room join
- [ ] Cross-cutting concerns from the bundle README apply

## Risks

- **Content moderation latency** — inappropriate content can briefly appear before moderation kicks in. Pre-moderate with a client-side profanity hint but rely on server for truth.
- **Study room state sync** — presence updates can storm. Throttle presence events to 1/second per member.
- **Leaderboard fairness** — global rankings can feel demoralizing. Default to class scope, surface global only as an explicit toggle.
- **Friend request spam** — rate-limit requests per sender per target per week.
- **Study room scale** — limit study rooms to 8 members in v1 to keep presence sync simple.
- **Parent disable toggle** — must survive localStorage clear; always re-read from `/api/me/settings` on bootstrap.
- **Legal exposure** — anonymized peer solutions can still leak identity via writing style. Confirm with legal that display name + solution is acceptable, or require full anonymization.
