---
id: FIND-PRIVACY-018
task_id: t_fc830c05b406
severity: P1 — High
lens: privacy
tags: [reverify, privacy, ICO-Children, GDPR, safeguarding, moderation]
status: pending
assignee: unassigned
created: 2026-04-11
---

# FIND-privacy-018: Social UGC has no in-app reporting, blocking, or moderation

## Summary

Social UGC has no in-app reporting, blocking, or moderation

## Severity

**P1 — High**

## Requirements

The fix for this task MUST be production-grade:

- **No stubs, no canned data, no hardcoded objects, no `NotImplementedException`**
- **Labels must match actual data** — if a button says "Save", it must persist; if a metric says "tokens", it must count real tokens
- **Verify E2E** — query the DB, call the API, render the UI, compare field names
- **Include a CI-wired regression test** that fails on the current (buggy) commit and passes on the fix
- **Add a structured log line** on the error path so a re-regression is detectable in production

## Task body

framework: ICO-Children (Std 11 published policies / community standards), GDPR (Art 25 by design)
severity: P1 (high)
lens: privacy
related_prior_finding: none

## Goal

Add in-app reporting, blocking, and moderation to the social/UGC surface
(class feed, comments, peer solutions, friend requests, study rooms). Today
none of these exist — a child being bullied has no in-app way to report it.

## Background

`src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs:27-39` exposes
11 social endpoints:

```
GetClassFeed, GetPeerSolutions, GetFriends, GetStudyRooms,
AddReaction, AddComment, SendFriendRequest, AcceptFriendRequest,
CreateStudyRoom, JoinStudyRoom, LeaveStudyRoom
```

`grep -nri 'report\|abuse\|moderation\|block.*user' src/student/full-version/src/pages/social/`
returns zero matches. There is no /api/social/report endpoint, no /api/social/block
endpoint, no per-student blocklist, no moderation queue.

ICO Children's Code Standard 11 ("parental controls") requires "tools to
support children's right to be heard, to object, to challenge profiling
and to seek redress" — none of these exist for the social surface.

## Files

- `src/api/Cena.Student.Api.Host/Endpoints/SocialEndpoints.cs` (add report,
  block endpoints)
- `src/actors/Cena.Actors/Events/SocialEvents.cs` (add SocialReportFiled_V1,
  UserBlocked_V1, UserUnblocked_V1)
- `src/actors/Cena.Actors/Social/SocialReportDocument.cs` (NEW)
- `src/actors/Cena.Actors/Social/UserBlocklistDocument.cs` (NEW)
- `src/student/full-version/src/components/social/ReportDialog.vue` (NEW)
- `src/student/full-version/src/components/social/BlockUserDialog.vue` (NEW)
- `src/student/full-version/src/pages/social/{index,friends,leaderboard,peers}.vue`
  (add Report and Block buttons on every UGC surface)
- `src/api/Cena.Admin.Api/Moderation/ModerationQueueEndpoints.cs` (NEW —
  back office)
- `src/admin/full-version/src/pages/apps/moderation/queue.vue` (NEW)

## Definition of Done

1. POST /api/social/report endpoint accepts `{ contentType, contentId,
   category, reason }`. Severity inferred from category. Creates a
   SocialReportDocument with reportedAt, reportedBy, severity. Rate-limited
   to 10/hour per student to prevent abuse.
2. POST /api/social/block accepts `{ targetStudentId }`. Creates / updates
   the reporter's UserBlocklistDocument.
3. DELETE /api/social/block/{targetStudentId} unblocks.
4. Every social query (GetClassFeed, GetPeerSolutions, GetFriends, etc.)
   filters out content from blocked users.
5. "Report" button on every comment, friend request, study room, class-feed
   item, peer solution. Clicking opens ReportDialog with category options
   (Bullying, Inappropriate, Spam, Self-harm risk, Other).
6. "Block" button on every friend / peer / study-room participant.
7. Back-office moderation queue at /apps/moderation/queue for the
   safeguarding admin role with: filter by severity / category / age,
   review action (resolve, escalate, suspend account), audit trail.
8. For under-13 students (depends on FIND-privacy-001), default
   friend-requests, comments, and study rooms are gated entirely behind
   the parent consent gate.
9. Self-harm severity reports trigger an automatic high-priority
   SafeguardingAlert (cooperates with FIND-privacy-008) routed to the
   safeguarding admin within minutes.
10. E2E test: child A posts a comment, child B reports it, asserts a
    SocialReportDocument was created and visible in the moderation queue.
11. E2E test: child A blocks child B, then child B's comment doesn't appear
    in child A's class feed.

## Reporting requirements

Branch: `<worker>/<task-id>-privacy-018-social-moderation`. Result must
include:

- the new endpoint surface
- the report categories + severity mapping
- E2E test paths
- screenshots of the ReportDialog in all 3 locales
- a sample SocialReportDocument

## Out of scope

- Automated content classification of comments (manual moderation only for v1)
- Cross-tenant moderation (per-tenant only)
- Appeals process (deferred to a follow-up)


## Evidence & context

- Lens report: `docs/reviews/agent-privacy-reverify-2026-04-11.md`
- Merged report: `docs/reviews/cena-review-2026-04-11-reverify.md`
- Queue ID: `t_fc830c05b406`

## Definition of Done

1. Root cause identified and fixed (not symptoms)
2. Regression test added and wired into CI (`.github/workflows/`)
3. Structured log emitted on the error path
4. `dotnet build` succeeds with 0 errors
5. All existing tests pass (`dotnet test`)
6. Code review by coordinator (`claude-code`)
