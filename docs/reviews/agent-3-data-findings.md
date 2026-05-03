---
agent: claude-subagent-data
role: Data, Performance & Projection Architect
date: 2026-04-11
branch: claude-subagent-data/review-2026-04-11
worktree: .claude/worktrees/review-data
scope:
  - Marten projection registration + event drift
  - Event replay determinism (Apply purity)
  - N+1 query hunting in list endpoints
  - Unbounded queries + pagination defaults
  - CQRS violations (write-side reads read-models)
  - Raw SQL / schema coupling
  - AI prompt persistence (events vs docs)
postgres_reachable: false
postgres_note: "docker not running; no live EXPLAIN ANALYZE evidence. Static + schema analysis only."
files_scanned_cs: 469
query_allrawevents_hits: 55
severity_counts:
  p0: 7
  p1: 6
  p2: 5
  p3: 1
---

# Agent 3 — Data, Performance & Projection Findings (2026-04-11)

## Executive summary

The event store is configured correctly at the Marten layer (single-tenant mode, inline/async projections, upcasters wired). BUT the code that reads from it is broken or unsafe in several systemic ways:

1. **Read-layer bypass via `QueryAllRawEvents`** — 55 usages across 16 files pull every event of a type across every tenant into memory, then filter in LINQ. Unbounded, full-scan, no index involvement. User-facing endpoints rely on this pattern (student analytics, gamification, session detail).
2. **Event type name predicates use three different conventions** (`_v1`, `__v1`, `nameof(...)`). At least 30+ predicate strings never match any stored event, so the queries silently return empty forever.
3. **Two "inline snapshot" projections have no Apply/Create methods** (`LearningSessionQueueProjection`, `ActiveSessionSnapshot`). Marten cannot rebuild them from events; endpoint code mutates them directly, and the next event append will race with the projection machinery.
4. **Projection event drift in the question bank**: `QuestionOptionChanged_V1`, `QuestionForked_V1`, and `LanguageVersionAdded_V1` are emitted + handled by the aggregate but NOT by the read model — so the UI list view silently falls out of sync.
5. **`RegisterNotificationEvents(opts)` is defined but never called** from `ConfigureCommon`. Four notification event types are never registered, yet they are written to the event store from endpoints.
6. **`ThreadSummaryProjection` and its source events (`ThreadCreated_V1`, `MessageSent_V1`) are completely unregistered AND never written to the Marten event store**. The admin messaging UI queries an empty read model.
7. **`ClassFeedItemProjection` uses `DateTime.UtcNow` as a fallback** — non-deterministic replay.
8. **Leaderboard service uses raw string-interpolated SQL** bound to Marten internal table names (`cena.mt_doc_studentprofilesnapshot`), with no functional index on the XP column it sorts by.

I did not run Postgres, so every finding below is backed by file:line + source citation, not an EXPLAIN plan.

---

## Findings

### P0 — Critical

