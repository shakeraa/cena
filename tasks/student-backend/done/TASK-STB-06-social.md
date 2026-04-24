# TASK-STB-06: Social Learning (Class Feed, Peers, Friends, Study Rooms)

**Priority**: MEDIUM
**Effort**: 5-6 days
**Depends on**: [STB-00](TASK-STB-00-me-profile-onboarding.md), [STB-03](TASK-STB-03-gamification.md)
**UI consumers**: [STU-W-12](../student-web/TASK-STU-W-12-social-learning.md)
**Status**: Not Started

---

## Goal

Build the social backend surface — class feeds, peer solutions, friends, study rooms — with strict privacy defaults, age-gating, moderation integration, and the presence infrastructure study rooms need.

## Endpoints

| Method | Path | Purpose | Rate limit | Auth |
|---|---|---|---|---|
| `GET` | `/api/social/class-feed?page=&filter=` | Class feed items | `api` | JWT |
| `POST` | `/api/social/reactions` | React to a feed item | `social` (60/hour) | JWT |
| `POST` | `/api/social/comments` | Comment on a feed item | `social` (30/hour) | JWT, ≥13 |
| `GET` | `/api/social/peers/solutions?questionId=&subject=&sort=` | Browse peer solutions | `api` | JWT, ≥13 |
| `POST` | `/api/social/peers/solutions/{id}/vote` | Upvote/downvote | `social` | JWT, ≥13 |
| `POST` | `/api/social/peers/solutions/{id}/report` | Report inappropriate | `social` | JWT |
| `GET` | `/api/social/friends` | Friends list | `api` | JWT |
| `POST` | `/api/social/friends/request` | Send friend request | `social` (10/day) | JWT |
| `POST` | `/api/social/friends/{id}/accept` | Accept friend request | `api` | JWT |
| `POST` | `/api/social/friends/{id}/block` | Block user | `api` | JWT |
| `GET` | `/api/social/study-rooms` | List open study rooms | `api` | JWT |
| `POST` | `/api/social/study-rooms` | Create a study room | `social` (5/day) | JWT |
| `POST` | `/api/social/study-rooms/{id}/join` | Join a study room | `api` | JWT |
| `POST` | `/api/social/study-rooms/{id}/leave` | Leave a study room | `api` | JWT |

## Data Access

- **Reads**:
  - `ClassFeedItemProjection` (new async, per classroom)
  - `PeerSolutionProjection` (new async, filterable by subject/concept)
  - `FriendshipDocument` (new)
  - `StudyRoomDocument` (new, short-lived)
  - `StudyRoomMembershipDocument` (new, short-lived)
- **Writes**:
  - Reactions: append `ReactionAdded_V1`
  - Comments: append `CommentPosted_V1` (after moderation)
  - Peer solution upload: already happens during session completion; solutions surface here
  - Friend request: append `FriendRequestSent_V1`
  - Study room: document writes + presence tracking
- **Moderation hook**: every comment and peer solution text passes through the existing moderation pipeline (SAI work)
- **Statement timeout**: class feed pagination must hit a materialized projection, never a live aggregate

## Hub Events (additive, land in STB-10)

- `ClassFeedItemAdded`
- `ReactionChanged`
- `CommentAdded`
- `FriendRequestReceived`
- `FriendRequestAccepted`
- `StudyRoomPresenceChanged` — user joined/left/moved
- `StudyRoomMessagePosted` — chat message

## Privacy Enforcement

- Display name only; never full name / email / phone in any response
- Peer solutions anonymized by default; `author_display_name` nullable
- Under-13 students hard-blocked from: comments, peer solutions, leaderboard beyond class scope, study rooms with strangers
- Per-user mute and block honored server-side (server filters blocked users out of feeds and peer solutions)
- Parent / admin can disable social features via a settings flag; when disabled, all social endpoints return 403

## Contracts

Add to `Cena.Api.Contracts/Dtos/Social/`:

- `ClassFeedItemDto` with discriminator by type
- `ReactionDto`, `ReactionInputDto`
- `CommentDto`, `CommentInputDto`
- `PeerSolutionDto`, `PeerSolutionReportDto`
- `FriendshipDto`, `FriendRequestDto`
- `StudyRoomDto`, `StudyRoomMemberDto`, `StudyRoomMessageDto`

## Auth & Authorization

- Firebase JWT
- Age check on comment, peer solution, and study-room endpoints (≥13 required)
- `ResourceOwnershipGuard` on friend / mute / block operations
- Classroom membership required for class-feed reads

## Cross-Cutting

- Comment + peer solution text passes through content moderation before persistence
- Reactions rate-limited per student per item per minute
- Friend requests rate-limited per sender per target per week
- Study room sizes capped at 8 members in v1
- Hub presence events throttled to 1/second per member
- Handler logs with `correlationId`, `endpoint=social.*`

## Definition of Done

- [ ] All 14 endpoints implemented and registered in `Cena.Student.Api.Host`
- [ ] DTOs in `Cena.Api.Contracts/Dtos/Social/`
- [ ] New projections enabled and tested against seeded events
- [ ] Age gating verified (under-13 test account)
- [ ] Parent-disabled social blocks all endpoints with 403
- [ ] Moderation rejects profanity and hate speech in comments (integration test with sample inputs)
- [ ] Peer solutions default to anonymous; opt-in flag works
- [ ] Study room presence events propagate within 200 ms
- [ ] Rate limits enforced and verified
- [ ] Integration tests cover: feed pagination, reaction add/remove, comment post + moderation reject, peer solution browse/vote/report, friend request flow, study room create/join/leave
- [ ] OpenAPI spec updated
- [ ] TypeScript types regenerated

## Out of Scope

- Live video rooms
- Direct messaging
- DM group chats
- Peer solution quality ranking via ML — use simple vote counts for v1
- Classroom-wide announcements with targeted delivery — use the class feed
- Teacher moderation tools — admin surface, separate