```yaml
- id: FIND-data-001
  severity: p0
  category: event-replay-determinism
  title: "ClassFeedItemProjection seeds PostedAt from DateTime.UtcNow when event is default — non-deterministic replay"
  files:
    - path: src/actors/Cena.Actors/Projections/ClassFeedItemProjection.cs
      lines: "39"
  evidence: |
    PostedAt = e.AwardedAt == default ? DateTime.UtcNow : e.AwardedAt.UtcDateTime
    This projection is registered as Async (MartenConfiguration.cs:95).
    Every rebuild of the projection on an event whose AwardedAt is the default
    value (0001-01-01) yields a different PostedAt. Event sourcing requires
    deterministic replay — given the same events, a projection rebuild must
    yield identical state. A rebuild triggered by a Marten daemon restart
    or a manual "rebuild projection" invocation would produce a read model
    inconsistent with any prior materialised state, and ordering in the
    class feed (ORDER BY PostedAt DESC) would flip between rebuilds.
  fix_hint: |
    Use a domain timestamp that is ALWAYS in the event payload. If AwardedAt
    can legitimately be default, reject the event at the emit site. Never
    read wall-clock time inside Apply/Project.

- id: FIND-data-002
  severity: p0
  category: projection-registration-bug
  title: "RegisterNotificationEvents is defined but never called from ConfigureCommon — 4 notification events written but not registered"
  files:
    - path: src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      lines: "70-78, 526-532"
  evidence: |
    ConfigureCommon calls:
      RegisterLearnerEvents(opts);        // line 70
      RegisterPedagogyEvents(opts);       // line 71
      RegisterEngagementEvents(opts);     // line 72
      RegisterOutreachEvents(opts);       // line 73
      RegisterQuestionEvents(opts);       // line 74
      RegisterFocusEvents(opts);          // line 75
      ...
    But RegisterNotificationEvents (line 526) is NEVER called.
    Meanwhile, NotificationsEndpoints.cs writes:
      session.Events.Append(studentId, new NotificationDeleted_V1(...))   // line 221
    and similar for NotificationSnoozed_V1, WebPushSubscribed_V1,
    WebPushUnsubscribed_V1. Marten will either auto-register lazily on
    append (silently, without upcaster registration) or fail at read time
    because no upcaster chain exists for the type alias. The read path is
    broken either way — any future projection that wants to consume these
    events will not see them until the type is registered.
  fix_hint: |
    Add RegisterNotificationEvents(opts); in ConfigureCommon between the
    existing Register* calls. Verify the notification events are appearing
    in mt_events after a round-trip test.

- id: FIND-data-003
  severity: p0
  category: projection-incomplete
  title: "LearningSessionQueueProjection registered as Inline Snapshot but has NO Apply/Create methods — projection cannot rebuild from events"
  files:
    - path: src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      lines: "181"
    - path: src/actors/Cena.Actors/Projections/LearningSessionQueueProjection.cs
      lines: "1-100"
    - path: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
      lines: "587, 704"
  evidence: |
    MartenConfiguration.cs:181:
      opts.Projections.Snapshot<LearningSessionQueueProjection>(SnapshotLifecycle.Inline);
    But grep for `public void Apply|public .* Create` on the type returns zero matches.
    The type has methods EnqueueQuestions, DequeueNext, RecordAnswer, PeekNext,
    GetAccuracy — plain domain-object mutators, not Marten Apply handlers.
    Meanwhile, SessionEndpoints.cs:587 and :704 do:
      session.Store(queue);
    directly from the endpoint. This is NOT event sourcing — it's just a
    manually-maintained document that happens to also be registered as a
    Marten inline snapshot. On the next `session.Events.Append(...)` for
    ANY event type targeting sessionId, Marten's snapshot daemon will try
    to rebuild the projection from events, find no Apply handlers, reset
    the document to default, and overwrite the endpoint's manual state.
  fix_hint: |
    Either:
    (a) Remove the Snapshot<...>(Inline) registration and treat it as a
        plain document (opts.Schema.For<T>().Identity(...)), or
    (b) Implement Create(...) and Apply(...) methods for the events that
        should drive the projection (LearningSessionStarted_V1,
        QuestionAnsweredInSession_V1, LearningSessionEnded_V1) and stop
        mutating the document directly from the endpoint.
    Pick ONE pattern. Current state is worst-of-both.

- id: FIND-data-004
  severity: p0
  category: projection-incomplete
  title: "ActiveSessionSnapshot registered as Inline Snapshot with zero Apply/Create methods — same broken pattern as FIND-data-003"
  files:
    - path: src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      lines: "178"
    - path: src/actors/Cena.Actors/Projections/ActiveSessionSnapshot.cs
      lines: "12-36"
  evidence: |
    MartenConfiguration.cs:178:
      opts.Projections.Snapshot<ActiveSessionSnapshot>(SnapshotLifecycle.Inline);
    ActiveSessionSnapshot.cs has only:
      - public int GetProgressPercent() (line 29, NOT an Apply method)
      - Property auto-setters (no event-driven logic)
    Same consequence as FIND-data-003 — Marten cannot build this from
    events. Any session endpoint that writes to it via session.Store(doc)
    will be silently reset on the next event append.
  fix_hint: |
    Same as FIND-data-003.

- id: FIND-data-005
  severity: p0
  category: dead-query
  title: "StudentInsightsService uses '__v1' (double underscore) for ALL 13 event type predicates — every query returns empty forever"
  files:
    - path: src/api/Cena.Admin.Api/StudentInsightsService.cs
      lines: "50, 86, 119, 133, 153, 186, 233, 260, 293, 323, 357, 363, 439"
    - path: src/api/Cena.Admin.Api/FocusAnalyticsService.cs
      lines: "134"
    - path: src/api/Cena.Admin.Api/AdminDashboardService.cs
      lines: "157-161, 177-178"
  evidence: |
    StudentInsightsService line 50:
      .Where(e => e.EventTypeName == "focus_score_updated__v1")
    Marten's default NameToAlias() on typeof(FocusScoreUpdated_V1).Name
    returns "focus_score_updated_v1" (single underscore separator between
    the base name and the _v1 version suffix).
    Proof: other files in the same codebase use the correct single-
    underscore form:
      StudentAnalyticsEndpoints.cs:55  "concept_attempted_v1"
      SessionEndpoints.cs:246          "concept_attempted_v1"
      SessionEndpoints.cs:375          "concept_attempted_v1"
      GamificationEndpoints.cs:60      "concept_attempted_v1"
    vs StudentInsightsService.cs:186  "concept_attempted__v1"  ← broken
    Both strings cannot be correct; the aliasing is deterministic. The
    admin insights dashboards, focus analytics dashboards, and admin
    question metrics dashboards are therefore showing empty data. The
    UI label says "real events", the query returns zero rows — classic
    lying label.
    Additionally, AdminDashboardService.cs:160-161 references event types
    that don't exist at all ("question_reviewed__v1", "question_approved__v1"):
      - question_approved_v1 is registered (MartenConfiguration.cs:552)
      - question_reviewed_v1 is NOT a registered event type anywhere in
        the codebase (grep confirms).
  fix_hint: |
    Replace all "__v1" with "_v1" in these three services.
    For AdminDashboardService, remove the "question_reviewed" predicate
    entirely (it's a ghost event type), and decide whether the intended
    lifecycle event is QuestionQualityEvaluated_V1 (NeedsReview decision).
    Add an integration test that appends one event of each type and then
    asserts the service returns a non-empty list.

- id: FIND-data-006
  severity: p0
  category: dead-query
  title: "ExperimentAdminService uses nameof(T) for EventTypeName — always returns PascalCase which never matches Marten's snake_case aliases"
  files:
    - path: src/api/Cena.Admin.Api/ExperimentAdminService.cs
      lines: "54, 102, 121, 127, 178, 188, 197, 208, 219"
  evidence: |
    ExperimentAdminService.cs:54:
      .Where(e => e.EventTypeName == nameof(SessionStarted_V1))
    nameof(SessionStarted_V1) returns the string "SessionStarted_V1"
    (exact source identifier). Marten's EventTypeName stores the snake_case
    alias "session_started_v1". String comparison against "SessionStarted_V1"
    will never match a single row. Same bug for:
      nameof(TutoringEpisodeCompleted_V1)  → "TutoringEpisodeCompleted_V1"  vs  "tutoring_episode_completed_v1"
      nameof(ConceptMastered_V1)           → "ConceptMastered_V1"          vs  "concept_mastered_v1"
      nameof(TutoringSessionStarted_V1)    → "TutoringSessionStarted_V1"   vs  "tutoring_session_started_v1"
      nameof(AnnotationAdded_V1)           → "AnnotationAdded_V1"          vs  "annotation_added_v1"
    9 broken predicates total. The experiment admin analytics UI is
    showing hard-coded zeros or empty lists.
  fix_hint: |
    Option A (quick): replace nameof(...) with the string literal
    "session_started_v1", etc.
    Option B (robust): add a helper EventTypeAlias<T>() that derives the
    alias via Marten's MartenRegistry.EventTypeFor<T>() at startup, and
    store the aliases in a dictionary. This avoids the naming-drift bug
    if Marten's alias convention changes in a future upgrade.
    Add the same integration test as FIND-data-005.

- id: FIND-data-007
  severity: p0
  category: cqrs-violation
  title: "SessionEndpoints.SubmitAnswer manually Stores StudentProfileSnapshot from the endpoint — races with Marten's Inline snapshot projection"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
      lines: "640-657"
    - path: src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      lines: "172"
  evidence: |
    MartenConfiguration.cs:172:
      opts.Projections.Snapshot<StudentProfileSnapshot>(SnapshotLifecycle.Inline);
    This means every time an event is appended to a student stream, Marten
    calls StudentProfileSnapshot.Apply(event) to rebuild the snapshot from
    the event stream, then persists the new document — overwriting whatever
    is there.
    SessionEndpoints.cs:642-654 does:
      session.Events.Append(studentId, conceptAttempt);
      ...
      profile = new StudentProfileSnapshot { StudentId = studentId, CreatedAt = UtcNow };
      profile.Apply(xpEvent);
      session.Store(profile);
      await session.SaveChangesAsync();
    On SaveChangesAsync, Marten will:
      1. Append conceptAttempt → rebuild StudentProfileSnapshot from ALL
         events in the stream (the inline snapshot projection runs).
      2. ALSO persist the session.Store(profile) — order unspecified.
    Which write wins is a race. Worse, the endpoint's manual Apply(xpEvent)
    is done OUT-OF-BAND from the event stream — the xpEvent is NOT appended
    to Marten (grep confirms only conceptAttempt is Appended in this block),
    so the XP increment survives only in the document column, not in the
    event stream, and the next event append will rebuild the snapshot
    WITHOUT the XP increment, losing it permanently.
    Additionally, StudentProfileSnapshot.CreatedAt is never set by any
    event handler, only set once by StudentActor.cs:514 when the actor
    initializes fresh in-memory. So any rebuild of the snapshot yields
    CreatedAt = default(DateTimeOffset) = 0001-01-01. This is a silent
    field degradation every time an event is appended to a student's stream.
  fix_hint: |
    1. Append the XP event (XpAwarded_V1) to the stream. That's the event-
       sourced fix. Let the inline projection absorb it.
    2. Remove the manual session.Store(profile) call.
    3. Move CreatedAt handling to a dedicated StudentOnboarded_V1 event (or
       use the OnboardingCompleted_V1 already registered) and add an Apply
       handler on StudentProfileSnapshot that sets CreatedAt from e.Timestamp.
```

### P1 — High

```yaml
- id: FIND-data-008
  severity: p1
  category: projection-event-drift
  title: "QuestionListProjection does not handle 3 events that QuestionState (aggregate) does handle — read-model silently stale after option edits, forks, language additions"
  files:
    - path: src/actors/Cena.Actors/Questions/QuestionListProjection.cs
      lines: "13-162"
    - path: src/actors/Cena.Actors/Questions/QuestionState.cs
      lines: "195-301"
  evidence: |
    Aggregate (QuestionState) handles:
      QuestionAuthored_V1          Apply line 117
      QuestionIngested_V1          Apply line 138
      QuestionAiGenerated_V1       Apply line 162
      QuestionStemEdited_V1        Apply line 187
      QuestionOptionChanged_V1     Apply line 195   ← read-model does NOT handle
      QuestionMetadataUpdated_V1   Apply line 208
      QuestionQualityEvaluated_V1  Apply line 233
      QuestionApproved_V1          Apply line 250
      QuestionPublished_V1         Apply line 257
      QuestionDeprecated_V1        Apply line 264
      ExplanationEdited_V1         Apply line 271
      QuestionExplanationUpdated_V1 Apply line 278
      QuestionForked_V1            Apply line 285   ← read-model does NOT handle
      LanguageVersionAdded_V1      Apply line 293   ← read-model does NOT handle
    Read model (QuestionListProjection):
      QuestionAuthored_V1           Create line 17
      QuestionIngested_V1           Create line 27
      QuestionAiGenerated_V1        Create line 37
      ExplanationEdited_V1          Apply  line 49
      QuestionExplanationUpdated_V1 Apply  line 55
      QuestionStemEdited_V1         Apply  line 63
      QuestionMetadataUpdated_V1    Apply  line 69
      QuestionQualityEvaluated_V1   Apply  line 99
      QuestionApproved_V1           Apply  line 109
      QuestionPublished_V1          Apply  line 115
      QuestionDeprecated_V1         Apply  line 121
    Missing from the read model:
      - QuestionOptionChanged_V1   (option text/correctness edits never appear in list view)
      - QuestionForked_V1          (fork event is a no-op on the source stream — OK to ignore)
      - LanguageVersionAdded_V1    (list view never shows additional languages)
    QuestionListProjection is registered as Inline (MartenConfiguration.cs:92)
    so any event append triggers a projection update. Since QuestionOptionChanged_V1
    has no handler, the read model's updatedAt timestamp will not advance when
    an admin edits an option, and the list view's stale-detection UI will lie.
  fix_hint: |
    Add Apply(QuestionOptionChanged_V1 e, QuestionReadModel model) that
    updates model.UpdatedAt at minimum. If the list view shows option
    correctness anywhere, also update that. For LanguageVersionAdded_V1,
    maintain a list of languages on the read model.

- id: FIND-data-009
  severity: p1
  category: unbounded-query
  title: "API Host endpoints call QueryAllRawEvents() and filter by stream/student in memory — cross-tenant full-scan every request"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/StudentAnalyticsEndpoints.cs
      lines: "54-60, 139-146"
    - path: src/api/Cena.Api.Host/Endpoints/SessionEndpoints.cs
      lines: "245-251, 374-380"
    - path: src/api/Cena.Api.Host/Endpoints/GamificationEndpoints.cs
      lines: "53-56, 59-62, 72-75, 176-177, 317-320"
  evidence: |
    StudentAnalyticsEndpoints.cs:54-60:
      var allAttemptEvents = await session.Events.QueryAllRawEvents()
          .Where(e => e.EventTypeName == "concept_attempted_v1")
          .ToListAsync();
      var studentAttempts = allAttemptEvents
          .Where(e => ExtractString(e, "studentId") == studentId)
          .ToList();
    This pattern loads EVERY concept_attempted_v1 event for EVERY student
    across EVERY tenant on EVERY /api/analytics/summary call. Once the
    system has a few thousand active students, each analytics request
    streams millions of rows back to the API process and sorts in memory.
    The correct primitive is session.Events.FetchStreamAsync(studentId)
    or an indexed document projection scoped to the student. GamificationEndpoints
    at line 62 does the same pattern (QueryAllRawEvents then .Where(StreamKey
    == studentId)) — StreamKey is indexed so this could use session.Events.
    FetchStreamAsync, but the current code still scans every event type
    alias first.
    Five separate endpoint handlers in GamificationEndpoints.cs do this
    three times each for badges/XP/streak calculations (lines 53-75), so
    a single /api/gamification/badges call fires 3 full event-scan queries.
    Postgres not reachable; no EXPLAIN available. Pure pattern-based finding.
  fix_hint: |
    Replace QueryAllRawEvents() with either:
      - session.Events.FetchStreamAsync(streamId)   (for per-student)
      - A materialised projection indexed on (StudentId, Date)
    For the student analytics summary, introduce a StudentLifetimeStats
    projection (fields: totalAttempts, totalCorrect, totalSessions) that
    applies on ConceptAttempted, LearningSessionStarted, LearningSessionEnded.

- id: FIND-data-010
  severity: p1
  category: n+1-query
  title: "SocialEndpoints.GetFriends performs N+1 queries for friend + pending-request profiles"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/SocialEndpoints.cs
      lines: "144-180"
  evidence: |
    Friends path:
      var friendships = await session.Query<FriendshipDocument>()
          .Where(...) .ToListAsync();                    // 1 query
      foreach (var f in friendships) {
          var friendProfile = await session.LoadAsync<StudentProfileSnapshot>(friendId);  // +1 per friend
          ...
      }
    Then:
      var pendingDocs = await session.Query<FriendRequestDocument>()
          .Where(...) .ToListAsync();                    // 1 query
      foreach (var r in pendingDocs) {
          var fromProfile = await session.LoadAsync<StudentProfileSnapshot>(r.FromStudentId); // +1 per request
          ...
      }
    Total queries per request: 2 + (friendCount + pendingCount). For a
    student with 50 friends and 5 pending requests: 57 sequential round-trips.
    No pagination. The endpoint also returns a placeholder StreakDays: 0
    and IsOnline: false (line 161-162) so the label-vs-data rule is
    partially broken.
  fix_hint: |
    var ids = friendships.Select(...).Concat(pendingDocs.Select(r => r.FromStudentId)).Distinct().ToList();
    var profiles = await session.Query<StudentProfileSnapshot>()
        .Where(p => ids.Contains(p.StudentId))
        .ToListAsync()
        .ContinueWith(t => t.Result.ToDictionary(p => p.StudentId));
    Then the foreach does a dictionary lookup instead of a DB round-trip.
    Drop StreakDays/IsOnline from the DTO or actually compute them.

- id: FIND-data-011
  severity: p1
  category: n+1-query
  title: "SocialEndpoints.GetStudyRooms performs 2 queries per room (member count + host profile)"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/SocialEndpoints.cs
      lines: "203-232"
  evidence: |
      var rooms = await session.Query<StudyRoomDocument>()...ToListAsync();  // 1 query
      foreach (var r in rooms) {
          var memberCount = await session.Query<StudyRoomMembershipDocument>()
              .CountAsync(m => m.RoomId == r.RoomId && m.IsActive);  // +1 per room
          var hostProfile = await session.LoadAsync<StudentProfileSnapshot>(r.HostStudentId);  // +1 per room
          ...
      }
    Total: 1 + 2*roomCount queries per /api/social/study-rooms call.
    For a public discovery view of 100 rooms, that's 201 round-trips.
  fix_hint: |
    Batch-load memberships:
      var roomIds = rooms.Select(r => r.RoomId).ToList();
      var counts = await session.Query<StudyRoomMembershipDocument>()
          .Where(m => roomIds.Contains(m.RoomId) && m.IsActive)
          .GroupBy(m => m.RoomId)
          .Select(g => new { RoomId = g.Key, Count = g.Count() })
          .ToListAsync();
    Same trick for profiles.
    OR maintain MemberCount and HostDisplayName as denormalized columns
    on StudyRoomDocument, updated by an event projection on room join/leave.

- id: FIND-data-012
  severity: p1
  category: dead-projection
  title: "ThreadSummaryProjection is never registered + ThreadCreated_V1/MessageSent_V1 never persisted to Marten — admin messaging UI always empty"
  files:
    - path: src/actors/Cena.Actors/Messaging/ThreadSummary.cs
      lines: "38-62"
    - path: src/actors/Cena.Actors/Configuration/MartenConfiguration.cs
      lines: "70-76, 526"
    - path: src/actors/Cena.Actors/Messaging/ConversationThreadActor.cs
      lines: "142-148, 160-165"
    - path: src/api/Cena.Admin.Api/MessagingAdminService.cs
      lines: "40-77, 79-100"
  evidence: |
    1. ThreadSummaryProjection is defined (ThreadSummary.cs:38) but is
       NOT in MartenConfiguration.cs — grep for "ThreadSummaryProjection"
       in the config file returns zero matches.
    2. ThreadCreated_V1 and MessageSent_V1 are NOT in any AddEventType
       call (grep confirms the full list of registered events in
       MartenConfiguration.cs does not include them).
    3. ConversationThreadActor.cs:146 publishes ThreadCreated_V1 to NATS:
         await _eventPublisher.PublishThreadCreatedAsync(threadCreated);
       NOT to session.Events.Append(). Grep for session.Events.Append on
       these event types returns only the publishers in
       MessagingNatsPublisher.cs (NATS IPublisher, not Marten).
    4. MessagingAdminService.cs:40 then queries:
         session.Query<ThreadSummary>()
       which will always return empty because no projection has ever
       written a ThreadSummary row. Line 91:
         session.Events.FetchStreamAsync(threadId)
       returns zero events because nothing ever appended to that stream.
    Net effect: admin messaging views are permanently stubbed-out despite
    the service layer looking production-ready.
  fix_hint: |
    Decide the source of truth:
    (a) Messaging lives in Redis + NATS (current), and the admin view
        should query the same Redis streams, NOT a Marten projection.
        Delete ThreadSummary + ThreadSummaryProjection + the
        MessagingAdminService.GetThreadDetailAsync FetchStreamAsync path.
    (b) Messaging also persists to Marten for audit/admin. In that case:
        - Add ThreadCreated_V1 and MessageSent_V1 to RegisterEvents.
        - Register the ThreadSummaryProjection as Inline or Async.
        - Have ConversationThreadActor append the event via
          session.Events.Append in addition to publishing to NATS.
    Pick one. Current state is a deceptive facade.

- id: FIND-data-013
  severity: p1
  category: unbounded-query
  title: "NotificationsEndpoints.GetNotifications materialises all rows for the student before paginating in memory"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/NotificationsEndpoints.cs
      lines: "69-92"
  evidence: |
    var notifications = await query
        .OrderByDescending(n => n.CreatedAt)
        .ToListAsync();                       // line 69-71: no Take/Skip — all rows
    var visible = notifications
        .Where(n => n.SnoozedUntil == null || n.SnoozedUntil < now)  // memory filter
        .Select(...)
        .ToList();                            // line 86
    const int pageSize = 10;
    ...
    var pagedItems = visible.Skip(skip).Take(pageSize).ToArray();    // in-memory paging
    For a student with 2000 notifications, every /api/notifications call
    fetches all 2000, filters, then returns 10. The SnoozedUntil predicate
    could be pushed to SQL (n.SnoozedUntil == null || n.SnoozedUntil < now)
    since Marten LINQ supports DateTime comparisons on scalar fields.
  fix_hint: |
    Push the snooze filter into the Where clause:
      query = query.Where(n => n.SnoozedUntil == null || n.SnoozedUntil < now);
    Then .Skip((currentPage-1)*pageSize).Take(pageSize).ToListAsync().
    Run the .Count() + .CountAsync for the unreadCount separately as a
    scalar query (the current total requires two hits — that's fine).
```

### P2 — Normal

```yaml
- id: FIND-data-014
  severity: p2
  category: cqrs-smell
  title: "StudentActor (write side) reads QuestionReadModel from the inline projection during AttemptConcept"
  files:
    - path: src/actors/Cena.Actors/Students/StudentActor.Commands.cs
      lines: "160-177, 296-318"
  evidence: |
    await using var qs = _documentStore.QuerySession();
    var readModel = await qs.LoadAsync<Questions.QuestionReadModel>(cmd.QuestionId);
    ...
    explanation = readModel?.Explanation ?? "";
    The write-side actor reads from a read-side projection. This works
    because QuestionListProjection is Inline so it's synchronous with
    question appends — but it couples the command path to the read-model
    schema and will break if the projection is moved to Async for scale.
  fix_hint: |
    Read the question via session.Events.AggregateStreamAsync<QuestionState>(questionId)
    instead. That's the aggregate-backed path and doesn't depend on the
    read model staying inline.

- id: FIND-data-015
  severity: p2
  category: raw-sql-coupling
  title: "LeaderboardService uses raw SQL with string interpolation and hardcoded Marten table name"
  files:
    - path: src/shared/Cena.Infrastructure/Gamification/LeaderboardService.cs
      lines: "81-89, 147-156, 214-224, 267-271, 300-303, 322-326, 349-353"
  evidence: |
    Line 87: FROM cena.mt_doc_studentprofilesnapshot
    This is Marten's auto-generated document table name. Any schema rename
    (which Marten can do silently on a type rename) will break the
    leaderboard. Also:
      line 154: WHERE data->>'SchoolId' = '{classroom.SchoolId}'
      line 222: WHERE data->>'StudentId' IN ('{idList}')
      line 271: WHERE data->>'StudentId' = '{studentId}'
      line 303: > {studentXp}
    These are string-interpolated. The security finding belongs to Agent 2;
    the DATA finding is that the queries bypass Marten's LINQ provider,
    so schema changes and compiled-query caching both go out the window.
    Also: the global leaderboard does ORDER BY COALESCE((data->>'TotalXp')::int, 0) DESC
    with NO index on that expression. A full-table sort on every call.
    The SchoolId index on StudentProfileSnapshot exists (MartenConfiguration.cs:175)
    but TotalXp has no index.
  fix_hint: |
    Rewrite as Marten LINQ:
      await session.Query<StudentProfileSnapshot>()
          .OrderByDescending(s => s.TotalXp)
          .Take(limit)
          .ToListAsync();
    Add an Index(s => s.TotalXp) on StudentProfileSnapshot in MartenConfiguration.cs
    so the sort becomes a reverse index scan. For the rank queries, prefer
    session.Query<T>().CountAsync(s => s.SchoolId == schoolId && s.TotalXp > studentXp).

- id: FIND-data-016
  severity: p2
  category: query-planner-hostile
  title: "StudentActor.HandleResumeSession uses Id OR SessionId equality in the same predicate — defeats both indexes"
  files:
    - path: src/actors/Cena.Actors/Students/StudentActor.Commands.cs
      lines: "485-488"
  evidence: |
    var doc = await querySession.Query<Cena.Actors.Tutoring.TutoringSessionDocument>()
        .FirstOrDefaultAsync(d => d.Id == cmd.SessionId || d.SessionId == cmd.SessionId);
    The OR forces a sequential scan or a UNION plan. Both Id and SessionId
    are indexed (MartenConfiguration.cs:159-161), but the planner can't
    pick one. This pattern also appears in SessionEndpoints.cs:235 and :365.
  fix_hint: |
    Decide whether the caller is passing the document Id or the SessionId.
    If both forms are legitimate, try the Id load first (the identity
    lookup is a primary-key hit), then fall back to a SessionId query only
    if the first missed.

- id: FIND-data-017
  severity: p2
  category: unbounded-query
  title: "ChallengesEndpoints.GetDailyLeaderboard loads every daily completion into memory before sorting and .Take(10)"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/ChallengesEndpoints.cs
      lines: "105-123"
  evidence: |
    var allCompletions = await session.Query<DailyChallengeCompletionDocument>()
        .Where(c => c.Date == today)
        .ToListAsync();                    // no Take
    var ranked = allCompletions
        .OrderByDescending(c => c.Score)
        .ThenBy(c => c.TimeSeconds)
        .Select((c, i) => new { Rank = i+1, Completion = c })
        .ToList();
    var topTen = ranked.Take(10)...
    Once daily-challenge participation reaches 10k+ students, every
    leaderboard call loads 10k rows and sorts in .NET. Also, the
    leaderboard is GLOBAL — no SchoolId filter — which crosses tenants
    for a supposedly class-based UI.
  fix_hint: |
    await session.Query<DailyChallengeCompletionDocument>()
        .Where(c => c.Date == today)
        .OrderByDescending(c => c.Score)
        .ThenBy(c => c.TimeSeconds)
        .Take(10)
        .ToListAsync();
    For "my rank" line 125, do a scalar CountAsync(c => c.Date == today
    && (c.Score > myScore || (c.Score == myScore && c.TimeSeconds < myTime))) + 1.
    Add a SchoolId filter — the current global leaderboard is a tenant leak.

- id: FIND-data-018
  severity: p2
  category: unbounded-query
  title: "TutorEndpoints.GetMessages returns every message in a thread with no pagination and HasMore hard-coded to false"
  files:
    - path: src/api/Cena.Api.Host/Endpoints/TutorEndpoints.cs
      lines: "177-192"
  evidence: |
    var messages = await session.Query<TutorMessageDocument>()
        .Where(m => m.ThreadId == threadId)
        .OrderBy(m => m.CreatedAt)
        .ToListAsync();                                // no Take
    ...
    return Results.Ok(new TutorMessageListDto(
        ThreadId: threadId,
        Messages: dtos,
        HasMore: false)); // Phase 1: no pagination
    A tutor thread is a natural long-running conversation. No bound means
    a single OOM-shaped response. The HasMore=false is a lying label.
  fix_hint: |
    Add a limit + before-cursor parameter, Take(limit+1), and set HasMore
    based on whether the extra row came back. The thread actor already
    supports cursor-based reads (GetThreadHistory handler in
    ConversationThreadActor.cs) — same shape here.
```

### P3 — Low

```yaml
- id: FIND-data-019
  severity: p3
  category: file-size
  title: "AiGenerationService.cs is 916 lines — violates CLAUDE.md 500-line file size rule"
  files:
    - path: src/api/Cena.Admin.Api/AiGenerationService.cs
      lines: "1-916"
  evidence: |
    wc -l reports 916 lines. CLAUDE.md:
      "Keep files under 500 lines"
    Also contains four provider call-site shapes in one file (Anthropic
    real, OpenAI/Google/Azure NotImplementedException stubs). This will
    grow as more providers are added.
  fix_hint: |
    Split into AiGenerationService (orchestration) + AnthropicProvider +
    PromptBuilder + ProviderConfigService. 
```

---

## Non-findings (categories where I looked and found nothing)

- **AI prompt event storage**: `QuestionAiGenerated_V1` correctly stores `PromptText`, `ModelId`, `ModelTemperature`, `RawModelOutput` as event payload fields (QuestionEvents.cs:59-77). Prompts ARE in the event stream. `AiGenerationService` correctly returns the prompt in its DTO for the caller to embed when creating the question. Pattern is correct. (no findings)
- **Write-side aggregate reads of `DateTime.UtcNow` / `Guid.NewGuid` inside Apply handlers**: grep'd `QuestionState.Apply*`, `StudentProfileSnapshot.Apply*`, `ThreadSummary.Apply*` — no occurrences. All timestamps in Apply methods come from event payload (`e.Timestamp`, `e.AwardedAt`). One exception is FIND-data-001 (ClassFeedItemProjection). (good)
- **Event upcaster correctness**: ConceptAttemptedV1ToV2Upcaster is registered (MartenConfiguration.cs:569) and the V2 event is registered (line 458). StudentProfileSnapshot has Apply handlers for both V1 and V2 (lines 62-90). That path is clean. (no findings)
- **Tenancy-style index on tenant_id**: Marten is configured `TenancyStyle.Single`, so tenant isolation is entirely app-level. This is a known trade-off and the SchoolId indexes exist on the 16 doc types most at risk. The remaining docs (TutoringSessionDocument, ClassFeedItemDocument, TutorThreadDocument, NotificationDocument) scope by StudentId which is owner-bound. Not a data finding per se — Agent 2's authZ domain.

---

## Postgres / live evidence

`docker ps` returned no running containers; `pg_isready` was not on PATH.
No database was reachable during this review.
`EXPLAIN ANALYZE` evidence is therefore absent from this report.
All findings are file:line + source citation only.

If Postgres becomes reachable, the highest-leverage checks would be:
1. `EXPLAIN (ANALYZE, BUFFERS) SELECT * FROM cena.mt_events WHERE event_type_name = 'concept_attempted_v1';`
   to verify the event_type_name column has an index (Marten default: yes, via `mt_events_event_type_idx`).
2. `SELECT COUNT(*) FROM cena.mt_doc_studentprofilesnapshot GROUP BY (data->>'SchoolId');` to verify tenant distribution matches expected shape.
3. `EXPLAIN (ANALYZE, BUFFERS) SELECT ... FROM cena.mt_doc_studentprofilesnapshot ORDER BY (data->>'TotalXp')::int DESC LIMIT 100;` to confirm FIND-data-015's sort is a full scan.

---

## Enqueued Tasks

The following P0/P1 tasks have been enqueued on the shared queue as priority `critical`/`high`. IDs are populated after the bash calls immediately following this document's commit.

| Finding ID | Enqueued Task ID | Priority |
|---|---|---|
| FIND-data-001 | t_358ad20a7cfb | critical |
| FIND-data-002 | t_e819cb261f43 | critical |
| FIND-data-003 | t_9db1ff67567c | critical |
| FIND-data-004 | t_c9e788d0867e | critical |
| FIND-data-005 | t_8e6f2df4b5ce | critical |
| FIND-data-006 | t_cae884912113 | critical |
| FIND-data-007 | t_c5e8f53dc1e5 | critical |
| FIND-data-008 | t_1ef04121af84 | high |
| FIND-data-009 | t_2ccf46e4da19 | high |
| FIND-data-010 | t_1229cba1112b | high |
| FIND-data-011 | t_0a9ad57e4385 | high |
| FIND-data-012 | t_ead60d7097c2 | high |
| FIND-data-013 | t_c9cc8b4c74e3 | high |
